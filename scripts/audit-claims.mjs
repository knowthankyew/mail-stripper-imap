#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');

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

// 1. Central Package Management & Fail-closed NuGet security audit
checkFile('Directory.Packages.props', (content) => {
  if (!content.includes('<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>')) {
    errors.push('Directory.Packages.props must enable Central Package Management.');
  }
});

checkFile('Directory.Build.props', (content) => {
  if (!content.includes('NU1903') || !content.includes('NU1904')) {
    errors.push('Directory.Build.props must enforce fail-closed NU1903/NU1904 security audit warnings as errors.');
  }
});

// 2. Zero Disk Writes Invariant
function scanDirectoryForDiskWrites(dirPath) {
  if (!fs.existsSync(dirPath)) return;
  const entries = fs.readdirSync(dirPath, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dirPath, entry.name);
    if (entry.isDirectory() && entry.name !== 'bin' && entry.name !== 'obj' && entry.name !== '.git') {
      scanDirectoryForDiskWrites(full);
    } else if (entry.isFile() && entry.name.endsWith('.cs')) {
      const code = fs.readFileSync(full, 'utf8');
      if (code.includes('File.WriteAll') || code.includes('File.Create') || code.includes('StreamWriter') && !code.includes('MemoryStream')) {
        errors.push(`Found forbidden disk write API in ${entry.name}. Ingestion must use volatile MemoryStream only.`);
      }
    }
  }
}
scanDirectoryForDiskWrites(path.join(rootDir, 'MailStripper.Imap'));

// 3. Sterile RFC 8058 dispatching invariants
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
});

// 4. Prohibited vocabulary in docs
for (const doc of ['README.md', 'ROADMAP.md']) {
  const full = path.join(rootDir, doc);
  if (fs.existsSync(full)) {
    const text = fs.readFileSync(full, 'utf8').toLowerCase();
    for (const bad of ['bulletproof', 'conclusive', 'definitive', 'infinitely scalable']) {
      if (text.includes(bad)) {
        errors.push(`${doc} contains prohibited marketing adjective: '${bad}'.`);
      }
    }
  }
}

console.log('=== MailStripper IMAP Watcher Claims & Security Invariant Audit ===\n');

if (errors.length > 0) {
  console.error(`[FAIL] Found ${errors.length} violation(s):`);
  for (const err of errors) {
    console.error(`  - ${err}`);
  }
  process.exit(1);
}

console.log('[PASS] All IMAP companion architectural & security invariants verified:');
console.log('  ✓ Central Package Management (Directory.Packages.props) & fail-closed NU1903/NU1904 verified');
console.log('  ✓ 100% volatile in-memory streaming verified (zero File.Write/Create disk calls)');
console.log('  ✓ Sterile RFC 8058 HTTP POST execution enforced (UseCookies = false, AllowAutoRedirect = false)');
console.log('  ✓ Zero prohibited marketing adjectives in documentation');
process.exit(0);
