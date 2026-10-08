<!-- mcp-name: cloud.anythink/anythink -->

# Anythink MCP Server

**Sick of Frankenstein's monster?** [Anythink](https://anythink.cloud) replaces
stitched-together services with a streamlined, all-in-one backend — giving you more
time to create a killer product. All configured with a little help from AI, when
you need it.

`anythink-mcp` exposes that Backend-as-a-Service platform — databases, auth, data,
files, workflows, integrations, payments, and REST APIs — to AI assistants over the
[Model Context Protocol](https://modelcontextprotocol.io). It ships as a .NET global
tool and runs as a stdio MCP server.

## Install

```bash
dotnet tool install -g anythink-mcp
```

## Use with Claude Code

Register the server (stdio):

```bash
claude mcp add anythink anythink-mcp
```

Or add it to your `.mcp.json`:

```json
{
  "mcpServers": {
    "anythink": {
      "command": "anythink-mcp"
    }
  }
}
```

To pin a profile:

```json
{
  "mcpServers": {
    "anythink": {
      "command": "anythink-mcp",
      "args": ["--profile", "my-project"]
    }
  }
}
```

## Use over HTTP (local)

Some MCP clients connect over HTTP instead of launching a process. Run the server locally
with `--http` and point the client at `http://localhost:5300/mcp`:

```bash
anythink-mcp --http
npx -y @anythink-cloud/mcp --http --port 5300
```

It serves the same tools as stdio, uses your saved `anythink login`, and listens on
`localhost` only. Requests for other hosts and from web pages are refused. There is no
separate sign-in: anything that can reach `localhost` on your machine can use your login
while it runs, so don't run it on a shared machine. For Claude Code:

```bash
claude mcp add --transport http anythink http://localhost:5300/mcp
```

## Authenticate

Once connected, run the `login` (or `login_direct`) tool, then `accounts_use` /
`projects_use` to select your working context.

## Capabilities

- **Auth** — `signup`, `login`, `login_direct`, `logout`
- **Config** — `config_show`, `config_use`, `config_remove`
- **Accounts & projects** — `accounts_list`, `accounts_create`, `accounts_use`,
  `projects_list`, `projects_create`, `projects_use`, `projects_delete` (the hosted
  server has its own versions, described under [Account tools](#account-tools))
- **Every project command** — one tool per CLI command, generated from the CLI itself,
  e.g. `entities_list`, `fields_add`, `data_list`, `workflows_create`. Each is marked
  read-only or destructive so clients can ask before changing anything.
- **Generic CLI** — `cli` to run any Anythink CLI command

## Hosted mode (remote MCP connector)

`--hosted` serves MCP over Streamable HTTP for remote MCP clients, which sign the
user in with their Anythink account via OAuth. When signing in, the user grants
access to one project or to all of their projects. With all projects, `projects_list`
shows what the connection can reach, and every other tool takes a `project` id.

To run it locally:

```bash
export MCP_PUBLIC_URL=https://mcp.anythink.cloud/mcp
export MCP_AUTH_ISSUER=https://api.billing.anythink.cloud
export MCP_EXCHANGE_CLIENT_ID=<confidential client id>
export MCP_EXCHANGE_CLIENT_SECRET=<confidential client secret>
export MCP_UPSTREAM_AUDIENCE=<audience your project API expects>
export MCP_ALLOWED_HOSTS=localhost
anythink-mcp --hosted
```

Configuration is via environment variables:

| Variable | Required | Description |
|---|---|---|
| `MCP_PUBLIC_URL` | yes | The resource URL clients connect to, e.g. `https://mcp.anythink.cloud/mcp`. Must match exactly what's registered with the OAuth authorisation server. |
| `MCP_AUTH_ISSUER` | yes | The Anythink OAuth authorisation server's issuer URL. |
| `MCP_BILLING_URL` | no | Base URL of the Anythink billing service that account tools call with the caller's own token. Must be an absolute https URL. Defaults to `MCP_AUTH_ISSUER`. |
| `MCP_AUTH_AUDIENCE` | no | The audience tokens must carry. Defaults to `MCP_PUBLIC_URL`. |
| `MCP_EXCHANGE_CLIENT_ID` | yes | Client id of this server's confidential client at the authorisation server, used for token exchange. |
| `MCP_EXCHANGE_CLIENT_SECRET` | yes | Secret for that client. Never logged. |
| `MCP_UPSTREAM_AUDIENCE` | yes | Audience requested for the exchanged token, i.e. the audience your project API expects. |
| `MCP_INSTANCE_HOST_SUFFIXES` | no | Comma-separated host suffixes a token's `instance_url` must match (https only, and the host's first label must be `api`). Defaults to `.anythink.cloud,.anythink.dev,.anythink.uk`. |
| `MCP_ALLOWED_ORIGINS` | no | Comma-separated `Origin` values accepted when a request carries one. Requests without an `Origin` header are always accepted; the default is to reject any browser origin. |
| `MCP_ALLOWED_HOSTS` | no | Extra `Host` values accepted besides the host in `MCP_PUBLIC_URL` (`/health` is exempt). |
| `MCP_TOKEN_ENDPOINT` | no | Explicit https token endpoint for the exchange. Without it the endpoint is discovered from the issuer and must share the issuer's origin. |
| `MCP_ALLOW_LOOPBACK_INSTANCE` | no | Set to `true` for local development to accept loopback `instance_url` values (and `X-Instance-Url` on the internal API). Only allowed when `ASPNETCORE_ENVIRONMENT=Development`; otherwise the server refuses to start. |
| `MCP_INTERNAL_BIND` | no | Address the internal REST port listens on. Defaults to `127.0.0.1`. Any non-loopback address requires `MCP_INTERNAL_TOKEN`. |
| `MCP_INTERNAL_TOKEN` | when bound beyond loopback | Shared secret internal callers send in the `X-Internal-Token` header on `/tools` and `/tools/call`. |
| `MCP_PORT` | no | Public port serving `/mcp` and the OAuth metadata. Defaults to `5300` (or pass `--port`). |
| `MCP_INTERNAL_PORT` | no | Port serving the internal REST API described below. Defaults to `5301` (or pass `--internal-port`). |

Hosted mode serves only three things on the public port: the MCP endpoint (`/mcp`),
the OAuth protected-resource metadata (`/.well-known/oauth-protected-resource` and
`/.well-known/oauth-protected-resource/mcp`), and `/health`. Every request to `/mcp`
must carry a valid bearer token issued by the Anythink authorisation server. For a
one-project connection the project and its API URL come from the token's own claims; for an
all-projects connection the token names no project, and each call names one with `project`.
Either way the project's API URL comes from a validated token, never from headers.

The inbound token is issued for this server, so it is never forwarded to the project
API. Instead the server exchanges it (RFC 8693) at the authorisation server's token
endpoint, found through the issuer's discovery document, for a token whose audience is
`MCP_UPSTREAM_AUDIENCE`, caches the result until shortly before it expires, and uses
only that token upstream. For a one-project connection the exchange happens when the request
arrives, and if it fails the client gets a generic 401 or 502. For an all-projects connection it
happens when a tool runs (once per token and project), and a failure is a tool error, not an
HTTP error.

A connection to one project may have 8 concurrent requests in flight on `/mcp`, shared
by everyone connected to that project, with up to 8 more queued; beyond that the server
returns `429`. A connection that covers all projects has the same limit per user, across
every project it calls. Upstream calls share one connection pool, don't follow redirects,
and time out after 30 seconds.

Hosted mode serves `projects_list`, `project_details`, the account tools below and the generated command tools, leaving out
commands that sign in, switch profiles, use the local machine (opening a browser,
or reading and writing local files), call arbitrary routes (`fetch`) or create API
keys. Positional values can't contain `/`, `..`, `?` or `#` (free-text arguments such as
search text and names excepted), every request is checked to stay under the project's
API root, tool output is cut off at 200,000 characters, and an upstream error is reported
by status only. Tools run as the signed-in user, so their role in the project decides
what each call can do.

### Account tools

When the user grants account access at sign-in (the token's `scope` contains `account`),
hosted mode also serves five tools that run against the billing service as that user, using the
connection's own token and never this server's saved login:

| Tool | What it does | Hint |
|---|---|---|
| `accounts_list` | List your billing accounts, with their ids | read-only |
| `accounts_create` | Create a billing account (`name`, `email`, optional `currency`) | additive |
| `plans` | List the plans a project can use, with their ids | read-only |
| `projects_create` | Create a project (`name`, `plan_id`, optional `region`, `description`, `account_id`) | additive |
| `projects_delete` | Delete a project by its full id (`id`, optional `account_id`) | destructive |

These tools take no `project` argument, work on both one-project and all-projects connections, and never prompt.
There's no active account in a hosted run: leave out `account_id` when you have one account,
or pass an id from `accounts_list` when you have several. A new project is set up in the
background and, on an all-projects connection, appears in `projects_list` after about a
minute. On a connection limited to one project the tools work the same way, but a project
created there isn't reachable from that connection, so the result says to reconnect with
all projects to work in it.
Without `account` in the scope the tools are left out of the tool list, and calling one
anyway is refused without contacting the billing service. The server advertises `account` in
the protected-resource metadata's `scopes_supported`. A billing error is reported by status only, except for a 400,
where the billing service's own message (for example, that a paid plan needs a payment method)
is passed on.

The internal REST API (`GET /tools`, `POST /tools/call`) used by your internal services
keeps running, but only on the internal port, never on the public port. It trusts
caller-supplied `X-Org-Id` and `X-Instance-Url` headers (the latter must be on the
`instance_url` allowlist), so the internal port must never have ingress. It listens on
loopback by default. If your internal services reach it over the network, set
`MCP_INTERNAL_BIND` and `MCP_INTERNAL_TOKEN`; callers then send the token in
`X-Internal-Token`.

`GET /tools` lists each tool's `name`, `description`, `input_schema` and `annotations`
(`title`, `read_only_hint`, `destructive_hint`, `open_world_hint`; `destructive_hint` is
`false` for a read-only tool). `POST /tools/call` returns `result.content` and a top-level
`is_error`, which is `true` when the command failed, was given arguments it doesn't
accept, or was cancelled.

Under `--hosted` the internal REST API listens on port `5301`. Route ingress only to `5300`,
and allow only your internal services to reach `5301`.

`--internal` runs only that internal REST API, on `--port` (default `5300`), with the same
bind and token settings. The Docker image runs `--hosted` by default; override the
container command with `--internal` to run only the internal REST API.

## Links

- Repository: https://github.com/anythink-cloud/anythink-cli
- Platform: https://anythink.cloud
