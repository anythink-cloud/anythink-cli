using AnythinkCli.Config;
using AnythinkCli.Output;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace AnythinkCli.Commands;

public class DocsSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output as machine-readable JSON instead of markdown")]
    public bool Json { get; set; }
}

public class DocsCommand : Command<DocsSettings>
{
    public override int Execute(CommandContext context, DocsSettings settings)
    {
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(BuildSpec(), Renderer.PrettyJson));
            return 0;
        }

        Console.WriteLine(BuildMarkdown());
        return 0;
    }

    // ── Structured spec (also drives JSON output) ─────────────────────────────

    private static object BuildSpec() => new
    {
        tool = "anythink",
        version = "1.0.0",
        description = "Anythink BaaS CLI — manage your backend platform from the command line",
        api_urls = new
        {
            myanythink = ApiDefaults.MyAnythinkApiUrl,
            billing = ApiDefaults.BillingApiUrl
        },
        quick_start = new[]
        {
            "anythink signup",
            "anythink login",
            "anythink accounts create",
            "anythink projects create \"My App\"",
            "anythink projects use <project-id>",
            "anythink entities list",
        },
        auth_header = "X-API-Key: <key>  OR  Authorization: Bearer <token>",
        commands = BuildCommandList(),
    };

    private static object[] BuildCommandList() =>
    [
        C("signup",
          "Create a new Anythink platform account",
          ["--first-name NAME", "--last-name NAME", "--email EMAIL", "--password PASSWORD", "--referral CODE"],
          ["anythink signup", "anythink signup --email you@example.com"]),

        C("login",
          "Authenticate; saves credentials to ~/.anythink/config.json",
          ["--email EMAIL", "--password PASSWORD"],
          ["anythink login", "anythink login --email you@example.com"]),

        C("logout",
          "Remove saved credentials",
          ["--profile NAME"],
          ["anythink logout", "anythink logout --profile my-project"]),

        C("config show",  "Show all configured profiles and platform settings",
          null, ["anythink config show"]),

        C("config use NAME",  "Set the active project profile",
          null, ["anythink config use my-project"]),

        C("config remove NAME",  "Remove a project profile",
          null, ["anythink config remove my-project"]),

        C("plans",
          "List available billing plans",
          ["--json"],
          ["anythink plans", "anythink plans --json"]),

        C("accounts list",  "List your billing accounts",
          null, ["anythink accounts list"]),

        C("accounts create",
          "Create a billing account",
          ["--name NAME", "--email EMAIL", "--currency gbp|usd|eur"],
          ["anythink accounts create --name \"Acme Ltd\" --email billing@acme.com"]),

        C("accounts use ID",  "Set the active billing account",
          null, ["anythink accounts use a1b2c3d4"]),

        C("projects list",
          "List projects in the active billing account",
          ["--account ID"],
          ["anythink projects list"]),

        C("projects create [NAME]",
          "Create a project — interactive plan and region picker if flags omitted",
          ["--plan PLAN_UUID", "--region REGION", "--account ID"],
          ["anythink projects create \"My App\"", "anythink projects create \"My App\" --plan <uuid> --region lon1"],
          "Regions: lon1"),

        C("projects use ID",
          "Connect to a project; saves it as the active profile",
          ["--account ID", "--api-key KEY"],
          ["anythink projects use a1b2c3d4"]),

        C("projects delete ID",
          "Delete a project and all its data",
          ["--yes"],
          ["anythink projects delete a1b2c3d4 --yes"]),

        C("entities list",  "List all entities (database tables)",
          null, ["anythink entities list"]),

        C("entities get NAME",  "Get entity schema and all fields",
          null, ["anythink entities get customers"]),

        C("entities create NAME",
          "Create a new entity",
          ["--rls (row-level security)", "--public (allow unauthenticated reads)"],
          ["anythink entities create orders --rls"]),

        C("entities update NAME",  "Update entity settings",
          ["--rls", "--public"], ["anythink entities update products --public"]),

        C("entities delete NAME",  "Delete entity and all data",
          ["--yes"], ["anythink entities delete temp_data --yes"]),

        C("fields list ENTITY",  "List fields on an entity",
          null, ["anythink fields list customers"]),

        C("fields add ENTITY FIELD",
          "Add a field to an entity",
          ["--type TYPE", "--required", "--unique", "--default VALUE"],
          ["anythink fields add customers email --type varchar --required --unique",
           "anythink fields add orders status --type varchar --default active"],
          "Types: varchar text int bigint float bool date timestamp json uuid varchar[] int[]"),

        C("fields delete ENTITY FIELD_ID",  "Delete a field",
          ["--yes"], ["anythink fields delete customers 1234 --yes"]),

        C("workflows list",  "List all workflows",
          null, ["anythink workflows list"]),

        C("workflows get ID",  "Get workflow details and steps",
          null, ["anythink workflows get 76"]),

        C("workflows create NAME",
          "Create a workflow",
          ["--trigger TYPE", "--cron EXPRESSION"],
          ["anythink workflows create daily-sync --trigger Timed --cron \"0 6 * * *\""],
          "Trigger types: Api Timed EntityCreated EntityUpdated EntityDeleted"),

        C("workflows enable ID",  "Enable a workflow",  null, ["anythink workflows enable 76"]),
        C("workflows disable ID", "Disable a workflow", null, ["anythink workflows disable 76"]),
        C("workflows trigger ID", "Manually run a workflow", null, ["anythink workflows trigger 76"]),
        C("workflows delete ID",  "Delete a workflow",  ["--yes"], ["anythink workflows delete 76 --yes"]),

        C("data list ENTITY",
          "List records",
          ["--page N", "--limit N", "--filter JSON", "--json"],
          ["anythink data list blog_posts",
           "anythink data list blog_posts --filter '{\"status\":\"draft\"}' --json"]),

        C("data get ENTITY ID",     "Get a record by ID",
          null, ["anythink data get blog_posts 42"]),

        C("data create ENTITY",
          "Create a record",
          ["--data JSON"],
          ["anythink data create blog_posts --data '{\"title\":\"Hello\",\"status\":\"draft\"}'"]),

        C("data update ENTITY ID",
          "Update a record",
          ["--data JSON"],
          ["anythink data update blog_posts 42 --data '{\"status\":\"published\"}'"]),

        C("data delete ENTITY ID",  "Delete a record",
          ["--yes"], ["anythink data delete blog_posts 42 --yes"]),

        C("api",
          "List all REST endpoints — platform routes + dynamically generated entity CRUD routes",
          ["--json", "--base-url URL"],
          ["anythink api", "anythink api --json"]),

        C("pay status",  "Show Stripe Connect account status", null, ["anythink pay status"]),
        C("pay connect", "Set up Stripe Connect and start onboarding", null, ["anythink pay connect"]),
        C("pay setup",   "Guided setup: Stripe Connect, Apple IAP, and a first plan",
          null, ["anythink pay setup"]),
        C("pay payments", "List recent payments",
          ["--page N", "--limit N"], ["anythink pay payments --limit 50"]),
        C("pay methods",  "List saved payment methods", null, ["anythink pay methods"]),
        C("pay entitlement", "Show the current user's access / trial entitlement",
          null, ["anythink pay entitlement"]),
        C("pay payment-options",
          "Show available payment providers for a platform/storefront",
          ["--platform ios|android|web", "--storefront CODE"],
          ["anythink pay payment-options --platform ios --storefront GBR"]),

        C("pay trial status",  "Show whether the app engagement trial is enabled",
          null, ["anythink pay trial status"]),
        C("pay trial enable",  "Enable the app engagement trial", null, ["anythink pay trial enable"]),
        C("pay trial disable", "Disable the app engagement trial", null, ["anythink pay trial disable"]),

        C("pay apple credentials set",
          "Set Apple IAP credentials (reads the .p8 key from a file)",
          ["--issuer-id ID", "--key-id ID", "--bundle-id ID", "--private-key-file PATH", "--environment sandbox|production"],
          ["anythink pay apple credentials set --issuer-id <uuid> --key-id <id> --bundle-id com.example.app --private-key-file ./AuthKey.p8"]),
        C("pay apple credentials show", "Show Apple IAP credentials (identifiers masked)",
          null, ["anythink pay apple credentials show"]),
        C("pay apple credentials notification-url",
          "Print the App Store Server Notifications URL to register in App Store Connect",
          null, ["anythink pay apple credentials notification-url"]),
        C("pay apple verify",
          "Verify an Apple transaction (testing) and bind it to the current user",
          ["--signed-transaction JWS", "--original-transaction-id ID"],
          ["anythink pay apple verify --signed-transaction <JWS>"]),

        C("pay plans list",  "List subscription plans", null, ["anythink pay plans list"]),
        C("pay plans get ID", "Show one plan by id", null, ["anythink pay plans get 12"]),
        C("pay plans create",
          "Create a subscription plan (prompts for missing fields)",
          ["--plan-name NAME", "--name NAME", "--description TEXT", "--amount N", "--currency gbp|usd|eur",
           "--interval day|week|month|year", "--trial-days N", "--apple-product-id ID", "--apple-subscription-group-id ID"],
          ["anythink pay plans create --plan-name monthly --name Monthly --amount 9.99 --currency gbp --interval month --apple-product-id monthly_002"]),
        C("pay plans update ID", "Update a plan (any --flag overrides; unset = keep)",
          ["--name NAME", "--amount N", "--apple-product-id ID"], ["anythink pay plans update 12 --amount 12.99"]),
        C("pay plans delete ID", "Delete a subscription plan", ["--yes"], ["anythink pay plans delete 12 --yes"]),

        C("pay subscriptions list", "List subscriptions",
          ["--page N", "--limit N", "--status STATUS"], ["anythink pay subscriptions list --status active"]),
        C("pay subscriptions get ID", "Show one subscription by id (guid)", null, ["anythink pay subscriptions get <subId>"]),
        C("pay subscriptions events ID", "Show the lifecycle history of a subscription",
          null, ["anythink pay subscriptions events <subId>"]),
        C("pay subscriptions by-user USER_ID", "List subscriptions accessible to a user",
          null, ["anythink pay subscriptions by-user 42"]),
        C("pay subscriptions cancel ID", "Cancel a subscription", null, ["anythink pay subscriptions cancel <subId>"]),
        C("pay subscriptions resume ID", "Resume a cancelled subscription", null, ["anythink pay subscriptions resume <subId>"]),
        C("pay subscriptions delete ID", "Hard-delete a subscription (tenant admin)",
          ["--yes"], ["anythink pay subscriptions delete <subId> --yes"]),
        C("pay subscriptions force-expire ID", "Force-expire a subscription now (tenant admin)",
          ["--yes"], ["anythink pay subscriptions force-expire <subId>"]),
        C("pay subscriptions relink ID", "Move a subscription to another user (tenant admin)",
          ["--to-user-id USER_ID"], ["anythink pay subscriptions relink <subId> --to-user-id 42"]),
        C("pay subscriptions resync ID", "Re-sync a subscription from the provider (tenant admin)",
          null, ["anythink pay subscriptions resync <subId>"]),
        C("pay subscriptions users set ID USER_ID", "Grant/update a user's access to a subscription",
          ["--readonly"], ["anythink pay subscriptions users set <subId> 42 --readonly"]),
        C("pay subscriptions users remove ID USER_ID", "Remove a user's access to a subscription",
          null, ["anythink pay subscriptions users remove <subId> 42"]),

        C("pay offers list", "List offers and their primary code",
          null, ["anythink pay offers list"]),
        C("pay offers get ID", "Show one offer by id (guid)", null, ["anythink pay offers get <offerId>"]),
        C("pay offers create",
          "Create an offer (rewards via convenience flags or raw JSON)",
          ["--name NAME", "--kind discount|trial_extension|referral", "--description TEXT",
           "--redeemer-reward JSON", "--referrer-reward JSON", "--eligibility JSON",
           "--redeemer-trial-days N", "--discount-percent N", "--discount-duration once|forever|repeating",
           "--referrer-trial-days N", "--referrer-subscription-days N",
           "--valid-from DATE", "--valid-until DATE", "--total-cap N", "--per-user-cap N", "--status STATUS"],
          ["anythink pay offers create --name \"Launch 50\" --kind discount --discount-percent 50",
           "anythink pay offers create --name Referral --kind referral --redeemer-trial-days 14 --referrer-subscription-days 30"],
          "Rewards/eligibility are JSON strings forwarded to AnythinkPay; convenience flags build the common shapes."),
        C("pay offers update ID",
          "Update an offer (patch — only supplied fields change; kind is immutable)",
          ["--name NAME", "--status active|paused|expired", "--redeemer-reward JSON", "--referrer-reward JSON", "--eligibility JSON"],
          ["anythink pay offers update <offerId> --referrer-trial-days 30"]),
        C("pay offers pause ID", "Pause an offer", null, ["anythink pay offers pause <offerId>"]),
        C("pay offers activate ID", "Activate a paused offer", null, ["anythink pay offers activate <offerId>"]),
        C("pay offers codes ID", "List the promo/referral codes attached to an offer",
          null, ["anythink pay offers codes <offerId>"]),
        C("pay offers add-code ID", "Add a promo/referral code to an offer",
          ["--slug SLUG", "--owner-user-id N"],
          ["anythink pay offers add-code <offerId> --slug LAUNCH50"]),
        C("pay offers redemptions ID",
          "List redemptions for an offer",
          ["--page N", "--limit N"],
          ["anythink pay offers redemptions <offerId>"]),
        C("pay offers user-code USER_ID", "Look up a user's personal referral code",
          null, ["anythink pay offers user-code 42"]),

        C("docs",  "Show this reference",
          ["--json"], ["anythink docs", "anythink docs --json"]),
    ];

    private static object C(string syntax, string description,
        string[]? options, string[] examples, string? notes = null) =>
        new { syntax = $"anythink {syntax}", description, options = options ?? [], examples, notes };

    // ── Markdown output ───────────────────────────────────────────────────────

    private static string BuildMarkdown() => $$"""
        # Anythink CLI — Reference

        > Run `anythink docs --json` for machine-readable output · `anythink <command> --help` for per-command help

        ## Quick Start

        ```sh
        anythink signup                          # create account (check email for confirmation link)
        anythink login                           # authenticate
        anythink accounts create                 # create a billing account
        anythink projects create "My App"        # pick plan + region interactively
        anythink projects use <project-id>       # connect to project (saves as active profile)
        anythink entities list                   # explore the data model
        ```

        ## Auth & Config

        | Command | Description |
        |---------|-------------|
        | `anythink signup` | Create account — prompts for name, email, password |
        | `anythink login` | Authenticate — saves to `~/.anythink/config.json` |
        | `anythink logout [--profile NAME]` | Remove credentials |
        | `anythink config show` | List all profiles and platform settings |
        | `anythink config use NAME` | Switch active project profile |
        | `anythink config remove NAME` | Remove a project profile |

        ## Billing & Projects

        | Command | Description |
        |---------|-------------|
        | `anythink plans [--json]` | List available plans |
        | `anythink accounts list` | List billing accounts |
        | `anythink accounts create` | Create billing account — prompts for name, email, currency |
        | `anythink accounts use ID` | Set active billing account |
        | `anythink projects list` | List projects in the active billing account |
        | `anythink projects create [NAME]` | Create project — interactive plan + region picker |
        | `anythink projects use ID` | Connect to project; saves as active profile |
        | `anythink projects delete ID [--yes]` | Delete project and all data |

        **Regions:** `lon1`

        ## Data Model

        | Command | Description |
        |---------|-------------|
        | `anythink entities list` | List all entities (tables) |
        | `anythink entities get NAME` | Get entity schema + fields |
        | `anythink entities create NAME [--rls] [--public]` | Create entity |
        | `anythink entities update NAME [--rls] [--public]` | Update entity settings |
        | `anythink entities delete NAME [--yes]` | Delete entity + all data |
        | `anythink fields list ENTITY` | List fields |
        | `anythink fields add ENTITY FIELD --type TYPE` | Add field |
        | `anythink fields delete ENTITY FIELD_ID [--yes]` | Delete field |

        **Field types:** `varchar` `text` `int` `bigint` `float` `bool` `date` `timestamp` `json` `uuid` `varchar[]` `int[]`

        ```sh
        anythink fields add customers email --type varchar --required --unique
        anythink fields add orders status --type varchar --default active
        ```

        ## Workflows

        | Command | Description |
        |---------|-------------|
        | `anythink workflows list` | List all workflows |
        | `anythink workflows get ID` | Get workflow + steps |
        | `anythink workflows create NAME --trigger TYPE [--cron EXPR]` | Create workflow |
        | `anythink workflows enable ID` | Enable |
        | `anythink workflows disable ID` | Disable |
        | `anythink workflows trigger ID` | Manually run |
        | `anythink workflows delete ID [--yes]` | Delete |

        **Trigger types:** `Api` `Timed` `EntityCreated` `EntityUpdated` `EntityDeleted`

        ```sh
        anythink workflows create daily-sync --trigger Timed --cron "0 6 * * *"
        anythink workflows trigger 76
        ```

        ## Data CRUD

        | Command | Description |
        |---------|-------------|
        | `anythink data list ENTITY [--page N] [--limit N] [--filter JSON] [--json]` | List records |
        | `anythink data get ENTITY ID` | Get record |
        | `anythink data create ENTITY --data JSON` | Create record |
        | `anythink data update ENTITY ID --data JSON` | Update record |
        | `anythink data delete ENTITY ID [--yes]` | Delete record |

        ```sh
        anythink data list blog_posts --filter '{"status":"draft"}' --json
        anythink data create blog_posts --data '{"title":"Hello","status":"draft"}'
        anythink data update blog_posts 42 --data '{"status":"published"}'
        ```

        ## API Explorer

        ```sh
        anythink api           # print all REST endpoints for the active project
        anythink api --json    # machine-readable endpoint list
        ```

        Auth header: `X-API-Key: <key>`  or  `Authorization: Bearer <token>`

        ## API URLs

        | Environment | URL |
        |-------------|-----|
        | MyAnythink | `{{ApiDefaults.MyAnythinkApiUrl}}` |
        | Billing | `{{ApiDefaults.BillingApiUrl}}` |

        ## Config File

        Path: `~/.anythink/config.json`
        Contains: project profiles (per-project credentials + API URL) and platform config (billing credentials).
        """;
}
