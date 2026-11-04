export function installFakeWorker(t) {
    const previous = Object.getOwnPropertyDescriptor(globalThis, 'Worker');
    const workers = [];
    globalThis.Worker = class {
        sent = [];
        terminated = 0;
        constructor() { workers.push(this); }
        postMessage(message) { this.sent.push(message); }
        terminate() { this.terminated++; }
        emit(data) { this.onmessage?.({ data }); }
    };
    t.after(() => {
        if (previous) Object.defineProperty(globalThis, 'Worker', previous);
        else delete globalThis.Worker;
    });
    return workers;
}
