import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { ROUTE_MANIFEST } from '../../tests/ui-audit/route-manifest';
import {
  buildScenarioCatalog,
  scenarioId,
  writeCoverageArtifact,
} from '../../tests/ui-audit/support/css-coverage';

// @ts-expect-error The production analyzer is intentionally a JavaScript CLI module.
import {
  analyzeStyleUsage,
  isRuntimeObservable,
  loadRuntimeCoverage,
  mapRuntimeCoverage,
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
.runtime-covered {}
.runtime-uncovered {}
.runtime-unobserved {}
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
@mixin card-namespaced { display: flex; }
.sass-user { @include card; @extend %shared; }
`,
  );
  await writeFile(
    join(root, 'src', 'namespaced.scss'),
    `@use './theme' as t;
.consumer { @include t.card-namespaced; }
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
  it('builds a complete deterministic scenario catalog from the route manifest', () => {
    const catalog = buildScenarioCatalog(ROUTE_MANIFEST);

    expect(buildScenarioCatalog(ROUTE_MANIFEST)).toEqual(catalog);
    expect(new Set(catalog.map((scenario) => scenario.id)).size).toBe(catalog.length);
    for (const route of ROUTE_MANIFEST) {
      for (const state of route.states) {
        for (const viewport of route.viewports) {
          expect(
            catalog.some(
              (scenario) =>
                scenario.kind === 'core' &&
                scenario.route === route.path &&
                scenario.state === state &&
                scenario.viewport === viewport,
            ),
            `${route.path}/${state}/${viewport}`,
          ).toBe(true);
        }
      }
    }
    expect(new Set(catalog.filter(({ route }) => route === 'login').map(({ locale }) => locale)))
      .toEqual(new Set(ROUTE_MANIFEST[0].locales));
    expect(new Set(catalog.filter(({ route }) => route === 'dashboard').map(({ role }) => role)))
      .toEqual(new Set(['owner', 'manager']));
    expect(catalog).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ route: 'login', offline: true }),
        expect.objectContaining({ route: 'sales/new', offline: true }),
        expect.objectContaining({ route: 'login', state: 'submitting', interaction: 'submit' }),
        expect.objectContaining({ zone: 'standalone-print', media: 'print' }),
      ]),
    );
    expect(catalog[0].id).toBe(scenarioId(catalog[0]));
  });

  it('persists and reloads per-scenario coverage artifacts with metadata', async () => {
    const rootDir = await fixtureProject();
    const coverageDir = join(rootDir, '.ui-audit', 'style-usage', 'runtime');
    const scenarios = buildScenarioCatalog(ROUTE_MANIFEST).slice(0, 2);
    const stylesheet = '.covered { color: green; } .uncovered { color: red; }';
    for (const scenario of scenarios) {
      await writeCoverageArtifact({
        coverageDir,
        scenario,
        browser: { name: 'chromium', version: '140.0' },
        entries: [
          {
            url: 'http://127.0.0.1:4300/styles.css?token=secret-value',
            text: stylesheet,
            ranges: [{ start: 0, end: 27 }],
          },
        ],
      });
    }

    const runtime = await loadRuntimeCoverage({
      coverageDir,
      expectedScenarios: scenarios,
    });
    expect(runtime).toMatchObject({
      status: 'complete',
      missingScenarioIds: [],
      failedScenarioIds: [],
    });
    expect(runtime.scenarios).toHaveLength(2);
    expect(runtime.scenarios[0]).toMatchObject({
      schemaVersion: 1,
      browser: { name: 'chromium', version: '140.0' },
      status: 'success',
      scenario: { id: expect.any(String), viewport: expect.any(String) },
      sheets: [{ hash: expect.stringMatching(/^[a-f0-9]{64}$/), ranges: [{ start: 0, end: 27 }] }],
    });
    expect(await readdir(join(coverageDir, 'stylesheets'))).toHaveLength(1);
    expect(JSON.stringify(runtime.scenarios)).not.toContain('secret-value');
  });

  it('redacts captured stylesheet text without shifting Chromium coverage offsets', async () => {
    const rootDir = await fixtureProject();
    const coverageDir = join(rootDir, '.ui-audit', 'style-usage', 'runtime');
    const scenario = buildScenarioCatalog(ROUTE_MANIFEST)[0];
    const stylesheet = '/*! framework 4.1.11 */\n.x{}';
    const ruleStart = stylesheet.indexOf('.x{}');
    await writeCoverageArtifact({
      coverageDir,
      scenario,
      browser: { name: 'chromium', version: '140.0' },
      entries: [{
        url: '',
        text: stylesheet,
        ranges: [{ start: ruleStart, end: stylesheet.length }],
      }],
    });

    const runtime = await loadRuntimeCoverage({ coverageDir, expectedScenarios: [scenario] });
    const captured = [...runtime.stylesheetTexts.values()][0];
    expect(captured).toHaveLength(stylesheet.length);
    expect(captured).not.toContain('4.1.11');
    expect(mapRuntimeCoverage(runtime)).toContainEqual(expect.objectContaining({
      kind: 'selector',
      normalizedEntry: '.x',
      status: 'covered',
    }));
  });

  it('maps covered and uncovered runtime ranges to normalized entries', async () => {
    const rootDir = await fixtureProject();
    const coverageDir = join(rootDir, '.ui-audit', 'style-usage', 'runtime');
    const catalog = buildScenarioCatalog(ROUTE_MANIFEST);
    const scenarios = [
      catalog.find(({ kind, route }) => kind === 'core' && route === 'login')!,
      catalog.find(({ kind }) => kind === 'print')!,
    ];
    const stylesheet = `.covered[_ngcontent-ng-c1] { color: green; --covered-token: 1; }
.uncovered[_nghost-ng-c2] { color: red; --uncovered-token: 1; }
@media print { .print-only[_ngcontent-ng-c1] { display: block; } }`;
    const ranges = [
      [{ start: 0, end: stylesheet.indexOf('.uncovered') }],
      [{ start: stylesheet.indexOf('.print-only'), end: stylesheet.length }],
    ];
    for (const [index, scenario] of scenarios.entries()) {
      await writeCoverageArtifact({
        coverageDir,
        scenario,
        browser: { name: 'chromium', version: '140.0' },
        entries: [{ url: '', text: stylesheet, ranges: ranges[index] }],
      });
    }

    const runtime = await loadRuntimeCoverage({ coverageDir, expectedScenarios: scenarios });
    const evidence = mapRuntimeCoverage(runtime);
    const find = (kind: string, name: string) =>
      evidence.find((item: any) => item.kind === kind && item.normalizedEntry === name);
    expect(find('selector', '.covered')).toMatchObject({
      status: 'covered',
      coveredScenarios: [scenarios[0].id],
    });
    expect(find('selector', '.uncovered')).toMatchObject({
      status: 'uncovered',
      coveredScenarios: [],
      observedScenarios: expect.arrayContaining(scenarios.map(({ id }) => id)),
    });
    expect(find('media-rule', '@media print')).toMatchObject({
      status: 'covered',
      coveredScenarios: [scenarios[1].id],
    });
    expect(find('custom-property', '--uncovered-token')?.status).toBe('uncovered');
    expect(isRuntimeObservable('selector')).toBe(true);
    expect(isRuntimeObservable('keyframes')).toBe(false);
    expect(isRuntimeObservable('sass-mixin')).toBe(false);
  });

  it('merges static and runtime evidence into conservative dispositions', async () => {
    const rootDir = await fixtureProject();
    const coverageDir = join(rootDir, '.ui-audit', 'style-usage', 'runtime');
    const scenario = buildScenarioCatalog(ROUTE_MANIFEST)[0];
    const stylesheet = `.runtime-covered { color: green; }
.runtime-uncovered { color: red; }
.literal { color: blue; }
.dynamic-success { color: orange; }`;
    await writeCoverageArtifact({
      coverageDir,
      scenario,
      browser: { name: 'chromium', version: '140.0' },
      entries: [{
        url: '',
        text: stylesheet,
        ranges: [
          { start: 0, end: stylesheet.indexOf('.runtime-uncovered') },
          { start: stylesheet.indexOf('.dynamic-success'), end: stylesheet.length },
        ],
      }],
    });

    const report = await analyzeStyleUsage({
      rootDir,
      runtime: { coverageDir, expectedScenarios: [scenario] },
    });
    expect(entry(report, '.runtime-covered')).toMatchObject({
      disposition: 'uncertain',
      runtime: { status: 'covered', coveredScenarios: [scenario.id] },
      allowlistReason: expect.stringContaining('runtime'),
    });
    expect(entry(report, '.runtime-uncovered')).toMatchObject({
      disposition: 'removable-candidate',
      runtime: { status: 'uncovered', coveredScenarios: [] },
    });
    expect(entry(report, '.runtime-unobserved')).toMatchObject({
      disposition: 'uncertain',
      runtime: { status: 'not-observed' },
    });
    expect(entry(report, '.literal').disposition).toBe('used');
    expect(entry(report, '.dynamic-success')).toMatchObject({
      disposition: 'uncertain',
      risks: expect.arrayContaining(['dynamic-class']),
    });
  });

  it('fails closed when the scenario catalog is incomplete', async () => {
    const rootDir = await fixtureProject();
    const coverageDir = join(rootDir, '.ui-audit', 'style-usage', 'runtime');
    const scenarios = buildScenarioCatalog(ROUTE_MANIFEST).slice(0, 3);
    await writeCoverageArtifact({
      coverageDir,
      scenario: scenarios[0],
      browser: { name: 'chromium', version: '140.0' },
      entries: [{ url: '', text: '.runtime-uncovered {}', ranges: [] }],
    });
    await writeCoverageArtifact({
      coverageDir,
      scenario: scenarios[1],
      browser: { name: 'chromium', version: '140.0' },
      error: new Error('navigation failed'),
    });

    const report = await runStyleAudit({
      rootDir,
      runtime: { coverageDir, expectedScenarios: scenarios },
    });
    expect(report.runtime).toMatchObject({
      status: 'incomplete',
      successfulScenarioCount: 1,
      failedScenarioIds: [scenarios[1].id],
      missingScenarioIds: [scenarios[2].id],
    });
    expect(entry(report, '.runtime-uncovered')).toMatchObject({
      disposition: 'uncertain',
      allowlistReason: expect.stringContaining('incomplete'),
    });
    expect(
      await readFile(join(rootDir, '.ui-audit', 'style-usage', 'report.json'), 'utf8'),
    ).toContain('"status": "incomplete"');
    const result = spawnSync(
      process.execPath,
      ['run', join(import.meta.dir, '..', 'audit-unused-styles.mjs')],
      { cwd: rootDir, encoding: 'utf8' },
    );
    expect(result.status).toBe(1);
  });

  it('renders merged static/runtime evidence in redacted JSON and HTML', async () => {
    const rootDir = await fixtureProject();
    const coverageDir = join(rootDir, '.ui-audit', 'style-usage', 'runtime');
    const scenario = buildScenarioCatalog(ROUTE_MANIFEST)[0];
    await writeCoverageArtifact({
      coverageDir,
      scenario,
      browser: { name: 'chromium', version: '140.0' },
      entries: [{
        url: 'http://localhost/styles.css?token=secret-value',
        text: '.runtime-covered {}',
        ranges: [{ start: 0, end: 21 }],
      }],
    });

    await runStyleAudit({
      rootDir,
      runtime: { coverageDir, expectedScenarios: [scenario] },
    });
    const json = await readFile(join(rootDir, '.ui-audit', 'style-usage', 'report.json'), 'utf8');
    const html = await readFile(join(rootDir, '.ui-audit', 'style-usage', 'report.html'), 'utf8');
    const parsed = JSON.parse(json);
    expect(parsed).toMatchObject({
      runtime: { status: 'complete', scenarios: [{ scenario: { id: scenario.id } }] },
    });
    expect(entry(parsed, '.runtime-covered')).toMatchObject({
      source: { path: 'src/styles.css' },
      runtime: { status: 'covered', coveredScenarios: [scenario.id] },
    });
    expect(html).toContain('Runtime CSS coverage: complete');
    expect(html).toContain('Covered scenarios');
    expect(html).toContain(scenario.id);
    expect(`${json}${html}`).not.toContain(rootDir);
    expect(`${json}${html}`).not.toContain('secret-value');
  });

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

  it('resolves namespaced Sass includes to the mixin name', async () => {
    const report = await analyzeStyleUsage({ rootDir: await fixtureProject() });

    expect(entry(report, '@mixin card-namespaced')?.disposition).toBe('used');
    expect(entry(report, '@mixin card-namespaced')?.references.length).toBeGreaterThan(0);
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
