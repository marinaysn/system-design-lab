using AuthPolicyEngine.Api.Accounts;

namespace AuthPolicyEngine.Api.Strategies;

public sealed class PasswordStrategy : IAuthenticationStrategy
{
    public AuthenticationType Type => AuthenticationType.Password;
    public string DisplayName => "Password";
    public string Description => "Enter your account password.";
    public bool IsApplicable(Account account) => account.HasPassword;
}

public sealed class EmailOtpStrategy : IAuthenticationStrategy
{
    public AuthenticationType Type => AuthenticationType.EmailOtp;
    public string DisplayName => "Email code";
    public string Description => "Enter a one-time code sent to your email.";
    public bool IsApplicable(Account account) => account.HasVerifiedEmail;
}

public sealed class TotpStrategy : IAuthenticationStrategy
{
    public AuthenticationType Type => AuthenticationType.Totp;
    public string DisplayName => "Authenticator app";
    public string Description => "Enter the code from your authenticator app.";
    public bool IsApplicable(Account account) => account.HasTotpEnrolled;
}
