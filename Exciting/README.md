# LocalExile: the Exciting Items build

This is [LocalExile](https://github.com/LocalExile/LocalExile) (a local server for the Path of Exile 3.29.3 client) with
the parts that are not in LocalExile's main yet, so that **Exciting Items** and our other mods work completely:

- **The effect rules engine**: item, flask and unique lines that do something through table rows (the custom implicits,
  legacy uniques and many unique lines need it).
- **Modding hooks**: currency used on items, chest drops, level requirements, cosmetics, minion kills, gem stats...
- **Content in the client**: new currency with its own art (Defiled Vaal Orb, Krangle Orb, Limit Break Orb, Radiant
  Orb, Sealed Cosmetic, Cosmetic Cache), new implicit texts, larger stacks, pickup popups, a sun beside exciting items.
  On a Steam client this goes through [NativeMods](https://github.com/mrnamwen/LocalExile-NativeMods), which hands the
  game our files without changing your client's files.
- **The mods**: Exciting Items, Cosmetic Drops, Quality of Life (with its panels inside the game), Harvest Crafts and
  Minion Unnerf.

Everything runs on your own computer. You need your own copy of the game.

## What you need

| | Why | How to check |
|---|---|---|
| Windows 10 or 11, about 80 GB free | the client is copied once | |
| [Git](https://git-scm.com/download/win) | gets this build and its updates | `git --version` |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | builds and runs the servers | `dotnet --list-sdks` shows `10.x` |
| [Node.js](https://nodejs.org) (LTS) | unpacks the game's files | `node --version` |
| Path of Exile **3.29.3d** (Steam layout) | the game you play | `PathOfExileSteam.exe` and a `Bundles2` folder |

Install the three programs with their installers (defaults are fine), then open a new PowerShell window.

### Getting the 3.29.3d client

LocalExile runs only the client versions it knows (3.29.3c and 3.29.3d); NativeMods needs **3.29.3d**. If your Steam
install is already 3.29.3d, use it. Otherwise download that exact version with
[DepotDownloader](https://github.com/SteamRE/DepotDownloader/releases) (you sign in with your own Steam account, which
must own the game):

1. Download DepotDownloader and unzip it anywhere.
2. Run `Exciting\get-client.cmd` (after step 1 of Install below), give it the path of `DepotDownloader.exe` and an empty
   folder. It downloads about 40-50 GB and checks that the result is exactly 3.29.3d.

## Install

1. Open PowerShell in the folder where you want the game (for example `D:\Games`) and run:

   ```powershell
   git clone -b exciting https://github.com/marshmellion/LocalExile.git LocalExile-Exciting
   ```

2. Open the new `LocalExile-Exciting` folder and double-click **`Exciting\setup.cmd`**. It:
   - checks Git, .NET and Node, and prepares the file unpacker (`Tools\ooz`);
   - asks for your 3.29.3d client folder and copies it into `Client\` (your own install is only read);
   - makes your own login keys and a patched copy of the client that trusts them (`Client\Game.patched.exe`);
   - installs NativeMods into `Client\` (with a private copy of your .NET runtime: nothing is installed in Windows);
   - copies the mods into `Mods\Binaries\` and the in-game panels into `Mods\Client\`;
   - offers to make your login account.

3. Double-click **`Exciting\play.cmd`**. The first start builds the servers and the game data (a few minutes), puts the
   mods' content into the game, and opens the game. Log in with the account from step 2.

To make another account later:

```powershell
"your-password" | dotnet run --project src\GameServer.Auth -c Release -- register yourname
```

## Update

Close the game and the servers, then double-click **`Exciting\update.cmd`**. It gets the newest build (`git pull`) and
installs the mods and NativeMods again. Your characters, accounts, keys and mod settings stay (they are in `data\`).

## Playing

- **Mod settings**: every mod has its settings in `Mods\Binaries\<mod>\settings.json` (names, defaults and ranges); your
  own values are kept in `data\mods\<mod>.json`. In game, `/mod` lists the mods and why any did not load.
- **Quality of Life**: buttons at the top right (Loot, Sort, Prices), above the inventory and on the stash; keys F5
  loot, F6 sort, F7 deposit all, F8 price lines, F9 hide the buttons, F10 alternate quality gems. Its in-game panel
  settings (keys, which buttons show, price lines) are in `Mods\Client\qol\settings.json`. `/qol` does the same by chat.
- **Exciting Items**: `/exciting` lists its commands. Its README is in `Mods\Binaries\excitingitems\README.md`.

## When something goes wrong

| What you see | What to do |
|---|---|
| The game closes at start, or right after loading | Delete `Client\version.dll` (turns NativeMods off; the game then runs without our client content) and send `Client\nativemods-host\nativemods-shim.log` |
| New currency shows as a plain or missing item | Read `logs\content-patch.log` (the content step of `play.cmd`) |
| A mod does not load | `/mod` in game, and `logs\live-gateway.log` |
| `Game.patched.exe` disappears | Your antivirus removed it (a patched executable): allow it, then run `setup.cmd` again |
| `setup.cmd` or `update.cmd` stops with a red line | It says what is missing; fix that and run it again (each step is safe to repeat) |
| Anything else | LocalExile's own guide: `Agents\GettingStarted.md` |

To remove NativeMods completely: delete `Client\version.dll`, `Client\LocalExile.ClientHost.dll`,
`Client\nativemods-host\` and `Mods\Client\`.

## What this build is made of

The branch `exciting` of https://github.com/marshmellion/LocalExile is LocalExile's main (at the commit in
`Exciting\BUILD.md`) with these branches merged in, kept up to date by its maintainer:

| Branch | What it adds |
|---|---|
| `effect-rules-tables` | the effect rules engine and its tables |
| `modding-api` | modding hooks, the content patcher (also for Steam clients, through NativeMods) |
| `private-hooks` | more modding hooks (hand rules, gem stats, minion kills, spell repeats, ...) |
| `local-hooks` | the hand-overs Legacy Restored uses (supports, skill stats, currency use, Imprint items) |
| `jewel-allocates` | jewels that "Allocate" a passive grant it while socketed |
| `piety-crash-fix` | fixes for two client crashes (Tempest Shield, Piety's retreat) |
| `le-trade-patch` | what the LE.trade bridge needs (abyssal sockets in item specs, the trade panel) |
| `exciting-kit` | this folder: the setup scripts, the mods, NativeMods and these docs |

Licenses: LocalExile is GNU GPL v2; NativeMods is GNU GPL v2 (`Exciting\nativemods\SOURCE.md` says where its source
is). LocalExile is not affiliated with or endorsed by Grinding Gear Games, and is for local play only.
