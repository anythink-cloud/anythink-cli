# Connecting to Anythink

If neither the `anythink-mcp` MCP tools nor the `anythink` CLI are available, set
one up before doing project work.

## MCP server (preferred for AI assistants)

Register the server with the client, then authenticate:

```bash
claude mcp add anythink anythink-mcp        # Claude Code one-liner
```

Or add it to the MCP client config (`.mcp.json`):

```json
{ "mcpServers": { "anythink": { "command": "anythink-mcp" } } }
```

If `anythink-mcp` isn't installed yet, install it first (see CLI install below —
Homebrew bundles both, or `dotnet tool install -g anythink-mcp`).

After connecting, run the `login` tool, then `accounts_use` / `projects_use` to
select context.

## CLI

```bash
# Homebrew (installs both anythink and anythink-mcp)
brew install anythink-cloud/tap/anythink

# or .NET global tool
dotnet tool install -g anythink-cli      # CLI
dotnet tool install -g anythink-mcp      # MCP server

# or download the platform binary from
# https://github.com/anythink-cloud/anythink-cli/releases/latest
```

Then `anythink login` and select context with `anythink accounts use` /
`anythink projects use`.

## Verify
`anythink config show` (or the `config_show` MCP tool) prints the active profile,
account, and project — confirm these before building.
