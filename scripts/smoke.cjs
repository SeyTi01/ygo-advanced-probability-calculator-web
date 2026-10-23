// Existing external Playwright installation; no package installation or new runner.
// node scripts/smoke.cjs <clean-optimized-published-wwwroot>
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');
assert(process.argv[2], 'Provide a clean optimized published wwwroot directory');
for (const script of ['background-calculation/integration-smoke.cjs', 'workspace/browser-smoke.cjs', 'workspace/session-loading-smoke.cjs']) {
    const result = spawnSync(process.execPath, [path.join(__dirname, script), path.resolve(process.argv[2])], { stdio: 'inherit' });
    if (result.error) throw result.error;
    if (result.status !== 0) process.exit(result.status ?? 1);
}
