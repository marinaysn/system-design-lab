using System.Net;
using System.Net.Http.Json;
using AuthPolicyEngine.Api.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace AuthPolicyEngine.Tests;

/// <summary>
/// Runs the real proxy configuration (proxy.appsettings.json) in-process in front of three
/// independent API instances, each with its own in-memory flow store.
/// </summary>
public sealed class ProxyFixture : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _instances = new();
    private readonly WebApplication _proxy;
    public HttpClient Client { get; }

    private sealed class RoutingHandler(Dictionary<string, HttpMessageHandler> byHost) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            new HttpMessageInvoker(byHost[request.RequestUri!.Host], disposeHandler: false).SendAsync(request, ct);
    }

    private sealed class TestForwarderClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
    }

    public ProxyFixture(bool affinity)
    {
        var handlers = new Dictionary<string, HttpMessageHandler>();
        for (var i = 1; i <= 3; i++)
        {
            var id = $"instance-{i}";
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Instance:Id"] = id })));
            _instances.Add(factory);
            handlers[$"instance{i}"] = factory.Server.CreateHandler();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "proxy.appsettings.json"), optional: false);
        const string c = "ReverseProxy:Clusters:authcluster:";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [c + "SessionAffinity:Enabled"] = affinity.ToString(),
            [c + "HealthCheck:Active:Enabled"] = "false",
            [c + "HealthCheck:Passive:Enabled"] = "false",
            [c + "Destinations:instance1:Address"] = "http://instance1/",
            [c + "Destinations:instance2:Address"] = "http://instance2/",
            [c + "Destinations:instance3:Address"] = "http://instance3/",
        });
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(new TestForwarderClientFactory(new RoutingHandler(handlers)));
        _proxy = builder.Build();
        _proxy.MapReverseProxy();
        _proxy.Start();
        Client = _proxy.GetTestClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        _proxy.StopAsync().GetAwaiter().GetResult();
        foreach (var i in _instances) i.Dispose();
    }
}

public class ProxyTests
{
    private static IConfiguration ProxyConfig() =>
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "proxy.appsettings.json")).Build();

    private static string? Instance(HttpResponseMessage r) =>
        r.Headers.TryGetValues("X-Instance-Id", out var v) ? v.FirstOrDefault() : null;

    private static async Task<(HttpResponseMessage Identify, HttpResponseMessage Select, string Flow)> IdentifyThenSelect(HttpClient client)
    {
        var identify = await client.PostAsJsonAsync("/api/auth/identify", new { identifier = "account.a@example.test" });
        var body = (await identify.Content.ReadFromJsonAsync<ChallengeResponse>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }))!;

        var select = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/flows/{body.FlowId}/select")
        {
            Content = JsonContent.Create(new { authenticationType = "EmailOtp" })
        };
        // Behave like a browser: replay only the routing cookie, if the proxy issued one.
        if (identify.Headers.TryGetValues("Set-Cookie", out var cookies))
            select.Headers.Add("Cookie", cookies.First().Split(';')[0]);
        return (identify, await client.SendAsync(select), body.FlowId!);
    }

    [Fact]
    public void ProxyConfig_DefinesThreeHttpsDestinationsRoundRobinAndHealthChecks()
    {
        var cluster = ProxyConfig().GetSection("ReverseProxy:Clusters:authcluster");
        Assert.Equal("RoundRobin", cluster["LoadBalancingPolicy"]);
        var addresses = cluster.GetSection("Destinations").GetChildren().Select(d => d["Address"]!).OrderBy(a => a).ToArray();
        Assert.Equal(["https://localhost:5101/", "https://localhost:5102/", "https://localhost:5103/"], addresses);
        Assert.Equal("true", cluster["HealthCheck:Active:Enabled"], ignoreCase: true);
        Assert.Equal("/health", cluster["HealthCheck:Active:Path"]);
    }

    [Fact]
    public void ProxyConfig_AffinityCookieIsSecureHttpOnlyAndRedistributes()
    {
        var a = ProxyConfig().GetSection("ReverseProxy:Clusters:authcluster:SessionAffinity");
        Assert.Equal("Cookie", a["Policy"]);
        Assert.Equal("Redistribute", a["FailurePolicy"]);
        Assert.Equal("true", a["Cookie:HttpOnly"], ignoreCase: true);
        Assert.Equal("Always", a["Cookie:SecurePolicy"]);
        Assert.Equal("Lax", a["Cookie:SameSite"]);
    }

    [Fact]
    public async Task WithoutAffinity_RoundRobinSpreadsRequests_AndFlowIsUnknownOnAnotherInstance()
    {
        using var fx = new ProxyFixture(affinity: false);
        var seen = new HashSet<string?>();
        foreach (var _ in Enumerable.Range(0, 3))
        {
            var (identify, select, _) = await IdentifyThenSelect(fx.Client);
            seen.Add(Instance(identify));
            Assert.NotEqual(Instance(identify), Instance(select));
            Assert.Equal(HttpStatusCode.NotFound, select.StatusCode);
            Assert.False(identify.Headers.Contains("Set-Cookie"));
        }
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public async Task WithAffinity_FlowStaysOnTheSameInstance()
    {
        using var fx = new ProxyFixture(affinity: true);
        var seen = new HashSet<string?>();
        foreach (var _ in Enumerable.Range(0, 6))
        {
            var (identify, select, _) = await IdentifyThenSelect(fx.Client);
            Assert.Equal(HttpStatusCode.OK, select.StatusCode);
            Assert.Equal(Instance(identify), Instance(select));
            seen.Add(Instance(identify));
        }
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public async Task AffinityCookie_IsRoutingOnly_NotAnAuthenticationArtifact()
    {
        using var fx = new ProxyFixture(affinity: true);
        var (identify, _, flow) = await IdentifyThenSelect(fx.Client);
        var cookie = identify.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith(".AuthPolicyEngine.Affinity=", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(flow, cookie);
        Assert.DoesNotContain("account.a", cookie);
    }

    [Fact]
    public async Task InstanceHeader_IsPresentInDevelopment_AndAbsentInProduction()
    {
        var config = new Dictionary<string, string?> { ["Instance:Id"] = "instance-test" };
        using var dev = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config)));
        using var prod = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Production").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config)));

        Assert.Equal("instance-test", Instance(await dev.CreateClient().GetAsync("/health")));
        Assert.Null(Instance(await prod.CreateClient().GetAsync("/health")));
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var f = new WebApplicationFactory<Program>();
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().GetAsync("/health")).StatusCode);
    }
}
