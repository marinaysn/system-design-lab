using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthPolicyEngine.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthPolicyEngine.Tests;

public class ApiTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static HttpClient Client(string[] order, TestClock? clock = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) =>
            {
                var d = new Dictionary<string, string?>();
                for (var i = 0; i < order.Length; i++) d[$"AuthPolicy:PreferredOrder:{i}"] = order[i];
                c.AddInMemoryCollection(d);
            });
            if (clock is not null)
                b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(clock));
        }).CreateClient();

    private static Task<HttpResponseMessage> Identify(HttpClient c, string? id) =>
        c.PostAsJsonAsync("/api/auth/identify", new { identifier = id });

    private static async Task<ChallengeResponse> Body(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<ChallengeResponse>(Json))!;

    private static StringContent Raw(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Identify_SelectsConfiguredPreferredChallenge()
    {
        var r = await Identify(Client(["EmailOtp", "Password"]), "account.a@example.test");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        Assert.Equal(Api.Strategies.AuthenticationType.EmailOtp, body.SelectedChallenge!.Type);
        Assert.Single(body.Alternatives);
        Assert.False(string.IsNullOrEmpty(body.FlowId));
        Assert.Contains("SIMULATION", body.Notice);
    }

    [Fact]
    public async Task ChangingServerPolicy_ChangesChallengeWithoutClientChange()
    {
        var pw = await Body(await Identify(Client(["Password", "EmailOtp"]), "account.a@example.test"));
        var otp = await Body(await Identify(Client(["EmailOtp", "Password"]), "account.a@example.test"));
        Assert.Equal(Api.Strategies.AuthenticationType.Password, pw.SelectedChallenge!.Type);
        Assert.Equal(Api.Strategies.AuthenticationType.EmailOtp, otp.SelectedChallenge!.Type);
    }

    [Fact]
    public async Task Identify_TotpPreferredAndAvailable()
    {
        var body = await Body(await Identify(Client(["Totp", "Password"]), "account.b@example.test"));
        Assert.Equal(Api.Strategies.AuthenticationType.Totp, body.SelectedChallenge!.Type);
    }

    [Fact]
    public async Task Identify_NoEligibleMethods_ReturnsNoChallengeAndNoFlow()
    {
        var r = await Identify(Client(["Password"]), "account.d@example.test");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        Assert.Null(body.SelectedChallenge);
        Assert.Null(body.FlowId);
        Assert.Empty(body.Alternatives);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Identify_MissingOrEmptyIdentifier_Returns400(string? id)
    {
        var r = await Identify(Client(["Password"]), id);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Identify_InvalidBody_Returns400()
    {
        var c = Client(["Password"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/auth/identify", Raw("{not json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/auth/identify", Raw("null"))).StatusCode);
    }

    [Fact]
    public async Task Identify_UnknownAccount_Returns404ProblemDetails()
    {
        var r = await Identify(Client(["Password"]), "nobody@example.test");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Select_AllowedAlternative_ChangesSelectedChallenge()
    {
        var c = Client(["Password", "EmailOtp"]);
        var flow = await Body(await Identify(c, "account.a@example.test"));

        var r = await c.PostAsJsonAsync($"/api/auth/flows/{flow.FlowId}/select", new { authenticationType = "EmailOtp" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        Assert.Equal(Api.Strategies.AuthenticationType.EmailOtp, body.SelectedChallenge!.Type);
        Assert.Equal(Api.Strategies.AuthenticationType.Password, Assert.Single(body.Alternatives).Type);
    }

    [Fact]
    public async Task Select_UnavailableAlternative_Returns409()
    {
        var c = Client(["Password", "EmailOtp"]);
        var flow = await Body(await Identify(c, "account.a@example.test")); // no TOTP

        var r = await c.PostAsJsonAsync($"/api/auth/flows/{flow.FlowId}/select", new { authenticationType = "Totp" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"authenticationType\":\"Sms\"}")]
    [InlineData("{\"authenticationType\":1}")]
    public async Task Select_InvalidRequest_Returns400(string json)
    {
        var c = Client(["Password", "EmailOtp"]);
        var flow = await Body(await Identify(c, "account.a@example.test"));
        var r = await c.PostAsync($"/api/auth/flows/{flow.FlowId}/select", Raw(json));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Select_UnknownFlow_Returns404()
    {
        var r = await Client(["Password"]).PostAsJsonAsync("/api/auth/flows/does-not-exist/select", new { authenticationType = "Password" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Select_ExpiredFlow_Returns410_AfterTenMinutes()
    {
        var clock = new TestClock();
        var c = Client(["Password", "EmailOtp"], clock);
        var flow = await Body(await Identify(c, "account.a@example.test"));
        Assert.Equal(clock.Now.AddMinutes(10), flow.ExpiresAt);

        clock.Now = clock.Now.AddMinutes(9).AddSeconds(59);
        var ok = await c.PostAsJsonAsync($"/api/auth/flows/{flow.FlowId}/select", new { authenticationType = "EmailOtp" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        clock.Now = clock.Now.AddSeconds(1);
        var expired = await c.PostAsJsonAsync($"/api/auth/flows/{flow.FlowId}/select", new { authenticationType = "Password" });
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
    }
}
