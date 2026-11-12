# Editor and visual contracts

Preserve drafts and active state through edits, removal/reordering, session loads and deck imports. Keep `EditorKeys<T>` and Blazor `@key` identity across model replacement. Parent updates must not overwrite an in-progress draft; hand-size changes may update untouched defaults, never user-entered or saved constraints.

Unless redesign is requested, preserve established density, spacing, colors, badges/counters and icon conventions. Reuse existing representations and shared controls. Avoid redundant labels or explanatory text in compact controls. Icon-only controls require accessible names, consistent hover titles/tooltips, keyboard access and reasonable click targets.

Preserve user-controlled focus, selection and scroll position where the interaction calls for it. Inspect popup anchoring, tab-specific state and dismissal in the actual rendered UI when affected. Do not bake a one-off preview preference into every future control.

Use [test design](../../ygo-verify/references/testing.md) for editor regressions. Inspect practical desktop/narrow viewports and relevant themes; exact spacing and incidental markup generally belong to visual review rather than exhaustive brittle test matrices.
