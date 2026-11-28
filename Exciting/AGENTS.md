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
| `Exciting/client-mods/qol/` | `Mods/Client/qol/` (only with `-NativeMods`) | NativeMods client mod (the QoL in-game panels); a player's `settings.json` is kept |
| `Exciting/nativemods/` | `Client/version.dll`, `Client/LocalExile.ClientHost.dll`, `Client/nativemods-host/` (only with `-NativeMods`) | NativeMods loader, host, address library |
| `Exciting/client-art.ps1` (run by `setup.ps1`, and by `play.cmd` with `-IfMissing`) | `Mods/Binaries/excitingitems/content/*.json` (orb art), `Mods/Binaries/bodyauras/content/` | makes from the player's own client what we may not share: the orbs' art and the Body Auras' effects (Python 3; Pillow for the orbs) |
| `Exciting/update-all.cmd`, `update-all.ps1` | `Mods/Binaries/<id>/` (other mods), cache `Tools/LocalExile-Mods/` | `setup.ps1 -Update` (with `-NativeMods` if `Client/version.dll` exists), then every installed mod that is not the build's own and has a higher `version` in our mods repository |
| `Exciting/install.cmd`, `install.ps1` | (nothing: a guide) | the guided first install for players: checks programs, clones `exciting`, gets the client, runs setup, explains Waypoint |
| the user's .NET 10 runtime (`dotnet --list-runtimes`) | `Client/nativemods-host/` (only with `-NativeMods`) | private runtime for the host; `LocalExile.Client.Host.runtimeconfig.json` is written with `includedFrameworks` |

At every start `scripts/dev/live.ps1` runs `tools/ContentPatch` (`apply --if-changed`). It writes the mods' content packs
(`content/*.json`: table rows, `.dds` art, appended text files) into the client copy: for a Steam client our bundle
`Client/Bundles2/LocalExile/Content.bundle.bin` and a patched `Client/Bundles2/_.index.bin` (the original kept in
`data/content-patch/original_index.steam.bin`), for a standalone client the same inside `Content.ggpk`. The same rows go
into `data/client.db` for the servers, and the item symbol hook (a section `.lxhook`) into `Client/Game.patched.exe`.
`restore` undoes it. NativeMods is optional and off by default: on Windows its file overlay was mounted after the game
had read some tables (mixed tables close the game) and its hooks cost frame rate; it is not used for content.

## Diagnosing

- Setup: `Exciting/setup.ps1` prints each step; every step is safe to repeat.
- Servers: `logs/live-gateway.log`, `logs/live-auth.log`, `logs/live-patch.log` (and `.err`); previous runs in `logs/archive/`.
- Content: `logs/content-patch.log` (`[content]` lines). A row-count mismatch between client and `client.db` means the
  client is another build than the tables in `sql/`.
- NativeMods (if installed): `Client/nativemods-shim.log` and `Client/nativemods-host/nativemods-shim.log`. To rule it out, rename `Client/version.dll`.
- "Deserialised a mod that we don't recognise": the client lacks the content (the content step failed or did not run).
- Mods: `/mod` in game; settings values in `data/mods/<id>.json`, declarations in each mod's `settings.json`.
- Client build: only 3.29.3c and 3.29.3d run (`src/GameServer.Core/Protocol/ClientBuilds.cs`); NativeMods needs 3.29.3d
  (`Client/Game.exe` SHA-256 `e9ba3d31...f53d`).

## Rules

- Never commit `Client/`, `data/` (except `data/schema/`), `Mods/` (git-ignored), keys, accounts or game files.
- Change a mod in `Exciting/mods/<id>/` (the copy in `Mods/Binaries/` is overwritten by the next setup or update).
- Change engine or hook code on its own branch (listed in `BUILD.md`), not on `exciting`; then publish a new build.
- Ask the person before deleting anything in `Client/` or `data/`, and before running anything that writes their
  client's own install folder (setup only reads it).

## Testing first, then release (maintainer)

- `exciting-testing` gets every change first: merge `exciting-kit` (and new engine branches or a new pinned main) into
  it, push it, and play it. `exciting` (the release) only ever moves to a commit `exciting-testing` already had:
  `git push origin origin/exciting-testing:exciting` (fast-forward; check `git merge-base --is-ancestor origin/exciting
  origin/exciting-testing` first). Never commit on `exciting` directly, so a release is always a fast-forward.
- Players choose with `Exciting/use-testing.cmd` / `use-release.cmd` (`setup.ps1 -Branch ...`); `update.cmd` follows
  the branch they are on.

## Publishing a new build (maintainer)

1. Fetch `main` and the branches in `BUILD.md`; check that each merges cleanly onto the pinned main, in that order,
   and that the result builds (`dotnet build GameServer.slnx`) and the mods in `Exciting/mods/` compile with it.
2. On `exciting-testing` (see above; `exciting` follows once it is tested): merge the new pinned main, then each branch, then `exciting-kit`; update `Exciting/BUILD.md` (the main
   commit and each branch's head); commit; push `exciting-testing` (fast-forward only).
3. If a branch no longer merges, fix the branch (merge main into it), not `exciting`.
