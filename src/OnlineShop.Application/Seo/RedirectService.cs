using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Seo;

namespace OnlineShop.Application.Seo;

/// <summary>Old and new addresses as stored and compared: decoded, without host, fragment or trailing slash.</summary>
public static class RedirectPaths
{
    /// <summary>
    /// The path (with its query, if any) of an old address, or null when it cannot be one: not starting with "/", the
    /// site root, or inside the API or the Next.js assets. A full URL of the old site is accepted (its host is dropped).
    /// </summary>
    public static string? NormalizeFrom(string? input)
    {
        var path = PathOf(input);
        if (path is null || path == "/")
            return null;
        var upper = path.ToUpperInvariant();
        return upper.StartsWith("/API/", StringComparison.Ordinal) || upper == "/API" || upper.StartsWith("/_NEXT/", StringComparison.Ordinal) ? null : path;
    }

    /// <summary>The new address: a path on this site, or an absolute http(s) URL kept as given; null when neither.</summary>
    public static string? NormalizeTo(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !text.StartsWith('/'))
            return uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) ? uri.ToString() : null;
        return PathOf(text);
    }

    private static string? PathOf(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace))
            return null;
        if (!text.StartsWith('/') && Uri.TryCreate(text, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            text = absolute.PathAndQuery;
        if (!text.StartsWith('/') || text.StartsWith("//", StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal))
            return null;
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
            text = text[..hash];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return null;
        }
        var query = decoded.IndexOf('?', StringComparison.Ordinal);
        var (path, rest) = query >= 0 ? (decoded[..query], decoded[query..]) : (decoded, "");
        if (path.Length > 1)
            path = path.TrimEnd('/');
        var result = path + (rest == "?" ? "" : rest);
        return result.Length <= 500 ? result : null;
    }
}

/// <summary>
/// Redirects of old addresses (301/302) and removed ones (410), managed in the admin panel and imported from CSV.
/// One hop only: a target may not be another redirect's old address, and nothing points back to itself.
/// </summary>
public sealed class RedirectService(IApplicationDbContext db, TimeProvider time)
{
    public const int MaxImportLines = 5000;

