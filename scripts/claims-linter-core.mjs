/**
 * Shared Claims & Privacy Invariant Linter Core (@knowthankyew)
 * Provides string-aware comment stripping, network primitive detection,
 * and banned vocabulary scanning.
 *
 * DEFENSE-IN-DEPTH ARCHITECTURE NOTE:
 * Static regex/AST scanning is a build-time guardrail against accidental developer regression.
 * Static pattern matching cannot detect dynamic computed URLs (e.g. string concatenation
 * `fetch(a + b)` or variable dynamic imports `import(u)`). The definitive runtime security
 * boundaries across the knowthankyew portfolio are strict Content Security Policies (CSP),
 * loopback socket isolation, and air-gapped container network policies.
 */

/**
 * String-aware line comment stripper.
 * Strips `//` comments while preserving `//` inside single, double, or backtick string literals
 * and escaped quotes.
 *
 * @param {string} line
 * @returns {string}
 */
export function stripLineComment(line) {
  let inSingle = false;
  let inDouble = false;
  let inBacktick = false;
  let escape = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (escape) {
      escape = false;
      continue;
    }
    if (c === '\\') {
      escape = true;
      continue;
    }
    if (c === "'" && !inDouble && !inBacktick) {
      inSingle = !inSingle;
    } else if (c === '"' && !inSingle && !inBacktick) {
      inDouble = !inDouble;
    } else if (c === '`' && !inSingle && !inDouble) {
      inBacktick = !inBacktick;
    } else if (!inSingle && !inDouble && !inBacktick) {
      if (c === '/' && line[i + 1] === '/') {
        return line.slice(0, i);
      }
    }
  }
  return line;
}

/**
 * Strips C-style block comments while preserving newlines for accurate line numbers.
 *
 * @param {string} content
 * @returns {string}
 */
export function stripBlockComments(content) {
  return content.replace(/\/\*[\s\S]*?\*\//g, (match) => {
    const newlines = match.match(/\n/g);
    return newlines ? newlines.join('') : '';
  });
}

/**
 * Regular expression matching all forbidden client-side network egress primitives:
 * - Direct or aliased fetch calls
 * - XMLHttpRequest, axios
 * - Computed bracket access (`['fetch']`, `['sendBeacon']`, etc.)
 * - navigator.sendBeacon
 * - Remote Workers: `new Worker('https://...')` or `new Worker(new URL('https://...'))`
 * - Remote dynamic imports: `import('https://...')` or `import(`https://...`)`
 * - Dynamic script tag creation: `document.createElement('script')`
 * - WebSockets, EventSource, new Image beacons
 * - Global window/self/globalThis fetch references
 */
export const FORBIDDEN_NETWORK_REGEX =
  /(?:\b(?:fetch|\??\.\s*fetch)\s*\(|=\s*fetch\b|\b(?:XMLHttpRequest|axios)\b|\[\s*['"`](?:fetch|XMLHttpRequest|sendBeacon|WebSocket|EventSource|axios)['"`]\s*\]|\bnavigator\s*(?:\??\.\s*sendBeacon|\[\s*['"`]sendBeacon['"`]\s*\])|\bnew\s+(?:WebSocket|EventSource|Image)\b|\bnew\s+Worker\s*\(\s*(?:new\s+URL\s*\(\s*)?['"`]https?:\/\/|\bimport\s*\(\s*['"`]https?:\/\/|\bdocument\s*(?:\??\.\s*createElement|\[\s*['"`]createElement['"`]\s*\])\s*\(\s*['"`]script['"`]\s*\)|\b(?:window|globalThis|self)\s*(?:\.\s*fetch|\[\s*['"`]fetch['"`]\s*\]))/;

/**
 * Tests a single line for forbidden network primitives after stripping comments.
 *
 * @param {string} line
 * @returns {boolean}
 */
export function testNetworkPrimitive(line) {
  const stripped = stripLineComment(line).trim();
  if (!stripped) return false;
  return FORBIDDEN_NETWORK_REGEX.test(stripped);
}

/**
 * Scans a source file's raw content for forbidden network primitives.
 *
 * @param {string} rawContent
 * @param {string} relPath
 * @param {{ isPermittedLine?: (line: string, lineIdx: number, relPath: string) => boolean }} [options]
 * @returns {string[]} List of violation errors
 */
export function scanSourceForNetworkPrimitives(rawContent, relPath, options = {}) {
  const errors = [];
  const noBlock = stripBlockComments(rawContent);
  const lines = noBlock.split('\n');
  lines.forEach((line, idx) => {
    if (options.isPermittedLine && options.isPermittedLine(line, idx, relPath)) {
      return;
    }
    const stripped = stripLineComment(line).trim();
    if (!stripped) return;
    if (FORBIDDEN_NETWORK_REGEX.test(stripped)) {
      errors.push(`Privacy violation in ${relPath}:${idx + 1}: Unauthorized network primitive detected: ${stripped}`);
    }
  });
  return errors;
}

/**
 * Prohibited marketing buzzwords.
 */
export const BANNED_VOCABULARY = [
  { term: 'bulletproof', regex: /\bbullet-?proof\b/i },
  { term: 'rock solid', regex: /\brock\s+solid\b/i },
  { term: 'conclusive', regex: /\bconclusive\b/i },
  { term: 'definitive', regex: /\bdefinitive\b/i },
  { term: 'infinitely scalable', regex: /\binfinitely\s+scalable\b/i },
];

/**
 * Scans documentation content for banned marketing vocabulary.
 *
 * @param {string} content
 * @param {string} relPath
 * @param {{ isPermittedLine?: (line: string, lineIdx: number, relPath: string) => boolean }} [options]
 * @returns {string[]} List of violation errors
 */
export function scanDocumentationForVocabulary(content, relPath, options = {}) {
  const errors = [];
  const lines = content.split('\n');
  lines.forEach((line, idx) => {
    const lower = line.toLowerCase();
    if (
      lower.includes('prohibits unprovable') ||
      lower.includes('prohibited word') ||
      lower.includes('prohibited marketing') ||
      lower.includes('banned vocabulary')
    ) {
      return;
    }
    if (options.isPermittedLine && options.isPermittedLine(line, idx, relPath)) {
      return;
    }
    for (const { term, regex } of BANNED_VOCABULARY) {
      if (regex.test(line)) {
        errors.push(`Vocabulary violation in ${relPath}:${idx + 1}: Prohibited marketing term '${term}' found: "${line.trim()}"`);
      }
    }
  });
  return errors;
}
