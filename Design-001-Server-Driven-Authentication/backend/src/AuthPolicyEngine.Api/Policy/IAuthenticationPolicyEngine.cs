using AuthPolicyEngine.Api.Accounts;
using AuthPolicyEngine.Api.Strategies;

namespace AuthPolicyEngine.Api.Policy;

public sealed record AuthenticationContext(string? Region = null);

public sealed record Challenge(AuthenticationType Type, string DisplayName, string Description);

/// <summary>Selected is null when the account has no applicable method.</summary>
public sealed record PolicyDecision(Challenge? Selected, IReadOnlyList<Challenge> Alternatives);

public interface IAuthenticationPolicyEngine
{
    PolicyDecision Evaluate(Account account, AuthenticationContext context);
}
