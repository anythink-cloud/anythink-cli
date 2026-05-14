namespace AnythinkCli.Importers;

// Each platform we migrate from (Directus, Supabase, Strapi, n8n, ...) implements
// this interface. The interface is intentionally narrow — only fetch and translate.
// Applying the schema to Anythink is the runner's job.

public interface IPlatformImporter
{
    /// <summary>Display name (e.g. "Directus", "Supabase").</summary>
    string PlatformName { get; }

    /// <summary>Connection summary line for logging — e.g. the source URL.</summary>
    string ConnectionSummary { get; }

    /// <summary>
    /// Read the source schema and translate it into Anythink-vocabulary records.
    /// Includes flows when <paramref name="includeFlows"/> is true, file
    /// metadata when <paramref name="includeFiles"/> is true, and roles +
    /// public-access info when <paramref name="includeRoles"/> is true.
    /// </summary>
    Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles);

    /// <summary>
    /// Fetch a single page of records from a source collection.
    /// Records are raw <see cref="System.Text.Json.Nodes.JsonObject"/> so the
    /// runner can apply field-level remapping (FK ids, file references).
    /// </summary>
    Task<ImportRecordPage> FetchRecordsAsync(string collectionName, int page, int pageSize);

    /// <summary>URL the runner can use to fetch a file's raw bytes from source.</summary>
    string GetFileDownloadUrl(string sourceFileId);

    /// <summary>
    /// Bearer token to send when downloading from <see cref="GetFileDownloadUrl"/>.
    /// Null when the source doesn't require auth for file reads.
    /// </summary>
    string? SourceAuthToken { get; }
}
