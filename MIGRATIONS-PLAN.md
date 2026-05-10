# Migrations Plan

Working plan for **Supabase → Anythink** and **Directus → Anythink** migration support in
`anythink-cli`, plus the existing **Anythink → Anythink** env-to-env migration.

Last reviewed: 2026-05-10.

---

## 1. Where things stand

### Existing Anythink → Anythink (`anythink migrate`)
[src/Commands/MigrateCommand.cs](src/Commands/MigrateCommand.cs) — feature-complete,
in active use for env-to-env (e.g. `getahead-staging` → `getahead-prod`).

Handles entities, fields, workflows (with step-link remap), roles + permissions, org
settings, menus (with org-ID href remap), files (URL re-upload), and data records
(with FK/file remap, two-pass for one-to-many/many-to-many fields). Includes
`--dry-run`, `--include-*` selectors, `--force-data-entities`. Solid reference for
shape of any new importer.

### Directus → Anythink (`anythink import directus`) — WIP, **not wired up**
On this branch (`migrate-directus`):
- [src/Client/DirectusClient.cs](src/Client/DirectusClient.cs) — read-only REST client (collections, fields, flows, operations).
- [src/Models/DirectusModels.cs](src/Models/DirectusModels.cs) — record types for the four endpoints above.
- [src/Commands/ImportDirectusCommand.cs](src/Commands/ImportDirectusCommand.cs) — schema + flows importer with field-type and trigger mapping. Does NOT migrate data.
- [src/Commands/FetchCommand.cs](src/Commands/FetchCommand.cs) — generic raw-API helper (useful for debugging both sides).

`ImportDirectusCommand` is **never registered in [src/Program.cs](src/Program.cs)** —
hence "we never tested the proper directus migration." Compiles, but unrunnable.

### Supabase → Anythink — does not exist yet.

### Branch hygiene
`migrate-directus` is **11 commits behind `public/main`** — the fork point predates
the open-source release, so all the recent CLI improvements (search, integrations,
step-delete, billing, OAuth v2, etc.) are missing here. **Don't merge this branch
forward** — port the four Directus files onto a fresh branch from `public/main`
instead, and treat this branch as a reference.

---

## 2. Goals (in priority order)

1. **Directus → Anythink** schema + data + flows, working end-to-end against a real
   Directus instance.
2. **Supabase → Anythink** schema + data, working end-to-end against a real Supabase
   project.
3. **Side-by-side perf**: same fixture dataset, time both migrations, record results.
4. **Surface in Breeze UI** as a one-click migration wizard (calls into the same
   importer logic — extract a service if needed).

Anythink → Anythink already covers env-to-env; no new work needed there other than
making sure new importers reuse its patterns (FK remap, file remap, dry-run, etc.).

---

## 3. Test environment options

### Directus
Local Docker — official `directus/directus:11.x` + Postgres, seeded with a
representative schema (6 collections incl. junction, m2o relations, an
event-trigger flow, ~30 records).

Lives **outside this repo** at `../test-instances/directus/` so we don't ship
fixtures with the CLI. See `../test-instances/README.md` for usage.

### Supabase
**Two options to evaluate:**
- **Local:** `supabase` CLI (`supabase start`) — full local stack, deterministic.
- **Remote:** spin up a free-tier Supabase project, talk to it via MCP for setup.

Local wins for repeatable perf tests; remote wins for testing real-world latency.
Probably want both, with local as the default for CI.

### Anythink target
Use a dedicated test project on staging (`anythink-staging-migrations` or similar)
that we wipe between runs. Don't run against prod.

---

## 4. Architecture (current)

Importers live under `src/Importers/`, separated from the command shell:

```
src/Importers/
├── Common/
│   ├── IPlatformImporter.cs   # interface every platform implements
│   ├── ImportSchema.cs         # platform-agnostic records — collection,
│   │                            #   field, flow, step (already mapped to
│   │                            #   Anythink vocabulary)
│   └── ImportRunner.cs         # orchestrator: fetch existing target,
│                                #   create-or-merge entities, add fields,
│                                #   create flows, summary
└── Directus/
    ├── DirectusClient.cs       # Directus REST client
    ├── DirectusModels.cs       # request/response records
    ├── DirectusFieldMapping.cs # db_type + display_type mapping (db-aware)
    ├── DirectusFlowMapping.cs  # trigger + WorkflowAction mapping
    └── DirectusImporter.cs     # implements IPlatformImporter

src/Commands/
└── ImportDirectusCommand.cs    # ~60 lines: parse args → DirectusImporter →
                                  ImportRunner
```

Adding a new platform = a new subdirectory under `src/Importers/`
containing client + models + mapping + importer, plus a thin
`Import<Platform>Command` shell. Runner and shared types stay untouched.

The runner handles all target-side concerns (existing-entity detection,
field merge, error collection, dry-run plan rendering, summary), so each
platform just declares "here's my schema in Anythink terms" and gets
applied uniformly.

## 5. Patterns to inherit from MigrateCommand for data import (M4+)

