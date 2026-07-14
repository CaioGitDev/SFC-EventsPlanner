using Sfc.Domain.Organizations;
using Xunit;

namespace Sfc.Domain.Tests.Organizations;

public class OrganizationTests
{
    [Fact]
    public void Constructor_WithValidNameAndSlug_SetsProperties()
    {
        var organization = new Organization("SFC", "sfc");

        Assert.NotEqual(Guid.Empty, organization.Id);
        Assert.Equal("SFC", organization.Name);
        Assert.Equal("sfc", organization.Slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Organization(name!, "sfc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankSlug_Throws(string? slug)
    {
        Assert.Throws<ArgumentException>(() => new Organization("SFC", slug!));
    }

    [Fact]
    public void Constructor_TrimsNameAndSlug()
    {
        var organization = new Organization("  SFC  ", "  sfc  ");

        Assert.Equal("SFC", organization.Name);
        Assert.Equal("sfc", organization.Slug);
    }
}
