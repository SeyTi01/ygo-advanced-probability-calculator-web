# French localization terminology

The French locale (fr, Français) uses short infinitive action labels and complete explanatory messages. Terminology is suitable for French-speaking TCG users; it does not select a numeric culture. The implementation agents authored and reviewed this translation; no native-speaker review is claimed.

| English concept | French presentation | Meaning |
| --- | --- | --- |
| Exact / Fast / Progressive | Exact / Rapide / Progressif | Calculation modes; a Rapide result can still be exact. |
| Bounded / Uncertified | Borné / Non certifié | A certified interval / no established physical error bound. |
| Preliminary / Inputs changed | Provisoire / Données modifiées | A completed Rapide result awaiting refinement / a result for earlier inputs. |
| Any combo | Au moins un combo | Union of active successes, counting overlaps once. |
| requirement / AND / OR | condition / ET / OU | Distinct copies for separate positive conditions; ET properties match one card. |
| distinct physical copies | exemplaires physiques distincts | One physical copy cannot fill two separate positive requirements. |
| modeled primary fraction | fraction principale du modèle | Retained fraction, which can differ from a displayed certified midpoint. |
| workspace / session / pinned result | espace de travail / session / résultat épinglé | Current accepted state / portable configuration / captured comparison result. |
| built-in simulation / model notes | simulation intégrée / notes du modèle | Maintained model selected through the existing compatibility gate. |
| draw / search / removal | pioche / recherche / retrait | Modeled operations; a generic retrait is not necessarily a défausse. |
| discard / banish / GY | défausser / bannir / Cimetière | Specific operations when described by the maintained model. |
| Level / Rank / Link rating | Niveau / Rang / Lien | Numeric labels; identifiers remain invariant. |
| Work / Storage | Travail / Mémoire | Runtime resource limits, not accuracy settings. |

Common card kinds, monster traits/types, attributes, Spell/Trap subtypes, numeric properties and palette accessibility labels have explicit optional resources. Attribute labels use TÉNÈBRES, LUMIÈRE, TERRE, EAU, FEU, VENT and DIVIN. The [official French rulebook](https://www.yugioh-card.com/eu/fr/play/yu-gi-oh-tcg-rulebook/) and [2021 rules update](https://www.yugioh-card.com/eu/fr/play/2021-rules-update/) provide reference terminology, including Échelle Pendule and Zones Monstre Main. Resource keys and canonical metadata are unchanged.

## Intentional English and fallback

- The product name, URLs, file formats/extensions, keyboard key names (Enter, Escape), identity tokens, two existing CSS-token resources and proper names remain unchanged. Words shared by both languages, such as Combo, Maximum and Simulation, are intentional.
- User-authored card/category/combo/group names remain verbatim. The example's Full VS, Full K9 and VS Starter + (FIRE OR DARK) are saved names, so the Help explanation preserves them. Generic explanatory examples use French property labels.
- Verified catalog-backed card display and bilingual search use the separate French name asset. Missing names, unknown identities, ambiguous artwork aliases, unsupported property/archetype labels and unavailable assets retain English. Saved names and IDs are never rewritten; name text alone cannot prove identity. See [offline catalog generation](../infrastructure/card-catalog/README.md).
- Maintained effect descriptions translate the calculator's own model, rather than importing card text. Their card and archetype references retain canonical English proper names, including newer releases, independently of optional name-data refreshes. Custom/historical notes remain saved text; incompatible saved plans do not acquire current maintained translations.
- Static/prerendered SEO and social metadata, initial host loading/fatal-error/noscript text, linked developer documentation, detailed runtime/worker/certificate diagnostics and underlying validation exception details remain English. Their UI wrappers are translated without altering protocol data.
- UI language does not change sessions/share links, recovery data, worker inputs, exact fractions or numeric serialization/formatting. Literal example fractions/percentages and existing resource-limit option notation remain unchanged.

All nine Help FAQs reuse the shared page and semantic placeholders. Further changes should follow the [localization contributor guide](localization.md), retain the existing locale/checkpoint and catalog boundaries, and preserve complete messages and placeholders.
