// Maintainer step (needs the .NET 8 SDK): publishes the app as ONE self-contained exe into dist/, so people who
// install from npm need nothing but Node. Runs automatically before `npm pack` / `npm publish`.
import { execFileSync } from 'node:child_process';
import { rmSync, statSync } from 'node:fs';
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
