namespace AnythinkMcp.Cli;

public enum CliToolScope { Local, Internal, Hosted }

public static class CliToolPolicy
{
    private static readonly string[] ExcludedEverywhere =
        ["cli", "signup", "login", "logout", "config", "accounts", "projects", "plans", "migrate"];

    private static readonly string[] ExcludedRemotely =
        ["files upload", "workflows seed", "integrations oauth connect", "pay connect", "oauth google", "fetch", "api-keys create"];

    internal static readonly string[] HiddenRemotely =
        [
            "workflows create --filter-file",
            "workflows export --output",
            "data list --all",
            "email templates update --content",
            "email preview --content",
            "email preview --wrapper",
            "email shell update --html"
        ];

    private static readonly string[] FreeTextArguments =
        ["search query text", "workflows create name", "roles create name", "users invite email", "users invite first_name", "users invite last_name", "charts create name", "dashboards create name"];

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
        "show",
        "preview",
        "mine",
        "data",
        "preview",
        "platform-metrics",
        "widget-preview"
    ];

    private static readonly string[] ReadOnlyRemotely = ["workflows export"];

    private static readonly HashSet<string> AdditiveVerbs =
    [
        "create",
        "add",
        "add-item",
        "add-chart",
        "step-add",
        "file-handler-add",
        "integration-add",
        "connect",
        "enable",
        "disable",
        "test",
        "rehydrate",
        "upload",
        "seed",
        "invite",
        "trigger",
        "execute"
    ];

    private static readonly HashSet<string> OpenWorldVerbs = ["execute", "trigger", "invite"];

    public static bool IsRemote(CliToolScope scope) => scope != CliToolScope.Local;

    public static bool Includes(CliCommand command, CliToolScope scope) =>
        !Matches(command.Key, ExcludedEverywhere)
        && (!IsRemote(scope) || !Matches(command.Key, ExcludedRemotely));

    public static IReadOnlyList<CliParameter> Parameters(CliCommand command, CliToolScope scope) =>
        command.Parameters
            .Where(p => !AutomaticFlags.Contains(p.Token))
            .Where(p => !IsRemote(scope) || !HiddenRemotely.Contains($"{command.Key} {p.Token}"))
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
        ReadOnlyVerbs.Contains(command.Verb) || (IsRemote(scope) && ReadOnlyRemotely.Contains(command.Key));

    public static bool IsDestructive(CliCommand command, CliToolScope scope) =>
        !IsReadOnly(command, scope) && !AdditiveVerbs.Contains(command.Verb);

    public static bool IsOpenWorld(CliCommand command) => OpenWorldVerbs.Contains(command.Verb);

    private static bool Matches(string key, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => key == prefix || key.StartsWith(prefix + " ", StringComparison.Ordinal));
}
