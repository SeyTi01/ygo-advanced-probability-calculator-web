import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import vm from "node:vm";

const storageKey = "ygo-advanced-probability-calculator.theme";
const themeScript = readFileSync(
    new URL("../../YGOProbabilityCalculatorBlazor/wwwroot/js/theme.js", import.meta.url),
    "utf8");

function createThemeRuntime({ savedPreference = null, prefersDark = false, storageBlocked = false, matchMediaAvailable = true } = {}) {
    const values = new Map();
    if (savedPreference !== null) values.set(storageKey, savedPreference);

    const storage = {
        getItem(key) {
            if (storageBlocked) throw new Error("Storage is blocked");
            return values.has(key) ? values.get(key) : null;
        },
        setItem(key, value) {
            if (storageBlocked) throw new Error("Storage is blocked");
            values.set(key, String(value));
        },
        removeItem(key) {
            if (storageBlocked) throw new Error("Storage is blocked");
            values.delete(key);
        }
    };

    const mediaListeners = new Set();
    const mediaQuery = {
        matches: prefersDark,
        addEventListener(_name, listener) {
            mediaListeners.add(listener);
        },
        change(matches) {
            this.matches = matches;
            for (const listener of mediaListeners) listener({ matches });
        }
    };

    const documentElement = {
        dataset: {},
        setAttribute(name, value) {
            if (name === "data-bs-theme") this.dataset.bsTheme = value;
        }
    };

    const fakeWindow = {
        localStorage: storage,
        matchMedia: matchMediaAvailable ? () => mediaQuery : undefined
    };

    vm.runInNewContext(themeScript, {
        window: fakeWindow,
        document: { documentElement }
    }, { filename: "theme.js" });

    return { api: fakeWindow.ygoTheme, documentElement, mediaQuery, values };
}

test("uses the operating system preference when no choice is saved", () => {
    const runtime = createThemeRuntime({ prefersDark: true });

    assert.equal(runtime.api.getPreference(), "system");
    assert.equal(runtime.documentElement.dataset.bsTheme, "dark");

    runtime.mediaQuery.change(false);
    assert.equal(runtime.documentElement.dataset.bsTheme, "light");
});

test("restores a valid saved theme before the page styles load", () => {
    const runtime = createThemeRuntime({ savedPreference: "dark" });

    assert.equal(runtime.api.getPreference(), "dark");
    assert.equal(runtime.documentElement.dataset.bsTheme, "dark");
});

test("ignores malformed saved values and returns to system mode", () => {
    const runtime = createThemeRuntime({ savedPreference: "sepia", prefersDark: true });

    assert.equal(runtime.api.getPreference(), "system");
    assert.equal(runtime.documentElement.dataset.bsTheme, "dark");
    assert.equal(runtime.values.has(storageKey), false);
});

test("persists explicit choices and removes the override when system mode is selected", () => {
    const runtime = createThemeRuntime({ prefersDark: true });

    runtime.api.setPreference("light");
    assert.equal(runtime.values.get(storageKey), "light");
    assert.equal(runtime.documentElement.dataset.bsTheme, "light");

    runtime.api.setPreference("system");
    assert.equal(runtime.values.has(storageKey), false);
    assert.equal(runtime.documentElement.dataset.bsTheme, "dark");

    runtime.mediaQuery.change(false);
    assert.equal(runtime.documentElement.dataset.bsTheme, "light");
});

test("does not fail when storage is blocked", () => {
    const runtime = createThemeRuntime({ storageBlocked: true, prefersDark: true });

    assert.equal(runtime.api.getPreference(), "system");
    runtime.api.setPreference("dark");
    assert.equal(runtime.documentElement.dataset.bsTheme, "dark");
});

test("defaults to light if system preference detection is unavailable", () => {
    const runtime = createThemeRuntime({ matchMediaAvailable: false });

    assert.equal(runtime.documentElement.dataset.bsTheme, "light");
});
