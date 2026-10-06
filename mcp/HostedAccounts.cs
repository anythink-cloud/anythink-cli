using AnythinkCli.Client;

namespace AnythinkMcp;

public sealed class HostedAccounts(HostedCredentials credentials, McpClientFactory factory, HostedMode.Options options)
{
    public const string NoAccessMessage =
        "This connection doesn't include account access. Reconnect Anythink and tick 'Manage projects and billing'.";

    public const string OneProjectMessage =
        "This connection is limited to one project, so it can't manage projects or billing. Reconnect Anythink and grant access to all your projects.";

    // A one-project grant is a deliberate confinement; account powers would escape it.
    public bool HasAccess => credentials.AccountAccess && credentials.AllProjects && !string.IsNullOrEmpty(credentials.InboundToken);

    public BillingClient Client() =>
        HasAccess
            ? factory.GetCallerBillingClient(options.Issuer, credentials.InboundToken!)
            : throw new HostedProjectException(credentials.AccountAccess ? OneProjectMessage : NoAccessMessage);
}
