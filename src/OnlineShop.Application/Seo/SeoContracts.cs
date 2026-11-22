using FluentValidation;
using OnlineShop.Domain.Seo;

namespace OnlineShop.Application.Seo;

/// <summary>Error codes of synonyms and redirects (translated by the web app under <c>admin.seoErrors</c>).</summary>
public static class SeoErrors
{
    public const string NotFound = "seo.not_found";
    public const string LocaleInvalid = "seo.locale_invalid";
    public const string TooFewTerms = "seo.too_few_terms";
    public const string TooManyTerms = "seo.too_many_terms";
    public const string TermTooLong = "seo.term_too_long";
    public const string TermInOtherGroup = "seo.term_in_other_group";
    public const string PathInvalid = "seo.path_invalid";
    public const string TargetInvalid = "seo.target_invalid";
    public const string PathTaken = "seo.path_taken";
    public const string RedirectLoop = "seo.redirect_loop";
    public const string RedirectChain = "seo.redirect_chain";
    public const string ImportTooLarge = "seo.import_too_large";
}

public sealed record SynonymView(Guid Id, string Locale, IReadOnlyList<string> Terms);

/// <summary>Words that mean the same in one language (at least two).</summary>
public sealed record SaveSynonymRequest(string Locale, IReadOnlyList<string> Terms);

public sealed record SynonymList(IReadOnlyList<string> Locales, IReadOnlyList<SynonymView> Groups);

public sealed record RedirectView(Guid Id, string FromPath, string? ToPath, RedirectKind Kind, long HitCount, DateTime? LastHitUtc);

/// <summary>An old address and where it goes now (<see cref="ToPath"/> is ignored for <see cref="RedirectKind.Gone"/>).</summary>
public sealed record SaveRedirectRequest(string FromPath, string? ToPath, RedirectKind Kind);

/// <summary>Lines of "old,new" or "old,new,301|302|410" (a header line and blank lines are skipped).</summary>
public sealed record ImportRedirectsRequest(string Text);

public sealed record ImportLineError(int Line, string Code);

public sealed record ImportRedirectsResult(int Added, int Updated, IReadOnlyList<ImportLineError> Errors);

/// <summary>What the storefront needs to answer an old address.</summary>
public sealed record RedirectRule(string FromPath, string? ToPath, RedirectKind Kind);

/// <summary>The storefront reports a used redirect.</summary>
public sealed record RedirectHitRequest(string FromPath);

public sealed class SaveSynonymRequestValidator : AbstractValidator<SaveSynonymRequest>
{
    public SaveSynonymRequestValidator()
    {
        RuleFor(r => r.Locale).NotEmpty().WithErrorCode(SeoErrors.LocaleInvalid).MaximumLength(8).WithErrorCode(SeoErrors.LocaleInvalid);
        RuleFor(r => r.Terms).Must(t => t.Count <= SynonymService.MaxTerms).WithErrorCode(SeoErrors.TooManyTerms);
    }
}

public sealed class SaveRedirectRequestValidator : AbstractValidator<SaveRedirectRequest>
{
    public SaveRedirectRequestValidator()
    {
        RuleFor(r => r.FromPath).NotEmpty().WithErrorCode(SeoErrors.PathInvalid).MaximumLength(500).WithErrorCode(SeoErrors.PathInvalid);
        RuleFor(r => r.ToPath).MaximumLength(2048).WithErrorCode(SeoErrors.TargetInvalid);
        RuleFor(r => r.Kind).IsInEnum().WithErrorCode(SeoErrors.TargetInvalid);
    }
}
