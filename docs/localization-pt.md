# Portuguese localization terminology

The `pt` locale uses **Brazilian Portuguese**, with direct, concise actions and
`você` in explanatory messages. Browser preferences such as `pt-BR` resolve to
`pt` through the existing parent-language matching; `pt-PT` also selects these
Brazilian resources rather than a separate European Portuguese translation.
The translation was authored by implementation agents; no native-speaker review
is claimed. UI language remains independent of numeric culture and saved data.

| English concept | Portuguese presentation | Meaning |
| --- | --- | --- |
| Exact / Fast / Progressive | Exato / Rápido / Progressivo | Calculation routes; Rápido can still return an exact result. |
| Bounded | Com limites | Certified interval, not a statistical confidence interval. |
| Uncertified / Preliminary | Sem certificação / Preliminar | No proven physical error bound / accepted result awaiting possible refinement. |
| Any combo | Qualquer combo | Success in at least one active combo; overlapping successes count once. |
| distinct physical copies | cópias físicas distintas | Separate positive requirements need separate copies. |
| requirement / same-card AND / OR | requisito / E na mesma carta / OU | Inclusive bounds and independent alternatives retain their meaning. |
| modeled primary fraction | fração primária modelada | Retained model fraction, potentially different from the certified display midpoint. |
| workspace / session / pinned result | área de trabalho / sessão / resultado fixado | Accepted working state / portable saved setup / captured comparison. |
| draw / search / remove | comprar / buscar / remover | Modeled operations; removal does not imply discarding. |
| discard / banish / GY | descartar / banir / Cemitério | Specific operations when described by the maintained model. |
| Level / Rank / Link rating | Nível / Classe / Valor Link | Numeric presentation labels; metadata identities remain invariant. |
| Work / Storage | trabalho / armazenamento | Runtime resource limits, separate from probability accuracy. |

Monster types, attributes, card traits, Spell/Trap types and numeric labels use
optional `Metadata.*` resources. Attribute labels are TREVAS, LUZ, TERRA, ÁGUA,
FOGO, VENTO and DIVINO. These terms follow the
[official Portuguese card search](https://www.db.yugioh-card.com/yugiohdb/card_search.action?request_locale=pt)
and [Portuguese rulebook](https://img.yugioh-card.com/ygo_cms/ygo/all/uploads/Rulebook_v9_pt.pdf).
The interface uses familiar `carta` for a card; proper names, Deck, Link, ATK and
technical formats such as YDKe remain recognizable. All twelve palette colors
have accessible Portuguese labels; stored palette indices remain unchanged.

## Deliberately unchanged content

- The product name, proper names, filenames, URLs, keyboard key names, exact Help
  reference numbers and two existing CSS-token resources remain unchanged.
  Example labels such as `Full VS`, `Full K9` and
  `VS Starter + (FIRE OR DARK)` are saved authored text and stay verbatim.
- User-authored card, category, combo and group names are never translated in
  storage. Only verified canonical card identities may receive the separately
  loaded Portuguese display/search label; missing names, ambiguous identities
  and failed asset loads retain English. YAML Yugi supplies the reviewed name
  data through the shared [offline generation path](../infrastructure/card-catalog/README.md).
  No translation completeness beyond the generated audit is claimed.
- Maintained effect descriptions translate the calculator's supported model,
  with explicitly authored card/archetype references. References without a
  verified Portuguese wording retain their canonical proper names. Name-data
  refreshes do not rewrite these descriptions or compile alternate membership
  rules. Historical/custom effects retain their saved notes under the existing
  compatibility gate.
- Canonical prerendered SEO/social metadata, initial host loading/fatal-error/
  noscript text, linked developer documentation, runtime/worker/certificate
  diagnostics and underlying validation exceptions retain English. The visible
  UI wrappers are translated without changing protocol payloads.
- Session/share/recovery JSON, IDs, property keys, selector signatures, exact
  fractions, numeric serialization and numeric culture remain unchanged.

All nine Help FAQs use the existing semantic fragments and resources. Continue
through the shared [localization contributor guide](localization.md) when
changing messages, placeholders, data or loading behavior.
