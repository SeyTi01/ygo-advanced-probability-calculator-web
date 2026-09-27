(() => {
    "use strict";

    const storageKey = "ygo-advanced-probability-calculator.theme";
    const validPreferences = new Set(["system", "light", "dark"]);
    let systemPreferenceQuery = null;

    try {
        systemPreferenceQuery = window.matchMedia?.("(prefers-color-scheme: dark)") ?? null;
    }
    catch {
        // Browsers that cannot expose the system preference default to light mode.
    }

    const normalizePreference = value => validPreferences.has(value) ? value : "system";

    const readPreference = () => {
        let savedPreference;
        try {
            savedPreference = window.localStorage.getItem(storageKey);
        }
        catch {
            return "system";
        }

        const preference = normalizePreference(savedPreference);
        if (savedPreference !== null && preference === "system") {
            try {
                window.localStorage.removeItem(storageKey);
            }
            catch {
                // Invalid entries are ignored when storage is read-only.
            }
        }
        return preference;
    };

    let preference = readPreference();

    const applyTheme = () => {
        const systemPrefersDark = systemPreferenceQuery?.matches === true;
        const theme = preference === "system"
            ? (systemPrefersDark ? "dark" : "light")
            : preference;

        document.documentElement.setAttribute("data-bs-theme", theme);
    };

    const setPreference = value => {
        preference = normalizePreference(value);
        try {
            if (preference === "system") {
                window.localStorage.removeItem(storageKey);
            }
            else {
                window.localStorage.setItem(storageKey, preference);
            }
        }
        catch {
            // Keep the current page usable when local storage is blocked.
        }
        applyTheme();
    };

    const updateSystemTheme = () => {
        if (preference === "system") applyTheme();
    };

    if (systemPreferenceQuery?.addEventListener) {
        systemPreferenceQuery.addEventListener("change", updateSystemTheme);
    }
    else if (systemPreferenceQuery?.addListener) {
        systemPreferenceQuery.addListener(updateSystemTheme);
    }

    // Runs before stylesheets load to avoid a light-theme flash on first paint.
    applyTheme();
    window.ygoTheme = Object.freeze({
        getPreference: () => preference,
        setPreference
    });
})();
