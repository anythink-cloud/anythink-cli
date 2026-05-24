using AnythinkCli.Commands;
using AnythinkCli.Config;
using Spectre.Console;
using Spectre.Console.Cli;

// Load .env from the current working directory if present.
var dotEnvPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(dotEnvPath))
{
    foreach (var line in File.ReadAllLines(dotEnvPath))
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
        var idx = trimmed.IndexOf('=');
        var key = trimmed[..idx].Trim();
        var val = trimmed[(idx + 1)..].Trim().Trim('"').Trim('\'');
        if (!string.IsNullOrEmpty(key) && Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, val);
    }
}

// ── Pre-parse --profile before Spectre.Console sees args ─────────────────────
// Strips "--profile <name>" (or "-p <name>") from the args array so Spectre
// doesn't reject it as an unknown option, and stores the value in ProfileContext
// for BaseCommand.GetClient() to use.
{
    var argList = args.ToList();
    for (var i = 0; i < argList.Count - 1; i++)
    {
        if (argList[i] is "--profile" or "-p")
        {
            ProfileContext.Current = argList[i + 1];
            argList.RemoveRange(i, 2);
            break;
        }
    }
    args = [.. argList];
}

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("anythink");
    config.SetApplicationVersion("1.0.0");

    config.AddExample("signup");
    config.AddExample("login");
    config.AddExample("accounts create --name \"My Company\"");
    config.AddExample("projects create \"My App\" --region lon1");
    config.AddExample("projects use my-app");
    config.AddExample("migrate --from project-staging --to project-prod");
    config.AddExample("entities list");
    config.AddExample("fields add products price --type float --required");
    config.AddExample("workflows trigger 76");
    config.AddExample("docs", "--json");
    config.AddExample("oauth", "google", "configure");

    // ── New user / onboarding ─────────────────────────────────────────────────

    config.AddCommand<SignupCommand>("signup")
        .WithDescription("Create a new Anythink account")
        .WithExample("signup")
        .WithExample("signup", "--email", "you@example.com");

    // ── Auth ──────────────────────────────────────────────────────────────────

    config.AddCommand<PlatformLoginCommand>("login")
        .WithDescription("Log in to the Anythink platform (billing + project management)")
        .WithExample("login")
        .WithExample("login", "--email", "you@example.com");

    config.AddCommand<LogoutCommand>("logout")
        .WithDescription("Remove saved credentials for a project profile");

    // ── Config ────────────────────────────────────────────────────────────────

    config.AddBranch("config", cfg =>
    {
        cfg.SetDescription("View and manage CLI configuration");

        cfg.AddCommand<ConfigShowCommand>("show")
            .WithDescription("List all configured profiles and platform settings");

        cfg.AddCommand<ConfigUseCommand>("use")
            .WithDescription("Set the active project profile")
            .WithExample("config", "use", "my-project");

        cfg.AddCommand<ConfigRemoveCommand>("remove")
            .WithDescription("Remove a project profile");

        cfg.AddCommand<ConfigResetCommand>("reset")
            .WithDescription("Reset CLI configuration to default settings");
    });

    // ── Plans ─────────────────────────────────────────────────────────────────

    config.AddCommand<PlansListCommand>("plans")
        .WithDescription("List available Anythink plans")
        .WithExample("plans")
        .WithExample("plans", "--json");

    // ── Accounts (billing) ────────────────────────────────────────────────────

    config.AddBranch("accounts", acc =>
    {
        acc.SetDescription("Manage billing accounts");

        acc.AddCommand<AccountsListCommand>("list")
            .WithDescription("List your billing accounts");

        acc.AddCommand<AccountsCreateCommand>("create")
            .WithDescription("Create a new billing account")
            .WithExample("accounts", "create", "--name", "Acme Ltd", "--email", "billing@acme.com");

        acc.AddCommand<AccountsUseCommand>("use")
            .WithDescription("Set the active billing account")
            .WithExample("accounts", "use", "a1b2c3d4");
    });

    // ── Projects ──────────────────────────────────────────────────────────────

    config.AddBranch("projects", proj =>
    {
        proj.SetDescription("Create and manage Anythink projects");

        proj.AddCommand<ProjectsListCommand>("list")
            .WithDescription("List projects in the active billing account");

        proj.AddCommand<ProjectsCreateCommand>("create")
            .WithDescription("Create a new project")
            .WithExample("projects", "create", "\"My App\"", "--region", "lon1")
            .WithExample("projects", "create", "\"My App\"", "--plan", "<plan-id>", "--region", "lon1");

        proj.AddCommand<ProjectsUseCommand>("use")
            .WithDescription("Connect to a project (saves it as the active profile)")
            .WithExample("projects", "use", "a1b2c3d4");

        proj.AddCommand<ProjectsDeleteCommand>("delete")
            .WithDescription("Delete a project")
            .WithExample("projects", "delete", "a1b2c3d4", "--yes");
    });

    // ── Schema migration ──────────────────────────────────────────────────────

    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Copy entities + fields from one project profile to another")
        .WithExample("migrate", "--from", "project-staging", "--to", "project-prod")
        .WithExample("migrate", "--from", "project-staging", "--to", "project-prod", "--dry-run");

    // ── Entities ──────────────────────────────────────────────────────────────

    config.AddBranch("entities", entities =>
    {
        entities.SetDescription("Manage entities (database tables) in the active project");

        entities.AddCommand<EntitiesListCommand>("list")
            .WithDescription("List all entities");

        entities.AddCommand<EntitiesGetCommand>("get")
            .WithDescription("Get entity details and fields")
            .WithExample("entities", "get", "customers");

        entities.AddCommand<EntitiesCreateCommand>("create")
            .WithDescription("Create a new entity")
            .WithExample("entities", "create", "orders", "--rls");

        entities.AddCommand<EntitiesUpdateCommand>("update")
            .WithDescription("Update entity settings");

        entities.AddCommand<EntitiesDeleteCommand>("delete")
            .WithDescription("Delete an entity and all its data")
            .WithExample("entities", "delete", "temp_data", "--yes");
    });

    // ── Fields ────────────────────────────────────────────────────────────────

    config.AddBranch("fields", fields =>
    {
        fields.SetDescription("Manage fields on an entity");

        fields.AddCommand<FieldsListCommand>("list")
            .WithDescription("List fields on an entity")
            .WithExample("fields", "list", "customers");

        fields.AddCommand<FieldsAddCommand>("add")
            .WithDescription("Add a field to an entity")
            .WithExample("fields", "add", "customers", "email", "--type", "varchar", "--unique", "--required");

        fields.AddCommand<FieldsUpdateCommand>("update")
            .WithDescription("Update properties of an existing field")
            .WithExample("fields", "update", "doc_page", "status", "--public", "--searchable")
            .WithExample("fields", "update", "products", "description", "--display", "rich-text");

        fields.AddCommand<FieldsDeleteCommand>("delete")
            .WithDescription("Delete a field from an entity")
            .WithExample("fields", "delete", "customers", "1234", "--yes");
    });

    // ── Workflows ─────────────────────────────────────────────────────────────

    config.AddBranch("workflows", wf =>
    {
        wf.SetDescription("Manage automation workflows");

        wf.AddCommand<WorkflowsListCommand>("list")
            .WithDescription("List all workflows");

        wf.AddCommand<WorkflowsGetCommand>("get")
            .WithDescription("Get workflow details and steps")
            .WithExample("workflows", "get", "76");

        wf.AddCommand<WorkflowsJobsCommand>("jobs")
            .WithDescription("View job history for a workflow")
            .WithExample("workflows", "jobs", "31");

        wf.AddCommand<WorkflowsStepGetCommand>("step-get")
            .WithDescription("View step details including parameters")
            .WithExample("workflows", "step-get", "31", "8");

        wf.AddCommand<WorkflowsStepAddCommand>("step-add")
            .WithDescription("Add a step to a workflow")
            .WithExample("workflows", "step-add", "31", "parse_response", "--action", "RunScript", "--params", "{\"script\":\"return {}\"}");

        wf.AddCommand<WorkflowsStepUpdateCommand>("step-update")
            .WithDescription("Update step parameters and properties")
            .WithExample("workflows", "step-update", "31", "8", "--params", "{\"entity\":\"mental_edge_answers\"}");

        wf.AddCommand<WorkflowsCreateCommand>("create")
            .WithDescription("Create a new workflow")
            .WithExample("workflows", "create", "daily-sync", "--trigger", "Timed", "--cron", "0 6 * * *");

        wf.AddCommand<WorkflowsUpdateCommand>("update")
            .WithDescription("Update a workflow's name or description")
            .WithExample("workflows", "update", "31", "--name", "Generate Mental Edge Report");

        wf.AddCommand<WorkflowsEnableCommand>("enable")
            .WithDescription("Enable a workflow");

        wf.AddCommand<WorkflowsDisableCommand>("disable")
            .WithDescription("Disable a workflow");

        wf.AddCommand<WorkflowsTriggerCommand>("trigger")
            .WithDescription("Manually trigger a workflow")
            .WithExample("workflows", "trigger", "76");

        wf.AddCommand<WorkflowsDeleteCommand>("delete")
            .WithDescription("Delete a workflow");

        wf.AddCommand<WorkflowsStepLinkCommand>("step-link")
            .WithDescription("Link workflow steps together (set on-success/on-failure)")
            .WithExample("workflows", "step-link", "31", "8", "--on-success", "9");

        wf.AddCommand<WorkflowsStepDeleteCommand>("step-delete")
            .WithDescription("Delete a step from a workflow (warns if other steps link to it)")
            .WithExample("workflows", "step-delete", "31", "8", "--yes");
    });

    // ── Data ──────────────────────────────────────────────────────────────────

    config.AddBranch("data", data =>
    {
        data.SetDescription("CRUD operations on entity records");

        data.AddCommand<DataListCommand>("list")
            .WithDescription("List records in an entity")
            .WithExample("data", "list", "blog_posts", "--limit", "10");

        data.AddCommand<DataGetCommand>("get")
            .WithDescription("Get a single record by ID")
            .WithExample("data", "get", "blog_posts", "42");

        data.AddCommand<DataCreateCommand>("create")
            .WithDescription("Create a new record")
            .WithExample("data", "create", "blog_posts", "--data", "{\"title\":\"Hello World\"}");

        data.AddCommand<DataUpdateCommand>("update")
            .WithDescription("Update a record")
            .WithExample("data", "update", "blog_posts", "42", "--data", "{\"status\":\"approved\"}");

        data.AddCommand<DataDeleteCommand>("delete")
            .WithDescription("Delete a record")
            .WithExample("data", "delete", "blog_posts", "42", "--yes");

        data.AddCommand<DataRlsCommand>("rls")
            .WithDescription("View or set RLS (row-level security) user access on a record")
            .WithExample("data", "rls", "completed_workouts", "3")
            .WithExample("data", "rls", "completed_workouts", "3", "--user", "72");
    });

    // ── Users ─────────────────────────────────────────────────────────────────

    config.AddBranch("users", users =>
    {
        users.SetDescription("Manage users in the active project");

        users.AddCommand<UsersListCommand>("list")
            .WithDescription("List all users");

        users.AddCommand<UsersMeCommand>("me")
            .WithDescription("Show the currently authenticated user");

        users.AddCommand<UsersGetCommand>("get")
            .WithDescription("Get a user by ID")
            .WithExample("users", "get", "42");

        users.AddCommand<UsersInviteCommand>("invite")
            .WithDescription("Create a user and send an invitation email")
            .WithExample("users", "invite", "alice@example.com", "Alice", "Smith")
            .WithExample("users", "invite", "alice@example.com", "Alice", "Smith", "--role-id", "3");

        users.AddCommand<UsersDeleteCommand>("delete")
            .WithDescription("Delete a user by ID")
            .WithExample("users", "delete", "42", "--yes");
    });

    // ── Files ─────────────────────────────────────────────────────────────────

    config.AddBranch("files", files =>
    {
        files.SetDescription("Manage uploaded files in the active project");

        files.AddCommand<FilesListCommand>("list")
            .WithDescription("List uploaded files")
            .WithExample("files", "list", "--page", "1", "--limit", "50");

        files.AddCommand<FilesGetCommand>("get")
            .WithDescription("Get file metadata by ID")
            .WithExample("files", "get", "12");

        files.AddCommand<FilesUploadCommand>("upload")
            .WithDescription("Upload a file")
            .WithExample("files", "upload", "logo.png")
            .WithExample("files", "upload", "logo.png", "--public");

        files.AddCommand<FilesDeleteCommand>("delete")
            .WithDescription("Delete a file by ID")
            .WithExample("files", "delete", "12", "--yes");
    });

    // ── Roles ─────────────────────────────────────────────────────────────────

    config.AddBranch("roles", roles =>
    {
        roles.SetDescription("Manage roles in the active project");

        roles.AddCommand<RolesListCommand>("list")
            .WithDescription("List all roles");

        roles.AddCommand<RolesCreateCommand>("create")
            .WithDescription("Create a new role")
            .WithExample("roles", "create", "editor", "--description", "Can edit content");

        roles.AddCommand<RolesDeleteCommand>("delete")
            .WithDescription("Delete a role by ID")
            .WithExample("roles", "delete", "5", "--yes");

        roles.AddBranch("permissions", perms =>
        {
            perms.SetDescription("Manage role permissions");

            perms.AddCommand<RolesPermissionsListCommand>("list")
                .WithDescription("List entity permissions for a role")
                .WithExample("roles", "permissions", "list", "239");

            perms.AddCommand<RolesPermissionsAddCommand>("add")
                .WithDescription("Add entity permissions to a role")
                .WithExample("roles", "permissions", "add", "239", "badges", "--actions", "read,create");
        });
    });

    // ── Menus ─────────────────────────────────────────────────────────────────

    config.AddBranch("menus", menus =>
    {
        menus.SetDescription("Manage dashboard menus in the active project");

        menus.AddCommand<MenusListCommand>("list")
            .WithDescription("List all menus and their items");

        menus.AddCommand<MenuAddItemCommand>("add-item")
            .WithDescription("Add a menu item for an entity")
            .WithExample("menus", "add-item", "250", "badges", "--icon", "Award", "--parent", "168");
    });

    // ── Pay ───────────────────────────────────────────────────────────────────

    config.AddBranch("pay", pay =>
    {
        pay.SetDescription("Manage Anythink Pay (Stripe Connect, payments, methods)");

        pay.AddCommand<PayStatusCommand>("status")
            .WithDescription("Show Stripe Connect account status");

        pay.AddCommand<PayConnectCommand>("connect")
            .WithDescription("Set up a Stripe Connect account and start onboarding");

        pay.AddCommand<PayPaymentsCommand>("payments")
            .WithDescription("List recent payments")
            .WithExample("pay", "payments", "--page", "1", "--limit", "50");

        pay.AddCommand<PayMethodsCommand>("methods")
            .WithDescription("List saved payment methods");
    });

    // ── API Keys ──────────────────────────────────────────────────────────────

    config.AddBranch("api-keys", apiKeys =>
    {
        apiKeys.SetDescription("Manage API keys for programmatic access (CI, scripts, integrations)");

        apiKeys.AddCommand<ApiKeysListCommand>("list")
            .WithDescription("List API keys for the current user");

        apiKeys.AddCommand<ApiKeysCreateCommand>("create")
            .WithDescription("Create an API key. The raw key is shown once and never retrievable.")
            .WithExample("api-keys", "create", "ci-deploy", "--permissions", "data:read,data:create", "--expires-in", "90")
            .WithExample("api-keys", "create", "scraper", "--permissions", "data:read", "--save-as", "scraper-bot");

        apiKeys.AddCommand<ApiKeysRevokeCommand>("revoke")
            .WithDescription("Revoke an API key by ID")
            .WithExample("api-keys", "revoke", "42");
    });

    // ── Integrations ──────────────────────────────────────────────────────────

    config.AddBranch("integrations", integ =>
    {
        integ.SetDescription("Manage integrations (catalog and active connections)");

        integ.AddCommand<IntegrationsListCommand>("list")
            .WithDescription("List available integration providers");

        integ.AddCommand<IntegrationsGetCommand>("get")
            .WithDescription("Show details and operations for one provider")
            .WithExample("integrations", "get", "claude");

        integ.AddBranch("connections", conns =>
        {
            conns.SetDescription("Manage integration connections");

            conns.AddCommand<IntegrationsConnectionsListCommand>("list")
                .WithDescription("List your integration connections")
                .WithExample("integrations", "connections", "list", "--provider", "claude");
        });

        integ.AddCommand<IntegrationsConnectCommand>("connect")
            .WithDescription("Connect to a provider with an API key (prompts for the key if not given)")
            .WithExample("integrations", "connect", "claude", "--name", "main")
            .WithExample("integrations", "connect", "openai", "--user-connection");

        integ.AddBranch("oauth", oauth =>
        {
            oauth.SetDescription("Manage OAuth provider configuration and connect via the browser");

            oauth.AddCommand<IntegrationsOAuthStatusCommand>("status")
                .WithDescription("Show OAuth client setup status for a provider")
                .WithExample("integrations", "oauth", "status", "slack");

            oauth.AddCommand<IntegrationsOAuthCallbackUrlCommand>("callback-url")
                .WithDescription("Print the redirect URL to add to OAuth app configurations (same for every provider)")
                .WithExample("integrations", "oauth", "callback-url");

            oauth.AddCommand<IntegrationsOAuthConfigureCommand>("configure")
                .WithDescription("Set the OAuth client ID + secret for a provider")
                .WithExample("integrations", "oauth", "configure", "slack");

            oauth.AddCommand<IntegrationsOAuthConnectCommand>("connect")
                .WithDescription("Connect via the browser OAuth flow (starts a local listener)")
                .WithExample("integrations", "oauth", "connect", "slack", "--name", "main");
        });

        integ.AddCommand<IntegrationsTestCommand>("test")
            .WithDescription("Test a connection")
            .WithExample("integrations", "test", "<connection-id>");

        integ.AddCommand<IntegrationsEnableCommand>("enable")
            .WithDescription("Enable a connection")
            .WithExample("integrations", "enable", "<connection-id>");

        integ.AddCommand<IntegrationsDisableCommand>("disable")
            .WithDescription("Disable a connection")
            .WithExample("integrations", "disable", "<connection-id>");

        integ.AddCommand<IntegrationsDisconnectCommand>("disconnect")
            .WithDescription("Delete a connection")
            .WithExample("integrations", "disconnect", "<connection-id>", "--yes");

        integ.AddCommand<IntegrationsExecuteCommand>("execute")
            .WithDescription("Run an operation on a connected provider")
            .WithExample("integrations", "execute", "claude", "generate-text", "--input", "prompt=Tell me a haiku")
            .WithExample("integrations", "execute", "claude", "generate-text", "--inputs", "{\"prompt\":\"hi\",\"model\":\"claude-3-haiku-20240307\"}");
    });

    // ── Secrets ───────────────────────────────────────────────────────────────

    config.AddBranch("secrets", secrets =>
    {
        secrets.SetDescription("Manage project secrets (API keys, tokens — values are encrypted and never displayed)");

        secrets.AddCommand<SecretsListCommand>("list")
            .WithDescription("List all secret keys (metadata only, values never shown)");

        secrets.AddCommand<SecretsCreateCommand>("create")
            .WithDescription("Store a new secret value")
            .WithExample("secrets", "create", "CLAUDE_API_KEY")
            .WithExample("secrets", "create", "STRIPE_SECRET_KEY");

        secrets.AddCommand<SecretsUpdateCommand>("update")
            .WithDescription("Rotate (overwrite) an existing secret value")
            .WithExample("secrets", "update", "CLAUDE_API_KEY");

        secrets.AddCommand<SecretsDeleteCommand>("delete")
            .WithDescription("Delete a secret")
            .WithExample("secrets", "delete", "CLAUDE_API_KEY", "--yes");
    });

    // ── OAuth / Social Auth ───────────────────────────────────────────────────

    config.AddBranch("oauth", oauth =>
    {
        oauth.SetDescription("Manage OAuth / social sign-in providers for the active project");

        oauth.AddBranch("google", google =>
        {
            google.SetDescription("Configure Google OAuth for user sign-in");

            google.AddCommand<OAuthGoogleStatusCommand>("status")
                .WithDescription("Show Google OAuth configuration status")
                .WithExample("oauth", "google", "status");

            google.AddCommand<OAuthGoogleConfigureCommand>("configure")
                .WithDescription("Set Google OAuth client ID and secret")
                .WithExample("oauth", "google", "configure");
        });
    });

    // ── Fetch (raw API call) ──────────────────────────────────────────────────

    config.AddCommand<FetchCommand>("fetch")
        .WithDescription("Make an authenticated API request to the active project")
        .WithExample("fetch", "/integrations/definitions")
        .WithExample("fetch", "/integrations/definitions/slack/fields/channel/options")
        .WithExample("fetch", "/integrations/connections", "--method", "POST", "--body", "{\"name\":\"test\"}");

    // ── API explorer ──────────────────────────────────────────────────────────

    config.AddCommand<ApiListCommand>("api")
        .WithDescription("List all API endpoints (platform + dynamically generated entity routes)")
        .WithExample("api")
        .WithExample("api", "--json");

    // ── Docs ──────────────────────────────────────────────────────────────────

    config.AddCommand<DocsCommand>("docs")
        .WithDescription("Print the full CLI reference (markdown or --json for AI/tooling consumption)")
        .WithExample("docs")
        .WithExample("docs", "--json");
});

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Fatal error:[/] {Markup.Escape(ex.Message)}");
    return 1;
}
