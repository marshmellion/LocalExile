# NativeMods binaries

These files are built from NativeMods by mrnamwen (https://github.com/mrnamwen/LocalExile-NativeMods), GNU GPL v2:

| File | From |
|---|---|
| `version.dll`, `LocalExile.ClientHost.dll` | `src/native-shim` (C++), built with MinGW-w64 |
| `host/LocalExile.Client.Host.dll`, `host/LocalExile.Client.Sdk.dll` | `src/LocalExile.Client.Host`, `src/LocalExile.Client.Sdk` (.NET 10) |
| `host/addresses/3.29.3d.json` | `data/addresses/3.29.3d.json` |

Source: the branch `play-build` (commit `ba9e0ae`) of https://github.com/marshmellion/LocalExile-NativeModsbranch:
commit `75436cb` of mrnamwen/LocalExile-NativeMods (main) with these branches of that repository merged in, each also
offered to NativeMods: `fix-version-proxy-export` (`version.dll` also exports `GetFileVersionInfoW`, which Path of Exile
imports), `windows-performance` (cached readable checks, one hover read per frame, handshake backoff, client mods load
on the first frame), `appendable-content-tables`, `generated-ui-format` (slot button, toast and tooltip .ui text the
game can parse), `managed-fault-passthrough` (the guard leaves .NET's and the runtime's own faults alone) and
`keep-click-handlers` (buttons keep working when the game rebuilds its UI). To build them yourself, follow that
repository's README ("dotnet build NativeMods.slnx -c Release" and the CMake build of `src/native-shim`).
