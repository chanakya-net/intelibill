import { dirname, relative, resolve } from 'node:path';
import { parseTemplate } from '@angular/compiler';
import ts from 'typescript';

const toPath = (root, path) => relative(root, path).replaceAll('\\', '/');
const propertyName = (node) =>
  ts.isIdentifier(node) || ts.isStringLiteral(node) ? node.text : undefined;
const literals = (node) => {
  if (ts.isStringLiteralLike(node)) return [node];
  return ts.isArrayLiteralExpression(node)
    ? node.elements.filter((item) => ts.isStringLiteralLike(item))
    : [];
};

function sourceLocation(path, content, offset, baseLine = 1, baseColumn = 1) {
  const before = content.slice(0, Math.max(0, offset)).split('\n');
  return {
    path,
    line: baseLine + before.length - 1,
    column: (before.length === 1 ? baseColumn - 1 : 0) + before.at(-1).length + 1,
  };
}

export function addReference(referenceMap, token, reference) {
  if (!token) return;
  const references = referenceMap.get(token) ?? [];
  const key = `${reference.path}:${reference.line}:${reference.column}:${reference.kind}`;
  if (!references.some((item) => item.key === key)) references.push({ ...reference, key });
  referenceMap.set(token, references);
}

function addStringReferences(referenceMap, value, location, kind) {
  for (const match of value.matchAll(/--[_a-zA-Z][\w-]*|-?[_a-zA-Z][\w:-]*/g)) {
    addReference(referenceMap, match[0], {
      ...location,
      column: location.column + (match.index ?? 0),
      kind,
    });
  }
}

function addDynamicReferences(dynamicReferences, value, location) {
  for (const match of value.matchAll(/['"`]([_a-zA-Z][\w-]*-)['"`]\s*(?:\+|\$\{)/g)) {
    dynamicReferences.push({
      prefix: match[1],
      reference: { ...location, kind: 'dynamic-class' },
    });
  }
}

function literalDocument(root, source, node, kind) {
  const start = source.getLineAndCharacterOfPosition(node.getStart(source) + 1);
  return {
    path: toPath(root, source.fileName),
    content: node.text,
    kind,
    baseLine: start.line + 1,
    baseColumn: start.character + 1,
  };
}

function addTemplatePrefixReference(source, node, path, state) {
  const match = node.head.text.match(/[_a-zA-Z][\w-]*-$/);
  if (!match) return;
  const start = source.getLineAndCharacterOfPosition(
    node.getStart(source) + 1 + (match.index ?? 0),
  );
  state.dynamicReferences.push({
    prefix: match[0],
    reference: {
      path,
      line: start.line + 1,
      column: start.character + 1,
      kind: 'dynamic-class',
    },
  });
}

export function inspectTypeScript(root, absolute, content, state) {
  const path = toPath(root, absolute);
  const source = ts.createSourceFile(absolute, content, ts.ScriptTarget.Latest, true);
  const visit = (node) => {
    if (ts.isPropertyAssignment(node)) {
      const name = propertyName(node.name);
      const values = literals(node.initializer);
      if (name === 'styles') {
        state.inlineStyles.push(
          ...values.map((item) => literalDocument(root, source, item, 'inline-style')),
        );
        return;
      }
      if (name === 'template') {
        state.inlineTemplates.push(
          ...values.map((item) => literalDocument(root, source, item, 'inline-template')),
        );
        return;
      }
      if (name === 'styleUrl' || name === 'styleUrls') {
        for (const item of values) state.componentStyles.add(resolve(dirname(absolute), item.text));
        return;
      }
      if (name === 'templateUrl') {
        for (const item of values) state.templates.add(resolve(dirname(absolute), item.text));
        return;
      }
    }
    if (ts.isImportDeclaration(node)) return;
    if (ts.isTemplateExpression(node)) addTemplatePrefixReference(source, node, path, state);
    if (ts.isStringLiteralLike(node)) {
      const start = source.getLineAndCharacterOfPosition(node.getStart(source) + 1);
      const location = { path, line: start.line + 1, column: start.character + 1 };
      addStringReferences(state.references, node.text, location, 'typescript-string');
      addDynamicReferences(state.dynamicReferences, node.text, location);
    }
    if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.PlusToken) {
      const text = node.getText(source);
      const start = source.getLineAndCharacterOfPosition(node.getStart(source));
      addDynamicReferences(state.dynamicReferences, text, {
        path,
        line: start.line + 1,
        column: start.character + 1,
      });
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
}

export function inspectConfiguration(file, state) {
  const source = ts.parseJsonText(file.absolute, file.content);
  const visit = (node) => {
    if (ts.isStringLiteral(node)) {
      const start = source.getLineAndCharacterOfPosition(node.getStart(source) + 1);
      addStringReferences(
        state.references,
        node.text,
        {
          path: file.path,
          line: start.line + 1,
          column: start.character + 1,
        },
        'configuration-string',
      );
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
}

export function inspectTemplate(document, state) {
  const location = (offset) =>
    sourceLocation(
      document.path,
      document.content,
      offset,
      document.baseLine,
      document.baseColumn,
    );
  const parsed = parseTemplate(document.content, document.path, { preserveWhitespaces: true });
  for (const error of parsed.errors ?? []) {
    state.diagnostics.push({ path: document.path, message: String(error) });
  }
  const visit = (node) => {
    for (const attribute of node.attributes ?? []) {
      if (!['class', 'styleClass'].includes(attribute.name)) continue;
      addStringReferences(
        state.references,
        attribute.value,
        location(attribute.valueSpan?.start.offset ?? attribute.sourceSpan.start.offset),
        `angular-${attribute.name}`,
      );
    }
    for (const input of node.inputs ?? []) {
      const raw = input.sourceSpan.toString();
      const classMatch = raw.match(/^\[class\.([^\]]+)]/);
      if (classMatch) {
        addReference(state.references, classMatch[1], {
          ...location(input.sourceSpan.start.offset),
          kind: 'angular-class-binding',
        });
      }
      const propertyMatch = raw.match(/^\[style\.(--[^\]]+)]/);
      if (propertyMatch) {
        addReference(state.references, propertyMatch[1], {
          ...location(input.sourceSpan.start.offset),
          kind: 'angular-style-binding',
        });
      }
      if (['ngClass', 'styleClass', 'class'].includes(input.name)) {
        const value = input.valueSpan?.toString() ?? raw;
        addStringReferences(
          state.references,
          value,
          location(input.sourceSpan.start.offset),
          `angular-${input.name}`,
        );
        addDynamicReferences(
          state.dynamicReferences,
          value,
          location(input.sourceSpan.start.offset),
        );
      }
    }
    for (const child of node.children ?? []) visit(child);
    const blocks = [
      ...(node.branches ?? []),
      ...(node.cases ?? []),
      ...(node.groups ?? []),
      node.empty,
      node.placeholder,
      node.loading,
      node.error,
    ];
    for (const block of blocks) if (block) visit(block);
  };
  for (const node of parsed.nodes) visit(node);
}
