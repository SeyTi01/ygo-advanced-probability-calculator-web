// Research-only model of a sequential staged lease. No production imports.
// The worker probe measures the unchanged solvers and returns charged Work.
export function createResearchOwner(url) {
    let worker, ready, active, nextId = 0, successes = 0;
    let restartAfter = 0;
    const events = [];
    function terminate(reason) {
        if (!worker) return;
        events.push({kind: 'terminate', reason, at: performance.now()});
        worker.onmessage = worker.onerror = null;
        worker.terminate();
        worker = undefined;
        ready = undefined;
        successes = 0;
        restartAfter = performance.now() + 2500;
    }
    async function ensure() {
        if (ready) return ready;
        await new Promise(resolve => setTimeout(resolve, Math.max(0, restartAfter - performance.now())));
        const start = performance.now();
        worker = new Worker(url, {type: 'module'});
        events.push({kind: 'create', at: start});
        ready = new Promise((resolve, reject) => {
            const timer = setTimeout(() => { terminate('startup'); reject(Error('startup timeout')); }, 30000);
            worker.onerror = event => { clearTimeout(timer); reject(Error(event.message)); };
            worker.onmessage = ({data}) => {
                if (data.ready) { clearTimeout(timer); resolve(performance.now() - start); }
            };
        });
        return ready;
    }
    return {
        events,
        async run(input, mode) {
            if (active) throw Error('busy');
            active = true;
            const begin = performance.now();
            const cold = !worker;
            try {
                const bootstrap = await ensure();
                const sent = performance.now();
                const id = ++nextId;
                const stages = [];
                const automatic = mode === 'automatic' && input.Options?.Mode !== 1;
                let fallback, timer, upgradeStart;
                let deadline = automatic ? sent + 7000 : sent + 60000;
                return await new Promise(resolve => {
                    let settled = false;
                    function finish(outcome, result, forced = false) {
                        if (settled) return;
                        settled = true;
                        clearTimeout(timer);
                        const finished = performance.now();
                        if (forced) terminate(outcome);
                        else if (++successes >= 20 || outcome === 'failure') terminate('recycle');
                        resolve({outcome, result, stages, cold, bootstrap: cold ? bootstrap : 0,
                            totalMs: finished - begin, dispatchMs: finished - sent,
                            upgradeMs: upgradeStart === undefined ? null : finished - upgradeStart,
                            forced, at: finished});
                    }
                    function timeout() { finish(fallback ? 'fallback-timeout' : 'timeout-no-result', fallback?.Result ?? null, true); }
                    function arm() { clearTimeout(timer); timer = setTimeout(timeout, Math.max(0, deadline - performance.now())); }
                    worker.onerror = () => finish(fallback ? 'fallback-error' : 'failure', fallback?.Result ?? null, true);
                    worker.onmessage = ({data}) => {
                        if (settled || data.id !== id) return;
                        // Absolute time check also wins if a response task precedes a delayed timer.
                        if ((!automatic || fallback) && performance.now() >= deadline) return timeout();
                        if (data.error) return finish(fallback ? 'fallback-error' : 'failure', fallback?.Result ?? null, true);
                        const measurement = data.measurement;
                        stages.push(measurement);
                        if (!automatic || fallback) {
                            if (measurement.Outcome === 'Success') finish(fallback ? 'upgraded' : 'reference', measurement.Result);
                            else finish(fallback ? 'fallback-' + measurement.Outcome : 'failure', fallback?.Result ?? null, !!fallback);
                            return;
                        }
                        if (measurement.Outcome !== 'Success') return finish('failure', null);
                        if (measurement.Result.Contract.Accuracy === 0) return finish('already-exact', measurement.Result);
                        if (performance.now() >= deadline) return finish('no-time', measurement.Result);
                        fallback = measurement;
                        const remaining = input.WorkUnits - measurement.Work;
                        if (remaining <= 0) return finish('fallback-work', fallback.Result);
                        upgradeStart = performance.now();
                        deadline = Math.min(deadline, upgradeStart + 5000);
                        arm();
                        const upgraded = structuredClone(input);
                        upgraded.Options = {...upgraded.Options, Mode: 2};
                        upgraded.WorkUnits = remaining;
                        worker.postMessage({id, input: upgraded});
                    };
                    if (!automatic) arm();
                    const request = structuredClone(input);
                    if (mode === 'exact' && request.Options?.Mode !== 1) request.Options = {...request.Options, Mode: 2};
                    worker.postMessage({id, input: request});
                });
            } finally { active = false; }
        },
        dispose() { terminate('dispose'); }
    };
}
