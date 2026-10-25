namespace Anythink.ProjectImporters.Supabase;

internal sealed class PostgresSupabaseSource(ISqlRunner runner, string description) : ISupabaseSource
{
    internal const string ColumnsSql = """
        SELECT n.nspname::text, c.relname::text, a.attname::text,
               CASE WHEN bt.typcategory = 'A' THEN 'ARRAY' WHEN bt.typtype = 'e' THEN 'USER-DEFINED' ELSE bt.typname::text END,
               bn.nspname::text, bt.typname::text,
               CASE WHEN bt.typname IN ('varchar', 'bpchar') AND a.atttypmod > 4 THEN a.atttypmod - 4 END::int,
               NOT a.attnotnull, c.relkind::text
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        JOIN pg_type t ON t.oid = a.atttypid
        JOIN pg_type bt ON bt.oid = CASE WHEN t.typtype = 'd' THEN t.typbasetype ELSE t.oid END
        JOIN pg_namespace bn ON bn.oid = bt.typnamespace
        WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition AND n.nspname = ANY($1)
          AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e')
        ORDER BY n.nspname, c.relname, a.attnum
        """;

    internal const string SkippedTablesSql = """
        SELECT n.nspname::text, c.relname::text, c.relkind::text
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = ANY($1) AND NOT c.relispartition
          AND (c.relkind = 'f' OR (c.relkind IN ('r', 'p')
               AND EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e')))
        ORDER BY 1, 2
        """;

    internal const string ConstraintsSql = """
        SELECT con.contype::text, ns.nspname::text, cl.relname::text, con.conname::text,
               ARRAY(SELECT a.attname::text FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum ORDER BY k.ord),
               fns.nspname::text, fcl.relname::text,
               ARRAY(SELECT a.attname::text FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum ORDER BY k.ord)
        FROM pg_constraint con
        JOIN pg_class cl ON cl.oid = con.conrelid
        JOIN pg_namespace ns ON ns.oid = cl.relnamespace
        LEFT JOIN pg_class fcl ON fcl.oid = con.confrelid
        LEFT JOIN pg_namespace fns ON fns.oid = fcl.relnamespace
        WHERE con.contype IN ('p', 'u', 'f', 'c', 'x') AND ns.nspname = ANY($1)
        ORDER BY ns.nspname, cl.relname, con.conname
        """;

    internal const string EnumsSql = """
        SELECT n.nspname::text, t.typname::text, e.enumlabel::text
        FROM pg_enum e
        JOIN pg_type t ON t.oid = e.enumtypid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        ORDER BY n.nspname, t.typname, e.enumsortorder
        """;

    internal const string PoliciesSql = """
        SELECT schemaname::text, tablename::text, policyname::text, permissive::text, roles::text[], cmd::text, qual::text, with_check::text
        FROM pg_policies
        WHERE schemaname = ANY($1) OR (schemaname = 'storage' AND tablename = 'objects')
        ORDER BY schemaname, tablename, policyname
        """;

    internal const string ViewsSql = """
        SELECT schemaname::text || '.' || viewname::text FROM pg_views WHERE schemaname = ANY($1)
        UNION ALL
        SELECT schemaname::text || '.' || matviewname::text FROM pg_matviews WHERE schemaname = ANY($1)
        ORDER BY 1
        """;

    internal const string RoutinesSql = """
        SELECT n.nspname::text, p.proname::text
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = ANY($1) AND p.prokind IN ('f', 'p')
          AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
        ORDER BY 1, 2
        """;

    internal const string TriggersSql = """
        SELECT n.nspname::text, c.relname::text, t.tgname::text
        FROM pg_trigger t
        JOIN pg_class c ON c.oid = t.tgrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE NOT t.tgisinternal AND n.nspname = ANY($1)
        ORDER BY 1, 2, 3
        """;

    internal const string ExtensionsSql = "SELECT extname::text FROM pg_extension ORDER BY extname";

    internal const string PublicationsSql = """
        SELECT p.pubname::text, (SELECT count(*) FROM pg_publication_tables pt WHERE pt.pubname = p.pubname)::int
        FROM pg_publication p
        ORDER BY p.pubname
        """;

    internal const string CronJobsSql = "SELECT count(*)::bigint FROM cron.job";
    internal const string AuthUsersSql = "SELECT count(*)::bigint FROM auth.users";
    internal const string BucketsSql = "SELECT id::text, name::text, public FROM storage.buckets ORDER BY id";

    internal const string ObjectsSql = """
        SELECT bucket_id::text, name::text FROM storage.objects
        WHERE (bucket_id, name) > ($1::text, $2::text)
        ORDER BY bucket_id, name
        LIMIT $3
        """;

    public string Description => description;

    private async Task<IReadOnlyList<object?[]>> Query(string sql, params object?[] parameters) =>
        await runner.QueryAsync(ReadOnlySql.Guard(sql), parameters);

    private static string Text(object?[] row, int i) => (string)row[i]!;
    private static string? NullableText(object?[] row, int i) => row[i] as string;
    private static IReadOnlyList<string> Texts(object?[] row, int i) => row[i] is string[] array ? array : [];
    private static object ToArray(IReadOnlyList<string> schemas) => schemas.ToArray();

