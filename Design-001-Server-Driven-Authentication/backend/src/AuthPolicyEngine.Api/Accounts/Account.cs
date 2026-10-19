namespace AuthPolicyEngine.Api.Accounts;

/// <summary>
/// Fictional demo account. Holds only which methods are set up; never any credential or secret.
/// </summary>
public sealed record Account(
    string UserId,
    string Identifier,
    bool HasPassword,
    bool HasVerifiedEmail,
    bool HasTotpEnrolled);
