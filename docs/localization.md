# Localization contributors

English is the only production locale. Presentation uses the platform's
`ResourceManager` and keyed `.resx` satellites in `Localization/Strings.resx`.
This avoids an additional package and works equally in Razor, static C#
presentation helpers, WebAssembly, and the publish-time prerenderer.

## Resources and messages

Use `UiText.Get("Area.Message")` for plain text and
`UiText.Format("Area.Message", count, name)` for a complete message with numbered
placeholders. Existing `Ui.*` keys identify the migrated English messages;
retain their keys when wording changes. New keys should describe their purpose.
Do not translate CSS classes, DOM IDs, operator/input tokens, file extensions,
JSON keys, URLs, or developer/worker protocol diagnostics. Razor encodes the
returned text normally: resources must not contain executable or trusted HTML.

Keep full sentences in one resource. Parameters may be reordered, repeated,
aligned, or formatted using .NET composite formatting (`{0}`, `{1:N2}`). Do not
build sentences by translating English prefixes or suffixes separately. Lists
of independent effect steps can share a separator; these describe custom plans,
not new maintained card descriptions.

Lookup follows satellite, parent culture, then neutral English fallback. An
unknown English key returns the key and emits a trace warning. Invalid translated
format syntax or a different set of parameter indices emits a warning and falls
back to English. Incorrect argument counts at the call site remain programming
errors (`FormatException`). Resource newlines use LF, preserving copy/export
bytes across build hosts. Formatting uses the caller's existing `CurrentCulture`;
language resolution changes only `CurrentUICulture`.
Unchanged/neutral text skips placeholder parsing. Translated text is validated
once per resource manager, key and locale (and revalidated if its source text
changes), in a bounded cache whose entries never retain discarded managers.
The WASM project loads the full globalization dataset so changing the UI language
during startup is supported while retaining the browser's numeric culture.

## Adding a locale

1. Add `Localization/Strings.<locale>.resx` with the same keys. Missing entries
   intentionally fall back to English. Verify every translated placeholder.
2. Add its deployable language ID and readable native name to
   `wwwroot/localization/locales.json`. This single file is served to startup
   JavaScript and embedded in .NET. Register only locales whose satellite ships.
   At two production locales the global header selector appears automatically
   on both routes. English alone keeps the existing header.
3. Run the localization/editor tests, optimized publish, prerendered SEO checks,
   and the isolated published browser workflow described below.

## Website preference and startup

`ygo-calculator:language:v1` in localStorage contains an explicit website
preference, independently of every deck, session, worker and share payload.
Resolution is saved supported preference, then ordered `navigator.languages`,
then English. Matching walks from a regional tag to registered parent tags.
An explicit English selection overrides a supported German browser language.
Unsupported/stale values are ignored; denied storage still allows startup.
There is no language entry in Calculation settings or Browser default option.
Clearing browser data resets the preference.

