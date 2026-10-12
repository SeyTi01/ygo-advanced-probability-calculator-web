// Set YGO_AUDIT_INPUT/OUTPUT and build Release before invoking this serial runner.
const { spawn, spawnSync } = require('node:child_process');
const windows = process.platform === 'win32';
const child = spawn('dotnet', ['test', 'YGOProbabilityCalculatorBlazor.sln', '-c', 'Release',
    '--no-build', '--filter', 'FullyQualifiedName~PerformanceAuditBenchmark', '--logger', 'console;verbosity=normal'],
{ stdio: 'inherit', detached: !windows, env: { ...process.env, DOTNET_TieredCompilation: '0' } });
let expired = false;
const deadline = setTimeout(() => {
    expired = true;
    console.error('Engine audit exceeded its three-minute process deadline; this run is censored.');
    if (windows) spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
    else process.kill(-child.pid, 'SIGKILL');
}, 180000);
child.on('error', error => { clearTimeout(deadline); console.error(error.message); process.exitCode = 1; });
child.on('exit', code => { clearTimeout(deadline); process.exitCode = expired ? 1 : code ?? 1; });
