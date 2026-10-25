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

## Language reload and accepted workspace

A language change takes one reload of the **exact current URL**. Before switching,
finish an open effect, requirement, rule or rename form by saving/applying or
cancelling it. Clear or add nonempty quick-add/category/group text first. Invalid
numeric input also blocks the switch. The shell reads these semantic edit markers
and input states only to enforce the precondition; it never serializes form fields.
Accepted inline name/copy/settings changes are already part of the workspace.
Calculations and pending session/deck imports must finish or be cancelled first.
Blocking leaves both the live page and the saved language preference unchanged.

The calculator supplies **one top-level checkpoint** containing its existing
SessionState JSON, resource setting, accepted result and manual pin. Session
serialization is checked with the existing offline codec before navigation.
No child editors participate. Expanded panels, menus, focus and move destinations
are transient and are not restored.

The checkpoint uses a separate tab-scoped sessionStorage record at
`ygo-calculator:language-handoff:v1`. Protocol version 2 contains one opaque
accepted-workspace payload, not a component registry. Its record and the new
website preference are both written and read back before reloading. A write or
capture failure keeps the live workspace. A prior record is never overwritten.
Only the immediately following reload of the same URL and resolved locale,
within two minutes, can claim it. Any startup claims the record once; later or
unrelated visits cannot replay it. Invalid, expired or earlier-version records
are kept and reported rather than silently applied.

On startup the calculator validates and applies the session offline before its
first render, without catalog enrichment, default plans or share consumption.
The shell consumes the checkpoint after successful restoration. Autosave stays
suspended until acknowledgement. Recovery resumes without a prompt only when its
expected owned record is unchanged; foreign/preexisting recovery drafts and
incoming shared offers remain protected. On failure, the record remains in this
tab with a clear error and **Download preserved handoff** action. This is separate
from normal explicit recovery and never changes session schema or worker bytes.

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
invalid preferences, navigation, accepted results, blocked unfinished edits, protected recovery/share state,
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
