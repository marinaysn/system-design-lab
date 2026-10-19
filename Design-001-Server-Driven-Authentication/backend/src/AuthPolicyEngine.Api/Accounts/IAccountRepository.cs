namespace AuthPolicyEngine.Api.Accounts;

public interface IAccountRepository
{
    Account? FindByIdentifier(string identifier);
}
