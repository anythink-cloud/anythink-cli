# Data modelling on Anythink

Anythink's data layer is a typed schema over the database with relationships,
polymorphic references, and row-/field-level security. Get the exact, current
field-type tokens and flags from the platform itself — `fields add --help` and
`docs --json` via the `cli` tool — rather than memorising; the categories below
orient you, but the live help is authoritative.

## Entities

An entity is a collection/table. Create with flags that set its behaviour:

```
cli: entities create orders --rls       # row-level security on
cli: entities create pages --public     # publicly readable (no auth)
cli: entities create imports --lock      # block direct record creation (e.g. system-managed)
cli: entities create post_tags --junction   # many-to-many join table
```

- **`--rls`** — records are visible only to users granted access to that row
  (see access-control.md). Use for per-user/tenant data.
- **`--public`** — readable without authentication. Use for public content.
- **`--lock`** — prevents creating records directly (populated by workflows/imports).
- **`--junction`** — marks a many-to-many join entity.

System fields (`id`, timestamps, tenant) are managed for you — don't define or
set them in `--data`.

## Fields

```
cli: fields add customers email --type varchar --required --unique
cli: fields add products price --type float --required
cli: fields add posts body --type text
cli: fields list customers
```

Field-type **categories** (confirm exact `--type` tokens with `fields add --help`):

- **Text** — short string (`varchar`) and long text (`text`).
- **Numeric** — integer, big integer, and decimal/float.
- **Boolean**, **date**, **timestamp**.
- **JSON** — arbitrary structured data.
- **Geo** — coordinates; enables `_geoRadius(...)` search.
- **Arrays** — array variants of the scalar types.
- **Relationships** — one-to-one, one-to-many, many-to-one, many-to-many, and a
  **dynamic/polymorphic reference** (one field that can point at multiple
  entity types).
- **Special** — **file** (links to file storage), **secret** (encrypted value),
  **user** (links to a user).

Common flags: `--required`, `--unique`. Design the whole entity's fields up
front where you can — it's cheaper than adding them record by record.

## Relationships

Model relationships with the relationship field types. A many-to-many is backed
by a `--junction` entity. Anythink resolves nested/related data on read (the
query layer traverses relationships, with cycle protection), so you can fetch a
record with its related records rather than stitching them yourself. Use
`data list <entity> --json` and inspect, or `docs --json`, to see how related
data is shaped for a given entity.

## Search shaping

Mark fields searchable (and publicly searchable, for `--public` surfaces) so
they're indexed for full-text/semantic search. `search audit <entity>` compares
what's configured as public-searchable against expectations. After a schema
change, `search rehydrate <entity>` rebuilds that entity's index.

## Verify as you go

`entities get <name>` and `fields list <entity>` show the real current schema.
Read before you write — the schema is the contract everything else depends on.
