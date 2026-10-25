using System.Text.Json;
using System.Text.RegularExpressions;
using Anythink.ProjectImporters.Models;

namespace Anythink.ProjectImporters.N8n;

public class N8nImporter : IPlatformImporter
{
    private readonly N8nClient _client;
    private readonly string _url;

    public string PlatformName => "n8n";
    public string ConnectionSummary => _url;
    public string? SourceAuthToken => null;

    public N8nImporter(string url, string apiKey)
        : this(url, new N8nClient(url, apiKey)) { }

    internal N8nImporter(string url, N8nClient client)
    {
        _url = UrlRedaction.Redact(url);
        _client = client;
    }

    public string GetFileDownloadUrl(string sourceFileId) =>
        throw new ImportException("n8n workflows have no files to import.");

    public Task<ImportRecordPage> FetchRecordsAsync(string collectionName, int page, int pageSize) =>
        Task.FromResult(new ImportRecordPage([], 0));

    public async Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles)
    {
        var warnings = new List<string>();
        var flows = includeFlows ? Convert(await _client.GetWorkflowsAsync(), warnings) : [];
        return new ImportSchema([], flows, [], [], warnings, []);
    }

    internal static List<ImportFlow> Convert(List<N8nWorkflow> workflows, List<string> warnings)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return workflows
            .OrderBy(w => w.Id, StringComparer.Ordinal)
            .Select(w =>
            {
                var name = usedNames.Add(w.Name) ? w.Name : $"{w.Name} (#{w.Id})";
                usedNames.Add(name);
                return ConvertWorkflow(w, name, warnings);
            })
            .ToList();
    }

    private static ImportFlow ConvertWorkflow(N8nWorkflow wf, string name, List<string> warnings)
    {
        var nodes = (wf.Nodes ?? []).Where(n => !N8nFlowMapping.IsAnnotation(n)).ToList();
        var label = $"workflow '{name}'";

        var triggers = new List<WorkflowTriggerRequest>();
        var triggerNodes = nodes.Where(N8nFlowMapping.IsTrigger).ToList();
        foreach (var t in triggerNodes)
        {
            if (N8nFlowMapping.MapTriggers(t, wf.Name) is { } mapped) triggers.AddRange(mapped);
            else warnings.Add($"{label}: trigger node '{t.Name}' ({t.Type}) has no Anythink equivalent.");
        }
        if (triggers.Count == 0)
            triggers.Add(new("Manual", true, new { manual_entities = Array.Empty<string>() }));

        var stepNodes = nodes.Where(n => !N8nFlowMapping.IsTrigger(n)).ToList();
        var keys = StepKeys(stepNodes);
        var links = ReadConnections(wf.Connections);

        var needed = new SortedSet<string>(StringComparer.Ordinal);
        var steps = new List<ImportStep>();
        foreach (var node in stepNodes)
        {
            var translated = N8nFlowMapping.Translate(node);
            if (translated is null)
            {
                translated = N8nFlowMapping.Placeholder(node);
                warnings.Add($"{label}: node '{node.Name}' ({node.Type}) has no Anythink equivalent — imported as a disabled placeholder step.");
            }

            foreach (var (type, credName) in N8nFlowMapping.Credentials(node))
                needed.Add($"{credName} ({type})");

            var outputs = links.GetValueOrDefault(node.Name) ?? [];
            string? Next(int output) =>
                outputs.ElementAtOrDefault(output)?.FirstOrDefault(keys.ContainsKey);
            if (outputs.Any(o => o.Count(keys.ContainsKey) > 1))
                warnings.Add($"{label}: node '{node.Name}' fans out to several nodes — only the first link was kept.");

            var isIf = N8nFlowMapping.ShortType(node.Type) == "if";
            if (!isIf && outputs.Skip(1).Any(o => o.Any(keys.ContainsKey)))
                warnings.Add($"{label}: node '{node.Name}' has extra outputs that were not linked.");

            steps.Add(new ImportStep(
                SourceId: node.Name,
                Key: keys[node.Name],
                Name: node.Name,
                Action: translated.Action,
                IsStartStep: false,
                Description: $"Imported from n8n ({(N8nFlowMapping.ShortType(node.Type) is { Length: > 0 } s ? s : node.Type)})",
                Parameters: translated.Parameters,
                OnSuccessSourceId: Next(0),
                OnFailureSourceId: isIf ? Next(1) : null,
                NeedsManualReview: translated.NeedsManualReview,
                ReviewNote: translated.ReviewNote,
                Enabled: translated.Enabled && node.Disabled != true));
        }

        foreach (var t in triggerNodes)
            foreach (var cred in N8nFlowMapping.Credentials(t))
                needed.Add($"{cred.Name} ({cred.Type})");
        if (needed.Count > 0)
            warnings.Add($"{label} needs connections set up: {string.Join(", ", needed)}. Credentials are never imported.");

        var start = FirstStep(triggerNodes, links, keys, steps);
        if (start is not null)
            steps = steps.Select(s => s.SourceId == start ? s with { IsStartStep = true } : s).ToList();

        return new ImportFlow(name, triggers, steps);
    }

    private static string? FirstStep(
        List<N8nNode> triggerNodes, Dictionary<string, List<List<string>>> links,
        Dictionary<string, string> keys, List<ImportStep> steps)
    {
        foreach (var t in triggerNodes)
            if (links.GetValueOrDefault(t.Name)?.FirstOrDefault()?.FirstOrDefault(keys.ContainsKey) is { } first)
                return first;

        var targeted = links.Values.SelectMany(o => o).SelectMany(o => o).ToHashSet();
        return (steps.FirstOrDefault(s => !targeted.Contains(s.SourceId)) ?? steps.FirstOrDefault())?.SourceId;
    }

    private static Dictionary<string, string> StepKeys(List<N8nNode> nodes)
    {
        var keys = new Dictionary<string, string>();
        var used = new HashSet<string>();
        foreach (var node in nodes)
        {
            var baseKey = Regex.Replace(node.Name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            if (baseKey.Length == 0) baseKey = "step";
            var key = baseKey;
            for (var n = 2; !used.Add(key); n++) key = $"{baseKey}_{n}";
            keys[node.Name] = key;
        }
        return keys;
    }

    private static Dictionary<string, List<List<string>>> ReadConnections(JsonElement? connections)
    {
        var result = new Dictionary<string, List<List<string>>>();
        if (connections is not { ValueKind: JsonValueKind.Object } root) return result;

        foreach (var source in root.EnumerateObject())
        {
            if (source.Value.ValueKind != JsonValueKind.Object ||
                !source.Value.TryGetProperty("main", out var main) || main.ValueKind != JsonValueKind.Array)
                continue;

            result[source.Name] = main.EnumerateArray()
                .Select(output => output.ValueKind == JsonValueKind.Array
                    ? output.EnumerateArray()
                        .Select(l => l.ValueKind == JsonValueKind.Object && l.TryGetProperty("node", out var n) ? n.GetString() : null)
                        .OfType<string>().ToList()
                    : [])
                .ToList();
        }
        return result;
    }
}
