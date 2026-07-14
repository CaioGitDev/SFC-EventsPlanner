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
