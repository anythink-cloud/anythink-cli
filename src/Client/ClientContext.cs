namespace AnythinkCli.Client;

public static class ClientContext
{
    private static readonly AsyncLocal<AnythinkClient?> Ambient = new();
    private static readonly AsyncLocal<CancellationToken> AmbientCancellation = new();
    private static readonly AsyncLocal<bool> AmbientRemote = new();
    private static readonly AsyncLocal<BillingClient?> AmbientBilling = new();

    public static AnythinkClient? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    public static BillingClient? Billing
    {
        get => AmbientBilling.Value;
        set => AmbientBilling.Value = value;
    }

    public static bool Remote
    {
        get => AmbientRemote.Value;
        set => AmbientRemote.Value = value;
    }

    public static void RequireLocal()
    {
        if (Remote)
            throw new InvalidOperationException("The server's own CLI login and configuration aren't available to remote callers.");
    }

    public static CancellationToken Cancellation
    {
        get => AmbientCancellation.Value;
        set => AmbientCancellation.Value = value;
    }
}
