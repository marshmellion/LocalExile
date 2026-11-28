# NativeMods binaries

These files are built from NativeMods by mrnamwen (https://github.com/mrnamwen/LocalExile-NativeMods), GNU GPL v2:

| File | From |
|---|---|
| `version.dll`, `LocalExile.ClientHost.dll` | `src/native-shim` (C++), built with MinGW-w64 |
| `host/LocalExile.Client.Host.dll`, `host/LocalExile.Client.Sdk.dll` | `src/LocalExile.Client.Host`, `src/LocalExile.Client.Sdk` (.NET 10) |
| `host/addresses/3.29.3d.json` | `data/addresses/3.29.3d.json` |

Source: commit `75436cb` of mrnamwen/LocalExile-NativeMods (main), unchanged. The same source with our one pull request
(five more appendable content tables) is the branch `appendable-content-tables` of
https://github.com/marshmellion/LocalExile-NativeModsbranch. To build them yourself, follow that repository's README
("dotnet build NativeMods.slnx -c Release" and the CMake build of `src/native-shim`).
