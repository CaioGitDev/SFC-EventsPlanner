using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Tests.Fakes;

public sealed class FakeImageStorage : IImageStorage
{
    public Dictionary<string, byte[]> Saved { get; } = [];

    public async Task<string> SaveAsync(
        Stream content, string key, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Saved[key] = buffer.ToArray();
        return $"https://media.test.local/{key}";
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Saved.Remove(key);
        return Task.CompletedTask;
    }
}
