namespace Sfc.Infrastructure.Storage;

public interface IImageStorage
{
    /// <summary>Uploads the content and returns its public URL.</summary>
    Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
