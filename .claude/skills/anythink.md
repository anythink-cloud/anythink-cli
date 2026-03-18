# Anythink CLI Skill

## What is Anythink?

Anythink (anythink.cloud) is a headless Backend-as-a-Service (BaaS) platform. It provides:

- **Entities** — database tables with a REST API generated automatically
- **Fields** — typed columns on those tables (varchar, text, integer, boolean, json, relationships, etc.)
- **Data** — CRUD operations on records within any entity
- **Workflows** — serverless automation pipelines (cron, event-driven, API-triggered, or manual)
- **Users & Roles** — user management with role-based access control
- **Files** — managed file storage with optional public URLs
- **Pay** — Stripe Connect integration for accepting payments
- **OAuth** — Google OAuth configuration for social login

The CLI (`anythink`) is the primary developer interface. Use it to scaffold schemas, manage data, trigger automations, and administer projects — all from the terminal.

---

## Installation

```bash
# macOS (Homebrew)
brew tap Anythink-Ltd/homebrew-tap
brew install anythink

# .NET global tool (cross-platform)
dotnet tool install -g anythink-cli

# Build from source
git clone https://github.com/Anythink-Ltd/anythink-cli
cd anythink-cli && dotnet build && dotnet run --
```

---

## Authentication

```bash
# Create a new Anythink account (interactive)
anythink signup

# Log in to an existing project (prompts for env: cloud or self-hosted)
anythink login

# List saved profiles
anythink config list

# Switch active project
anythink config use <profile-name>

# Show current active profile
anythink config current

# Remove a saved profile
anythink config remove <profile-name>
```

The CLI stores profiles in `~/.anythink/config.json`. Each profile holds the org ID, base URL, and access token.

---

## Command Reference

### Entities

```bash
# List all entities in the project
anythink entities list

# Inspect a single entity (includes fields)
anythink entities get <name>

# Create a new entity
anythink entities create <name>
anythink entities create orders --rls --public
anythink entities create sessions --lock-records

# Update entity settings
anythink entities update <name> --public true
anythink entities update <name> --rls false

# Delete an entity (drops all data)
anythink entities delete <name>
anythink entities delete <name> --yes        # skip confirmation
```

Flags for `create`:
- `--public` — allow unauthenticated reads
- `--rls` — enable row-level security
- `--lock-records` — prevent new records via API
- `--junction` — mark as a many-to-many join table

### Fields

```bash
# List fields on an entity
anythink fields list <entity>

# Add a field (interactive type selector if --type omitted)
anythink fields add <entity> <field_name>
anythink fields add products price --type float --label "Price" --required
anythink fields add users email --type varchar --unique --indexed --searchable
anythink fields add posts body --type text --display textarea
anythink fields add orders customer --type many-to-one   # FK relationship

# Delete a field by its numeric ID
anythink fields delete <entity> <field_id>
anythink fields delete products 42 --yes
```

Database types: `varchar`, `varchar[]`, `text`, `integer`, `integer[]`, `bigint`, `bigint[]`, `decimal`, `decimal[]`, `boolean`, `date`, `timestamp`, `jsonb`, `geo`, `file`, `user`, `one-to-one`, `one-to-many`, `many-to-one`, `many-to-many`, `dynamic-reference`

Display types: `input`, `textarea`, `rich-text`, `select`, `entity-select`, `radio`, `country-select`, `checkbox`, `short-date`, `long-date`, `timestamp`, `relationship`, `file`, `user`, `jsonb`, `geo`, `dynamic-reference`

### Data (Records)

```bash
# List records
anythink data list <entity>
anythink data list products --limit 50 --page 2
anythink data list orders --json                          # raw JSON output
anythink data list orders --filter '{"status":"pending"}'

# Get a single record by ID
anythink data get <entity> <id>

# Create a record
anythink data create <entity> --data '{"name":"Widget","price":9.99}'
anythink data create products                             # prompts for JSON

# Update a record
anythink data update <entity> <id> --data '{"status":"active"}'
anythink data update orders 123                           # prompts for JSON

# Delete a record
anythink data delete <entity> <id>
anythink data delete orders 123 --yes
```

### Workflows

