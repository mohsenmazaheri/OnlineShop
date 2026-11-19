using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Media;
using OnlineShopping.Infrastructure.Media;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>An <see cref="IFileStorage"/> kept in memory, so tests can see what was stored and deleted.</summary>
internal sealed class InMemoryFileStorage : IFileStorage
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        Files[path] = content;
        return Task.CompletedTask;
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[path] = copy.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        foreach (var key in Files.Keys.Where(k => k.StartsWith(folder + "/", StringComparison.Ordinal)))
            Files.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <summary>A media service over this store with the real image processor.</summary>
    public MediaService Media(MediaOptions? options = null) => new(new SkiaImageProcessor(), this, Options.Create(options ?? new MediaOptions()));
}
