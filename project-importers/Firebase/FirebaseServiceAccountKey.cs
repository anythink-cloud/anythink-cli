using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anythink.ProjectImporters.Firebase;

public sealed class FirebaseServiceAccountKey
{
    private readonly string[] _secrets;

    public string ProjectId { get; }
    internal string Json { get; }

    private FirebaseServiceAccountKey(string json, string projectId, string[] secrets)
    {
        Json = json;
        ProjectId = projectId;
        _secrets = secrets;
    }

    public static FirebaseServiceAccountKey Parse(string json)
    {
        JsonObject? key = null;
        try { key = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { }

        string? Read(string name) => key?[name] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

        if (Read("type") != "service_account" || Read("project_id") is not { } projectId ||
            Read("client_email") is null || Read("private_key") is not { } privateKey)
            throw new ImportException("The key is not a Firebase service account key file (expected type, project_id, client_email and private_key).");

        var secrets = new[] { json.Trim(), privateKey, JsonSerializer.Serialize(privateKey).Trim('"'), Read("private_key_id") }
            .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).OrderByDescending(s => s.Length).ToArray();
        return new FirebaseServiceAccountKey(json, projectId, secrets);
    }

    public string Redact(string text) =>
        _secrets.Aggregate(text, (current, secret) => current.Replace(secret, "[redacted]", StringComparison.Ordinal));

    public override string ToString() => $"service account key for {ProjectId}";
}
