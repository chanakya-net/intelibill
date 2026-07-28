import { spawnSync } from 'node:child_process';
import process from 'node:process';

const args = process.argv.slice(2);
const setup = args.includes('--setup');
const baseline = args.includes('--baseline');
const forwardedArgs = args.filter(
  (argument) => !['--setup', '--baseline', '--mocked'].includes(argument),
);

if (args.includes('--update-snapshots') && !baseline) {
  console.error('Baseline writes require the explicit audit:ui:baseline command.');
  process.exit(1);
}

const command = setup
  ? ['x', 'playwright', 'install', 'chromium']
  : ['x', 'playwright', 'test', '--config', 'playwright.config.ts', ...forwardedArgs];
const result = spawnSync(process.execPath, command, {
  cwd: process.cwd(),
  stdio: 'inherit',
  env: {
    ...process.env,
    ...(baseline ? { UI_AUDIT_UPDATE_BASELINE: '1' } : {}),
  },
});

if (result.error) {
  console.error(result.error.message);
  process.exit(1);
}

process.exit(result.status ?? 1);
