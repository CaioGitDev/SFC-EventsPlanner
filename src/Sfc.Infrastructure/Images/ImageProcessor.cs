using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Sfc.Infrastructure.Images;

public static class ImageProcessor
{
    private const int WebpQuality = 80;

    /// <summary>
    /// Validates the stream is a real image, resizes so the longest side is at
    /// most <paramref name="maxDimension"/> (never upscales), and re-encodes as WebP.
    /// </summary>
    public static async Task<MemoryStream> ToWebpAsync(
        Stream input, int maxDimension, CancellationToken ct = default)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(input, ct);
        }
        catch (UnknownImageFormatException ex)
        {
            throw new InvalidImageException("File is not a valid image.", ex);
        }
        catch (InvalidImageContentException ex)
        {
            throw new InvalidImageException("File is not a valid image.", ex);
        }

        using (image)
        {
            if (image.Width > maxDimension || image.Height > maxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxDimension, maxDimension),
                }));
            }

            var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder { Quality = WebpQuality }, ct);
            output.Position = 0;
            return output;
        }
    }
}
