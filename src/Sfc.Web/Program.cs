var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "SFC EventsPlanner");
app.Run();

public partial class Program;
