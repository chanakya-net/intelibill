import { execFile, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process';
import { access } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createInterface } from 'node:readline';
import { promisify } from 'node:util';
import { test as base } from '@playwright/test';

const run = promisify(execFile);
const repo = resolve(process.cwd(), '../..');
const fixtureProject = resolve(repo, 'tests/release/Intelibill.Onboarding.Fixture');
const fixtureBin = resolve(fixtureProject, 'bin/Debug/net10.0');
const frontendServer = resolve(process.cwd(), 'dist/INVENTORY/server/server.mjs');

interface Snapshot {
  readonly sales: number;
  readonly saleDocuments: readonly { number: string; document: { version: number } }[] | null;
  readonly saleItems: number;
  readonly sequences: readonly { next_number: number }[] | null;
  readonly stockTransactions: number;
  readonly batches: readonly [string, number, number][] | null;
  readonly aggregate: readonly [string, number][] | null;
}

interface OnboardingFixture {
  readonly origin: string;
  mailbox(email: string): Promise<{ challengeId: string; token: string }>;
  snapshot(shopId: string): Promise<Snapshot>;
}

function launch(executable: string, args: string[], env = process.env, cwd = repo) {
  const child = spawn(executable, args, { cwd, env, stdio: 'pipe' });
  const output = createInterface({ input: child.stdout });
  const diagnostics: string[] = [];
  const stderr = createInterface({ input: child.stderr });
  const record = (line: string) => {
    if (line.startsWith('ONBOARDING ')) return;
    diagnostics.push(line);
    if (diagnostics.length > 30) diagnostics.shift();
  };
  output.on('line', record);
  stderr.on('line', record);

  const next = (pattern: RegExp, timeout: number) => new Promise<string>((resolveLine, reject) => {
    const finish = (error?: Error, value?: string) => {
      clearTimeout(timer);
      output.off('line', onLine);
      child.off('exit', onExit);
      child.off('error', onError);
      if (error) reject(error);
      else resolveLine(value!);
    };
    const onLine = (line: string) => {
      const match = pattern.exec(line);
      if (match?.[1]) finish(undefined, match[1]);
    };
    const failed = (reason: string) => new Error(`${executable} ${reason}\n${diagnostics.join('\n')}`);
    const onExit = () => finish(failed(`exited (${child.exitCode ?? child.signalCode})`));
    const onError = (error: Error) => finish(failed(error.message));
    const timer = setTimeout(() => finish(failed('did not answer in time')), timeout);
    output.on('line', onLine);
    child.once('exit', onExit);
    child.once('error', onError);
    if (child.exitCode !== null || child.signalCode !== null) onExit();
  });

  return { child, next };
}

async function stop(child: ChildProcessWithoutNullStreams, message?: string): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  await new Promise<void>((resolveExit) => {
    const timer = setTimeout(() => child.kill('SIGKILL'), 15_000);
    child.once('exit', () => {
      clearTimeout(timer);
      resolveExit();
    });
    if (message) child.stdin.end(message);
    else child.kill();
  });
}

async function withOnboardingFixture(use: (fixture: OnboardingFixture) => Promise<void>): Promise<void> {
  await access(frontendServer).catch(() => {
    throw new Error('The production bundle is missing. Run bun run build before audit:ui:onboarding.');
  });
  await run('docker', ['info', '--format', '{{.ServerVersion}}']).catch(() => {
    throw new Error('audit:ui:onboarding requires a running Docker engine for its isolated PostgreSQL database.');
  });
  await run('dotnet', ['build', fixtureProject, '--nologo'], { cwd: repo, timeout: 120_000 });

  const api = launch('dotnet', ['Intelibill.Onboarding.Fixture.dll'], process.env, fixtureBin);
  let web: ReturnType<typeof launch> | undefined;
  const reply = async <T>(request?: object): Promise<T> => {
    const answer = api.next(/^ONBOARDING (.+)$/, 60_000);
    if (request) api.child.stdin.write(`${JSON.stringify(request)}\n`);
    return JSON.parse(await answer) as T;
  };

  try {
    const { origin: apiOrigin } = await reply<{ origin: string }>();
    web = launch(process.execPath, [frontendServer], {
      ...process.env, API_ORIGIN: apiOrigin, PORT: '0', WEB_BIND_HOST: '127.0.0.1',
    });
    const port = await web.next(/^Web server listening on http:\/\/localhost:(\d+)$/, 10_000);
    await use({
      origin: `http://127.0.0.1:${port}`,
      mailbox: (email) => reply({ command: 'mailbox', email }),
      snapshot: (shopId) => reply({ command: 'snapshot', shopId }),
    });
  } finally {
    if (web) await stop(web.child);
    await stop(api.child, '{"command":"stop"}\n');
  }
}

export const test = base.extend<{}, { onboarding: OnboardingFixture }>({
  onboarding: [async ({}, use) => withOnboardingFixture(use), { scope: 'worker', timeout: 180_000 }],
});

export { expect, type Page } from '@playwright/test';
