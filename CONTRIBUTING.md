# Contributing

Thanks for looking at `anythink-cli`. This document covers the CI pipeline, what
each check does, and the one-time setup required for the optional nightly
integration run.

## Project layout

- `src/` — the `anythink` CLI (`AnythinkCli.csproj`)
- `mcp/` — the `anythink-mcp` MCP server (`AnythinkMcp.csproj`), shipped as a
  dotnet tool and, via the `npm/` wrapper, through npm
- `tests/` — unit/contract tests for the CLI (`dotnet test tests/AnythinkCli.Tests.csproj`)
- `tests-mcp/`, `mcp-tests/` — unit, blocked-tool, and conformance tests for the
  MCP server (`dotnet test tests-mcp/AnythinkMcp.Tests.csproj` and
  `dotnet test mcp-tests/AnythinkMcp.Tests.csproj`) — two separate projects,
  both named `AnythinkMcp.Tests`; run them as separate `dotnet test` invocations,
  not together, since they produce same-named output assemblies
- `scripts/ci/` — standalone scripts used by CI (currently the public-repo
  hygiene check); runnable locally with plain Node, no install step

## Running everything locally

```bash
dotnet build AnythinkCli.sln -c Release

dotnet test tests/AnythinkCli.Tests.csproj -c Release
dotnet test tests-mcp/AnythinkMcp.Tests.csproj -c Release
dotnet test mcp-tests/AnythinkMcp.Tests.csproj -c Release

dotnet format src/AnythinkCli.csproj --include <changed files under src/> --verify-no-changes

node --test scripts/ci/check-public-hygiene.test.js
git diff main... | node scripts/ci/check-public-hygiene.js -

dotnet pack mcp/AnythinkMcp.csproj -c Release -o /tmp/nupkg
dotnet pack src/AnythinkCli.csproj -c Release -o /tmp/nupkg
(cd npm && npm pack --dry-run)
```

## What runs on a PR (`.github/workflows/pr-checks.yml`)

Every pull request and every push to `main` runs:

| Job | What it does | Typical time |
| --- | --- | --- |
| `test` (matrix: ubuntu-latest, macos-latest) | Restores, builds with warnings visible, runs all three test projects, uploads TRX results and coverlet coverage as artifacts, and writes a pass/fail summary to the job summary | ~2-3 min per OS |
| `format` (PRs only) | `dotnet format --verify-no-changes`, scoped to the `.cs` files the PR actually changed (see below) | ~30s |
| `hygiene` (PRs only) | Runs the hygiene script's own tests, then scans the PR's added lines for obvious secrets, internal vocabulary in user-facing text where "project" is the public term (docs and CLI strings only; code identifiers are fine), and the maintainers' private patterns (see below) | ~10s |
| `mcp` | Builds the MCP server and packs both dotnet tools (`anythink-mcp`, `anythink`) plus a `npm pack --dry-run` of the npm wrapper, to catch packaging breaks before a release ships them. (The MCP server's own tests, including the stdio conformance smoke test, run as part of the `test` job above — they're ordinary test classes in `tests-mcp/`.) | ~1 min |
| `all-checks` | A single named gate the release pipeline depends on (see below) | instant |

A newer push to the same PR/branch cancels the previous run (`concurrency` with
`cancel-in-progress: true`).

### Public-repo hygiene

`scripts/ci/check-public-hygiene.js` holds only generic checks. Names that must
never appear in this repo are kept out of it: maintainers store them, one regex
per line, in the `PUBLIC_HYGIENE_PATTERNS` repository secret, which the
`hygiene` job passes in. Findings refer to these as "private pattern N" so the
log never prints the pattern itself. PRs from forks don't receive secrets, so
they get the generic checks only.

### Code formatting

`dotnet format AnythinkCli.sln --verify-no-changes` currently flags about 25 of
60 `.cs` files in the repo — pre-existing whitespace/analyzer drift, not
anything this change introduced. Gating every PR on the whole repo being clean
would block unrelated work on debt it didn't create, so the `format` job only
checks files the PR itself changed, one `dotnet format <project> --include
<files>` call per project. The debt shrinks as files get touched, rather than
landing on whoever opens the next PR. A dedicated repo-wide cleanup PR is
tracked separately.

### MCP conformance smoke test

`tests-mcp/McpStdioConformanceTests.cs` launches the real `anythink-mcp`
binary in stdio mode (the mode Claude Code/Desktop actually use), speaks
newline-delimited JSON-RPC 2.0 to it over its stdin/stdout exactly as a real
client would, and asserts:

1. `initialize` returns `serverInfo.name == "anythink"` and advertises tool support;
2. `tools/list` includes the expected tool names (login, config, accounts, projects, `cli`, ...);
3. `tools/call` against the harmless, read-only `config_show` tool (run
   against an isolated temp `$HOME`, never your real `~/.anythink`) returns
   `isError: false` with the expected shape.

## Release pipeline

`auto-tag.yml` runs when PR Checks finishes on a push to `main`, and only
tags and releases if it passed, checking out that exact commit. A red `main`
doesn't ship.

Add `[skip release]` anywhere in a commit message on `main` to skip the release
for that push (checked against the triggering `workflow_run`'s head commit).

## Optional nightly integration run (`.github/workflows/nightly-integration.yml`)

Runs real `anythink` CLI commands (login, create/get/delete an entity) against
a live staging Anythink project, not fixtures. It is **disabled until you add
the required secrets** — every job checks for them first and exits quietly
(with a `::notice::`) if they're missing, so merging this workflow file is
safe even before staging exists.

To enable it, add these repository (or environment) secrets:

| Secret | What it is |
| --- | --- |
| `ANYTHINK_CI_API_URL` | Base API URL of the staging Anythink project |
| `ANYTHINK_CI_ORG_ID` | Org/project ID of the staging project |
| `ANYTHINK_CI_API_KEY` | An API key (`ak_...`) scoped to the staging project, with permission to create/read/delete entities |

Every entity the run creates is tagged with a run-specific prefix
(`ci_nightly_<timestamp>_<run id>`) and deleted in a cleanup step that runs
even if the test step fails, so a flaky run doesn't leave garbage in staging.

It also runs on a schedule (`03:17 UTC` daily) and via manual
`workflow_dispatch`.
