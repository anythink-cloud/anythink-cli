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

## Authenticate

Once connected, run the `login` (or `login_direct`) tool, then `accounts_use` /
`projects_use` to select your working context.

## Capabilities

- **Auth** — `signup`, `login`, `login_direct`, `logout`
- **Config** — `config_show`, `config_use`, `config_remove`
- **Accounts & projects** — `accounts_list`, `accounts_create`, `accounts_use`,
  `projects_list`, `projects_create`, `projects_use`, `projects_delete`
- **Email** — `email_templates_list`
- **Generic CLI** — `cli` to run any Anythink CLI command

## Hosted mode (remote MCP connector)

`--hosted` serves MCP over Streamable HTTP for remote MCP clients, which sign the
user in with their Anythink account via OAuth. One connection targets one
project. stdio stays the default for local installs, and `--http` is unchanged.

To run it locally:

```bash
export MCP_PUBLIC_URL=https://mcp.anythink.cloud/mcp
export MCP_AUTH_ISSUER=https://api.billing.anythink.cloud
export MCP_EXCHANGE_CLIENT_ID=<confidential client id>
export MCP_EXCHANGE_CLIENT_SECRET=<confidential client secret>
export MCP_UPSTREAM_AUDIENCE=<audience your project API expects>
anythink-mcp --hosted
```

Configuration is via environment variables:

| Variable | Required | Description |
|---|---|---|
| `MCP_PUBLIC_URL` | yes | The resource URL clients connect to, e.g. `https://mcp.anythink.cloud/mcp`. Must match exactly what's registered with the OAuth authorisation server. |
| `MCP_AUTH_ISSUER` | yes | The Anythink OAuth authorisation server's issuer URL. |
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
must carry a valid bearer token issued by the Anythink authorisation server; the
project and its API URL come from the token's own claims, never from headers.

The inbound token is issued for this server, so it is never forwarded to the project
API. Instead the server exchanges it (RFC 8693) at the authorisation server's token
endpoint, found through the issuer's discovery document, for a token whose audience is
`MCP_UPSTREAM_AUDIENCE`, caches the result until shortly before it expires, and uses
only that token upstream. If the exchange fails the client gets a generic 401 or 502.

Each project may have 8 concurrent requests in flight on `/mcp`, with up to 8 more
queued; beyond that the server returns `429`. Upstream calls share one connection pool,
don't follow redirects, and time out after 30 seconds.

The hosted tool set is intentionally small and read-only for now:
`project_details`, `entities_list`, and `records_query`. A broader tool set is
planned for a follow-up release.

The internal REST API (`GET /tools`, `POST /tools/call`) used by your internal services
keeps running, but only on the internal port, never on the public port. It trusts
caller-supplied `X-Org-Id` and `X-Instance-Url` headers (the latter must be on the
`instance_url` allowlist), so the internal port must never have ingress. It listens on
loopback by default. If your internal services reach it over the network, set
`MCP_INTERNAL_BIND` and `MCP_INTERNAL_TOKEN`; callers then send the token in
`X-Internal-Token`.

Rollout note: under `--hosted` the internal REST API moves to port `5301` (it was on the
`--http` port, `5300`), so callers' URLs must change. Ingress should target only `5300`,
and a NetworkPolicy should allow only your internal services to reach `5301`.

The Docker image runs `--http --port 5300` by default; override the container command
with `--hosted` to switch modes.

## Links

- Repository: https://github.com/anythink-cloud/anythink-cli
- Platform: https://anythink.cloud
