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
    List<ImportFlow>       Flows
);

public record ImportCollection(
    string                  Name,
    List<ImportFieldSpec>   Fields
);

public record ImportFieldSpec(
    string  Name,
    string  DatabaseType,    // already mapped to a valid Anythink db type
    string  DisplayType,     // already mapped to a valid Anythink display type
    string? Label,
    bool    IsRequired,
    bool    IsUnique,
    bool    IsIndexed
);

public record ImportFlow(
    string             Name,
    string             Trigger,           // already mapped to Anythink trigger string
    object             TriggerOptions,
    List<ImportStep>   Steps
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
    string?       OnFailureSourceId
);
