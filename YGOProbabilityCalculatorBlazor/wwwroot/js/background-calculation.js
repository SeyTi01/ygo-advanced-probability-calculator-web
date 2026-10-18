// One component-owned runtime, leased to one request at a time. No inputs or
// results are cached. Only C# can acknowledge a successfully decoded response.
const idleMilliseconds = 60000;
const maximumSuccessfulRequests = 20;
const opportunityMilliseconds = 7000;
const upgradeMilliseconds = 5000;
// A shared cooldown also covers component replacement. Worker.terminate() has
// no web-platform completion acknowledgement; Chromium shutdown can lag.
let restartAfter = 0;

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
        clearTimeout(state.restartTimer);
        if (state.runningAutomatic) restartAfter = Math.max(restartAfter, performance.now() + 2500);
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
            clearTimeout(lease.deadlineTimer);
            lease.pending?.reject(new Error(message));
            lease.pending = undefined;
            lease.first?.reject(new Error(message));
            lease.first = undefined;
        }
    }

    function answer(lease, response) {
        clearTimeout(lease.deadlineTimer);
        lease.phase = 'answered';
        lease.state.runningAutomatic = false;
        lease.json = lease.fallback = undefined;
        const pending = lease.pending;
        lease.pending = undefined;
        pending.resolve(response);
        lease.first?.resolve({ response, isPreliminary: false });
        lease.first = undefined;
    }

    function annotated(response, outcome) {
        const parsed = JSON.parse(response);
        parsed.Result.Contract.AutomaticExact = outcome;
        return JSON.stringify(parsed);
    }

    function fallback(lease, outcome) {
        // Terminate before delivering the completed response. C# still owns the
        // detached lease until it validates and acknowledges (or discards) it.
        const response = annotated(lease.fallback, outcome);
        active = undefined;
        retire(lease.state);
        active = lease;
        lease.detached = true;
        answer(lease, response);
    }

    function deadline(lease) {
        if (active !== lease || lease.phase !== 'running') return;
        if (lease.fallback) fallback(lease, 1);
    }

    function arm(lease) {
        clearTimeout(lease.deadlineTimer);
        lease.deadlineTimer = setTimeout(() => deadline(lease), Math.max(0, lease.deadline - performance.now()));
    }

    function failed(state, message) {
        if (active?.state === state && active.phase === 'running' && active.fallback) fallback(active, 5);
        else retire(state, message);
    }

    function automaticResponse(lease, data) {
        if (lease.fallback && performance.now() >= lease.deadline) return deadline(lease);
        const response = JSON.parse(data.response);
        const contract = response.Result?.Contract;
        const success = response.Error == null && response.LimitReason == null && response.FailureKind == null
            && contract?.Version === 1 && contract.RequestedMode === 0 && contract.ExecutedMode === 0
            && contract.Completion === 0 && [0, 1, 2].includes(contract.Accuracy);
        if (lease.fallback) {
            if (success && contract.Accuracy === 0) answer(lease, annotated(data.response, 0));
            else fallback(lease, response.LimitReason === 1 ? 2 : response.LimitReason === 2 ? 3
                : response.FailureKind === 3 ? 4 : 5);
            return;
        }
        // A normal initial failure follows the existing typed-error path. Never
        // invent a fallback, and never repeat already proven/no-search work.
        if (!success || contract.Accuracy === 0) return answer(lease, data.response);
        // Initial Fast is never timed out automatically. Its elapsed time only
        // consumes the opportunity to do additional physical counting.
        if (performance.now() >= lease.deadline) return answer(lease, annotated(data.response, 6));
        if (typeof data.remainingWork !== 'string' || !/^[0-9]+$/.test(data.remainingWork)) {
            return retire(lease.state, 'Invalid automatic calculation Work allowance.');
        }
        lease.fallback = data.response;
        if (data.remainingWork === '0') return fallback(lease, 2);
        lease.deadline = Math.min(lease.deadline, performance.now() + upgradeMilliseconds);
        arm(lease);
        lease.state.worker.postMessage({ id: lease.id, json: lease.json, fastStage: 'upgrade', remainingWork: data.remainingWork });
        lease.json = undefined;
        // Only an actually dispatched upgrade has a presentation stage. The
        // terminal promise still owns the lease until C# validates and releases it.
        lease.first?.resolve({ response: data.response, isPreliminary: true });
        lease.first = undefined;
    }

    function send(lease) {
        if (!lease.state.ready || lease.phase !== 'running' || !lease.pending || lease.sent) return;
        lease.sent = true;
        const json = lease.pending.json;
        lease.pending.json = undefined;
        try {
            if (lease.automatic) {
                lease.json = json;
                lease.deadline = performance.now() + opportunityMilliseconds;
                lease.state.worker.postMessage({ id: lease.id, json, fastStage: 'initial' });
            } else lease.state.worker.postMessage({ id: lease.id, json });
        }
        catch { retire(lease.state, 'Could not send the background calculation.'); }
    }

    function start(state) {
        if (state.dead || disposed || current !== state) return;
        const delay = restartAfter - performance.now();
        if (delay > 0) {
            state.restartTimer = setTimeout(() => start(state), delay);
            return;
        }
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
                    failed(state, data.error || 'Background calculation failed.');
                } else if (state.ready && lease?.state === state && lease.phase === 'running'
                    && data?.id === lease.id && typeof data.response === 'string'
                    && Object.keys(data).length === (lease.automatic ? 3 : 2)) {
                    if (lease.automatic) {
                        try { automaticResponse(lease, data); }
                        catch { failed(state, 'Invalid automatic calculation response.'); }
                    } else answer(lease, data.response);
                } else {
                    failed(state, 'Background calculation returned an invalid response.');
                }
            };
            state.worker.onerror = event => {
                event.preventDefault();
                failed(state, 'Background calculation worker failed to load or run. Please try again.');
            };
            state.worker.onmessageerror = () => failed(state, 'Background calculation response could not be read.');
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
                runProgressive(json, automatic = false) {
                    if (lease.phase !== 'created') return Promise.reject(new Error('Calculation is already running or finished.'));
                    const first = new Promise((resolve, reject) => { lease.first = { resolve, reject }; });
                    lease.final = this.run(json, automatic);
                    // Cancellation/worker failure may precede C# requesting the
                    // final stage. Observe rejection now; finish still receives it.
                    lease.final.catch(() => {});
                    return first;
                },
                finish() {
                    if (!lease.final) return Promise.reject(new Error('No progressive calculation was started.'));
                    return lease.final;
                },
                run(json, automatic = false) {
                    if (lease.phase !== 'created') return Promise.reject(new Error(lease.error ||
                        (lease.phase === 'running' ? 'Calculation is already running.' : 'Calculation is already finished.')));
                    lease.phase = 'running';
                    lease.automatic = automatic === true;
                    lease.state.runningAutomatic = lease.automatic;
                    return new Promise((resolve, reject) => {
                        lease.pending = { json, resolve, reject };
                        send(lease);
                    });
                },
                release() {
                    if (lease.phase !== 'answered' || active !== lease || state.dead && !lease.detached) {
                        throw new Error(lease.error || 'Calculation has no successful response to release.');
                    }
                    lease.phase = 'released';
                    lease.final = undefined;
                    active = undefined;
                    if (lease.detached) return lease.id;
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
                    if (active === lease && lease.detached) active = undefined;
                    clearTimeout(lease.deadlineTimer);
                    lease.phase = 'disposed';
                    lease.pending = undefined;
                    lease.first = lease.final = undefined;
                    lease.json = lease.fallback = undefined;
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
            active = undefined;
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
