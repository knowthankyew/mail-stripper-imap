/**
 * Shared Claims & Privacy Invariant Linter Core (@knowthankyew)
 * Provides string-aware comment stripping, network primitive detection,
 * and banned vocabulary scanning.
 */

/**
 * String-aware line comment stripper.
 * Strips `//` comments while preserving `//` inside string literals.
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
 * Regular expression matching forbidden client-side network egress primitives.
 */
export const FORBIDDEN_NETWORK_REGEX =
  /(?:\b(?:fetch|\??\.\s*fetch)\s*\(|=\s*fetch\b|\b(?:XMLHttpRequest|axios)\b|\[\s*['"`](?:fetch|XMLHttpRequest|sendBeacon|WebSocket|EventSource|axios)['"`]\s*\]|\bnavigator\s*(?:\??\.\s*sendBeacon|\[\s*['"`]sendBeacon['"`]\s*\])|\bnew\s+(?:WebSocket|EventSource|Image)\b|\bnew\s+Worker\s*\(\s*(?:new\s+URL\s*\(\s*)?['"`]https?:\/\/|\bimport\s*\(\s*['"`]https?:\/\/|\bdocument\s*(?:\??\.\s*createElement|\[\s*['"`]createElement['"`]\s*\])\s*\(\s*['"`]script['"`]\s*\)|\b(?:window|globalThis|self)\s*(?:\.\s*fetch|\[\s*['"`]fetch['"`]\s*\]))/;

export function testNetworkPrimitive(line) {
  const stripped = stripLineComment(line).trim();
  if (!stripped) return false;
  return FORBIDDEN_NETWORK_REGEX.test(stripped);
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
      lower.includes('banned vocabulary') ||
      lower.includes('banned marketing')
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
