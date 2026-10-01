// Maintainer step (needs the .NET 8 SDK): publishes the Windows app as ONE self-contained exe into dist/, so people
// who install from npm need nothing but Node, and adds the macOS app (KawaiiIsland-macos.zip) that CI built for this
// exact commit (needs the GitHub CLI, signed in). `--release` (npm pack / publish) fails without the Mac build;
// a plain `npm run build` (local Windows testing) skips it with a warning.
import { execFileSync } from 'node:child_process';
import { existsSync, rmSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const project = path.resolve(here, '../../../windows/src/KawaiiIsland/KawaiiIsland.csproj');
const dist = path.resolve(here, '../dist');

rmSync(dist, { recursive: true, force: true });
execFileSync('dotnet', [
  'publish', project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', dist,
  '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
  '-p:DebugType=none', '-p:GenerateDocumentationFile=false',
], { stdio: 'inherit' });

const exe = path.join(dist, 'KawaiiIsland.exe');
console.log(`dist/KawaiiIsland.exe: ${(statSync(exe).size / 1048576).toFixed(1)} MB`);

// macOS: the CI artifact for HEAD (the Mac app can only be compiled on a Mac).
const release = process.argv.includes('--release');
const gh = process.platform === 'win32' && existsSync('C:/Program Files/GitHub CLI/gh.exe') ? 'C:/Program Files/GitHub CLI/gh.exe' : 'gh';
try {
  const sha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  const runs = JSON.parse(execFileSync(gh, ['run', 'list', '--commit', sha, '--workflow', 'build', '--json', 'databaseId,conclusion'], { encoding: 'utf8' }));
  const ok = runs.find(r => r.conclusion === 'success');
  if (!ok) throw new Error(`no successful CI build for ${sha.slice(0, 7)} yet (push it and wait for CI)`);
  execFileSync(gh, ['run', 'download', String(ok.databaseId), '--name', 'KawaiiIsland-macos-zip', '--dir', dist], { stdio: 'inherit' });
  console.log(`dist/KawaiiIsland-macos.zip: ${(statSync(path.join(dist, 'KawaiiIsland-macos.zip')).size / 1048576).toFixed(1)} MB`);
} catch (e) {
  if (release) { console.error(`macOS build missing: ${e.message}`); process.exit(1); }
  console.warn(`(skipping the macOS app: ${e.message})`);
}
