using Sfc.Infrastructure.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Images;

public class ImageProcessorTests
{
    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task ToWebpAsync_ConvertsToWebp()
    {
        using var png = await CreatePngAsync(100, 50);

        using var result = await ImageProcessor.ToWebpAsync(png, 800);

        var format = await Image.DetectFormatAsync(result);
        Assert.Equal("Webp", format.Name, ignoreCase: true);
    }

    [Fact]
    public async Task ToWebpAsync_ResizesDownToMaxDimensionKeepingAspectRatio()
    {
        using var png = await CreatePngAsync(1600, 800);

        using var result = await ImageProcessor.ToWebpAsync(png, 800);

        using var image = await Image.LoadAsync(result);
        Assert.Equal(800, image.Width);
        Assert.Equal(400, image.Height);
    }

    [Fact]
    public async Task ToWebpAsync_DoesNotUpscaleSmallImages()
    {
        using var png = await CreatePngAsync(200, 100);

        using var result = await ImageProcessor.ToWebpAsync(png, 800);

        using var image = await Image.LoadAsync(result);
        Assert.Equal(200, image.Width);
    }

    [Fact]
    public async Task ToWebpAsync_WithNonImageContent_ThrowsInvalidImageException()
    {
        using var garbage = new MemoryStream("this is not an image"u8.ToArray());

        await Assert.ThrowsAsync<InvalidImageException>(() => ImageProcessor.ToWebpAsync(garbage, 800));
    }
}
