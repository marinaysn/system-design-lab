using System.Text.Json.Serialization;
using AuthPolicyEngine.Api.Accounts;
using AuthPolicyEngine.Api.Flows;
using AuthPolicyEngine.Api.Policy;
using AuthPolicyEngine.Api.Strategies;

var builder = WebApplication.CreateBuilder(args);

// appsettings.json is loaded with reloadOnChange, so AuthPolicy edits apply without a restart.
builder.Services.Configure<AuthPolicyOptions>(builder.Configuration.GetSection(AuthPolicyOptions.SectionName));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountRepository, InMemoryAccountRepository>();
builder.Services.AddSingleton<IAuthFlowStore, InMemoryAuthFlowStore>();
builder.Services.AddSingleton<IAuthenticationStrategy, PasswordStrategy>();
builder.Services.AddSingleton<IAuthenticationStrategy, EmailOtpStrategy>();
builder.Services.AddSingleton<IAuthenticationStrategy, TotpStrategy>();
builder.Services.AddSingleton<IAuthenticationPolicyEngine, AuthenticationPolicyEngine>();

var app = builder.Build();

// Development-only diagnostic: reports which backend instance handled the request.
// The id is an operator-chosen label (Instance:Id), never user data or a secret.
if (app.Environment.IsDevelopment())
{
    var instanceId = app.Configuration["Instance:Id"] ?? "instance-local";
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Instance-Id"] = instanceId;
            return Task.CompletedTask;
        });
        app.Logger.LogInformation("Instance {InstanceId} handling {Method} {Path}",
            instanceId, context.Request.Method, context.Request.Path);
        await next();
    });
}

// Used by the reverse proxy's active health checks.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapControllers();
app.Run();

public partial class Program { }
