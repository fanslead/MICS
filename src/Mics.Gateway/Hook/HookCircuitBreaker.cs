using System.Collections.Concurrent;

namespace Mics.Gateway.Hook;

internal enum HookOperation
{
    Auth = 0,
    CheckMessage = 1,
    GetGroupMembers = 2,
}

internal sealed class HookCircuitBreaker
{
    private sealed class State
    {
        public int ConsecutiveFailures;
        public long OpenUntilUnixMs;
        public int HalfOpenInFlight;
    }

    private readonly ConcurrentDictionary<(string TenantId, HookOperation Op), State> _states = new();
    private readonly TimeProvider _timeProvider;

    public HookCircuitBreaker(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool TryBegin(string tenantId, HookOperation op)
    {
        var state = _states.GetOrAdd((tenantId, op), _ => new State());

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var openUntil = Volatile.Read(ref state.OpenUntilUnixMs);
        if (openUntil <= 0 || now >= openUntil)
        {
            // Closed (or open duration elapsed -> half-open)
            if (openUntil > 0)
            {
                // Half-open: allow only one in flight
                return Interlocked.CompareExchange(ref state.HalfOpenInFlight, 1, 0) == 0;
            }

            return true;
        }

        return false;
    }

    public void OnSuccess(string tenantId, HookOperation op)
    {
        var state = _states.GetOrAdd((tenantId, op), _ => new State());
        Volatile.Write(ref state.ConsecutiveFailures, 0);
        Volatile.Write(ref state.OpenUntilUnixMs, 0);
        Interlocked.Exchange(ref state.HalfOpenInFlight, 0);
    }

    public void EndAttempt(string tenantId, HookOperation op)
    {
        var state = _states.GetOrAdd((tenantId, op), _ => new State());
        // Release the half-open in-flight gate even if the request never reached the hook (e.g., queue rejected).
        Interlocked.Exchange(ref state.HalfOpenInFlight, 0);
    }

    public void OnFailure(string tenantId, HookOperation op, HookBreakerPolicy policy)
    {
        var state = _states.GetOrAdd((tenantId, op), _ => new State());

        var threshold = Math.Max(1, policy.FailureThreshold);
        var openDuration = policy.OpenDuration < TimeSpan.Zero ? TimeSpan.Zero : policy.OpenDuration;

        var failures = Interlocked.Increment(ref state.ConsecutiveFailures);
        if (failures >= threshold)
        {
            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var until = now + (long)openDuration.TotalMilliseconds;
            Volatile.Write(ref state.OpenUntilUnixMs, until);
            Interlocked.Exchange(ref state.HalfOpenInFlight, 0);
        }
    }
}
