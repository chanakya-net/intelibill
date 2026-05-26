import { spawnSync } from 'node:child_process';
import process from 'node:process';

const args = process.argv.slice(2);
const runIndex = args.indexOf('--run');

let commandArgs = ['test'];

if (runIndex !== -1) {
  const includePath = args[runIndex + 1];
  if (!includePath || includePath.startsWith('--')) {
    console.error('Expected a file path after --run.');
    process.exit(1);
  }

  const forwardedArgs = args.filter((_, index) => index !== runIndex && index !== runIndex + 1);
  commandArgs = ['test', '--include', includePath, '--watch=false', ...forwardedArgs];
} else {
  commandArgs = ['test', ...args];
}

const result = spawnSync('ng', commandArgs, {
  stdio: 'inherit',
  env: process.env,
});

process.exit(result.status ?? 1);
