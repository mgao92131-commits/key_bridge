using BlueType.Agent.Models;
using BlueType.Agent.Transport;

namespace BlueType.Agent.Application.Sessions;

internal sealed record SessionExecutionContext(
    ClientSession Session,
    Guid SessionId,
    string? RemoteAddress,
    string Transport,
    Action<ConnectionState>? OnState,
    Action<string>? OnMessage,
    Action DisconnectCurrentSession);