```bash
# List all workflows
anythink workflows list

# Inspect a workflow (shows step chain)
anythink workflows get <id>

# Create a workflow
anythink workflows create "Daily Digest" --trigger Timed --cron "0 9 * * *"
anythink workflows create "On Order Created" --trigger Event --entity orders --event EntityCreated
anythink workflows create "Webhook Handler" --trigger Api --api-route "/handle-webhook"
anythink workflows create "Manual Cleanup" --trigger Manual

# Enable / disable
anythink workflows enable <id>
anythink workflows disable <id>

# Manually trigger a workflow
anythink workflows trigger <id>
anythink workflows trigger <id> --payload '{"dry_run":true}'

# Delete
anythink workflows delete <id>
anythink workflows delete <id> --yes
```

Trigger types: `Manual`, `Timed`, `Event`, `Api`
Event types: `EntityCreated`, `EntityUpdated`, `EntityDeleted`

### Users

```bash
# List all users in the project
anythink users list

# Inspect a user
anythink users get <id>

# Show the currently authenticated user
anythink users me

# Invite a new user (creates + sends invitation email)
anythink users invite alice@example.com Alice Smith
anythink users invite alice@example.com Alice Smith --role-id 3

# Delete a user
anythink users delete <id>
anythink users delete <id> --yes
```

### Secrets

Secrets are encrypted key/value pairs stored at the project level. Values are write-only — they are **never returned** to human users via the API. Secrets are injected into workflow steps at runtime by the platform.

Common uses: Claude API keys, Stripe secret keys, third-party webhook tokens, SMTP passwords.

```bash
# List all secret keys (metadata only — values never shown)
anythink secrets list

# Store a new secret (prompts for value with masked input)
anythink secrets create CLAUDE_API_KEY
anythink secrets create STRIPE_SECRET_KEY

# Rotate (overwrite) an existing secret value
anythink secrets update CLAUDE_API_KEY

# Delete a secret (warns that referencing workflows will break)
anythink secrets delete CLAUDE_API_KEY
anythink secrets delete CLAUDE_API_KEY --yes
```

**Referencing secrets in workflows:**
In a workflow `CallAnApi` step body or header, reference a secret as:
```
{{ $anythink.secrets.CLAUDE_API_KEY }}
```

### Roles

```bash
# List roles
anythink roles list

# Create a role
anythink roles create "Editor"

# Delete a role
anythink roles delete <id>
```

### Files

```bash
# List uploaded files
anythink files list
anythink files list --page 2 --limit 50

# Get file metadata
anythink files get <id>

# Upload a file
anythink files upload ./logo.png
anythink files upload ./report.pdf --public      # publicly accessible URL

# Delete a file
anythink files delete <id>
anythink files delete <id> --yes
```

### Pay (Stripe Connect)

```bash
# Check Stripe Connect status
anythink pay status

# Set up Stripe Connect (interactive — opens browser for onboarding)
anythink pay connect

# List payment transactions
anythink pay payments
anythink pay payments --page 2 --limit 50

# List saved payment methods
anythink pay methods
```

### OAuth (Google)

```bash
# Check current Google OAuth configuration
anythink oauth google status

# Configure Google OAuth credentials (interactive)
anythink oauth google configure
# Prompts for: enabled (y/n), Client ID, Client Secret (masked input)
# Outputs the callback URL to register in Google Cloud Console
```

The Google OAuth callback URL follows the pattern:
`{baseUrl}/org/{orgId}/auth/v1/google/callback`

### Config & Auth

```bash
anythink config list              # all saved profiles
anythink config current           # active profile details
anythink config use <name>        # switch active profile
anythink config remove <name>     # delete a saved profile
```

### Accounts & Projects

```bash
anythink accounts list            # list Anythink accounts tied to this login
anythink projects list            # list projects in the active account
```

### Miscellaneous

```bash
anythink docs                     # open docs in browser
anythink docs --json              # dump full command reference as JSON
anythink plans                    # show Anythink pricing plans
anythink migrate                  # run schema migrations on the active project
```

---

## Scaffolding a Complete Backend — Step by Step

When asked to scaffold a backend or data model with Anythink, follow this pattern:

### 1. Verify authentication
```bash
anythink config current
```
If not logged in: `anythink login`

### 2. Create entities (tables)

```bash
anythink entities create products --public
anythink entities create orders --rls
anythink entities create customers --rls
```

### 3. Add fields to each entity

```bash
# Products
anythink fields add products name --type varchar --required --searchable
anythink fields add products description --type text
anythink fields add products price --type float --required
anythink fields add products stock --type integer --default 0
anythink fields add products is_active --type boolean --default true

# Customers
anythink fields add customers email --type varchar --required --unique --indexed
anythink fields add customers first_name --type varchar --required
anythink fields add customers last_name --type varchar

# Orders
anythink fields add orders status --type varchar --required
anythink fields add orders total --type float
anythink fields add orders customer_id --type many-to-one   # FK to customers
```

