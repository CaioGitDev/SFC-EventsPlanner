using Amazon.S3;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;
using Sfc.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<SfcDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<SfcDbContext>();

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var storage = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    var config = new AmazonS3Config
    {
        ServiceURL = storage.Endpoint,
        ForcePathStyle = true, // required by MinIO
        AuthenticationRegion = "us-east-1",
    };
    return new AmazonS3Client(storage.AccessKey, storage.SecretKey, config);
});
builder.Services.AddSingleton<IImageStorage, S3ImageStorage>();

var app = builder.Build();

app.UseStaticFiles();
app.MapRazorPages();

await DatabaseSeeder.SeedAsync(app.Services, app.Configuration);

app.Run();

public partial class Program;
