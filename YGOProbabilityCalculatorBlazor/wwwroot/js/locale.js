// Website preference and a tab-local handoff. Never writes session recovery.
(function (root) {
    const preferenceKey = 'ygo-calculator:language:v1';
    const handoffKey = 'ygo-calculator:language-handoff:v1';
    const maxLength = 2 * 1024 * 1024;
    let locales = [{ id: 'en', name: 'English' }];
    let locale = 'en';
    let handoff = null;
    const resolve = (preference, languages, available = locales) => {
        for (const candidate of [preference, ...(languages || [])]) {
            if (typeof candidate !== 'string') continue;
            let tag = candidate.trim().toLowerCase();
            while (tag) {
                const found = available.find(item => item.id.toLowerCase() === tag);
                if (found) return found.id;
                tag = tag.includes('-') ? tag.slice(0, tag.lastIndexOf('-')) : '';
            }
        }
        return 'en';
    };
    const read = () => {
        try { return root.localStorage.getItem(preferenceKey); } catch { return null; }
    };
    const checkedWrite = (storage, key, value) => {
        storage.setItem(key, value);
        if (storage.getItem(key) !== value) throw new Error('Storage did not retain the language change.');
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
        locale = resolve(read(), root.navigator?.languages);
        if (locale === 'en') root.document?.documentElement.classList.remove('locale-pending');
        try {
            const raw = root.sessionStorage.getItem(handoffKey);
            if (raw) {
                const record = JSON.parse(raw);
                const reload = root.performance?.getEntriesByType('navigation')[0]?.type === 'reload';
                const eligible = reload && !record.claimed && record.version === 1 && record.url === root.location.href
                    && Date.now() >= record.created && Date.now() - record.created < 120000 && record.locale === locale && raw.length <= maxLength
                    && typeof record.id === 'string' && record.states && typeof record.states === 'object'
                    && !Array.isArray(record.states) && Object.values(record.states).every(value => typeof value === 'string');
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
            const url = URL.createObjectURL(new Blob([raw], { type: 'application/json' }));
            const link = root.document.createElement('a');
            link.href = url; link.download = 'language-workspace-handoff.json'; link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        complete(id) {
            if (!handoff || handoff.id !== id) throw new Error('The language handoff changed.');
            const record = JSON.parse(root.sessionStorage.getItem(handoffKey));
            if (record.id !== id) throw new Error('The language handoff changed.');
            root.sessionStorage.removeItem(handoffKey);
            handoff = null;
        },
        change(next, states, presentation) {
            if (!locales.some(item => item.id === next)) throw new Error('This language is unavailable.');
            const previous = read();
            if (root.sessionStorage.getItem(handoffKey) !== null) throw new Error('A previous language handoff is still saved. Return to that workspace or save your work before clearing it.');
            const record = JSON.stringify({ version: 1, id: root.crypto.randomUUID(), created: Date.now(),
                url: root.location.href, locale: next, states, presentation,
                recovery: root.sessionRecovery?.captureLanguage() ?? null });
            if (record.length > maxLength) throw new Error('This workspace is too large for a language reload. Save your work first.');
            checkedWrite(root.sessionStorage, handoffKey, record);
            try { checkedWrite(root.localStorage, preferenceKey, next); }
            catch (error) {
                root.sessionStorage.removeItem(handoffKey);
                try {
                    if (previous === null) root.localStorage.removeItem(preferenceKey);
                    else root.localStorage.setItem(preferenceKey, previous);
                } catch { /* Live workspace is retained. */ }
                throw error;
            }
            root.location.reload();
        },
        capturePresentation() {
            const workspace = root.document.querySelector('.calculator-workspace');
            if (!workspace) return null;
            return {
                details: [...workspace.querySelectorAll('details')].map(item => item.open),
                controls: [...workspace.querySelectorAll('input:not([type=file]):not([readonly]),select,textarea:not([readonly])')]
                    .map(item => ({ tag: item.tagName, type: item.type, value: item.value, checked: item.checked }))
            };
        },
        restorePresentation(saved) {
            if (!saved) return;
            const workspace = root.document.querySelector('.calculator-workspace');
            const details = [...workspace.querySelectorAll('details')];
            const controls = [...workspace.querySelectorAll('input:not([type=file]):not([readonly]),select,textarea:not([readonly])')];
            if (details.length !== saved.details.length || controls.length !== saved.controls.length
                || controls.some((item, index) => item.tagName !== saved.controls[index].tag || item.type !== saved.controls[index].type)) {
                throw new Error('Editor layout changed. Your language handoff is kept; do not clear browser data.');
            }
            details.forEach((item, index) => { item.open = saved.details[index]; });
            // Binding ownership is restored by components. Never dispatch events that commit a form.
            controls.forEach((item, index) => { item.value = saved.controls[index].value; item.checked = saved.controls[index].checked; });
        }
    };
})(globalThis);
