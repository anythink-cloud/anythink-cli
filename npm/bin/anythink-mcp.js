#!/usr/bin/env node
// Thin launcher: exec the native MCP server that postinstall placed next to this
// shim, forwarding argv and stdio (stdio is how MCP clients talk to the server).
//
// We prepend the bin dir to PATH so the server can find the bundled `anythink`
// CLI — in stdio mode the MCP's `cli` tool shells out to `anythink`.
const { spawnSync } = require("child_process");
const path = require("path");
const fs = require("fs");

const isWin = process.platform === "win32";
const binDir = __dirname;
const server = path.join(binDir, isWin ? "anythink-mcp.exe" : "anythink-mcp");

if (!fs.existsSync(server)) {
  console.error(
    "[anythink-mcp] native binary missing — postinstall may have failed. " +
      "Reinstall, or grab a binary from " +
      "https://github.com/anythink-cloud/anythink-cli/releases/latest"
  );
  process.exit(1);
}

const env = {
  ...process.env,
  PATH: binDir + path.delimiter + (process.env.PATH || ""),
};

const result = spawnSync(server, process.argv.slice(2), { stdio: "inherit", env });
if (result.error) {
  console.error(`[anythink-mcp] failed to launch: ${result.error.message}`);
  process.exit(1);
}
process.exit(result.status === null ? 1 : result.status);
