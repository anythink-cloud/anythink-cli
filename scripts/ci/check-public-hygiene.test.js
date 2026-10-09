const test = require("node:test");
const assert = require("node:assert/strict");
const { scanDiff, parsePrivatePatterns } = require("./check-public-hygiene");

function diff(fileName, ...addedLines) {
  return [
    `diff --git a/${fileName} b/${fileName}`,
    `--- a/${fileName}`,
    `+++ b/${fileName}`,
    "@@ -1,0 +1,3 @@",
    ...addedLines.map((l) => `+${l}`),
  ].join("\n");
}

test("private patterns come from configuration, not the script", () => {
  const patterns = parsePrivatePatterns("# comment\n\\bcodename\\b\n\n");
  assert.equal(patterns.length, 1);
  const findings = scanDiff(diff("README.md", "Talks to the Codename backend."), patterns);
  assert.equal(findings.length, 1);
  assert.equal(findings[0].rule, "private pattern 1");
});

test("findings name the pattern by number, never its source", () => {
  const findings = scanDiff(diff("a.md", "codename"), parsePrivatePatterns("codename"));
  assert.doesNotMatch(findings[0].rule, /codename/);
});

test("without private patterns, only secrets and wording are checked", () => {
  assert.equal(scanDiff(diff("README.md", "Talks to the Codename backend.")).length, 0);
});

test("tenant in prose is flagged", () => {
  const findings = scanDiff(diff("docs/guide.md", "Pick a tenant first."));
  assert.equal(findings.length, 1);
  assert.match(findings[0].rule, /project/);
});

test("tenant in a user-facing string is flagged", () => {
  assert.equal(scanDiff(diff("src/Commands/Foo.cs", 'Renderer.Info("Select a tenant to continue");')).length, 1);
  assert.equal(scanDiff(diff("src/Commands/Foo.cs", '[Description("The tenant id")]')).length, 1);
});

test("tenant in code identifiers is not flagged", () => {
  const lines = [
    "var tenant = await client.GetTenantAsync();",
    "var ts = tenant.TenantSettings ?? Defaults();",
    'var url = $"/org/{OrgId}/tenants";',
    '[property: JsonPropertyName("tenant_settings")] TenantSettingsDto? TenantSettings,',
  ];
  assert.equal(scanDiff(diff("src/Commands/Foo.cs", ...lines)).length, 0);
});

test("ordinary uppercase-dash-number tokens are not flagged", () => {
  assert.equal(scanDiff(diff("src/Foo.cs", 'var algo = "SHA-256"; var d = "UTF-16";')).length, 0);
});

test("obvious secrets are flagged", () => {
  const findings = scanDiff(diff("src/Foo.cs",
    "var k = \"AKIAABCDEFGHIJKLMNOP\";",
    "-----BEGIN RSA PRIVATE KEY-----",
    'api_key = "abcdefghijklmnop1234"'));
  assert.equal(findings.length, 3);
});

test("removed and context lines are ignored", () => {
  const text = [
    "--- a/README.md",
    "+++ b/README.md",
    "@@ -1,2 +1,1 @@",
    "-Pick a tenant first.",
    " Pick a tenant first.",
  ].join("\n");
  assert.equal(scanDiff(text).length, 0);
});

test("reports the line number in the new file", () => {
  const text = ["+++ b/a.md", "@@ -10,2 +10,3 @@", " ctx", "+a tenant"].join("\n");
  assert.equal(scanDiff(text)[0].line, 11);
});
