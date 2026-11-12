# Editor state contracts

Preserve drafts and active state through edits, removal/reordering, session loads and deck imports. Keep `EditorKeys<T>` and Blazor `@key` identity across model replacement. Parent updates must not overwrite an in-progress draft; hand-size changes may update untouched defaults, never user-entered or saved constraints.

Preserve user-controlled focus, selection, scroll position and tab state where the interaction requires it. Use [test design](../../ygo-verify/references/testing.md) for meaningful editor regressions.
