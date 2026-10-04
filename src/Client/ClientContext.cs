namespace AnythinkCli.Client;

public static class ClientContext
{
    private static readonly AsyncLocal<AnythinkClient?> Ambient = new();

    public static AnythinkClient? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }
}
