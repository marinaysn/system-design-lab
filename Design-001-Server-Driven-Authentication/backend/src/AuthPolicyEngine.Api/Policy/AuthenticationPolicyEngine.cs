using AuthPolicyEngine.Api.Accounts;
using AuthPolicyEngine.Api.Strategies;
using Microsoft.Extensions.Options;

namespace AuthPolicyEngine.Api.Policy;

public sealed class AuthenticationPolicyEngine : IAuthenticationPolicyEngine
{
    private readonly IReadOnlyList<IAuthenticationStrategy> _strategies;
    private readonly IOptionsMonitor<AuthPolicyOptions> _options;

    public AuthenticationPolicyEngine(
        IEnumerable<IAuthenticationStrategy> strategies,
        IOptionsMonitor<AuthPolicyOptions> options)
    {
        _strategies = strategies.ToList();
        _options = options;
    }

    public PolicyDecision Evaluate(Account account, AuthenticationContext context)
    {
        var options = _options.CurrentValue;
        var preferred = ResolvePreferredOrder(options, context);

        // Methods missing from the configured order are appended in registration order,
        // so an applicable method is never dropped by a partial configuration.
        var rank = preferred.Distinct().ToList();
        foreach (var s in _strategies)
        {
            if (!rank.Contains(s.Type)) rank.Add(s.Type);
        }

        var ordered = _strategies
            .Where(s => s.IsApplicable(account))
            .OrderBy(s => rank.IndexOf(s.Type))
            .Select(s => new Challenge(s.Type, s.DisplayName, s.Description))
            .ToList();

        return ordered.Count == 0
            ? new PolicyDecision(null, Array.Empty<Challenge>())
            : new PolicyDecision(ordered[0], ordered.Skip(1).ToList());
    }

    private static List<AuthenticationType> ResolvePreferredOrder(AuthPolicyOptions options, AuthenticationContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Region))
        {
            foreach (var (region, order) in options.RegionOverrides)
            {
                if (string.Equals(region, context.Region.Trim(), StringComparison.OrdinalIgnoreCase))
                    return order;
            }
        }
        return options.PreferredOrder;
    }
}
