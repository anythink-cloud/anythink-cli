# Anythink command map

The **authoritative, always-current** source is the platform itself — run
`docs --json` (full reference for AI tooling) or `<group> --help` (exact flags)
through the `cli` tool. This file is a map of what exists so you know where to
look; run any of it via the MCP `cli` tool with the string after `anythink`
(e.g. `data list posts --json`). Add `--yes` to destructive commands and
`--json` where you need to parse output.

## Auth & context (dedicated MCP tools + CLI)
| Command | Purpose |
| --- | --- |
| `signup` / `login` / `logout` | Create account, log in, remove a saved profile |
| `config show` | Profiles + the active account/project |
| `config use <profile>` / `config remove <profile>` | Switch / remove a profile |
| `accounts list` / `accounts create` / `accounts use <id>` | Billing accounts |
| `projects list` / `projects create <name>` / `projects use <id>` / `projects delete <id>` | Projects (most commands act on the active one) |

`projects create` flags: `--region <id>` (e.g. `lon1`), `--plan <id>`.

## Schema
| Command | Purpose |
| --- | --- |
| `entities list` / `entities get <name>` / `entities create <name>` / `entities update <name>` / `entities delete <name>` | Collections/tables |
| `fields list <entity>` / `fields add <entity> <name> --type <type> [--required --unique]` / `fields delete <entity> <id>` | Columns |

`entities create` flags: `--rls` (row-level security), `--public` (publicly
readable), `--lock` (block direct record creation), `--junction` (many-to-many
join table). See `references/data-modeling.md`.

## Data & search
| Command | Purpose |
| --- | --- |
| `data list <entity> [--limit N --json]` / `data get <entity> <id>` | Read records |
| `data create <entity> --data '<json>'` / `data update <entity> <id> --data '<json>'` / `data delete <entity> <id>` | Write records |
| `search query "<text>" [--filter ... --sort ...]` | Full-text/filtered search; `_geoRadius(lat,lng,m)` for geo |
| `search similar <entity> <id>` | Semantic / vector similarity |
| `search rehydrate [<entity>]` / `search purge [<entity>]` / `search audit <entity>` | Index admin |

## Access control
| Command | Purpose |
| --- | --- |
| `roles list` / `roles create <name> [--description ...]` / `roles delete <id>` | Roles |
| `users list` / `users me` / `users get <id>` / `users invite <email> <first> <last> [--role-id N]` / `users delete <id>` | End-users |
| `api-keys list` / `api-keys create <name> --permissions "<e>:<a>,..." [--save-as <profile>]` / `api-keys revoke <id>` | API keys (value prints to **stderr**) |

Permissions are `{entity}:{action}` — see `references/access-control.md`.

## Automation & integrations
| Command | Purpose |
| --- | --- |
| `workflows list` / `get <id>` / `create <name> [--trigger Event\|Timed\|Manual\|Api --cron ...]` / `enable\|disable\|trigger <id>` / `delete <id>` | Workflow engine |
| `integrations list` / `get <provider>` / `connect <provider> [--name ...]` / `execute <provider> <operation> --input "k=v"` | Connect & call providers (Claude, OpenAI, Slack, Google, GitHub…) |
| `integrations oauth status\|configure\|connect <provider>` · `integrations connections list` · `test\|enable\|disable\|disconnect <connection-id>` | OAuth provider connections |
| `secrets ...` | Encrypted secrets for workflows/integrations |

## Content & platform
| Command | Purpose |
| --- | --- |
| `files list` / `get <id>` / `upload <path> [--public]` / `delete <id>` | File storage |
| `menus list` / `menus add-item <menu_id> <entity> [--icon <Icon> --parent <id>]` | Dashboard navigation |
| `pay status` / `connect` / `payments` / `methods` | Payments (Stripe Connect) |
| `oauth google status\|configure` | First-party OAuth provider config |
| `plans` | List plans |
| `migrate --from <profile> --to <profile> [--dry-run]` | Move schema/data between projects |
| `api [--json]` | List REST endpoints |
| `fetch <path>` | Raw API request |
| `docs [--json]` | Full reference (markdown / JSON) |

## Conventions
- Most commands act on the **active project** — `projects use` first.
- JSON payloads via `--data '<json>'`; machine-readable output via `--json`.
- `--yes` skips confirmation on destructive commands.
- Don't pass `--profile` through the MCP (injected automatically).
- Prefer `<group> --help` / `docs --json` over guessing — the surface evolves.
