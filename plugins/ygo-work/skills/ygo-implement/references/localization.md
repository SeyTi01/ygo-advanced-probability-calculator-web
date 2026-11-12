# Localization contributions

Use for presentation translations or adding a locale. Read the relevant resource/identity contracts in `repo:docs/localization.md` and the matching `repo:docs/localization-<locale>.md` terminology guide. Reuse the satellite, Help, checkpoint and optional-label architecture; do not create a separate page, catalog or service stack for a language.

## Add a UI locale

1. Add `repo:YGOProbabilityCalculatorBlazor/Localization/Strings.<locale>.resx` with the neutral keys. Verify every translated placeholder; missing entries intentionally fall back to English.
2. Add the deployable language ID and readable native name to `repo:YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json`. Register only locales whose satellite ships. For optional card-name data, use [card-data maintenance](card-data.md#manual-localized-name-repair-or-first-baseline); a UI-only locale needs no card-name asset.

## Verify translations and locale changes

Use [verification](../../ygo-verify/SKILL.md) for applicable localization/editor and regular .NET/JavaScript tests.

For locale registration, startup, loading or handoff changes, use [browser checks](../../ygo-verify/references/browser-checks.md) for the optimized publish, localization smoke and published SEO checks, including `PublishedOutputSeoTest`. Inspect translated UI where wording or layout can affect controls; [UI work](../../ygo-ui/SKILL.md) owns visual review and any applicable approval gate.
