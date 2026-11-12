# Web/cloud, usually Ubuntu or restricted Linux

Assume no automatic initialization or CLI credentials. Reuse a checkout or obtain current source in writable storage; do not run the Windows setup script. Common [environment guidance](environment.md) owns authentication and transport selection.

Before local commits, initialize a missing portable guard using [Git identity setup](../../ygo-pr/references/git-identity-setup.md).

If the required SDK is missing, attempt [Linux SDK bootstrap](sdk-linux.md). If MSBuild fails starting an out-of-process task host with `MSB4216`, `MSB4027` or socket permission errors, investigate [the conditional WASM workaround](wasm-task-host.md). Do not load either procedure on a working host.
