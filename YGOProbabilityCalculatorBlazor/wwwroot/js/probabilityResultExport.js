export async function copyText(text) {
    try {
        if (typeof navigator === "undefined" || typeof navigator.clipboard?.writeText !== "function") {
            return false;
        }

        await navigator.clipboard.writeText(text);
        return true;
    }
    catch {
        return false;
    }
}

export function focusElementById(elementId) {
    document.getElementById(elementId)?.focus();
}
