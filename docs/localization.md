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
The WASM project loads the full globalization dataset so changing the UI language
during startup is supported while retaining the browser's numeric culture.

## Adding a locale

1. Add `Localization/Strings.<locale>.resx` with the same keys. Missing entries
   intentionally fall back to English. Verify every translated placeholder.
2. Add the locale to `LocalePreference.SupportedLocales`. Its resolver accepts
   an explicit shell preference first, followed by browser language preferences,
   then English. Regional preferences can resolve to their supported language.
3. Before enabling a second language, wire a shell-level preference store and
   `navigator.languages` to `Initialize`, and rerender after an explicit change.
   Store this preference separately from decks, sessions, recovery/share payloads
   and calculation input. Resolve it before interactive rendering. Keep static
   prerendering English, with matching initial HTML/lang and SEO; adding another
   production locale requires deciding the corresponding prerender route policy.
   There is deliberately no one-option language control or browser-storage read
   for language preference today.
4. Run the localization tests, existing editor/service suites, optimized publish,
   prerendered SEO tests, and desktop/mobile published-app checks.

The test project's `Localization/Strings.fr.resx` is an intentionally artificial
pseudo locale with selected substitutions, omissions, and one invalid format.
The test project links the neutral resources and uses its own satellite assembly
through an internal, async-local resource scope. No pseudo resource or locale
selection ships in the app. `UiText.English` always reads the production neutral
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
