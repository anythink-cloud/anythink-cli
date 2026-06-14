// Postinstall: download the matching anythink-mcp native binary for this
// platform from the GitHub release that corresponds to this package version,
// verify its checksum, and place it next to the bin shim. No .NET required —
// the binary is self-contained.
const fs = require("fs");
const path = require("path");
const https = require("https");
const crypto = require("crypto");
const { version } = require("./package.json");

const REPO = "anythink-cloud/anythink-cli";

// node platform-arch -> release asset name
const ASSETS = {
  "darwin-arm64": "anythink-mcp-osx-arm64",
  "darwin-x64": "anythink-mcp-osx-x64",
  "linux-x64": "anythink-mcp-linux-x64",
  "linux-arm64": "anythink-mcp-linux-arm64",
  "win32-x64": "anythink-mcp-win-x64.exe",
  "win32-arm64": "anythink-mcp-win-arm64.exe",
};

const RELEASES = `https://github.com/${REPO}/releases`;

function fail(message) {
  console.error(`[anythink-mcp] ${message}`);
  console.error(
    `[anythink-mcp] Install a binary manually instead: ${RELEASES}/latest`
  );
  process.exit(1);
}

function fetch(url, redirects = 0) {
  return new Promise((resolve, reject) => {
    if (redirects > 10) return reject(new Error("too many redirects"));
    https
      .get(url, { headers: { "User-Agent": "anythink-mcp-installer" } }, (res) => {
        if (
          res.statusCode >= 300 &&
          res.statusCode < 400 &&
          res.headers.location
        ) {
          res.resume();
          return resolve(fetch(res.headers.location, redirects + 1));
        }
        if (res.statusCode !== 200) {
          res.resume();
          return reject(new Error(`HTTP ${res.statusCode} for ${url}`));
        }
        const chunks = [];
        res.on("data", (c) => chunks.push(c));
        res.on("end", () => resolve(Buffer.concat(chunks)));
      })
      .on("error", reject);
  });
}

async function main() {
  const key = `${process.platform}-${process.arch}`;
  const asset = ASSETS[key];
  if (!asset) {
    fail(
      `unsupported platform ${key}. Supported: ${Object.keys(ASSETS).join(", ")}.`
    );
  }

  const base = `${RELEASES}/download/v${version}`;
  const binDir = path.join(__dirname, "bin");
  const isWin = process.platform === "win32";
  const outPath = path.join(binDir, isWin ? "anythink-mcp.exe" : "anythink-mcp");

  fs.mkdirSync(binDir, { recursive: true });

  let binary, checksums;
  try {
    [binary, checksums] = await Promise.all([
      fetch(`${base}/${asset}`),
      fetch(`${base}/checksums.txt`).catch(() => null),
    ]);
  } catch (err) {
    fail(`could not download ${asset} (v${version}): ${err.message}`);
  }

  // Verify against checksums.txt (lines: "<sha256>  <filename>")
  if (checksums) {
    const sha = crypto.createHash("sha256").update(binary).digest("hex");
    const match = checksums
      .toString("utf8")
      .split("\n")
      .map((l) => l.trim().split(/\s+/))
      .find((parts) => parts[1] === asset);
    if (match && match[0].toLowerCase() !== sha.toLowerCase()) {
      fail(`checksum mismatch for ${asset} (expected ${match[0]}, got ${sha})`);
    }
  }

  fs.writeFileSync(outPath, binary);
  if (!isWin) fs.chmodSync(outPath, 0o755);
  console.log(`[anythink-mcp] installed ${asset} (v${version}).`);
}

main().catch((err) => fail(err.message));
