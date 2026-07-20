using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Events;
using Sfc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class EventServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static EventInput Input(string name, string? slug = null)
        => new(name, "Descrição", new DateTime(2026, 11, 20, 20, 0, 0), "Pavilhão", "Lisboa",
            null, null, slug);

    [Fact]
    public async Task CreateAsync_GeneratesSlugAndDefaultsToDraft()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();

        var evt = await service.CreateAsync(Input("Gala de Verão"), null, null);

        Assert.Equal("gala-de-verao", evt.Slug);
        Assert.Equal(EventStatus.Draft, evt.Status);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_GetsNumericSuffix()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();

        await service.CreateAsync(Input("Evento Duplicado"), null, null);
        var second = await service.CreateAsync(Input("Evento Duplicado"), null, null);

        Assert.Equal("evento-duplicado-2", second.Slug);
    }

    [Fact]
    public async Task CreateAsync_WithBannerAndPoster_StoresWebps()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        using var banner = await CreatePngAsync(2400, 1000);
        using var poster = await CreatePngAsync(1400, 2000);

        var evt = await service.CreateAsync(Input("Com Imagens"), banner, poster);

        Assert.Equal($"https://media.test.local/events/{evt.Id}-banner.webp", evt.BannerUrl);
        Assert.Equal($"https://media.test.local/events/{evt.Id}-poster.webp", evt.PosterUrl);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"events/{evt.Id}-banner.webp"));
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"events/{evt.Id}-poster.webp"));
    }

    [Fact]
    public async Task Transitions_FollowTheStateMachine()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await service.CreateAsync(Input("Máquina de Estados"), null, null);

        Assert.Equal(EventTransitionResult.InvalidTransition, await service.CompleteAsync(evt.Id));
        Assert.Equal(EventTransitionResult.Success, await service.PublishAsync(evt.Id));
        Assert.Equal(EventTransitionResult.InvalidTransition, await service.PublishAsync(evt.Id));
        Assert.Equal(EventTransitionResult.Success, await service.CompleteAsync(evt.Id));
        Assert.Equal(EventTransitionResult.NotFound, await service.PublishAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_PublishedEvent_ReturnsNotDeletable()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await service.CreateAsync(Input("Não Apagável"), null, null);
        await service.PublishAsync(evt.Id);

        Assert.Equal(EventDeleteResult.NotDeletable, await service.DeleteAsync(evt.Id));
    }

    [Fact]
    public async Task DeleteAsync_DraftWithImages_DeletesRowAndBlobs()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        using var banner = await CreatePngAsync(1000, 400);
        var evt = await service.CreateAsync(Input("Apagável"), banner, null);

        var result = await service.DeleteAsync(evt.Id);

        Assert.Equal(EventDeleteResult.Deleted, result);
        Assert.False(factory.ImageStorage.Saved.ContainsKey($"events/{evt.Id}-banner.webp"));
        Assert.Null(await service.GetWithCardAsync(evt.Id));
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameAndStatus()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var published = await service.CreateAsync(Input("Pesquisável Publicado"), null, null);
        await service.PublishAsync(published.Id);
        await service.CreateAsync(Input("Pesquisável Rascunho"), null, null);

        var byName = await service.SearchAsync("pesquisável", null);
        var byStatus = await service.SearchAsync("Pesquisável", EventStatus.Published);

        Assert.True(byName.Count >= 2);
        Assert.Contains(byStatus, e => e.Id == published.Id);
        Assert.DoesNotContain(byStatus, e => e.Name == "Pesquisável Rascunho");
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
