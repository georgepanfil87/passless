#!/usr/bin/env node
/**
 * Fails if any component names a colour directly.
 *
 * The design system's central claim is that a theme is a re-mapping of tokens
 * and that no component can tell which theme it is in. A single literal colour
 * breaks that for one element in one theme, and it does so silently: the light
 * theme still looks correct, so review does not catch it. Clearing Tailwind's
 * palette stops `bg-red-500` from resolving, but nothing in the build stops
 * `bg-[#ff0000]`, so the rule is checked here instead.
 */
import { globSync, readFileSync } from 'node:fs';
import { relative } from 'node:path';

const ROOTS = ['src/design/components/**/*.ts', 'src/app/**/*.ts', 'src/app/**/*.html'];

const RULES = [
  { name: 'hex literal', re: /#[0-9a-fA-F]{3,8}\b/g },
  { name: 'colour function', re: /\b(?:rgba?|hsla?|oklch|oklab|color-mix)\s*\(/g },
  { name: 'arbitrary colour utility', re: /\b(?:bg|text|border|fill|stroke|outline|ring|shadow|from|via|to)-\[\s*(?:#|rgb|hsl|oklch)/g },
];

// Character references such as &#64; are text, not colour.
const ALLOW = [/&#\d+;/];

let failures = 0;

for (const pattern of ROOTS) {
  for (const file of globSync(pattern)) {
    const source = readFileSync(file, 'utf8');
    for (const { name, re } of RULES) {
      for (const match of source.matchAll(re)) {
        const context = source.slice(Math.max(0, match.index - 12), match.index + 24);
        if (ALLOW.some(a => a.test(context))) continue;
        const line = source.slice(0, match.index).split('\n').length;
        console.error(`${relative(process.cwd(), file)}:${line}  ${name}: ${match[0]}`);
        failures++;
      }
    }
  }
}

if (failures > 0) {
  console.error(`\n${failures} literal colour(s) found. Add a token in src/design/tokens.css instead.`);
  process.exit(1);
}

console.log('No literal colours in components.');
