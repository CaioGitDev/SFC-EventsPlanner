using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests;

public class StartupTests(SfcWebApplicationFactory factory) : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task HomePage_ReturnsSuccess()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Database_HasSfcOrganizationSeeded()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var organization = await dbContext.Organizations
            .SingleAsync(o => o.Id == SeedData.SfcOrganizationId);

        Assert.Equal("SFC", organization.Name);
        Assert.Equal("sfc", organization.Slug);
    }

    [Fact]
    public async Task Database_HasAdminAndEditorRoles()
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Assert.True(await roleManager.RoleExistsAsync("Admin"));
        Assert.True(await roleManager.RoleExistsAsync("Editor"));
    }

    [Fact]
    public async Task Database_HasSeededAdminUserWithAdminRole()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        var admin = await userManager.FindByEmailAsync("admin@test.local");

        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin, "Admin"));
    }
}
