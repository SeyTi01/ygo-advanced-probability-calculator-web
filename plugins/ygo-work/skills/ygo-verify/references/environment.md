# Choose the actual execution environment

For source-only issue, planning or review work, use the checkout or authenticated GitHub reads directly; skip runtime setup. For code/test work, inspect tools once, reuse valid initialization/results, and prepare only missing prerequisites. Determine runtime requirements from the working revision's project/build files. For .NET work, run `dotnet --info` and `dotnet --list-sdks` before substantial implementation or tests.

For publication, establish authentication early without printing credentials. Public fetches and GitHub connector access do not prove CLI push access. Missing CLI credentials do not block local work; use [API publication](../../ygo-pr/references/api-publication.md) rather than repeatedly attempting login.

Set variables in the active shell: `$env:NAME = 'value'` in PowerShell; `export NAME='value'` in POSIX shells.

Read only the applicable host branch:

| Actual host | Instructions |
| --- | --- |
| This repository's initialized/restricted Windows Codex setup | [Windows Codex](environment-windows.md) |
| Web/cloud, usually Ubuntu or restricted Linux, without automatic setup | [Web/cloud](environment-cloud.md) |
| Another host | Use the common rules below; initialize only missing prerequisites. |

## Other hosts and common failures

Detect actual OS, tools, checkout, authentication and permission model. Reuse existing setup; do not import Windows approvals or Linux workarounds just because a task runs in Codex. Use a reasonable supported nonprivileged SDK bootstrap where needed.

For Git ownership errors use command-scoped `-c safe.directory=<repo>`, never global configuration. For guards invoking Git, use the equivalent `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0` and `GIT_CONFIG_VALUE_0` environment settings without overwriting unrelated entries. Retry MSBuild with `-m:1` only after parallel-node/reuse errors. Report exact failed commands/errors; a missing runtime blocks its checks, not independent source work or an available publication path.
