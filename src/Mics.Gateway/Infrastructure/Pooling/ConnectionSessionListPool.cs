using System.Collections.Concurrent;
using Mics.Gateway.Connections;

namespace Mics.Gateway.Infrastructure.Pooling;

internal static class ConnectionSessionListPool
{
    private static readonly ConcurrentBag<List<ConnectionSession>> Bag = new();

    // Per-user online device connections are typically small.
    // Avoid retaining unusually large backing arrays in the pool.
    private const int MaxRetainedCapacity = 64;

    public static Pooled Rent()
    {
        if (!Bag.TryTake(out var list))
        {
            list = new List<ConnectionSession>(capacity: 8);
        }

        return new Pooled(list);
    }

    private static void Return(List<ConnectionSession> list)
    {
        list.Clear();

        if (list.Capacity > MaxRetainedCapacity)
        {
            return;
        }

        Bag.Add(list);
    }

    public readonly struct Pooled : IDisposable
    {
        public Pooled(List<ConnectionSession> list)
        {
            List = list;
        }

        public List<ConnectionSession> List { get; }

        public void Dispose()
        {
            Return(List);
        }
    }
}
