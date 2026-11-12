# Persistence, import and localization

Preserve saved-session JSON, `.ydk`/YDKe imports, canonical identities and supported migrations unless explicitly changed. Read the relevant converter/session/import code before replacing models or serialized shapes. Apply [test design](../../ygo-verify/references/testing.md) at the affected boundary.

For loading, recovery, sharing or language handoffs, read `repo:YGOProbabilityCalculatorBlazor/Services/Session/RECOVERY.md`. Keep replacement precedence, draft behavior and cancellation/stale-load protection intact; use [editor contracts](../../ygo-ui/references/editor-contracts.md) when editor state is involved.

Card-name localization is presentation-only: preserve canonical IDs, typed drafts, custom names and serialized JSON. Read `repo:docs/localization.md` and only the affected locale guide. For catalog/source/coverage work, read `repo:infrastructure/card-catalog/README.md`; do not turn localized display data into identity or provenance.

Saved/custom effect plans are executed as saved; loading an old session must not silently replace them with current registry definitions. Inspect adapter and schema boundaries when changing effect data.
