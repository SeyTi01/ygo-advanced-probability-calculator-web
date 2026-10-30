# German localization terminology

The German pilot uses informal **du**, short action labels and complete explanatory messages. The translation was authored and reviewed by the implementation agents; no native-speaker review is claimed. UI language does not change numeric culture, calculation behavior or serialized values.

| English concept | German presentation | Meaning |
| --- | --- | --- |
| Exact | Exakt | Proven probability within the configured model. |
| Fast | Schnell | Calculation route; its result may still be exact, bounded or uncertified. |
| Progressive | Progressiv | Accepted Fast result followed by an Exact attempt when needed. |
| Bounded | Mit garantierten Grenzen | Certified interval, not a statistical confidence interval. |
| Uncertified | Nicht zertifiziert | No proven error bound against the physical model. |
| Preliminary | Vorläufig | Accepted Fast result awaiting possible Exact refinement. |
| distinct physical copies | verschiedene physische Kartenexemplare | Separate positive requirements consume separate copies. |
| requirement / same-card AND / OR | Anforderung / UND auf derselben Karte / ODER | Inclusive minima/maxima and alternative semantics are unchanged. |
| Any active combo | Mindestens eine aktive Kombo | Union of active successes, counting overlaps once. |
| modeled primary fraction | modellierter primärer Bruch | Retained model fraction; may differ from the bounded display midpoint. |
| workspace / session | Arbeitsbereich / Sitzung | Current accepted state / portable saved configuration. |
| pinned result | angeheftetes Ergebnis | Snapshot retaining its original inputs and accuracy. |
| built-in simulation / model notes | integrierte Simulation / Modellhinweise | Maintained model, subject to the existing compatibility/provenance gate. |
| draw / search / removal | Ziehen / Suche / Entfernen | Modeled operations; removal is not automatically described as discarding. |
| discard / banish / GY | abwerfen / verbannen / Friedhof | Specific operations when the maintained model describes them. |
| Level / Rank / Link rating | Stufe / Rang / Linkzahl | Numeric property labels; identities stay invariant. |
| Work / Storage | Rechenschritte / Speicher | Runtime resource limits, not accuracy settings. |

Common monster traits/types, attributes, Spell/Trap subtypes and supported numeric property labels have explicit optional `Metadata.<exact key>` entries. Attribute labels use FINSTERNIS, LICHT, ERDE, WASSER, FEUER, WIND and GÖTTLICH. Keys, IDs and canonical metadata remain unchanged. Color accessibility labels use optional `Color.*` resources; stored palette indices do not change.

## Intentional English and unchanged content

- The product name **Yu-Gi-Oh! Advanced Probability Calculator**, proper names, URLs, filenames, keyboard key names and technical formats such as YDKe remain unchanged. Two preexisting CSS-token resources also remain byte-for-byte unchanged; they are not prose.
- User-authored card, category, combo and group names remain exactly as saved, including the example's `Full VS`, `Full K9` and `VS Starter + (FIRE OR DARK)`. A verified external ID plus an exact canonical English name allows a German display label in card rows, fields and selectors; the saved name remains unchanged. Names alone never establish card identity.
- Card suggestions and verified catalog-backed deck entries use the separate German name asset when available. Missing provider translations, unsupported metadata/archetype labels and failed asset requests retain English labels. This is incomplete provider coverage, not a claim of complete German card data. See [catalog generation and coverage](../infrastructure/card-catalog/README.md).
  The October 29 YAML Yugi comparison would resolve all 24 example entries,
  including five K9 names equal to English, but remains research-only because
  redistribution permission for its name/passcode data is unresolved. The
  shipped example therefore still has 20 provider-name gaps; no manual example
  translations or additional runtime source have been introduced.
- Maintained effect prose is translated explicitly. Proper card references with verified German snapshot matches use those names (for example Polymerisation, Mystischer Raum-Taifun, Sonderfahrplan, Knapper Fahrplan, Märchenschweif-Geschichten, Wagenkupplung and Drehscheiben-Bahnhof). Established family spellings supported by snapshot names include Weltvermächtnis, Finsterlord, Himmelsjäger, Märchenschweif and Lichtverpflichtet. Newer references absent from that snapshot retain their canonical English names: Regulus, the Prince of Endymion; Shining Sarcophagus; Radiant Typhoon; Dracotail; Fiendsmith; Kewl Tune; Primite; and Diagram Token. These names are presentation only and never compile membership rules.
- Historical/custom effect notes remain their authored text. The existing compatibility gate decides whether a maintained translation applies.
- Static/prerendered English SEO and social metadata, host loading/fatal-error/noscript text, external linked documentation, runtime/worker/certificate diagnostic payloads and underlying validation exception details remain English. Localized wrappers do not rewrite protocol values.
- Invariant JSON, session/share/worker bytes, exact fractions and numeric formatting remain independent of UI language. Literal reference values in Help and the existing resource-limit options retain their existing numeric notation.

For another locale, add resources to the existing satellite architecture and use the existing optional label/search boundaries. Do not translate a second Help page or create another catalog/service stack. See [localization contributors](localization.md).
