// One component-owned runtime, leased to one request at a time. No inputs or
// results are cached. Only C# can acknowledge a successfully decoded response.
const idleMilliseconds = 60000;
const maximumSuccessfulRequests = 20;

export function createOwner() {
    let current;
    let active;
    let nextId = 0;
    let disposed = false;

    function retire(state, message = 'Calculation was cancelled.') {
        if (!state || state.dead) return;
        state.dead = true;
        clearTimeout(state.startupTimer);
        clearTimeout(state.idleTimer);
        if (state.worker) {
            state.worker.onmessage = state.worker.onerror = state.worker.onmessageerror = null;
            state.worker.terminate();
            state.worker = undefined;
        }
        if (current === state) current = undefined;
        if (active?.state === state) {
            const lease = active;
            active = undefined;
            lease.error = message;
            lease.phase = 'failed';
            lease.pending?.reject(new Error(message));
            lease.pending = undefined;
        }
    }

    function send(lease) {
        if (!lease.state.ready || lease.phase !== 'running' || !lease.pending || lease.sent) return;
        lease.sent = true;
        const json = lease.pending.json;
        lease.pending.json = undefined;
        try { lease.state.worker.postMessage({ id: lease.id, json }); }
        catch { retire(lease.state, 'Could not send the background calculation.'); }
    }

    function start(state) {
        try {
            state.worker = new Worker(new URL('./calculation-worker.js', import.meta.url), { type: 'module' });
            state.worker.onmessage = ({ data }) => {
                if (state.dead || current !== state) return;
                if (data?.ready === true && Object.keys(data).length === 1) {
                    if (state.ready) return;
                    state.ready = true;
                    clearTimeout(state.startupTimer);
                    if (active) send(active);
                    return;
                }
                const lease = active;
                const startupFailure = !state.ready && typeof data?.error === 'string'
                    && Object.keys(data).length === 1;
                const requestFailure = state.ready && lease?.state === state && data?.id === lease.id
                    && typeof data.error === 'string' && Object.keys(data).length === 2;
                if (startupFailure || requestFailure) {
                    retire(state, data.error || 'Background calculation failed.');
                } else if (state.ready && lease?.state === state && lease.phase === 'running'
                    && data?.id === lease.id && typeof data.response === 'string' && Object.keys(data).length === 2) {
                    lease.phase = 'answered';
                    const pending = lease.pending;
                    lease.pending = undefined;
                    pending.resolve(data.response);
                } else {
                    retire(state, 'Background calculation returned an invalid response.');
                }
            };
            state.worker.onerror = event => {
                event.preventDefault();
                retire(state, 'Background calculation worker failed to load or run. Please try again.');
            };
            state.worker.onmessageerror = () => retire(state, 'Background calculation response could not be read.');
            state.startupTimer = setTimeout(() => {
                if (!state.ready) retire(state, 'Background calculation could not start within 30 seconds. Please try again.');
            }, 30000);
        } catch {
            retire(state, 'Background calculation is unavailable in this browser.');
        }
    }

    return {
        createJob() {
            if (disposed) throw new Error('Background calculator was disposed.');
            if (active) throw new Error('Calculation is already running.');
            const fresh = !current;
            const state = current ??= { ready: false, dead: false, successes: 0 };
            clearTimeout(state.idleTimer);
            const lease = { state, id: ++nextId, phase: 'created' };
            state.lastId = lease.id;
            active = lease;
            if (fresh) start(state);

            return {
                run(json) {
                    if (lease.phase !== 'created') return Promise.reject(new Error(lease.error ||
                        (lease.phase === 'running' ? 'Calculation is already running.' : 'Calculation is already finished.')));
                    lease.phase = 'running';
                    return new Promise((resolve, reject) => {
                        lease.pending = { json, resolve, reject };
                        send(lease);
                    });
                },
                release() {
                    if (lease.phase !== 'answered' || active !== lease || state.dead) {
                        throw new Error(lease.error || 'Calculation has no successful response to release.');
                    }
                    lease.phase = 'released';
                    active = undefined;
                    if (++state.successes >= maximumSuccessfulRequests) {
                        retire(state);
                    } else {
                        const idleId = lease.id;
                        state.idleTimer = setTimeout(() => {
                            if (!active && state.lastId === idleId) retire(state);
                        }, idleMilliseconds);
                    }
                    return lease.id;
                },
                dispose() {
                    // A late request cleanup must never terminate a later lease,
                    // even if that lease has already returned this runtime to idle.
                    if (current === state && state.lastId === lease.id) retire(state);
                    lease.phase = 'disposed';
                    lease.pending = undefined;
                }
            };
        },
        discard(id) {
            if (current?.lastId === id) retire(current);
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            retire(current);
        }
    };
}

// Isolated one-shot jobs remain useful for wire/parity probes. Production uses
// createOwner; this helper deliberately retains nothing after a response.
export function createJob() {
    const owner = createOwner();
    const job = owner.createJob();
    let started = false;
    return {
        async run(json) {
            if (started) throw new Error('Calculation is already running or finished.');
            started = true;
            try { return await job.run(json); }
            finally { owner.dispose(); }
        },
        dispose() { owner.dispose(); }
    };
}
