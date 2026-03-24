using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Config;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing billing accounts.
/// Requires a platform login (use the 'login' tool first).
/// </summary>
[McpServerToolType]
public class AccountTools
{
    private readonly McpClientFactory _factory;
    public AccountTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "accounts_list"),
     Description("List all billing accounts for the logged-in user")]
    public async Task<string> AccountsList()
    {
        var client = _factory.GetBillingClient();
        var accounts = await client.GetAccountsAsync();

        return JsonSerializer.Serialize(accounts.Select(a => new
        {
            Id = a.Id,
            Name = a.OrganizationName,
            Email = a.BillingEmail,
            Currency = a.Currency,
            Status = a.Status switch { 0 => "Active", 1 => "Suspended", 2 => "Canceled", _ => $"Unknown({a.Status})" },
            IsActive = ConfigService.ResolvePlatform().AccountId == a.Id.ToString()
        }));
    }

    [McpServerTool(Name = "accounts_create"),
     Description("Create a new billing account")]
    public async Task<string> AccountsCreate(
        [Description("Organization name")] string name,
        [Description("Billing email address")] string email,
        [Description("Currency: gbp, usd, or eur (default: gbp)")] string currency = "gbp")
    {
        var client = _factory.GetBillingClient();
        var account = await client.CreateAccountAsync(
            new CreateBillingAccountRequest(name, email, currency));

        // Auto-set as active account.
        var platform = ConfigService.ResolvePlatform();
        platform.AccountId = account.Id.ToString();
        ConfigService.SavePlatform(platform);

        return JsonSerializer.Serialize(new
        {
            Id = account.Id,
            Name = account.OrganizationName,
            Message = "Account created and set as active."
        });
    }

    [McpServerTool(Name = "accounts_use"),
     Description("Set the active billing account (used for project management)")]
    public async Task<string> AccountsUse(
        [Description("Billing account ID (full UUID or prefix)")] string id)
    {
        var client = _factory.GetBillingClient();
        var accounts = await client.GetAccountsAsync();

        var match = accounts.FirstOrDefault(a =>
            a.Id.ToString().StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
            a.Id.ToString() == id);

        if (match is null)
            return $"No account found matching '{id}'. Use 'accounts_list' to see available accounts.";

        var platform = ConfigService.ResolvePlatform();
        platform.AccountId = match.Id.ToString();
        ConfigService.SavePlatform(platform);

        return $"Active account set to '{match.OrganizationName}' ({match.Id}).";
    }
}
