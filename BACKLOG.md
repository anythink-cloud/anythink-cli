# CLI Backlog

Outstanding work captured during the integrations + workflows step-delete development.
Items are split between things that live in this repo (CLI fixes / new commands) and
things that need server-side changes on the platform repo.

## CLI repo — fixes & new commands

- [ ] **Search commands** — wrap `SearchController` (authenticated search, rehydrate /
      purge index, similar docs, faceted search, geo-filter). High value: index ops
      are natural CLI tasks.
- [ ] **Email templates** — `email-templates list / get / update / preview` wrapping
      `EmailTemplateController`. Useful for customisation and backup/restore.
- [ ] **`workflows trigger` payload bug** — current implementation wraps user JSON as
      `{ data: parsed }` rather than building the proper `TriggerWorkflowRequest`
      `{ entity_name, entity_id, data: <stringified> }`. Confirmed broken when we
      tried to manually trigger workflow 43 during integrations testing.
- [ ] **`workflows step-add --interactive`** — prompt-driven step authoring; today
      users have to know action types and write JSON parameters by hand.
- [ ] **`workflows clone`** — clone an existing workflow as a starting point.
- [ ] **`workflows step-delete --force-relink`** — auto-clear inbound `on_success_step_id`
      / `on_failure_step_id` references before deleting (currently the user has to
      run `workflows step-link` manually for each inbound link).
- [ ] **Charts** — `charts list / create / update / delete` wrapping
      `ChartConfigurationController`. Lower priority; mostly UI-driven.
- [ ] **Feature flags** — `feature-flags list` wrapping `FeatureFlagController`.
      Niche; could just use `fetch` for ad-hoc reads.

## Platform repo (AnyAPI) — server-side changes

- [ ] **Allow manual triggering of `Event` workflows** —
      `WorkflowRepository.TriggerWorkflow` rejects Event-typed workflows with
      "Cannot trigger event workflows". Both breeze and CLI hit this wall, so
      Event workflows can only be exercised by firing the actual entity event.
      Worth adding a `?force=true` flag (or dropping the check entirely) so we
      can test Event workflows from the dashboard / CLI.
- [ ] **API key controller hardening** — flagged in PR #10:
  - No max `expiresInDays` cap on `CreateApiKeyRequest` (a key can be issued for
    any number of days, including 36500).
  - API-key-authed requests can call `POST /api-keys` to create more api-keys
    (a leaked key could spawn more keys to extend its lifetime).
- [ ] **Apple sign-in `client_secret` JWT generation** — Apple expects a JWT
      signed with a `.p8` key as the OAuth client secret, with a max 6-month
      lifetime. The platform currently expects users to generate this JWT
      externally and paste it into `client_secret`. Servers like Supabase and
      Auth0 accept the `.p8` key once and generate the JWT internally per request
      — big DX win when added.
- [ ] **`workflows step-delete` API error shape** — returns a 500 with a raw EF
      Core message ("An error occurred while saving the entity changes") on FK
      violation. Should return a structured 4xx with the offending step IDs so
      clients can present a useful error without inferring it.
- [ ] **`/search/public` returns 500** on getahead-prod (org 37523255), with or
      without auth. Verified via direct curl — empty 500 response body. Either
      the route is broken on that tenant, or it requires a header we aren't
      sending. Worth investigating because the CLI's `search audit` (and the
      whole "what does an unauthenticated visitor see" use case) depends on it.

## Already done / merged-in-progress

- [x] Integrations CLI (catalog, connections, OAuth, execute) — PR #11
- [x] `workflows step-delete` with inbound-link warnings + FK-error translation — PR #11
- [x] API keys CLI — PR #10
- [x] Field options + jsonb stringify — PR #9
- [x] Menus list/add-item — PR #8
- [x] Fields system relations fix — PR #6
- [x] Workflow step parameter normalisation — PR #4
