namespace AuthPolicyEngine.Api.Accounts;

public sealed class InMemoryAccountRepository : IAccountRepository
{
    private static readonly Account[] Accounts =
    [
        new("user-a", "account.a@example.test", HasPassword: true,  HasVerifiedEmail: true,  HasTotpEnrolled: false),
        new("user-b", "account.b@example.test", HasPassword: true,  HasVerifiedEmail: false, HasTotpEnrolled: true),
        new("user-c", "account.c@example.test", HasPassword: false, HasVerifiedEmail: true,  HasTotpEnrolled: false),
        new("user-d", "account.d@example.test", HasPassword: false, HasVerifiedEmail: false, HasTotpEnrolled: false),
    ];

    public Account? FindByIdentifier(string identifier) =>
        Accounts.FirstOrDefault(a =>
            string.Equals(a.Identifier, identifier?.Trim(), StringComparison.OrdinalIgnoreCase));
}
