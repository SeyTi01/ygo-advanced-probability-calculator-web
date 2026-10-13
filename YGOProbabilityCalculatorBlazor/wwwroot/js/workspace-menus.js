(() => {
    // Let Blazor handle suggestion selection without scrolling the page with arrow keys.
    document.addEventListener('keydown', event => {
        if (event.target.matches?.('.card-name-field input[aria-expanded="true"]')
            && ['ArrowDown', 'ArrowUp'].includes(event.key)) event.preventDefault();
    });
    window.workspaceSearch = {
        reveal(id) { if (id) document.getElementById(id)?.scrollIntoView({ block: 'nearest' }); }
    };
    const cleanups = new WeakMap();
    const dispose = panel => { cleanups.get(panel)?.(); cleanups.delete(panel); };
    window.workspaceMenus = {
        open(panel, trigger, receiver) {
            dispose(panel);
            const position = () => {
                const anchor = trigger.getBoundingClientRect();
                const box = panel.getBoundingClientRect();
                panel.style.left = `${Math.max(8, Math.min(anchor.right - box.width, innerWidth - box.width - 8))}px`;
                panel.style.top = `${Math.max(8, anchor.bottom + box.height + 8 <= innerHeight ? anchor.bottom + 4 : anchor.top - box.height - 4)}px`;
            };
            const dismiss = () => {
                dispose(panel);
                receiver.invokeMethodAsync('Dismiss');
            };
            const outside = event => {
                if (!panel.contains(event.target) && !trigger.contains(event.target)) dismiss();
            };
            const keydown = event => {
                if (event.key === 'Escape') {
                    event.preventDefault();
                    event.stopPropagation();
                    trigger.focus({ preventScroll: true });
                    dismiss();
                }
                if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
                    const items = [...panel.querySelectorAll('button:not(:disabled), input:not(:disabled)')];
                    if (!items.length) return;
                    event.preventDefault();
                    const current = items.indexOf(document.activeElement);
                    const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
                        : (current + (event.key === 'ArrowUp' ? items.length - 1 : 1)) % items.length;
                    items[next].focus();
                }
            };
            position();
            panel.querySelector('button:not(:disabled), input:not(:disabled)')?.focus({ preventScroll: true });
            document.addEventListener('pointerdown', outside, true);
            document.addEventListener('focusin', outside, true);
            panel.addEventListener('keydown', keydown);
            window.addEventListener('resize', position);
            window.addEventListener('scroll', position, true);
            cleanups.set(panel, () => {
                document.removeEventListener('pointerdown', outside, true);
                document.removeEventListener('focusin', outside, true);
                panel.removeEventListener('keydown', keydown);
                window.removeEventListener('resize', position);
                window.removeEventListener('scroll', position, true);
            });
        },
        close(panel, trigger, removing) {
            dispose(panel);
            let target = trigger;
            if (removing) {
                const row = trigger.closest('.accordion-item');
                target = row?.nextElementSibling?.querySelector('.action-menu-trigger')
                    ?? row?.previousElementSibling?.querySelector('.action-menu-trigger')
                    ?? (row?.classList.contains('card-editor') ? document.querySelector('#quickCardName') : document.querySelector('#addCombo'));
            }
            target?.focus({ preventScroll: true });
        },
        dispose
    };
})();
