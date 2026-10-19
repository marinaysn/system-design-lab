using System.Collections.Concurrent;
using AuthPolicyEngine.Api.Strategies;

namespace AuthPolicyEngine.Api.Flows;

/// <summary>Server-held state of one identify flow. The client only ever sees the Id.</summary>
public sealed class AuthFlow
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required IReadOnlyList<AuthenticationType> AllowedTypes { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public AuthenticationType SelectedType { get; set; }
}

public interface IAuthFlowStore
{
    AuthFlow Create(string userId, IReadOnlyList<AuthenticationType> allowedTypes, AuthenticationType selected);
    AuthFlow? Get(string flowId);
    bool IsExpired(AuthFlow flow);
}

public sealed class InMemoryAuthFlowStore : IAuthFlowStore
{
    private readonly ConcurrentDictionary<string, AuthFlow> _flows = new();
    private readonly TimeProvider _time;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Policy.AuthPolicyOptions> _options;

    public InMemoryAuthFlowStore(TimeProvider time, Microsoft.Extensions.Options.IOptionsMonitor<Policy.AuthPolicyOptions> options)
    {
        _time = time;
        _options = options;
    }

    public AuthFlow Create(string userId, IReadOnlyList<AuthenticationType> allowedTypes, AuthenticationType selected)
    {
        var now = _time.GetUtcNow();
        foreach (var (id, f) in _flows)
        {
            if (f.ExpiresAt <= now) _flows.TryRemove(id, out _);
        }

        var flow = new AuthFlow
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            AllowedTypes = allowedTypes,
            SelectedType = selected,
            ExpiresAt = now + _options.CurrentValue.FlowLifetime
        };
        _flows[flow.Id] = flow;
        return flow;
    }

    public AuthFlow? Get(string flowId) => _flows.TryGetValue(flowId, out var f) ? f : null;

    public bool IsExpired(AuthFlow flow) => flow.ExpiresAt <= _time.GetUtcNow();
}
