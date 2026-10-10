// Postinstall: download the matching native binaries for this platform from the
// GitHub release that corresponds to this package version, verify checksums, and
// place them next to the bin shim. No .NET required — the binaries are
// self-contained.
//
// We download BOTH `anythink-mcp` (the server) and `anythink` (the CLI): in
// stdio mode the MCP's generic `cli` tool shells out to `anythink` on PATH, so
// shipping only the server would leave that tool broken.
const fs = require("fs");
const path = require("path");
const https = require("https");
const crypto = require("crypto");
const { version } = require("./package.json");

const REPO = "anythink-cloud/anythink-cli";
const RELEASES = `https://github.com/${REPO}/releases`;

// node platform-arch -> release asset suffix
const SUFFIX = {
  "darwin-arm64": "osx-arm64",
  "darwin-x64": "osx-x64",
  "linux-x64": "linux-x64",
  "linux-arm64": "linux-arm64",
  "win32-x64": "win-x64.exe",
  "win32-arm64": "win-arm64.exe",
};

function fail(message) {
  console.error(`[anythink-mcp] ${message}`);
  console.error(`[anythink-mcp] Install binaries manually instead: ${RELEASES}/latest`);
  process.exit(1);
}

function fetch(url, redirects = 0) {
  return new Promise((resolve, reject) => {
    if (redirects > 10) return reject(new Error("too many redirects"));
    https
      .get(url, { headers: { "User-Agent": "anythink-mcp-installer" } }, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
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

function verify(buf, asset, checksums) {
  if (!checksums) return;
  const sha = crypto.createHash("sha256").update(buf).digest("hex");
  const match = checksums
    .toString("utf8")
    .split("\n")
    .map((l) => l.trim().split(/\s+/))
    .find((parts) => parts[1] === asset);
  if (match && match[0].toLowerCase() !== sha.toLowerCase()) {
    fail(`checksum mismatch for ${asset} (expected ${match[0]}, got ${sha})`);
  }
}

async function main() {
  const key = `${process.platform}-${process.arch}`;
  const suffix = SUFFIX[key];
  if (!suffix) {
    fail(`unsupported platform ${key}. Supported: ${Object.keys(SUFFIX).join(", ")}.`);
  }

  const base = `${RELEASES}/download/v${version}`;
  const isWin = process.platform === "win32";
  const ext = isWin ? ".exe" : "";
  const binDir = path.join(__dirname, "bin");
  fs.mkdirSync(binDir, { recursive: true });

  // local name -> release asset name
  const targets = [
    { out: `anythink-mcp${ext}`, asset: `anythink-mcp-${suffix}` },
    { out: `anythink${ext}`, asset: `anythink-${suffix}` },
  ];

  let checksums;
  try {
    checksums = await fetch(`${base}/checksums.txt`).catch(() => null);
    for (const { out, asset } of targets) {
      const buf = await fetch(`${base}/${asset}`);
      verify(buf, asset, checksums);
      const outPath = path.join(binDir, out);
      fs.writeFileSync(outPath, buf);
      if (!isWin) fs.chmodSync(outPath, 0o755);
    }
  } catch (err) {
    fail(`could not download binaries (v${version}): ${err.message}`);
  }

  console.log(`[anythink-mcp] installed anythink-mcp + anythink (v${version}).`);
}

main().catch((err) => fail(err.message));