    public async Task<IReadOnlyList<RedirectView>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.Redirects.AsNoTracking().OrderBy(r => r.FromPath).ToListAsync(cancellationToken)).Select(View).ToList();

    /// <summary>Every active rule, for the storefront (small: it is cached there).</summary>
    public async Task<IReadOnlyList<RedirectRule>> RulesAsync(CancellationToken cancellationToken = default) =>
        await db.Redirects.AsNoTracking().OrderBy(r => r.FromPath).Select(r => new RedirectRule(r.FromPath, r.ToPath, r.Kind)).ToListAsync(cancellationToken);

    public async Task<Result<RedirectView>> CreateAsync(SaveRedirectRequest request, CancellationToken cancellationToken = default)
    {
        var rule = await ValidateAsync(request, null, cancellationToken);
        if (!rule.IsSuccess)
            return rule.Error!;
        var row = new Redirect { FromPath = rule.Value.FromPath, ToPath = rule.Value.ToPath, Kind = rule.Value.Kind };
        db.Redirects.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return View(row);
    }

    public async Task<Result<RedirectView>> UpdateAsync(Guid id, SaveRedirectRequest request, CancellationToken cancellationToken = default)
    {
        var row = await db.Redirects.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null)
            return NotFound();
        var rule = await ValidateAsync(request, id, cancellationToken);
        if (!rule.IsSuccess)
            return rule.Error!;
        (row.FromPath, row.ToPath, row.Kind) = (rule.Value.FromPath, rule.Value.ToPath, rule.Value.Kind);
        await db.SaveChangesAsync(cancellationToken);
        return View(row);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Redirects.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null)
            return NotFound();
        db.Redirects.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Adds or updates (matched by old address) one redirect per line; lines with an error are reported and skipped,
    /// the others are saved.
    /// </summary>
    public async Task<Result<ImportRedirectsResult>> ImportAsync(ImportRedirectsRequest request, CancellationToken cancellationToken = default)
    {
        var lines = request.Text.Split('\n');
        if (lines.Length > MaxImportLines)
            return AppError.Validation(SeoErrors.ImportTooLarge, $"At most {MaxImportLines} lines at once.");

        var (added, updated) = (0, 0);
        var errors = new List<ImportLineError>();
        for (var i = 0; i < lines.Length; i++)
        {
            var cells = lines[i].Trim().TrimEnd('\r').Split(',', ';', '\t').Select(c => c.Trim().Trim('"')).ToArray();
            if (cells.All(c => c.Length == 0) || (i == 0 && !cells[0].StartsWith('/') && !cells[0].Contains("://", StringComparison.Ordinal)))
                continue; // blank line or header
            var kind = cells.Length > 2 && cells[2].Length > 0 ? KindOf(cells[2]) : RedirectKind.Permanent;
            if (kind is null)
            {
                errors.Add(new(i + 1, SeoErrors.TargetInvalid));
                continue;
            }
            var line = new SaveRedirectRequest(cells[0], cells.Length > 1 ? cells[1] : null, kind.Value);
            var from = RedirectPaths.NormalizeFrom(line.FromPath);
            var existing = from is null ? null : await db.Redirects.SingleOrDefaultAsync(r => r.FromPath == from, cancellationToken);
            var rule = await ValidateAsync(line, existing?.Id, cancellationToken);
            if (!rule.IsSuccess)
            {
                errors.Add(new(i + 1, rule.Error!.Code));
                continue;
            }
            if (existing is null)
            {
                db.Redirects.Add(new Redirect { FromPath = rule.Value.FromPath, ToPath = rule.Value.ToPath, Kind = rule.Value.Kind });
                added++;
            }
            else
            {
                (existing.ToPath, existing.Kind) = (rule.Value.ToPath, rule.Value.Kind);
                updated++;
            }
            // Saved line by line, so later lines see earlier ones (chains and duplicates are caught).
            await db.SaveChangesAsync(cancellationToken);
        }
        return new ImportRedirectsResult(added, updated, errors);
    }

    /// <summary>Counts a use of the redirect for this old address (unknown addresses are ignored).</summary>
    public async Task RecordHitAsync(string fromPath, CancellationToken cancellationToken = default)
    {
        var from = RedirectPaths.NormalizeFrom(fromPath);
        if (from is null)
            return;
        var now = time.GetUtcNow().UtcDateTime;
        await db.Redirects.Where(r => r.FromPath == from)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.HitCount, r => r.HitCount + 1).SetProperty(r => r.LastHitUtc, now), cancellationToken);
    }

    private async Task<Result<RedirectRule>> ValidateAsync(SaveRedirectRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var from = RedirectPaths.NormalizeFrom(request.FromPath);
        if (from is null)
            return AppError.Validation(SeoErrors.PathInvalid, "The old address must be a path such as /old-page.");
        string? to = null;
        if (request.Kind != RedirectKind.Gone)
        {
            to = RedirectPaths.NormalizeTo(request.ToPath);
            if (to is null)
                return AppError.Validation(SeoErrors.TargetInvalid, "The new address must be a path on this site or an http(s) URL.");
            if (to == from)
                return AppError.Validation(SeoErrors.RedirectLoop, "An address cannot redirect to itself.");
        }

        if (await db.Redirects.AnyAsync(r => r.FromPath == from && r.Id != id, cancellationToken))
            return AppError.Conflict(SeoErrors.PathTaken, $"{from} already redirects.");
        // One hop: the target is not an old address, and no redirect leads to this old address.
        if ((to is not null && await db.Redirects.AnyAsync(r => r.FromPath == to && r.Id != id, cancellationToken))
            || await db.Redirects.AnyAsync(r => r.ToPath == from && r.Id != id, cancellationToken))
            return AppError.Conflict(SeoErrors.RedirectChain, "Point the redirect straight at the final address.");
        return new RedirectRule(from, to, request.Kind);
    }

    private static RedirectKind? KindOf(string text) => text.Trim() switch
    {
        "301" => RedirectKind.Permanent,
        "308" => RedirectKind.Permanent,
        "302" => RedirectKind.Temporary,
        "307" => RedirectKind.Temporary,
        "410" => RedirectKind.Gone,
        _ => Enum.TryParse<RedirectKind>(text.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? kind : null,
    };

    private static RedirectView View(Redirect r) => new(r.Id, r.FromPath, r.ToPath, r.Kind, r.HitCount, r.LastHitUtc);

    private static AppError NotFound() => AppError.NotFound(SeoErrors.NotFound, "No such redirect.");
}
