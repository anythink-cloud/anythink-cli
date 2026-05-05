<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://anythink.cloud/images/logo-dark.png">
  <img alt="Anythink" src="https://anythink.cloud/images/logo.png" height="40">
</picture>

# Anythink CLI

The official command-line interface for [Anythink](https://anythink.cloud) — the headless backend platform for developers and founders. Manage your projects, entities, data, workflows, users, files, and payments without leaving the terminal.

```
   ░███                             ░██    ░██        ░██           ░██
  ░██░██                            ░██    ░██                      ░██
 ░██  ░██  ░████████  ░██    ░██ ░████████ ░████████  ░██░████████  ░██    ░██
░█████████ ░██    ░██ ░██    ░██    ░██    ░██    ░██ ░██░██    ░██ ░██   ░██
░██    ░██ ░██    ░██ ░██    ░██    ░██    ░██    ░██ ░██░██    ░██ ░███████
░██    ░██ ░██    ░██ ░██   ░███    ░██    ░██    ░██ ░██░██    ░██ ░██   ░██
░██    ░██ ░██    ░██  ░█████░██     ░████ ░██    ░██ ░██░██    ░██ ░██    ░██
                             ░██
                       ░███████
```

---

## Contents

- [Installation](#installation)
- [Getting started](#getting-started)
- [Command reference](#command-reference)
  - [signup / login / logout](#signup--login--logout)
  - [accounts](#accounts)
  - [projects](#projects)
  - [config](#config)
  - [entities](#entities)
  - [fields](#fields)
  - [data](#data)
  - [workflows](#workflows)
  - [users](#users)
  - [files](#files)
  - [roles](#roles)
  - [menus](#menus)
  - [pay](#pay)
  - [oauth](#oauth)
  - [api](#api)
  - [docs](#docs)
  - [migrate](#migrate)
  - [plans](#plans)
- [MCP server](#mcp-server)
- [Contributing](#contributing)
- [License](#license)

---

## Installation

### macOS / Linux — download binary

Grab the latest release for your platform from the [Releases](https://github.com/anythink-cloud/anythink-cli/releases/latest) page:

| Platform              | Binary                 |
| --------------------- | ---------------------- |
| macOS (Apple Silicon) | `anythink-osx-arm64`   |
| macOS (Intel)         | `anythink-osx-x64`     |
| Linux (x86_64)        | `anythink-linux-x64`   |
| Linux (ARM64)         | `anythink-linux-arm64` |

```bash
# Example — macOS Apple Silicon
curl -Lo anythink https://github.com/anythink-cloud/anythink-cli/releases/latest/download/anythink-osx-arm64
chmod +x anythink
sudo mv anythink /usr/local/bin/
```

Verify the download against `checksums.txt` in the release assets:

```bash
sha256sum -c checksums.txt --ignore-missing
```

### .NET global tool

If you have the [.NET 8 SDK](https://dotnet.microsoft.com/download) installed:

```bash
dotnet tool install --global anythink-cli
```

### Build from source

```bash
git clone https://github.com/anythink-cloud/anythink-cli
cd anythink-cli
dotnet build
dotnet run -- --help
```

---

## Getting started

```bash
# 1. Create an account (or log in if you already have one)
anythink signup

# 2. Create a billing account
anythink accounts create --name "My Company"

# 3. Create a project
anythink projects create "My App" --region lon1

# 4. Connect to the project
anythink projects use <project-id>

# 5. Start building
anythink entities list
anythink workflows list
```

Your credentials and project profiles are stored in `~/.anythink/config.json`. You can manage multiple projects by running `projects use` to switch between them.

---

## Command reference

### signup / login / logout

```
anythink signup                        Create a new Anythink account
anythink login                         Log in to the platform
anythink logout                        Remove saved credentials for a project profile
```

`signup` and `login` are interactive — they prompt for email and password and walk you through connecting to a billing account and project on first run.

---

### accounts

Manage billing accounts. A billing account is the container for one or more projects and holds your subscription and payment details.

```
anythink accounts list                 List your billing accounts
anythink accounts create               Create a new billing account
anythink accounts use <id>             Set the active billing account
```

**Examples**

```bash
anythink accounts create --name "Acme Ltd" --email billing@acme.com
anythink accounts use a1b2c3d4
```

---

### projects

Create and manage Anythink projects. Each project is an isolated backend instance with its own database, auth, files, and workflows.

```
anythink projects list                 List projects in the active billing account
anythink projects create <name>        Create a new project
anythink projects use <id>             Connect to a project (sets it as the active profile)
anythink projects delete <id>          Delete a project
```

**Options — `projects create`**

| Flag            | Description                     |
| --------------- | ------------------------------- |
| `--region <id>` | Deployment region (e.g. `lon1`) |
| `--plan <id>`   | Plan ID (see `anythink plans`)  |

**Examples**

```bash
anythink projects create "My App" --region lon1
anythink projects use a1b2c3d4
anythink projects delete a1b2c3d4 --yes
```

---

### config

View and manage saved CLI profiles.

```
anythink config show                   List all profiles and platform settings
anythink config use <profile>          Set the active project profile
anythink config remove <profile>       Remove a profile
```

Profiles are named by project alias or ID and stored in `~/.anythink/config.json`.

---

### entities

Manage entities (database tables) in the active project.

```
anythink entities list                 List all entities
anythink entities get <name>           Get entity details and fields
anythink entities create <name>        Create a new entity
anythink entities update <name>        Update entity settings
anythink entities delete <name>        Delete an entity and all its data
```

**Options — `entities create`**

| Flag         | Description                                |
| ------------ | ------------------------------------------ |
| `--rls`      | Enable row-level security                  |
| `--public`   | Make the entity publicly readable          |
| `--lock`     | Lock new records (prevent direct creation) |
| `--junction` | Mark as a junction (many-to-many) table    |

**Examples**

```bash
anythink entities create orders --rls
anythink entities get customers
anythink entities delete temp_data --yes
```

---

### fields

Manage fields on an entity. Fields map directly to database columns.

```
anythink fields list <entity>          List fields on an entity
anythink fields add <entity> <name>    Add a field
anythink fields delete <entity> <id>   Delete a field
```

**Options — `fields add`**

| Flag                | Description                                                                       |
| ------------------- | --------------------------------------------------------------------------------- |
| `--type <type>`     | Field type: `varchar`, `text`, `int`, `float`, `bool`, `datetime`, `json`, `uuid` |
| `--required`        | Mark the field as required                                                        |
| `--unique`          | Enforce a unique constraint                                                       |
| `--indexed`         | Add a database index                                                              |
| `--default <value>` | Default value                                                                     |

**Examples**

```bash
anythink fields list customers
anythink fields add customers email --type varchar --unique --required
anythink fields add products price --type float --required
anythink fields delete customers 1234 --yes
```

---

### data

CRUD operations on entity records.

```
anythink data list <entity>            List records
anythink data get <entity> <id>        Get a single record by ID
anythink data create <entity>          Create a new record
anythink data update <entity> <id>     Update a record
anythink data delete <entity> <id>     Delete a record
```

**Options — `data list`**

| Flag              | Description                                                                        |
| ----------------- | ---------------------------------------------------------------------------------- |
| `--limit <n>`     | Records per page (default: 20)                                                     |
| `--page <n>`      | Page number (default: 1)                                                           |
| `--filter <json>` | Filter expression (JSON)                                                           |
| `--json`          | Output raw JSON instead of table                                                   |
| `--all`           | Stream all pages as sequential JSON objects (requires `--json`, constant memory)    |

**Options — `data create` / `data update`**

| Flag            | Description                 |
| --------------- | --------------------------- |
| `--data <json>` | JSON object of field values |

**Examples**

```bash
anythink data list blog_posts --limit 10
anythink data get blog_posts 42
anythink data create blog_posts --data '{"title":"Hello World","status":"draft"}'
anythink data update blog_posts 42 --data '{"status":"approved"}'
anythink data delete blog_posts 42 --yes
```

---

### workflows

Manage automation workflows. Workflows can be triggered on a cron schedule, when entities are created or updated, or manually.

```
anythink workflows list                List all workflows
anythink workflows get <id>            Get workflow details and steps
anythink workflows create <name>       Create a new workflow
anythink workflows enable <id>         Enable a workflow
anythink workflows disable <id>        Disable a workflow
anythink workflows trigger <id>        Manually trigger a workflow
anythink workflows delete <id>         Delete a workflow
```

**Options — `workflows create`**

| Flag               | Description                                                       |
| ------------------ | ----------------------------------------------------------------- |
| `--trigger <type>` | Trigger type: `Timed`, `EntityCreated`, `EntityUpdated`, `Manual` |
| `--cron <expr>`    | Cron expression (for `Timed` trigger, e.g. `0 6 * * *`)           |
| `--entity <name>`  | Entity name (for `EntityCreated` / `EntityUpdated` triggers)      |

**Examples**

```bash
anythink workflows create daily-sync --trigger Timed --cron "0 6 * * *"
anythink workflows trigger 76
anythink workflows disable 83
```

---

### users

Manage users in the active project.

```
anythink users list                    List all users
anythink users me                      Show the currently authenticated user
anythink users get <id>                Get a user by ID
anythink users invite <email> <first> <last>   Create a user and send an invitation email
anythink users delete <id>             Delete a user
```

**Options — `users invite`**

| Flag             | Description                   |
| ---------------- | ----------------------------- |
| `--role-id <id>` | Assign a role to the new user |

**Examples**

```bash
anythink users list
anythink users invite alice@example.com Alice Smith --role-id 3
anythink users delete 42 --yes
```

---

### files

Manage uploaded files in the active project.

```
anythink files list                    List uploaded files
anythink files get <id>                Get file metadata by ID
anythink files upload <path>           Upload a file
anythink files delete <id>             Delete a file
```

**Options — `files list`**

| Flag          | Description                  |
| ------------- | ---------------------------- |
| `--page <n>`  | Page number                  |
| `--limit <n>` | Files per page (default: 25) |

**Options — `files upload`**

| Flag       | Description                       |
| ---------- | --------------------------------- |
| `--public` | Make the file publicly accessible |

**Examples**

```bash
anythink files list
anythink files upload logo.png --public
anythink files upload export.csv
anythink files delete 12 --yes
```

---

### roles

Manage roles in the active project. Roles control what authenticated users can access.

```
anythink roles list                    List all roles
anythink roles create <name>           Create a new role
anythink roles delete <id>             Delete a role
```

**Options — `roles create`**

| Flag                   | Description                            |
| ---------------------- | -------------------------------------- |
| `--description <text>` | Human-readable description of the role |

**Examples**

```bash
anythink roles list
anythink roles create editor --description "Can edit content"
anythink roles delete 5 --yes
```

---

### menus

Manage dashboard sidebar menus in the active project. Menus control what entities appear in the Anythink dashboard and how they are grouped.

```
anythink menus list                              List all menus with tree structure
anythink menus add-item <menu_id> <entity>       Add an entity to a dashboard menu
```

**Options — `menus add-item`**

| Flag              | Description                                          |
| ----------------- | ---------------------------------------------------- |
| `--icon <name>`   | Lucide icon name (e.g. `MessageCircle`, `Target`)    |
| `--name <text>`   | Display name (defaults to entity name, title-cased)  |
| `--parent <id>`   | Parent menu item ID for nesting under a group        |

**Examples**

```bash
# List all menus and their items
anythink menus list

# Add "Check-ins" under the Profiles group (parent 168) in admin menu (250)
anythink menus add-item 250 check_ins --icon MessageCircle --parent 168

# Add a top-level menu item
anythink menus add-item 250 badges --icon Award
```

---

### pay

Configure and manage Anythink Pay — the built-in Stripe Connect integration for accepting payments in your project.

```
anythink pay status                    Show Stripe Connect account status
anythink pay connect                   Set up a Stripe Connect account and start onboarding
anythink pay payments                  List recent payments
anythink pay methods                   List saved payment methods
```

**Options — `pay payments`**

| Flag          | Description                     |
| ------------- | ------------------------------- |
| `--page <n>`  | Page number                     |
| `--limit <n>` | Payments per page (default: 25) |

`pay connect` is interactive — it prompts for business type, country, and contact email, creates a Stripe Connect account, then opens the Stripe onboarding URL in your browser.

**Examples**

```bash
anythink pay status
anythink pay connect
anythink pay payments --limit 50
```

---

### oauth

Configure OAuth social sign-in providers for the active project.

```
anythink oauth google status           Show Google OAuth configuration status
anythink oauth google configure        Set Google OAuth client ID and secret
```

Google OAuth lets your project's users sign in with their Google account. You'll need a Google Cloud project with the OAuth 2.0 credentials created — see the [Google Cloud Console](https://console.cloud.google.com/apis/credentials).

Set the authorised redirect URI in your Google Cloud credentials to:

```
https://api.my.anythink.cloud/org/<your-org-id>/auth/v1/google/callback
```

**Examples**

```bash
anythink oauth google status
anythink oauth google configure
```

---

### api

List all API endpoints available for the active project — both platform routes and the dynamically generated REST routes for your entities.

```
anythink api                           List all endpoints
anythink api --json                    Output as JSON (useful for AI tooling)
```

---

### docs

Print the full CLI reference.

```
anythink docs                          Print reference as markdown
anythink docs --json                   Print reference as JSON (for AI/tooling consumption)
```

---

### migrate

Copy the entity schema (entities + fields) from one project profile to another. Useful for promoting a schema from staging to production.

```
anythink migrate --from <profile> --to <profile>
```

**Options**

| Flag               | Description                                        |
| ------------------ | -------------------------------------------------- |
| `--from <profile>` | Source profile name (required)                     |
| `--to <profile>`   | Destination profile name (required)                |
| `--dry-run`        | Show what would be migrated without making changes |

**Examples**

```bash
anythink migrate --from my-app-staging --to my-app-prod
anythink migrate --from my-app-staging --to my-app-prod --dry-run
```

---

### plans

List available Anythink plans.

```
anythink plans                         List plans
anythink plans --json                  Output as JSON
```

---

## MCP server

The Anythink MCP server exposes the platform to AI assistants (Claude, Cursor, etc.) via the [Model Context Protocol](https://modelcontextprotocol.io).

### Install

```bash
dotnet tool install -g anythink-cli
dotnet tool install -g anythink-mcp
```

### Configure

Add to your MCP client config (e.g. `.mcp.json` for Claude Code):

```json
{
  "mcpServers": {
    "anythink": {
      "command": "anythink-mcp"
    }
  }
}
```

To use a specific profile: `"args": ["--profile", "my-project"]`

### Available tools

The MCP server provides dedicated tools for authentication, account/project management, and configuration — plus a generic `cli` tool that can run any CLI command:

| Tool | Description |
| --- | --- |
| `signup` | Create a new Anythink account |
| `login` | Log in with email and password |
| `login_direct` | Store credentials directly (org ID + API key or JWT) |
| `logout` | Remove a saved profile |
| `config_show` | List all profiles |
| `config_use` | Switch active profile |
| `config_remove` | Remove a profile |
| `accounts_list` | List billing accounts |
| `accounts_create` | Create a billing account |
| `accounts_use` | Set the active billing account |
| `projects_list` | List projects |
| `projects_create` | Create a project |
| `projects_use` | Connect to a project |
| `projects_delete` | Delete a project |
| `cli` | Run any CLI command (entities, data, workflows, roles, etc.) |

---

## Contributing

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- An Anythink account (free tier works)

### Setup

```bash
git clone https://github.com/anythink-cloud/anythink-cli
cd anythink-cli
dotnet build
```

### Running locally

```bash
dotnet run -- --help
dotnet run -- projects list
dotnet run -- entities list
```

### Environment variables

The CLI targets production (`https://api.my.anythink.cloud`) by default. You can override platform and API settings via environment variables (e.g. in your shell or a `.env` file in the current working directory):

| Variable                 | Description                                           |
| ------------------------ | ----------------------------------------------------- |
| `MYANYTHINK_API_URL`     | Platform management API base URL                      |
| `MYANYTHINK_ORG_ID`      | Platform organization/tenant ID                       |
| `BILLING_API_URL`        | Billing API base URL                                  |
| `ANYTHINK_PLATFORM_TOKEN`| JWT access token for platform commands                |
| `ANYTHINK_ACCOUNT_ID`    | UUID of the active billing account                    |

These variables take precedence over the saved configuration in `~/.anythink/config.json`. Overrides are applied at runtime by the configuration resolution logic.

### Releases

Releases are automated via GitHub Actions. Push a version tag to trigger a build:

```bash
git tag v1.2.0
git push origin v1.2.0
```

The workflow builds self-contained binaries for macOS (arm64, x64) and Linux (x64, arm64), computes SHA256 checksums, and publishes them as a GitHub release.

### Project structure

```
anythink-cli/
├── src/                        # CLI source
│   ├── Commands/               # Command implementations (signup, login, etc)
│   ├── Config/                 # CliConfig.cs, Profile, ConfigService
│   ├── Models/                 # ApiModels.cs, BillingModels.cs
│   ├── Client/                 # HttpApiClient.cs, AnythinkClient.cs, BillingClient.cs
│   ├── Output/                 # Renderer.cs (Spectre.Console helpers)
│   └── Program.cs              # Application entry point & .env loader
├── mcp/                        # MCP server source
│   ├── Tools/                  # MCP tool implementations
│   ├── McpClientFactory.cs     # Auth + client resolution
│   └── Program.cs              # MCP server entry point
├── tests/                      # CLI unit tests
├── mcp-tests/                  # MCP unit tests
├── AnythinkCli.sln             # Solution file
└── .gitignore
```

---

## License

MIT — see [LICENSE](LICENSE).

---

Built with [Spectre.Console](https://spectreconsole.net) · Powered by [Anythink](https://anythink.cloud)
