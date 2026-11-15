import { expect, test } from 'bun:test';
import { existsSync, readFileSync, realpathSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, sep } from 'node:path';
import ts from 'typescript';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const members = [
  'src/frontend',
  'src/admin-frontend',
  'src/shared/frontend/ui',
  'src/shared/frontend/api',
];
const manifest = (directory: string) => join(root, directory, 'package.json');

test('both web apps and their shared packages resolve one installed Angular core', () => {
  expect(existsSync(manifest(''))).toBe(true);
  expect(existsSync(join(root, 'bun.lock'))).toBe(true);
  expect(existsSync(join(root, 'src/frontend/bun.lock'))).toBe(false);

  const packages = members.map((member) =>
    realpathSync(createRequire(manifest(member)).resolve('@angular/core/package.json')),
  );
  expect(new Set(packages).size).toBe(1);
  expect(JSON.parse(readFileSync(packages[0], 'utf8')).version).toBe('21.2.8');
});

test('admin and shared code have no dependencies on merchant applications or POS libraries', async () => {
  const merchantApp = join(root, 'src/frontend') + sep;
  const forbidden =
    /^(?:inventory(?:\/|$)|@angular\/service-worker(?:\/|$)|@ngrx\/|@microsoft\/signalr|@zxing\/)/;
  const compilerOptions: ts.CompilerOptions = { moduleResolution: ts.ModuleResolutionKind.Bundler };

  for (const member of members.slice(1)) {
    const directory = join(root, member);
    const app = JSON.parse(readFileSync(manifest(member), 'utf8'));
    const dependencies = Object.keys({
      ...app.dependencies,
      ...app.devDependencies,
      ...app.peerDependencies,
    });
    expect(dependencies.filter((name) => forbidden.test(name))).toEqual([]);

    for await (const file of new Bun.Glob('src/**/*.ts').scan({ cwd: directory, absolute: true })) {
      const { importedFiles } = ts.preProcessFile(readFileSync(file, 'utf8'), true);
      for (const { fileName } of importedFiles) {
        expect(fileName).not.toMatch(forbidden);
        const path = ts.resolveModuleName(fileName, file, compilerOptions, ts.sys).resolvedModule
          ?.resolvedFileName;
        if (path) {
          expect(path.startsWith(merchantApp), file).toBe(false);
          if (member.startsWith('src/shared/')) {
            expect(path.startsWith(join(root, 'src/admin-frontend') + sep), file).toBe(false);
          }
        }
      }
    }
  }

  const adminConfig = readFileSync(join(dirname(manifest(members[1])), 'angular.json'), 'utf8');
  expect(adminConfig).not.toContain('"serviceWorker"');
});

test('shared peer ranges are satisfied by the versions the apps use', () => {
  for (const member of members.slice(2)) {
    const { peerDependencies } = JSON.parse(readFileSync(manifest(member), 'utf8'));
    for (const [name, range] of Object.entries<string>(peerDependencies)) {
      const resolve = (directory: string) =>
        createRequire(manifest(directory)).resolve(`${name}/package.json`);
      const installed = realpathSync(resolve(member));
      const { version } = JSON.parse(readFileSync(installed, 'utf8'));
      expect(Bun.semver.satisfies(version, range), name).toBe(true);
      expect(members.slice(0, 2).map((app) => realpathSync(resolve(app)))).toEqual([
        installed,
        installed,
      ]);
    }
  }
});
