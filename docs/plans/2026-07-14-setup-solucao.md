# Setup da Solução — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Initial solution setup per `prompts/00-setup-solucao.md` — .NET 9 solution (Domain/Infrastructure/Web + tests), EF Core + PostgreSQL with `Organization` seed and tenant query-filter mechanism, ASP.NET Identity (Admin/Editor roles + dev admin seed), Next.js portal placeholder, docker-compose (Postgres + MinIO), and a passing Testcontainers integration test. No business features.

**Architecture:** Modular monolith (ADR-001): `Sfc.Domain` (no dependencies) → `Sfc.Infrastructure` (EF Core, Npgsql, Identity stores) → `Sfc.Web` (Razor Pages + future public API). Portal is a separate Next.js app in `portal/`. Multi-tenancy is only `OrganizationId` + a global query filter convention (ADR-002).

**Tech Stack:** .NET 9 (TFM `net9.0`), EF Core 9, Npgsql 9, ASP.NET Identity, xUnit, Testcontainers.PostgreSql, WebApplicationFactory, Next.js (App Router) + TypeScript + Tailwind + shadcn/ui, PostgreSQL 17, MinIO.

## Global Constraints

- Code, entities, tests in **English**; user-visible strings in **pt-PT** (CLAUDE.md rule 4).
- `OrganizationId` on every domain entity + global query filter (ADR-002). `Organization` itself is the tenant root and is NOT scoped.
- No secrets in committed files. `appsettings.Development.json` is gitignored and holds local dev-only values (seed admin password). Docker-compose dev credentials are non-secret placeholders (`sfc`/`sfc`, `minioadmin`).
- No MediatR/CQRS/Redis/SignalR/Hangfire (ADR-001, docs/03).
- No business entities beyond `Organization`; no UI beyond placeholders (prompt acceptance criteria).
- Never push to `master` — branch `feature/setup-solucao`, PR with gates (docs/05).
- **Environment note:** local machine has .NET SDK 10.0.301 and runtimes 8/10 only. All projects target `net9.0` (docs mandate) with `<RollForward>LatestMajor</RollForward>` in `Directory.Build.props` so apps/tests run on the installed .NET 10 runtime locally. CI (`setup-dotnet` 9.0.x) runs natively on 9.
- Deterministic seed IDs: SFC organization `00000000-0000-0000-0000-000000000001`; seed timestamps fixed at `2026-07-14T00:00:00Z`.
- xUnit tests: domain tests without mocks; integration via WebApplicationFactory + Testcontainers (sfc-convencoes).

---

### Task 1: Branch + solution skeleton

**Files:**
- Create: `Directory.Build.props`, `Sfc.sln`, `src/Sfc.Domain/Sfc.Domain.csproj`, `src/Sfc.Infrastructure/Sfc.Infrastructure.csproj`, `src/Sfc.Web/Sfc.Web.csproj`, `tests/Sfc.Domain.Tests/Sfc.Domain.Tests.csproj`, `tests/Sfc.Web.Tests/Sfc.Web.Tests.csproj`, `src/Sfc.Web/Program.cs` (minimal), placeholder class files removed later.

**Interfaces:**
- Produces: solution layout consumed by all later tasks; project references Domain ← Infrastructure ← Web; test projects reference their targets.

- [ ] **Step 1: Create branch**

```bash
git checkout -b feature/setup-solucao
```

- [ ] **Step 2: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RollForward>LatestMajor</RollForward>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the five csproj files**

`src/Sfc.Domain/Sfc.Domain.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
```

`src/Sfc.Infrastructure/Sfc.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sfc.Domain\Sfc.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="9.0.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.*" />
  </ItemGroup>
</Project>
```

`src/Sfc.Web/Sfc.Web.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sfc.Infrastructure\Sfc.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

`tests/Sfc.Domain.Tests/Sfc.Domain.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Sfc.Domain\Sfc.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.9.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

`tests/Sfc.Web.Tests/Sfc.Web.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Sfc.Web\Sfc.Web.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.*" />
    <PackageReference Include="xunit" Version="2.9.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Minimal `src/Sfc.Web/Program.cs`** (placeholder so the solution builds; replaced in Task 4)

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "SFC EventsPlanner");
app.Run();

public partial class Program;
```