| Concern | Pattern from MigrateCommand |
|---|---|
| Two-pass fields | Create FK columns first, defer one-to-many/many-to-many |
| FK remap | Build `srcId → name` and `name → dstId` maps, remap as records insert |
| File remap | Upload first, build `srcFileId → dstFileId`, rewrite refs in record payloads |
| Cycles | `dataIdMap[entity][srcId] = dstId`, drop unresolvable FKs rather than fail |
| Errors non-fatal | Collect into `errors` list, summary at end, exit 2 if any failed |
| Dry-run | Same code path, skip writes, report counts |
| Progress | Spectre `Progress()` with a task per scope |

Common importer flags:
- `--source-url` / `--source-token` (or env-var fallback)
- `--to <profile>` (target Anythink profile)
- `--dry-run`
- `--include-data` / `--include-flows`
- `--force-data-entities <csv>` for re-runs

---

## 5. Milestones

**M1 — Branch refresh** (no functional change yet)
- Cut a fresh `feat/migrations` branch from `public/main`.
- Cherry-pick the 4 Directus files from `baac90c`.
- Wire `ImportDirectusCommand` and `FetchCommand` into `Program.cs`
  (`anythink import directus`, `anythink fetch`).
- Confirm `--help` shows them; confirm build is green.

**M2 — Local Directus environment**
- Add `docs/test-fixtures/directus/docker-compose.yml` and a seed script.
- Document in README under a new "Migrating from Directus" section.
- Manual smoke test: stand up Directus, run `anythink import directus --dry-run`,
  confirm the plan table looks right.

**M3 — Directus schema migration (real run)**
- Run against the seeded fixture: 10–15 collections, ~50 fields total.
- Diff source vs. target schema; fix any field-type mismatches.
- Add unit tests for `MapDatabaseType` / `MapDisplayType` mapping tables.

**M4 — Directus data migration**
- Add a `--include-data` path to `ImportDirectusCommand` mirroring
  `MigrateCommand`'s data section: paginate `/items/<collection>`, FK remap, file
  remap (upload from Directus assets endpoint), insert into Anythink.
- Test with seeded data (~1k records mixed across collections).

**M5 — Supabase importer**
- Add `SupabaseClient` (PostgREST + service role key, or direct PG via `Npgsql`).
- Read schema from `information_schema.tables` / `columns` /
  `pg_catalog.pg_constraint`.
- New `anythink import supabase` command. Same shape as Directus importer.
- Test with a seeded local Supabase project.

**M6 — One-click**
- `anythink import <platform>` with sensible auto-detection of token/url from env
  (`DIRECTUS_URL`, `DIRECTUS_TOKEN`, `SUPABASE_URL`, `SUPABASE_SERVICE_ROLE_KEY`).
- Single command runs schema + data + flows by default; flags to opt out.
- Final summary table and a "what to do next" pointer.

**M7 — Performance harness**
- Fixed fixture: 10k records, 50 collections, 5 file-bearing fields.
- Wall-clock per phase (schema, data, files, flows).
- Run on each platform; record results in `docs/test-fixtures/perf-results.md`.
- Helps us prove "1 click in N seconds" claims and identify bottlenecks.

**M8 — Breeze UI integration**
- Decide: shell out to CLI binary, or extract a `MigrationService` library?
  Library is cleaner; CLI shell-out is faster to ship. Probably ship CLI shell-out
  first, refactor to library once we know the UX shape.
- Wizard: pick source platform → enter creds → choose scope → progress stream →
  summary.

---

## 6. Decisions (2026-05-10)

1. **Order of attack**: **Directus first.** Scaffolding exists, M1–M3 are mostly
   plumbing already done; patterns transfer directly to Supabase afterwards.
2. **Flows in v1**: **Yes, include.** `ImportDirectusCommand` already maps flows →
   workflows. Action mapping is best-effort — flag any unmapped types in the
   summary so the user can fix them up by hand.
3. **Test target**: **Dedicated staging org.** A long-lived
   `anythink-migrations-test` org on staging; wipe between runs. Throwaway via
   signup-from-CLI is fine for one-offs but not the default.
4. **Supabase access (when we get to M5)**: **Direct PG via Npgsql.**
   - Schema introspection needs `information_schema` + `pg_catalog` for FK
     constraints, types, defaults, indexes — PostgREST can't surface this cleanly.
   - Supabase publishes the PG connection string in the dashboard; same auth
     surface as the service role key.
   - No row caps; streaming `COPY`/cursor reads scale to large tables.
   - Same Npgsql code becomes a "migrate from any Postgres" path later (RDS,
     Neon, self-hosted). Supabase is a special case with known schema layout.
   - Free-tier firewall caveat: use the pgbouncer pooler URL
     (`db.<ref>.supabase.co:6543`). Document in the README.

---

## 7. Backlog items spawned

- Existing `MigrateCommand` doesn't migrate **integrations** or **API keys** —
  worth adding once integration migration is well-understood from the Slack/SES
  work.
- `MigrateCommand` doesn't currently re-run idempotently for **fields** that were
  added since last run on existing entities — only skips by full-entity-exists.
  Worth a fix once we're touching that file.
