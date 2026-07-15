using Sfc.Domain.Events;
using Xunit;

namespace Sfc.Domain.Tests.Events;

public class EventTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Event CreateEvent(string name = "SFC 12", string slug = "sfc-12")
        => new(OrgId, name, new DateTime(2026, 9, 12, 20, 0, 0), slug,
            description: "Gala anual", venue: "Pavilhão Municipal", city: "Lisboa");

    [Fact]
    public void Constructor_WithValidData_SetsPropertiesAndDefaults()
    {
        var evt = CreateEvent();

        Assert.NotEqual(Guid.Empty, evt.Id);
        Assert.Equal(OrgId, evt.OrganizationId);
        Assert.Equal("SFC 12", evt.Name);
        Assert.Equal("sfc-12", evt.Slug);
        Assert.Equal(EventStatus.Draft, evt.Status);
        Assert.Null(evt.PublishedAt);
        Assert.Empty(evt.Fights);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            new Event(OrgId, name!, new DateTime(2026, 9, 12), "slug"));
    }

    [Fact]
    public void Constructor_WithDefaultDate_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Event(OrgId, "SFC 12", default, "sfc-12"));
    }

    [Fact]
    public void Constructor_WithNonCanonicalSlug_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Event(OrgId, "SFC 12", new DateTime(2026, 9, 12), "SFC 12"));
    }

    [Fact]
    public void Publish_FromDraft_SetsPublishedAtOnce()
    {
        var evt = CreateEvent();

        evt.Publish();
        var firstPublishedAt = evt.PublishedAt;
        evt.Unpublish();
        evt.Publish();

        Assert.Equal(EventStatus.Published, evt.Status);
        Assert.Equal(firstPublishedAt, evt.PublishedAt);
    }

    [Fact]
    public void Publish_WhenAlreadyPublished_Throws()
    {
        var evt = CreateEvent();
        evt.Publish();

        Assert.Throws<InvalidOperationException>(evt.Publish);
    }

    [Fact]
    public void Unpublish_FromDraft_Throws()
    {
        Assert.Throws<InvalidOperationException>(CreateEvent().Unpublish);
    }

    [Fact]
    public void Complete_FromPublished_Succeeds()
    {
        var evt = CreateEvent();
        evt.Publish();

        evt.Complete();

        Assert.Equal(EventStatus.Completed, evt.Status);
    }

    [Fact]
    public void Complete_FromDraft_Throws()
    {
        Assert.Throws<InvalidOperationException>(CreateEvent().Complete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_FromDraftOrPublished_Succeeds(bool publishFirst)
    {
        var evt = CreateEvent();
        if (publishFirst)
            evt.Publish();

        evt.Cancel();

        Assert.Equal(EventStatus.Cancelled, evt.Status);
    }

    [Fact]
    public void Cancel_FromCompleted_Throws()
    {
        var evt = CreateEvent();
        evt.Publish();
        evt.Complete();

        Assert.Throws<InvalidOperationException>(evt.Cancel);
    }

    [Fact]
    public void UpdateSlug_BeforeFirstPublication_Changes()
    {
        var evt = CreateEvent();

        evt.UpdateSlug("sfc-12-lisboa");

        Assert.Equal("sfc-12-lisboa", evt.Slug);
    }

    [Fact]
    public void UpdateSlug_AfterFirstPublication_ThrowsEvenIfUnpublished()
    {
        var evt = CreateEvent();
        evt.Publish();
        evt.Unpublish();

        Assert.Throws<InvalidOperationException>(() => evt.UpdateSlug("outro-slug"));
    }

    [Fact]
    public void Update_ChangesEditableFieldsOnly()
    {
        var evt = CreateEvent();

        evt.Update("SFC 12 — Noite de Campeões", "Nova descrição", new DateTime(2026, 9, 13, 21, 0, 0),
            "Altice Arena", "Lisboa", "https://tickets.example/sfc12", "https://youtube.com/watch?v=x");

        Assert.Equal("SFC 12 — Noite de Campeões", evt.Name);
        Assert.Equal(new DateTime(2026, 9, 13, 21, 0, 0), evt.Date);
        Assert.Equal("https://tickets.example/sfc12", evt.TicketsUrl);
        Assert.Equal("sfc-12", evt.Slug);
        Assert.Equal(EventStatus.Draft, evt.Status);
    }

    [Fact]
    public void SetBannerAndPoster_SetUrls()
    {
        var evt = CreateEvent();

        evt.SetBanner("https://media.local/events/x-banner.webp");
        evt.SetPoster("https://media.local/events/x-poster.webp");

        Assert.Equal("https://media.local/events/x-banner.webp", evt.BannerUrl);
        Assert.Equal("https://media.local/events/x-poster.webp", evt.PosterUrl);
    }
}
