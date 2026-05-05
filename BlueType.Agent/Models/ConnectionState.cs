namespace BlueType.Agent.Models;

internal enum ConnectionState
{
    Idle,
    Listening,
    ClientConnected,
    Authenticating,
    PendingApproval,
    Connected,
    Disconnecting,
}
