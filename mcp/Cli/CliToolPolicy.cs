using AnythinkCli.Commands;

namespace AnythinkMcp.Cli;

public enum CliToolScope { Local, Internal, Hosted }

public static class CliToolPolicy
{
    private static readonly string[] ExcludedEverywhere =
        ["cli", "signup", "login", "logout", "config", "migrate", "pay apple", "pay setup"];

    private static readonly string[] AccountGroups = ["accounts", "projects", "plans"];

    internal static readonly string[] AccountCommands =
        ["accounts list", "accounts create", "plans", "projects create", "projects delete"];

    private const string AccountIdDescription =
        "Billing account id from accounts_list. Optional when you have exactly one account.";

    private static readonly Dictionary<string, string> AccountDescriptions = new()
    {
        ["accounts list"] = "List your billing accounts, with each account's id, name, email, currency, status and your access level.",
        ["accounts create"] = "Create a billing account: the organisation that owns projects and pays for them. Check accounts_list first, as most people need only one.",
        ["plans"] = "List the plans a project can be created on, with each plan's id, price and limits. Use a plan's id as plan_id in projects_create.",
        ["projects create"] = "Create a new project in a billing account. A paid plan is billed to the account, which needs a payment method. The project takes about a minute to set up, then appears in projects_list.",
        ["projects delete"] = "Permanently delete a project and all its data. This can't be undone."
    };

    private static readonly Dictionary<string, (string Name, string Description, bool Required)> AccountParameters = new()
    {
        ["projects create name"] = ("name", "Name for the new project.", true),
        ["projects create plan"] = ("plan_id", "Plan id from the plans tool.", true),
        ["projects create region"] = ("region", $"Deployment region. Defaults to {ProjectsCreateCommand.DefaultRegion}.", false),
        ["projects create description"] = ("description", "Short description of the project.", false),
        ["projects create account"] = ("account_id", AccountIdDescription, false),
        ["projects delete id"] = ("id", "Full id of the project to delete, from projects_list.", true),
        ["projects delete account"] = ("account_id", AccountIdDescription, false),
        ["accounts create name"] = ("name", "Organisation name for the account.", true),
        ["accounts create email"] = ("email", "Billing email address for invoices.", true),
        ["accounts create currency"] = ("currency", $"Currency code: gbp, usd or eur. Defaults to {AccountsCreateCommand.DefaultCurrency}.", false)
    };

    // import directus makes the server fetch a caller-supplied URL with a token (SSRF) and may read DIRECTUS_TOKEN from the host environment;
    // data import and export read and write a file path on the machine running the command, which a remote server can't share with the caller.
    private static readonly string[] ExcludedRemotely =
        ["files upload",
            "workflows seed",
            "integrations oauth connect",
            "pay connect",
            "oauth google",
            "fetch",
            "api-keys create",
            "import directus",
            "pay subscriptions delete",
            "pay subscriptions force-expire",
            "pay subscriptions relink",
            "pay subscriptions resync",
            "data import",
            "data export"];

    internal static readonly string[] HiddenRemotely =
    [
        "workflows create --filter-file",
        "workflows export --output",
        "data list --all",
        "email templates update --content",
        "email preview --content",
        "email preview --wrapper",
        "email shell update --html",
    ];

    // A plan file is written to a path the caller picks, so no tool may offer it; the tool prints the plan through its automatic --json instead.
    internal static readonly string[] HiddenEverywhere = ["import directus --plan", "import directus --force"];

    private static readonly string[] FreeTextArguments =
        [
            "search query text",
            "workflows create name",
            "roles create name",
            "users invite email",
            "users invite first_name",
            "users invite last_name",
            "projects create name",
            "menus create name",
            "menus reorder menu_ids",
            "menus reorder-items item_ids",
            "charts create name",
            "dashboards create name"
        ];

    private static readonly string[] AutomaticFlags = ["--json", "--yes"];

    private static readonly HashSet<string> ReadOnlyVerbs =
    [
        "list",
        "get",
        "me",
        "status",
        "jobs",
        "step-get",
        "query",
        "similar",
        "audit",
        "payments",
        "methods",
        "callback-url",
        "api",
        "docs",
        "file-handler-example",
        "entitlement",
        "payment-options",
        "by-user",
        "check-access",
        "events",
        "codes",
        "redemptions",
        "mine",
        "data",
        "preview",
        "platform-metrics",
        "widget-preview",
        "show",
    ];

    private static readonly string[] ReadOnlyRemotely = ["workflows export"];

    private static readonly string[] ReadOnlyCommands = ["plans"];

    private static readonly HashSet<string> AdditiveVerbs =
    [
        "create",
        "add",
        "add-item",
        "add-code",
        "add-chart",
        "step-add",
        "file-handler-add",
        "integration-add",
        "connect",
        "enable",
        "disable",
        "activate",
        "resume",
        "test",
        "rehydrate",
        "upload",
        "seed",
        "invite",
        "resend-invite",
        "trigger",
        "execute"
    ];

