import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

// @ts-expect-error The production analyzer is intentionally a JavaScript CLI module.
import {
  analyzeStyleUsage,
  renderStyleUsageHtml,
  runStyleAudit,
} from '../audit-unused-styles.mjs';

const temporaryProjects: string[] = [];

async function fixtureProject(): Promise<string> {
  const root = await mkdtemp(join(tmpdir(), 'style-audit-'));
  temporaryProjects.push(root);
  await mkdir(join(root, 'src', 'app'), { recursive: true });
  await writeFile(
    join(root, 'angular.json'),
    JSON.stringify({
      projects: {
        APP: {
          sourceRoot: 'src',
          architect: {
            build: {
              options: { styles: ['src/styles.css'], browser: 'src/main.ts' },
            },
          },
        },
      },
    }),
  );
  await writeFile(join(root, 'package.json'), '{"name":"fixture","token":"secret-value"}');
  await writeFile(join(root, 'tsconfig.json'), '{"compilerOptions":{"strict":true}}');
  await writeFile(join(root, 'src', 'main.ts'), "import './app/example.component';\n");
  await writeFile(
    join(root, 'src', 'styles.css'),
    `@import './theme.scss';
:root {
  --brand: #123456;
  --typescript-brand: #654321;
  --template-brand: #abcdef;
  --unused-token: 1rem;
}
.literal { color: var(--brand); animation: pulse 1s; }
.bound {}
.ng-array {}
.ng-object {}
.ng-conditional {}
.hosted {}
.host-bound {}
.bound-host {}
.ts-string {}
.if-only {}
.else-only {}
.switch-only {}
.switch-default-only {}
.for-only {}
.for-empty-only {}
.defer-only {}
.defer-placeholder-only {}
.defer-loading-only {}
.defer-error-only {}
.dynamic-success {}
.status-danger {}
.feature-only {}
.p-button {}
.pi-check {}
.md\\:block {}
.tooltip::before { content: "help"; }
.unused {}
@keyframes pulse { from { opacity: 0; } to { opacity: 1; } }
@keyframes dormant { from { opacity: 0; } to { opacity: 1; } }
@media (min-width: 40rem) { .responsive-only { display: block; } }
@supports (display: grid) { .supported-only { display: grid; } }
@media print { .print-only { display: block; } }
`,
  );
  await writeFile(
    join(root, 'src', 'theme.scss'),
    `@use './tokens';
$accent: tokens.$accent;
$unused-sass: pink;
@mixin card { border: 1px solid $accent; }
@mixin orphan { color: pink; }
%shared { padding: 1rem; }
.sass-user { @include card; @extend %shared; }
`,
  );
  await writeFile(join(root, 'src', '_tokens.scss'), '$accent: rebeccapurple;\n');
  await writeFile(
    join(root, 'src', 'app', 'example.component.scss'),
    '.component-class { display: block; }\n',
  );
  await writeFile(
    join(root, 'src', 'app', 'example.component.html'),
    `<main class="literal component-class inline-class hoverable"
  [class.bound]="enabled"
  [style.--template-brand]="tone"
  [ngClass]="['ng-array', { 'ng-object': enabled }, enabled ? 'ng-conditional' : 'other']">
  <p-button styleClass="p-button"></p-button>
</main>
@if (enabled) {
  <span class="if-only"></span>
} @else {
  <span class="else-only"></span>
}
@switch (tone) {
  @case ('success') { <span class="switch-only"></span> }
  @default { <span class="switch-default-only"></span> }
}
@for (item of items; track item) {
  <span class="for-only"></span>
} @empty {
  <span class="for-empty-only"></span>
}
@defer {
  <span class="defer-only"></span>
} @placeholder {
  <span class="defer-placeholder-only"></span>
} @loading {
  <span class="defer-loading-only"></span>
} @error {
  <span class="defer-error-only"></span>
}`,
  );
  await writeFile(
    join(root, 'src', 'app', 'example.component.ts'),
    `import { Component, HostBinding } from '@angular/core';
@Component({
  selector: 'app-example',
  templateUrl: './example.component.html',
  styleUrl: './example.component.scss',
  host: { class: 'hosted', '[class.host-bound]': 'enabled' },
  styles: [\`.inline-class { display: grid; }\`],
})
export class ExampleComponent {
  @HostBinding('class.bound-host') boundHost = true;
  readonly className = 'ts-string';
  readonly dynamicClass = 'dynamic-' + this.tone;
  readonly templateDynamicClass = \`status-\${this.tone}\`;
  readonly brand = document.body.style.getPropertyValue('--typescript-brand');
  readonly featureClass = this.enabled ? 'feature-only' : '';
  tone = 'success';
  enabled = true;
}
`,
  );
  return root;
}

function entry(report: any, normalizedEntry: string): any {
  return report.entries.find((item: any) => item.normalizedEntry === normalizedEntry);
}

async function styleHashes(root: string): Promise<Record<string, string>> {
  const paths = [
    'src/styles.css',
    'src/theme.scss',
    'src/_tokens.scss',
    'src/app/example.component.scss',
  ];
  return Object.fromEntries(
    await Promise.all(
      paths.map(async (path) => [
        path,
        createHash('sha256').update(await readFile(join(root, path))).digest('hex'),
      ]),
    ),
  );
}

afterEach(async () => {
  await Promise.all(temporaryProjects.splice(0).map((path) => rm(path, { recursive: true })));
});

