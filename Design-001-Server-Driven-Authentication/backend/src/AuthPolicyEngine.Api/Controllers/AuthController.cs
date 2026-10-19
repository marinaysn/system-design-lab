using AuthPolicyEngine.Api.Accounts;
using AuthPolicyEngine.Api.Contracts;
using AuthPolicyEngine.Api.Flows;
using AuthPolicyEngine.Api.Policy;
using AuthPolicyEngine.Api.Strategies;
using Microsoft.AspNetCore.Mvc;

namespace AuthPolicyEngine.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string Notice = "SIMULATION: challenge selection only. No credentials are verified and no user is authenticated.";

    private readonly IAccountRepository _accounts;
    private readonly IAuthenticationPolicyEngine _engine;
    private readonly IEnumerable<IAuthenticationStrategy> _strategies;
    private readonly IAuthFlowStore _flows;

    public AuthController(
        IAccountRepository accounts,
        IAuthenticationPolicyEngine engine,
        IEnumerable<IAuthenticationStrategy> strategies,
        IAuthFlowStore flows)
    {
        _accounts = accounts;
        _engine = engine;
        _strategies = strategies;
        _flows = flows;
    }

    [HttpPost("identify")]
    [ProducesResponseType<ChallengeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult Identify([FromBody] IdentifyRequest request)
    {
        var account = _accounts.FindByIdentifier(request.Identifier!);
        if (account is null)
            return Problem(StatusCodes.Status404NotFound, "Account not found", "No account matches the identifier.");

        var decision = _engine.Evaluate(account, new AuthenticationContext(request.Region));
        if (decision.Selected is null)
            return Ok(new ChallengeResponse(null, null, null, Array.Empty<ChallengeDto>(), Notice));

        var allowed = new[] { decision.Selected }.Concat(decision.Alternatives).Select(c => c.Type).ToList();
        var flow = _flows.Create(account.UserId, allowed, decision.Selected.Type);

        return Ok(new ChallengeResponse(
            flow.Id, flow.ExpiresAt,
            ToDto(decision.Selected),
            decision.Alternatives.Select(ToDto).ToList(),
            Notice));
    }

    [HttpPost("flows/{flowId}/select")]
    [ProducesResponseType<ChallengeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public IActionResult Select(string flowId, [FromBody] SelectChallengeRequest request)
    {
        var flow = _flows.Get(flowId);
        if (flow is null)
            return Problem(StatusCodes.Status404NotFound, "Flow not found", "The authentication flow does not exist.");

        if (_flows.IsExpired(flow))
            return Problem(StatusCodes.Status410Gone, "Flow expired", "The authentication flow has expired. Start again.");

        var requested = request.AuthenticationType!.Value;
        if (!flow.AllowedTypes.Contains(requested))
            return Problem(StatusCodes.Status409Conflict, "Method not allowed", "The requested method is not allowed for this flow.");

        flow.SelectedType = requested;

        var all = flow.AllowedTypes.Select(t => ToDto(_strategies.First(s => s.Type == t))).ToList();
        var selected = all.First(c => c.Type == requested);
        return Ok(new ChallengeResponse(
            flow.Id, flow.ExpiresAt, selected,
            all.Where(c => c.Type != requested).ToList(),
            Notice));
    }

    private static ChallengeDto ToDto(Challenge c) => new(c.Type, c.DisplayName, c.Description);
    private static ChallengeDto ToDto(IAuthenticationStrategy s) => new(s.Type, s.DisplayName, s.Description);

    private ObjectResult Problem(int status, string title, string detail) =>
        base.Problem(statusCode: status, title: title, detail: detail);
}
