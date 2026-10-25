namespace Anythink.ProjectImporters.Supabase;

internal record SupabaseColumn(
    string Schema, string Table, string Name, string DataType, string UdtSchema, string UdtName, int? MaxLength, bool Nullable, char RelKind = 'r');

internal record SupabaseConstraint(
    char Kind, string Schema, string Table, string Name, IReadOnlyList<string> Columns,
    string? RefSchema, string? RefTable, IReadOnlyList<string> RefColumns);

internal record SupabaseEnum(string Schema, string Name, IReadOnlyList<string> Labels);

internal record SupabasePolicy(
    string Schema, string Table, string Name, string Permissive, IReadOnlyList<string> Roles,
    string Command, string? Using, string? WithCheck);

internal record SupabaseBucket(string Id, string Name, bool Public);

internal record SupabaseObject(string Bucket, string Name);

internal record SupabasePublication(string Name, int Tables);

internal record SupabaseRoutine(string Schema, string Name);

internal record SupabaseTrigger(string Schema, string Table, string Name);

internal record SupabaseKeyColumn(string Name, string TypeSchema, string TypeName);

internal record SupabaseTableRef(string Schema, string Table, IReadOnlyList<SupabaseColumn> Columns, IReadOnlyList<SupabaseKeyColumn> Key, bool Partitioned = false);

internal record SupabaseSkippedTable(string Schema, string Name, char Kind);

// Rows come back as one display cell per column, then one plain-text cell per key column to continue from.
// Read-only by design: nothing here can change the Supabase project.
internal interface ISupabaseSource
{
    string Description { get; }
    Task<IReadOnlyList<SupabaseColumn>> ListColumnsAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<SupabaseSkippedTable>> ListSkippedTablesAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<SupabaseConstraint>> ListConstraintsAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<SupabaseEnum>> ListEnumsAsync();
    Task<IReadOnlyList<SupabasePolicy>> ListPoliciesAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<string>> ListViewsAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<SupabaseRoutine>> ListRoutinesAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<SupabaseTrigger>> ListTriggersAsync(IReadOnlyList<string> schemas);
    Task<IReadOnlyList<string>> ListExtensionsAsync();
    Task<IReadOnlyList<SupabasePublication>> ListPublicationsAsync();
    Task<long> CountCronJobsAsync();
    Task<long> CountAuthUsersAsync();
    Task<IReadOnlyList<SupabaseBucket>> ListBucketsAsync();
    Task<IReadOnlyList<SupabaseObject>> ListObjectsAsync(string? afterBucket, string? afterName, int limit);
    Task<IReadOnlyList<string?[]>> ReadRowsAsync(SupabaseTableRef table, IReadOnlyList<string?>? afterKey, int limit);
}
