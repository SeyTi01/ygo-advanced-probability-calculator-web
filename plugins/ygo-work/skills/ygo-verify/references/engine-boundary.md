# Engine isolation proof

After relevant calculator integration checks and [environment preparation](environment.md), run from the repository root:

```sh
node --test scripts/engine-boundary/guards.test.mjs
node scripts/engine-boundary/check.mjs
```

The second command restores/builds/runs temporary .NET projects, so apply the host's execution permissions. `--guards-only` selects source-boundary guards; `--keep` retains the temporary tree for diagnosis. The full check builds the isolated engine, public-only consumer, complete neutral tests and app/test substitution. It complements solution behavior and browser/AOT checks; it publishes nothing.

Read `repo:scripts/engine-boundary/README.md` when investigating proof mechanics, closure or failures.
