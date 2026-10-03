export function createJob() {
    let worker;
    let pending;
    let ready = false;
    let finished = false;
    let startupError;
    let timer;
    const id = 1; // Each job owns a dedicated worker and exactly one request.

    function stop() {
        clearTimeout(timer);
        if (worker) {
            worker.onmessage = worker.onerror = worker.onmessageerror = null;
            worker.terminate();
            worker = undefined;
        }
    }
    function fail(message) {
        startupError = message;
        finished = true;
        stop();
        pending?.reject(new Error(message));
        pending = undefined;
    }
    function send() {
        if (ready && pending) {
            try { worker.postMessage({ id, json: pending.json }); }
            catch { fail('Could not send the background calculation.'); }
        }
    }

    try {
        worker = new Worker(new URL('./calculation-worker.js', import.meta.url), { type: 'module' });
        worker.onmessage = ({ data }) => {
            if (finished) return;
            if (data?.ready === true) {
                if (ready) return;
                ready = true;
                clearTimeout(timer);
                send();
            } else if (data?.error) {
                fail(data.error);
            } else if (data?.id === id && typeof data.response === 'string' && pending) {
                finished = true;
                stop();
                pending.resolve(data.response);
                pending = undefined;
            } else {
                fail('Background calculation returned an invalid response.');
            }
        };
        worker.onerror = event => {
            event.preventDefault();
            fail('Background calculation worker failed to load or run. Please try again.');
        };
        worker.onmessageerror = () => fail('Background calculation response could not be read.');
        timer = setTimeout(() => fail('Background calculation could not start within 30 seconds. Please try again.'), 30000);
    } catch {
        fail('Background calculation is unavailable in this browser.');
    }

    return {
        run(json) {
            if (finished) return Promise.reject(new Error(startupError || 'Calculation was cancelled.'));
            if (pending) return Promise.reject(new Error('Calculation is already running.'));
            return new Promise((resolve, reject) => {
                pending = { json, resolve, reject };
                send();
            });
        },
        dispose() { fail('Calculation was cancelled.'); }
    };
}