    // Import adds records; export only writes a local file and refuses to overwrite one without --force, so neither destroys project data.
    private static readonly string[] AdditiveCommands = ["data import", "data export", "pay offers user-code"];

    private static readonly HashSet<string> OpenWorldVerbs = ["execute", "trigger", "invite"];

    public static bool IsRemote(CliToolScope scope) => scope != CliToolScope.Local;

    public static bool IsAccountCommand(CliCommand command) => AccountCommands.Contains(command.Key);

    public static bool TakesProject(CliCommand command, CliToolScope scope) =>
        scope == CliToolScope.Hosted && !IsAccountCommand(command);

    public static bool Includes(CliCommand command, CliToolScope scope) =>
        !Matches(command.Key, ExcludedEverywhere)
        && (!Matches(command.Key, AccountGroups) || (scope == CliToolScope.Hosted && IsAccountCommand(command)))
        && (!IsRemote(scope) || !Matches(command.Key, ExcludedRemotely));

    public static string Description(CliCommand command, CliToolScope scope)
    {
        var summary = scope == CliToolScope.Hosted && AccountDescriptions.TryGetValue(command.Key, out var description)
            ? description
            : command.Description;
        var parts = new List<string> { Sentence(summary) };
        if (command.Guide.Length > 0)
            parts.Add(command.Guide);
        if (command.Parameters.Any(p => p.Token == "--json"))
            parts.Add("Returns JSON.");
        if (IsDestructive(command, scope))
            parts.Add("It changes or deletes existing data, so check with the user before running it.");
        return string.Join(' ', parts.Where(part => part.Length > 0));
    }

    public static IReadOnlyList<CliParameter> Parameters(CliCommand command, CliToolScope scope) =>
        command.Parameters
            .Where(p => !AutomaticFlags.Contains(p.Token))
            .Where(p => !HiddenEverywhere.Contains($"{command.Key} {p.Token}"))
            .Where(p => !IsRemote(scope) || !HiddenRemotely.Contains($"{command.Key} {p.Token}"))
            .Select(p => scope == CliToolScope.Hosted && AccountParameters.TryGetValue($"{command.Key} {p.Name}", out var adapted)
                ? p with { Name = adapted.Name, Description = adapted.Description, Required = adapted.Required }
                : p)
            .Select(p => WithIdSource(command, p, scope))
            .ToList();

    private static readonly Lazy<HashSet<string>>[] ToolNames =
        Enum.GetValues<CliToolScope>()
            .Select(scope => new Lazy<HashSet<string>>(() =>
                CliCommandModel.All.Where(c => Includes(c, scope)).Select(c => c.ToolName).ToHashSet()))
            .ToArray();

    private static CliParameter WithIdSource(CliCommand command, CliParameter parameter, CliToolScope scope)
    {
        if (parameter.Description.Contains("_list", StringComparison.Ordinal))
            return parameter;

        var source = IdSource(command, parameter.Name, ToolNames[(int)scope].Value);
        return source is null ? parameter : parameter with { Description = $"{Sentence(parameter.Description)} Find it with {source}.".Trim() };
    }

    private static string? IdSource(CliCommand command, string name, HashSet<string> tools)
    {
        var stem = name.EndsWith("_id", StringComparison.Ordinal) ? name[..^3] : null;
        string[] candidates = name == "id"
            ? [string.Join('_', command.Path.SkipLast(1)).Replace('-', '_') + "_list"]
            : stem is null ? [] : [$"{stem}s_list", $"{stem}_list", $"{command.Path[0].Replace('-', '_')}_{stem}s_list"];
        return candidates.FirstOrDefault(candidate => candidate != command.ToolName && tools.Contains(candidate));
    }

    private static string Sentence(string text) =>
        text.Length == 0 || text.EndsWith('.') || text.EndsWith('?') ? text : text + ".";

    public static IReadOnlyList<string> AutomaticArgs(CliCommand command) =>
        command.Parameters
            .Where(p => p.Kind == CliParameterKind.Flag && AutomaticFlags.Contains(p.Token))
            .Select(p => p.Token)
            .ToList();

    public static bool IsFreeText(CliCommand command, CliParameter parameter) =>
        FreeTextArguments.Contains($"{command.Key} {parameter.Name}");

    public static bool IsPlainPathValue(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') && !value.Contains("..", StringComparison.Ordinal);

    public static bool IsReadOnly(CliCommand command, CliToolScope scope) =>
        ReadOnlyVerbs.Contains(command.Verb) || ReadOnlyCommands.Contains(command.Key)
        || (IsRemote(scope) && ReadOnlyRemotely.Contains(command.Key));

    public static bool IsDestructive(CliCommand command, CliToolScope scope) =>
        !IsReadOnly(command, scope) && !AdditiveVerbs.Contains(command.Verb) && !AdditiveCommands.Contains(command.Key);

    public static bool IsOpenWorld(CliCommand command) => OpenWorldVerbs.Contains(command.Verb);

    private static bool Matches(string key, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => key == prefix || key.StartsWith(prefix + " ", StringComparison.Ordinal));
}
