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

## Session share links

`Copy share link` captures accepted model state using the same serializer as Save
Session. Unsaved editor drafts, results and theme preferences are excluded. Links
use the actual app base URI and calculator root, without unrelated queries or
anchors. Compression is cached on accepted snapshot changes; a press checks and
captures the current serialized state before clipboard interop. A clipboard denial
or lost user activation shows selectable link text. Success changes only the icon
for two seconds and announces a screen-reader status.

Transport v1 is `#ygo-session=v1.<unpadded-base64url>`. Decoded bytes contain a
four-byte little-endian uncompressed UTF-8 JSON length, a 32-byte SHA-256 digest of
the compressed bytes, then one gzip member containing compacted, otherwise
unchanged session JSON. The transport version is independent of the session
schema; existing file migration/converters validate a loaded session. The digest
detects corruption, and provides no authentication or encryption.

The application cap is 16,384 characters for a complete generated URL and for an
incoming fragment, with at most 262,144 decompressed bytes. This is a conservative
sharing policy, not a universal browser limit. Measured test-origin links are
3,240 characters for the current bundled example and 2,592 for a synthetic
100-card/50-combo fixture. A longer base path or less repetitive names can increase
these sizes. Larger sessions must use normal session-file sharing; no truncation
or partial link is produced. Input is bounded before base64 decoding. Declared
output length is checked before allocation, and gzip reads into that bounded
buffer followed by a single excess-byte probe. Strict UTF-8, JSON depth 32,
20,000 nodes, arrays of at most 2,048 entries, strings of at most 4,096 characters,
and duplicate-key/null-collection checks bound parsing and model loading.
Copy counts are bounded to -10,000 through 10,000 per row to keep aggregate UI
arithmetic safe; invalid/incomplete values within that range keep normal file semantics.

Initial links and later navigation (including back/forward) offer an explicit
`Load shared session` / dismiss choice. The warning states that acceptance replaces
the workspace, editor drafts and saved recovery draft. Inspection must finish
before loading is enabled. A pending link pauses recovery writes and cancels old
timers, including native hash/popstate navigation before the .NET render. Recovery
remains available as an alternative. An incoming startup link takes precedence
over a pending example by offering it without automatically applying either.
Dismissal keeps the workspace and draft; normal recovery conflict rules continue.
Accepted loads reuse load/edit ownership, enrichment, result invalidation and
replacement accounting. A newer edit, file/import/recovery/link load, dismissal,
or disposal fences a delayed shared load. Shared-link parsing never computes,
fetches metadata, writes storage, or sends a payload for remote validation.

Accepted loads and dismissals consume only a recognized share fragment with a
history replacement and no page reload. Ordinary anchors are preserved. Opening
the same explicit link later offers it again; rerenders of the current offer do
not reapply it. Links remain readable by anyone with the link, browser history
access or page-script access. They are not placed in query strings, logs or
analytics. Names and errors are rendered as text.
