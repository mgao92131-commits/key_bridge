namespace BlueType.Agent.Models;

internal sealed record AuthResult(
    bool IsAuthorized,
    bool PersistToken,
    string? Token,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static AuthResult Authorized(string? token, bool persistToken)
    {
        return new AuthResult(true, persistToken, token, null, null);
    }

    public static AuthResult Error(string code, string message)
    {
        return new AuthResult(false, false, null, code, message);
    }
}
