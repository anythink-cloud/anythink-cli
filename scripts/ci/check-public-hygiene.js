#!/usr/bin/env node
"use strict";

const fs = require("fs");

const SECRET_PATTERNS = [
  { name: "AWS access key", pattern: /\bAKIA[0-9A-Z]{16}\b/ },
  { name: "GitHub token", pattern: /\bgh[pousr]_[A-Za-z0-9]{36,}\b/ },
  { name: "Slack token", pattern: /\bxox[baprs]-[A-Za-z0-9-]{10,}\b/ },
  { name: "private key block", pattern: /-----BEGIN [A-Z ]*PRIVATE KEY-----/ },
  { name: "hard-coded credential", pattern: /\b(api[_-]?key|secret|token|password)\b\s*[:=]\s*["'][A-Za-z0-9_\-/+=.]{16,}["']/i },
];

const TENANT_WORD = /\btenants?\b/i;
const USER_FACING_CALL = /(Description|WithDescription|WithExample|Renderer\.\w+|AnsiConsole\.\w+|CliException|Confirm)\s*\(/;
const STRING_LITERAL = /\$?@?"(?:[^"\\]|\\.)*"/g;

function parsePrivatePatterns(raw) {
  return (raw || "")
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith("#"))
    .map((source, i) => ({ name: `private pattern ${i + 1}`, pattern: new RegExp(source, "i") }));
}

function isProse(file) {
  return /\.(md|txt)$/i.test(file) || /^docs\//.test(file);
}

function saysTenantToUsers(file, line) {
  if (isProse(file)) return TENANT_WORD.test(line);
  if (!/\.cs$/.test(file) || !USER_FACING_CALL.test(line)) return false;
  return (line.match(STRING_LITERAL) || []).some((s) => TENANT_WORD.test(s));
}

function scanDiff(diffText, privatePatterns = []) {
  const findings = [];
  let file = null;
  let lineNo = 0;

  for (const raw of diffText.split("\n")) {
    if (raw.startsWith("+++ ")) {
      file = raw.replace(/^\+\+\+ (?:b\/)?/, "");
      continue;
    }
    if (raw.startsWith("@@")) {
      const hunk = raw.match(/\+(\d+)/);
      lineNo = hunk ? parseInt(hunk[1], 10) - 1 : 0;
      continue;
    }
    if (!raw.startsWith("-")) lineNo += 1;
    if (!raw.startsWith("+") || raw.startsWith("+++")) continue;
    if (file && file.startsWith("scripts/ci/check-public-hygiene")) continue;

    const text = raw.slice(1);
    const add = (rule) => findings.push({ file, line: lineNo, rule, text: text.trim() });

    for (const { name, pattern } of privatePatterns) if (pattern.test(text)) add(name);
    for (const { name, pattern } of SECRET_PATTERNS) if (pattern.test(text)) add(`possible secret: ${name}`);
    if (saysTenantToUsers(file, text)) add('say "project", not "tenant", in user-facing text');
  }
  return findings;
}

function main() {
  const arg = process.argv[2];
  if (!arg) {
    console.error("Usage: check-public-hygiene.js <diff-file|->");
    process.exit(2);
  }

  const privatePatterns = parsePrivatePatterns(process.env.PUBLIC_HYGIENE_PATTERNS);
  if (privatePatterns.length === 0) {
    console.log("::notice::PUBLIC_HYGIENE_PATTERNS is not set; checking secrets and wording only.");
  }

  const diffText = fs.readFileSync(arg === "-" ? 0 : arg, "utf8");
  const findings = scanDiff(diffText, privatePatterns);

  if (findings.length === 0) {
    console.log("Public-repo hygiene check passed.");
    return;
  }

  console.error(`Public-repo hygiene check failed: ${findings.length} finding(s)\n`);
  for (const f of findings) {
    console.error(`  ${f.file}:${f.line}  [${f.rule}]`);
    console.error(`    + ${f.text}`);
  }
  process.exit(1);
}

if (require.main === module) main();

module.exports = { scanDiff, parsePrivatePatterns };
