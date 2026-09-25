# Issue #12: refactor recovery verification

## Scope and baseline

Recovery starts at `240a036` on `12-refactor-main-component`, preserving all eight existing commits. `dev` was `4789484`; the refactor had no divergence, and no open PR existed when checked. The original branch is unchanged.

The five extracted editors remain. The main component still coordinates calculation, imports, and sessions. `Program.cs` preserves its service registrations. The inherited CSS/HTML focus changes were reviewed and retained; browser focus behavior still needs the manual check below. No deployment, Cloudflare, CI, framework-version, release, or repository-policy changes were made.

The original suite contains 35 tests in eight files: probability (7), converters (14), deck import/card information (9), and sessions (5). Both untouched branches restored, built without warnings/errors, and passed all 35 tests. Neither baseline exercised the calculator components.

## Repairs and evidence

- Explicit zero maxima and invalid drafts survive parent renders and hand-size changes; only untouched defaults track hand size. Reselecting a saved combo category loads its bounds and allows updating it.
- Restored visible validation for missing/unknown/duplicate category selections, invalid ranges, and non-positive/invalid copy counts. `dev` allowed any positive copy count; the refactor's silent 0–3 clamp was a regression.
- List-level keys follow card/combo identities through deletion/reordering. Immutable edits transfer their existing editor key, preserving draft input and focus. Imported/loaded objects get new editors. Keys are UI-only and do not alter the session schema.
- Category changes notify the parent so sibling selectors refresh immediately. Usage checks use typed cards/combos, with feedback when deletion is blocked.
- Blazor alone controls accordion expansion, avoiding competing Bootstrap collapse handlers. Restored input labels and expansion attributes.
- Import/session loading resets stale selection/result state. Category drafts reset on session load. Combo updates preserve unrelated repeated constraints from existing sessions; removing one constraint removes that entry rather than every matching name.

Four temporary, isolated interaction probes failed on the original refactor and passed after applying the component repairs: zero max became hand size; six copies became three; category creation left sibling options empty; deleting an earlier card lost the surviving draft. Equivalent behavior is covered by the committed interaction suite; the temporary probe file is not included as duplicate tests.

The final suite has 57 cases: the original 35, ten bUnit interaction tests, and twelve oracle cases (eleven named fixtures plus one bounded 24-sample seeded test). Session testing uses the real serializer/service and download payload; import testing uses the real YDK parser and file reader, with the external card-name lookup stubbed. The oracle enumerates each physical hand once and directly checks OR-of-combos / AND-of-constraints; it shares no production counting or merging helpers.

## Independent probability defects

The new oracle exposed two pre-existing defects, fixed in a separate small commit without replacing the inclusion-exclusion/distribution algorithm:

1. Positive maxima were clipped into range, counting invalid hands as successes. For three A cards and four other cards, drawing two with exactly one A returned 15/21 instead of 12/21. Over-limit states are now discarded because category counts can only increase.
2. Intersections of mutually exclusive category ranges constructed invalid `Category` objects and threw. Such intersections now contribute zero to inclusion-exclusion.

The first oracle run failed on positive maxima, overlapping bounded categories, disjoint ranges, and seeded cases. The original `ExactRangeRequirements_CalculateCorrectProbability` assertion encoded the first bug: for two starter-only cards, one extender-only card, and one dual-category card, exactly one of each succeeds only for the two starter-only + extender-only hands, or **2/6**, not 5/6. The test is retained with that corrected expectation and explanation.

## Commands and results

Executed on Linux with .NET SDK **9.0.318** / runtime **9.0.20**, keeping both projects on `net9.0` and existing application package versions. bUnit **1.40.0** is the only added test dependency. The SDK was installed locally for execution. This sandbox required serial MSBuild (`-m:1`); an initial parallel restore exited without a useful error, then serial restore/build succeeded.

From each separate checkout (`baseline-dev`, `baseline-refactor`, and recovery):

```sh
dotnet restore YGOProbabilityCalculatorBlazor.sln -m:1
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build -m:1 \
  --collect:"XPlat Code Coverage" --results-directory ../results/<checkout>
```

The actual result-directory names were `baseline-dev`, `baseline-refactor`, and `final-verified`. All three restores/builds succeeded with zero warnings/errors. Tests passed **35/35**, **35/35**, and **57/57**, respectively; no tests were skipped. `git diff --check` also passed.

Coverlet's Cobertura files measured the instrumented application assembly (not the test assembly or dependencies). Percentages below are calculated from the covered/valid counts. All measurements used Debug and the same collector settings. Added component execution expands exercised code, but the application assembly remains the measurement scope; the source denominator changes with the refactor and repairs.

| Scope | `dev` lines / branches | Original refactor lines / branches | Recovery lines / branches |
| --- | --- | --- | --- |
| Entire application | 41.89% (323/771) / 34.13% (86/252) | 41.15% (323/785) / 31.16% (86/276) | 88.04% (736/836) / 80.79% (244/302) |
| All services | 89.23% (290/325) / 76.42% (81/106) | same | 93.23% (303/325) / 80.70% (92/114) |
| Probability service | 99.03% (102/103) / 91.67% (33/36) | same | 99.03% (102/103) / 93.18% (41/44) |
| Calculator components and editor-key helper | 0% (0/360) / 0% (0/130) | 0% (0/363) / 0% (0/154) | 92.36% (387/419) / 85.29% (145/170) |

The probability service already had 99% line coverage while returning incorrect results. The independent oracle improves correctness assurance despite little change in that headline metric.

## Limits and follow-up

- bUnit verifies rendered components/events and application workflows, not real-browser focus, Bootstrap styling, downloads, or WebAssembly execution. No browser E2E or production smoke test was run. Manual checks below remain for review.
- Existing large-input algorithm limits remain outside this recovery: subset enumeration is exponential; its signed 32-bit bound cannot represent 31+ combos correctly, and category masks collide beyond 32 distinct categories. Separately add supported-input guards or redesign masks/enumeration before claiming support for those cases.
- `StateKey` contains an array with reference equality, so equal count vectors do not coalesce as intended; this is a separate scalability improvement. Floating-point intermediate counts also remain; small-deck exact enumeration does not prove precision at arbitrary scale.
- External live card API availability, malformed-session schema hardening, and all browser/device accessibility behavior are not covered by these tests.

## Manual smoke test before merge

- Add categories, cards, and combos; confirm new category options appear immediately. Rename and change counts, including six card copies. Reject empty selection, zero copies, negative minimum, and max below min with a visible message.
- Select an existing combo category, set min/max to 0/0, toggle editors and change hand size, then save/update. Confirm zero remains zero and reselecting the constraint loads its saved values.
- Keep drafts in the second card/combo, delete the first, and confirm the draft and expanded editor still belong to the surviving entry. Remove constraints and rows. Reject deleting categories used by either a card or a combo.
- Calculate with two A cards and two other cards, hand size two, and A constrained to 0/0: expect 16.67%. Test incompatible alternative ranges without an exception.
- Import a `.ydk`; confirm main-deck copies/names, replacement editor state, and hand-size eligibility. Save a session, change data/drafts, and reload it; confirm names, categories, bounds, copies, hand size, and calculation are restored.
- Navigate editors by mouse and keyboard; inspect focus indication, associated labels, and accordion expansion.
