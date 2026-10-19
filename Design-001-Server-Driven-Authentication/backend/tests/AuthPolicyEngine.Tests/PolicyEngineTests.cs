using AuthPolicyEngine.Api.Accounts;
using AuthPolicyEngine.Api.Policy;
using AuthPolicyEngine.Api.Strategies;
using Microsoft.Extensions.Options;

namespace AuthPolicyEngine.Tests;

public class PolicyEngineTests
{
    private sealed class FixedMonitor(AuthPolicyOptions value) : IOptionsMonitor<AuthPolicyOptions>
    {
        public AuthPolicyOptions Current { get; set; } = value;
        public AuthPolicyOptions CurrentValue => Current;
        public AuthPolicyOptions Get(string? name) => Current;
        public IDisposable? OnChange(Action<AuthPolicyOptions, string?> listener) => null;
    }

    private static readonly IAuthenticationStrategy[] Strategies =
        [new PasswordStrategy(), new EmailOtpStrategy(), new TotpStrategy()];

    private static AuthenticationPolicyEngine Engine(FixedMonitor m) => new(Strategies, m);

    private static FixedMonitor Prefer(params AuthenticationType[] order) =>
        new(new AuthPolicyOptions { PreferredOrder = order.ToList() });

    private static readonly InMemoryAccountRepository Repo = new();
    private static Account A => Repo.FindByIdentifier("account.a@example.test")!; // Password + EmailOtp
    private static Account B => Repo.FindByIdentifier("account.b@example.test")!; // Password + Totp
    private static Account C => Repo.FindByIdentifier("account.c@example.test")!; // EmailOtp
    private static Account D => Repo.FindByIdentifier("account.d@example.test")!; // none

    private static readonly AuthenticationContext Ctx = new();

    [Fact]
    public void Strategies_ReportApplicabilityPerAccount()
    {
        Assert.True(new PasswordStrategy().IsApplicable(A));
        Assert.True(new EmailOtpStrategy().IsApplicable(A));
        Assert.False(new TotpStrategy().IsApplicable(A));
        Assert.True(new TotpStrategy().IsApplicable(B));
        Assert.False(new PasswordStrategy().IsApplicable(C));
        Assert.False(new EmailOtpStrategy().IsApplicable(D));
    }

    [Fact]
    public void PasswordPreference_SelectsPassword()
    {
        var d = Engine(Prefer(AuthenticationType.Password, AuthenticationType.EmailOtp, AuthenticationType.Totp)).Evaluate(A, Ctx);
        Assert.Equal(AuthenticationType.Password, d.Selected!.Type);
        Assert.Equal([AuthenticationType.EmailOtp], d.Alternatives.Select(a => a.Type));
    }

    [Fact]
    public void EmailOtpPreference_SelectsEmailOtp()
    {
        var d = Engine(Prefer(AuthenticationType.EmailOtp, AuthenticationType.Password)).Evaluate(A, Ctx);
        Assert.Equal(AuthenticationType.EmailOtp, d.Selected!.Type);
        Assert.Equal([AuthenticationType.Password], d.Alternatives.Select(a => a.Type));
    }

    [Fact]
    public void TotpPreference_SelectsTotpWhenAvailable()
    {
        var d = Engine(Prefer(AuthenticationType.Totp, AuthenticationType.Password)).Evaluate(B, Ctx);
        Assert.Equal(AuthenticationType.Totp, d.Selected!.Type);
        Assert.Equal([AuthenticationType.Password], d.Alternatives.Select(a => a.Type));
    }

    [Fact]
    public void UnavailablePreferredMethod_FallsBack()
    {
        var d = Engine(Prefer(AuthenticationType.Totp, AuthenticationType.Password)).Evaluate(A, Ctx);
        Assert.Equal(AuthenticationType.Password, d.Selected!.Type);

        var onlyEmail = Engine(Prefer(AuthenticationType.Password, AuthenticationType.Totp)).Evaluate(C, Ctx);
        Assert.Equal(AuthenticationType.EmailOtp, onlyEmail.Selected!.Type);
        Assert.Empty(onlyEmail.Alternatives);
    }

    [Fact]
    public void PartialOrConfiguredEmpty_StillReturnsAllApplicableMethods()
    {
        var d = Engine(Prefer()).Evaluate(A, Ctx);
        Assert.Equal(AuthenticationType.Password, d.Selected!.Type);
        Assert.Single(d.Alternatives);
    }

    [Fact]
    public void NoEligibleMethods_ReturnsNoChallenge()
    {
        var d = Engine(Prefer(AuthenticationType.Password)).Evaluate(D, Ctx);
        Assert.Null(d.Selected);
        Assert.Empty(d.Alternatives);
    }

    [Fact]
    public void SameInputs_AreDeterministic()
    {
        var e = Engine(Prefer(AuthenticationType.EmailOtp));
        var first = e.Evaluate(A, Ctx);
        var second = e.Evaluate(A, Ctx);
        Assert.Equal(first.Selected, second.Selected);
        Assert.Equal(first.Alternatives, second.Alternatives);
    }

    [Fact]
    public void ChangingPolicy_ChangesSelection()
    {
        var monitor = Prefer(AuthenticationType.Password);
        var e = Engine(monitor);
        Assert.Equal(AuthenticationType.Password, e.Evaluate(A, Ctx).Selected!.Type);

        monitor.Current = new AuthPolicyOptions { PreferredOrder = [AuthenticationType.EmailOtp] };
        Assert.Equal(AuthenticationType.EmailOtp, e.Evaluate(A, Ctx).Selected!.Type);
    }

    [Fact]
    public void RegionOverride_AppliesOnlyToMatchingRegion()
    {
        var options = new AuthPolicyOptions
        {
            PreferredOrder = [AuthenticationType.Password],
            RegionOverrides = new() { ["EU"] = [AuthenticationType.EmailOtp] }
        };
        var e = Engine(new FixedMonitor(options));
        Assert.Equal(AuthenticationType.EmailOtp, e.Evaluate(A, new AuthenticationContext("eu")).Selected!.Type);
        Assert.Equal(AuthenticationType.Password, e.Evaluate(A, new AuthenticationContext("US")).Selected!.Type);
    }
}
