import assert from 'node:assert/strict';
import test from 'node:test';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkBoundary, enginePackage, inspectApplicationSource, inspectProject } from './guards.mjs';

test('application allows the public API but rejects internal access and local namespace shims', () => {
  assert.deepEqual(inspectApplicationSource('using CardProbability; CalculationEngine.Calculate(request);', 'app.cs'), []);
  for (const code of [
    'using CardProbability.Internal;',
    'using C = global::CardProbability.Internal.Contracts;',
    'CardProbability.Internal.CalculationRuntime runtime;',
    'namespace CardProbability; public class CalculationEngine {}',
    'namespace CardProbability.Internal { }'
  ]) assert.ok(inspectApplicationSource(code, 'app.cs').length, code);
});

test('dependency requires one fixed package version', () => {
  const reference = version => '<PackageReference Include="CardProbability.Engine" Version="' + version + '" />';
  assert.equal(enginePackage(reference('1.0.0-preview.1')).version, '1.0.0-preview.1');
  for (const version of ['*', '1.*', '[1.0.0,2.0.0)', '$(EngineVersion)', '']) {
    assert.throws(() => enginePackage(reference(version)), /pinned/);
  }
  for (const xml of ['', reference('1.0.0') + reference('2.0.0'), '<!-- ' + reference('1.0.0') + ' -->']) {
    assert.throws(() => enginePackage(xml), /exactly one/);
  }
  assert.throws(() => enginePackage('<PackageReference Include="CardProbability.Engine" Version="1.0.0" Condition="false" />'), /unconditional/);
});

test('project wiring rejects engine project/binary references and linked sources', () => {
  for (const xml of [
    '<ProjectReference Include="../CardProbability/Engine.csproj" />',
    '<Reference Include="CardProbability"><HintPath>engine.dll</HintPath></Reference>',
    '<Compile Include="../shared/CalculationEngine.cs" />',
    '<Compile Include="Engine/**/*.cs" />',
    '<Compile Include="D:\\library\\source.cs" />'
  ]) assert.ok(inspectProject(xml, 'app.csproj').length, xml);
  assert.deepEqual(inspectProject('<ProjectReference Include="../YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj" />', 'tests.csproj'), []);
});

test('calculator checkout uses only the package-owned engine boundary', () => {
  const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
  assert.equal(checkBoundary(repository).id, 'CardProbability.Engine');
});
