# Local browser verification and debugging

Apply [computer-use authorization](../../ygo-implement/references/computer-use.md). Confirm the task's localhost/loopback server, checkout and served revision before interacting with it.

All browser actions needed for verification or debugging are authorized without separate confirmation: navigation and UI interaction; console/log access and JavaScript execution; DOM, network and worker inspection; sending or replaying requests; breakpoints, profiling and automation; test uploads/downloads; and changing or resetting test storage, caches and service workers.

Keep state changes within task-owned test data. Remote side effects retain the underlying task's authorization requirements.
