using System.Security.Cryptography;
using System.Text;
using BlueType.Agent.Models;

namespace BlueType.Agent.Core;

internal sealed class AuthService
{
    private readonly DeviceRegistry _deviceRegistry;
    private readonly Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> _promptApprovalAsync;

    public AuthService(
        DeviceRegistry deviceRegistry,
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptApprovalAsync)
    {
        _deviceRegistry = deviceRegistry;
        _promptApprovalAsync = promptApprovalAsync;
    }

    public AuthResult TryAuthorizeKnownDevice(HelloInfo helloInfo, string? token, string? remoteAddress, string transport)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthResult.Error("NOT_AUTHORIZED", "Device is not authorized.");
        }

        if (!_deviceRegistry.TryGet(helloInfo.DeviceId, out var device))
        {
            return AuthResult.Error("NOT_AUTHORIZED", "Device is not authorized.");
        }

        if (!string.Equals(device.TokenHash, HashToken(token), StringComparison.Ordinal))
        {
            return AuthResult.Error("NOT_AUTHORIZED", "Invalid token.");
        }

        _deviceRegistry.Upsert(device with
        {
            DeviceName = helloInfo.DeviceName,
            LastIp = NormalizeRemoteAddress(remoteAddress),
            LastTransport = transport,
            LastSeenAt = DateTimeOffset.UtcNow,
        });

        return AuthResult.Authorized(token, persistToken: true);
    }

    public async Task<AuthResult> RequestApprovalAsync(
        HelloInfo helloInfo,
        string? remoteAddress,
        string transport,
        CancellationToken cancellationToken)
    {
        var promptRequest = new AuthPromptRequest(
            AuthPromptMode.AuthorizeDevice,
            helloInfo.DeviceId,
            helloInfo.DeviceName,
            remoteAddress,
            transport);

        AuthPromptDecision decision;
        try
        {
            decision = await _promptApprovalAsync(promptRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return AuthResult.Error("AUTH_UI_UNAVAILABLE", "Authorization prompt is unavailable.");
        }

        return decision switch
        {
            AuthPromptDecision.AllowOnce => AuthResult.Authorized(token: null, persistToken: false),
            AuthPromptDecision.AlwaysAllow => PersistAuthorizedDevice(helloInfo, remoteAddress, transport),
            AuthPromptDecision.Unavailable => AuthResult.Error("AUTH_UI_UNAVAILABLE", "Authorization prompt is unavailable."),
            _ => AuthResult.Error("NOT_AUTHORIZED", "Device was not approved."),
        };
    }

    private AuthResult PersistAuthorizedDevice(HelloInfo helloInfo, string? remoteAddress, string transport)
    {
        var token = CreateToken();
        var device = new DeviceAuthInfo(
            DeviceId: helloInfo.DeviceId,
            DeviceName: helloInfo.DeviceName,
            BluetoothAddress: null,
            LastIp: NormalizeRemoteAddress(remoteAddress),
            LastTransport: transport,
            TokenHash: HashToken(token),
            LastSeenAt: DateTimeOffset.UtcNow);

        _deviceRegistry.Upsert(device);
        return AuthResult.Authorized(token, persistToken: true);
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return $"sha256:{Convert.ToHexString(hash)}";
    }

    private static string? NormalizeRemoteAddress(string? remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress))
        {
            return null;
        }

        var lastColon = remoteAddress.LastIndexOf(':');
        return lastColon > 0 ? remoteAddress[..lastColon] : remoteAddress;
    }
}
