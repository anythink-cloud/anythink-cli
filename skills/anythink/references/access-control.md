# Access control on Anythink

Anythink has layered authorization: **roles + permissions** (RBAC),
**row-level security** (RLS), **field-level access**, and **API keys**. The
single most important rule is at the top.

## Grant permissions, or you get 403s

Permissions are named **`{entity}:{action}`** — actions are `read`, `create`,
`update`, `delete`, and `trigger` (workflows). Examples: `posts:read`,
`posts:create`, `orders:update`.

**When you create a new entity or user, grant the relevant role the matching
permissions.** A non-admin role with no permission for an entity gets a **403**
at runtime — this is the most common "it doesn't work" cause. So a typical
"add a feature" sequence is: create entity → add fields → **grant the app's
role(s) the new `entity:action` permissions** → then use it.

Tenant **administrators bypass** role/permission/field/RLS checks — handy for
setup, but build your app's *non-admin* roles deliberately.

## Roles

```
cli: roles list
cli: roles create editor --description "Can edit content"
```
Roles are tenant-scoped and hold a set of permissions. Users are assigned a role
per project (`users invite <email> <first> <last> --role-id <id>`).

## API keys

API keys carry their own permission set and are how non-interactive clients
(CI, scripts, agents) authenticate.

```
cli: api-keys create ci --permissions "data:read,data:create"
cli: api-keys create scraper --permissions "data:read" --save-as scraper-bot
cli: api-keys revoke 42 --yes
```

- The key **value is printed to stderr** (not stdout) so it isn't captured in
  normal output — **let the user copy it; never echo it back** into the chat.
- `--save-as <profile>` stores the key as a CLI profile you can then select.
- Scope keys to the **minimum** permissions they need.

## Row-level security (RLS)

On entities created with `--rls`, records are visible only to users granted
access to that row. Use it for per-user or per-tenant data (a user sees only
their own orders, etc.). RLS is enforced on both data queries and search
results. Public search/`--public` entities intentionally bypass RLS.

## Field-level access

Roles can be restricted from seeing specific fields on an entity (e.g. hide an
internal `cost` column from a customer-facing role). System fields are always
visible; non-system fields default to accessible until explicitly restricted.

## Public access

`--public` entities are readable without auth; mark fields publicly searchable
to expose them to unauthenticated search. Be deliberate — public means anyone.

## Secrets

Use the `secrets` command group for credentials that workflows/integrations
need; they're stored encrypted. Never paste secret values into the transcript —
create/reference them by name.
