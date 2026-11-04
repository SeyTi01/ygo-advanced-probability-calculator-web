# Italian localization terminology

Italian (`it`, Italiano) uses direct, informal instructions and short action labels. The implementation agents authored and reviewed the translation; no native-speaker review is claimed. UI language does not change numeric culture, calculation behavior or serialized values.

| English concept | Italian presentation | Meaning |
| --- | --- | --- |
| Exact | Esatto | Proven probability within the configured model. |
| Fast | Rapido | Calculation route; the result can still be exact, bounded or uncertified. |
| Progressive | Progressivo | Completed Fast result followed by an Exact attempt when needed. |
| Bounded | Con limiti certificati | Certified interval, not a statistical confidence interval. |
| Uncertified | Non certificato | No proven physical-model error bound. |
| Preliminary | Preliminare | Completed Fast result awaiting a possible Exact refinement. |
| distinct physical copies | copie fisiche distinte | Separate positive requirements need separate copies. |
| requirement / same-card AND / OR | requisito / E sulla stessa carta / O | Inclusive bounds and alternative semantics are unchanged. |
| Any combo | Almeno una combo | Union of active successes, counting overlaps once. |
| modeled primary fraction | frazione primaria modellata | Retained model fraction; it can differ from the certified interval's midpoint. |
| workspace / session | area di lavoro / sessione | Current accepted state / portable saved configuration. |
| pinned result | risultato fissato | Snapshot retaining its original inputs and accuracy. |
| built-in simulation / model notes | simulazione integrata / note sul modello | Maintained model subject to the existing compatibility gate. |
| draw / search / removal | pesca / ricerca / rimozione | Modeled operations; removal does not imply discarding. |
| discard / banish / GY | scartare / bandire / Cimitero | Specific operations when described by the maintained model. |
| Level / Rank / Link rating | Livello / Rango / Classificazione Link | Numeric display labels; compact Link labels remain short. Identifiers stay invariant. |
| Work / Storage | Operazioni / Memoria | Runtime resource limits, not accuracy settings. |

Common monster traits/types, attributes, Spell/Trap subtypes and supported numeric properties have optional `Metadata.<exact key>` resources. Attribute labels use OSCURITÀ, LUCE, TERRA, ACQUA, FUOCO, VENTO and DIVINO. Color accessibility labels use `Color.*`; stored palette indices remain unchanged. Display operators use E/O/NON; their internal tokens and serialized selectors remain unchanged.

Common property terminology follows the [official Italian card-search labels](https://www.db.yugioh-card.com/yugiohdb/card_search?request_locale=it), including Cyberso, Scoperta, Divinità-Bestia and Classificazione Link. The catalog-only Creator God type uses Divinità-Creatrice as a descriptive label; no official Italian card release is implied.

## Intentional English and unchanged content

- The product name **Yu-Gi-Oh! Advanced Probability Calculator**, proper names, URLs, filenames, keyboard key names, formats such as YDKe and the two preexisting CSS-token resources remain unchanged.
- User-authored card, category, combo and group labels remain verbatim, including the example's `Full VS`, `Full K9` and `VS Starter + (FIRE OR DARK)`. Only a verified external ID and an exact canonical English stored card name permit a localized display label. Saved names and JSON are never rewritten.
- The separate Italian card-name asset supplies verified display/search labels where available. English queries remain supported. Missing source names, unsupported metadata/archetype labels and asset failures fall back to English; no complete provider coverage is claimed. See the [catalog generation and coverage guide](../infrastructure/card-catalog/README.md).
- Maintained effect prose is translated independently of the card-name asset. Its card and archetype references retain canonical English proper names to keep their identity unambiguous. Refreshing card labels does not rewrite prose, memberships, effects or rules.
- Historical/custom effect notes remain their authored text. The existing provenance and compatibility gate decides whether a maintained translation applies.
- Canonical static/prerendered SEO, social metadata, host loading/fatal-error/noscript text, external documentation and runtime/worker/certificate diagnostics remain English. Localized wrappers preserve diagnostic payloads.
- Exact fractions, numeric culture, invariant session/share/worker bytes and literal Help/resource-limit reference numbers remain unchanged.

The satellite covers every neutral resource key, including all nine Help FAQs and maintained effect summaries/limitations, plus the shared optional metadata and color labels. It uses the existing resource, Help-fragment, language-handoff and optional card-name loading architecture. See [localization contributors](localization.md).
