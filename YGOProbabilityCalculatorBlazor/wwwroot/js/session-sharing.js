// No payload is logged, fetched or persisted here. Denial always leaves a manual fallback.
globalThis.sessionSharing = {
    async copy(text) {
        try {
            if (!globalThis.isSecureContext || !globalThis.navigator?.clipboard?.writeText) return false;
            await globalThis.navigator.clipboard.writeText(text);
            return true;
        } catch { return false; }
    },
    select(id) {
        const element = document.getElementById(id);
        element?.focus();
        element?.select();
    }
};
