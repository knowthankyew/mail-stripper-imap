/**
 * Shared Claims & Privacy Invariant Linter Core (@knowthankyew)
 * Provides string-aware comment stripping, network primitive detection,
 * and banned vocabulary scanning.
 */

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
 * C#-aware comment stripper for whole C# source files.
 * Strips both block comments and line comments while respecting C# character and string literals.
 *
 * @param {string} source
 * @returns {string}
 */
export function stripCSharpComments(source) {
  const noBlock = stripBlockComments(source);
  return noBlock
    .split('\n')
    .map(line => stripCSharpLineComment(line))
    .join('\n');
}

/**
 * Strips C# // comments from a single line while respecting C# strings and char literals.
 *
 * @param {string} line
 * @returns {string}
 */
export function stripCSharpLineComment(line) {
  let inString = false;
  let inChar = false;
  let escape = false;

  for (let i = 0; i < line.length; i++) {
    const c = line[i];

    if (inChar) {
      if (escape) {
        escape = false;
      } else if (c === '\\') {
        escape = true;
      } else if (c === "'") {
        inChar = false;
      }
      continue;
    }

    if (inString) {
      if (escape) {
        escape = false;
      } else if (c === '\\') {
        escape = true;
      } else if (c === '"') {
        inString = false;
      }
      continue;
    }

    if (c === "'") {
      inChar = true;
      escape = false;
    } else if (c === '"') {
      inString = true;
      escape = false;
    } else if (c === '/' && line[i + 1] === '/') {
      return line.slice(0, i);
    }
  }

  return line;
}

/**
 * String-aware line comment stripper for JS/TS lines.
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
 * Note: Regex entries intentionally omit the 'g' flag so that repeated .test() calls
 * remain completely stateless and never depend on shared lastIndex state across lines.
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
    if (options.isPermittedLine && options.isPermittedLine(line, idx, relPath)) {
      return;
    }
    const lower = line.toLowerCase();
    const isMetaPolicyLine =
      lower.includes('prohibits unprovable') ||
      lower.includes('prohibited marketing') ||
      lower.includes('banned vocabulary') ||
      lower.includes('banned marketing') ||
      lower.includes('prohibited word');

    for (const { term, regex } of BANNED_VOCABULARY) {
      if (regex.test(line)) {
        // If it's a meta-policy explanation line, allow it only if the term is explicitly quoted
        // (e.g. `bulletproof` or 'bulletproof' or "bulletproof") to refer to the rule itself.
        if (isMetaPolicyLine) {
          const quotedRegex = new RegExp(`['"\`]${term}['"\`]`, 'i');
          if (quotedRegex.test(line)) {
            continue;
          }
        }
        errors.push(`Vocabulary violation in ${relPath}:${idx + 1}: Prohibited marketing term '${term}' found: "${line.trim()}"`);
      }
    }
  });
  return errors;
}
