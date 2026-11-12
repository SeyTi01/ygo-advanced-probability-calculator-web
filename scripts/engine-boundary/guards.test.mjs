import assert from 'node:assert/strict';
import test from 'node:test';
import { inspectApplicationSource, inspectEngineSource } from './guards.mjs';

test('engine rejects application namespaces, resources, host dependencies and friend access', () => {
  for (const dependency of [
    'using YGOProbabilityCalculatorBlazor.Models;',
    'using Microsoft.JSInterop;',
    'using OtherLibrary;',
    'using System.IO;',
    '[assembly: InternalsVisibleTo("Consumer")]',
    'Assembly.Load("App");',
    'assembly.GetManifestResourceStream("App.asset");'
  ]) {
    assert.ok(inspectEngineSource(`namespace CardProbability;\n${dependency}`, 'engine.cs').length, dependency);
  }
});

test('engine permits BCL and owned aliases; app rejects direct and aliased internal access', () => {
  assert.deepEqual(inspectEngineSource('using System.Numerics;\nusing C = CardProbability.Internal.Contracts;\nnamespace CardProbability;', 'engine.cs'), []);
  assert.deepEqual(inspectApplicationSource('using CardProbability; CalculationEngine.Calculate(request);', 'app.cs'), []);
  for (const code of ['using CardProbability.Internal;', 'using C = global::CardProbability.Internal.Contracts;', 'CardProbability.Internal.CalculationRuntime runtime;']) {
    assert.equal(inspectApplicationSource(code, 'app.cs').length, 1);
  }
});

test('only the neutral engine test assembly has friend access, in its one owned declaration file', () => {
  const file = 'YGOProbabilityCalculatorBlazor/Engine/EngineTestVisibility.cs';
  const header = 'using System.Runtime.CompilerServices;\nnamespace CardProbability;\n';
  const approved = '[assembly: InternalsVisibleTo("CardProbability.EngineTests")]';
  assert.deepEqual(inspectEngineSource(header + approved, file), []);
  assert.ok(inspectEngineSource(header + approved, 'other.cs').length);
  assert.ok(inspectEngineSource(header, file).length);
  for (const friend of ['YGOProbabilityCalculatorBlazorTest', 'Consumer', 'CardProbability.EngineTests.Other']) {
    assert.ok(inspectEngineSource(`${header}${approved}\n[assembly: InternalsVisibleTo("${friend}")]`, file).length, friend);
  }
  assert.ok(inspectEngineSource(`${header}${approved}\n${approved}`, file).length);
});
