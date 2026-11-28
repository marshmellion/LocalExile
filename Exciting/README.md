# LocalExile: the Exciting Items build

This is [LocalExile](https://github.com/LocalExile/LocalExile) (a local server for the Path of Exile 3.29.3 client) with
the parts that are not in LocalExile's main yet, so that **Exciting Items** and our other mods work completely:

- **The effect rules engine**: item, flask and unique lines that do something through table rows (the custom implicits,
  legacy uniques and many unique lines need it).
- **Modding hooks**: currency used on items, chest drops, level requirements, cosmetics, minion kills, gem stats...
- **Content in the client**: new currency with its own art (Defiled Vaal Orb, Krangle Orb, Limit Break Orb, Radiant
  Orb, Sealed Cosmetic, Cosmetic Cache), new implicit texts, larger stacks, pickup popups, a sun beside exciting items.
  Each start puts it into your copy of the client (`Client\`): our own bundle beside the game's files and an updated
  file index (the original is kept and comes back with `restore`). Your own install is never touched.
- **The mods**: Exciting Items, Cosmetic Drops, Body Auras, Quality of Life (with its panels inside the game), Harvest
  Crafts and Minion Unnerf. More of our mods, and what each one needs: https://github.com/marshmellion/LocalExile-Mods

Everything runs on your own computer. You need your own copy of the game.

## The easy way: the guided install

1. Open https://github.com/marshmellion/LocalExile/blob/exciting/Exciting/install.cmd and click the **download** button
   (the arrow at the top right of the file, "Download raw file").
2. Double-click the downloaded **`install.cmd`** (if Windows says it protected your PC: **More info**, then **Run anyway**).
3. Do what it says. It checks each program you need and opens its download page when one is missing, downloads this
   build, walks you through getting the 3.29.3d client from your own Steam account, sets everything up, explains
   Waypoint, and starts the game. Stopped halfway? Run it again: it skips what is done.
   Have some of it already? It lists its six steps first and you can start at any of them. It also finds a 3.29.3d
   client already on your PC (and offers to move or copy it instead of downloading), and can turn a LocalExile you
   installed from Git (Waypoint's "Source code" install) into this build, keeping your characters.

The rest of this page is the same thing by hand, and what to do when something goes wrong.

## With Waypoint

[Waypoint](https://github.com/LocalExile/Waypoint/releases) is LocalExile's launcher (it needs the
[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)). After the install above:

1. In Waypoint choose **Add a folder you already have...** and pick your install folder (Waypoint then shows the
   `exciting` branch's updates under Versions).
2. Start and stop the game with Waypoint's buttons; **Mods** has every mod's settings.
3. To get more of our mods: **Mods** -> **Browse** -> the **...** menu -> **Add repository**, paste
   `https://github.com/marshmellion/LocalExile-Mods`, and accept the warning that it is not the official list. Each
   mod's description starts with what it needs ("Needs the Exciting build" means this install).

## What you need

| | Why | How to check |
|---|---|---|
| Windows 10 or 11, about 60 GB free | the client is about 45 GB | |
| [Git](https://git-scm.com/download/win) | gets this build and its updates | `git --version` |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | builds and runs the servers | `dotnet --list-sdks` shows `10.x` |
| [Node.js](https://nodejs.org) (LTS) | unpacks the game's files | `node --version` |
| Path of Exile in your Steam library (free) | `get-client.cmd` downloads version 3.29.3d from it | |
| [Python 3](https://www.python.org/downloads/windows/) (optional, tick "Add python.exe to PATH") and Pillow (`py -m pip install pillow`) | makes Body Auras' effects and the new orbs' art from your client | `py --version` |

Install the three programs with their installers (defaults are fine), then open a new PowerShell window.

### Getting the 3.29.3d client

LocalExile runs only the client versions it knows (3.29.3c and 3.29.3d); your normal Steam install is newer.
**`Exciting\get-client.cmd`** (after step 1 of Install below; the guided install runs it for you) gets the right one:

1. If Path of Exile is not in your Steam library yet, click **Play Game** on its
   [Steam page](https://store.steampowered.com/app/238960/Path_of_Exile/) (it is free).
2. Double-click `Exciting\get-client.cmd` and press Enter to download into the install's `Client\` folder. It fetches
   [DepotDownloader](https://github.com/SteamRE/DepotDownloader) by itself, asks your Steam account name, and
   DepotDownloader asks your password and Steam Guard code (they go to Steam only).
3. Wait for about 45 GB. It checks that the result is exactly 3.29.3d. If it stops, run it again: it carries on.

## Install

1. Open PowerShell in the folder where you want the game (for example `D:\Games`) and run:

   ```powershell
   git clone -b exciting https://github.com/marshmellion/LocalExile.git LocalExile-Exciting
   ```

2. Open the new `LocalExile-Exciting` folder and double-click **`Exciting\setup.cmd`**. It:
   - checks Git, .NET and Node, and prepares the file unpacker (`Tools\ooz`);
   - asks for your 3.29.3d client folder and copies it into `Client\` (your own install is only read);
   - makes your own login keys and a patched copy of the client that trusts them (`Client\Game.patched.exe`);
   - copies the mods into `Mods\Binaries\`, and with Python makes the orbs' art from your client (`Exciting\client-art.ps1`);
   - offers to make your login account.

3. Double-click **`Exciting\play.cmd`**. The first start builds the servers and the game data (a few minutes), puts the
   mods' content into the game, and opens the game. Log in with the account from step 2. With Python, the second start
   also makes the Body Auras (they read the game data the first start made).

To make another account later:

```powershell
"your-password" | dotnet run --project src\GameServer.Auth -c Release -- register yourname
```

## The testing build

New things go to the **testing build** first (the branch `exciting-testing`) and become the released build (`exciting`)
once they have been played for a while. To try them early, close the game and the servers and double-click
**`Exciting\use-testing.cmd`**; **`Exciting\use-release.cmd`** goes back. `update.cmd` keeps you on the one you chose
(in Waypoint, Versions shows both branches). Expect bugs there, and back up `data\` first: a character played on the
testing build may use things the released build does not know yet. A new install straight on the testing build:
`install.cmd -Testing`.

## Update

Close the game and the servers, then double-click **`Exciting\update-all.cmd`**. It gets the newest build (`git pull`),
installs its mods again, and updates every other mod you added from
[our mods repository](https://github.com/marshmellion/LocalExile-Mods) to its newest version (`update.cmd` does only
the first two). Your characters, accounts, keys and mod settings stay (they are in `data\`).

## Playing

- **Mod settings**: every mod has its settings in `Mods\Binaries\<mod>\settings.json` (names, defaults and ranges); your
  own values are kept in `data\mods\<mod>.json`. In game, `/mod` lists the mods and why any did not load.
- **Quality of Life**: its overlay window (started with the servers) has the buttons (Loot, Sort, Prices, and on the
  stash Deposit all, Vendor tab, Sort tab, Upgrade maps) and the keys F5 loot, F6 sort, F7 deposit all, F8 price lines,
  F9 hide, F10 alternate quality gems. `/qol` does the same by chat.
- **NativeMods (experimental)**: `Exciting\setup.cmd -NativeMods` also installs
  [NativeMods](https://github.com/mrnamwen/LocalExile-NativeMods) and the QoL panels inside the game. On Windows it
  lowered the frame rate and closed the game in our tests, so it is off by default; delete `Client\version.dll` to
  turn it off again.
- **Exciting Items**: `/exciting` lists its commands. Its README is in `Mods\Binaries\excitingitems\README.md`.

## When something goes wrong

| What you see | What to do |
|---|---|
| "Deserialised a mod that we don't recognise" | The client has no content yet: read `logs\content-patch.log` (the content step of `play.cmd`) |
| New currency shows as a plain or missing item | The same: `logs\content-patch.log` |
| The game closes at start with NativeMods on | Delete `Client\version.dll` (NativeMods off) |
| A mod does not load | `/mod` in game, and `logs\live-gateway.log` |
| `Game.patched.exe` disappears | Your antivirus removed it (a patched executable): allow it, then run `setup.cmd` again |
| `setup.cmd` or `update.cmd` stops with a red line | It says what is missing; fix that and run it again (each step is safe to repeat) |
| No Body Auras in the cosmetics | Python was missing: install it, run `setup.cmd`, then start twice |
| Anything else | LocalExile's own guide: `Agents\GettingStarted.md` |

To take the content out of your client copy: `dotnet run --project tools\ContentPatch -c Release -- restore`.
To remove NativeMods completely: delete `Client\version.dll`, `Client\LocalExile.ClientHost.dll`,
`Client\nativemods-host\` and `Mods\Client\`.

## What this build is made of

The branch `exciting` of https://github.com/marshmellion/LocalExile is LocalExile's main (at the commit in
`Exciting\BUILD.md`) with these branches merged in, kept up to date by its maintainer:

| Branch | What it adds |
|---|---|
| `effect-rules-tables` | the effect rules engine and its tables |
| `modding-api` | modding hooks, the content patcher (standalone and Steam clients) |
| `private-hooks` | more modding hooks (hand rules, gem stats, minion kills, spell repeats, ...) |
| `local-hooks` | the hand-overs Legacy Restored uses (supports, skill stats, currency use, Imprint items) |
| `jewel-allocates` | jewels that "Allocate" a passive grant it while socketed |
| `piety-crash-fix` | fixes for two client crashes (Tempest Shield, Piety's retreat) |
| `le-trade-patch` | what the LE.trade bridge needs (abyssal sockets in item specs, the trade panel) |
| `exciting-kit` | this folder: the setup scripts, the mods, NativeMods (optional) and these docs |

Licenses: LocalExile is GNU GPL v2; NativeMods is GNU GPL v2 (`Exciting\nativemods\SOURCE.md` says where its source
is). LocalExile is not affiliated with or endorsed by Grinding Gear Games, and is for local play only.
