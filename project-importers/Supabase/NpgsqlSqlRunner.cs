using Npgsql;

namespace Anythink.ProjectImporters.Supabase;

internal sealed record SupabaseConnection(string ConnectionString, string Password, string Description)
{
    private static readonly Dictionary<string, SslMode> SslModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = SslMode.Disable,
        ["allow"] = SslMode.Allow,
        ["prefer"] = SslMode.Prefer,
        ["require"] = SslMode.Require,
        ["verify-ca"] = SslMode.VerifyCA,
        ["verify-full"] = SslMode.VerifyFull
    };

    public static SupabaseConnection Parse(string raw)
    {
        try
        {
            var builder = raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                ? FromUri(new Uri(raw))
                : new NpgsqlConnectionStringBuilder(raw);
            if (string.IsNullOrEmpty(builder.Host)) throw new ArgumentException("no host");
            builder.ApplicationName = "anythink-import";
            return new SupabaseConnection(builder.ConnectionString, builder.Password ?? "",
                $"postgres://{builder.Host}:{builder.Port}/{builder.Database}");
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or FormatException or KeyNotFoundException)
        {
            throw new ImportException("The database connection string isn't valid. Use the postgres:// URI from Supabase (Connect), with the password URL-encoded.");
        }
    }

    private static NpgsqlConnectionStringBuilder FromUri(Uri uri)
    {
        var user = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')) is { Length: > 0 } db ? db : "postgres",
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null
        };
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)))
            if (pair[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) && pair.Length == 2)
                builder.SslMode = SslModes[Uri.UnescapeDataString(pair[1])];
        return builder;
    }
}

// Thin adapter: every call is its own read-only transaction with only transaction-scoped settings, so it also holds behind transaction-mode poolers and leaves shared connections untouched.
internal sealed class NpgsqlSqlRunner(string connectionString) : ISqlRunner
{
    public async Task<IReadOnlyList<object?[]>> QueryAsync(string sql, params object?[] parameters)
    {
        ReadOnlySql.Guard(sql);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ReadOnlySql.PrepareTransactionAsync(statement => Scalar(connection, transaction, statement));

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in parameters) command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row.Select(v => v is DBNull ? null : v).ToArray());
        }
        await reader.CloseAsync();
        await transaction.RollbackAsync();
        return rows;
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, NpgsqlTransaction transaction, string statement)
    {
        await using var command = new NpgsqlCommand(statement, connection, transaction);
        return await command.ExecuteScalarAsync();
    }
}
