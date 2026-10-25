using System.Text.RegularExpressions;

namespace Anythink.ProjectImporters.Supabase;

internal interface ISqlRunner
{
    Task<IReadOnlyList<object?[]>> QueryAsync(string sql, params object?[] parameters);
}

internal static partial class ReadOnlySql
{
    public const string ShowReadOnly = "SHOW transaction_read_only";

    // Transaction-scoped only: a session-level SET would outlive the transaction on a shared pooled connection.
    public static readonly IReadOnlyList<string> TransactionSetup =
    [
        "SET TRANSACTION READ ONLY",
        "SET LOCAL row_security = off",
        "SET LOCAL TimeZone = 'UTC'",
        "SET LOCAL DateStyle = 'ISO, YMD'",
        ShowReadOnly
    ];

    public static async Task PrepareTransactionAsync(Func<string, Task<object?>> run)
    {
        object? shown = null;
        foreach (var statement in TransactionSetup) shown = await run(statement);
        VerifyReadOnly(shown);
    }

    [GeneratedRegex(@"\b(insert|update|delete|merge|drop|alter|create|truncate|grant|revoke|copy|call|do|lock|vacuum|reindex|refresh|set|execute|discard|listen|notify|prepare|into|comment|analyze|cluster|reset|load)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"\b(set_config|nextval|setval|pg_advisory\w*|lo_\w+|pg_terminate_backend|pg_cancel_backend|dblink\w*|pg_read_\w+|pg_notify|pg_reload_conf|pg_sleep\w*|pg_rotate_logfile|query_to_\w+|table_to_xml\w*|schema_to_xml\w*|database_to_xml\w*|cursor_to_xml)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenFunctions();

    [GeneratedRegex(@"""(?:[^""]|"""")*""|'(?:[^']|'')*'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\$\$.*?\$\$|\$([A-Za-z_]\w*)\$.*?\$\1\$", RegexOptions.Singleline)]
    private static partial Regex DollarQuoted();

    [GeneratedRegex(@"\$[A-Za-z_]*\$")]
    private static partial Regex DollarTag();

    public static string Guard(string sql)
    {
        var bare = Quoted().Replace(DollarQuoted().Replace(sql, "''"), "''").Trim();
        var startsRight = bare.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) || bare.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase);
        if (!startsRight || bare.Contains(';') || DollarTag().IsMatch(bare) || Forbidden().IsMatch(bare) || ForbiddenFunctions().IsMatch(bare))
            throw new ImportException("Refusing to run a statement that is not a plain read-only SELECT.");
        return sql;
    }

    public static void VerifyReadOnly(object? transactionReadOnly)
    {
        if (!string.Equals(transactionReadOnly as string, "on", StringComparison.OrdinalIgnoreCase))
            throw new ImportException("The database session is not read-only, so nothing was read. Use a connection that allows read-only transactions.");
    }

    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
