namespace AnythinkMcp.Cli;

public enum CliToolScope { Local, Remote }

public static class CliToolPolicy
{
    private static readonly string[] ExcludedEverywhere =
        ["cli", "signup", "login", "logout", "config", "accounts", "projects", "plans", "migrate"];

    private static readonly string[] ExcludedRemotely =
        ["files upload", "workflows seed", "integrations oauth connect", "pay connect", "oauth google", "fetch"];

    private static readonly string[] HiddenRemotely =
    [
        "workflows create --filter-file", "workflows export --output", "api-keys create --save-as",
        "api-keys create --no-expiry-cap", "data list --all"
    ];

    private static readonly string[] FreeTextArguments =
        ["search query text", "workflows create name", "roles create name", "users invite first_name", "users invite last_name"];

    private static readonly string[] AutomaticFlags = ["--json", "--yes"];

    private static readonly HashSet<string> ReadOnlyVerbs =
    [
        "list", "get", "me", "status", "jobs", "step-get", "query", "similar", "audit", "payments", "methods",
        "callback-url", "api", "docs", "file-handler-example"
    ];

    private static readonly HashSet<string> DestructiveVerbs =
        ["delete", "remove", "revoke", "purge", "disconnect", "step-delete", "fetch", "update", "step-update"];

    private static readonly HashSet<string> OpenWorldVerbs = ["execute", "trigger", "invite"];

    public static bool Includes(CliCommand command, CliToolScope scope) =>
        !Matches(command.Key, ExcludedEverywhere)
        && (scope == CliToolScope.Local || !Matches(command.Key, ExcludedRemotely));

    public static IReadOnlyList<CliParameter> Parameters(CliCommand command, CliToolScope scope) =>
        command.Parameters
            .Where(p => !AutomaticFlags.Contains(p.Token))
            .Where(p => scope == CliToolScope.Local || !HiddenRemotely.Contains($"{command.Key} {p.Token}"))
            .ToList();

    public static IReadOnlyList<string> AutomaticArgs(CliCommand command) =>
        command.Parameters
            .Where(p => p.Kind == CliParameterKind.Flag && AutomaticFlags.Contains(p.Token))
            .Select(p => p.Token)
            .ToList();

    public static bool IsFreeText(CliCommand command, CliParameter parameter) =>
        FreeTextArguments.Contains($"{command.Key} {parameter.Name}");

    public static bool HasPathSyntax(string value) =>
        value.AsSpan().IndexOfAny('/', '?', '#') >= 0 || value.Contains("..", StringComparison.Ordinal);

    public static bool IsReadOnly(CliCommand command) => ReadOnlyVerbs.Contains(command.Verb);

    public static bool IsDestructive(CliCommand command) => DestructiveVerbs.Contains(command.Verb);

    public static bool IsOpenWorld(CliCommand command) => OpenWorldVerbs.Contains(command.Verb);

    private static bool Matches(string key, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => key == prefix || key.StartsWith(prefix + " ", StringComparison.Ordinal));
}