    public async Task<IReadOnlyList<SupabaseColumn>> ListColumnsAsync(IReadOnlyList<string> schemas) =>
        (await Query(ColumnsSql, ToArray(schemas))).Select(r => new SupabaseColumn(
            Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Text(r, 5), r[6] as int?, r[7] is true, Text(r, 8)[0])).ToList();

    public async Task<IReadOnlyList<SupabaseSkippedTable>> ListSkippedTablesAsync(IReadOnlyList<string> schemas) =>
        (await Query(SkippedTablesSql, ToArray(schemas))).Select(r => new SupabaseSkippedTable(Text(r, 0), Text(r, 1), Text(r, 2)[0])).ToList();

    public async Task<IReadOnlyList<SupabaseConstraint>> ListConstraintsAsync(IReadOnlyList<string> schemas) =>
        (await Query(ConstraintsSql, ToArray(schemas))).Select(r => new SupabaseConstraint(
            Text(r, 0)[0], Text(r, 1), Text(r, 2), Text(r, 3), Texts(r, 4),
            NullableText(r, 5), NullableText(r, 6), Texts(r, 7))).ToList();

    public async Task<IReadOnlyList<SupabaseEnum>> ListEnumsAsync() =>
        (await Query(EnumsSql)).GroupBy(r => (Schema: Text(r, 0), Name: Text(r, 1)))
            .Select(g => new SupabaseEnum(g.Key.Schema, g.Key.Name, g.Select(r => Text(r, 2)).ToList())).ToList();

    public async Task<IReadOnlyList<SupabasePolicy>> ListPoliciesAsync(IReadOnlyList<string> schemas) =>
        (await Query(PoliciesSql, ToArray(schemas))).Select(r => new SupabasePolicy(
            Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Texts(r, 4), Text(r, 5), NullableText(r, 6), NullableText(r, 7))).ToList();

    public async Task<IReadOnlyList<string>> ListViewsAsync(IReadOnlyList<string> schemas) =>
        (await Query(ViewsSql, ToArray(schemas))).Select(r => Text(r, 0)).ToList();

    public async Task<IReadOnlyList<SupabaseRoutine>> ListRoutinesAsync(IReadOnlyList<string> schemas) =>
        (await Query(RoutinesSql, ToArray(schemas))).Select(r => new SupabaseRoutine(Text(r, 0), Text(r, 1))).ToList();

    public async Task<IReadOnlyList<SupabaseTrigger>> ListTriggersAsync(IReadOnlyList<string> schemas) =>
        (await Query(TriggersSql, ToArray(schemas))).Select(r => new SupabaseTrigger(Text(r, 0), Text(r, 1), Text(r, 2))).ToList();

    public async Task<IReadOnlyList<string>> ListExtensionsAsync() =>
        (await Query(ExtensionsSql)).Select(r => Text(r, 0)).ToList();

    public async Task<IReadOnlyList<SupabasePublication>> ListPublicationsAsync() =>
        (await Query(PublicationsSql)).Select(r => new SupabasePublication(Text(r, 0), Convert.ToInt32(r[1]))).ToList();

    public async Task<long> CountCronJobsAsync() => Convert.ToInt64((await Query(CronJobsSql))[0][0]);

    public async Task<long> CountAuthUsersAsync() => Convert.ToInt64((await Query(AuthUsersSql))[0][0]);

    public async Task<IReadOnlyList<SupabaseBucket>> ListBucketsAsync() =>
        (await Query(BucketsSql)).Select(r => new SupabaseBucket(Text(r, 0), Text(r, 1), r[2] is true)).ToList();

    public async Task<IReadOnlyList<SupabaseObject>> ListObjectsAsync(string? afterBucket, string? afterName, int limit) =>
        (await Query(ObjectsSql, afterBucket ?? "", afterName ?? "", limit)).Select(r => new SupabaseObject(Text(r, 0), Text(r, 1))).ToList();

    internal static string SelectExpression(SupabaseColumn column)
    {
        var quoted = ReadOnlySql.Quote(column.Name);
        return SupabaseTypes.CellExpression(column, quoted);
    }

    internal static string RowsSql(SupabaseTableRef table, bool afterKey)
    {
        var select = string.Join(", ", table.Columns.Select(SelectExpression).Concat(table.Key.Select(k => $"{ReadOnlySql.Quote(k.Name)}::text")));
        var key = string.Join(", ", table.Key.Select(k => ReadOnlySql.Quote(k.Name)));
        var after = afterKey
            ? $" WHERE ({key}) > ({string.Join(", ", table.Key.Select((k, i) => $"${i + 1}::{ReadOnlySql.Quote(k.TypeSchema)}.{ReadOnlySql.Quote(k.TypeName)}"))})"
            : "";
        return $"SELECT {select} FROM {(table.Partitioned ? "" : "ONLY ")}{ReadOnlySql.Quote(table.Schema)}.{ReadOnlySql.Quote(table.Table)}{after} ORDER BY {key} LIMIT ${(afterKey ? table.Key.Count : 0) + 1}";
    }

    public async Task<IReadOnlyList<string?[]>> ReadRowsAsync(SupabaseTableRef table, IReadOnlyList<string?>? afterKey, int limit)
    {
        object?[] parameters = [.. (afterKey ?? []), limit];
        var rows = await Query(RowsSql(table, afterKey is not null), parameters);
        return rows.Select(r => r.Select(cell => cell as string).ToArray()).ToList();
    }
}
