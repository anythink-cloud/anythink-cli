using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── integrations list ────────────────────────────────────────────────────────

public class IntegrationsListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<IntegrationDefinition> defs = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching integrations...", async _ =>
                {
                    defs = await client.GetIntegrationDefinitionsAsync();
                });

            if (defs.Count == 0)
            {
                Renderer.Info("No integrations available.");
                return 0;
            }

            Renderer.Header($"Available Integrations ({defs.Count})");

            var table = Renderer.BuildTable("Provider", "Display Name", "Category", "Auth", "Operations", "Enabled");
            foreach (var d in defs.OrderBy(x => x.Category).ThenBy(x => x.Provider))
            {
                Renderer.AddRow(table,
                    d.Provider,
                    d.DisplayName,
                    d.Category,
                    d.AuthType,
                    d.Operations.Count.ToString(),
                    d.IsEnabled ? "yes" : "no"
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations get ─────────────────────────────────────────────────────────

public class IntegrationGetSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("Integration provider key (e.g. claude, slack, hubspot)")]
    public string Provider { get; set; } = "";
}

public class IntegrationsGetCommand : BaseCommand<IntegrationGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationGetSettings settings)
    {
        try
        {
            var client = GetClient();
            IntegrationDefinition? def = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching '{settings.Provider}'...", async _ =>
                {
                    def = await client.GetIntegrationDefinitionAsync(settings.Provider);
                });

            if (def is null)
            {
                Renderer.Error($"Integration '{settings.Provider}' not found.");
                return 1;
            }

            Renderer.Header($"{def.DisplayName} ({def.Provider})");
            var info = Renderer.BuildTable("Property", "Value");
            Renderer.AddRow(info, "Description", def.Description);
            Renderer.AddRow(info, "Category", def.Category);
            Renderer.AddRow(info, "Auth type", def.AuthType);
            Renderer.AddRow(info, "Enabled", def.IsEnabled ? "yes" : "no");
            if (!string.IsNullOrEmpty(def.ParentProvider))
                Renderer.AddRow(info, "Parent provider", def.ParentProvider);
            AnsiConsole.Write(info);

            if (def.Operations.Count > 0)
            {
                Renderer.Header($"Operations ({def.Operations.Count})");
                var ops = Renderer.BuildTable("Key", "Display Name", "Description");
                foreach (var op in def.Operations)
                    Renderer.AddRow(ops, op.Key, op.DisplayName, op.Description);
                AnsiConsole.Write(ops);
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations connections list ────────────────────────────────────────────

public class IntegrationConnectionsListSettings : CommandSettings
{
    [CommandOption("--provider <PROVIDER>")]
    [Description("Filter to a specific provider")]
    public string? Provider { get; set; }
}

public class IntegrationsConnectionsListCommand : BaseCommand<IntegrationConnectionsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationConnectionsListSettings settings)
    {
        try
        {
            var client = GetClient();
            List<IntegrationConnection> conns = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching connections...", async _ =>
                {
                    conns = string.IsNullOrEmpty(settings.Provider)
                        ? await client.GetIntegrationConnectionsAsync()
                        : await client.GetIntegrationConnectionsForProviderAsync(settings.Provider);
                });

            if (conns.Count == 0)
            {
                Renderer.Info(string.IsNullOrEmpty(settings.Provider)
                    ? "No connections found."
                    : $"No connections found for '{settings.Provider}'.");
                return 0;
            }

            Renderer.Header($"Connections ({conns.Count})");
            var table = Renderer.BuildTable("ID", "Provider", "Name", "Scope", "Enabled", "Connected");
            foreach (var c in conns.OrderBy(x => x.Provider).ThenBy(x => x.Name))
            {
                Renderer.AddRow(table,
                    c.Id,
                    c.Provider ?? c.IntegrationDefinitionId,
                    c.Name,
                    c.UserId.HasValue ? $"user {c.UserId}" : "tenant",
                    c.IsEnabled ? "yes" : "no",
                    c.ConnectedAt.ToString("yyyy-MM-dd")
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations connect ─────────────────────────────────────────────────────

public class IntegrationConnectSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("Provider key (e.g. claude, openai)")]
    public string Provider { get; set; } = "";

    [CommandOption("--api-key <KEY>")]
    [Description("API key for the provider. If omitted, you'll be prompted (input is hidden).")]
    public string? ApiKey { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("Friendly name for this connection (default: \"<provider> connection\")")]
    public string? Name { get; set; }

    [CommandOption("--user-connection")]
    [Description("Make this a user-scoped connection (only the current user sees it). Default: tenant-wide.")]
    public bool UserConnection { get; set; }
}

public class IntegrationsConnectCommand : BaseCommand<IntegrationConnectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationConnectSettings settings)
    {
        try
        {
            var client = GetClient();

            // Look up the definition first so we can validate the auth type.
            IntegrationDefinition? def = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Looking up '{settings.Provider}'...", async _ =>
                {
                    def = await client.GetIntegrationDefinitionAsync(settings.Provider);
                });

            if (def is null)
            {
                Renderer.Error($"Integration '{settings.Provider}' not found. Run 'anythink integrations list' to see available providers.");
                return 1;
            }

            // v1 only handles API key auth — OAuth needs a browser callback.
            var normalizedAuth = def.AuthType.Replace("_", "").Replace("-", "");
            if (!normalizedAuth.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            {
                Renderer.Error($"'{settings.Provider}' uses {def.AuthType} auth — only API key auth is supported in this command (yet).");
                Renderer.Info("OAuth providers must be connected via the dashboard for now.");
                return 1;
            }

            var apiKey = settings.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = AnsiConsole.Prompt(
                    new TextPrompt<string>($"[#F97316]API key for {def.DisplayName}:[/]")
                        .Secret('*')
                );
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Renderer.Error("API key is required.");
                return 1;
            }

            var name = string.IsNullOrWhiteSpace(settings.Name)
                ? $"{def.Provider} connection"
                : settings.Name;

            IntegrationConnection? created = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating connection to {def.DisplayName}...", async _ =>
                {
                    created = await client.CreateApiKeyConnectionAsync(new CreateApiKeyConnectionRequest(
                        def.Id,
                        name,
                        apiKey,
                        settings.UserConnection
                    ));
                });

            if (created is null)
            {
                Renderer.Error("Failed to create connection (no response).");
                return 1;
            }

            Renderer.Success($"Connected '{Markup.Escape(created.Name)}' to {Markup.Escape(def.DisplayName)} (id: {created.Id}).");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations test ────────────────────────────────────────────────────────

public class IntegrationConnectionIdSettings : CommandSettings
{
    [CommandArgument(0, "<CONNECTION_ID>")]
    [Description("Connection ID")]
    public string ConnectionId { get; set; } = "";
}

public class IntegrationsTestCommand : BaseCommand<IntegrationConnectionIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationConnectionIdSettings settings)
    {
        try
        {
            var client = GetClient();
            TestConnectionResult? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Testing connection {settings.ConnectionId}...", async _ =>
                {
                    result = await client.TestIntegrationConnectionAsync(settings.ConnectionId);
                });

            if (result is null)
            {
                Renderer.Error("No response from test endpoint.");
                return 1;
            }

            if (result.Success)
            {
                Renderer.Success(result.Message);
                return 0;
            }
            Renderer.Error(result.Message);
            return 1;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations enable / disable ────────────────────────────────────────────

public class IntegrationsEnableCommand : BaseCommand<IntegrationConnectionIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationConnectionIdSettings settings)
        => await ToggleAsync(settings.ConnectionId, true);

    private async Task<int> ToggleAsync(string connectionId, bool enable)
    {
        try
        {
            var client = GetClient();
            IntegrationConnection? updated = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"{(enable ? "Enabling" : "Disabling")} connection {connectionId}...", async _ =>
                {
                    updated = await client.UpdateIntegrationConnectionAsync(connectionId, new UpdateConnectionRequest(IsEnabled: enable));
                });

            if (updated is null)
            {
                Renderer.Error($"Connection {connectionId} not found.");
                return 1;
            }

            Renderer.Success($"Connection '{Markup.Escape(updated.Name)}' {(updated.IsEnabled ? "enabled" : "disabled")}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

public class IntegrationsDisableCommand : BaseCommand<IntegrationConnectionIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationConnectionIdSettings settings)
    {
        try
        {
            var client = GetClient();
            IntegrationConnection? updated = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Disabling connection {settings.ConnectionId}...", async _ =>
                {
                    updated = await client.UpdateIntegrationConnectionAsync(settings.ConnectionId, new UpdateConnectionRequest(IsEnabled: false));
                });

            if (updated is null)
            {
                Renderer.Error($"Connection {settings.ConnectionId} not found.");
                return 1;
            }

            Renderer.Success($"Connection '{Markup.Escape(updated.Name)}' disabled.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations disconnect ──────────────────────────────────────────────────

public class IntegrationDisconnectSettings : CommandSettings
{
    [CommandArgument(0, "<CONNECTION_ID>")]
    [Description("Connection ID to delete")]
    public string ConnectionId { get; set; } = "";

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt")]
    public bool Yes { get; set; }
}

public class IntegrationsDisconnectCommand : BaseCommand<IntegrationDisconnectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationDisconnectSettings settings)
    {
        try
        {
            var client = GetClient();

            if (!settings.Yes)
            {
                List<IntegrationConnection> conns = [];
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Looking up connection...", async _ => conns = await client.GetIntegrationConnectionsAsync());

                var conn = conns.FirstOrDefault(c => c.Id == settings.ConnectionId);
                if (conn is null)
                {
                    Renderer.Error($"Connection {settings.ConnectionId} not found.");
                    return 1;
                }

                Renderer.Header($"Disconnect: {settings.ConnectionId}");
                var summary = Renderer.BuildTable("Property", "Value");
                Renderer.AddRow(summary, "Provider", conn.Provider ?? conn.IntegrationDefinitionId);
                Renderer.AddRow(summary, "Name", conn.Name);
                Renderer.AddRow(summary, "Scope", conn.UserId.HasValue ? $"user {conn.UserId}" : "tenant");
                AnsiConsole.Write(summary);

                if (!AnsiConsole.Confirm("[red]Delete this connection?[/]"))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting connection {settings.ConnectionId}...", async _ =>
                {
                    await client.DeleteIntegrationConnectionAsync(settings.ConnectionId);
                });

            Renderer.Success($"Connection {settings.ConnectionId} deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations oauth status ────────────────────────────────────────────────

public class IntegrationOAuthProviderSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("Provider key (e.g. slack, google, github)")]
    public string Provider { get; set; } = "";
}

public class IntegrationsOAuthStatusCommand : BaseCommand<IntegrationOAuthProviderSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationOAuthProviderSettings settings)
    {
        try
        {
            var client = GetClient();
            IntegrationOAuthSettings? s = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching OAuth settings for '{settings.Provider}'...", async _ =>
                {
                    s = await client.GetIntegrationOAuthSettingsAsync(settings.Provider);
                });

            if (s is null)
            {
                Renderer.Error($"Provider '{settings.Provider}' not found.");
                return 1;
            }

            Renderer.Header($"OAuth status: {settings.Provider}");
            var table = Renderer.BuildTable("Property", "Value");
            Renderer.AddRow(table, "Client ID configured", s.HasClientId ? "yes" : "no");
            Renderer.AddRow(table, "Enabled", s.IsEnabled ? "yes" : "no");
            Renderer.AddRow(table, "Used for social sign-in", s.UseSocialSignIn ? "yes" : "no");
            Renderer.AddRow(table, "Add this redirect URL to the OAuth app", client.IntegrationsCallbackUrl);
            AnsiConsole.Write(table);

            if (!s.HasClientId)
                Renderer.Info("Run 'anythink integrations oauth configure <provider>' to set up the client credentials.");

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations oauth callback-url ──────────────────────────────────────────

public class IntegrationsOAuthCallbackUrlCommand : BaseCommand<EmptySettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        var client = GetClient();
        // Plain stdout — easy to capture in scripts (e.g. `anythink integrations oauth callback-url | pbcopy`).
        Console.WriteLine(client.IntegrationsCallbackUrl);
        return Task.FromResult(0);
    }
}

// ── integrations oauth configure ─────────────────────────────────────────────

public class IntegrationOAuthConfigureSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("Provider key (e.g. slack, google, github)")]
    public string Provider { get; set; } = "";

    [CommandOption("--client-id <ID>")]
    [Description("OAuth client ID for this tenant. Prompted if omitted.")]
    public string? ClientId { get; set; }

    [CommandOption("--client-secret <SECRET>")]
    [Description("OAuth client secret. Prompted (hidden) if omitted.")]
    public string? ClientSecret { get; set; }

    [CommandOption("--use-social-sign-in")]
    [Description("Also expose this provider as a social sign-in option for end users")]
    public bool UseSocialSignIn { get; set; }

    [CommandOption("--disable")]
    [Description("Save the credentials but mark the provider disabled")]
    public bool Disable { get; set; }
}

