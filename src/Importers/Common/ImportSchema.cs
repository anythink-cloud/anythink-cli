using System.Text.Json;

namespace AnythinkCli.Importers;

// Platform-agnostic representation of what we want to import. Each platform
// importer is responsible for translating its own schema into these records,
// using Anythink's vocabulary for db_type / display_type / workflow action.
//
// The runner does not care which platform produced this — it just creates
// entities, adds fields, and (optionally) creates workflows.

public record ImportSchema(
    List<ImportCollection> Collections,
    List<ImportFlow>       Flows,
    List<ImportFile>       Files,
    List<ImportRole>       Roles
);

// A file in the source platform — describes enough metadata for the runner to
// download via the importer and re-upload to Anythink. The runner builds a
// SourceId → Anythink file ID map that the data section uses to remap
// file-typed fields in records.
public record ImportFile(
    string SourceId,        // platform-native file id (UUID for Directus, etc.)
    string FileName,
    bool   IsPublic
);

// A platform role with its effective collection-level permissions, already
// reduced from whatever multi-table model the source uses (e.g. Directus'
// role → access → policy → permission chain) and mapped to user collections
// only. The runner turns each (collection, action) into an Anythink permission
// named "<collection>:<action>".
public record ImportRole(
    string                                    Name,
    string?                                   Description,
    List<(string Collection, string Action)>  CollectionPermissions
);

public record ImportCollection(
    string                Name,
    List<ImportFieldSpec> Fields,
    bool                  IsJunction = false,     // hidden in source; back m2m relationships
    bool                  IsPublic   = false      // mapped onto Anythink entity.is_public
);

public record ImportFieldSpec(
    string  Name,
    string  DatabaseType,    // already mapped to a valid Anythink db type
    string  DisplayType,     // already mapped to a valid Anythink display type
    string? Label,
    bool    IsRequired,
    bool    IsUnique,
    bool    IsIndexed,
    string? ForeignKeyCollection = null,   // set when this field references another collection
    bool    IsFileField        = false,    // file-type fields need ID remap on data import
    JsonElement? Relationship  = null      // populated for File / m2o etc. — passed to AnyAPI as-is
);

public record ImportFlow(
    string                                  Name,
    List<AnythinkCli.Models.WorkflowTriggerRequest> Triggers,
    List<ImportStep>                        Steps
);

public record ImportStep(
    string        SourceId,         // platform-native step id (used to wire up Success/Failure links)
    string        Key,
    string        Name,
    string        Action,           // already mapped to a valid Anythink WorkflowAction value
    bool          IsStartStep,
    string?       Description,
    JsonElement?  Parameters,
    string?       OnSuccessSourceId,
    string?       OnFailureSourceId,
    bool          NeedsManualReview = false,
    string?       ReviewNote        = null
);

// A page of records from a source collection. The records are kept as raw
// JsonObjects — the runner does the field-level remapping.
public record ImportRecordPage(
    List<System.Text.Json.Nodes.JsonObject> Records,
    int?                                    TotalCount   // null when source doesn't report it
);
