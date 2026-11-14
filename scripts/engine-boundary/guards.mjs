import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';

export function sourceFiles(directory) {
  if (!existsSync(directory)) return [];
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (['bin', 'obj', 'TestResults', '.git'].includes(entry.name)) return [];
    const path = join(directory, entry.name);
    return entry.isDirectory() ? sourceFiles(path) : [path];
  });
}

// Conservative tripwires; normal compilation against the package establishes
// accessibility. Engine implementation and white-box tests belong upstream.
export function inspectApplicationSource(source, file) {
  const violations = [];
  if (/\bCardProbability\s*(?:\.|::)\s*Internal\b/.test(source)) {
    violations.push(file + ': application code/test bypasses the public API');
  }
  if (/\bnamespace\s+CardProbability\b/.test(source)) {
    violations.push(file + ': local engine implementation or namespace shim');
  }
  return violations;
}

export function enginePackage(project) {
  const xml = project.replace(/<!--[\s\S]*?-->/g, '');
  const references = [...xml.matchAll(/<PackageReference\b[^>]*\bInclude\s*=\s*["']CardProbability\.Engine["'][^>]*>/g)];
  if (references.length !== 1) throw new Error('Expected exactly one CardProbability.Engine PackageReference.');
  const version = references[0][0].match(/\bVersion\s*=\s*["']([^"']+)["']/)?.[1];
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version ?? '') || /\bCondition\s*=/.test(references[0][0])) {
    throw new Error('CardProbability.Engine must use an unconditional literal pinned package version.');
  }
  return { id: 'CardProbability.Engine', version };
}

export function inspectProject(project, file) {
  const violations = [];
  const xml = project.replace(/<!--[\s\S]*?-->/g, '');
  for (const item of xml.matchAll(/<(Compile|ProjectReference|Reference)\b[^>]*\bInclude\s*=\s*["']([^"']+)["'][^>]*>/g)) {
    const [, kind, path] = item;
    if (/CardProbability|(?:^|[\\/])Engine(?:[\\/]|\.)/i.test(path)
        || (kind === 'Compile' && /(?:^|[\\/])\.\.(?:[\\/])|^[\\/]|^[A-Za-z]:/.test(path))) {
      violations.push(file + ': local or linked engine dependency (' + kind + ': ' + path + ')');
    }
  }
  return violations;
}

export function checkBoundary(repository) {
  const app = join(repository, 'YGOProbabilityCalculatorBlazor');
  const appTests = join(repository, 'YGOProbabilityCalculatorBlazorTest');
  const project = readFileSync(join(app, 'YGOProbabilityCalculatorBlazor.csproj'), 'utf8');
  const dependency = enginePackage(project);
  const violations = [];
  for (const file of [...sourceFiles(app), ...sourceFiles(appTests)]) {
    const name = relative(repository, file);
    if (/\.(?:cs|razor)$/.test(file)) violations.push(...inspectApplicationSource(readFileSync(file, 'utf8'), name));
    if (file.endsWith('.csproj')) violations.push(...inspectProject(readFileSync(file, 'utf8'), name));
  }
  if (sourceFiles(join(app, 'Engine')).length) violations.push('Embedded Engine source must be removed.');
  if (sourceFiles(join(repository, 'CardProbability.EngineTests')).length
      || readFileSync(join(repository, 'YGOProbabilityCalculatorBlazor.sln'), 'utf8').includes('CardProbability.EngineTests')) {
    violations.push('Neutral engine tests belong in the library repository.');
  }
  if (violations.length) throw new Error(violations.join('\n'));
  return dependency;
}
