# NativeMods binaries

These files are built from NativeMods by mrnamwen (https://github.com/mrnamwen/LocalExile-NativeMods), GNU GPL v2:

| File | From |
|---|---|
| `version.dll`, `LocalExile.ClientHost.dll` | `src/native-shim` (C++), built with MinGW-w64 |
| `host/LocalExile.Client.Host.dll`, `host/LocalExile.Client.Sdk.dll` | `src/LocalExile.Client.Host`, `src/LocalExile.Client.Sdk` (.NET 10) |
| `host/addresses/3.29.3d.json` | `data/addresses/3.29.3d.json` |

Source: commit `75436cb` of mrnamwen/LocalExile-NativeMods (main) with two branches of
https://github.com/marshmellion/LocalExile-NativeModsbranch merged in: `fix-version-proxy-export` (`version.dll` also
exports `GetFileVersionInfoW`, which Path of Exile imports) and `windows-performance` (cached readable checks instead of
a VirtualQuery per read, one hover read per frame, handshake backoff, client mods load on the first frame). To build
them yourself, follow that repository's README ("dotnet build NativeMods.slnx -c Release" and the CMake build of
`src/native-shim`).
