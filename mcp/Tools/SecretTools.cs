using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing project secrets.
/// Wraps AnythinkClient secret methods.
///
/// Note: secrets are write-only by design — the API never returns secret values,
/// only metadata (key name, created/updated timestamps, authorised users).
/// </summary>
[McpServerToolType]
public class SecretTools
{
    private readonly McpClientFactory _factory;
    public SecretTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "secrets_list"), Description("List all secrets (metadata only, values are never returned)")]
    public async Task<string> ListSecrets()
    {
        var secrets = await _factory.GetClient().GetSecretsAsync();
        return JsonSerializer.Serialize(secrets.Select(s => new
        {
            s.Id, s.Key, s.CreatedAt, s.UpdatedAt
        }));
    }

    [McpServerTool(Name = "secrets_create"), Description("Create a new secret")]
    public async Task<string> CreateSecret(
        [Description("Secret key name (e.g. STRIPE_SECRET_KEY)")] string key,
        [Description("Secret value")] string value)
    {
        var secret = await _factory.GetClient().CreateSecretAsync(
            new CreateSecretRequest(key, value));
        return JsonSerializer.Serialize(new { secret.Id, secret.Key, secret.CreatedAt });
    }

    [McpServerTool(Name = "secrets_update"), Description("Update an existing secret's value")]
    public async Task<string> UpdateSecret(
        [Description("Secret key name")] string key,
        [Description("New secret value")] string value)
    {
        await _factory.GetClient().UpdateSecretAsync(key,
            new UpdateSecretRequest(value));
        return $"Secret '{key}' updated.";
    }

    [McpServerTool(Name = "secrets_delete"), Description("Delete a secret by key name")]
    public async Task<string> DeleteSecret(
        [Description("Secret key name")] string key)
    {
        await _factory.GetClient().DeleteSecretAsync(key);
        return $"Secret '{key}' deleted.";
    }
}
