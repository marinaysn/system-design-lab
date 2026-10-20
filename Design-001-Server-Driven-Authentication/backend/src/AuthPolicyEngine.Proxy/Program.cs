var builder = WebApplication.CreateBuilder(args);

// Routes, clusters, load balancing, affinity and health checks all come from configuration
// (appsettings.json, section "ReverseProxy") and reload without a restart.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();
app.Run();

public partial class Program { }
