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

## Links

- Repository: https://github.com/anythink-cloud/anythink-cli
- Platform: https://anythink.cloud