- [ ] **Step 5: Create solution and add projects**

```bash
dotnet new sln -n Sfc
dotnet sln add src/Sfc.Domain src/Sfc.Infrastructure src/Sfc.Web tests/Sfc.Domain.Tests tests/Sfc.Web.Tests
```

- [ ] **Step 6: Verify build**

Run: `dotnet build Sfc.sln --nologo`
Expected: Build succeeded, 0 warnings/errors (targeting packs for net9.0 restore via NuGet automatically).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add .NET solution skeleton with Domain, Infrastructure, Web and test projects"
```

---

### Task 2: Domain — `Organization` entity (TDD)

**Files:**
- Create: `src/Sfc.Domain/Common/IOrganizationScoped.cs`, `src/Sfc.Domain/Organizations/Organization.cs`
- Test: `tests/Sfc.Domain.Tests/Organizations/OrganizationTests.cs`

**Interfaces:**
- Produces: `Organization` with `Guid Id`, `string Name`, `string Slug`, `DateTime CreatedAt`, `DateTime UpdatedAt`; ctor `Organization(string name, string slug)` throwing `ArgumentException` on blank input. `IOrganizationScoped { Guid OrganizationId { get; } }` — the marker interface future tenant entities implement; the DbContext filter convention (Task 3) keys off it.

- [ ] **Step 1: Write the failing tests**

`tests/Sfc.Domain.Tests/Organizations/OrganizationTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Domain.Tests --nologo`
Expected: FAIL — compile error, `Organization` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Sfc.Domain/Common/IOrganizationScoped.cs`:
```csharp
namespace Sfc.Domain.Common;

/// <summary>
/// Marker for tenant-scoped entities (ADR-002). Every domain entity except
/// <see cref="Organizations.Organization"/> must implement this; the DbContext
/// applies a global query filter on <see cref="OrganizationId"/>.
/// </summary>
public interface IOrganizationScoped
{
    Guid OrganizationId { get; }
}
```

`src/Sfc.Domain/Organizations/Organization.cs`:
```csharp
namespace Sfc.Domain.Organizations;

public class Organization
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Organization()
    {
        Name = null!;
        Slug = null!;
    }

    public Organization(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));

        Id = Guid.NewGuid();
        Name = name.Trim();
        Slug = slug.Trim();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Domain.Tests --nologo`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Domain tests/Sfc.Domain.Tests
