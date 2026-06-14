# anythink-mcp

**Sick of Frankenstein's monster?** [Anythink](https://anythink.cloud) replaces
stitched-together services with a streamlined, all-in-one backend — giving you
more time to create a killer product. All configured with a little help from AI,
when you need it.

`anythink-mcp` is the [Anythink](https://anythink.cloud) MCP server. It exposes
the Backend-as-a-Service platform — databases, auth, data and search, files,
workflows, integrations, and payments — to AI assistants and agents over the
[Model Context Protocol](https://modelcontextprotocol.io).

This package runs the native, self-contained binary via `npx` — **no .NET
runtime required**.

## Use with Claude Code

```bash
claude mcp add anythink -- npx -y anythink-mcp
```

## Use with any MCP client

Add this server entry to the client's MCP config:

```json
{
  "mcpServers": {
    "anythink": {
      "command": "npx",
      "args": ["-y", "anythink-mcp"]
    }
  }
}
```

| Client | Config location |
| --- | --- |
| Claude Code | `claude mcp add` (above) |
| Cursor | `~/.cursor/mcp.json` |
| VS Code | `.vscode/mcp.json` |
| Windsurf | `~/.codeium/windsurf/mcp_config.json` |
| Cline / Continue / Zed | their MCP settings |

To pin a profile: add `"--profile", "my-project"` to `args`.

Once connected, run the `login` tool, then `accounts_use` / `projects_use` to
pick your working context.

## Supported platforms

macOS (arm64/x64), Linux (x64/arm64), Windows (x64/arm64). The matching binary is
downloaded from the [GitHub release](https://github.com/anythink-cloud/anythink-cli/releases)
on install and checksum-verified.

Prefer a native install? `brew install anythink-cloud/tap/anythink` (macOS/Linux)
installs both the CLI and the MCP server.

## License

MIT
