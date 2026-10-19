using AuthPolicyEngine.Api.Accounts;

namespace AuthPolicyEngine.Api.Strategies;

/// <summary>
/// Describes one authentication method. Credential verification is intentionally out of scope.
/// </summary>
public interface IAuthenticationStrategy
{
    AuthenticationType Type { get; }
    string DisplayName { get; }
    string Description { get; }
    bool IsApplicable(Account account);
}
