namespace Mics.Gateway.Infrastructure;

internal interface IShutdownState
{
    bool IsDraining { get; }
    void BeginDrain();
}

internal sealed class ShutdownState : IShutdownState
{
    private int _draining;

    public bool IsDraining => Volatile.Read(ref _draining) != 0;

    public void BeginDrain()
    {
        Interlocked.Exchange(ref _draining, 1);
    }
}