The head script `js/locale.js` fetches the small registry before
`Blazor.start({ applicationCulture })` (autostart is disabled). Program sets
only the UI culture before `RunAsync`. .NET 10 loads satellites for **both**
CurrentCulture and CurrentUICulture, so the browser numeric culture is retained
in the shell preference and applied by App.OnInitialized **after** satellite
loading but **before** the first route renders. The full ICU dataset stays
configured. This prevents an unselected numeric-language satellite download,
while retaining existing decimal formatting and invariant serialization.
See the [Blazor startup culture documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0#statically-set-the-client-side-culture).

Static `/` and `/help` HTML, canonical routes and SEO remain English. A head-time
visibility gate hides prerendered app labels until resolution; English immediately
reveals its canonical HTML. For a non-English preference it keeps that HTML hidden
until the correctly initialized interactive layout is ready. The document language
then changes with the interactive UI. This does not translate or replace SEO
metadata. JavaScript-disabled crawlers/readers retain canonical English HTML.
Startup errors reveal the existing runtime-error UI. No translated card catalog
is bundled; translations remain .resx satellites selected by startup.

## Language reload and editor handoff

A language change takes one reload of the **exact current URL**. The header
briefly makes the live page inert, captures component-owned drafts and accepted
workspace data, then rechecks that capture for concurrent changes. Active
calculations require confirmation before the reload cancels them. Pending
session/import work blocks a switch with an actionable message.

`LanguageDraftBoundary` is a non-rendering registration with a stable semantic
key. It does not wrap editor rendering or replace EditorKeys/@key. Explicit
snapshots retain quick-add text, category/rule/group drafts, combo requirement
forms, numeric-picker invalid input, simulation plans and nested selector edits,
move destinations and open menus. A separate presentation checkpoint retains
native details state and raw uncommitted/invalid control values without firing
commit events. Accepted results and manual pins are retained; computation in
progress can end. New draft-owning controls must register a checkpoint and add a
restoration test; never use translated labels or generated DOM IDs as keys.

The handoff is separate sessionStorage under
`ygo-calculator:language-handoff:v1` (at most 2 Mi UTF-16 code units). Each typed
snapshot must round-trip locally. Both the handoff and persistent preference are
written and read back **before** navigation. Storage failures abort in the live
workspace. Existing handoffs are never overwritten. The record is tab-local,
contains a build identity, and is eligible only on the immediately following
reload of the exact URL/locale within two minutes. Any startup claims it once;
unrelated visits and further reloads cannot replay it. A changed app build
rejects restoration rather than interpreting a different draft format.

Restoration waits for every registered snapshot and verifies the control layout
before acknowledging/deleting the record. During restoration autosave is suspended.
The existing recovery key is never used as the handoff store. The recovery client
carries its expected record and paused status: only an unchanged owned record
resumes without a recovery prompt. A foreign/preexisting record remains protected,
as does a pending shared offer and its URL fragment. This handoff never invokes
session enrichment, applies presets, consumes a shared link, or changes the
session schema or the maintained-effect compatibility gate.

If restoration fails or remains incomplete, a warning reveals the UI and offers
**Download preserved handoff**; the tab record remains, and autosave stays
suspended to protect the original. Download it before clearing browser data.
Normal changes restore automatically without a file load or recovery prompt.

## Published integration check

`node scripts/localization/prepare-test-host.cjs` makes an ignored source copy
under `bin/localization-test-host`, adding the test-only pseudo satellite under
`fr` and `de` plus an isolated registry. Neither pseudo resource nor registry
is included in production. Publish that copy with the same optimized non-AOT
configuration as the production app. Use `--refresh` to recreate a previously
marked generated copy after source changes, then run:

```sh
node --test scripts/localization/locale.test.mjs
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release -o YGOProbabilityCalculatorBlazor/bin/language-publish
node scripts/localization/prepare-test-host.cjs
dotnet publish bin/localization-test-host/YGOProbabilityCalculatorBlazor.csproj -c Release -o bin/localization-test-publish
node scripts/localization/browser-smoke.cjs YGOProbabilityCalculatorBlazor/bin/language-publish/wwwroot bin/localization-test-publish/wwwroot
```

Playwright and a Chromium browser are external test dependencies; set
`YGO_BROWSER_EXECUTABLE` or `YGO_BROWSER_CHANNEL` as for the existing smoke tests.
The optional third browser-script argument is a frozen dev publish for startup
transfer comparison. The script checks first visible interactive language,
satellite requests, real reload/restoration, saved English on a German browser,
invalid preferences, navigation, drafts/results, protected recovery/share state,
storage failure, and desktop/mobile header usability. Run the regular .NET/JS
suites and `PublishedOutputSeoTest` against the production publish as well.

The test project's `Localization/Strings.fr.resx` is an intentionally artificial
pseudo locale with selected substitutions, omissions, and one invalid format.
The test project links the neutral resources and uses its own satellite assembly
through an internal, async-local resource scope. No pseudo resource or pseudo
locale registration ships in the app. `UiText.English` always reads the production neutral
resources, even inside that scope.

## Identity and optional label data

`DisplayLabels` is a rendering boundary. Optional resource/data keys are
`Cards.<verified canonical ID>.Name`, `Metadata.<exact MetadataKey>`,
`Archetypes.<canonical name>`, and `NumericProperty.<enum name>`. Missing optional
labels retain the canonical English display value. Numeric requirement rendering
uses parameterized `NumericRange.*` messages. Another offline label/alias source
can be integrated here later; no additional catalog is supplied by this change.

Catalog `CardInfo` results have verified identity and may use localized labels.
Saved `Card.Name` has no reliable authorship provenance, so it always retains its
stored text, even when it equals a catalog name. User card, category, combo and
group names are never translated or rewritten. Keep canonical English default
names used when duplicating authored entities independent of locale as well.

Never change `CategoryBase.Identity`, `MetadataKey`, selector signatures, numeric
keys, permanent-name/mention identities, card IDs, rule definitions, manual
overrides, matching order, or serialized memberships to match a display label.
Encoded English metadata tokens remain stable identifiers. Continue using the
single offline archetype resolver's canonical `resolvedArchetypes`; translated
names and aliases must not compile alternate membership rules. See the
[catalog instructions](../infrastructure/card-catalog/README.md).

## Maintained effect descriptions

Each definition has `Effects.<canonical ID>.Summary` and `.Limitations` resources,
including empty notes. Definition/profile construction reads **neutral English**
with `UiText.English`, preserving saved snapshots independently of locale.
`EffectDescriptions.Describe` first calls `CardEffectRegistry.Describe` and only
translates when `UsesCurrentDescription` passes its full provenance, ID, revision,
execution model, support and saved-plan gate. Historical/incompatible descriptions
remain saved text. Custom and legacy effects retain their existing plan
presentation/fallback; they never acquire translated curated notes by inference.
No profiles/plans are mutated. Wording-only corrections need no semantic revision
bump. Author each maintained card's translation explicitly; do not synthesize it
from arbitrary selectors.

## Coverage and deferred English

Resources cover the calculator's card/category/combo editors, rule and effect
editors, import/session/recovery/share actions and their parser errors, settings,
results/accuracy and export messages, dialogs, shell/theme controls, accessible
names, titles and tooltips, plus all maintained summaries/notes.

The deliberate later translation pass consists of:

- The Help article's getting-started, requirements, simulations, modes, results,
  sessions, example and credits prose, headings and topic navigation. The page
  title and example action/errors establish `Help.*`; continue extracting complete
  paragraphs around the existing semantic markup rather than cloning the page.
- The canonical English SEO descriptions/social-image alternatives in
  `Pages/Index.razor` and `Pages/Help.razor`, plus the static host's initial loading,
  fatal runtime-error and noscript text in `wwwroot/index.html`.
- Detailed solver/certificate/worker diagnostic explanations and exception text
  from model/converter/schema validation and external runtimes. UI-authored
  wrappers are resources; underlying diagnostic payloads stay unchanged. A future
  diagnostic-code presentation adapter must preserve those protocol contracts.
- Canonical catalog/property/color data with no translated label supplied. These
  use existing English fallback values; user-authored text stays verbatim.

The `/help` route, canonical metadata, development-channel version label and
publish-time prerendering behavior stay intact.
