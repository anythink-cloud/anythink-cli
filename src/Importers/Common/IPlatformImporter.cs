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
    /// May fetch flows too if <paramref name="includeFlows"/> is true.
    /// </summary>
    Task<ImportSchema> FetchSchemaAsync(bool includeFlows);
}
