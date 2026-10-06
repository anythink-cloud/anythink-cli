using AnythinkCli.Client;

namespace AnythinkMcp;

public sealed class HostedAccounts(HostedCredentials credentials, McpClientFactory factory, HostedMode.Options options)
{
    public const string NoAccessMessage =
        "This connection doesn't include account access. Reconnect Anythink and tick 'Manage projects and billing'.";

    public bool HasAccess => credentials.AccountAccess && !string.IsNullOrEmpty(credentials.InboundToken);

    public BillingClient Client() =>
        HasAccess
            ? factory.GetCallerBillingClient(options.Issuer, credentials.InboundToken!)
            : throw new HostedProjectException(NoAccessMessage);
}
