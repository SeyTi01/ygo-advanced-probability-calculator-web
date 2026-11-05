import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

export function sourceFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (['bin', 'obj', '.git'].includes(entry.name)) return [];
    const path = join(directory, entry.name);
    return entry.isDirectory() ? sourceFiles(path) : [path];
  });
}

// This is a deliberately conservative dependency tripwire. The isolated build
// and separate app compilation are the authoritative compiler/access checks.
export function inspectEngineSource(source, file) {
  const violations = [];
  const neutralVisibilityFile = 'YGOProbabilityCalculatorBlazor/Engine/EngineTestVisibility.cs';
  const neutralVisibility = /^\s*\[assembly:\s*InternalsVisibleTo\("CardProbability\.EngineTests"\)\]\s*$/m;
  if (file.replaceAll('\\', '/') === neutralVisibilityFile) {
    if (!neutralVisibility.test(source)) violations.push(`${file}: missing exact neutral test visibility declaration`);
    // Remove exactly one approved declaration. A second or different friend is
    // still rejected below, including app-specific and public-consumer friends.
    source = source.replace(neutralVisibility, '');
  }
  const forbidden = /\b(?:YGOProbabilityCalculatorBlazor|Microsoft\.AspNetCore|Microsoft\.JSInterop|System\.Runtime\.InteropServices\.JavaScript|JSInvokable|JSExport|JSImport|InternalsVisibleTo|DllImport|LibraryImport|GetManifestResourceStream|ResourceManager|Assembly\.Load|Type\.GetType|Activator\.CreateInstance)\b/;
  if (forbidden.test(source)) violations.push(`${file}: application, host, resource or privileged dependency`);
  for (const match of source.matchAll(/^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?([\w.]+)\s*;/gm)) {
    if (!/^(?:System|CardProbability)(?:\.|$)/.test(match[1])) violations.push(`${file}: undeclared using ${match[1]}`);
  }
  if (/\bSystem\.(?:IO|Net|Reflection)\b/.test(source)) violations.push(`${file}: host IO/reflection dependency`);
  if (!/^\s*namespace CardProbability(?:\.\w+)*\s*[;{]/m.test(source)) violations.push(`${file}: source is outside the owned namespace`);
  return violations;
}

export function inspectApplicationSource(source, file) {
  return /\bCardProbability\s*(?:\.|::)\s*Internal\b/.test(source)
    ? [`${file}: application code/test bypasses the public API`]
    : [];
}

export function checkBoundary(repository) {
  const app = join(repository, 'YGOProbabilityCalculatorBlazor');
  const engine = join(app, 'Engine');
  const files = sourceFiles(engine);
  const violations = [];
  for (const file of files) {
    if (!file.endsWith('.cs')) {
      violations.push(`${relative(repository, file)}: undeclared engine resource (only .cs files are owned)`);
      continue;
    }
    violations.push(...inspectEngineSource(readFileSync(file, 'utf8'), relative(repository, file)));
  }
  for (const file of sourceFiles(app)) {
    if (file.startsWith(engine + sep) || !/\.(?:cs|razor)$/.test(file)) continue;
    violations.push(...inspectApplicationSource(readFileSync(file, 'utf8'), relative(repository, file)));
  }
  for (const file of sourceFiles(join(repository, 'YGOProbabilityCalculatorBlazorTest'))) {
    if (!file.endsWith('.cs')) continue;
    violations.push(...inspectApplicationSource(readFileSync(file, 'utf8'), relative(repository, file)));
  }
  if (violations.length) throw new Error(violations.join('\n'));
  if (!files.length) throw new Error('The owned engine source set is empty.');
  return { engine, files };
}
