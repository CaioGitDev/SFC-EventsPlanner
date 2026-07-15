namespace Sfc.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; set; } = "";
    public string Bucket { get; set; } = "sfc-media";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    /// <summary>Base URL public clients use to reach objects (MinIO dev: endpoint + bucket).</summary>
    public string PublicBaseUrl { get; set; } = "";
}
