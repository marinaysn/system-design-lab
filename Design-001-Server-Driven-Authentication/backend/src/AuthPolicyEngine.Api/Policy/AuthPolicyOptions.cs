using AuthPolicyEngine.Api.Strategies;

namespace AuthPolicyEngine.Api.Policy;

/// <summary>
/// Server-side policy configuration (section "AuthPolicy"). Order = most preferred first.
/// </summary>
public sealed class AuthPolicyOptions
{
    public const string SectionName = "AuthPolicy";

    public List<AuthenticationType> PreferredOrder { get; set; } = new();

    /// <summary>Optional per-region override of PreferredOrder, keyed by region code (e.g. "EU").</summary>
    public Dictionary<string, List<AuthenticationType>> RegionOverrides { get; set; } = new();

    public TimeSpan FlowLifetime { get; set; } = TimeSpan.FromMinutes(10);
}
