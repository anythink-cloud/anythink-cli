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

        C("config show", "Show all configured profiles and platform settings",
          null, ["anythink config show"]),

        C("config use NAME", "Set the active project profile",
          null, ["anythink config use my-project"]),

        C("config remove NAME", "Remove a project profile",
          null, ["anythink config remove my-project"]),

        C("plans",
          "List available billing plans",
          ["--json"],
          ["anythink plans", "anythink plans --json"]),

        C("accounts list", "List your billing accounts",
          null, ["anythink accounts list"]),

        C("accounts create",
          "Create a billing account",
          ["--name NAME", "--email EMAIL", "--currency gbp|usd|eur"],
          ["anythink accounts create --name \"Acme Ltd\" --email billing@acme.com"]),

        C("accounts use ID", "Set the active billing account",
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

        C("entities list", "List all entities (database tables)",
          null, ["anythink entities list"]),

        C("entities get NAME", "Get entity schema and all fields",
          null, ["anythink entities get customers"]),

        C("entities create NAME",
          "Create a new entity",
          ["--rls (row-level security)", "--public (allow unauthenticated reads)"],
          ["anythink entities create orders --rls"]),

        C("entities update NAME", "Update entity settings",
          ["--rls", "--public"], ["anythink entities update products --public"]),

        C("entities delete NAME", "Delete entity and all data",
          ["--yes"], ["anythink entities delete temp_data --yes"]),

        C("fields list ENTITY", "List fields on an entity",
          null, ["anythink fields list customers"]),

        C("fields add ENTITY FIELD",
          "Add a field to an entity",
          ["--type TYPE", "--required", "--unique", "--default VALUE"],
          ["anythink fields add customers email --type varchar --required --unique",
              "anythink fields add orders status --type varchar --default active"],
          "Types: varchar text int bigint float bool date timestamp json uuid varchar[] int[]"),

        C("fields delete ENTITY FIELD_ID", "Delete a field",
          ["--yes"], ["anythink fields delete customers 1234 --yes"]),

        C("workflows list", "List all workflows",
          null, ["anythink workflows list"]),

        C("workflows get ID", "Get workflow details and steps",
          null, ["anythink workflows get 76"]),

        C("workflows create NAME",
          "Create a workflow",
          ["--trigger TYPE", "--cron EXPRESSION"],
          ["anythink workflows create daily-sync --trigger Timed --cron \"0 6 * * *\""],
          "Trigger types: Api Timed EntityCreated EntityUpdated EntityDeleted"),

        C("workflows enable ID", "Enable a workflow", null, ["anythink workflows enable 76"]),
        C("workflows disable ID", "Disable a workflow", null, ["anythink workflows disable 76"]),
        C("workflows trigger ID", "Manually run a workflow", null, ["anythink workflows trigger 76"]),
        C("workflows delete ID", "Delete a workflow", ["--yes"], ["anythink workflows delete 76 --yes"]),

        C("data list ENTITY",
          "List records",
          ["--page N", "--limit N", "--filter FILTER", "--json"],
          ["anythink data list blog_posts",
              "anythink data list blog_posts --filter '{\"status\":\"draft\"}' --json",
              "anythink data list orders --filter '{\"total\":{\"gte\":10,\"lt\":100},\"status\":[\"paid\",\"shipped\"]}'",
              "anythink data list blog_posts --filter 'title=C:launch&published_at=NNULL:'"]),

        C("data get ENTITY ID", "Get a record by ID",
          null, ["anythink data get blog_posts 42"]),

        C("data create ENTITY",
          "Create a record",
          ["--data JSON"],
          ["anythink data create blog_posts --data '{\"title\":\"Hello\",\"status\":\"draft\"}'"]),

        C("data update ENTITY ID",
          "Update a record",
          ["--data JSON"],
          ["anythink data update blog_posts 42 --data '{\"status\":\"published\"}'"]),

        C("data delete ENTITY ID", "Delete a record",
          ["--yes"], ["anythink data delete blog_posts 42 --yes"]),

        C("data export ENTITY FILE",
          "Export records to CSV, JSON or JSONL (streamed page by page; refuses to overwrite without --force)",
          ["--format csv|json|jsonl", "--filter FILTER", "--fields a,b", "--page-size N", "--force", "--no-formula-guard"],
          ["anythink data export customers customers.csv",
              "anythink data export orders orders.jsonl --filter 'status=paid' --fields id,total"]),

        C("data import ENTITY FILE",
          "Create records from CSV, JSON array or JSONL. Always --dry-run first; non-interactive runs need --yes. Not supported: upsert, relation lookup by key, file uploads",
          ["--format csv|json|jsonl", "--dry-run", "--concurrency N", "--errors FILE", "--ignore-unknown", "--yes"],
          ["anythink data import customers customers.csv --dry-run",
              "anythink data import customers customers.csv --errors rejected.csv --yes"]),

        C("charts list", "List charts, newest first",
          ["--entity ENTITY", "--json"], ["anythink charts list --entity orders"]),

        C("charts get ID", "Get a chart's full configuration",
          ["--json"], ["anythink charts get 12"]),

        C("charts create NAME",
          "Create a chart. Preview it first with charts preview, which takes the same options",
          ["--entity ENTITY",
              "--type TYPE",
              "--group-by FIELD",
              "--interval INTERVAL",
              "--measure FIELD",
              "--agg AGG",
              "--stack-by FIELD",
              "--filter FIELD:OP:VALUE",
              "--timeframe PRESET",
              "--from DATE",
              "--to DATE",
              "--limit N",
              "--sort DIR",
              "--source SOURCE",
              "--platform-metric ID",
              "--quota-metric ID",
              "--config JSON",
              "--json"],
          ["anythink charts create \"Orders by status\" --entity orders --type pie --group-by status",
              "anythink charts create \"Revenue per month\" --entity orders --type column --group-by created_at --interval month --measure total --agg sum --timeframe 365d"],
          "Types: column (bar) line pie area stat gauge funnel. Aggregations: count sum avg min max. Filter operators: eq ne gt gte lt lte contains in exists not_exists"),

        C("charts update ID",
          "Change a chart. Only the options you pass change",
          ["--name NAME", "(the same options as charts create)", "--json"], ["anythink charts update 12 --timeframe 30d"]),

        C("charts delete ID", "Delete a chart",
          ["--yes"], ["anythink charts delete 12 --yes"]),

        C("charts preview",
          "Show the data a chart configuration would draw, without saving it",
          ["(the same options as charts create)", "--rows N", "--json"],
          ["anythink charts preview --entity orders --type pie --group-by status"]),

        C("charts platform-metrics", "List the usage metrics a platform chart can plot",
          ["--json"], ["anythink charts platform-metrics"]),

        C("dashboards list", "List the dashboards you can see",
          ["--json"], ["anythink dashboards list"]),

        C("dashboards get ID", "Get a dashboard with its widgets and their positions",
          ["--json"], ["anythink dashboards get 3"]),

        C("dashboards mine", "Get your landing dashboard, or the project default",
          ["--json"], ["anythink dashboards mine"]),

        C("dashboards create NAME",
          "Create a dashboard, optionally with charts already on it",
          ["--description TEXT", "--chart ID", "--shared", "--home", "--config JSON", "--json"],
          ["anythink dashboards create Sales --chart 12 --chart 14"]),

        C("dashboards update ID",
          "Rename a dashboard, change who can see it, or replace its widgets and layout",
          ["--name NAME", "--description TEXT", "--shared BOOL", "--config JSON", "--json"],
          ["anythink dashboards update 3 --name \"Sales overview\""]),

        C("dashboards delete ID", "Delete a dashboard and its widgets. The charts on it are kept",
          ["--yes"], ["anythink dashboards delete 3 --yes"]),

        C("dashboards data ID", "Get the data behind a dashboard's widgets",
          ["--widget ID", "--rows N", "--json"], ["anythink dashboards data 3"]),

        C("dashboards add-chart DASHBOARD_ID CHART_ID",
          "Add a chart to a dashboard as a new widget. Everything else on the dashboard stays as it was",
          ["--title TITLE", "--column N", "--row N", "--width N", "--height N", "--json"],
          ["anythink dashboards add-chart 3 12 --width 6"],
          "The grid is 12 columns wide. Leave the position out to let the dashboard place the widget"),

        C("dashboards remove-widget DASHBOARD_ID WIDGET_ID",
          "Remove a widget from a dashboard. Everything else on the dashboard stays as it was",
          ["--json"], ["anythink dashboards remove-widget 3 41"]),

        C("dashboards set-home ID", "Make a dashboard of yours your landing dashboard",
          ["--json"], ["anythink dashboards set-home 3"]),

        C("dashboards promote-to-default ID", "Copy a dashboard over the project default that new users start from (administrators only)",
          ["--yes"], ["anythink dashboards promote-to-default 3 --yes"]),

        C("dashboards widget-preview", "Show the data a widget would draw, without saving it",
          ["--type TYPE", "--chart ID", "--config JSON", "--rows N", "--json"],
          ["anythink dashboards widget-preview --type chart --chart 12"]),

        C("email templates list", "List the project's email templates",
          null, ["anythink email templates list"]),

        C("email templates show TYPE",
          "Show a template's subject and content",
          ["--rendered (print the rendered HTML instead of the source)"],
          ["anythink email templates show confirmation", "anythink email templates show confirmation --rendered"]),

        C("email templates update TYPE",
          "Change a template's subject and/or content; whatever you leave out is kept",
          ["--subject TEXT", "--content PATH (or - for stdin)", "--content-text HTML"],
          ["anythink email templates update confirmation --subject \"Welcome aboard\"",
              "anythink email templates update confirmation --content ./confirmation.html"],
          "Pass at least one of the subject or the content. HTML is limited to 1,000,000 characters."),

        C("email preview",
          "Render an unsaved draft and print the HTML; nothing is saved",
          ["--subject TEXT", "--content PATH", "--content-text HTML", "--wrapper PATH", "--wrapper-text HTML"],
          ["anythink email preview --subject Hi --content ./draft.html --wrapper ./shell.html"]),

        C("email shell show", "Show the HTML shell that wraps every email (custom or platform default)",
          null, ["anythink email shell show > shell.html"]),

        C("email shell update",
          "Save new shell HTML, or reset to the platform default",
          ["--html PATH (or - for stdin)", "--html-text HTML", "--reset"],
          ["anythink email shell update --html ./shell.html", "anythink email shell update --reset"],
          "Pass exactly one of the HTML options or --reset."),

        C("api",
          "List all REST endpoints — platform routes + dynamically generated entity CRUD routes",
          ["--json", "--base-url URL"],
          ["anythink api", "anythink api --json"]),

        C("docs", "Show this reference",
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
        | `anythink data list ENTITY [--page N] [--limit N] [--filter FILTER] [--json]` | List records |
        | `anythink data get ENTITY ID` | Get record |
        | `anythink data create ENTITY --data JSON` | Create record |
        | `anythink data update ENTITY ID --data JSON` | Update record |
        | `anythink data delete ENTITY ID [--yes]` | Delete record |

        ```sh
        anythink data list blog_posts --filter '{"status":"draft"}' --json
        anythink data list orders --filter '{"total": {"gte": 10, "lt": 100} }'
        anythink data create blog_posts --data '{"title":"Hello","status":"draft"}'
        anythink data update blog_posts 42 --data '{"status":"published"}'
        ```

        ## Charts & Dashboards

        | Command | Description |
        |---------|-------------|
        | `anythink charts list [--entity ENTITY] [--json]` | List charts |
        | `anythink charts get ID [--json]` | Get a chart's configuration |
        | `anythink charts create NAME --entity ENTITY --type TYPE [--group-by FIELD] [--measure FIELD --agg AGG] [--filter F:OP:V] [--timeframe PRESET] [--config JSON]` | Create a chart |
        | `anythink charts update ID [--name NAME] [...same options]` | Change a chart; options you leave out keep their value |
        | `anythink charts preview [...same options as create] [--rows N]` | Show the data a configuration would draw, without saving |
        | `anythink charts delete ID [--yes]` | Delete a chart |
        | `anythink charts platform-metrics` | List the usage metrics a platform chart can plot |
        | `anythink dashboards list` / `get ID` / `mine` | Read dashboards |
        | `anythink dashboards create NAME [--chart ID ...] [--shared] [--home]` | Create a dashboard |
        | `anythink dashboards add-chart DASHBOARD_ID CHART_ID [--width N --height N --column N --row N]` | Add a chart as a new widget |
        | `anythink dashboards remove-widget DASHBOARD_ID WIDGET_ID` | Remove a widget |
        | `anythink dashboards data ID [--widget ID]` | Get the data behind the widgets |
        | `anythink dashboards update ID` / `delete ID [--yes]` / `set-home ID` / `promote-to-default ID [--yes]` | Manage a dashboard |

        **Chart types:** `column` (bar) `line` `pie` `area` `stat` `gauge` `funnel`

        ```sh
        anythink charts preview --entity orders --type pie --group-by status
        anythink charts create "Orders by status" --entity orders --type pie --group-by status
        anythink dashboards create Sales --chart 12
        anythink dashboards add-chart 3 14 --width 6
        ```

        ## Email

        | Command | Description |
        |---------|-------------|
        | `anythink email templates list` | List the project's email templates |
        | `anythink email templates show TYPE [--rendered]` | Show a template's subject + content, or the rendered HTML |
        | `anythink email templates update TYPE [--subject TEXT] [--content PATH \| --content-text HTML]` | Change subject and/or content |
        | `anythink email preview [--subject TEXT] [--content PATH \| --content-text HTML] [--wrapper PATH \| --wrapper-text HTML]` | Render an unsaved draft, print the HTML |
        | `anythink email shell show` | Show the shell HTML that wraps every email |
        | `anythink email shell update (--html PATH \| --html-text HTML \| --reset)` | Save new shell HTML, or reset to the platform default |

        ```sh
        anythink email templates update confirmation --subject "Welcome aboard"
        anythink email templates update confirmation --content ./confirmation.html
        anythink email shell show > shell.html
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
