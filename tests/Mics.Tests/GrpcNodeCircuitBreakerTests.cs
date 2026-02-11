using Mics.Gateway.Grpc;

namespace Mics.Tests;

public sealed class GrpcNodeCircuitBreakerTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    [Fact]
    public void HalfOpenAttempt_ThatNeverCompletes_DoesNotWedgeBreaker()
    {
        const string nodeId = "n1";

        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var breaker = new GrpcNodeCircuitBreaker(time);
        var policy = new GrpcBreakerPolicy(FailureThreshold: 1, OpenDuration: TimeSpan.FromSeconds(1));

        breaker.OnFailure(nodeId, policy);

        Assert.False(breaker.TryBegin(nodeId));

        time.Advance(TimeSpan.FromSeconds(2));

        // Half-open: allow only one in-flight.
        Assert.True(breaker.TryBegin(nodeId));
        Assert.False(breaker.TryBegin(nodeId));

        // If the half-open slot isn't released, breaker would stay wedged.
        breaker.EndAttempt(nodeId);
        Assert.True(breaker.TryBegin(nodeId));
    }
}