### 4. Create automation workflows

```bash
# Timed data sync
anythink workflows create "Sync Inventory" \
  --trigger Timed --cron "0 */6 * * *" --enabled

# React to new orders
anythink workflows create "Order Confirmation" \
  --trigger Event --entity orders --event EntityCreated --enabled
```

### 5. Invite team members
```bash
anythink users invite dev@company.com Dev Team --role-id 2
```

---

## Common Recipes

### Check what's in a table
```bash
anythink data list <entity> --json | head -100
```

### Quickly count records
```bash
anythink data list <entity> --limit 1   # header shows total count
```

### Disable all workflows before a migration
```bash
# Get IDs from list, then:
anythink workflows disable 76
anythink workflows disable 77
anythink migrate
anythink workflows enable 76
anythink workflows enable 77
```

### Audit users before a release
```bash
anythink users list
anythink users me
```

### Upload a batch of assets
```bash
for f in ./assets/*.png; do anythink files upload "$f" --public; done
```

---

## Field Types — Quick Reference

| Goal | `--type` | `--display` |
|------|----------|-------------|
| Short text / name | `varchar` | `input` |
| Dropdown / enum (single) | `varchar` | `select` |
| Multi-select / tag list | `varchar[]` | `select` |
| Long description | `text` | `textarea` |
| Rich / HTML content | `text` | `rich-text` |
| Whole number | `integer` | `input` |
| Multi-select integers | `integer[]` | `select` |
| Decimal / price | `decimal` | `input` |
| True/false toggle | `boolean` | `checkbox` |
| Boolean radio buttons | `boolean` | `radio` |
| Date only | `date` | `short-date` |
| Date + time | `timestamp` | `timestamp` |
| Structured JSON | `jsonb` | `jsonb` |
| Geographic coordinates | `geo` | `geo` |
| File upload / attachment | `file` | `file` |
| User reference | `user` | `user` |
| Belongs to (FK) | `many-to-one` | `relationship` |
| Has many (reverse) | `one-to-many` | `relationship` |
| One-to-one | `one-to-one` | `relationship` |
| Many-to-many | `many-to-many` | `relationship` |
| Polymorphic reference | `dynamic-reference` | `dynamic-reference` |
| Country picker | `varchar` | `country-select` |
| Entity dropdown | `varchar` | `entity-select` |

---

## Workflow Trigger Types

| Trigger | When it fires | Key options |
|---------|---------------|-------------|
| `Manual` | Only when `workflows trigger <id>` is called | `--payload` JSON |
| `Timed` | On a cron schedule | `--cron "0 9 * * *"` |
| `Event` | When a record is created/updated/deleted | `--entity`, `--event` |
| `Api` | When an external HTTP request hits the route | `--api-route` |

---

## Environment Variables (for CI/CD)

| Variable | Purpose |
|----------|---------|
| `ANYTHINK_ORG_ID` | Organisation / project ID |
| `ANYTHINK_BASE_URL` | API base URL (e.g. `https://api.my.anythink.dev`) |
| `ANYTHINK_API_KEY` | Long-lived API key (preferred for automation) |
| `ANYTHINK_TOKEN` | Short-lived JWT (for user-interactive sessions) |

When environment variables are set the CLI uses them automatically — no profile needed. `ANYTHINK_API_KEY` takes precedence over `ANYTHINK_TOKEN` and never expires.

---

## Notes & Gotchas

- **`--yes` flag**: All destructive commands (`delete`) require confirmation unless `--yes` is passed. Safe to use in scripts.
- **Field IDs are integers**: `fields delete` takes the numeric field ID (visible in `fields list`), not the field name.
- **Relationship fields**: Use `many-to-one` to add a foreign key column. The platform creates the constraint automatically.
- **Workflow step editing**: The CLI creates and deletes workflows; individual step configuration is done in the Anythink UI or via the REST API.
- **`--json` flag**: Available on `data list` — useful for piping output to `jq` or saving snapshots.
- **RLS (Row-Level Security)**: Enable with `--rls` to restrict data access to the authenticated user's own records.
- **Public entities**: `--public` allows unauthenticated GET requests — suitable for product catalogues, public content, etc. Do not use for sensitive data.
- **API keys vs tokens**: Use API keys (`ak_...`) for server-side integrations and CI/CD. Use transfer tokens / login flow for interactive use.
