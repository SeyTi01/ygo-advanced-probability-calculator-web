// Delegation also covers editors that Blazor inserts or replaces after a click.
let active;
let closeTimer;

function hide() {
    clearTimeout(closeTimer);
    active?.bubble.remove();
    active = undefined;
}

function triggerFor(target) {
    return target instanceof Element ? target.closest('[data-operator-hint]') : null;
}

function show(trigger, touch = false) {
    clearTimeout(closeTimer);
    if (active?.trigger === trigger) {
        active.touch ||= touch;
        return;
    }
    hide();
    const source = document.getElementById(trigger.dataset.operatorHint);
    if (!source) return;
    // Keep the described-by source in place; the visual copy escapes clipped rows.
    const bubble = source.cloneNode(true);
    bubble.removeAttribute('id');
    bubble.removeAttribute('role');
    bubble.setAttribute('aria-hidden', 'true');
    bubble.hidden = false;
    document.body.append(bubble);
    active = { trigger, bubble, touch };
    position();
    bubble.addEventListener('pointerenter', () => clearTimeout(closeTimer));
    bubble.addEventListener('pointerleave', scheduleHide);
}

function position() {
    if (!active) return;
    const { trigger, bubble } = active;
    if (!trigger.isConnected) {
        hide();
        return;
    }
    const anchor = trigger.getBoundingClientRect();
    const bounds = bubble.getBoundingClientRect();
    const left = Math.max(8, Math.min(anchor.left + (anchor.width - bounds.width) / 2, window.innerWidth - bounds.width - 8));
    const below = anchor.bottom + 6;
    const top = below + bounds.height <= window.innerHeight - 8 ? below : Math.max(8, anchor.top - bounds.height - 6);
    bubble.style.left = `${left}px`;
    bubble.style.top = `${top}px`;
}

function scheduleHide() {
    if (active?.touch || active?.trigger === document.activeElement) return;
    closeTimer = setTimeout(hide, 150);
}

document.addEventListener('pointerover', event => {
    if (event.pointerType === 'touch') return;
    const trigger = triggerFor(event.target);
    if (trigger) show(trigger);
});
document.addEventListener('pointerout', event => {
    if (triggerFor(event.target) === active?.trigger) scheduleHide();
});
document.addEventListener('pointerdown', event => {
    const trigger = triggerFor(event.target);
    if (trigger) show(trigger, event.pointerType === 'touch');
    else if (!active?.bubble.contains(event.target)) hide();
}, true);
document.addEventListener('focusin', event => {
    const trigger = triggerFor(event.target);
    if (trigger) show(trigger);
    else if (!active?.touch) hide();
});
document.addEventListener('focusout', () => {
    if (!active?.touch) closeTimer = setTimeout(hide, 0);
});
document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && active) {
        hide();
        event.stopPropagation();
        event.preventDefault();
    } else if (event.key === 'Tab') {
        hide();
    }
}, true);
document.addEventListener('change', event => {
    if (triggerFor(event.target) !== active?.trigger) return;
    const trigger = active.trigger;
    const touch = active.touch;
    hide();
    requestAnimationFrame(() => show(trigger, touch));
});
document.addEventListener('scroll', position, true);
window.addEventListener('resize', hide);
