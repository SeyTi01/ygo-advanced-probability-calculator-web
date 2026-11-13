# Release workflow checks

Walk through a new complete release and retries after each completed stage. Check notes-only/preparation-only limits; missing CLI PATH versus unavailable authentication; pending/failed checks and changed heads; an existing matching or mismatched tag; a draft or already-published Release; concurrent `main`/latest changes; and a missing/failed production deployment.

Verify that custom notes never gain an automatic changelog inventory, the predecessor never becomes the new release itself, and the weakest optional highlight is removed. Use read-only current state and hypothetical scenarios; do not create a real release to test instructions.
