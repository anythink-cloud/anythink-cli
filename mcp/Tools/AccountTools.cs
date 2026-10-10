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
     Description(
        "List the billing accounts (organizations) the logged-in user belongs to. " +
        "Requires a prior platform login (use the 'login' tool first). " +
        "Returns each account's id, organization name, billing email, currency, and status " +
        "(Active/Suspended/Canceled), and flags which one is currently active. " +
        "A billing account holds your projects and payment details — pick one with 'accounts_use' " +
        "before creating or listing projects.")]
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
     Description(
        "Create a new billing account (organization) to hold projects and payment details. " +
        "Requires a prior platform login (use the 'login' tool first). " +
        "The new account is automatically set as the active account, so subsequent " +
        "'projects_create' / 'projects_list' calls target it without further setup. " +
        "Returns the new account's id and name. Most users need only one account — call " +
        "'accounts_list' first to check whether a suitable one already exists.")]
    public async Task<string> AccountsCreate(
        [Description("Organization name, e.g. 'Acme Inc' — shown on invoices and in the dashboard")] string name,
        [Description("Billing email address that receives invoices and receipts")] string email,
        [Description("ISO currency for billing: 'gbp', 'usd', or 'eur'. Defaults to 'gbp'. Cannot be changed later.")] string currency = "gbp")
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
     Description(
        "Set the active billing account that project commands operate on. " +
        "Call this after 'login' when you belong to more than one account, before using " +
        "'projects_list', 'projects_create', or 'projects_use'. " +
        "Accepts a full account UUID or a unique prefix; run 'accounts_list' to see valid ids. " +
        "Returns the resolved account name and id, or an error if no account matches.")]
    public async Task<string> AccountsUse(
        [Description("Billing account id — full UUID or a unique leading prefix. Get ids from 'accounts_list'.")] string id)
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
