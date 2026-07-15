using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Infrastructure.Storage;
using Sfc.Web.Tests.Fakes;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sfc.Web.Tests;

public sealed class SfcWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public FakeImageStorage ImageStorage { get; } = new();

    public Task InitializeAsync() => _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("SeedAdmin:Email", "admin@test.local");
        builder.UseSetting("SeedAdmin:Password", "Test-Admin-2026!");
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IImageStorage>(ImageStorage));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
