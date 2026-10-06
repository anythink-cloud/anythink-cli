using AnythinkCli.Commands;

namespace AnythinkMcp.Cli;

public enum CliToolScope { Local, Internal, Hosted }

public static class CliToolPolicy
{
    private static readonly string[] ExcludedEverywhere =
        ["cli", "signup", "login", "logout", "config", "migrate"];

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
        ["projects delete"] = "Permanently delete a project and all its data. This can't be undone, so confirm with the user first."
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

    private static readonly string[] ExcludedRemotely =
        ["files upload", "workflows seed", "integrations oauth connect", "pay connect", "oauth google", "fetch", "api-keys create"];

    internal static readonly string[] HiddenRemotely =
        ["workflows create --filter-file", "workflows export --output", "data list --all"];

    private static readonly string[] FreeTextArguments =
        ["search query text", "workflows create name", "roles create name", "users invite email", "users invite first_name", "users invite last_name",
         "projects create name"];

    private static readonly string[] AutomaticFlags = ["--json", "--yes"];

    private static readonly HashSet<string> ReadOnlyVerbs =
    [
        "list", "get", "me", "status", "jobs", "step-get", "query", "similar", "audit", "payments", "methods",
        "callback-url", "api", "docs", "file-handler-example"
    ];

    private static readonly string[] ReadOnlyRemotely = ["workflows export"];

    private static readonly string[] ReadOnlyCommands = ["plans"];

    private static readonly HashSet<string> AdditiveVerbs =
    [
        "create", "add", "add-item", "step-add", "file-handler-add", "integration-add", "connect",
        "enable", "disable", "test", "rehydrate", "upload", "seed", "invite", "trigger", "execute"
    ];

    private static readonly HashSet<string> OpenWorldVerbs = ["execute", "trigger", "invite"];

    public static bool IsRemote(CliToolScope scope) => scope != CliToolScope.Local;

    public static bool IsAccountCommand(CliCommand command) => AccountCommands.Contains(command.Key);

    public static bool TakesProject(CliCommand command, CliToolScope scope) =>
        scope == CliToolScope.Hosted && !IsAccountCommand(command);

    public static bool Includes(CliCommand command, CliToolScope scope) =>
        !Matches(command.Key, ExcludedEverywhere)
        && (!Matches(command.Key, AccountGroups) || (scope == CliToolScope.Hosted && IsAccountCommand(command)))
        && (!IsRemote(scope) || !Matches(command.Key, ExcludedRemotely));

    public static string Description(CliCommand command, CliToolScope scope) =>
        scope == CliToolScope.Hosted && AccountDescriptions.TryGetValue(command.Key, out var description)
            ? description
            : command.Description;

    public static IReadOnlyList<CliParameter> Parameters(CliCommand command, CliToolScope scope) =>
        command.Parameters
            .Where(p => !AutomaticFlags.Contains(p.Token))
            .Where(p => !IsRemote(scope) || !HiddenRemotely.Contains($"{command.Key} {p.Token}"))
            .Select(p => scope == CliToolScope.Hosted && AccountParameters.TryGetValue($"{command.Key} {p.Name}", out var adapted)
                ? p with { Name = adapted.Name, Description = adapted.Description, Required = adapted.Required }
                : p)
            .ToList();

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
        !IsReadOnly(command, scope) && !AdditiveVerbs.Contains(command.Verb);

    public static bool IsOpenWorld(CliCommand command) => OpenWorldVerbs.Contains(command.Verb);

    private static bool Matches(string key, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => key == prefix || key.StartsWith(prefix + " ", StringComparison.Ordinal));
}
