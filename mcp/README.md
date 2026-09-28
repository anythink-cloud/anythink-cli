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

## Use as a Claude connector (hosted mode)

Add `https://mcp.anythink.cloud/mcp` as a custom connector in Claude — Claude speaks
MCP over Streamable HTTP directly to the server and signs you in with your Anythink
account via OAuth. One connection targets one project; pick a different project by
adding another connector.

Hosted mode is how the server runs in production; you don't need to run anything
yourself to use it from Claude. To run it locally (for development or self-hosting):

```bash
export MCP_PUBLIC_URL=https://mcp.anythink.dev/mcp
export MCP_AUTH_ISSUER=https://api.billing.anythink.dev
anythink-mcp --hosted
```

Configuration is via environment variables:

| Variable | Required | Description |
|---|---|---|
| `MCP_PUBLIC_URL` | yes | The resource URL clients connect to, e.g. `https://mcp.anythink.dev/mcp`. Must match exactly what's registered with the OAuth authorisation server. |
| `MCP_AUTH_ISSUER` | yes | The Anythink OAuth authorisation server's issuer URL. |
| `MCP_AUTH_AUDIENCE` | no | The audience tokens must carry. Defaults to `MCP_PUBLIC_URL`. |
| `MCP_PORT` | no | Public port serving `/mcp` and the OAuth metadata. Defaults to `5300` (or pass `--port`). |
| `MCP_INTERNAL_PORT` | no | Port serving the internal REST API described below. Defaults to `5301` (or pass `--internal-port`). |

Hosted mode serves only three things on the public port: the MCP endpoint (`/mcp`),
the OAuth protected-resource metadata (`/.well-known/oauth-protected-resource` and
`/.well-known/oauth-protected-resource/mcp`), and `/health`. Every request to `/mcp`
must carry a valid bearer token issued by the Anythink authorisation server; the
project and its API URL come from the token's own claims, never from headers.

The tool set exposed to Claude is intentionally small and read-only for now:
`project_details`, `entities_list`, and `records_query`. A broader tool set is
planned for a follow-up release.

The internal REST API (`GET /tools`, `POST /tools/call`) that Anythink's own AI
sidebar uses keeps running, but only on the internal port — it's never exposed on
the public port or port `MCP_PORT`.

## Links

- Repository: https://github.com/anythink-cloud/anythink-cli
- Platform: https://anythink.cloud
