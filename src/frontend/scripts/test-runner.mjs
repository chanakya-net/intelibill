import { spawnSync } from 'node:child_process';
import process from 'node:process';

const args = process.argv.slice(2);
const runIndex = args.indexOf('--run');

let commandArgs = ['test'];
let command = 'ng';

if (runIndex !== -1) {
  const includePath = args[runIndex + 1];
  if (!includePath || includePath.startsWith('--')) {
    console.error('Expected a file path after --run.');
    process.exit(1);
  }

  const forwardedArgs = args.filter((_, index) => index !== runIndex && index !== runIndex + 1);
  if (includePath.startsWith('scripts/')) {
    command = 'bun';
    commandArgs = ['test', includePath, ...forwardedArgs];
  } else {
    commandArgs = [
      'test',
      '--include',
      normalizeIncludePath(includePath),
      '--watch=false',
      ...forwardedArgs,
    ];
  }
} else {
  const firstArg = args[0];
  const looksLikeIncludePattern =
    args.length === 1 &&
    typeof firstArg === 'string' &&
    firstArg.length > 0 &&
    !firstArg.startsWith('-') &&
    firstArg !== 'INVENTORY';

  if (looksLikeIncludePattern) {
    const includePath =
      firstArg.includes('/') || firstArg.includes('*') ? firstArg : `**/*${firstArg}*.spec.ts`;
    commandArgs = ['test', '--include', includePath, '--watch=false'];
  } else {
    commandArgs = [
      'test',
      '--include',
      '**/*.spec.ts',
      '--include',
      '../tests/ui-audit/route-manifest.spec.ts',
      ...args,
    ];
  }
}

function run(executable, executableArgs) {
  return spawnSync(executable, executableArgs, {
    stdio: 'inherit',
    env: process.env,
  }).status ?? 1;
}

let exitCode = run(command, commandArgs);
if (exitCode === 0 && args.length === 0) {
  exitCode = run('bun', ['test', 'scripts/tests']);
}
process.exit(exitCode);

function normalizeIncludePath(path) {
  return path.startsWith('tests/') ? `../${path}` : path;
}
