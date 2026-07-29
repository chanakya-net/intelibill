import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';

const SENSITIVE_KEY = /password|token|secret|api.?key|authorization|cookie/i;
const SENSITIVE_TEXT =
  /(password|passwd|token|secret|api[_-]?key|authorization|cookie|set-cookie)\s*([:=])\s*(['"]?)([^'"\s<;,}]+)\3/gi;
const BEARER_TOKEN = /\bBearer\s+[A-Za-z0-9._~+/=-]+/gi;
const JWT = /\b[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b/g;

export function redactArtifact(value: unknown): unknown {
  if (typeof value === 'string') {
    return redactText(value);
  }

  if (Array.isArray(value)) {
    return value.map((item) => redactArtifact(item));
  }

  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([key, item]) => [
        key,
        SENSITIVE_KEY.test(key) ? '[REDACTED]' : redactArtifact(item),
      ]),
    );
  }

  return value;
}

export function redactText(value: string): string {
  return value
    .replace(SENSITIVE_TEXT, '$1$2[REDACTED]')
    .replace(BEARER_TOKEN, 'Bearer [REDACTED]')
    .replace(JWT, '[REDACTED]');
}

export async function writeRedactedArtifacts(options: {
  readonly outputDir: string;
  readonly name: string;
  readonly html: string;
  readonly result: unknown;
}): Promise<void> {
  const basePath = join(options.outputDir, options.name);
  await mkdir(dirname(basePath), { recursive: true });
  await Promise.all([
    writeFile(`${basePath}.html`, redactText(options.html), 'utf8'),
    writeFile(
      `${basePath}.json`,
      `${JSON.stringify(redactArtifact(options.result), null, 2)}\n`,
      'utf8',
    ),
  ]);
}