public class IntegrationsOAuthConfigureCommand : BaseCommand<IntegrationOAuthConfigureSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationOAuthConfigureSettings settings)
    {
        try
        {
            var clientId = settings.ClientId;
            if (string.IsNullOrWhiteSpace(clientId))
                clientId = AnsiConsole.Prompt(new TextPrompt<string>($"[#F97316]Client ID for {settings.Provider}:[/]"));

            var clientSecret = settings.ClientSecret;
            if (string.IsNullOrWhiteSpace(clientSecret))
                clientSecret = AnsiConsole.Prompt(new TextPrompt<string>($"[#F97316]Client secret for {settings.Provider}:[/]").Secret('*'));

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                Renderer.Error("Both client ID and client secret are required.");
                return 1;
            }

            var client = GetClient();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Saving OAuth settings for '{settings.Provider}'...", async _ =>
                {
                    await client.SetIntegrationOAuthSettingsAsync(settings.Provider, new SetOAuthSettingsRequest(
                        ClientId:        clientId,
                        ClientSecret:    clientSecret,
                        IsEnabled:       !settings.Disable,
                        UseSocialSignIn: settings.UseSocialSignIn
                    ));
                });

            Renderer.Success($"OAuth credentials saved for '{settings.Provider}'.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Next step — add this redirect URL to your OAuth app:[/]");
            AnsiConsole.WriteLine(client.IntegrationsCallbackUrl);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Once that's done, the dashboard's [#F97316]Connect[/] button (or 'anythink integrations oauth connect') will work for any user on this tenant.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── integrations oauth connect ───────────────────────────────────────────────

public class IntegrationOAuthConnectSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("Provider key (e.g. slack, google, github)")]
    public string Provider { get; set; } = "";

    [CommandOption("--name <NAME>")]
    [Description("Friendly name for this connection (default: \"<provider> connection\")")]
    public string? Name { get; set; }

    [CommandOption("--user-connection")]
    [Description("Make this a user-scoped connection (only the current user sees it). Default: tenant-wide.")]
    public bool UserConnection { get; set; }

    [CommandOption("--port <PORT>")]
    [Description("Local port for the OAuth callback (default: 8745)")]
    [DefaultValue(8745)]
    public int Port { get; set; } = 8745;

    [CommandOption("--no-open")]
    [Description("Don't try to open the browser automatically — just print the URL")]
    public bool NoOpen { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("Seconds to wait for the OAuth callback before giving up (default: 300)")]
    [DefaultValue(300)]
    public int TimeoutSeconds { get; set; } = 300;
}

