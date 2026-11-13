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
const { engine, files } = checkBoundary(repository);
console.log(`Source/dependency guards passed (${files.length} owned engine sources).`);
if (options.has('--guards-only')) process.exit(0);

// This proof is intentionally outside the checkout: no application build props,
// source globbing, resources or project references can leak in. The sole friend
// is the neutral engine test assembly, never this consumer or the app tests.
const temporary = realpathSync(mkdtempSync(join(tmpdir(), 'card-probability-boundary-')));
const insideRepository = relative(repository, temporary);
if (!insideRepository.startsWith('..' + sep) && !isAbsolute(insideRepository)) {
  throw new Error('The system TEMP directory must be outside the repository.');
}
const isolatedEngine = join(temporary, 'Engine');
const consumer = join(temporary, 'Consumer');
const isolatedApp = join(temporary, 'YGOProbabilityCalculatorBlazor');
const neutralTests = join(temporary, 'CardProbability.EngineTests');
const appTests = join(temporary, 'YGOProbabilityCalculatorBlazorTest');
try {
  mkdirSync(isolatedEngine);
  mkdirSync(consumer);
  for (const file of files) {
    const destination = join(isolatedEngine, 'Source', relative(engine, file));
    mkdirSync(dirname(destination), { recursive: true });
    cpSync(file, destination);
  }
  cpSync(join(repository, 'LICENSE'), join(isolatedEngine, 'LICENSE'));
  writeFileSync(join(isolatedEngine, 'Engine.csproj'), project('Library',
    '<AssemblyName>CardProbability.ClosureProof</AssemblyName><GenerateDocumentationFile>true</GenerateDocumentationFile>'
    + '<WarningsAsErrors>$(WarningsAsErrors);CS1591;CS1570;CS1572;CS1573;CS1574;CS1587</WarningsAsErrors>'));
  writeFileSync(join(consumer, 'Consumer.csproj'), project('Exe', '', '<ProjectReference Include="../Engine/Engine.csproj" />'));
  cpSync(join(here, 'Consumer.cs'), join(consumer, 'Program.cs'));
  // SDK reference packs supply all dependencies: no package feed is available.
  for (const directory of [isolatedEngine, consumer]) {
    writeFileSync(join(directory, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>');
  }
  run('build', join(consumer, 'Consumer.csproj'), '--configuration', 'Release', '--nologo');
  run('run', '--project', join(consumer, 'Consumer.csproj'), '--configuration', 'Release', '--no-build', '--no-restore');

  // Prove the complete neutral test source set moves with the engine: no app
  // source exists in this TEMP tree yet. Its one project reference changes to
  // the isolated engine; fixtures, test dependencies and tests stay unchanged.
  copyProject(join(repository, 'CardProbability.EngineTests'), neutralTests);
  const neutralTestProject = join(neutralTests, 'CardProbability.EngineTests.csproj');
  const neutralTestXml = readFileSync(neutralTestProject, 'utf8');
  const appReference = /<ProjectReference\s+Include="\.\.[\\/]YGOProbabilityCalculatorBlazor[\\/]YGOProbabilityCalculatorBlazor\.csproj"\s*\/>/g;
  if ([...neutralTestXml.matchAll(appReference)].length !== 1) throw new Error('Expected exactly one neutral-test app project reference to substitute.');
  writeFileSync(neutralTestProject, neutralTestXml.replace(appReference, '<ProjectReference Include="../Engine/Engine.csproj" />'));
  run('build', neutralTestProject, '--configuration', 'Release', '--nologo');
  run('test', neutralTestProject, '--configuration', 'Release', '--no-build', '--no-restore', '--nologo');

  // Compile the actual production app after removing the owned source set and
  // adding a normal reference to the isolated engine. This makes accidental
  // same-assembly internal access a compiler error, including aliases missed by
  // the small textual guard. It is a temporary substitution, never production
  // extraction or a second implementation.
  const app = join(repository, 'YGOProbabilityCalculatorBlazor');
  copyProject(app, isolatedApp, ['Engine']);
  const appProject = join(isolatedApp, 'YGOProbabilityCalculatorBlazor.csproj');
  const original = readFileSync(appProject, 'utf8');
  writeFileSync(appProject, original.replace('</Project>', '<ItemGroup><Compile Remove="Engine/**/*.cs" /><ProjectReference Include="../Engine/Engine.csproj" /></ItemGroup></Project>'));
  // Preserve original sibling names so existing application-test resource and
  // fixture links resolve without adding linked app source or internal friends.
  // Building the complete app test project also builds the substituted app.
  copyProject(join(repository, 'YGOProbabilityCalculatorBlazorTest'), appTests);
  run('build', join(appTests, 'YGOProbabilityCalculatorBlazorTest.csproj'), '--configuration', 'Debug', '--nologo');
  console.log('Independent engine, public consumer, neutral test suite and complete app/test dependency substitution passed.');
} finally {
  if (options.has('--keep')) console.log(`Proof files retained at ${temporary}`);
  else rmSync(temporary, { recursive: true, force: true });
}

function project(outputType, properties = '', references = '') {
  return `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>14</LangVersion><Nullable>annotations</Nullable><ImplicitUsings>enable</ImplicitUsings><OutputType>${outputType}</OutputType>${properties}</PropertyGroup><ItemGroup>${references}</ItemGroup></Project>`;
}

function copyProject(sourceDirectory, destinationDirectory, exclusions = []) {
  cpSync(sourceDirectory, destinationDirectory, {
    recursive: true,
    filter: source => !['bin', 'obj', 'TestResults', ...exclusions].includes(relative(sourceDirectory, source).split(sep)[0])
  });
}

function run(...args) {
  const buildOptions = args[0] === 'build'
    ? ['-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false']
    : [];
  console.log(`dotnet ${args[0]}: ${args.find(argument => argument.endsWith('.csproj'))}`);
  const result = spawnSync('dotnet', [...args, ...buildOptions], { cwd: temporary, stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`dotnet ${args[0]} failed with exit code ${result.status}`);
}
