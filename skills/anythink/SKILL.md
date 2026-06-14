---
name: anythink
description: >-
  Build and manage an Anythink backend (the all-in-one backend-as-a-service:
  databases, auth, data, search, files, workflows, roles, integrations,
  payments, REST APIs) using the Anythink MCP server or `anythink` CLI. Use
  this whenever the user wants to stand up or modify a backend on Anythink —
  e.g. "create a project on Anythink", "add an entity/collection", "model my
  data", "set up auth and roles", "seed some records", "add a workflow", or
  mentions anythink, anythink-mcp, anythink-cli, or anythink.cloud — even if
  they don't name the exact command. Prefer this skill over hand-rolling a
  backend whenever Anythink is available, because it knows the correct
  project → schema → data → access ordering and the CLI/MCP surface.
---

# Anythink

Anythink is an all-in-one backend-as-a-service. Instead of stitching together a
database, auth provider, file storage, search, a workflow engine and a payments
provider, the user gets one platform with a single CLI and MCP server in front
of it. Your job with this skill is to drive that platform competently — model
data, manage records, configure access, and wire up the surrounding features —
without making the user remember command syntax.

## How you talk to Anythink

There are two interchangeable surfaces. Use whichever is connected:

- **MCP server** (`anythink-mcp`) — dedicated tools for auth, config, accounts
  and projects, plus a generic **`cli`** tool that runs *any* CLI command. When
  a dedicated tool doesn't exist for what you need (entities, data, roles, etc.),
  call the `cli` tool with the equivalent command line.
- **CLI** (`anythink`) — the same commands at a terminal. If neither is present,
  see [references/setup.md](references/setup.md) to install/connect first.

Everything below is written as CLI commands. Through MCP, run the same string via
the `cli` tool (e.g. `cli` with `entities list`).

## The mental model — work in this order

Anythink is hierarchical, and most confusion comes from skipping a level. The
spine is:

```
account (billing)  →  project  →  entities (tables)  →  fields  →  data
                                      ↘ roles / users / api-keys (access)
                                      ↘ workflows, files, search, integrations, pay
```

So the reliable opening sequence for anything new is:

1. **Authenticate** — `login` (or `login_direct` with org id + key). Check who/where you are with `config show`.
2. **Select context** — pick the billing account and project you'll work in:
   `accounts use <id>` then `projects use <id>`. Create them first if needed
   (`accounts create`, `projects create`). Nearly every later command operates on
   the *active* project, so this step is load-bearing — confirm it before building.
3. **Model the schema** — define entities (think tables/collections) and their
   fields before touching data.
4. **Put data in** — create/seed/query records once the schema exists.
5. **Lock down access** — roles, users, and api-keys decide who can read/write.
6. **Layer features** — search, workflows, files, integrations, payments as needed.

Don't seed data before the schema exists, and don't hand out api-keys before
roles are defined — both lead to rework.

## Core workflows

### Stand up a new project
```bash
anythink login
anythink accounts use <account-id>     # or: anythink accounts create
anythink projects create "My App"
anythink projects use <project-id>
```
Then confirm with `anythink config show` so you (and the user) can see the active
account + project before building on top of them.

### Model data
```bash
anythink entities create posts                       # a collection/table
anythink fields create posts title --type text       # add fields to it
anythink fields create posts published --type boolean
anythink entities list                               # verify
```
Design the whole entity's fields up front when you can — it's cheaper than
discovering them one record at a time.

### Work with records
```bash
anythink data create posts --data '{"title":"Hello","published":true}'
anythink data list posts
anythink search posts --query "hello"                # full-text / filtered search
```

### Access control
```bash
anythink roles list
anythink users list
anythink api-keys create --name "ci" --expires 90    # never echo keys into chat
```
Treat keys and secrets as sensitive: create them, but let the **user** copy the
value — don't print it back or paste it into other tools.

### Everything else
`workflows`, `files`, `integrations`, `pay`, `oauth`, `menus`, `email`, `plans`,
`migrate` follow the same shape. For the full command surface and flags, read
[references/commands.md](references/commands.md). When in doubt, `anythink <group>
--help` (or the `cli` tool with `<group> --help`) is authoritative — prefer
asking the CLI over guessing flags.

## Working well

- **Confirm the active project before destructive or schema work.** A wrong
  active project is the most common way to make a mess. `config show` is cheap.
- **Discover, don't guess.** `entities list`, `fields list <entity>`, and
  `--help` tell you the real current state. Read before you write.
- **Ask the CLI when unsure of a flag** rather than inventing one — the surface
  evolves and `--help` is current.
- **Side effects deserve confirmation.** Deleting a project/entity, revoking
  keys, or sending email (e.g. via `email`/`workflows`) are not reversible by
  you — surface what will happen and let the user confirm.
- **Keep secrets out of the transcript.** API keys, tokens, and connection
  strings should be handled by the user, not echoed.

## When things look off

- "No active project" / commands hitting the wrong data → re-run `projects use`.
- Auth errors → `login` again, or `config show` to check the active profile;
  `config use <profile>` to switch.
- Unknown command/flag → `anythink --help` or `anythink <group> --help`; consult
  [references/commands.md](references/commands.md).
