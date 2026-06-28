using AnythinkCli.Auth;
using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CliProfile = AnythinkCli.Config.Profile;

namespace AnythinkCli.Commands;

// ── anythink signup ────────────────────────────────────────────────────────────

public class SignupSettings : CommandSettings
{
    [CommandOption("--first-name <NAME>")]  public string? FirstName   { get; set; }
    [CommandOption("--last-name <NAME>")]   public string? LastName    { get; set; }
    [CommandOption("--email <EMAIL>")]      public string? Email       { get; set; }
    [CommandOption("--password <PASSWORD>")] public string? Password  { get; set; }

    [CommandOption("--referral <CODE>")]
    [Description("Optional referral code")]
    public string? ReferralCode { get; set; }
}

public class SignupCommand : BasePlatformCommand<SignupSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SignupSettings settings)
    {
        Renderer.PrintWelcomeBanner();

        var firstName = settings.FirstName ?? AnsiConsole.Ask<string>("[#F97316]First name:[/]");
        var lastName  = settings.LastName  ?? AnsiConsole.Ask<string>("[#F97316]Last name:[/]");
        var email     = settings.Email     ?? AnsiConsole.Ask<string>("[#F97316]Email:[/]");
        var password  = settings.Password
            ?? AnsiConsole.Prompt(new TextPrompt<string>("[#F97316]Password:[/]").Secret());

        if (settings.Password == null)
        {
            var confirm = AnsiConsole.Prompt(new TextPrompt<string>("[#F97316]Confirm password:[/]").Secret());
            if (password != confirm) { Renderer.Error("Passwords do not match."); return 1; }
        }

        var (platformKey, platform) = ResolvePlatformContext();

        var client = new BillingClient(ConfigService.ApplyRuntimeOverrides(platform));
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Creating your account...", async _ =>
                    await client.RegisterAsync(new RegisterRequest(
                        firstName, lastName, email, password, settings.ReferralCode)));

            Renderer.Success("Account created!");
            AnsiConsole.MarkupLine("\n[yellow]Check your email for a confirmation link before logging in.[/]");
            AnsiConsole.MarkupLine($"\nOnce confirmed, run:\n  [bold #F97316]anythink login --email {email}[/]");

            SaveAndActivatePlatform(platformKey, platform);
            return 0;
        }
        catch (AnythinkException ex)
        {
            Renderer.Error($"Signup failed ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}

// ── anythink login ─────────────────────────────────────────────────────────────

public class PlatformLoginSettings : CommandSettings
{
    [CommandOption("--email <EMAIL>")]       public string? Email    { get; set; }
    [CommandOption("--password <PASSWORD>")] public string? Password { get; set; }

    // ── Direct credential options (bypasses billing API) ───────────────────────
    [CommandOption("--org-id <ORG_ID>")]
    [Description("Org/tenant ID — saves a project profile directly (no billing login needed)")]
    public string? OrgId { get; set; }

    [CommandOption("--token <JWT>")]
    [Description("JWT access token to store in the profile")]
    public string? Token { get; set; }

    [CommandOption("--api-key <KEY>")]
    [Description("API key (ak_...) to store in the profile")]
    public string? ApiKey { get; set; }

    [CommandOption("--profile <NAME>")]
    [Description("Profile name to save as (default: org ID)")]
    public string? Profile { get; set; }

    [CommandOption("--base-url <URL>")]
    [Description("Override API base URL for this profile")]
    public string? BaseUrl { get; set; }

    [CommandOption("--google")]
    [Description("Sign in with Google (opens browser)")]
    public bool Google { get; set; }
}

public class PlatformLoginCommand : BasePlatformCommand<PlatformLoginSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PlatformLoginSettings settings)
    {
        // ── Google OAuth path ──────────────────────────────────────────────────
        if (settings.Google)
            return await GoogleLogin();

        var (platformKey, platform) = ResolvePlatformContext();

        // ── Direct credential path: --org-id + (--token or --api-key) ──────────
        // Bypasses the billing API entirely — useful when you already have a JWT
        // or API key from the dashboard and just want to save a named profile.
        if (!string.IsNullOrEmpty(settings.OrgId) &&
            (!string.IsNullOrEmpty(settings.Token) || !string.IsNullOrEmpty(settings.ApiKey)))
        {
            var profileKey = settings.Profile ?? settings.OrgId;

            ConfigService.SaveProfile(profileKey, new CliProfile
            {
                OrgId          = settings.OrgId,
                AccessToken    = settings.Token,
                ApiKey          = settings.ApiKey,
                InstanceApiUrl = platform.MyAnythinkUrl,
                Alias          = profileKey,
                PlatformKey    = platformKey,
            });
            ConfigService.SetDefault(profileKey);

            Renderer.Success($"Profile [#F97316]{Markup.Escape(profileKey)}[/] saved and set as active.");
            Renderer.Info($"Org ID:   {settings.OrgId}");
            Renderer.Info($"URL:      {platform.MyAnythinkUrl}");
            Renderer.Info($"Platform: {platformKey}");
            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink entities list[/] to explore the project.");
            return 0;
        }

        // ── Platform (billing) login path: email + password ────────────────────
        if (string.IsNullOrEmpty(settings.Email))
            AnsiConsole.MarkupLine("[dim]Tip: run [bold]anythink login --google[/] to sign in with Google.[/]\n");

        var email    = settings.Email    ?? AnsiConsole.Ask<string>("[#F97316]Email:[/]");
        var password = settings.Password ?? AnsiConsole.Prompt(new TextPrompt<string>("[#F97316]Password:[/]").Secret());
        var client   = new BillingClient(ConfigService.ApplyRuntimeOverrides(platform));
        try
        {
            LoginResponse? resp = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Authenticating...", async _ =>
                    resp = await client.LoginAsync(email, password));

            platform.Token          = resp!.AccessToken;
            platform.TokenExpiresAt = resp.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(resp.ExpiresIn.Value - 30)  // 30s buffer
                : DateTime.UtcNow.AddHours(1);
            SaveAndActivatePlatform(platformKey, platform);

            Renderer.PrintWelcomeBanner(Renderer.NameFromJwt(resp!.AccessToken));

            // If --org-id (and optionally --profile) were given, also save a project profile
            // so entity/migrate commands can use the JWT without needing 'projects use'.
            if (!string.IsNullOrEmpty(settings.OrgId))
            {
                var profileKey = settings.Profile ?? settings.OrgId;
                ConfigService.SaveProfile(profileKey, new CliProfile
                {
                    OrgId          = settings.OrgId,
                    AccessToken    = resp!.AccessToken,
                    InstanceApiUrl = platform.MyAnythinkUrl,
                    Alias          = profileKey,
                    PlatformKey    = platformKey,
                });
                ConfigService.SetDefault(profileKey);
                AnsiConsole.MarkupLine($"Project profile [bold #F97316]{Markup.Escape(profileKey)}[/] saved (org: {settings.OrgId}).");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink entities list[/] to explore the project.");
            }
            else
            {
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink accounts use[/] to select a billing account, then [bold #F97316]anythink projects use[/] to connect to a project.");
            }

            return 0;
        }
        catch (AnythinkException ex)
        {
            Renderer.Error($"Login failed ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    // ── Google OAuth: browser → loopback → JWT ────────────────────────────────

    private async Task<int> GoogleLogin()
    {
        var (platformKey, platform) = ResolvePlatformContext();
        var eff = ConfigService.ApplyRuntimeOverrides(platform);

        LoginResponse tokens;
        try
        {
            AnsiConsole.MarkupLine("\n[#F97316]Opening browser for Google sign-in...[/]");
            tokens = await GoogleAuthFlow.RunAsync(eff, url =>
            {
                AnsiConsole.MarkupLine($"[dim]If it doesn't open automatically, visit:[/]\n{url}\n");
                AnsiConsole.MarkupLine("[dim]Waiting for sign-in...[/]");
            });
        }
        catch (Exception ex)
        {
            Renderer.Error(ex.Message);
            return 1;
        }

        // Persist the token straight away so login sticks even when account
        // selection is skipped (e.g. a non-interactive / agent session).
        platform.Token = tokens.AccessToken;
        platform.TokenExpiresAt = tokens.ExpiresIn.HasValue
            ? DateTime.UtcNow.AddSeconds(tokens.ExpiresIn.Value - 30)
            : DateTime.UtcNow.AddHours(1);
        SaveAndActivatePlatform(platformKey, platform);

        try
        {
            List<BillingAccount> accounts = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Loading accounts...", async _ => accounts = await new BillingClient(platform).GetAccountsAsync());

            if (accounts.Count == 1)
            {
                platform.AccountId = accounts[0].Id.ToString();
                SaveAndActivatePlatform(platformKey, platform);
                Renderer.Info($"Account: [#F97316]{Markup.Escape(accounts[0].OrganizationName)}[/]");
            }
            else if (accounts.Count > 1 && AnsiConsole.Profile.Capabilities.Interactive)
            {
                var choices = accounts.Select(a => $"{a.OrganizationName}  ({a.BillingEmail})").ToList();
                var picked = AnsiConsole.Prompt(
                    Renderer.Prompt<string>().Title("[#F97316]Select billing account:[/]").AddChoices(choices));
                platform.AccountId = accounts[choices.IndexOf(picked)].Id.ToString();
                SaveAndActivatePlatform(platformKey, platform);
            }
            else if (accounts.Count > 1)
            {
                Renderer.Info($"{accounts.Count} billing accounts found — run 'anythink accounts use <id>' to choose one.");
            }
        }
        catch (AnythinkException ex)
        {
            Renderer.Error($"Signed in, but could not load accounts: {ex.Message}");
        }

        Renderer.PrintWelcomeBanner();
        AnsiConsole.MarkupLine("Run [bold #F97316]anythink accounts use[/] to select a billing account, then [bold #F97316]anythink projects use[/] to connect to a project.");
        return 0;
    }
}

