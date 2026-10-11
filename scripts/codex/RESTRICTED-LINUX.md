# Restricted Linux / ChatGPT Work verification

Use these procedures when directed by [AGENTS.md](../../AGENTS.md).

### Restricted Linux and ChatGPT Work: .NET 10 SDK bootstrap

If the initial SDK check finds `dotnet` missing or no usable 10.x SDK, attempt this nonprivileged bootstrap:

```sh
mkdir -p /tmp/dotnet10-sdk
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
TAR_OPTIONS=--no-same-owner bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /tmp/dotnet10-sdk --no-path

export DOTNET_ROOT=/tmp/dotnet10-sdk
export PATH="$DOTNET_ROOT:$PATH"

dotnet --info
dotnet --list-sdks
```

The `TAR_OPTIONS` setting prevents GNU tar from restoring archive owners, which can fail in restricted filesystems. You may also invoke `/tmp/dotnet10-sdk/dotnet` directly. After confirming a 10.x SDK, run the normal restore, build, and test commands in [AGENTS.md](../../AGENTS.md#local-development-and-verification). If the bootstrap fails, report the exact failed command and error.

### Restricted Linux / ChatGPT Work: WebAssembly task-host failures

This is a separate build-time issue, unrelated to a missing SDK. After obtaining a usable .NET 10 SDK, run the normal restore, build, and test commands in [AGENTS.md](../../AGENTS.md#local-development-and-verification) first; a successful restore alone does not prove the app or tests build. If a build or test fails with `MSB4216`/`MSB4027` or `SocketException (13): Permission denied` while MSBuild starts an out-of-process task host, and a socket probe confirms Unix sockets are blocked while loopback TCP works, use the following verification-only workaround rather than declaring the suite un-runnable:

- Use writable temporary `DOTNET_CLI_HOME` and `NUGET_PACKAGES` locations.
- Disable MSBuild/node reuse and compiler/Razor build servers.
- Create a temporary MSBuild targets file **outside the repository** that re-registers the unchanged official WebAssembly and ILLink tasks with the default in-process task factory by using `<UsingTask ... AssemblyFile="..." Override="true" />`.
- For the current .NET 10 toolchain, the affected tasks are `GenerateWasmBootJson`, `ComputeWasmBuildAssets`, `ComputeWasmPublishAssets`, `ConvertDllsToWebCil`, `ComputeManagedAssemblies`, and `ILLink`. Resolve their DLL paths from the actually restored NuGet packages; do not hard-code an old package version.
- Pass the temporary file with `-p:CustomBeforeMicrosoftCommonTargets=<path>` to build/publish. This must only change task process placement; do not skip targets or replace task assemblies.
- After a successful build, run the normal full test suite with `--no-build`. For publish verification, inspect diagnostic logs to confirm the WebAssembly/ILLink tasks actually executed.
- Never commit the temporary override file or sandbox-specific paths. Cloudflare/local builds do not require this workaround.

The .NET 10 migration PR #46 contains one verified example of this workaround and the exact commands used. Prefer reproducing the technique with the currently restored SDK/package versions over copying its temporary paths verbatim.
