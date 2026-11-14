import { cpSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { checkBoundary } from './guards.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const repository = realpathSync(resolve(here, '../..'));
const options = new Set(process.argv.slice(2));
for (const option of options) {
  if (!['--guards-only', '--keep'].includes(option)) throw new Error(`Unknown option: ${option}`);
}
const dependency = checkBoundary(repository);
console.log(`Package/source guards passed (${dependency.id} ${dependency.version}).`);
if (options.has('--guards-only')) process.exit(0);

// A fresh cache and explicit public feed prove that no local project, package,
// linked source or application build properties supply the consumer's engine.
const temporary = realpathSync(mkdtempSync(join(tmpdir(), 'card-probability-package-')));
const insideRepository = relative(repository, temporary);
if (!insideRepository.startsWith('..' + sep) && !isAbsolute(insideRepository)) {
  throw new Error('The system TEMP directory must be outside the repository.');
}
try {
  const consumer = join(temporary, 'Consumer');
  mkdirSync(consumer);
  const project = join(consumer, 'Consumer.csproj');
  writeFileSync(project, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <Nullable>annotations</Nullable><ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="${dependency.id}" Version="${dependency.version}" /></ItemGroup>
</Project>`);
  cpSync(join(here, 'Consumer.cs'), join(consumer, 'Program.cs'));
  const config = join(temporary, 'NuGet.Config');
  writeFileSync(config, '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>');
  run('restore', project, '--configfile', config, '--no-cache', '--nologo');
  const assets = JSON.parse(readFileSync(join(consumer, 'obj/project.assets.json'), 'utf8'));
  if (Object.keys(assets.libraries).length !== 1 || assets.libraries[`${dependency.id}/${dependency.version}`]?.type !== 'package') {
    throw new Error('Consumer must resolve exactly the pinned engine package, with no project or extra package dependency.');
  }
  run('build', project, '--configuration', 'Release', '--no-restore', '--nologo');
  run('run', '--project', project, '--configuration', 'Release', '--no-build', '--no-restore');
  console.log('Published package public-consumer proof passed. Run normal solution tests for calculator integration.');
} finally {
  if (options.has('--keep')) console.log(`Proof files retained at ${temporary}`);
  else rmSync(temporary, { recursive: true, force: true });
}

function run(...args) {
  const buildOptions = ['restore', 'build'].includes(args[0])
    ? ['-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false']
    : [];
  console.log(`dotnet ${args[0]}: ${args.find(argument => argument.endsWith('.csproj'))}`);
  const result = spawnSync('dotnet', [...args, ...buildOptions], {
    cwd: temporary, stdio: 'inherit',
    env: { ...process.env, NUGET_PACKAGES: join(temporary, 'packages'), NUGET_HTTP_CACHE_PATH: join(temporary, 'http-cache') }
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`dotnet ${args[0]} failed with exit code ${result.status}`);
}
