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
        AnsiConsole.Write(new FigletText("Anythink").Color(new Color(249, 115, 22)));
        AnsiConsole.MarkupLine("[dim]The BaaS platform for builders[/]\n");

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

        // Resolve environment from ANYTHINK_ENV (set by --env pre-processing or directly),
        // then individual URL overrides, then fall back to production defaults.
        var envName = Env("ANYTHINK_ENV");
        var (defApi, defBilling, defOrgId) = ApiDefaults.ForEnv(envName ?? "prod");

        var platform = new PlatformConfig
        {
            ApiUrl     = Env("ANYTHINK_PLATFORM_API_URL") ?? defApi,
            BillingUrl = Env("ANYTHINK_BILLING_URL")      ?? defBilling,
            OrgId      = Env("ANYTHINK_PLATFORM_ORG_ID")  ?? defOrgId ?? ""
        };

        if (string.IsNullOrEmpty(platform.OrgId))
            platform.OrgId = AnsiConsole.Ask<string>("[#F97316]Platform org ID:[/]");

        var client = new BillingClient(platform);
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Creating your account...", async _ =>
                    await client.RegisterAsync(new RegisterRequest(
                        firstName, lastName, email, password, settings.ReferralCode)));

            Renderer.Success("Account created!");
            AnsiConsole.MarkupLine("\n[yellow]Check your email for a confirmation link before logging in.[/]");
            AnsiConsole.MarkupLine($"\nOnce confirmed, run:\n  [bold #F97316]anythink login --email {email}[/]");

            SavePlatform(platform);   // save URLs, no token yet
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

        // ── Direct credential path: --org-id + (--token or --api-key) ──────────
        // Bypasses the billing API entirely — useful when you already have a JWT
        // or API key from the dashboard and just want to save a named profile.
        if (!string.IsNullOrEmpty(settings.OrgId) &&
            (!string.IsNullOrEmpty(settings.Token) || !string.IsNullOrEmpty(settings.ApiKey)))
        {
            var envName = Env("ANYTHINK_ENV");
            var (defApi, _, _) = envName != null
                ? ApiDefaults.ForEnv(envName)
                : (ApiDefaults.ProdApi, (string?)null, (string?)null);

            var baseUrl    = Env("ANYTHINK_PLATFORM_API_URL") ?? settings.BaseUrl ?? defApi;
            var profileKey = settings.Profile ?? settings.OrgId;

            ConfigService.SaveProfile(profileKey, new CliProfile
            {
                OrgId       = settings.OrgId,
                AccessToken = settings.Token,
                ApiKey      = settings.ApiKey,
                BaseUrl     = baseUrl,
                Alias       = profileKey
            });
            ConfigService.SetDefault(profileKey);

            Renderer.Success($"Profile [#F97316]{Markup.Escape(profileKey)}[/] saved and set as active.");
            Renderer.Info($"Org ID: {settings.OrgId}");
            Renderer.Info($"URL:    {baseUrl}");
            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink entities list[/] to explore the project.");
            return 0;
        }

        // ── Platform (billing) login path: email + password ────────────────────
        // Resolve environment from ANYTHINK_ENV (set by --env pre-processing or directly),
        // then individual URL overrides, then saved config, then production defaults.
        var envNameBilling = Env("ANYTHINK_ENV");
        var (defApiBilling, defBilling, defOrgId) = ApiDefaults.ForEnv(envNameBilling ?? "prod");

        var existing   = ResolvePlatform();
        var apiUrl     = Env("ANYTHINK_PLATFORM_API_URL") ?? defApiBilling ?? existing.ApiUrl;
        var billingUrl = Env("ANYTHINK_BILLING_URL")      ?? defBilling    ?? existing.BillingUrl;
        // Only reuse the saved org ID if we're pointing at the same API URL —
        // switching envs (e.g. dev → prod) means a different account org ID.
        var savedOrgId = existing.ApiUrl?.TrimEnd('/') == apiUrl.TrimEnd('/') ? existing.OrgId : null;
        var orgId = Env("ANYTHINK_PLATFORM_ORG_ID") ?? defOrgId ?? savedOrgId ?? settings.OrgId;

        if (string.IsNullOrEmpty(orgId))
            orgId = AnsiConsole.Ask<string>("[#F97316]Platform org ID:[/]");

        var email    = settings.Email    ?? AnsiConsole.Ask<string>("[#F97316]Email:[/]");
        var password = settings.Password ?? AnsiConsole.Prompt(new TextPrompt<string>("[#F97316]Password:[/]").Secret());

        var platform = new PlatformConfig { OrgId = orgId, ApiUrl = apiUrl, BillingUrl = billingUrl };
        var client   = new BillingClient(platform);
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
            SavePlatform(platform);

            Renderer.PrintWelcomeBanner(Renderer.NameFromJwt(resp!.AccessToken));

            // If --org-id (and optionally --profile) were given, also save a project profile
            // so entity/migrate commands can use the JWT without needing 'projects use'.
            if (!string.IsNullOrEmpty(settings.OrgId))
            {
                var profileKey = settings.Profile ?? settings.OrgId;
                ConfigService.SaveProfile(profileKey, new CliProfile
                {
                    OrgId       = settings.OrgId,
                    AccessToken = resp!.AccessToken,
                    BaseUrl     = apiUrl,
                    Alias       = profileKey
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
        var envName = Env("ANYTHINK_ENV");
        var (apiUrl, billingUrl, orgId) = ApiDefaults.ForEnv(envName ?? "prod");
        var resolvedOrgId = Env("ANYTHINK_PLATFORM_ORG_ID") ?? orgId ?? "";

        // Start a local listener on a free port
        var port        = FindFreePort();
        var callbackUrl = $"http://localhost:{port}/callback";
        var listener    = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        // 1. Get the Google authorization URL from the server
        string authUrl;
        try
        {
            using var http = new System.Net.Http.HttpClient();
            var json = await http.GetStringAsync(
                $"{apiUrl.TrimEnd('/')}/org/{resolvedOrgId}/auth/v1/google/authorize" +
                $"?redirectUri={Uri.EscapeDataString(callbackUrl)}");
            var doc = JsonDocument.Parse(json);
            authUrl = doc.RootElement.GetProperty("authorization_url").GetString()
                ?? throw new Exception("No authorization_url in response.");
        }
        catch (Exception ex)
        {
            listener.Stop();
            Renderer.Error($"Could not start Google login: {ex.Message}");
            return 1;
        }

        // 2. Open browser
        AnsiConsole.MarkupLine("\n[#F97316]Opening browser for Google sign-in...[/]");
        AnsiConsole.MarkupLine($"[dim]If it doesn't open automatically, visit:[/]\n{authUrl}\n");
        OpenBrowser(authUrl);

        // 3. Wait for Google to redirect back (3 min timeout)
        AnsiConsole.MarkupLine("[dim]Waiting for sign-in...[/]");
        HttpListenerContext ctx;
        try
        {
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
            ctx = await listener.GetContextAsync().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            Renderer.Error("Timed out waiting for Google sign-in.");
            return 1;
        }
        finally { listener.Stop(); }

        // Respond to the browser tab
        var html = "<html><body style='font-family:sans-serif;padding:2rem'><h2>Signed in — you can close this tab.</h2></body></html>"u8.ToArray();
        ctx.Response.ContentType = "text/html";
        ctx.Response.ContentLength64 = html.Length;
        await ctx.Response.OutputStream.WriteAsync(html);
        ctx.Response.Close();

        // 4. Extract code + state
        var qs   = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url?.Query ?? "");
        var code = qs["code"];
        var state = qs["state"];

        if (string.IsNullOrEmpty(code))
        {
            Renderer.Error($"Google sign-in failed: {qs["error"] ?? "unknown error"}");
            return 1;
        }

        // 5. Forward to Anythink callback to exchange code for tokens
        LoginResponse? tokens = null;
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Completing sign-in...", async _ =>
            {
                using var http = new System.Net.Http.HttpClient();
                var resp = await http.GetStringAsync(
                    $"{apiUrl.TrimEnd('/')}/org/{resolvedOrgId}/auth/v1/google/callback" +
                    $"?code={Uri.EscapeDataString(code)}" +
                    (state != null ? $"&state={Uri.EscapeDataString(state)}" : ""));
                tokens = JsonSerializer.Deserialize<LoginResponse>(resp,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            });

        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
        {
            Renderer.Error("No token received from server.");
            return 1;
        }

        // 6. Save platform config
        var platform = new PlatformConfig
        {
            OrgId      = resolvedOrgId,
            ApiUrl     = apiUrl,
            BillingUrl = billingUrl,
            Token      = tokens.AccessToken,
            TokenExpiresAt = tokens.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(tokens.ExpiresIn.Value - 30)
                : DateTime.UtcNow.AddHours(1)
        };

        // Fetch billing accounts to auto-select
        var billingClient = new BillingClient(platform);
        List<BillingAccount> accounts = [];
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Loading accounts...", async _ =>
                    accounts = await billingClient.GetAccountsAsync());
        }
        catch (AnythinkException ex)
        {
            Renderer.Error($"Logged in but could not load accounts: {ex.Message}");
            SavePlatform(platform);
            return 1;
        }

        if (accounts.Count == 1)
        {
            platform.AccountId = accounts[0].Id.ToString();
            Renderer.Info($"Account: [#F97316]{Markup.Escape(accounts[0].OrganizationName)}[/]");
        }
        else if (accounts.Count > 1)
        {
            var choices = accounts.Select(a => $"{a.OrganizationName}  ({a.BillingEmail})").ToList();
            var picked  = AnsiConsole.Prompt(
                Renderer.Prompt<string>().Title("[#F97316]Select billing account:[/]").AddChoices(choices));
            platform.AccountId = accounts[choices.IndexOf(picked)].Id.ToString();
        }

        ConfigService.SavePlatform(platform);
        Renderer.PrintWelcomeBanner();
        AnsiConsole.MarkupLine("Run [bold #F97316]anythink accounts use[/] to select a billing account, then [bold #F97316]anythink projects use[/] to connect to a project.");
        return 0;
    }

    private static int FindFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* user can open manually from the printed URL */ }
    }
}

