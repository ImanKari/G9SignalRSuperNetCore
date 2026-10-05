// One version for the whole repository: G9PackageVersion in G9SignalRSuperNetCore/Directory.Build.props (the NuGet
// packages) must equal every npm package's version, their VERSION constants, the native module versions and the
// cross-package dependency pins. A release that bumps one side and forgets the other fails here.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const js = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(js, '..');
const props = readFileSync(join(repo, 'G9SignalRSuperNetCore', 'Directory.Build.props'), 'utf8');
const expected = /<G9PackageVersion>([^<]+)<\/G9PackageVersion>/.exec(props)?.[1]?.trim();
if (!expected) throw new Error('G9PackageVersion not found in Directory.Build.props');

const problems = [];
const check = (where, actual) => {
  if (actual !== expected) problems.push(`${where}: ${actual ?? '(missing)'} (expected ${expected})`);
};
const read = (path) => readFileSync(join(js, path), 'utf8');
const json = (path) => JSON.parse(read(path));

const client = json('packages/client/package.json');
const lynx = json('packages/lynx/package.json');
check('packages/client/package.json version', client.version);
check('packages/lynx/package.json version', lynx.version);
check('packages/lynx peerDependencies @g9tm/signalr-supernetcore-client', lynx.peerDependencies?.['@g9tm/signalr-supernetcore-client']);
check('packages/lynx devDependencies @g9tm/signalr-supernetcore-client', lynx.devDependencies?.['@g9tm/signalr-supernetcore-client']);
check('packages/client/src/index.ts VERSION', /export const VERSION = '([^']+)'/.exec(read('packages/client/src/index.ts'))?.[1]);
check('packages/lynx/src/index.ts VERSION', /export const VERSION = '([^']+)'/.exec(read('packages/lynx/src/index.ts'))?.[1]);
check('packages/lynx/src/lynxtron/index.ts capabilities version', /version: '([^']+)'/.exec(read('packages/lynx/src/lynxtron/index.ts'))?.[1]);
check(
  'packages/lynx/android/.../G9SignalRLynxModule.kt VERSION',
  /const val VERSION = "([^"]+)"/.exec(read('packages/lynx/android/src/main/java/com/g9tm/signalrlynx/G9SignalRLynxModule.kt'))?.[1],
);
check('packages/lynx/ios/src/G9SignalRLynxModule.m version', /G9SignalRLynxVersion = @"([^"]+)"/.exec(read('packages/lynx/ios/src/G9SignalRLynxModule.m'))?.[1]);
check('packages/lynx/ios/g9tm-signalr-lynx.podspec version', /s\.version = '([^']+)'/.exec(read('packages/lynx/ios/g9tm-signalr-lynx.podspec'))?.[1]);

if (problems.length > 0) {
  console.error(`Version drift (Directory.Build.props says ${expected}):\n  ${problems.join('\n  ')}`);
  process.exit(1);
}
console.log(`versions: everything is ${expected}`);
