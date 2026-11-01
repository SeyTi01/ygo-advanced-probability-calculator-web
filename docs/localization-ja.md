# Japanese localization terminology

Japanese (`ja`, 日本語) uses concise action labels and polite explanatory prose.
The implementation agents authored and reviewed the resources; no native-speaker
review is claimed. UI language does not change numeric culture, calculations or
serialized values.

| English concept | Japanese presentation | Meaning |
| --- | --- | --- |
| Exact | 厳密 | Proven probability within the configured model. |
| Fast | 高速 | Calculation route; its result may still be exact, bounded or uncertified. |
| Progressive | 段階計算 | Completed Fast result followed by an Exact attempt when needed. |
| Bounded | 範囲保証あり | Certified interval, not a statistical confidence interval. |
| Uncertified | 保証なし | No proven physical-model error bound. |
| Preliminary | 暫定 | Accepted Fast result awaiting a possible Exact refinement. |
| Uninformative | 有用な下限なし | The lower bound gives no useful guarantee; the probability may be zero. |
| distinct physical copies | 別々のカード | Separate positive requirements consume separate copies. |
| requirement / same-card AND / OR | 条件 / 同じカードの特性 / 選択肢 | Inclusive bounds and distinct-copy semantics remain unchanged. |
| Any combo | いずれかのコンボ | At least one active success, counting overlaps once. |
| modeled primary fraction | 主モデルの分数 | Retained model fraction; may differ from the bounded display midpoint. |
| workspace / session | 作業内容 / セッション | Current accepted state / portable saved configuration. |
| pinned result | 固定した結果 | Snapshot retaining its original inputs and accuracy. |
| built-in simulation / model notes | 組み込みシミュレーション / モデル注記 | Maintained model selected through the existing compatibility gate. |
| draw / search / removal | ドロー / サーチ / 取り除く | Modeled operations; generic removal does not imply discarding or banishing. |
| discard / banish / GY | 捨てる / 除外 / 墓地 | Specific operations when the maintained model describes them. |
| Level / Rank / Link rating | レベル / ランク / リンク数 | Numeric labels only; identities stay invariant. |
| Work / Storage | 計算量 / メモリー | Runtime limits, not accuracy settings. |

Monster traits, types and races, attributes, Spell/Trap subtypes, supported numeric
properties and accessible color names have explicit optional labels. Attribute
labels use 闇, 光, 地, 水, 炎, 風 and 神. Stable metadata keys, card IDs, palette
indices and comparison operators remain unchanged. Gameplay vocabulary follows
the [official Japanese how-to guide](https://www.yugioh-card.com/japan/howto/).
The calculator models its documented policy, not all OCG or TCG play.

## Intentional fallback and preserved content

- The product name **Yu-Gi-Oh! Advanced Probability Calculator**, URLs, filenames,
  keyboard key names, technical formats such as YDKe, CSS tokens and operators
  remain unchanged. All nine Help FAQs use the existing resource-backed page.
- User-authored card, category, combo and group names remain exactly as saved.
  This includes the example's `Full VS`, `Full K9` and
  `VS Starter + (FIRE OR DARK)`. A verified external ID and an exact canonical
  English saved name allow a Japanese display label; the stored name stays intact.
  Names alone never establish identity.
- Catalog suggestions and verified card rows use the separate Japanese name asset
  when available. Missing or withheld source names, unsupported archetype labels
  and failed optional asset requests fall back to English. The
  [catalog generation guide](../infrastructure/card-catalog/README.md) records the
  retained source, canonical-ID join and coverage audit. The UI does not invent
  Japanese card names or infer memberships from translated text.
- Maintained effect descriptions retain canonical English card and archetype
  names as proper-name references. Their surrounding behavior, limitations and
  common gameplay terminology are translated. These authored descriptions do not
  dynamically follow the external name table. Historical/custom effect notes
  remain their original text; the compatibility gate governs maintained prose.
- Static/prerendered English SEO, social metadata, host loading/fatal-error/noscript
  text, external documentation and runtime/worker/certificate diagnostics remain
  English. Translated wrappers do not change protocol values or exception details.
- Invariant JSON, session/share/worker bytes, exact fractions and numeric formatting
  remain independent of UI language. Literal reference values in Help and the
  existing resource-limit choices keep their original numeric notation.

Use the existing [localization foundation](localization.md), checkpoint and
demand-loaded catalog boundary for future corrections; there is no Japanese
session format, probability engine or alternate card-membership resolver.
