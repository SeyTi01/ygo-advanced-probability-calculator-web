// Website preference and a tab-local handoff. Never writes session recovery.
(function (root) {
    const preferenceKey = 'ygo-calculator:language:v1';
    const handoffKey = 'ygo-calculator:language-handoff:v1';
    const maxLength = 2 * 1024 * 1024;
    let locales = [{ id: 'en', name: 'English' }];
    let locale = 'en';
    let preference = 'system';
    let handoff = null;
    const matchLocale = (candidate, available = locales) => {
        if (typeof candidate !== 'string') return null;
        let tag = candidate.trim().toLowerCase();
        while (tag) {
            const found = available.find(item => item.id.toLowerCase() === tag);
            if (found) return found.id;
            tag = tag.includes('-') ? tag.slice(0, tag.lastIndexOf('-')) : '';
        }
        return null;
    };
    const resolve = (selection, languages, available = locales) =>
        [selection, ...(languages || [])].map(candidate => matchLocale(candidate, available)).find(Boolean) ?? 'en';
    const read = () => {
        try { return root.localStorage.getItem(preferenceKey); } catch { return null; }
    };
    const checkedWrite = (storage, key, value) => {
        storage.setItem(key, value);
        if (storage.getItem(key) !== value) throw new Error('Storage did not retain the language change.');
    };
    const checkedRemove = (storage, key) => {
        storage.removeItem(key);
        if (storage.getItem(key) !== null) throw new Error('Storage did not retain the language change.');
    };
    root.document?.documentElement.classList.add('locale-pending');
    const ready = root.fetch ? root.fetch('localization/locales.json', { cache: 'no-cache' })
        .then(response => { if (!response.ok) throw new Error(); return response.json(); })
        .then(value => {
            if (!Array.isArray(value) || !value.some(item => item.id === 'en')
                || value.some(item => !/^[a-z]{2,3}(-[a-z0-9]{2,8})*$/i.test(item.id) || typeof item.name !== 'string')) throw new Error();
            locales = value;
        }).catch(() => {}) : Promise.resolve();
    const initialized = ready.then(() => {
        preference = matchLocale(read()) ?? 'system';
        locale = resolve(preference, root.navigator?.languages);
        if (locale === 'en') root.document?.documentElement.classList.remove('locale-pending');
        try {
            const raw = root.sessionStorage.getItem(handoffKey);
            if (raw) {
                const record = JSON.parse(raw);
                const reload = root.performance?.getEntriesByType('navigation')[0]?.type === 'reload';
                const eligible = reload && !record.claimed && record.version === 2 && record.url === root.location.href
                    && Date.now() >= record.created && Date.now() - record.created < 120000 && record.locale === locale && raw.length <= maxLength
                    && typeof record.id === 'string' && (record.workspace === null || typeof record.workspace === 'string');
                record.claimed = true;
                checkedWrite(root.sessionStorage, handoffKey, JSON.stringify(record));
                if (eligible) {
                    handoff = record;
                    root.document?.documentElement.classList.add('locale-pending');
                }
                // Unrelated visits never replay a record; leave it recoverable in tab storage.
            }
        } catch { /* Disabled storage must not block ordinary startup. */ }
        return locale;
    });
    root.ygoLocale = {
        resolve,
        ready: initialized,
        getStartup: () => ({ locale, numericCulture: root.navigator?.languages?.[0] || root.navigator?.language || 'en-US' }),
        getPreference: () => preference,
        getHandoff: () => handoff,
        hasHandoff() {
            try { return root.sessionStorage.getItem(handoffKey) !== null; } catch { return false; }
        },
        show() {
            root.document.documentElement.lang = locale;
            root.document.documentElement.classList.remove('locale-pending');
        },
        downloadHandoff() {
            const raw = root.sessionStorage.getItem(handoffKey);
            if (!raw) return;
            let payload = raw;
            let filename = 'language-workspace-checkpoint.json';
            try {
                const session = JSON.parse(JSON.parse(raw).workspace).Session;
                if (typeof session === 'string') { payload = session; filename = 'language-workspace-session.json'; }
            } catch { /* Keep malformed records available for diagnosis too. */ }
            const url = URL.createObjectURL(new Blob([payload], { type: 'application/json' }));
            const link = root.document.createElement('a');
            link.href = url; link.download = filename; link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        complete(id) {
            if (!handoff || handoff.id !== id) throw new Error('The language handoff changed.');
            const record = JSON.parse(root.sessionStorage.getItem(handoffKey));
            if (record.id !== id) throw new Error('The language handoff changed.');
            root.sessionStorage.removeItem(handoffKey);
            if (root.sessionStorage.getItem(handoffKey) !== null) throw new Error('The restored checkpoint could not be removed.');
            handoff = null;
        },
        hasPendingEdits() {
            const workspace = root.document.querySelector('.calculator-workspace');
            if (!workspace) return false;
            if (workspace.querySelector('[data-language-edit="true"], .simulation-editor, .category-rule-editor, .category-rename-input, .ydke-import-form')) return true;
            const drafts = workspace.querySelectorAll('#quickCardName, .category-management .editor-add-row input, .combo-group-management .editor-add-row input, select[id^="cardCategory"]');
            const numbers = workspace.querySelectorAll('#handSize, input[id^="cardCopies"]');
            return [...drafts].some(input => input.value.length > 0)
                || [...numbers].some(input => input.value === '' || !input.validity.valid);
        },
        change(next, workspace) {
            if (root.ygoLocale.hasPendingEdits()) throw new Error('Save/apply or cancel the unfinished edit before changing language.');
            if (workspace !== null && (typeof workspace !== 'string' || workspace.length === 0)) throw new Error('The accepted workspace could not be captured.');
            if (next !== 'system' && !locales.some(item => item.id === next)) throw new Error('This language is unavailable.');
            const previous = read();
            if (root.sessionStorage.getItem(handoffKey) !== null) throw new Error('A previous language handoff is still saved. Return to that workspace or save your work before clearing it.');
            const record = JSON.stringify({ version: 2, id: root.crypto.randomUUID(), created: Date.now(),
                url: root.location.href, locale: resolve(next, root.navigator?.languages), workspace,
                recovery: root.sessionRecovery?.captureLanguage() ?? null });
            if (record.length > maxLength) throw new Error('This workspace is too large for a language reload. Save your work first.');
            checkedWrite(root.sessionStorage, handoffKey, record);
            try {
                if (next === 'system') checkedRemove(root.localStorage, preferenceKey);
                else checkedWrite(root.localStorage, preferenceKey, next);
            }
            catch (error) {
                root.sessionStorage.removeItem(handoffKey);
                try {
                    if (previous === null) root.localStorage.removeItem(preferenceKey);
                    else root.localStorage.setItem(preferenceKey, previous);
                } catch { /* Live workspace is retained. */ }
                throw error;
            }
            preference = next;
            root.location.reload();
        },
    };
})(globalThis);