git commit -m "Add Organization entity and IOrganizationScoped marker"
```

---

### Task 3: Infrastructure — DbContext, Identity stores, query filter, seed, migration

**Files:**
- Create: `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`, `src/Sfc.Infrastructure/Persistence/SeedData.cs`, `src/Sfc.Infrastructure/Persistence/SfcDbContextFactory.cs`, `.config/dotnet-tools.json` (local tool manifest), `src/Sfc.Infrastructure/Migrations/*` (generated)

**Interfaces:**
- Consumes: `Organization`, `IOrganizationScoped` (Task 2).
- Produces: `SfcDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>` with `DbSet<Organization> Organizations`, instance property `Guid CurrentOrganizationId` (defaults to `SeedData.SfcOrganizationId`), and query-filter convention for `IOrganizationScoped`. `SeedData.SfcOrganizationId` and `SeedData.SeedTimestamp` constants. Migration `InitialCreate`.

- [ ] **Step 1: Write `SeedData.cs`**

```csharp
namespace Sfc.Infrastructure.Persistence;

public static class SeedData
{
    /// <summary>Fixed id of the single Fase 1 organization (ADR-002).</summary>
    public static readonly Guid SfcOrganizationId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Fixed timestamp so migrations stay deterministic.</summary>
    public static readonly DateTime SeedTimestamp = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
}
```

- [ ] **Step 2: Write `SfcDbContext.cs`**

```csharp
using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Common;
using Sfc.Domain.Organizations;

namespace Sfc.Infrastructure.Persistence;

public class SfcDbContext(DbContextOptions<SfcDbContext> options)
    : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    /// <summary>
    /// Tenant used by the global query filter (ADR-002). Fase 1 has exactly
    /// one organization, so this defaults to the SFC seed id.
    /// </summary>
    public Guid CurrentOrganizationId { get; set; } = SeedData.SfcOrganizationId;

    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Organization>(entity =>
        {
            entity.Property(o => o.Name).HasMaxLength(200).IsRequired();
            entity.Property(o => o.Slug).HasMaxLength(200).IsRequired();
            entity.HasIndex(o => o.Slug).IsUnique();
            entity.HasData(new
            {
                Id = SeedData.SfcOrganizationId,
                Name = "SFC",
                Slug = "sfc",
                CreatedAt = SeedData.SeedTimestamp,
                UpdatedAt = SeedData.SeedTimestamp,
            });
        });

        ApplyOrganizationQueryFilters(builder);
    }

    /// <summary>
    /// Applies the tenant global query filter to every entity implementing
    /// <see cref="IOrganizationScoped"/> (ADR-002). No entity implements it
    /// yet; the convention guarantees future entities are filtered from day one.
    /// </summary>
    private void ApplyOrganizationQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(IOrganizationScoped).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(parameter, nameof(IOrganizationScoped.OrganizationId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId))),
                parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
```

- [ ] **Step 3: Write design-time factory `SfcDbContextFactory.cs`** (lets `dotnet ef` run without the Web host)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sfc.Infrastructure.Persistence;

public class SfcDbContextFactory : IDesignTimeDbContextFactory<SfcDbContext>
{
    public SfcDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SfcDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=sfc_events;Username=sfc;Password=sfc")
            .Options;
        return new SfcDbContext(options);
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build Sfc.sln --nologo`
Expected: Build succeeded.

- [ ] **Step 5: Install dotnet-ef as local tool and generate migration**

```bash
dotnet new tool-manifest
dotnet tool install dotnet-ef --version "9.0.*"
dotnet ef migrations add InitialCreate --project src/Sfc.Infrastructure --startup-project src/Sfc.Web --output-dir Migrations
```

Expected: `Migrations/` folder created in `src/Sfc.Infrastructure` with Identity tables + `Organizations` table including the SFC seed row.

- [ ] **Step 6: Inspect generated migration** — confirm `Organizations` has `InsertData` for SFC and Identity tables (`AspNetUsers`, `AspNetRoles`, …) exist.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add SfcDbContext with Identity stores, tenant query filter and InitialCreate migration"
```

---

### Task 4: Web — wiring, Identity, seeder, placeholder page (pt-PT)

**Files:**
- Create: `src/Sfc.Web/appsettings.json`, `src/Sfc.Web/Startup/DatabaseSeeder.cs`, `src/Sfc.Web/Pages/Index.cshtml`, `src/Sfc.Web/Pages/Index.cshtml.cs`, `src/Sfc.Web/Pages/_ViewImports.cshtml`
- Create (local only, gitignored): `src/Sfc.Web/appsettings.Development.json`
- Modify: `src/Sfc.Web/Program.cs`

**Interfaces:**
- Consumes: `SfcDbContext`, `SeedData` (Task 3).
- Produces: running web app; `DatabaseSeeder.SeedAsync(IServiceProvider, IConfiguration)` — migrates DB, ensures roles `Admin`/`Editor`, creates admin user when `SeedAdmin:Email`/`SeedAdmin:Password` config present. Config key `ConnectionStrings:Default`. `public partial class Program;` for WebApplicationFactory (Task 6).

- [ ] **Step 1: Write `appsettings.json`** (dev-only local connection string; matches docker-compose — not a secret)

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=sfc_events;Username=sfc;Password=sfc"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 2: Write `DatabaseSeeder.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sfc.Infrastructure.Persistence;

namespace Sfc.Web.Startup;

public static class DatabaseSeeder
{
    public static readonly string[] Roles = ["Admin", "Editor"];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        await dbContext.Database.MigrateAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return; // Sem config de seed (ex.: produção) — não criar admin.

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var admin = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed admin user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(admin, "Admin");
    }
}
```

- [ ] **Step 3: Replace `Program.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<SfcDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<SfcDbContext>();

var app = builder.Build();

app.UseStaticFiles();
app.MapRazorPages();

await DatabaseSeeder.SeedAsync(app.Services, app.Configuration);

app.Run();

public partial class Program;
```

- [ ] **Step 4: Write the placeholder Razor page (pt-PT)**

`src/Sfc.Web/Pages/_ViewImports.cshtml`:
```cshtml
@namespace Sfc.Web.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

`src/Sfc.Web/Pages/Index.cshtml`:
```cshtml
@page
@model Sfc.Web.Pages.IndexModel
<!DOCTYPE html>
<html lang="pt-PT">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>SFC EventsPlanner — Backoffice</title>
</head>
<body>
    <main>
        <h1>SFC EventsPlanner</h1>
        <p>Backoffice em construção.</p>
    </main>
</body>
</html>
```

`src/Sfc.Web/Pages/Index.cshtml.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sfc.Web.Pages;

public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
```

- [ ] **Step 5: Create local (gitignored) `src/Sfc.Web/appsettings.Development.json`**

```json
{
  "SeedAdmin": {
    "Email": "admin@sfc.local",
    "Password": "Dev-Sfc-Admin-2026!"
  }
}
```

Verify it is ignored: `git status --short` must NOT list it.

- [ ] **Step 6: Verify build**

Run: `dotnet build Sfc.sln --nologo`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Wire EF Core, Identity roles and admin seeding in Sfc.Web with pt-PT placeholder page"
```

---

### Task 5: docker-compose (PostgreSQL + MinIO) + local run

**Files:**
- Create: `docker-compose.yml`

**Interfaces:**
- Produces: Postgres on `localhost:5432` (`sfc`/`sfc`, db `sfc_events`) matching `appsettings.json`; MinIO on `localhost:9000` (console `9001`). Volumes under `docker-volumes/` (gitignored).

- [ ] **Step 1: Write `docker-compose.yml`**

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: sfc
      POSTGRES_PASSWORD: sfc
      POSTGRES_DB: sfc_events
    ports:
      - "5432:5432"
    volumes:
      - ./docker-volumes/postgres:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U sfc -d sfc_events"]
      interval: 5s
      timeout: 3s
      retries: 10

  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - ./docker-volumes/minio:/data
```

- [ ] **Step 2: Start and verify**

Run: `docker compose up -d`, then `docker compose ps`
Expected: both services `running`, postgres healthy.

- [ ] **Step 3: Run the app against it**

Run: `dotnet run --project src/Sfc.Web` (background, then request `http://localhost:5000/` or the launched port)
Expected: HTTP 200 with the pt-PT placeholder; database `sfc_events` migrated; seeded admin present. Stop the app afterwards.

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml
git commit -m "Add docker-compose with PostgreSQL and MinIO for local dev"
```

---

### Task 6: Integration test (Testcontainers + WebApplicationFactory)

**Files:**
- Create: `tests/Sfc.Web.Tests/SfcWebApplicationFactory.cs`, `tests/Sfc.Web.Tests/StartupTests.cs`

**Interfaces:**
- Consumes: `public partial class Program` (Task 4), `SfcDbContext`, `SeedData` (Task 3), `DatabaseSeeder.Roles` (Task 4).

- [ ] **Step 1: Write the failing test**

`tests/Sfc.Web.Tests/SfcWebApplicationFactory.cs`:
```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sfc.Web.Tests;

public sealed class SfcWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("SeedAdmin:Email", "admin@test.local");
        builder.UseSetting("SeedAdmin:Password", "Test-Admin-2026!");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

`tests/Sfc.Web.Tests/StartupTests.cs`:
```csharp
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
```

Note: `CreateClient()`/`factory.Services` boot the host, which runs `DatabaseSeeder.SeedAsync` (migrations + roles + admin) against the container.

- [ ] **Step 2: Run to verify current state**

Run: `dotnet test tests/Sfc.Web.Tests --nologo`
Expected: PASS if Tasks 3–4 correct (this validates the whole pipeline); any failure is investigated with superpowers:systematic-debugging — the test is the spec.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test Sfc.sln --nologo`
Expected: all green (domain + integration).

- [ ] **Step 4: Commit**

```bash
git add tests/Sfc.Web.Tests
git commit -m "Add startup integration tests with Testcontainers PostgreSQL"
```

---

### Task 7: Portal Next.js (placeholder)

**Files:**
- Create: `portal/` via create-next-app; modify `portal/app/page.tsx`, `portal/app/layout.tsx`; shadcn/ui init (`portal/components.json`, `portal/lib/utils.ts`).

- [ ] **Step 1: Scaffold**

```bash
npx --yes create-next-app@latest portal --typescript --tailwind --eslint --app --no-src-dir --import-alias "@/*" --use-npm --yes
```

(create-next-app skips `git init` inside an existing repo; verify no `portal/.git` was created — remove it if so.)

- [ ] **Step 2: Init shadcn/ui**

```bash
cd portal
npx --yes shadcn@latest init --yes --base-color neutral
```

If the non-interactive init fails, create `portal/components.json` manually with the documented default schema and add `lib/utils.ts` (`cn` helper with `clsx` + `tailwind-merge`) and install `class-variance-authority clsx tailwind-merge lucide-react tw-animate-css`.

- [ ] **Step 3: Replace home with pt-PT placeholder**

`portal/app/layout.tsx` — set metadata + lang:
```tsx
import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "SFC EventsPlanner",
  description: "Eventos de desportos de combate — SFC",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="pt-PT">
      <body className="antialiased">{children}</body>
    </html>
  );
}
```

(Keep the font setup create-next-app generated if present — merge, don't delete.)

`portal/app/page.tsx`:
```tsx
export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4">
      <h1 className="text-4xl font-bold tracking-tight">SFC EventsPlanner</h1>
      <p className="text-muted-foreground">Portal em construção.</p>
    </main>
  );
}
```

- [ ] **Step 4: Verify lint + build**

Run (in `portal/`): `npm run lint` and `npm run build`
Expected: both succeed.

- [ ] **Step 5: Commit**

```bash
git add portal
git commit -m "Add Next.js portal with pt-PT placeholder home"
```

---

### Task 8: README, verification, gates, PR

**Files:**
- Modify: `README.md` (quickstart section)

- [ ] **Step 1: Add quickstart to README** — how to run: `docker compose up -d`; create `src/Sfc.Web/appsettings.Development.json` with `SeedAdmin` (documented example, file gitignored); `dotnet run --project src/Sfc.Web`; `cd portal && npm install && npm run dev`; `dotnet test`. Note about local SDK 10 + RollForward.

- [ ] **Step 2: Full acceptance verification (superpowers:verification-before-completion)**

Run and capture output:
- `dotnet test Sfc.sln --nologo` → all green
- `docker compose up -d` + `docker compose ps` → services running
- `cd portal; npm run dev` (briefly) → responds on `localhost:3000`

- [ ] **Step 3: Gate 3 — run `guardiao-ambito` agent** on the full diff. Expected: no scope creep (setup only, no business entities). Gate 4 (`revisor-dominio`) not applicable: no combat-sports rules touched.

- [ ] **Step 4: Gate 5 — run `/security-review`** on the branch. Fix anything Critical.

- [ ] **Step 5: Commit README, push branch, open PR**

```bash
git add README.md docs/plans/2026-07-14-setup-solucao.md
git commit -m "Add development quickstart to README"
git push -u origin feature/setup-solucao
gh pr create --fill
```

Expected: PR created against `master`; CI jobs `backend`, `portal`, `secrets-scan` green.

---

## Self-review notes

- **Spec coverage:** prompt items 1–7 map to Tasks 1, 2–3 (EF+Organization+filter), 3–4+6 (Identity+seed), 7 (portal), 5 (compose), CI already exists and activates automatically once `Sfc.sln`/`portal/package.json` land (no changes needed), 6 (integration test). Acceptance criteria verified in Task 8.
- **Secrets:** admin password only in gitignored `appsettings.Development.json` and in-memory test settings; compose/appsettings dev credentials are non-secret placeholders.
- **Type consistency:** `SeedData.SfcOrganizationId`, `ConnectionStrings:Default`, `SeedAdmin:Email/Password`, `public partial class Program;` used consistently across Tasks 3, 4, 6.
