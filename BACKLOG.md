# Backlog

Outstanding issues and follow-ups discovered while building features. Living
document — items move out as they're picked up.

## CLI

- **Spectre markup escape on user-supplied strings.** `Renderer.Warn`,
  `Renderer.Info`, `Renderer.Success` interpolate their `msg` argument straight
  into `MarkupLine`. When that argument is an API error message containing
  `[Something]` (e.g. "Authentication scheme `[SomeAuthHandler]` not
  configured"), Spectre tries to parse it as a markup tag and crashes with
  `"Could not find color or style 'SomeAuthHandler'."`.
  Repro: `dotnet run -- projects use` against a target where the platform
  call returns a bracket-containing error.
  Fix: `Markup.Escape` the user-supplied parts at every call site (e.g.
  `Renderer.Warn($"... {Markup.Escape(ae.Message)}")`). `Renderer.Error`
  already escapes its full input.

- **Saved platform config corruption** — `myanythink_org_id` field can end up
  containing the entire shell invocation, e.g. `"19101998 MYANYTHINK_API_URL=https://localhost:7136 BILLING_API_URL=https://localhost:7180 dotnet run -- login"`.
  Likely happens when an env-var-prefixed invocation hits the SavePlatform
  path with an unsanitized value. Worth a defensive validator on the org-id
  format before persisting.

- **Local AnyAPI signup missing required claims for `login`.** Signing up
  against `https://localhost:7136` works, but a subsequent `login --email
  ... --password ...` against the same instance returns 404. Need to
  reproduce + diagnose; symptom is the central `myanythink` org isn't
  reachable on local for the login endpoint.

- **Multi-environment auth.** Today the central `platform` block in
  `~/.anythink/config.json` is a single slot — you can be authenticated
  against production OR a lower environment, but not both. Per-project
  profiles already carry `instance_api_url`/tokens so they refresh
  against the correct environment, but the central session does not.
  We need:
  - A way to be logged in to multiple environments concurrently
    (e.g. `platforms.production`, `platforms.local`, etc., keyed by the
    URLs they were authenticated against).
  - Env vars (`MYANYTHINK_API_URL`, `BILLING_API_URL`) determine which
    environment a fresh `login` targets — but after that, the URLs are
    saved and refresh uses them; env vars are only relevant at
    initial-auth time.
  - Public CLI must NOT advertise env-var override capability (we don't
    want end users connecting to lower environments). Keep them as
    contributor-only knobs documented in `.env.example` only.
  - Consider an explicit `anythink env use <name>` (or similar) for
    contributors to switch which platform session is "active".

## Importers

- **Directus → Anythink data import (M4).** `import directus` is
  schema-only. Need to copy records: paginate `/items/<collection>`, build
  `srcId → dstId` maps per entity, remap FK fields, handle file fields,
  insert into Anythink. Mirror the patterns in `MigrateCommand.cs` (FK
  remap, two-pass field create, force-data flags).

- **Junction collections.** Currently `articles_tags`-style hidden
  collections come through as plain entities. Once data import is in,
  Anythink m2m relationships should be modelled via real
  `many-to-many` field types pointing at junction entities, not raw
  pairs of `m2o` columns.

- **Workflow action mapping is best-effort.** Directus `log` →
  `RunScript`, `transform` → `RunScript`, `sleep` → `RunScript` —
  these are placeholders; the user needs to fix them up after import.
  Surface these in the summary so they're easy to find.

- **Junction collection naming.** Directus convention is
  `<a>_<b>` with `<a>_id`/`<b>_id` columns; Anythink expects junction
  entities flagged via `is_junction` and tied to relationship fields. Map
  these properly during import rather than as plain entities.

## Platform (AnyAPI)

- **AuthN handler misconfigured locally.** The `[SomeAuthHandler]` error
  comes from AnyAPI returning a malformed auth-scheme name. Suggests
  the local Auth pipeline has an unregistered scheme name being
  surfaced into client error messages. Worth checking on the AnyAPI
  side too.
