using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Media;

namespace OnlineShopping.Infrastructure.Media;

/// <summary>Section <c>Storage</c>: where uploaded files are kept.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploads, absolute or relative to the app (Docker: a volume at /app/App_Data/media).</summary>
    public string LocalPath { get; set; } = "App_Data/media";
}

/// <summary>
/// Files on the server's disk. Every path is resolved inside the configured folder and rejected otherwise, so a crafted
/// key can never read or delete anything else.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment environment) : IFileStorage
{
    private string Root => Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalPath));

    public async Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content, cancellationToken);
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        return Task.FromResult<Stream?>(File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true) : null);
    }

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        var full = Resolve(folder);
        if (full != Root && Directory.Exists(full))
            Directory.Delete(full, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>The absolute path of a relative storage path, refusing anything that would leave the root folder.</summary>
    internal string Resolve(string path)
    {
        var root = Root;
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != root)
            throw new ArgumentException("The path leaves the storage folder.", nameof(path));
        return full;
    }
}
