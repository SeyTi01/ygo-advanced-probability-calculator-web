// Explicit opt-in; terminate only this measurement process tree after two minutes.
const { spawn, spawnSync } = require('node:child_process');
const windows = process.platform === 'win32';
const child = spawn('dotnet', ['test', 'YGOProbabilityCalculatorBlazor.sln', '-c', 'Release',
 '--no-restore', '--filter', 'FullyQualifiedName~WorkPolicyBenchmark', '--logger', 'console;verbosity=detailed'],
 { stdio: 'inherit', detached: !windows, env: { ...process.env, DOTNET_TieredCompilation: '0' } });
let expired = false;
const deadline = setTimeout(() => {
 expired = true;
 console.error('Work-policy measurement exceeded its two-minute process deadline.');
 if (windows) spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
 else process.kill(-child.pid, 'SIGKILL');
}, 120000);
child.on('error', error => { clearTimeout(deadline); console.error(error.message); process.exitCode = 1; });
child.on('exit', code => { clearTimeout(deadline); process.exitCode = expired ? 1 : code ?? 1; });
