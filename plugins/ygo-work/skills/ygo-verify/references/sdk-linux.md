# Missing SDK on restricted Linux

Use when the SDK check finds no usable SDK on a Linux host. Determine the required SDK channel from the working revision's project/build files; honor any `global.json` SDK pin with `--version` instead of `--channel`. Set `ygo_sdk_channel` below to the required major/minor channel before running this nonprivileged bootstrap:

```sh
ygo_sdk_channel='<required-major.minor>'
ygo_sdk_root="$(mktemp -d "${TMPDIR:-/tmp}/ygo-dotnet-sdk.XXXXXX")"
ygo_install_script="$(mktemp "${TMPDIR:-/tmp}/ygo-dotnet-install.XXXXXX")"
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$ygo_install_script"
TAR_OPTIONS=--no-same-owner bash "$ygo_install_script" --channel "$ygo_sdk_channel" --install-dir "$ygo_sdk_root" --no-path

export DOTNET_ROOT="$ygo_sdk_root"
export PATH="$DOTNET_ROOT:$PATH"

dotnet --info
dotnet --list-sdks
```

`TAR_OPTIONS` prevents GNU tar from restoring archive owners on restricted filesystems. Confirm the required SDK, then resume the selected checks. Report the exact failed command/error if bootstrap fails.