describe('static first-party style usage audit', () => {
  it('inventories active, alternate, component, inline, Sass, template, TypeScript, and configuration sources', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    expect(report.inventory.activeGlobalStyles).toEqual(['src/styles.css']);
    expect(report.inventory.alternateGlobalStyles).toContain('src/theme.scss');
    expect(report.inventory.componentStyles).toContain('src/app/example.component.scss');
    expect(report.inventory.inlineStyles[0]).toMatchObject({
      path: 'src/app/example.component.ts',
      kind: 'inline-style',
    });
    expect(report.inventory.sassDependencies).toEqual(
      expect.arrayContaining(['src/_tokens.scss', 'src/theme.scss']),
    );
    expect(report.inventory.templates).toContain('src/app/example.component.html');
    expect(report.inventory.typescriptFiles).toContain('src/app/example.component.ts');
    expect(report.inventory.configurationFiles).toEqual(
      expect.arrayContaining(['angular.json', 'package.json', 'tsconfig.json']),
    );
  });

  it('matches Angular, TypeScript, keyframe, variable, and Sass references with locations', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    for (const name of [
      '.literal',
      '.bound',
      '.ng-array',
      '.ng-object',
      '.ng-conditional',
      '.hosted',
      '.host-bound',
      '.bound-host',
      '.ts-string',
      '.component-class',
      '.inline-class',
      '@keyframes pulse',
      '--brand',
      '@mixin card',
      '%shared',
    ]) {
      expect(entry(report, name)?.references.length, name).toBeGreaterThan(0);
      expect(entry(report, name)?.references[0]).toMatchObject({
        path: expect.any(String),
        line: expect.any(Number),
        column: expect.any(Number),
      });
    }
  });

  it('matches classes inside Angular control-flow and deferred blocks', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    for (const name of [
      '.if-only',
      '.else-only',
      '.switch-only',
      '.switch-default-only',
      '.for-only',
      '.for-empty-only',
      '.defer-only',
      '.defer-placeholder-only',
      '.defer-loading-only',
      '.defer-error-only',
    ]) {
      expect(entry(report, name)?.disposition, name).toBe('used');
    }
  });

  it('retains selectors matching TypeScript template-literal class prefixes', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    expect(entry(report, '.status-danger')).toMatchObject({
      disposition: 'uncertain',
      risks: expect.arrayContaining(['dynamic-class']),
    });
  });

  it('matches custom properties referenced from TypeScript and Angular style bindings', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    for (const name of ['--typescript-brand', '--template-brand']) {
      expect(entry(report, name)?.disposition, name).toBe('used');
      expect(entry(report, name)?.references.length, name).toBeGreaterThan(0);
    }
  });

  it('emits used, removable-candidate, and conservative uncertain dispositions', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    expect(entry(report, '.literal').disposition).toBe('used');
    expect(entry(report, '.unused').disposition).toBe('removable-candidate');
    for (const name of [
      '.dynamic-success',
      '.feature-only',
      '.p-button',
      '.pi-check',
      '.md\\:block',
      '.tooltip::before',
      '.responsive-only',
      '.supported-only',
      '.print-only',
      '@keyframes dormant',
    ]) {
      expect(entry(report, name)?.disposition, name).toBe('uncertain');
      expect(entry(report, name)?.allowlistReason, name).toEqual(expect.any(String));
    }
    expect(entry(report, '.dynamic-success').risks).toContain('dynamic-class');
    expect(entry(report, '.feature-only').risks).toContain('feature-gated');
    expect(entry(report, '.p-button').risks).toContain('framework-owned');
    expect(entry(report, '.tooltip::before').risks).toContain('pseudo-selector');
    expect(entry(report, '.responsive-only').risks).toContain('responsive-rule');
    expect(entry(report, '.print-only').risks).toContain('print-rule');
  });

  it('renders equivalent redacted JSON and HTML evidence with the required schema', async () => {
    const rootDir = await fixtureProject();
    const report = await analyzeStyleUsage({ rootDir });
    const html = renderStyleUsageHtml(report);

    expect(report.summary.total).toBe(report.entries.length);
    expect(Object.values(report.summary.dispositions).reduce((a: any, b: any) => a + b, 0)).toBe(
      report.entries.length,
    );
    expect(report.entries[0]).toEqual(
      expect.objectContaining({
        kind: expect.any(String),
        normalizedEntry: expect.any(String),
        source: {
          path: expect.not.stringContaining(rootDir),
          line: expect.any(Number),
          column: expect.any(Number),
        },
        references: expect.any(Array),
        risks: expect.any(Array),
        disposition: expect.stringMatching(/^(used|removable-candidate|uncertain)$/),
      }),
    );
    expect(
      report.entries.every(
        (item: any) =>
          item.allowlistReason === null || typeof item.allowlistReason === 'string',
      ),
    ).toBe(true);
    expect(html).toContain('<!doctype html>');
    expect(html).toContain('Static first-party style usage');
    expect(html).not.toContain(rootDir);
  });

  it('writes only redacted audit artifacts and preserves every style source byte-for-byte', async () => {
    const rootDir = await fixtureProject();
    const before = await styleHashes(rootDir);

    await runStyleAudit({ rootDir });

    expect(await styleHashes(rootDir)).toEqual(before);
    expect((await readdir(join(rootDir, '.ui-audit', 'style-usage'))).sort()).toEqual([
      'report.html',
      'report.json',
    ]);
    const json = await readFile(
      join(rootDir, '.ui-audit', 'style-usage', 'report.json'),
      'utf8',
    );
    const html = await readFile(
      join(rootDir, '.ui-audit', 'style-usage', 'report.html'),
      'utf8',
    );
    expect(json).not.toContain('secret-value');
    expect(html).not.toContain('secret-value');
  });
});
