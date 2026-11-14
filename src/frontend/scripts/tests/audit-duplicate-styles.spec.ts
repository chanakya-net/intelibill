import { spawnSync } from 'node:child_process';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

// @ts-expect-error The audit is intentionally a JavaScript CLI module.
import { auditDuplicateStyles, blocksOf, CEILING_FILE, findDuplicateBlocks, readTokens } from '../audit-duplicate-styles.mjs';

const frontendRoot = join(import.meta.dir, '..', '..');
const temporaryProjects: string[] = [];

afterEach(async () => {
  await Promise.all(temporaryProjects.splice(0).map((root) => rm(root, { recursive: true, force: true })));
});

const CARD = `
.card {
  /* the frame */
  border: 1px solid #ead7c1;
  border-radius: 1rem;
  padding: 1rem;
}`;

async function fixtureProject(files: Record<string, string>, ceiling: number): Promise<{ root: string; ceilingFile: string }> {
  const root = await mkdtemp(join(tmpdir(), 'duplicate-styles-'));
  temporaryProjects.push(root);
  for (const [path, source] of Object.entries(files)) {
    await mkdir(join(root, path, '..'), { recursive: true });
    await writeFile(join(root, path), source);
  }
  const ceilingFile = join(root, 'ceiling.json');
  await writeFile(ceilingFile, JSON.stringify({ ceiling }));
  return { root, ceilingFile };
}

describe('audit-duplicate-styles', () => {
  it('compares declaration sets, ignoring comments, order, spacing, case and the selector', () => {
    const reordered = '.other-name{padding:1rem;BORDER-RADIUS:1rem;border:1px   solid #ead7c1}';

    expect(blocksOf(CARD)[0].body).toBe('border-radius: 1rem; border: 1px solid #ead7c1; padding: 1rem');
    expect(blocksOf(reordered)[0].body).toBe(blocksOf(CARD)[0].body);
  });

  it('keeps !important and nested blocks apart from their parent', () => {
    const [outer, inner] = blocksOf(`
      .a { color: red; margin: 0; padding: 0 !important;
        .b { color: blue; margin: 1px; padding: 2px; }
      }`);

    expect(outer.body).toBe('color: red; margin: 0; padding: 0 !important');
    expect(inner.body).toBe('color: blue; margin: 1px; padding: 2px');
  });

  it('counts a block once it has three declarations and appears in four files', () => {
    const twoDeclarations = '.x { color: red; margin: 0; }';
    const files = (count: number, source: string) =>
      Object.fromEntries(Array.from({ length: count }, (_, index) => [`f${index}.scss`, source]));

    expect(findDuplicateBlocks(files(3, CARD)).count).toBe(0);
    expect(findDuplicateBlocks(files(4, CARD))).toEqual({
      count: 1,
      blocks: [{ body: blocksOf(CARD)[0].body, files: ['f0.scss', 'f1.scss', 'f2.scss', 'f3.scss'] }],
    });
    expect(findDuplicateBlocks(files(6, twoDeclarations)).count).toBe(0);
    // Repeats inside one file are one file.
    expect(findDuplicateBlocks({ 'a.scss': CARD.repeat(5) }).count).toBe(0);
  });

  it('compares design tokens by value, so tokenising some copies does not hide them', async () => {
    const tokenised = CARD.replace('#ead7c1', 'var(--app-sand-300)').replace('1rem;\n  padding', 'var( --app-radius-lg );\n  padding');
    const project = await fixtureProject(
      {
        'src/styles.css': ':root { --app-sand-300: #ead7c1; --app-radius-lg: 1rem; }\n.other { --app-unused: red; }',
        'src/app/a.component.scss': CARD,
        'src/app/b.component.scss': CARD,
        'src/app/c.component.scss': tokenised,
        'src/app/d.component.scss': tokenised,
      },
      1,
    );

    expect(readTokens(':root { --a: 1px; } .x { --b: 2px; }')).toEqual({ '--a': '1px' });
    expect(blocksOf(tokenised, 'c.scss', { '--app-sand-300': '#ead7c1', '--app-radius-lg': '1rem' })[0].body).toBe(
      blocksOf(CARD)[0].body,
    );
    expect((await auditDuplicateStyles(project)).count).toBe(1);
  });

  it('resolves design tokens from the shared stylesheet', async () => {
    const project = await fixtureProject(
      {
        'web/src/styles.css': "@import '@intelibill/ui/styles/tokens.css';",
        'shared/frontend/ui/styles/tokens.css': ':root { --app-sand-300: #ead7c1; }',
        'web/src/app/a.scss': CARD,
        'web/src/app/b.scss': CARD,
        'web/src/app/c.scss': CARD,
        'web/src/app/d.scss': CARD.replace('#ead7c1', 'var(--app-sand-300)'),
      },
      1,
    );
    expect((await auditDuplicateStyles({ ...project, root: join(project.root, 'web') })).count).toBe(1);
  });

  it('reads component stylesheets under src/app only, skipping partials', async () => {
    const files = {
      'src/app/a/a.component.scss': CARD,
      'src/app/b/b.component.scss': CARD,
      'src/app/c/c.component.scss': CARD,
      'src/app/shared/_partial.scss': CARD,
      'src/styles.scss': CARD,
    };
    const project = await fixtureProject(files, 0);
    expect((await auditDuplicateStyles(project)).count).toBe(0);

    await writeFile(join(project.root, 'src/app/d.component.scss'), CARD);
    const report = await auditDuplicateStyles(project);
    expect(report).toMatchObject({ count: 1, ceiling: 0, passed: false, files: 4 });
  });

  it('passes the repository at or under its ceiling, which only goes down', async () => {
    const report = await auditDuplicateStyles();
    const { ceiling } = JSON.parse(await readFile(CEILING_FILE, 'utf8'));

    expect(report.ceiling).toBe(ceiling);
    expect(report.passed).toBe(true);
    expect(ceiling).toBeLessThanOrEqual(64);
  });

  it('fails the CLI when the count exceeds the ceiling', async () => {
    const project = await fixtureProject(
      Object.fromEntries(['a', 'b', 'c', 'd'].map((name) => [`src/app/${name}.component.scss`, CARD])),
      0,
    );
    const script = join(frontendRoot, 'scripts', 'audit-duplicate-styles.mjs');
    const failing = spawnSync(
      process.execPath,
      [script, '--root', project.root, '--ceiling-file', project.ceilingFile],
      { encoding: 'utf8' },
    );
    expect(failing.status).toBe(1);
    expect(failing.stdout).toContain(': 1, ceiling 0.');

    const repository = spawnSync(process.execPath, [script], { cwd: frontendRoot, encoding: 'utf8' });
    expect(repository.status, repository.stderr).toBe(0);
    expect(repository.stdout).toMatch(/Duplicate style blocks .*: \d+, ceiling \d+\./);
  });
});
