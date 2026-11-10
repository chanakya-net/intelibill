/*
 * Counts style rule blocks repeated word for word across component stylesheets.
 *
 * A block is the set of declarations directly inside one rule (nested rules and at-rules are
 * blocks of their own). Comments are dropped; each declaration is normalised (property lower-cased,
 * whitespace collapsed, `!important` kept) and the set is sorted, so order and formatting do not
 * hide a copy. A block counts when it has at least MIN_DECLARATIONS declarations and the same body
 * appears in at least MIN_FILES different component .scss files under src/app. The selector is not
 * part of the comparison: the same body under different class names is still a copy.
 *
 * The number of such distinct blocks may not exceed the ceiling in
 * audit-duplicate-styles.ceiling.json. Lower the ceiling whenever the count goes down, so it can
 * only fall (docs/superpowers/plans/2026-11-10-shared-styles-and-offline-pack.md, phase 0).
 *
 * Usage: bun run audit:styles:duplicates [--json] [--list]
 */
import { readFile, readdir } from 'node:fs/promises';
import { dirname, join, relative } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import scss from 'postcss-scss';

export const MIN_DECLARATIONS = 3;
export const MIN_FILES = 4;

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
export const CEILING_FILE = join(scriptDirectory, 'audit-duplicate-styles.ceiling.json');

async function listComponentStyles(directory) {
  const found = [];
  for (const item of await readdir(directory, { withFileTypes: true })) {
    const absolute = join(directory, item.name);
    if (item.isDirectory()) found.push(...(await listComponentStyles(absolute)));
    // Partials (`_name.scss`) are shared sources, not component stylesheets.
    else if (item.isFile() && item.name.endsWith('.scss') && !item.name.startsWith('_')) found.push(absolute);
  }
  return found.sort();
}

export function normaliseDeclaration(declaration) {
  const value = declaration.value.replace(/\s+/g, ' ').trim();
  return `${declaration.prop.toLowerCase()}: ${value}${declaration.important ? ' !important' : ''}`;
}

/** Declaration bodies of every rule in one stylesheet, each sorted and joined. */
export function blocksOf(source, from = 'inline.scss') {
  const blocks = [];
  scss.parse(source, { from }).walkRules((rule) => {
    const declarations = (rule.nodes ?? [])
      .filter((node) => node.type === 'decl')
      .map(normaliseDeclaration);
    if (declarations.length >= MIN_DECLARATIONS) {
      blocks.push({ selector: rule.selector, body: [...new Set(declarations)].sort().join('; ') });
    }
  });
  return blocks;
}

/**
 * @param {Record<string, string>} files path -> source
 * @returns {{ count: number, blocks: { body: string, files: string[] }[] }}
 */
export function findDuplicateBlocks(files) {
  const filesByBody = new Map();
  for (const [path, source] of Object.entries(files)) {
    for (const { body } of blocksOf(source, path)) {
      if (!filesByBody.has(body)) filesByBody.set(body, new Set());
      filesByBody.get(body).add(path);
    }
  }
  const blocks = [...filesByBody.entries()]
    .filter(([, paths]) => paths.size >= MIN_FILES)
    .map(([body, paths]) => ({ body, files: [...paths].sort() }))
    .sort((a, b) => b.files.length - a.files.length || a.body.localeCompare(b.body));
  return { count: blocks.length, blocks };
}

export async function auditDuplicateStyles({ root = join(scriptDirectory, '..'), ceilingFile = CEILING_FILE } = {}) {
  const appDirectory = join(root, 'src', 'app');
  const files = {};
  for (const path of await listComponentStyles(appDirectory)) {
    files[relative(root, path).replaceAll('\\', '/')] = await readFile(path, 'utf8');
  }
  const { count, blocks } = findDuplicateBlocks(files);
  const { ceiling } = JSON.parse(await readFile(ceilingFile, 'utf8'));
  return { count, ceiling, passed: count <= ceiling, files: Object.keys(files).length, blocks };
}

function option(name) {
  const index = process.argv.indexOf(name);
  return index === -1 ? undefined : process.argv[index + 1];
}

if (import.meta.main) {
  const report = await auditDuplicateStyles({
    ...(option('--root') ? { root: option('--root') } : {}),
    ...(option('--ceiling-file') ? { ceilingFile: option('--ceiling-file') } : {}),
  });
  if (process.argv.includes('--json')) {
    console.log(JSON.stringify(report, null, 2));
  } else {
    if (process.argv.includes('--list')) {
      for (const block of report.blocks) console.log(`${block.files.length} files: { ${block.body} }`);
    }
    console.log(
      `Duplicate style blocks (${MIN_DECLARATIONS}+ declarations in ${MIN_FILES}+ of ${report.files} component stylesheets): ${report.count}, ceiling ${report.ceiling}.`,
    );
  }
  if (!report.passed) {
    console.error(
      `More repeated blocks than the ceiling allows. Move the repeated rules into the shared UI kit instead of copying them (see audit-duplicate-styles.ceiling.json).`,
    );
    process.exitCode = 1;
  } else if (report.count < report.ceiling) {
    console.log(`The count fell: lower "ceiling" in scripts/audit-duplicate-styles.ceiling.json to ${report.count}.`);
  }
}
