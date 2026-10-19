using System.ComponentModel.DataAnnotations;
using AuthPolicyEngine.Api.Strategies;

namespace AuthPolicyEngine.Api.Contracts;

public sealed class IdentifyRequest
{
    [Required(AllowEmptyStrings = false)]
    public string? Identifier { get; set; }

    /// <summary>Optional request context used by region overrides in policy.</summary>
    public string? Region { get; set; }
}

public sealed class SelectChallengeRequest
{
    [Required]
    public AuthenticationType? AuthenticationType { get; set; }
}

public sealed record ChallengeDto(AuthenticationType Type, string DisplayName, string Description);

/// <summary>
/// Simulation only: describes which challenge the client should render. It never means "authenticated".
/// FlowId/ExpiresAt/SelectedChallenge are null when the account has no eligible method.
/// </summary>
public sealed record ChallengeResponse(
    string? FlowId,
    DateTimeOffset? ExpiresAt,
    ChallengeDto? SelectedChallenge,
    IReadOnlyList<ChallengeDto> Alternatives,
    string Notice);
