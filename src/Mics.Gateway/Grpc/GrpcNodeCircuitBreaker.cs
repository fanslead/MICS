using System.Collections.Concurrent;

namespace Mics.Gateway.Grpc;

internal readonly record struct GrpcBreakerPolicy(int FailureThreshold, TimeSpan OpenDuration);

internal sealed class GrpcNodeCircuitBreaker
{
    private sealed class State
    {
        public int ConsecutiveFailures;
        public long OpenUntilUnixMs;
        public int HalfOpenInFlight;
    }

    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public GrpcNodeCircuitBreaker(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool TryBegin(string targetNodeId)
    {
        var state = _states.GetOrAdd(targetNodeId, _ => new State());

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

    public void OnSuccess(string targetNodeId)
    {
        var state = _states.GetOrAdd(targetNodeId, _ => new State());
        Volatile.Write(ref state.ConsecutiveFailures, 0);
        Volatile.Write(ref state.OpenUntilUnixMs, 0);
        Interlocked.Exchange(ref state.HalfOpenInFlight, 0);
    }

    public void EndAttempt(string targetNodeId)
    {
        var state = _states.GetOrAdd(targetNodeId, _ => new State());
        Interlocked.Exchange(ref state.HalfOpenInFlight, 0);
    }

    public void OnFailure(string targetNodeId, GrpcBreakerPolicy policy)
    {
        var state = _states.GetOrAdd(targetNodeId, _ => new State());

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
        else
        {
            // Ensure half-open gate is released on failure.
            Interlocked.Exchange(ref state.HalfOpenInFlight, 0);
        }
    }
}
