# The Exciting Items build: notes for AI agents

Read this when you help someone install, update, play or fix the Exciting Items build of LocalExile, or when you
maintain the build. The repository's own `AGENTS.md` (one folder up) still applies; this adds what is different here.
The player-facing guide is `Exciting/README.md`: point people there, and keep it true when you change something.

## What the build is

The branch `exciting` of `https://github.com/marshmellion/LocalExile` = LocalExile `main` at a pinned commit, plus the
branches listed in `Exciting/BUILD.md` merged in, plus the branch `exciting-kit` (this folder). Players clone that
branch and only ever fast-forward it (`Exciting/update.cmd` runs `git pull --ff-only`): **never rewrite its history**
(no rebase, no force push). A new build is new merge commits on top.

## The pieces and where they end up

| In the repository | Copied by `setup.ps1` to | Role |
|---|---|---|
| `Exciting/mods/<id>/` | `Mods/Binaries/<id>/` (git-ignored) | server mods (C# scripts the Gateway compiles); `/MIR` keeps `saves/` |
| `Exciting/client-mods/qol/` | `Mods/Client/qol/` | NativeMods client mod (the QoL in-game panels); a player's `settings.json` is kept |
| `Exciting/nativemods/` | `Client/version.dll`, `Client/LocalExile.ClientHost.dll`, `Client/nativemods-host/` | NativeMods loader, host, address library |
| the user's .NET 10 runtime (`dotnet --list-runtimes`) | `Client/nativemods-host/` | private runtime for the host; `LocalExile.Client.Host.runtimeconfig.json` is written with `includedFrameworks` |

At every start `scripts/dev/live.ps1` runs `tools/ContentPatch` (`apply --if-changed`). For a Steam client (no
`Content.ggpk`) it reads the client's loose `Bundles2/` and writes the changed files (tables, `.dds` art, appended text
files) to `Mods/Client/.merged-cache/`, which NativeMods serves to the game; the same rows go into `data/client.db` for
the servers. It also writes the item symbol hook (a section `.lxhook`) into `Client/Game.patched.exe`. For a standalone
client it patches `Content.ggpk` instead, as before. The packs are the mods' `content/*.json` files.

## Diagnosing

- Setup: `Exciting/setup.ps1` prints each step; every step is safe to repeat.
- Servers: `logs/live-gateway.log`, `logs/live-auth.log`, `logs/live-patch.log` (and `.err`); previous runs in `logs/archive/`.
- Content: `logs/content-patch.log` (`[content]` lines). A row-count mismatch between client and `client.db` means the
  client is another build than the tables in `sql/`.
- NativeMods: `Client/nativemods-host/nativemods-shim.log`. To rule it out, rename `Client/version.dll`.
- Mods: `/mod` in game; settings values in `data/mods/<id>.json`, declarations in each mod's `settings.json`.
- Client build: only 3.29.3c and 3.29.3d run (`src/GameServer.Core/Protocol/ClientBuilds.cs`); NativeMods needs 3.29.3d
  (`Client/Game.exe` SHA-256 `e9ba3d31...f53d`).

## Rules

- Never commit `Client/`, `data/` (except `data/schema/`), `Mods/` (git-ignored), keys, accounts or game files.
- Change a mod in `Exciting/mods/<id>/` (the copy in `Mods/Binaries/` is overwritten by the next setup or update).
- Change engine or hook code on its own branch (listed in `BUILD.md`), not on `exciting`; then publish a new build.
- Ask the person before deleting anything in `Client/` or `data/`, and before running anything that writes their
  client's own install folder (setup only reads it).

## Publishing a new build (maintainer)

1. Fetch `main` and the branches in `BUILD.md`; check that each merges cleanly onto the pinned main, in that order,
   and that the result builds (`dotnet build GameServer.slnx`) and the mods in `Exciting/mods/` compile with it.
2. On `exciting`: merge the new pinned main, then each branch, then `exciting-kit`; update `Exciting/BUILD.md` (the main
   commit and each branch's head); commit; push `exciting` (fast-forward only).
3. If a branch no longer merges, fix the branch (merge main into it), not `exciting`.
