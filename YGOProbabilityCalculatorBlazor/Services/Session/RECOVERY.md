# Local session recovery

`SessionService.SerializeSession` is the offline serialization boundary shared with
Save Session. Loading uses the existing migration, converters and ID validation.
The transport envelope is version 1; the enclosed session retains schema version 2.

`SessionRecovery` compares serialized accepted model values on parent renders. This
covers category/color/name/order changes, cards and manual properties, constraints,
groups, hand size and active flags. Temporary editor drafts, results, artwork and
theme preferences are outside that snapshot. Successful explicit loads/imports
increment `ReplacementVersion`; failed loads preserve the recovery record.

`js/session-recovery.js` owns one `ygo-calculator:session-recovery:v1` record, limited
to 524,288 UTF-16 code units (1 MiB before storage overhead). A 750 ms timer coalesces
captured immutable JSON strings. Monotonic generations fence reordered interop,
replacement and discard. Storage reads, writes and removal run synchronously on the
browser event loop; no asynchronous storage write can later undo a decision.
Visibility-hidden, pagehide and component disposal flush an already queued snapshot.
No unload delivery guarantee is assumed. Crashes can lose the debounce window or
changes still travelling to JavaScript. Storage denial, quota and oversized sessions
retain the last good record and show a warning; Save Session remains available.

Inspection pauses writes until a recovery decision. Dismiss keeps the draft and
pauses autosave; discard removes only this key and waits for a new real edit.
Untouched empty startup creates no record. Unreadable/future drafts stay until an
explicit discard or replacement. Restore with current edits requires a second,
explicit replacement action. Loads/restore/import completions cannot replace work
that changed while they were awaiting I/O. Metadata enrichment is best effort with
a two-second bound and detached models so late results cannot mutate applied work.

Each tab compares the stored record against its last observed/successfully written
record before writing or discarding. A changed record pauses that tab and offers an
explicit use-current-workspace action. This is conflict detection, not collaborative
synchronization; explicit replacements and near-simultaneous cross-tab writes can
use last-writer behavior (Web Storage has no atomic compare-and-swap). Idle new tabs do
not write. Browser storage remains specific to the site's origin/profile and may
be cleared by the browser or user.

Run deterministic browser-storage tests with
`node --test YGOProbabilityCalculatorBlazorTest/SessionRecovery/session-recovery.test.mjs`.
