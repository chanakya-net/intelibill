import { spawnSync } from 'node:child_process';
import process from 'node:process';

const args = process.argv.slice(2);
const runIndex = args.indexOf('--run');

let commandArgs = ['test'];
let command = 'ng';
// The whole suite, not one file or pattern: then the Bun script specs run too, whatever flags were
// forwarded (CI passes --watch=false --coverage).
let runsFullSuite = false;

if (runIndex !== -1) {
  const firstOptionAfterRun = args.findIndex((arg, index) => index > runIndex && arg.startsWith('-'));
  const endOfPaths = firstOptionAfterRun === -1 ? args.length : firstOptionAfterRun;
  const includePaths = args.slice(runIndex + 1, endOfPaths);
  if (includePaths.length === 0) {
    console.error('Expected a file path after --run.');
    process.exit(1);
  }

  const forwardedArgs = [...args.slice(0, runIndex), ...args.slice(endOfPaths)];
  if (includePaths.every((path) => path.startsWith('scripts/'))) {
    command = 'bun';
    commandArgs = ['test', ...includePaths, ...forwardedArgs];
  } else {
    commandArgs = [
      'test',
      ...includePaths.flatMap((path) => ['--include', normalizeIncludePath(path)]),
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
    runsFullSuite = true;
  }
}

function run(executable, executableArgs) {
  return (
    spawnSync(executable, executableArgs, {
      stdio: 'inherit',
      env: process.env,
    }).status ?? 1
  );
}

let exitCode = run(command, commandArgs);
if (exitCode === 0 && runsFullSuite) {
  exitCode = run('bun', ['test', 'scripts/tests']);
}
process.exit(exitCode);

function normalizeIncludePath(path) {
  if (path.startsWith('tests/')) return `../${path}`;
  return path.includes('/') || path.includes('*') ? path : `**/*${path}*.spec.ts`;
}
