# Minimal release notes

Use this for notes-only requests, release PR summaries and published GitHub Releases. Read the actual changes from the previous published stable release's tag to the intended release commit, including main-only fixes and excluding reverted changes. For an upcoming release, use the prospective promotion diff, then reconcile it with the final commit. PR titles and generated notes are discovery aids, not the final text. Prior releases establish naming conventions, not a target length or an instruction to repeat their technical detail.

Select changes by what calculator users can now do, what important incorrect behavior is fixed, or what meaningful improvement they will notice. Rank candidate points by impact and combine related changes into one outcome.

- Aim for **two short bullets**. One is fine. A third must cover a distinct, substantial user benefit; three is a ceiling, not a quota. Do not add a fourth routine point.
- Use one short sentence per bullet in plain user language. Usually keep the complete highlights under 60 words. Avoid headings for such a short list, nested lists, PR numbers and implementation explanations.
- Exclude internal refactors, engine boundaries, worker/serialization details, tests, CI, dependency updates, plugin work and minor visual polish unless they cause a material user-facing change. Describe that consequence, not the mechanism. Claim speed or accuracy improvements only when supported by the release's evidence.
- Do not pad with "other fixes and improvements", inventory every feature, or preserve a point merely because it was work-intensive. With no meaningful user-facing change, use one honest short sentence rather than inventing highlights.
- Necessary compatibility, migration, data-loss or required-user-action warnings are never removed to meet the size target; put them in a short separate paragraph when needed.

Before publishing, perform a **deletion pass**: identify the weakest bullet and remove it unless its omission would hide an important feature, fix or required action. Remove technical clauses from the remaining bullets. This is an explicit second pass, not another request to "be concise". Honor a user's explicitly requested length or wording.

For example, excavation support, new interface languages and an editor refactor that preserves every existing behavior yield two user-facing points:

- Simulate Pot of Duality and Pot of Prosperity excavation.
- Use the calculator and search card names in the newly supported languages.

The refactor does not receive a third bullet. A small layout adjustment does not become "Improved editor usability" without a meaningful user benefit.

For a published Release, append one **Full changelog** link to `https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/compare/PREVIOUS_TAG...NEW_TAG`, substituting the verified tags. It supplies the full detail without adding another highlight. GitHub has no separate stored predecessor field: `previous_tag_name` selects the range for its generated-notes endpoint, not the Release creation endpoint. Use the captured predecessor for your evidence and comparison link. For the first release, omit that link rather than inventing a previous tag.

Publish the curated body verbatim. Do not enable automatic notes, prepend highlights to an automatically generated inventory, or use a tag/commit message as release notes. See [GitHub publication](github-publication.md) for the publication flags.
