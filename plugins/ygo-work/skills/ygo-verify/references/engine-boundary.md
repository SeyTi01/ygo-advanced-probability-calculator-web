# Engine package boundary

After relevant calculator integration checks and [environment preparation](environment.md), run from the repository root:

```sh
node --test scripts/engine-boundary/guards.test.mjs
node scripts/engine-boundary/check.mjs
```

The second command restores/builds/runs a temporary .NET consumer, so apply the host's execution permissions. `--guards-only` selects source/package guards; `--keep` retains the temporary tree for diagnosis. The full check restores the pinned package from NuGet.org into an isolated cache and exercises it through a public-only consumer. Normal solution builds compile the application and application tests against that package; run their behavioral tests separately. The engine implementation and complete neutral test suite are owned and verified by the library repository. This check complements solution behavior and browser/AOT checks; it publishes nothing.

Read `repo:scripts/engine-boundary/README.md` when investigating proof mechanics, closure or failures.
