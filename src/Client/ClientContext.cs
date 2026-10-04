namespace AnythinkCli.Client;

public static class ClientContext
{
    private static readonly AsyncLocal<AnythinkClient?> Ambient = new();
    private static readonly AsyncLocal<CancellationToken> AmbientCancellation = new();

    public static AnythinkClient? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    public static CancellationToken Cancellation
    {
        get => AmbientCancellation.Value;
        set => AmbientCancellation.Value = value;
    }
}
