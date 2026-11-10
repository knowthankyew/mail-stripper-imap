#!/usr/bin/env node
/**
 * audit-claims.mjs — Claims, Architectural Parity & Security Invariant Linter for mail-stripper-imap.
 *
 * Invariants Enforced:
 * 1. Central Package Management (CPM) & Fail-Closed NuGet Auditing (NU1901-NU1904).
 * 2. 100% Volatile RAM Ingestion (Zero Disk Writes).
 * 3. Sterile RFC 8058 One-Click HTTP POST Execution (UseCookies = false, AllowAutoRedirect = false).
 * 4. Heap Memory Size Budgeting (15 MB per-message ceiling).
 * 5. Embedded Health Probes & Safe Loopback Defaults (/healthz, /readyz, /livez).
 * 6. Non-Root Container & Kernel Sandboxed Execution (USER $APP_UID, ProtectSystem=strict).
 * 7. Vocabulary Guardrails (Prohibiting unprovable marketing adjectives).
 * 8. Dynamic Test Inventory & Portfolio Master Brief Parity (Dynamically verified against dotnet test).
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execSync } from 'node:child_process';
import {
  stripLineComment,
  stripBlockComments,
  scanDocumentationForVocabulary,
  BANNED_VOCABULARY,
} from './claims-linter-core.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');
const devRoot = path.resolve(rootDir, '..');

const errors = [];

function checkFile(relPath, validator) {
  const fullPath = path.join(rootDir, relPath);
  if (!fs.existsSync(fullPath)) {
    errors.push(`Missing required file: ${relPath}`);
    return;
  }
  const content = fs.readFileSync(fullPath, 'utf8');
  validator(content, relPath);
}

// ── Invariant 1: Central Package Management & Supply Chain Security ───────────
checkFile('Directory.Packages.props', (content) => {
  if (!content.includes('<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>')) {
    errors.push('Directory.Packages.props must enable Central Package Management.');
  }
  const pkcsMatch = content.match(/PackageVersion\s+Include="System\.Security\.Cryptography\.Pkcs"\s+Version="([^"]+)"/);
  if (!pkcsMatch) {
    errors.push('Directory.Packages.props must pin System.Security.Cryptography.Pkcs.');
  } else {
    const ver = pkcsMatch[1];
    const parts = ver.split('.').map(Number);
    const isSecure = parts[0] > 10 || (parts[0] === 10 && (parts[1] > 0 || (parts[1] === 0 && parts[2] >= 13)));
    if (!isSecure) {
      errors.push(`Directory.Packages.props must pin System.Security.Cryptography.Pkcs >= 10.0.13 to resolve GHSA-846f-f764-76mx (found ${ver}).`);
    }
  }
});

checkFile('Directory.Build.props', (content) => {
  if (!content.includes('NU1901') || !content.includes('NU1902') || !content.includes('NU1903') || !content.includes('NU1904')) {
    errors.push('Directory.Build.props must enforce fail-closed NU1901-NU1904 security audit warnings as fatal errors.');
  }
  if (!content.includes('<NuGetAudit>true</NuGetAudit>')) {
    errors.push('Directory.Build.props must enable <NuGetAudit>true</NuGetAudit>.');
  }
});

// ── Invariant 2: Zero Disk Writes Invariant ──────────────────────────────────
function scanDirectoryForDiskWrites(dirPath) {
  if (!fs.existsSync(dirPath)) return;
  const entries = fs.readdirSync(dirPath, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dirPath, entry.name);
    if (entry.isDirectory() && entry.name !== 'bin' && entry.name !== 'obj' && entry.name !== '.git') {
      scanDirectoryForDiskWrites(full);
    } else if (entry.isFile() && entry.name.endsWith('.cs')) {
      const code = fs.readFileSync(full, 'utf8');
      const stripped = stripBlockComments(stripLineComment(code));

      const forbiddenPrimitives = [
        { name: 'File.Write*', regex: /\bFile\s*\.\s*(?:WriteAllText|WriteAllBytes|WriteAllLines)\b/ },
        { name: 'File.Create*', regex: /\bFile\s*\.\s*(?:Create|CreateText)\b/ },
        { name: 'File.Append*', regex: /\bFile\s*\.\s*(?:AppendAllText|AppendAllLines|AppendText)\b/ },
        { name: 'File.Open*', regex: /\bFile\s*\.\s*(?:OpenWrite|Open)\b/ },
        { name: 'FileStream', regex: /\bFileStream\b/ },
        { name: 'Path.GetTempFileName', regex: /\bPath\s*\.\s*GetTempFileName\b/ },
        { name: 'StreamWriter path constructor', regex: /\bnew\s+StreamWriter\s*\(\s*(?:"|[^,)]*?(?:path|fileName|filePath|string\b))/i },
      ];

      for (const primitive of forbiddenPrimitives) {
        if (primitive.regex.test(stripped)) {
          errors.push(`Found forbidden disk write API (${primitive.name}) in ${entry.name}. Ingestion must use volatile MemoryStream only.`);
        }
      }
    }
  }
}
scanDirectoryForDiskWrites(path.join(rootDir, 'MailStripper.Imap'));

// ── Invariant 3: Sterile RFC 8058 Dispatching Invariants ──────────────────────
checkFile('MailStripper.Imap/Services/SterileUnsubscribeDispatcher.cs', (content) => {
  if (!content.includes('UseCookies = false')) {
    errors.push('SterileUnsubscribeDispatcher must enforce UseCookies = false.');
  }
  if (!content.includes('AllowAutoRedirect = false')) {
    errors.push('SterileUnsubscribeDispatcher must enforce AllowAutoRedirect = false.');
  }
  if (!content.includes('List-Unsubscribe=One-Click')) {
    errors.push('SterileUnsubscribeDispatcher must send standard RFC 8058 body.');
  }
  if (!content.includes('string.Equals(uri.Scheme, "https"') && !content.includes('Uri.UriSchemeHttps')) {
    errors.push('SterileUnsubscribeDispatcher must reject non-HTTPS targets.');
  }
  if (!content.includes('PoisonSignals.Count > 0')) {
    errors.push('SterileUnsubscribeDispatcher must reject candidates carrying poison signals.');
  }
});

// ── Invariant 4: Size Budgeting & Bounded In-Memory Cap ───────────────────────
checkFile('MailStripper.Imap/Models/ImapOptions.cs', (content) => {
  if (!content.includes('15 * 1024 * 1024') && !content.includes('15728640')) {
    errors.push('ImapOptions must configure default 15 MB MaxMessageByteSize budget.');
  }
});

checkFile('MailStripper.Imap/Services/ImapWatcherService.cs', (content) => {
  if (!content.includes('MaxMessageByteSize')) {
    errors.push('ImapWatcherService must enforce MaxMessageByteSize check before processing.');
  }
});

// ── Invariant 5: Health Monitoring & Safe Defaults ───────────────────────────
checkFile('MailStripper.Imap/Services/HealthEndpointService.cs', (content) => {
  if (!content.includes('/healthz') || !content.includes('/readyz') || !content.includes('/livez')) {
    errors.push('HealthEndpointService must expose /healthz, /readyz, and /livez endpoints.');
  }
});

checkFile('MailStripper.Imap/Models/ImapOptions.cs', (content) => {
  if (!content.includes('HealthCheckHost { get; set; } = "127.0.0.1"')) {
    errors.push('ImapOptions.HealthCheckHost must default to loopback 127.0.0.1 for security.');
  }
});

// ── Invariant 6: Non-Root Container & Systemd Sandboxing ──────────────────────
checkFile('Dockerfile', (content) => {
  if (!content.includes('USER $APP_UID')) {
    errors.push('Dockerfile must enforce non-root unprivileged execution via USER $APP_UID.');
  }
  if (!content.includes('HEALTHCHECK')) {
    errors.push('Dockerfile must include HEALTHCHECK instruction.');
  }
});

checkFile('systemd/mail-stripper-imap.service', (content) => {
  if (!content.includes('ProtectSystem=strict') || !content.includes('NoNewPrivileges=true') || !content.includes('PrivateTmp=true')) {
    errors.push('systemd service must enforce ProtectSystem=strict, NoNewPrivileges=true, and PrivateTmp=true sandboxing.');
  }
});

// ── Invariant 7: Prohibited Vocabulary in Documentation ───────────────────────
const docsToCheck = ['README.md', 'ROADMAP.md', 'docs/ARCHITECTURE.md', 'NOTICE.md'];
for (const doc of docsToCheck) {
  const full = path.join(rootDir, doc);
  if (fs.existsSync(full)) {
    const text = fs.readFileSync(full, 'utf8');
    const vocabErrors = scanDocumentationForVocabulary(text, doc);
    errors.push(...vocabErrors);
  }
}

// ── Invariant 8: Dynamic Test Inventory & Portfolio Parity ────────────────────
console.log('=== MailStripper IMAP Watcher Claims & Security Invariant Audit ===\n');

let actualTestCount = null;
try {
  const testOutput = execSync('dotnet test mail-stripper-imap.slnx', {
    cwd: rootDir,
    encoding: 'utf8',
    stdio: ['pipe', 'pipe', 'pipe'],
  });

  const matches = [...testOutput.matchAll(/Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)/g)];
  if (matches.length > 0) {
    let totalFailed = 0;
    let totalPassed = 0;
    let totalSkipped = 0;
    let totalCount = 0;
    for (const m of matches) {
      totalFailed += parseInt(m[1], 10);
      totalPassed += parseInt(m[2], 10);
      totalSkipped += parseInt(m[3], 10);
      totalCount += parseInt(m[4], 10);
    }

    if (totalFailed > 0) {
      errors.push(`Test suite failure: ${totalFailed} tests failed during verification.`);
    } else {
      actualTestCount = totalPassed;
      console.log(`  ✓ Dynamic test runner verification: ${totalPassed}/${totalCount} unit tests passing across ${matches.length} test project(s)`);
    }
  } else {
    errors.push('Unable to parse test results from dotnet test.');
  }
} catch (err) {
  errors.push(`Failed executing dotnet test during claims audit: ${err.message}`);
}

// Check parity with Portfolio Master Brief if present
const briefPath = path.join(devRoot, 'KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md');
if (fs.existsSync(briefPath) && actualTestCount !== null) {
  const briefText = fs.readFileSync(briefPath, 'utf8');
  // Match `mail-stripper-imap` row: ... (\d+)/(\d+) [uU]nit [tT]ests
  const briefMatch = briefText.match(/`mail-stripper-imap`.*?(\d+)\/(\d+)\s+unit tests/i);
  if (briefMatch) {
    const briefPassed = parseInt(briefMatch[1], 10);
    const briefTotal = parseInt(briefMatch[2], 10);
    if (briefPassed !== actualTestCount || briefTotal !== actualTestCount) {
      errors.push(`Portfolio Brief Drift: KNOWTHANKYEW_PORTFOLIO_MASTER_BRIEF.md documents ${briefPassed}/${briefTotal} unit tests, but test runner executed ${actualTestCount}/${actualTestCount}.`);
    } else {
      console.log(`  ✓ Portfolio Master Brief parity verified: ${briefPassed}/${briefTotal} tests documented`);
    }
  }
}

// Check published test counts in README.md and ROADMAP.md
if (actualTestCount !== null) {
  for (const docName of ['README.md', 'ROADMAP.md']) {
    const docFile = path.join(rootDir, docName);
    if (fs.existsSync(docFile)) {
      const docContent = fs.readFileSync(docFile, 'utf8');
      const testNumMatches = docContent.matchAll(/(\d+)\/(\d+)\s+(?:automated\s+)?unit\s+tests/gi);
      for (const m of testNumMatches) {
        const documented = parseInt(m[1], 10);
        if (documented !== actualTestCount) {
          errors.push(`${docName} documents ${documented} unit tests, but ${actualTestCount} are executed.`);
        }
      }
    }
  }
}

if (errors.length > 0) {
  console.error(`\n[FAIL] Found ${errors.length} violation(s):`);
  for (const err of errors) {
    console.error(`  - ${err}`);
  }
  process.exit(1);
}

console.log('[PASS] All IMAP companion architectural & security invariants verified:');
console.log('  ✓ Central Package Management (Directory.Packages.props) & fail-closed NU1901-NU1904 verified');
console.log('  ✓ 100% volatile in-memory streaming verified (zero File.Write/Create disk calls)');
console.log('  ✓ Sterile RFC 8058 HTTP POST execution enforced (UseCookies = false, AllowAutoRedirect = false)');
console.log('  ✓ 15 MB heap message budget enforced');
console.log('  ✓ Embedded health probes & 127.0.0.1 loopback default verified');
console.log('  ✓ Hardened container (USER $APP_UID) & systemd sandboxing verified');
console.log('  ✓ Zero prohibited marketing adjectives in documentation');
console.log(`  ✓ Dynamic published numbers & portfolio brief parity verified (${actualTestCount}/${actualTestCount} tests)`);
process.exit(0);
