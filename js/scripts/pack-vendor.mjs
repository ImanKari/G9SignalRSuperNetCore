// Packs this package (npm pack, after `npm run build`) and copies the tarball into the G9Hub web app's vendor folder:
//   <repo root>/G9Hub/web/vendor/g9-signalr-supernetcore-client-<version>.tgz
// The tarball is what the SPA installs with `npm install ./vendor/g9-signalr-supernetcore-client-<version>.tgz`.
import { execFileSync } from 'node:child_process';
import { copyFile, mkdir, readFile, rm } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(await readFile(join(root, 'package.json'), 'utf8'));
const vendorDir = resolve(root, '..', '..', 'G9Hub', 'web', 'vendor');

// --ignore-scripts: the build already ran (pack:vendor = build + this script), so prepack must not run it twice.
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
const output = execFileSync(npm, ['pack', '--json', '--ignore-scripts'], {
  cwd: root,
  encoding: 'utf8',
  shell: process.platform === 'win32',
});
const packed = JSON.parse(output);
const fileName = packed[0]?.filename;
if (!fileName) throw new Error(`npm pack did not report a file name: ${output}`);

await mkdir(vendorDir, { recursive: true });
const source = join(root, fileName);
const target = join(vendorDir, fileName);
await copyFile(source, target);
await rm(source, { force: true });

console.log(`${pkg.name}@${pkg.version} -> ${target}`);
