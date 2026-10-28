let listeners;

export function watchTopics() {
    stopWatchingTopics();
    listeners = new AbortController();
    const options = { signal: listeners.signal };
    const revealCurrent = () => revealTopic(location.hash);
    window.addEventListener('hashchange', revealCurrent, options);
    window.addEventListener('popstate', revealCurrent, options);
    document.addEventListener('click', event => {
        const link = event.target.closest?.('a[href]');
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
            || link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
        const target = new URL(link.href);
        if (target.origin === location.origin && target.pathname === location.pathname && target.search === location.search) {
            revealTopic(target.hash);
        }
    }, options);
}

export function stopWatchingTopics() {
    listeners?.abort();
    listeners = undefined;
}

export function revealTopic(fragment) {
    let id;
    try { id = decodeURIComponent(fragment.slice(1)); }
    catch { return; }
    const topic = document.getElementById(id);
    if (!topic?.matches('details.help-topic')) return;
    topic.open = true;
    topic.scrollIntoView({ block: 'start' });
}
