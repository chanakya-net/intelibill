import { readdir, readFile } from 'node:fs/promises';
import { dirname, join, normalize, relative, resolve } from 'node:path';
import process from 'node:process';
import postcss from 'postcss';
import selectorParser from 'postcss-selector-parser';
import scss from 'postcss-scss';
import { ROUTE_MANIFEST } from '../tests/ui-audit/route-manifest.ts';
import { writeRedactedArtifacts } from '../tests/ui-audit/support/artifact-redaction.ts';
import { buildScenarioCatalog } from '../tests/ui-audit/support/css-coverage.ts';
import {
  addReference,
  inspectConfiguration,
  inspectTemplate,
  inspectTypeScript,
} from './style-audit-references.mjs';
import {
  isRuntimeObservable,
  loadRuntimeCoverage,
  mapRuntimeCoverage,
} from './style-audit-runtime.mjs';
export { isRuntimeObservable, loadRuntimeCoverage, mapRuntimeCoverage };
const STYLE_EXTENSIONS = /\.(?:css|scss|sass)$/i;
const CONFIGURATION = /^(?:angular|package|tsconfig[^/]*|ngsw-config)\.json$|postcss|tailwind/i;
const STRONG_RISKS = new Set([
  'conditional-rule',
  'dynamic-class',
  'dynamic-selector',
  'feature-gated',
  'framework-owned',
  'parse-incomplete',
  'print-rule',
  'pseudo-selector',
  'responsive-rule',
  'sass-nesting',
  'structural-selector',
]);
async function discoverFiles(root) {
  const found = [];
  async function visit(directory) {
    for (const item of await readdir(directory, { withFileTypes: true })) {
      if (['.git', '.ui-audit', 'dist', 'node_modules'].includes(item.name)) continue;
      const absolute = join(directory, item.name);
      if (item.isDirectory()) await visit(absolute);
      else if (item.isFile()) found.push(absolute);
    }
  }
  await visit(join(root, 'src'));
  for (const item of await readdir(root, { withFileTypes: true })) {
    if (item.isFile() && CONFIGURATION.test(item.name)) found.push(join(root, item.name));
  }
  return found.sort();
}
const toPath = (root, path) => relative(root, path).replaceAll('\\', '/');
function selectorDetails(selector) {
  const classes = [];
  const normalizedSelector = selectorParser((root) => {
    root.walkClasses((node) => classes.push(node.value));
  }).processSync(selector, { lossless: false });
  return { classes, normalizedSelector };
}
function risksForSelector(selector, classes, rule) {
  const risks = [];
  if (selector.includes('#{')) risks.push('dynamic-selector');
  if (selector.includes('&')) risks.push('sass-nesting');
  if (/(^|[^\\]):(?!root\b)/.test(selector)) risks.push('pseudo-selector');
  if (classes.length === 0) risks.push('structural-selector');
  if (classes.some(isFrameworkClass)) risks.push('framework-owned');
  if (classes.some((name) => /feature|experiment|flag/i.test(name))) risks.push('feature-gated');
  for (let parent = rule.parent; parent; parent = parent.parent) {
    if (parent.type !== 'atrule') continue;
    if (parent.name === 'media') risks.push(/print/i.test(parent.params) ? 'print-rule' : 'responsive-rule');
    if (['supports', 'container', 'if', 'for', 'each', 'while'].includes(parent.name)) risks.push('conditional-rule');
    if (parent.name === 'keyframes') risks.push('keyframe-step');
  }
  return [...new Set(risks)];
}
function isFrameworkClass(name) {
  return /^(?:p-|pi-|sm:|md:|lg:|xl:|2xl:|hover:|focus:|dark:|[mp][trblxy]?-\d|[wh]-\d|text-|bg-|rounded|border|flex$|grid$|block$|hidden$)/.test(name);
}
function entry(kind, normalizedEntry, document, node, risks = [], details = {}) {
  const start = node.source?.start ?? { line: 1, column: 1 };
  return {
    kind,
    normalizedEntry,
    source: {
      path: document.path,
      line: document.baseLine + start.line - 1,
      column: (start.line === 1 ? document.baseColumn - 1 : 0) + start.column,
    },
    references: [],
    risks,
    allowlistReason: null,
    disposition: 'removable-candidate',
    ...details,
  };
}
function inspectStyle(document, state) {
  let root;
  try {
    root = (document.path.endsWith('.scss') || document.path.endsWith('.sass') ? scss : postcss).parse(document.content, { from: document.path });
  } catch (error) {
    state.diagnostics.push({ path: document.path, message: String(error) });
    state.entries.push(entry('unparsed-source', document.path, document, {}, ['parse-incomplete']));
    return;
  }
  root.walkRules((rule) => {
    if (rule.parent?.type === 'atrule' && rule.parent.name === 'keyframes') return;
    for (const arm of rule.selectors ?? [rule.selector]) {
      try {
        const { classes, normalizedSelector } = selectorDetails(arm);
        state.entries.push(entry('selector', normalizedSelector, document, rule, risksForSelector(arm, classes, rule), { matchTokens: classes.length > 0 ? classes : [normalizedSelector] }));
      } catch {
        state.entries.push(entry('selector', arm.trim(), document, rule, ['parse-incomplete'], { matchTokens: [] }));
      }
    }
  });
  root.walkDecls((declaration) => {
    const name = declaration.prop.trim();
    if (name.startsWith('--')) state.entries.push(entry('custom-property', name, document, declaration));
    if (name.startsWith('$')) state.entries.push(entry('sass-variable', name, document, declaration));
    for (const match of declaration.value.matchAll(/var\((--[\w-]+)/g)) addReference(state.references, match[1], { ...entry('', '', document, declaration).source, kind: 'css-variable' });
    for (const match of declaration.value.matchAll(/\$[\w-]+/g)) addReference(state.references, match[0], { ...entry('', '', document, declaration).source, kind: 'sass-variable' });
    if (/^animation(?:-name)?$/.test(name)) {
      for (const match of declaration.value.matchAll(/(?<![-\w])([_a-zA-Z][\w-]*)/g)) {
        if (!/^(?:ease|linear|infinite|normal|none|forwards|backwards|both|alternate)$/.test(match[1])) addReference(state.references, `@keyframes ${match[1]}`, { ...entry('', '', document, declaration).source, kind: 'animation' });
      }
    }
  });
  root.walkAtRules((atRule) => inspectAtRule(atRule, document, state));
}
function inspectAtRule(atRule, document, state) {
  const name = atRule.name.toLowerCase();
  const first = atRule.params.match(/[%$]?[\w-]+/)?.[0] ?? atRule.params.trim();
  const location = { ...entry('', '', document, atRule).source, kind: `sass-${name}` };
  if (name === 'keyframes') state.entries.push(entry('keyframes', `@keyframes ${first}`, document, atRule));
  if (name === 'mixin' || name === 'function') state.entries.push(entry(`sass-${name}`, `@${name} ${first}`, document, atRule));
  if (name === 'include') {
    const mixin = atRule.params.match(/^\s*(?:[\w$-]+\.)?([\w-]+)/)?.[1] ?? first;
    addReference(state.references, `@mixin ${mixin}`, location);
  }
  if (name === 'extend') addReference(state.references, first, location);
  if (name === 'use' || name === 'forward' || name === 'import') {
    const specifier = atRule.params.match(/['"]([^'"]+)['"]/)?.[1] ?? atRule.params.split(/\s/)[0];
    const normalizedEntry = `@${name} ${specifier}`;
    const risks = specifier.startsWith('.') ? [] : ['framework-owned'];
    state.entries.push(entry('sass-dependency', normalizedEntry, document, atRule, risks));
    addReference(state.references, normalizedEntry, location);
    state.dependencies.push({ from: document.path, specifier });
  }
  if (['media', 'supports', 'container', 'page'].includes(name)) {
    const risk = name === 'media' ? (/print/i.test(atRule.params) ? 'print-rule' : 'responsive-rule') : name === 'page' ? 'print-rule' : 'conditional-rule';
    state.entries.push(entry(`${name}-rule`, `@${name} ${atRule.params}`.trim(), document, atRule, [risk]));
  }
}
function resolveDependency(from, specifier, stylePaths) {
  if (!specifier.startsWith('.')) return null;
  const base = normalize(join(dirname(from), specifier)).replaceAll('\\', '/');
  const candidates = [base, `${base}.scss`, `${base}.sass`, `${base}.css`];
  const slash = base.lastIndexOf('/');
  candidates.push(`${base.slice(0, slash + 1)}_${base.slice(slash + 1)}.scss`);
  return candidates.find((candidate) => stylePaths.has(candidate)) ?? null;
}
function finalize(state) {
  for (const item of state.entries) {
    const references = new Map();
    for (const token of item.matchTokens ?? [item.normalizedEntry]) {
      for (const reference of state.references.get(token) ?? []) references.set(reference.key, reference);
      for (const dynamic of state.dynamicReferences.filter((value) => token.startsWith(value.prefix))) {
        references.set(`${dynamic.reference.path}:${dynamic.reference.line}:${token}`, dynamic.reference);
        item.risks.push('dynamic-class');
      }
    }
    item.references = [...references.values()].map(({ key: _key, ...value }) => value).sort(compareLocation);
    item.risks = [...new Set(item.risks)].sort();
    if (item.kind === 'keyframes' && item.references.length === 0) item.risks.push('dynamic-animation');
    const uncertain = item.risks.some((risk) => STRONG_RISKS.has(risk)) || item.risks.includes('dynamic-animation');
    item.disposition = uncertain ? 'uncertain' : item.references.length > 0 ? 'used' : 'removable-candidate';
    item.allowlistReason = item.disposition === 'uncertain' ? item.risks.map(riskReason).join('; ') : null;
    delete item.matchTokens;
  }
  state.entries.sort((a, b) => compareLocation(a.source, b.source) || a.normalizedEntry.localeCompare(b.normalizedEntry));
}
function mergeRuntimeEvidence(entries, runtime, evidence) {
  const byEntry = new Map(
    evidence.map((item) => [`${item.kind}\0${item.normalizedEntry}`, item]),
  );
  for (const item of entries) {
    const observable = isRuntimeObservable(item.kind);
    const matched = byEntry.get(`${item.kind}\0${item.normalizedEntry}`);
    item.runtime = {
      status: observable ? matched?.status ?? 'not-observed' : 'not-observable',
      observedScenarios: matched?.observedScenarios ?? [],
      coveredScenarios: matched?.coveredScenarios ?? [],
    };
    if (item.references.length > 0 || !observable || hasStrongRisk(item)) continue;
    if (runtime.status === 'incomplete') {
      retainForRuntime(item, 'Runtime coverage catalog is incomplete.');
    } else if (matched?.status === 'covered') {
      retainForRuntime(item, 'Static and runtime evidence conflict; runtime use was observed.');
    } else if (matched?.status === 'uncovered') {
      item.disposition = 'removable-candidate';
      item.allowlistReason = null;
    } else {
      retainForRuntime(item, 'Selector was not observed in emitted runtime styles.');
    }
  }
}
function hasStrongRisk(item) {
  return item.risks.some((risk) => STRONG_RISKS.has(risk) || risk === 'dynamic-animation');
}
function retainForRuntime(item, reason) {
  item.disposition = 'uncertain';
  item.allowlistReason = [item.allowlistReason, reason].filter(Boolean).join('; ');
}
function runtimeSummary(runtime) {
  return {
    status: runtime.status,
    expectedScenarioCount:
      runtime.scenarios.length + runtime.missingScenarioIds.length + runtime.failedScenarioIds.length,
    successfulScenarioCount: runtime.scenarios.length,
    missingScenarioIds: runtime.missingScenarioIds,
    failedScenarioIds: runtime.failedScenarioIds,
    scenarios: runtime.scenarios.map(({ scenario, browser, status, sheets }) => ({
      scenario,
      browser,
      status,
      sheets,
    })),
  };
}
const compareLocation = (a, b) => a.path.localeCompare(b.path) || a.line - b.line || a.column - b.column;
const riskReason = (risk) => ({
  'conditional-rule': 'Conditional rule may activate outside static evidence.',
  'dynamic-animation': 'Animation names may be selected dynamically.',
  'dynamic-class': 'A dynamic class prefix matches this selector.',
  'dynamic-selector': 'Interpolated Sass selector cannot be proven unused.',
  'feature-gated': 'Feature-gated selectors may be inactive during static inspection.',
  'framework-owned': 'Framework or utility classes may be applied at runtime.',
  'parse-incomplete': 'The source could not be completely parsed.',
  'print-rule': 'Print-only behavior requires a print rendering context.',
  'pseudo-selector': 'Pseudo-state or generated content requires runtime interaction.',
  'responsive-rule': 'Responsive behavior requires another viewport.',
  'sass-nesting': 'Nested Sass selector expansion is not proven.',
  'structural-selector': 'Element or structural selector usage is not statically complete.',
}[risk] ?? `Retained because of ${risk}.`);
export async function analyzeStyleUsage(options = {}) {
  const root = resolve(options.rootDir ?? process.cwd());
  const files = await discoverFiles(root);
  const records = await Promise.all(files.map(async (absolute) => ({ absolute, path: toPath(root, absolute), content: await readFile(absolute, 'utf8') })));
  const styleRecords = records.filter((file) => STYLE_EXTENSIONS.test(file.path));
  const stylePaths = new Set(styleRecords.map((file) => file.path));
  const state = { entries: [], references: new Map(), dynamicReferences: [], diagnostics: [], dependencies: [], componentStyles: new Set(), templates: new Set(), inlineStyles: [], inlineTemplates: [] };
  for (const file of records.filter((item) => item.path.endsWith('.ts'))) inspectTypeScript(root, file.absolute, file.content, state);
  for (const file of records.filter((item) => CONFIGURATION.test(item.path) && item.path.endsWith('.json'))) inspectConfiguration(file, state);
  for (const file of records.filter((item) => item.path.endsWith('.html'))) state.templates.add(file.absolute);
  const templateDocuments = records.filter((item) => item.path.endsWith('.html')).map((item) => ({ ...item, kind: 'template', baseLine: 1, baseColumn: 1 }));
  for (const document of [...templateDocuments, ...state.inlineTemplates]) inspectTemplate(document, state);
  const styleDocuments = styleRecords.map((item) => ({ ...item, kind: 'style', baseLine: 1, baseColumn: 1 }));
  for (const document of [...styleDocuments, ...state.inlineStyles]) inspectStyle(document, state);
  finalize(state);
  let runtime;
  if (options.runtime) {
    const loadedRuntime = await loadRuntimeCoverage(options.runtime);
    mergeRuntimeEvidence(state.entries, loadedRuntime, mapRuntimeCoverage(loadedRuntime));
    runtime = runtimeSummary(loadedRuntime);
  }
  const angular = JSON.parse(records.find((item) => item.path === 'angular.json')?.content ?? '{}');
  const configured = Object.values(angular.projects ?? {}).flatMap((project) => project.architect?.build?.options?.styles ?? []).map((item) => typeof item === 'string' ? item : item.input).filter(Boolean);
  const active = configured.filter((path) => stylePaths.has(path)).sort();
  const component = [...state.componentStyles].map((path) => toPath(root, path)).filter((path) => stylePaths.has(path)).sort();
  const dependencyPaths = new Set();
  for (const dependency of state.dependencies) {
    dependencyPaths.add(dependency.from);
    const resolvedPath = resolveDependency(dependency.from, dependency.specifier, stylePaths);
    if (resolvedPath) dependencyPaths.add(resolvedPath);
  }
  const dispositions = Object.fromEntries(['used', 'removable-candidate', 'uncertain'].map((name) => [name, state.entries.filter((item) => item.disposition === name).length]));
  return {
    schemaVersion: 1,
    generatedBy: 'audit-unused-styles',
    inventory: {
      activeGlobalStyles: active,
      alternateGlobalStyles: styleRecords.map((file) => file.path).filter((path) => dirname(path) === 'src' && !active.includes(path)).sort(),
      componentStyles: component,
      inlineStyles: state.inlineStyles.map(({ content: _content, baseLine: line, baseColumn: column, ...item }) => ({ path: item.path, kind: item.kind, line, column })),
      sassDependencies: [...dependencyPaths].sort(),
      styleSources: styleRecords.map((file) => file.path),
      templates: templateDocuments.map((item) => item.path).sort(),
      inlineTemplates: state.inlineTemplates.map((item) => ({ path: item.path, line: item.baseLine, column: item.baseColumn })),
      typescriptFiles: records.filter((file) => file.path.endsWith('.ts')).map((file) => file.path).sort(),
      configurationFiles: records.filter((file) => CONFIGURATION.test(file.path)).map((file) => file.path).sort(),
    },
    diagnostics: state.diagnostics.sort((a, b) => a.path.localeCompare(b.path)),
    summary: { total: state.entries.length, dispositions },
    entries: state.entries,
    ...(runtime ? { runtime } : {}),
  };
}
const escapeHtml = (value) => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
export function renderStyleUsageHtml(report) {
  const counts = report.summary.dispositions;
  const runtime = renderRuntimeHtml(report.runtime);
  const rows = report.entries.map(renderEntryRow).join('');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Static first-party style usage</title><style>body{font:14px system-ui;margin:2rem}table{border-collapse:collapse;width:100%;margin-block:1rem}th,td{border:1px solid #ccc;padding:.4rem;text-align:left;vertical-align:top}code{white-space:pre-wrap}.incomplete{border:2px solid #a00;padding:.75rem}</style></head><body><h1>Static first-party style usage</h1><p>Used: ${counts.used}; removable candidates: ${counts['removable-candidate']}; uncertain: ${counts.uncertain}. Uncertainty always retains source.</p>${runtime}<h2>Per-entry evidence</h2><table><thead><tr><th>Disposition</th><th>Kind</th><th>Entry</th><th>Source</th><th>References</th><th>Runtime status</th><th>Observed scenarios</th><th>Covered scenarios</th><th>Risks</th><th>Allowlist reason</th></tr></thead><tbody>${rows}</tbody></table></body></html>`;
}
function renderEntryRow(item) {
  const runtime = item.runtime ?? {};
  return `<tr><td>${escapeHtml(item.disposition)}</td><td>${escapeHtml(item.kind)}</td><td><code>${escapeHtml(item.normalizedEntry)}</code></td><td>${escapeHtml(`${item.source.path}:${item.source.line}:${item.source.column}`)}</td><td>${item.references.map((ref) => escapeHtml(`${ref.path}:${ref.line}`)).join('<br>')}</td><td>${escapeHtml(runtime.status ?? '')}</td><td>${escapeHtml((runtime.observedScenarios ?? []).join(', '))}</td><td>${escapeHtml((runtime.coveredScenarios ?? []).join(', '))}</td><td>${escapeHtml(item.risks.join(', '))}</td><td>${escapeHtml(item.allowlistReason ?? '')}</td></tr>`;
}
function renderRuntimeHtml(runtime) {
  if (!runtime) return '';
  const scenarioRows = runtime.scenarios.map(({ scenario, browser, sheets }) => {
    const ranges = sheets.reduce((total, sheet) => total + sheet.ranges.length, 0);
    const dimensions = [
      scenario.viewport, scenario.locale, scenario.role ?? 'anonymous',
      scenario.featureFlags.join(',') || 'no flags', scenario.media,
      scenario.offline ? 'offline' : 'online', scenario.interaction ?? 'passive',
    ].join(' / ');
    return `<tr><td><code>${escapeHtml(scenario.id)}</code></td><td>${escapeHtml(scenario.url)}</td><td>${escapeHtml(scenario.state)}</td><td>${escapeHtml(dimensions)}</td><td>${escapeHtml(`${browser.name} ${browser.version}`)}</td><td>${ranges}</td></tr>`;
  }).join('');
  const failures = [...runtime.missingScenarioIds, ...runtime.failedScenarioIds].join(', ');
  const className = runtime.status === 'complete' ? '' : ' class="incomplete"';
  return `<section${className}><h2>Runtime CSS coverage: ${escapeHtml(runtime.status)}</h2><p>${runtime.successfulScenarioCount}/${runtime.expectedScenarioCount} scenarios successful.${failures ? ` Missing or failed: ${escapeHtml(failures)}.` : ''}</p><table><thead><tr><th>Scenario</th><th>Route</th><th>State</th><th>Dimensions</th><th>Browser</th><th>Covered ranges</th></tr></thead><tbody>${scenarioRows}</tbody></table></section>`;
}
export async function runStyleAudit(options = {}) {
  const rootDir = resolve(options.rootDir ?? process.cwd());
  const runtime = options.runtime ?? {
    coverageDir: join(rootDir, '.ui-audit', 'style-usage', 'runtime'),
    expectedScenarios: buildScenarioCatalog(ROUTE_MANIFEST),
  };
  const report = await analyzeStyleUsage({ rootDir, runtime });
  await writeRedactedArtifacts({
    outputDir: join(rootDir, '.ui-audit', 'style-usage'),
    name: 'report',
    html: renderStyleUsageHtml(report),
    result: report,
  });
  return report;
}
if (import.meta.main) {
  const report = await runStyleAudit();
  console.log(`Style usage evidence: ${report.summary.total} entries (${report.summary.dispositions.uncertain} uncertain).`);
  if (report.runtime.status !== 'complete') {
    console.error(
      `Runtime CSS coverage incomplete: ${report.runtime.missingScenarioIds.length} missing, `
      + `${report.runtime.failedScenarioIds.length} failed.`,
    );
    process.exitCode = 1;
  }
}
