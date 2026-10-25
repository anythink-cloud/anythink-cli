namespace Anythink.ProjectImporters.Supabase;

internal sealed class SupabaseSecrets
{
    private readonly string[] _secrets;

    public SupabaseSecrets(params string?[] secrets) =>
        _secrets = secrets.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!)
            .SelectMany(s => new[] { s, Uri.EscapeDataString(s) }).Distinct().OrderByDescending(s => s.Length).ToArray();

    public string Redact(string text) =>
        _secrets.Aggregate(text, (current, secret) => current.Replace(secret, "[redacted]", StringComparison.Ordinal));
}
