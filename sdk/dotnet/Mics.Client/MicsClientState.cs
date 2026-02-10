namespace Mics.Client;

public enum MicsClientState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Disposing = 4,
}

