import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

import postcss from 'postcss';
import selectorParser from 'postcss-selector-parser';

const OBSERVABLE_KINDS = new Set([
  'selector',
  'custom-property',
  'media-rule',
  'supports-rule',
  'container-rule',
  'page-rule',
]);

export function isRuntimeObservable(kind) {
  return OBSERVABLE_KINDS.has(kind);
}

export async function loadRuntimeCoverage({ coverageDir, expectedScenarios }) {
  const result = emptyRuntimeResult();
  for (const expected of expectedScenarios) {
    await loadScenario(coverageDir, typeof expected === 'string' ? expected : expected.id, result);
  }
  result.status =
    result.missingScenarioIds.length === 0 && result.failedScenarioIds.length === 0
      ? 'complete'
      : 'incomplete';
  return result;
}

function emptyRuntimeResult() {
  return {
    status: 'incomplete',
    scenarios: [],
    missingScenarioIds: [],
    failedScenarioIds: [],
    stylesheetTexts: new Map(),
  };
}

async function loadScenario(coverageDir, id, result) {
  let artifact;
  try {
    artifact = JSON.parse(await readFile(join(coverageDir, `${id}.json`), 'utf8'));
  } catch (error) {
    (error?.code === 'ENOENT' ? result.missingScenarioIds : result.failedScenarioIds).push(id);
    return;
  }
  if (!isSuccessfulArtifact(artifact, id)) {
    result.failedScenarioIds.push(id);
    return;
  }
  try {
    for (const sheet of artifact.sheets) {
      if (!result.stylesheetTexts.has(sheet.hash)) {
        const path = join(coverageDir, 'stylesheets', `${sheet.hash}.css`);
        result.stylesheetTexts.set(sheet.hash, await readFile(path, 'utf8'));
      }
    }
    result.scenarios.push(artifact);
  } catch {
    result.failedScenarioIds.push(id);
  }
}

function isSuccessfulArtifact(artifact, expectedId) {
  return artifact?.schemaVersion === 1
    && artifact?.scenario?.id === expectedId
    && artifact?.browser?.name === 'chromium'
    && artifact?.status === 'success'
    && Array.isArray(artifact?.sheets)
    && artifact.sheets.every(validSheet);
}

function validSheet(sheet) {
  return /^[a-f0-9]{64}$/.test(sheet?.hash)
    && Array.isArray(sheet?.ranges)
    && sheet.ranges.every((range) =>
      Number.isInteger(range?.start)
      && Number.isInteger(range?.end)
      && range.start >= 0
      && range.end >= range.start);
}

export function mapRuntimeCoverage(runtime) {
  const evidence = new Map();
  for (const artifact of runtime.scenarios) {
    for (const sheet of artifact.sheets) {
      const text = runtime.stylesheetTexts.get(sheet.hash);
      if (text !== undefined) inspectRuntimeSheet(text, sheet.ranges, artifact.scenario.id, evidence);
    }
  }
  return [...evidence.values()].map(toRuntimeEntry).sort(compareRuntimeEntry);
}

function toRuntimeEntry(item) {
  return {
    kind: item.kind,
    normalizedEntry: item.normalizedEntry,
    status: item.covered.size > 0 ? 'covered' : 'uncovered',
    observedScenarios: [...item.observed].sort(),
    coveredScenarios: [...item.covered].sort(),
  };
}

function compareRuntimeEntry(left, right) {
  return left.kind.localeCompare(right.kind)
    || left.normalizedEntry.localeCompare(right.normalizedEntry);
}

function inspectRuntimeSheet(text, ranges, scenarioId, evidence) {
  let root;
  try {
    root = postcss.parse(text);
  } catch {
    return;
  }
  const lineStarts = sourceLineStarts(text);
  root.walkAtRules((atRule) => inspectRuntimeAtRule(atRule, ranges, lineStarts, scenarioId, evidence));
  root.walkRules((rule) => inspectRuntimeRule(rule, ranges, lineStarts, scenarioId, evidence));
}

function inspectRuntimeAtRule(atRule, ranges, lineStarts, scenarioId, evidence) {
  if (!['media', 'supports', 'container', 'page'].includes(atRule.name)) return;
  touchEvidence(
    evidence,
    `${atRule.name}-rule`,
    atRuleName(atRule),
    scenarioId,
    intersectsRanges(sourceOffsets(atRule, lineStarts), ranges),
  );
}

function inspectRuntimeRule(rule, ranges, lineStarts, scenarioId, evidence) {
  if (hasKeyframesParent(rule)) return;
  const covered = intersectsRanges(sourceOffsets(rule, lineStarts), ranges);
  for (const arm of rule.selectors ?? [rule.selector]) {
    inspectRuntimeSelector(arm, scenarioId, covered, evidence);
  }
  rule.walkDecls((declaration) => {
    if (declaration.prop.trim().startsWith('--')) {
      touchEvidence(evidence, 'custom-property', declaration.prop.trim(), scenarioId, covered);
    }
  });
  for (let parent = rule.parent; parent; parent = parent.parent) {
    if (parent.type === 'atrule' && OBSERVABLE_KINDS.has(`${parent.name}-rule`)) {
      touchEvidence(evidence, `${parent.name}-rule`, atRuleName(parent), scenarioId, covered);
    }
  }
}

function inspectRuntimeSelector(selector, scenarioId, covered, evidence) {
  try {
    const withoutEncapsulation = selector.replaceAll(/\[_ng(?:content|host)-[^\]]+\]/g, '');
    const normalized = selectorParser().processSync(withoutEncapsulation, { lossless: false });
    touchEvidence(evidence, 'selector', normalized, scenarioId, covered);
  } catch {
    // Generated selectors that cannot be normalized remain conservatively unobserved.
  }
}

function touchEvidence(evidence, kind, normalizedEntry, scenarioId, covered) {
  const key = `${kind}\0${normalizedEntry}`;
  const item = evidence.get(key) ?? {
    kind,
    normalizedEntry,
    observed: new Set(),
    covered: new Set(),
  };
  item.observed.add(scenarioId);
  if (covered) item.covered.add(scenarioId);
  evidence.set(key, item);
}

function sourceLineStarts(text) {
  const starts = [0];
  for (let index = 0; index < text.length; index += 1) {
    if (text[index] === '\n') starts.push(index + 1);
  }
  return starts;
}

function sourceOffsets(node, lineStarts) {
  const start = node.source?.start ?? { line: 1, column: 1 };
  const end = node.source?.end ?? start;
  return {
    start: (lineStarts[start.line - 1] ?? 0) + start.column - 1,
    end: (lineStarts[end.line - 1] ?? 0) + end.column,
  };
}

function intersectsRanges(node, ranges) {
  return ranges.some((range) => range.start < node.end && range.end > node.start);
}

const atRuleName = (atRule) => `@${atRule.name} ${atRule.params}`.trim();

function hasKeyframesParent(node) {
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (parent.type === 'atrule' && parent.name === 'keyframes') return true;
  }
  return false;
}
