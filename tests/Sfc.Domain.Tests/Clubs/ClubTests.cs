using Sfc.Domain.Clubs;
using Xunit;

namespace Sfc.Domain.Tests.Clubs;

public class ClubTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var club = new Club(OrgId, "Team Scorpion", "Lisboa", "Portugal", "geral@scorpion.pt", "+351 912 345 678");

        Assert.NotEqual(Guid.Empty, club.Id);
        Assert.Equal(OrgId, club.OrganizationId);
        Assert.Equal("Team Scorpion", club.Name);
        Assert.Equal("Lisboa", club.City);
        Assert.Equal("Portugal", club.Country);
        Assert.Equal("geral@scorpion.pt", club.ContactEmail);
        Assert.Equal("+351 912 345 678", club.ContactPhone);
        Assert.Empty(club.Coaches);
        Assert.Null(club.LogoUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Club(OrgId, name!));
    }

    [Fact]
    public void Constructor_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Club(Guid.Empty, "Team Scorpion"));
    }

    [Fact]
    public void Constructor_TrimsAndNormalizesOptionalFieldsToNull()
    {
        var club = new Club(OrgId, "  Team Scorpion  ", "  ", "", null, " +351 1 ");

        Assert.Equal("Team Scorpion", club.Name);
        Assert.Null(club.City);
        Assert.Null(club.Country);
        Assert.Null(club.ContactEmail);
        Assert.Equal("+351 1", club.ContactPhone);
    }

    [Fact]
    public void Update_ChangesDetails()
    {
        var club = new Club(OrgId, "Team Scorpion");

        club.Update("Scorpion Gym", "Porto", "Portugal", null, null);

        Assert.Equal("Scorpion Gym", club.Name);
        Assert.Equal("Porto", club.City);
    }

    [Fact]
    public void SetCoaches_ReplacesList()
    {
        var club = new Club(OrgId, "Team Scorpion");
        club.SetCoaches([new Coach("Mestre Rui", "rui@scorpion.pt")]);

        club.SetCoaches([new Coach("Kru Ana")]);

        var coach = Assert.Single(club.Coaches);
        Assert.Equal("Kru Ana", coach.Name);
        Assert.Null(coach.Contact);
    }

    [Fact]
    public void SetLogo_SetsUrl()
    {
        var club = new Club(OrgId, "Team Scorpion");

        club.SetLogo("https://media.local/clubs/x.webp");

        Assert.Equal("https://media.local/clubs/x.webp", club.LogoUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Coach_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Coach(name!));
    }
}
