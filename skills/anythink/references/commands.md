# Anythink command reference

The authoritative, always-current source is `anythink <group> --help` (or the MCP
`cli` tool with `<group> --help`). This file is a map of the surface so you know
what exists and where to look. Run it the same way through MCP: call the `cli`
tool with the command string (e.g. `entities create posts`).

## Auth & context
| Command | Purpose |
| --- | --- |
| `signup` / `login` / `logout` | Create account, log in, remove a saved profile |
| `login_direct` (MCP) | Authenticate with org id + API key/JWT (non-interactive) |
| `config show` | List profiles and show the active account/project |
| `config use <profile>` | Switch the active profile |
| `config remove <profile>` | Remove a saved profile |

## Accounts & projects (set context before building)
| Command | Purpose |
| --- | --- |
| `accounts list` / `accounts create` | List or create billing accounts |
| `accounts use <id>` | Set the active billing account |
| `projects list` / `projects create <name>` | List or create projects |
| `projects use <id>` | **Connect to a project** — most commands act on the active one |
| `projects delete <id>` | Delete a project (irreversible — confirm with user) |

## Schema
| Command | Purpose |
| --- | --- |
| `entities list` / `entities create <name>` | Collections/tables in the active project |
| `entities delete <name>` | Drop an entity (irreversible) |
| `fields list <entity>` | Fields on an entity |
| `fields create <entity> <name> --type <type>` | Add a field (text, number, boolean, date, etc.) |

## Data & search
| Command | Purpose |
| --- | --- |
| `data list <entity>` | List records |
| `data create <entity> --data '<json>'` | Create a record |
| `data update <entity> <id> --data '<json>'` | Update a record |
| `data delete <entity> <id>` | Delete a record |
| `search <entity> --query "<q>"` | Full-text/filtered search; supports sorting & geo radius |

## Access control
| Command | Purpose |
| --- | --- |
| `roles list` / `roles ...` | Manage roles and their permissions |
| `users list` / `users ...` | Manage end-users of the project |
| `api-keys create --name <n> [--expires <days>]` | Issue an API key (don't echo the value) |
| `api-keys revoke <id>` | Revoke a key |

## Features
| Command | Purpose |
| --- | --- |
| `workflows ...` | Automations / triggers |
| `files ...` | File storage |
| `integrations ...` | Connect external providers (Claude, OpenAI, Slack, Google, etc.) |
| `pay ...` | Payments / plans / subscriptions |
| `oauth ...` | OAuth connection flows |
| `email ...` | Email templates |
| `menus ...` | Navigation menus |
| `plans ...` | Billing plans |
| `migrate ...` | Data/schema migration |
| `api ...` | Low-level API access |
| `docs ...` | Built-in documentation |

## Conventions
- Most commands act on the **active project** — always `projects use` first.
- JSON payloads go via `--data '<json>'`.
- Prefer `--help` over guessing flags; the surface evolves.
- Treat key/secret output as sensitive — let the user copy values.
