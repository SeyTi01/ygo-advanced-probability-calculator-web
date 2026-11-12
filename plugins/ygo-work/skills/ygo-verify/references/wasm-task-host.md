# Restricted Linux WebAssembly task-host failure

Use only after a usable SDK is installed and normal build/test fails while MSBuild starts an out-of-process task host with `MSB4216`, `MSB4027` or `SocketException (13): Permission denied`, **and** a socket probe confirms Unix sockets are blocked while loopback TCP works. A successful restore alone does not prove build/test viability; a missing SDK is a separate problem.

Apply this verification-only workaround:

1. Use writable temporary `DOTNET_CLI_HOME` and `NUGET_PACKAGES` locations. Disable MSBuild/node reuse and compiler/Razor build servers.
2. Create a temporary targets file **outside the repository** that re-registers the unchanged official WebAssembly and ILLink tasks with the default in-process task factory: `<UsingTask ... AssemblyFile="..." Override="true" />`.
3. Resolve task DLLs from the actually restored NuGet packages. The affected .NET 10 tasks are `GenerateWasmBootJson`, `ComputeWasmBuildAssets`, `ComputeWasmPublishAssets`, `ConvertDllsToWebCil`, `ComputeManagedAssemblies` and `ILLink`; inspect the actual toolchain rather than hard-coding old package versions or paths.
4. Pass `-p:CustomBeforeMicrosoftCommonTargets=<path>` to build/publish. Change only task process placement; do not skip targets or replace task assemblies.
5. After a successful build, run the normal full test suite with `--no-build`. For publish verification, inspect diagnostic logs to confirm WebAssembly/ILLink tasks executed.

Never commit the override or sandbox-specific paths; ordinary Cloudflare/local builds do not need it. The .NET 10 migration PR #46 records a verified example and commands; reproduce the technique with the current SDK/packages rather than copying temporary paths.