public class IntegrationsOAuthConnectCommand : BaseCommand<IntegrationOAuthConnectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationOAuthConnectSettings settings)
    {
        try
        {
            var client = GetClient();

            // Look up the definition + verify it's an OAuth provider with credentials configured.
            IntegrationDefinition? def = null;
            IntegrationOAuthSettings? oauth = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Looking up '{settings.Provider}'...", async _ =>
                {
                    def = await client.GetIntegrationDefinitionAsync(settings.Provider);
                    if (def != null)
                        oauth = await client.GetIntegrationOAuthSettingsAsync(settings.Provider);
                });

            if (def is null)
            {
                Renderer.Error($"Integration '{settings.Provider}' not found.");
                return 1;
            }

            var normalized = def.AuthType.Replace("_", "").Replace("-", "");
            if (!normalized.Equals("oauth2", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals("oauth", StringComparison.OrdinalIgnoreCase))
            {
                Renderer.Error($"'{settings.Provider}' uses {def.AuthType} auth. Use 'integrations connect' for API-key providers.");
                return 1;
            }

            if (oauth is null || !oauth.HasClientId)
            {
                Renderer.Error($"OAuth client credentials are not configured for '{settings.Provider}'.");
                Renderer.Info($"Run 'anythink integrations oauth configure {settings.Provider}' first.");
                return 1;
            }

            var redirectUri = $"http://localhost:{settings.Port}/callback";

            // Generate a state token to verify the callback came from our flow.
            var expectedState = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();

            // Get the authorization URL from the API. The server returns the provider's auth URL
            // with our redirect_uri; we'll append our state on top.
            OAuthUrlResponse urlResponse;
            try
            {
                urlResponse = await client.GetIntegrationOAuthUrlAsync(settings.Provider, redirectUri);
            }
            catch (Exception ex)
            {
                Renderer.Error($"Could not generate OAuth URL: {ex.Message}");
                return 1;
            }

            var authUrl = AppendStateParam(urlResponse.Url, expectedState);

            // Bind the local listener before opening the browser, so the callback is guaranteed to land.
            HttpListener? listener;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{settings.Port}/");
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Renderer.Error($"Could not bind to localhost:{settings.Port} ({ex.Message}). Try --port <other>.");
                return 1;
            }

            try
            {
                AnsiConsole.MarkupLine($"[bold]Opening browser to authorise [#F97316]{Markup.Escape(def.DisplayName)}[/][/]");
                AnsiConsole.MarkupLine($"If it doesn't open, paste this URL into your browser:");
                AnsiConsole.WriteLine(authUrl);
                AnsiConsole.WriteLine();

                if (!settings.NoOpen)
                    TryOpenBrowser(authUrl);

                // Wait for the callback (with timeout).
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                var contextTask = listener.GetContextAsync();

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Waiting for browser callback...", async _ =>
                    {
                        var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
                        if (completed != contextTask)
                            throw new TimeoutException($"No callback received within {settings.TimeoutSeconds}s.");
                    });

                var ctx = await contextTask;
                var query = ctx.Request.QueryString;
                var code = query["code"];
                var state = query["state"];
                var error = query["error"];

                // Send a friendly response back to the browser.
                var bodyBytes = System.Text.Encoding.UTF8.GetBytes(
                    string.IsNullOrEmpty(error)
                        ? "<html><body style='font-family:system-ui;padding:48px;text-align:center'><h2>Connected.</h2><p>You can close this tab and return to the terminal.</p></body></html>"
                        : "<html><body style='font-family:system-ui;padding:48px;text-align:center'><h2>Authorisation failed.</h2><p>Return to the terminal for details.</p></body></html>"
                );
                ctx.Response.ContentType = "text/html";
                ctx.Response.OutputStream.Write(bodyBytes);
                ctx.Response.Close();

                if (!string.IsNullOrEmpty(error))
                {
                    Renderer.Error($"OAuth provider returned an error: {error}");
                    return 1;
                }

                if (string.IsNullOrEmpty(code))
                {
                    Renderer.Error("No authorisation code in callback.");
                    return 1;
                }

                // State validation — only check if the provider echoed one back. Some don't.
                if (!string.IsNullOrEmpty(state) && state != expectedState)
                {
                    Renderer.Error("State mismatch — possible CSRF. Aborting.");
                    return 1;
                }

                // Exchange the code for a connection.
                var name = string.IsNullOrWhiteSpace(settings.Name)
                    ? $"{def.Provider} connection"
                    : settings.Name;

                IntegrationConnection? connection = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Exchanging authorisation code...", async _ =>
                    {
                        connection = await client.CreateOAuthConnectionAsync(new CreateConnectionRequest(
                            IntegrationDefinitionId: def.Id,
                            Name:                    name,
                            AuthCode:                code,
                            RedirectUri:             redirectUri,
                            State:                   state,
                            IsUserConnection:        settings.UserConnection
                        ));
                    });

                if (connection is null)
                {
                    Renderer.Error("Connection creation returned no response.");
                    return 1;
                }

                Renderer.Success($"Connected '{Markup.Escape(connection.Name)}' to {Markup.Escape(def.DisplayName)} (id: {connection.Id}).");
                return 0;
            }
            finally
            {
                if (listener.IsListening) listener.Stop();
                listener.Close();
            }
        }
        catch (TimeoutException ex)
        {
            Renderer.Error(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    private static string AppendStateParam(string url, string state)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}state={Uri.EscapeDataString(state)}";
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", url);
        }
        catch
        {
            // Best-effort — the URL is already printed to stdout.
        }
    }
}

