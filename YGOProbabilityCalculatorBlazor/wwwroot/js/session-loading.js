// Yield one paint opportunity before synchronous WebAssembly restoration work.
// The fallback also settles in inactive tabs, where animation frames may stop.
window.sessionLoading = {
    beforeWork: () => new Promise(resolve => {
        let frame;
        let nextTask;
        const finish = () => {
            cancelAnimationFrame(frame);
            clearTimeout(nextTask);
            clearTimeout(fallback);
            resolve();
        };
        const fallback = setTimeout(finish, 100);
        frame = requestAnimationFrame(() => { nextTask = setTimeout(finish, 0); });
    })
};
