using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Sfc.Infrastructure.Storage;

/// <summary>
/// S3-compatible storage: MinIO in dev, Cloudflare R2 in production.
/// </summary>
public sealed class S3ImageStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IImageStorage
{
    private readonly StorageOptions _options = options.Value;
    private bool _bucketVerified;

    public async Task<string> SaveAsync(
        Stream content, string key, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(ct);

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        }, ct);

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => s3.DeleteObjectAsync(_options.Bucket, key, ct);

    private async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        if (_bucketVerified)
            return;

        if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, _options.Bucket))
            await s3.PutBucketAsync(_options.Bucket, ct);

        _bucketVerified = true;
    }
}