// ── integrations execute ─────────────────────────────────────────────────────

public class IntegrationExecuteSettings : CommandSettings
{
    [CommandArgument(0, "<PROVIDER>")]
    [Description("Provider key")]
    public string Provider { get; set; } = "";

    [CommandArgument(1, "<OPERATION>")]
    [Description("Operation key (see 'integrations get <provider>')")]
    public string Operation { get; set; } = "";

    [CommandOption("--input <KEY=VALUE>")]
    [Description("Input parameter as key=value. Repeatable.")]
    public string[] Inputs { get; set; } = [];

    [CommandOption("--inputs <JSON>")]
    [Description("Inputs as a JSON object, e.g. '{\"prompt\":\"hello\"}'")]
    public string? InputsJson { get; set; }

    [CommandOption("--json")]
    [Description("Print the full JSON response (default: just the content text if present)")]
    public bool Json { get; set; }
}

public class IntegrationsExecuteCommand : BaseCommand<IntegrationExecuteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IntegrationExecuteSettings settings)
    {
        try
        {
            // Build the input dictionary from --input flags and/or --inputs JSON.
            var inputs = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(settings.InputsJson))
            {
                var parsed = JsonNode.Parse(settings.InputsJson) as JsonObject;
                if (parsed is null)
                {
                    Renderer.Error("--inputs must be a JSON object.");
                    return 1;
                }
                foreach (var kv in parsed)
                    if (kv.Value is not null) inputs[kv.Key] = kv.Value;
            }

            foreach (var pair in settings.Inputs)
            {
                var idx = pair.IndexOf('=');
                if (idx < 1)
                {
                    Renderer.Error($"--input value '{pair}' is not key=value.");
                    return 1;
                }
                inputs[pair[..idx]] = pair[(idx + 1)..];
            }

            if (inputs.Count == 0)
            {
                Renderer.Error("Provide at least one --input key=value or --inputs '{...}'.");
                return 1;
            }

            var client = GetClient();
            JsonObject? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Executing {settings.Provider}/{settings.Operation}...", async _ =>
                {
                    result = await client.ExecuteIntegrationAsync(settings.Provider, new ExecuteIntegrationRequest(
                        Operation: settings.Operation,
                        Inputs:    inputs
                    ));
                });

            if (result is null)
            {
                Renderer.Error("No response.");
                return 1;
            }

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            // Default: surface the most-useful field. For Claude, this is `content`.
            // Fall through to pretty JSON if we can't find a known field.
            if (result.TryGetPropertyValue("content", out var content) && content is not null)
            {
                Console.WriteLine(content.ToString());
                return 0;
            }
            if (result.TryGetPropertyValue("output", out var output) && output is not null)
            {
                Console.WriteLine(output.ToString());
                return 0;
            }

            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
