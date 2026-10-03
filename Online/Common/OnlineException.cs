namespace ChessGame.Api.Online;

public sealed class OnlineException(string code, int status = 409, long? version = null) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public long? Version { get; } = version;
}
