# Project Hullbreach: Game

This is the game half of Hullbreach: the Unity client and the dedicated
server, built from one project. Players build a spaceship out of blocks
and try to breach each other's hull. Accounts, ELO, match history, and the
store are in the [platform repo](https://github.com/lognd/project-hullbreach-platform#readme);
this one talks to that API over HTTP(S).

for the sign-in changes on steven's branch, use the
[account and two-player test guide](docs/steven-testing.md).

Company of Theseus, UF CEN3031, fall 2026. All rights reserved, see
[LICENSE](LICENSE).

The first big part of this file, **Start here**, is a step-by-step guide
for launching the game and demoing multiplayer and the platform sign-in.
It assumes you have never used a terminal, Git, or Unity, and its Step 1
is a **doctor** that checks your computer and tells you how to fix what
is missing. The second
part, **Developer guide**, is the older reference material for people
changing the code. If you only want to tweak the single-player demo
without code, [docs/getting-started.md](docs/getting-started.md) is the
friendliest place.

---

# Start here

## Step 1: run the doctor

The **doctor** is a checker that looks at your computer and tells you,
one line at a time, what is ready and what is missing for each demo, and
exactly how to fix each missing thing. Use it first, and again whenever
something does not work. The **play** command next to it builds the game
and starts it in every mode, so you never have to type long game options.

1. You need the game folder first. If you do not have it yet, do
   [0.1 Open a terminal](#01-open-a-terminal),
   [0.2 Install Git](#02-install-git) and
   [0.3 Download (clone) the game](#03-download-clone-the-game), then come
   back here.
2. Run the doctor:
   - **Windows**: open the `project-hullbreach-game` folder in File
     Explorer and double-click **`doctor.cmd`**. (Or, in a terminal in
     that folder, type `.\doctor.cmd` and press **Enter**.)
   - **macOS / Linux**: in a terminal in that folder, type `./doctor.sh`
     and press **Enter**.
3. The first time, it says it needs a small helper called **uv** and asks
   `Install uv now? ... [y/N]`. Type `y` and press **Enter**. (uv fetches
   the right Python for the doctor; it changes nothing else. If you say
   no, it shows the command to install uv yourself.) Then it prints
   `Preparing the Hullbreach tools (the first time takes a minute)...`.
   If it says uv was installed but cannot be found yet, close the window,
   open a new one, and run the doctor again.
4. Read the list. Each line starts with one of:
   - `[PASS]` fine;
   - `[WARN]` works, but read the note;
   - `[FAIL]` fix this: numbered steps follow (what to click, what to
     type, what you should see) and a link to the part of this README
     that explains it;
   - `[ -- ]` waiting: this check needs an earlier `[FAIL]` fixed first;
   - `[SKIP]` not checked this time (some checks are opt-in).
5. Fix the **first** `[FAIL]`, then run the doctor again. Repeat until
   there is no `[FAIL]`. It then prints **You are ready. Next, type:** and
   the exact commands for each demo.

To check only one demo, add `--demo` and its name (on Windows type these
in a terminal in the game folder; on macOS/Linux use `./doctor.sh`
instead of `.\doctor.cmd`):

```
.\doctor.cmd --demo guest
.\doctor.cmd --demo online
.\doctor.cmd --demo platform
```

Other switches: `--deep` also starts Unity in the background to prove the
license works and the code compiles (takes several minutes);
`--login` tries a real platform sign-in (you type the password; it is
never stored); `--collect` makes a zip of every log plus the doctor's
report, with passwords and keys removed, to send to a teammate when you
are stuck. Every check's id (like `net.firewall`) is listed in
[Doctor check ids](#doctor-check-ids) with the section that explains it.

The quick versions of the demos, once the doctor is happy:

| Demo | What to type (Windows; macOS/Linux use `./play.sh`) |
| --- | --- |
| Build the game (once, and after every `git pull`) | `.\play.cmd build` |
| GUEST: host and client on this one computer | `.\play.cmd two-local` |
| GUEST: host for a friend on your network | `.\play.cmd host`, the friend types `.\play.cmd join <your IP>` |
| ONLINE: the Relay lobby | `.\play.cmd online` |
| PLATFORM: a server that requires sign-in | `.\play.cmd server --platform --require-auth` |
| PLATFORM: a player who signs in | `.\play.cmd join 127.0.0.1 --platform` |
| Show the newest game log | `.\play.cmd logs` |

The guides below explain each one, what you should see, and what to do
when it goes wrong.

## What is this?

Hullbreach is two separate programs that live in two separate GitHub
repositories ("repos"; a repo is just a folder of files with its full
history):

- **The game** (this repo, `project-hullbreach-game`). A Unity program.
  It is the window you play in, and the same project can also be built
  into a **game server**: a program with no window that runs a match and
  that the players' games connect to. In most of the guides below one of
  the players' games acts as the server too (that player is the
  **host**), so you do not need a separate server program.
- **The platform** (the other repo,
  [`project-hullbreach-platform`](https://github.com/lognd/project-hullbreach-platform#readme)).
  A website plus a web API (a program that answers requests over HTTP,
  the same protocol your browser uses) plus a database. It stores
  accounts and passwords. The game asks it "is this username and password
  right?" when you sign in, and the game server asks it "is this player
  really signed in?" when the player connects.

How the pieces talk to each other:

```
   Player A's computer                         Player B's computer
 +----------------------+                   +----------------------+
 |  Hullbreach game     |   game traffic    |  Hullbreach game     |
 |  (host: also runs    |<----------------->|  (joins A's match)   |
 |   the game server)   |  via Unity Relay  |                      |
 +----------------------+  or direct UDP    +----------------------+
            |                  port 7979               |
            | "sign me in" / "is this player           | "sign me in"
            |  signed in?"  (HTTP, port 8000)          |
            v                                          v
 +------------------------------------------------------------------+
 |  Hullbreach platform (other repo): web API + website + database   |
 |  http://127.0.0.1:8000 by default                                 |
 +------------------------------------------------------------------+
```

- **Unity Relay** is a free Unity cloud service that passes game traffic
  between players over the internet, so nobody has to open ports on their
  router. Players find each other through the in-game **lobby** screen.
- **Direct (LAN)** means one game connects straight to the other game's
  IP address (the computer's number on the network) on UDP port 7979.
  Good for two computers on the same Wi-Fi with no internet services.
- The platform is **optional**. Without it you play as a guest. With it
  you can show "signed in as <name>" and turn on a mode where the server
  refuses players who are not signed in.

## Which guide do I need?

| I want to... | Do this |
| --- | --- |
| Check this computer is ready, and get told how to fix what is not | [Step 1: run the doctor](#step-1-run-the-doctor) |
| See the game run at all, alone | [Part 0](#part-0-one-time-setup) then [Guide 1](#guide-1-single-player-quick-look) |
| Play two copies against each other over the internet | Part 0, then [Guide 2](#guide-2-multiplayer-through-the-relay-lobby) |
| Connect two computers on the same network without Unity's online services | Part 0, then [Guide 3](#guide-3-direct-lan-connection) |
| Show signing in with a platform account | Part 0, Guide 2, then [Guide 4](#guide-4-signing-in-with-a-platform-account) |
| Run a game server with no window, on a Linux machine | Part 0, then [Guide 5](#guide-5-dedicated-linux-server) |
| Run a five-minute demo at standup | [Demo script for standup](#demo-script-for-standup) |
| Fix something that went wrong | [Troubleshooting](#troubleshooting) |

**What has been tested, honestly.** On a fresh Windows 11 clone with
only the Windows (Mono) build module, the doctor and `play` commands
were walked top to bottom: `play build` made the game; `play two-local`
and a separate `play host` / `play join 127.0.0.1` both reached the
match (Guide 3); `play online --headless` signed in to Unity Services
and created a Relay lobby (Guide 2, without a second player clicking
JOIN CODE); and against a local platform with sign-in required, a
signed-in client was admitted under its account name while a guest was
turned away with the reason shown (Guide 4.5). Not tested yet: the
Linux and Windows Dedicated Server builds (Guide 5; the modules were
not installed, so `play server` used the game build), macOS and Linux
players, and a Relay match between two people clicking through the
lobby. If a step does not match what you see, say so in the team chat.

## Words you will see

- **Terminal**: a window where you type commands instead of clicking.
  You type (or paste) a command, press **Enter**, and it prints text
  back. On Windows it is called **PowerShell**; on a Mac it is called
  **Terminal**; on Linux it is usually called **Terminal** too.
- **Command**: one line of text you type into the terminal. In this file
  commands are shown in grey boxes like this:
  ```
  git --version
  ```
  Copy the whole line, paste it into the terminal (right-click, or
  **Ctrl+V** on Windows PowerShell, **Cmd+V** on a Mac,
  **Ctrl+Shift+V** in a Linux terminal), then press **Enter**.
- **Folder / path**: where a file lives, written like
  `C:\Users\you\Documents` (Windows) or `/home/you` (Linux, Mac uses
  `/Users/you`).
- **Clone**: download a repo with Git so you have your own copy.
- **Scene**: a Unity level. A file ending in `.unity` under
  `Assets/Scenes/`.
- **Play Mode**: pressing the Play button in Unity runs the open scene
  inside the editor window. Press Play again to stop.
- **Build**: turning the Unity project into a normal program (an `.exe`
  on Windows) that runs without Unity installed.
- **IP address**: a computer's number on a network, like
  `192.168.1.23`. `127.0.0.1` always means "this same computer".
- **Port**: a numbered door on a computer that a program listens on. The
  game uses **7979** (UDP) for direct connections; the platform uses
  **8000**.
- **Firewall**: the part of your operating system that blocks programs
  from receiving network traffic until you allow them.

## Part 0: one-time setup

You do this once per computer. Budget an hour, most of it waiting for
downloads.

### 0.1 Open a terminal

- **Windows**: press the **Windows key**, type `PowerShell`, click
  **Windows PowerShell**. A blue or black window with a line like
  `PS C:\Users\you>` appears. That line is the **prompt**; it shows the
  folder you are "in".
- **macOS**: press **Cmd+Space**, type `Terminal`, press **Enter**. The
  prompt ends with `%` or `$`.
- **Linux**: press **Ctrl+Alt+T** (or find "Terminal" in your app menu).
  The prompt ends with `$`.

You should see: a window with a blinking cursor after the prompt. Leave it
open; the next steps use it.

### 0.2 Install Git

Git is the tool that downloads (clones) this repo and keeps it up to date.
This repo does **not** use Git LFS (large file storage), so plain Git is
all you need.

- **Windows**: go to https://git-scm.com/download/win, download "Git for
  Windows", run the installer and click **Next** on every page (the
  defaults are right). Then **close PowerShell and open a new one** so it
  notices Git.
- **macOS**: in Terminal run `git --version`. If a box pops up offering
  to install "command line developer tools", click **Install** and wait.
- **Linux (Ubuntu/Debian)**: run `sudo apt update && sudo apt install -y git`
  and type your computer password when asked (nothing shows while you
  type; that is normal).

Check it worked:

```
git --version
```

You should see something like `git version 2.43.0`. Any version is fine.
If you see "not recognized" or "command not found", see
[Troubleshooting](#troubleshooting).

Unity runs on Windows and macOS here. On Windows, clone and open the game
from normal Windows folders with Git for Windows, **not** from inside WSL
(WSL is a Linux layer some of us use for the platform; Unity is very slow
and confused when the project lives there).

### 0.3 Download (clone) the game

1. Pick a folder with **no spaces** in its path. Unity and its build tools
   misbehave with spaces. `C:\dev` on Windows or your home folder on a
   Mac are good choices. On Windows, make and enter `C:\dev`:
   ```
   mkdir C:\dev
   cd C:\dev
   ```
   On macOS/Linux, `cd ~` puts you in your home folder.
2. Clone:
   ```
   git clone https://github.com/lognd/project-hullbreach-game.git
   ```
   What it does: downloads the whole project into a new folder named
   `project-hullbreach-game`. You should see `Cloning into
   'project-hullbreach-game'...` and some progress lines ending in
   `done.`. If it asks you to sign in to GitHub, you need to be added to
   the repo first (send your GitHub username to the team lead); GitHub
   wants a personal access token, not your password, and the
   [platform README](https://github.com/lognd/project-hullbreach-platform#readme)
   walks through making one.
3. Later, to get the newest version, go into that folder and pull:
   ```
   cd C:\dev\project-hullbreach-game
   git pull
   ```

### 0.4 Install Unity Hub and the exact Unity version

Unity Hub is a small launcher that installs Unity editors and opens
projects. This project needs **exactly Unity 6000.0.43f1** (Unity 6). Not
a newer 6000.x: a different version will try to "upgrade" the project and
break it for everyone else. (The version is recorded in
`ProjectSettings/ProjectVersion.txt`.)

1. Download Unity Hub from https://unity.com/download and install it.
2. Open Unity Hub and sign in (make a free Unity account if you do not
   have one; the **Personal** license is free and is what we use).
3. Install the editor. Easiest way: paste this into your web browser's
   address bar and press Enter, then let it open Unity Hub:
   ```
   unityhub://6000.0.43f1/97272b72f107
   ```
   Or in the Hub: **Installs -> Install Editor -> Archive -> download
   archive**, find **6000.0.43f1** on the web page and click its
   "Unity Hub" button.
4. Unity Hub then asks which **modules** (extra parts) to install. Tick:
   - **Windows Build Support** if you are on Windows, or **Mac Build
     Support** on a Mac. Needed to make a build. That alone is enough:
     the `play build` command uses Mono (Unity's simpler way to build)
     when the IL2CPP parts below are missing, and both play the same.
   - Optional: **Windows Build Support (IL2CPP)** plus **Microsoft Visual
     Studio Community** with its **Desktop development with C++**
     workload (on a Mac, Xcode from the App Store). IL2CPP turns the game
     into C++ first; the project is set to it for release builds.
   - **Windows Dedicated Server Build Support** (or the Mac/Linux one for
     your computer). Needed only for `play build server` (Guide 5);
     without it `play server` still works by running the normal build
     with no window.
   - **Linux Build Support** and **Linux Dedicated Server Build
     Support**. Needed only to build the Linux server for Guide 5.
   The doctor's `unity.modules` check lists what is missing and prints
   the exact Unity Hub steps (or one command) to add it later: Unity Hub
   -> **Installs** -> the gear next to `6000.0.43f1` -> **Add modules**.
5. Click **Install** (or **Continue**) and wait. It is several gigabytes.

You should see: under **Installs**, an entry `6000.0.43f1` with the
modules listed underneath.

### 0.5 Open the project in Unity

1. Unity Hub -> **Projects** -> **Add** (or the arrow next to it) ->
   **Add project from disk**.
2. Pick the `project-hullbreach-game` folder you cloned (the folder that
   *contains* `Assets` and `ProjectSettings`, not one of those).
3. The project appears in the list with **6000.0.43f1** next to it. Click
   it.
4. The first open **imports** everything and builds a hidden `Library`
   folder. This takes 5 to 15 minutes, sometimes more. A progress bar
   shows what it is doing. Let it finish.

You should see: the Unity editor. The important parts:

- **Play button**: the triangle at the top center. Press it to run the
  open scene; press it again to stop.
- **Project window**: bottom area, a file browser of `Assets`.
- **Hierarchy**: left side, the objects in the open scene.
- **Console**: a tab next to the Project window (if missing:
  **Window -> General -> Console**). Messages and errors appear here.
  Red lines are errors.

If Unity says the project was saved with a different editor version, or
the Console is full of red "package" errors, stop and see
[Troubleshooting](#troubleshooting). Do **not** click "Continue" to
upgrade.

## Build the game with one command

A **build** is the game as a normal program that runs without Unity. The
`play` command makes one for you, with the right scenes and settings, so
you never need Unity's **Build Profiles** window. (The by-hand way is in
[Appendix: doing it by hand](#appendix-doing-it-by-hand).)

1. **Close Unity** if it has this project open. (A build starts a hidden
   copy of Unity, and two Unitys cannot share one project.)
2. In a terminal in the game folder, type:
   - **Windows**: `.\play.cmd build`
   - **macOS / Linux**: `./play.sh build`
3. Wait. It prints `Building the windows client with Unity (no window
   opens).` and where Unity's log is. The **first** build imports the
   whole project and can take 30 to 60 minutes; later builds take a few
   minutes. Leave the window open.

You should see at the end:

```
[PASS] Built the windows client: C:\dev\project-hullbreach-game\Builds\windows-client
    Next: .\play.cmd two-local
```

The build is in `Builds\windows-client` (`Builds/macos-client` or
`Builds/linux-client` elsewhere) inside the game folder. It is a
**Development Build**, which Guide 4 needs (plain `http://` platform
addresses only work in development builds). Build again after every
`git pull`; the doctor warns when the build is older than your code
(`build.fresh`).

- **Two computers**: copy the **whole** `Builds\windows-client` folder
  (not just the `.exe`) to the other computer, for example zip it. If
  the other computer has its own clone of the game, put the folder at
  the same place (`Builds\windows-client`) and the `play` commands work
  there too.
- **A dedicated server build** (Guide 5): `.\play.cmd build server`.
  It needs Unity's **Dedicated Server** module (0.4). Without it, skip
  this: `play server` runs the normal build as a server with no window.
- **Another operating system**: add `--target linux` (or `windows`,
  `macos`), for example `.\play.cmd build server --target linux` for
  Guide 5. That needs the matching module too.
- **Options**: `--release` makes a non-development build (it then needs
  an `https://` platform); `--backend mono` or `--backend il2cpp` picks
  the scripting backend (the default uses IL2CPP when its module and the
  C++ tools are installed, otherwise Mono; both play the same).

If the build fails, the last lines say why (for example a missing module
or a compile error, with the fix). The full Unity log is
`Logs\play\build-client.log`. Run the doctor if you are not sure.

## Guide 1: single-player quick look

The fastest way to see the game. No network, no accounts.

1. In the **Project** window open the `Assets` folder, then `Scenes`.
2. Double-click **`DemoScene`**. The Hierarchy fills with objects like
   `PlayerShip` and `TargetShip`.
3. Press **Play**.

You should see: the game view with your ship and a target ship, starting
in **Build** mode. Try:

- **Tab** switches between Build and Fly.
- In Build mode, **1** to **7** pick a block type, **left click** places
  it, **Ctrl+Z** undoes.
- In Fly mode, **W/S** thrust, **A/D** turn, **Space** fires, **R**
  resets the ship, **O** cycles the color overlays (stress, damage and
  so on).

The full control list is in [Playing the demo](#playing-the-demo) below
and in [docs/getting-started.md](docs/getting-started.md). Press **Play**
again to stop.

## Guide 2: multiplayer through the Relay lobby

This is the main multiplayer path. Every copy of the game starts on a
lobby screen; one player hosts, the others join with a code, the host
presses Start Game. Traffic goes through Unity Relay, so it works across
the internet and through home routers. It needs internet access and the
Unity project linked to Unity's online services (step 2.1).

The scenes involved: `LobbyScene` (the lobby screen, first in the build)
and `MultiplayerGame` (the match, loaded when the host presses Start
Game).

### 2.1 Link the project to Unity's online services (once per computer)

The lobby uses three Unity cloud services: **Authentication** (signs each
copy in anonymously, no account needed by the player), **Lobby** (the
list of games and join codes), and **Relay** (passes the traffic). The
editor must be linked to the team's Unity Cloud project for these to
work.

1. In Unity: **Edit -> Project Settings -> Services**.
2. If it says the project is not linked, choose **Use an existing Unity
   project ID**, pick the organization and the team's project, and click
   **Link**. The repo remembers the team project's ID
   (`cloudProjectId` in `ProjectSettings/ProjectSettings.asset`) and its
   organization (`derrickdavison`), so you need to be a member of that
   Unity organization. If it is not in your list, ask the organization
   owner to invite your Unity account at https://cloud.unity.com.
3. In the Unity Cloud dashboard (https://cloud.unity.com) for that
   project, **Lobby** and **Relay** must be turned on (they are free at
   our scale). Anonymous sign-in for Authentication is on by default.

Builds remember which project they were made from. **Every copy that
plays together must be built from (or run in) an editor linked to the
same Unity Cloud project**, or they will not see each other's lobbies.

You should see: the Services page showing the linked project name.

### 2.2 Try it inside the editor (one computer)

1. Open `Assets/Scenes/LobbyScene` and press **Play**.
2. The lobby shows the title **HULLBREACH ONLINE** and a status line that
   says `Connecting to Unity Services...` and then `Online`.

If it says `Online service error` instead, the project is not linked or
the services are off; see [Troubleshooting](#troubleshooting).

To play against yourself inside the editor, use **Multiplayer Play Mode**
(it is installed with the project). It opens extra "virtual player"
windows that each run their own copy:

1. **Window -> Multiplayer -> Multiplayer Play Mode**.
2. Tick **Player 2**. Wait until it says it is active (the first time
   takes a minute or two; a second Unity window opens).
3. With `LobbyScene` open, press **Play**. Both windows start the lobby.
4. Host in one window and join in the other (step 2.4).

The [netcode doc](docs/netcode.md) notes the virtual players need
isolated player profiles. If the second player cannot join (for example
an error that the player is already in the lobby), use two builds on
two computers instead (2.3).

### 2.3 Make a build and start it

1. Build the game once (and again after every `git pull`): see
   [Build the game with one command](#build-the-game-with-one-command).
   In short, close Unity and type `.\play.cmd build` (macOS/Linux:
   `./play.sh build`).
2. Start the game on the lobby screen:
   - **Windows**: `.\play.cmd online`
   - **macOS / Linux**: `./play.sh online`

You should see: the game window opens on the lobby screen, and the
terminal prints which build it started and where its log is.

Windows may show "Windows protected your PC" the first time because the
build is not signed; click **More info** -> **Run anyway**. (Mac: see
Troubleshooting.)

**Two computers**: copy the **whole** `Builds\windows-client` folder to
the second computer (or build there from its own clone). Both computers
must run builds made from the same commit.

**Two copies on one computer**: start the second copy with its own Unity
sign-in profile, so Unity treats it as a different player:
`.\play.cmd online --profile friend2` (any short name of letters, digits,
`-` or `_`). Without `--profile` both copies share one saved sign-in and
the second one cannot join.

**Check the online services without clicking** (useful before a demo):
`.\play.cmd online --headless` starts a hidden copy that signs in to
Unity Services, creates a private test lobby with a Relay allocation, leaves it
again, and prints `[PASS] Unity Services sign-in, lobby and Relay work
from here.` or the exact error. The doctor's `services.linked` and
`services.reachable` checks cover the setup side.

The by-hand way (Unity's **Build Profiles** window, double-clicking the
`.exe`) is in [Appendix: doing it by hand](#appendix-doing-it-by-hand).

### 2.4 Host, join, and start a match

On the **host** copy:

1. Wait for the status to say `Online`.
2. Under **HOST A LOBBY**: type any **Lobby name**, leave **Maximum
   players** at 8. Optionally tick **Private (join code only)** and type a
   password (8 to 64 characters; shorter gives `Lobby passwords must be 8
   to 64 characters.`).
3. Click **HOST WITH RELAY**. Status shows `Creating lobby and Relay
   allocation...` and then `Hosting <name>`.
4. The waiting room shows an **INVITE CODE**. Read it out to the other
   player (or paste it in chat).

On the **joining** copy:

1. Either click **REFRESH** under **PUBLIC LOBBIES** and pick the host's
   lobby, or type the invite code under **JOIN BY CODE** (and the
   password if there is one) and click **JOIN CODE**.
2. Status shows `Joining lobby by code...` and then `Joined <name>`.

Both copies should list both players under **PLAYERS**. Then on the host
click **START GAME** (only the host has it; anyone else gets `Only the
lobby host can start the game.`).

You should see in both copies: the match scene with two ships (yours
light blue, the other orange) and a status line at the top like

```
Connected as #1  *  Replicated ships: 2  *  BUILD
```

(the real separator is a small dot). **`Replicated ships: 2` is the
proof that multiplayer works.** Build with 1-7 and left click, press Tab
to fly, W/S/A/D to move, Space or left click to fire. **LEAVE** returns to
the lobby.

## Guide 3: direct LAN connection

Use this when two computers are on the same network and you do not want
Unity's online services (no internet, no linking). One copy is the host
(it runs the server and plays); the other connects to the host's IP
address on UDP port **7979**.

The `play` command starts the game in direct mode for you. Under the
hood it passes these game options (the code is in
`Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachNetCodeBootstrap.cs`;
the [appendix](#appendix-doing-it-by-hand) shows how to type them
yourself):

| Option | Meaning |
| --- | --- |
| `-hullbreachDirect` | Host: run the server on port 7979 and play on it. |
| `-hullbreachConnect <ip>` | Join the game at `<ip>`, for example `-hullbreachConnect 192.168.1.23`. |
| `-hullbreachPort <number>` | Use this port instead of 7979 (for both hosting and joining). |
| `-hullbreachServer` | Run as a server with no player of its own (add `-batchmode -nographics` for no window). |

Each direct option opens the match (`MultiplayerGame`) by itself; the
lobby screen is skipped. A bad address logs `Hullbreach direct: Invalid
-hullbreachConnect address '<x>' (use a numeric IPv4 address such as
192.168.1.23); using 127.0.0.1.` and connects to the same computer instead.

### 3.1 Make a build

The same build as Guide 2 works:
[Build the game with one command](#build-the-game-with-one-command)
(`.\play.cmd build`). Both computers need it: build on each, or copy the
whole `Builds\windows-client` folder across.

### 3.2 Find the host's IP address

On the **host** computer, the doctor prints it: `.\doctor.cmd --demo
guest` (check `net.lan`) lists this computer's local addresses and says
which one to give a friend. By hand, in a terminal:

- **Windows**: `ipconfig`. Find your active adapter (Wi-Fi or Ethernet)
  and read **IPv4 Address**, for example `192.168.1.23`.
- **macOS**: `ipconfig getifaddr en0` (Wi-Fi; try `en1` if empty).
- **Linux**: `hostname -I`. The first number is usually the one.

Addresses starting `192.168.`, `10.`, or `172.16.` to `172.31.` are
local-network addresses: what you want. Both computers must be on the
same network (same Wi-Fi). Campus or guest Wi-Fi often blocks
device-to-device traffic; a phone hotspot or home router works better.

### 3.3 Start the host

In a terminal in the game folder on the host computer:

- **Windows**: `.\play.cmd host`
- **macOS / Linux**: `./play.sh host`

It prints `Hosting on UDP port 7979.` and the command your friend types.

The **first** time, Windows shows "Windows Defender Firewall has blocked
some features of this app". Tick **Private networks**, click **Allow
access**. If you clicked Cancel by mistake, run `.\doctor.cmd --demo
guest --fix-firewall`: it shows the exact rule for UDP port 7979 and
adds it after you type `y` (Windows asks for administrator permission).
Linux with `ufw` on: `sudo ufw allow 7979/udp`. macOS: click **Allow**
on the incoming-connections prompt.

You should see: the match scene, and the HUD line changes from `Waiting
for Netcode connection...` to `Connected as #1 ... Replicated ships: 1`.

### 3.4 Start the joiner

On the **other** computer, in a terminal in its game folder, with the
host's IP:

- **Windows**: `.\play.cmd join 192.168.1.23`
- **macOS / Linux**: `./play.sh join 192.168.1.23`

(Replace `192.168.1.23` with the host's real address.)

You should see in both: `Replicated ships: 2`.

**Same computer, two copies**: `.\play.cmd two-local` starts a host and
a client that joins `127.0.0.1`. Only the host listens on the port, so
both fit; each writes its own log. Add `--headless` to run both without
windows: after about half a minute it prints a **Result** with `[PASS]
host: in the match (...replicated ships: 2)` and the same for the
client. That is the quickest proof that direct multiplayer works on this
computer.

To use a different port (if 7979 is taken; the doctor's `net.port`
check tells you), add `--port 7980` to **both** the host and the join
commands, and allow that port in the firewall.

## Guide 4: signing in with a platform account

What you will show: a player signs in with a platform account on the
lobby screen; the game server checks that sign-in with the platform; the
in-game HUD says who is signed in; and a server can be set to refuse
players who are not signed in.

### 4.1 The sign-in screen is already in the project

`LobbyScene` ships with the sign-in screen
(`Assets/Prefabs/UI/SignInCanvas.prefab`), so there is no editor step.
If the file is missing from your copy, run `git pull`. If you ever need
to regenerate it, use the menu **Hullbreach -> Online -> Add sign-in
screen to lobby scene** and save the scene (**Ctrl+S** / **Cmd+S**).
Without it the lobby still works, but everyone simply plays offline.

### 4.2 Start the platform and make an account

Follow the [platform README](https://github.com/lognd/project-hullbreach-platform#readme)
("Setup (once)" and "Run it"). In short you will have the database, the
API, and the website running, and:

- the API answers at `http://127.0.0.1:8000` (open
  `http://127.0.0.1:8000/api/docs` in a browser; you should see the API
  docs page);
- the website is at `http://localhost:5173`. Use its **Register** link
  to create an account. Write down the username and password.

Run the platform on the **same computer** as the game copy that hosts,
because the game's default platform address is `http://127.0.0.1:8000`
(this computer). If you run the platform inside WSL on Windows, Windows
programs normally reach it at `127.0.0.1:8000` too.

Then run `.\doctor.cmd --demo platform` in the game folder. It checks the
platform answers (`platform.health`, `platform.ready`), its database is
set up (`platform.migrations`), and the server key (below). Add
`--login` to try your new account (you type the password; it is never
stored or logged). Pass `--platform-url http://<address>:<port>` to the
doctor and to every `play` command when the platform is not at
`http://127.0.0.1:8000`.

### 4.3 Game options for the platform

| Option | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `-hullbreachPlatformUrl <url>` | `HULLBREACH_PLATFORM_URL` | `http://127.0.0.1:8000` | Where the platform API is. Used to sign in and to check players. |
| `-hullbreachPlatformTimeout <seconds>` | (none) | `5` | How long to wait for the platform. |
| `-hullbreachRequirePlatformAuth` | `HULLBREACH_REQUIRE_PLATFORM_AUTH` (`1`, `true` or `yes`) | off | Server only: kick anyone not signed in. |
| (environment only) | `HULLBREACH_PLATFORM_SERVER_KEY` | unset | Server only: the key the platform gives game servers. Never pass it as an option; `-hullbreachPlatformServerKey` is ignored with a warning. |

In the Relay lobby flow the **host's copy is the server**, so the
"server only" settings go on the host's command line. Plain `http://`
platform addresses only work in the editor and in **Development Build**
builds; a non-development build needs an `https://` address.

### 4.4 Demo with sign-in optional (the default)

1. Make sure the platform is running (4.2).
2. Start the game at the lobby: `.\play.cmd online` (it tells the game
   where the platform is; add `--platform-url ...` if it is not at
   `http://127.0.0.1:8000`).
3. The first thing the lobby shows is the sign-in form: **HULLBREACH**,
   "Sign in with your Hullbreach account", **Username**, **Password**,
   **SIGN IN**, and **PLAY OFFLINE**.
4. Type the account you made and click **SIGN IN**. It shows `Signing
   in...` and then the form closes; a small bar in the top-right corner
   says **Signed in as <name>** with a **SIGN OUT** button.
5. Start a second copy (`.\play.cmd online --profile friend2`, or use
   the second computer, which must reach the platform; see 4.6) and click **PLAY OFFLINE** on it, or sign in with a
   second account.
6. Host and join as in Guide 2, then **START GAME**.

You should see on the HUD, on a second line under `Replicated ships`:

- signed-in player: `Platform: signed in as <name>`
- offline player: `Platform: guest (Guest #<id>)`
- for a moment while connecting: `Platform: waiting for server
  admission...`

The host's log (see [where logs are](#where-the-logs-are)) shows
`Hullbreach platform: config url=http://127.0.0.1:8000 timeout=5s
requireAuth=no serverKey=not set` and `Hullbreach platform: server allows
guests; platform sign-in is optional (pass -hullbreachRequirePlatformAuth
to require it).`

### 4.5 Demo with sign-in required

1. Close all copies. Start the host copy with sign-in required:
   `.\play.cmd online --require-auth` (macOS/Linux:
   `./play.sh online --require-auth`).
2. On the host, **sign in** (the host's own player is checked too).
3. Start a second copy (`.\play.cmd online --profile friend2`), click
   **PLAY OFFLINE**, then join the host's lobby and let the host start
   the game.

You should see: the offline player is disconnected and its HUD shows
`Platform: rejected - This server requires a Hullbreach account. Sign in
and reconnect.` The host's log (`.\play.cmd logs`) shows `Hullbreach
platform: server REQUIRES platform sign-in; unverified players are
disconnected.` and `Hullbreach platform: disconnecting rejected peer
<n>.`

Then sign the second player in (account bar -> **SIGN IN**), rejoin, and
show it is admitted with `Platform: signed in as <name>`.

**The server key.** The platform only answers "is this player signed
in?" for game servers that show one of its keys (the platform setting
`HULLBREACH_GAME_SERVER_API_KEYS`). The game server reads its key from
the environment variable `HULLBREACH_PLATFORM_SERVER_KEY`, never from a
command-line option. Set it in the same terminal before starting the
server (replace the placeholder with the real key from the platform
owner; never commit it or paste it in chat):

- **Windows PowerShell**: `$env:HULLBREACH_PLATFORM_SERVER_KEY = "<dev server key>"`
- **Windows cmd**: `set HULLBREACH_PLATFORM_SERVER_KEY=<dev server key>`
- **macOS / Linux**: `export HULLBREACH_PLATFORM_SERVER_KEY='<dev server key>'`

`.env.example` at the repo root lists every Hullbreach environment variable
with placeholder values. Nothing loads a `.env` file, so it is a reference:
export the values in your shell as above.

The doctor's `platform.serverkey.set` and `platform.serverkey.accepted`
checks test it (the second one signs in with `--login` and asks the
platform with the key). If the key is wrong, every player gets `Could not
verify your platform session (platform refused this game server's key
(invalid server key); set HULLBREACH_PLATFORM_SERVER_KEY to one of the
platform's game server keys).` instead of being signed in. Older
platforms that do not check keys yet work without one.

#### The same demo with no clicking

The `play` command can run the whole sign-in-required demo in one
terminal window, with hidden players. Good for checking before a demo:

1. Start a dedicated server that requires sign-in (it keeps running;
   leave this window open):
   ```
   .\play.cmd server --platform --require-auth
   ```
   It first asks the platform about the key and prints `Platform accepts
   this server's key.` (or `Platform does not check server keys.`). A wrong
   key stops it with `[FAIL] KEY MISMATCH: the platform refused the key in
   HULLBREACH_PLATFORM_SERVER_KEY.` and the steps to fix it.
2. In a **second** terminal, join as a signed-in player:
   ```
   .\play.cmd join 127.0.0.1 --platform --headless
   ```
   It asks for your platform username and password (the password is
   hidden and not stored), signs in, and starts a hidden player. You
   should see `[PASS] client: in the match (...)` and, in the server
   window, `peer 2: admitted verified name='<your name>'`.
3. Join as a guest (no account):
   ```
   .\play.cmd join 127.0.0.1 --headless
   ```
   You should see `[FAIL] client: Hullbreach platform: server rejected
   this client: This server requires a Hullbreach account. Sign in and
   reconnect.` That `[FAIL]` is the point of the demo: the server refused
   a player who is not signed in.

Demo/testing aid: instead of typing at the prompt, a script may set
`HULLBREACH_PLATFORM_USERNAME` and `HULLBREACH_PLATFORM_PASSWORD`, or a
token it already has in `HULLBREACH_PLATFORM_TOKEN` (plus
`HULLBREACH_PLATFORM_USERNAME`). A game started with
`HULLBREACH_PLATFORM_TOKEN` signs in from it without the sign-in screen
and logs `signed in from HULLBREACH_PLATFORM_TOKEN ... (demo/testing
aid)`. Secrets only ever go in the environment, never on the command
line.

### 4.6 Players on another computer

Each copy signs in with the platform itself, so a player on another
computer needs `--platform-url http://<platform computer's IP>:8000`
on its `play` commands (the game option `-hullbreachPlatformUrl`), and the platform must listen on the network instead of only
on `127.0.0.1` (the platform's `--host` option, for example
`uv run hullbreach_server --host 0.0.0.0`, plus a firewall rule for TCP
port 8000). If the platform runs inside WSL, other computers cannot
reach it without extra WSL network setup; for a demo, keep every copy on
the platform's computer, or ask in chat.

Note: on the direct (LAN) path the sign-in token travels unencrypted;
over Relay it is encrypted. Use test accounts for demos.

## Guide 5: dedicated Linux server

A dedicated server is the game built with no window, which only runs
matches. Players connect to it with `play join` from Guide 3.

1. Build it. On any computer with Unity and the **Linux Dedicated Server
   Build Support** module (0.4; the doctor's `unity.modules` check says
   whether you have it), close Unity and type:
   ```
   .\play.cmd build server --target linux
   ```
   (`./play.sh build server --target linux` on macOS/Linux.) You get
   `Builds/linux-server` with `HullbreachServer.x86_64` inside. The
   server opens the match scene (`MultiplayerGame`) by itself.
2. Get it onto the Linux machine. Easiest: clone the game repo there
   too and copy the `Builds/linux-server` folder into the clone's
   `Builds` folder (for example zip it, or
   `scp -r Builds/linux-server user@host:~/project-hullbreach-game/Builds/`).
3. On the Linux machine, in the clone:
   ```
   ./play.sh server
   ```
   It prints `Server on UDP port 7979.` and keeps running; the log is in
   `Logs/play/server.log` and says `Hullbreach direct: role=server
   listen=0.0.0.0:7979 scene=MultiplayerGame`. Add `--port <n>` for
   another port, and `--platform --require-auth` (with
   `HULLBREACH_PLATFORM_SERVER_KEY` set, Guide 4.5) to require platform
   sign-in. Stop it with **Ctrl+C**.
   If it says `could not start`, the copy lost the "may run" flag (zip
   files do that); type `chmod +x Builds/linux-server/HullbreachServer.x86_64`
   and try again.
4. Open UDP 7979 on that machine (`sudo ufw allow 7979/udp`; the doctor's
   `net.firewall` check shows the rule) and, for a cloud server, in its
   provider's firewall / security group.
5. Players type `.\play.cmd join <server IP>` (Guide 3.4).

To keep it running after you log out, run it inside `tmux` or `screen`,
or as a systemd service. Running the server binary without the clone is
in the [appendix](#appendix-doing-it-by-hand).

No Dedicated Server module at all? `play server` on any computer runs
the ordinary game build as a server with no window
(`-batchmode -nographics -hullbreachServer`); it prints `No dedicated
server build here; running the game build as a server (no window).`

## Demo script for standup

A five-minute run. Do the "before" part ahead of time.

Before standup:

- [ ] `git pull` on `main`, then `.\doctor.cmd` shows no `[FAIL]`.
- [ ] `.\play.cmd build` done (Development Build), copied to a second
      computer if you have one.
- [ ] `.\play.cmd online --headless` prints `[PASS] Unity Services
      sign-in, lobby and Relay work from here.`
- [ ] `.\play.cmd two-local --headless` prints two `[PASS]` lines.
- [ ] For the sign-in part: platform running, two test accounts made,
      and `.\doctor.cmd --demo platform --login` passes.

During standup:

1. [ ] (30 s) Say what it is: Unity game + platform, diagram above.
2. [ ] (1 min) Single player: `DemoScene`, Play, place two blocks, Tab,
      fly, fire.
3. [ ] (2 min) Multiplayer: `.\play.cmd online`; sign in; **HOST WITH
      RELAY**; read out the invite code; second copy
      (`.\play.cmd online --profile friend2`, or a second computer)
      joins by code; both names under **PLAYERS**; **START GAME**; point
      at `Replicated ships: 2`; fly one ship and show it move on the
      other screen.
4. [ ] (1 min) Platform: point at `Platform: signed in as <name>` on the
      HUD; second copy plays offline and shows `Platform: guest (...)`.
5. [ ] (30 s, optional) Restart the host with
      `.\play.cmd online --require-auth`; offline player joins and gets
      `Platform: rejected - This server requires a Hullbreach account.
      Sign in and reconnect.` (Or the no-click version in 4.5.)

If anything fails live, fall back to step 2 and show the logs later.

## Appendix: doing it by hand

Everything the `play` command does, step by step, for when you cannot
run it (no Python allowed, an unusual computer) or want to see what
happens underneath. The guides above are the easy way.

### Make a build in Unity

1. Open the project in Unity (0.5). **File -> Build Profiles**.
2. In the list on the left pick **Windows** (or **macOS** on a Mac). If
   it is not the active platform, click **Switch Platform** and wait.
3. Check the **Scene List**: `LobbyScene` must be ticked and first, and
   `MultiplayerGame` must be ticked. (The repo lists `LobbyScene`,
   `MultiplayerGame`, `DemoScene`, `RocketScene`, in that order.)
4. Tick **Development Build**. This matters for Guide 4: the game only
   allows plain `http://` platform addresses in development builds.
5. Click **Build**, make a new empty folder **outside** the repo (for
   example `C:\dev\hullbreach-build`), and select it.
6. Wait. The first build takes several minutes.

You should see: the folder contains `Project Hullbreach.exe` (named after
the product name in Player Settings), a `Project Hullbreach_Data` folder,
and some `.dll` files. On a Mac it is `Project Hullbreach.app`.
Double-click the `.exe` to start the game at the lobby.

For a Linux server: pick **Linux Server** in step 2 (needs the Linux
Dedicated Server Build Support module), and name the file
`HullbreachServer`; you get `HullbreachServer.x86_64` plus a
`HullbreachServer_Data` folder.

The same thing without the window, from a terminal (close Unity first;
the `play build` command runs exactly this):

```
"C:\Program Files\Unity\Hub\Editor\6000.0.43f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath . -buildTarget Win64 -executeMethod Hullbreach.Editor.HullbreachBuild.BuildFromCommandLine -hullbreachBuildKind client -hullbreachBuildTarget windows -logFile Logs\build.log
```

### Start the game with options

In a terminal, go to the build folder, then start the game with the
options from the table in Guide 3 (and 4.3 for the platform):

- **Windows PowerShell** (the `&` and quotes are needed because the name
  has a space):
  ```
  cd C:\dev\hullbreach-build
  & ".\Project Hullbreach.exe" -hullbreachDirect
  ```
- **Windows Command Prompt (cmd)**:
  ```
  cd /d C:\dev\hullbreach-build
  "Project Hullbreach.exe" -hullbreachDirect
  ```
- **Linux** (a Linux player build; the file name ends in `.x86_64`):
  ```
  chmod +x ./ProjectHullbreach.x86_64
  ./ProjectHullbreach.x86_64 -hullbreachDirect
  ```
- **macOS**:
  ```
  open -n "Project Hullbreach.app" --args -hullbreachDirect
  ```

Replace `-hullbreachDirect` with what you need:

| You want | Options |
| --- | --- |
| Host a direct match (Guide 3.3) | `-hullbreachDirect` |
| Join a direct match (Guide 3.4) | `-hullbreachConnect 192.168.1.23` |
| Another port | add `-hullbreachPort 7980` on both sides |
| A server with no window and no player | `-batchmode -nographics -hullbreachServer` |
| Lobby with a platform elsewhere (Guide 4.4) | `-hullbreachPlatformUrl http://<ip>:8000` |
| Lobby or server that requires sign-in (Guide 4.5) | `-hullbreachRequirePlatformAuth` |
| No window at all | add `-batchmode -nographics` |
| Your own log file per copy | add `-logFile C:\dev\host.log` |

A Linux dedicated server build, by hand:

```
cd ~/hullbreach-server
chmod +x ./HullbreachServer.x86_64
./HullbreachServer.x86_64 -batchmode -nographics -logFile -
```

A server build always listens, on UDP 7979 unless you add
`-hullbreachPort <n>`; `-logFile -` prints the log to the terminal.

The server key and require-auth can also come from the environment
(PowerShell; replace the placeholder, never commit the real key):

```
$env:HULLBREACH_REQUIRE_PLATFORM_AUTH = "1"
$env:HULLBREACH_PLATFORM_SERVER_KEY = "<dev server key>"
& ".\Project Hullbreach.exe"
```

Linux/macOS: `HULLBREACH_REQUIRE_PLATFORM_AUTH=1 HULLBREACH_PLATFORM_SERVER_KEY='<dev server key>' ./HullbreachServer.x86_64 -batchmode -nographics`.

### Firewall rule by hand

Windows (a terminal opened **as administrator**: Start -> type
`cmd` -> right-click **Command Prompt** -> **Run as administrator**; the
doctor's `--fix-firewall` runs the same):

```
netsh advfirewall firewall add rule name="Project Hullbreach UDP 7979" dir=in action=allow protocol=UDP localport=7979
```

Linux with ufw: `sudo ufw allow 7979/udp`. macOS: click **Allow** when
the game first asks about incoming connections (System Settings ->
Network -> Firewall -> Options lists it afterwards).

## Troubleshooting

Start with the doctor: `.\doctor.cmd` (macOS/Linux `./doctor.sh`). Each
`[FAIL]` has fix steps and a link back here. To ask a teammate for help,
run `.\doctor.cmd --collect` and send them the zip it makes: it holds the
doctor's report and every log, with passwords, tokens and keys removed.

### Doctor check ids

Every line of the doctor starts with a check id. This table says which
demos each check matters for and where the README explains it.

| Check id | What it checks | Demos | Read |
| --- | --- | --- | --- |
| `os.shell` | Computer and terminal | all | [Step 1: run the doctor](#step-1-run-the-doctor) |
| `git.installed` | Git is installed | all | [0.2 Install Git](#02-install-git) |
| `repo.clone` | This folder is a git clone | all | [0.3 Download (clone) the game](#03-download-clone-the-game) |
| `repo.uptodate` | Up to date with origin/main | all | [0.3 Download (clone) the game](#03-download-clone-the-game) |
| `repo.path` | Folder location is Unity-friendly | all | [0.3 Download (clone) the game](#03-download-clone-the-game) |
| `project.files` | Unity project files | all | [Setup and Unity](#setup-and-unity) |
| `git.lfs` | Git LFS (large files) | all | [0.3 Download (clone) the game](#03-download-clone-the-game) |
| `unity.hub` | Unity Hub | all | [0.4 Install Unity Hub and the exact Unity version](#04-install-unity-hub-and-the-exact-unity-version) |
| `unity.editor` | Unity 6000.0.43f1 | all | [0.4 Install Unity Hub and the exact Unity version](#04-install-unity-hub-and-the-exact-unity-version) |
| `unity.modules` | Unity build modules | all | [0.4 Install Unity Hub and the exact Unity version](#04-install-unity-hub-and-the-exact-unity-version) |
| `unity.cpp` | C++ tools for IL2CPP builds | all | [Setup and Unity](#setup-and-unity) |
| `unity.running` | Unity editor is closed | all | [Setup and Unity](#setup-and-unity) |
| `unity.license` | Unity license works (--deep) | all | [Setup and Unity](#setup-and-unity) |
| `unity.compile` | Project compiles (--deep) | all | [Setup and Unity](#setup-and-unity) |
| `services.linked` | Linked to Unity online services | online | [2.1 Link the project to Unity's online services (once per computer)](#21-link-the-project-to-unitys-online-services-once-per-computer) |
| `services.reachable` | Unity Lobby and Relay reachable | online | [Relay lobby](#relay-lobby) |
| `signin.prefab` | Sign-in screen in the lobby | platform | [4.1 The sign-in screen is already in the project](#41-the-sign-in-screen-is-already-in-the-project) |
| `build.present` | Game build exists | all | [Build the game with one command](#build-the-game-with-one-command) |
| `build.fresh` | Build matches the code | all | [Build the game with one command](#build-the-game-with-one-command) |
| `build.scenes` | Build has the right scenes | all | [Build the game with one command](#build-the-game-with-one-command) |
| `build.server` | Server build | guest, platform | [Build the game with one command](#build-the-game-with-one-command) |
| `net.port` | Game port is free | guest, platform | [Direct / LAN](#direct--lan) |
| `net.firewall` | Firewall lets the game in | guest, platform | [Direct / LAN](#direct--lan) |
| `net.lan` | Address to give a friend | guest, platform | [3.2 Find the host's IP address](#32-find-the-hosts-ip-address) |
| `platform.url` | Platform address | platform | [4.2 Start the platform and make an account](#42-start-the-platform-and-make-an-account) |
| `platform.health` | Platform is running | platform | [4.2 Start the platform and make an account](#42-start-the-platform-and-make-an-account) |
| `platform.ready` | Platform database answers | platform | [Platform sign-in (Guide 4)](#platform-sign-in-guide-4) |
| `platform.migrations` | Platform accounts work | platform | [Platform sign-in (Guide 4)](#platform-sign-in-guide-4) |
| `platform.http` | Plain http allowed by this build | platform | [Platform sign-in (Guide 4)](#platform-sign-in-guide-4) |
| `platform.loopback` | Platform address works across computers | platform | [4.6 Players on another computer](#46-players-on-another-computer) |
| `platform.serverkey.set` | Game server key is set | platform | [4.5 Demo with sign-in required](#45-demo-with-sign-in-required) |
| `platform.login` | Real sign-in (--login) | platform | [4.2 Start the platform and make an account](#42-start-the-platform-and-make-an-account) |
| `platform.serverkey.accepted` | Platform accepts the server key | platform | [4.5 Demo with sign-in required](#45-demo-with-sign-in-required) |
| `dev.dotnet` | dotnet for the plain C# tests (developers) | developers | [Checks](#checks) |
| `logs.locations` | Where the logs are | all | [Where the logs are](#where-the-logs-are) |

### Where the logs are

Most problems are explained in the log. Search it for `Hullbreach` or
`Exception`.

- **In the editor**: the **Console** window. Full file: Windows
  `%LOCALAPPDATA%\Unity\Editor\Editor.log`, macOS
  `~/Library/Logs/Unity/Editor.log`, Linux `~/.config/unity3d/Editor.log`.
- **A build** (`Player.log`; the previous run is `Player-prev.log`):
  - Windows: `%USERPROFILE%\AppData\LocalLow\Company of Theseus\Project Hullbreach\Player.log`
    (paste that into the File Explorer address bar; `AppData` is hidden
    otherwise).
  - macOS: `~/Library/Logs/Company of Theseus/Project Hullbreach/Player.log`
  - Linux: `~/.config/unity3d/Company of Theseus/Project Hullbreach/Player.log`
- **Started with `play`**: each copy writes its own log to
  `Logs/play/<name>.log` inside the repo folder (`host.log`,
  `client.log`, `server.log`, `build-client.log`, ...).
  `.\play.cmd logs` prints the end of the newest one (`./play.sh logs`
  on macOS/Linux).
- **To send to a teammate**: `.\doctor.cmd --collect` zips the logs
  and the doctor's report with passwords, tokens and keys removed, and
  prints where the zip is.
- Any build can be told where to write it: add
  `-logFile C:\dev\host.log` (Windows) or `-logFile -` (Linux, print to
  the terminal). Use a different file per copy when running two on one
  computer.

When asking for help, paste the command you ran and the last 30 lines of
the log.

### Setup and Unity

**"git is not recognized" / "git: command not found"**
Cause: Git is not installed, or the terminal was opened before it was.
Fix: install Git (0.2), then close and reopen the terminal.

**"This project was saved with a different editor version" / Unity
offers to upgrade**
Cause: you opened it with a Unity other than 6000.0.43f1.
Fix: click **Quit** (never Continue/upgrade), install 6000.0.43f1 (0.4),
and in Unity Hub's project list pick that version next to the project.
If you already upgraded, run `git checkout -- Packages ProjectSettings`
in the repo folder to undo it.

**Console full of red package errors after opening (for example about
`com.unity.netcode` or `com.unity.services.multiplayer`)**
Cause: package download failed (no internet, proxy) or wrong Unity
version.
Fix: check the version, make sure you are online, close Unity, delete
the `Library` folder inside the project (it is rebuilt), reopen. Do not
edit `Packages/manifest.json`.

**Everything is pink**
Cause: shaders still compiling, or a broken first import.
Fix: wait for the import; see "Things that bite people" below.

**The build fails mentioning IL2CPP, C++, or Visual Studio**
Cause: IL2CPP needs a C++ compiler.
Fix: the quickest way out is `.\play.cmd build --backend mono` (or
`./play.sh build --backend mono`), which skips IL2CPP; the game plays
the same. For IL2CPP builds install Visual Studio with **Desktop
development with C++** (Windows) or Xcode (Mac), then restart Unity.
Doctor check `unity.cpp` says which one you have.

**Build option for Linux / Linux Server missing or greyed out**
Cause: the module is not installed.
Fix: Unity Hub -> Installs -> the gear next to 6000.0.43f1 -> **Add
modules** -> tick Linux Build Support and Linux Dedicated Server Build
Support. Doctor check `unity.modules` lists what is installed and
missing.

### Relay lobby

**Lobby status says `Online service error`; log says `Hullbreach online
services could not start: ...`**
Cause: the project is not linked to Unity Cloud, you are not in its
organization, Lobby/Relay are off for that project, or no internet.
Fix: Guide 2.1. Read the text after `could not start:` for which one.
After linking, make a fresh build: builds keep the project ID they were
built with.

**Host can't see the joiner's lobby, or REFRESH finds nothing**
Cause: the two copies were built from editors linked to different Unity
Cloud projects, or the lobby is private.
Fix: build both from the same linked project; for private lobbies use
**JOIN BY CODE**.

**Join code rejected / `Hullbreach lobby operation failed: ...` when
joining**
Cause: typo (codes are short and easy to mistype), the host left or
the lobby was deleted, the lobby is full, or the password is wrong.
Fix: copy-paste the **INVITE CODE** exactly, make sure the host is still
in the waiting room, enter the password if there is one. `Enter a lobby
code.` means the code box was empty.

**Second copy on the same computer joins as the same player / is "already
a member"**
Cause: two copies of one build share the saved Unity anonymous sign-in.
Fix: start the second copy with its own profile:
`.\play.cmd online --profile friend2` (any name works). Or use a second
computer, or Multiplayer Play Mode with separate virtual players.

**Match loads but stays on `Waiting for Netcode connection...`**
Cause: the Relay connection did not come up (network blocks UDP), or in
direct mode see Guide 3 below.
Fix: try another network (phone hotspot); check the log for transport
errors.

### Direct / LAN

**Joiner stays on `Waiting for Netcode connection...`**
Cause, most likely first:
1. The build is older than the S47-5 fix and starts on `LobbyScene`
   instead of `MultiplayerGame`, so the server never admits anyone.
   `git pull` and rebuild; the log should say `Hullbreach direct:
   loading MultiplayerGame`.
2. The host's firewall blocks UDP 7979. Easiest: on the host run
   `.\doctor.cmd --demo guest --fix-firewall` and accept the
   administrator prompt. By hand on Windows: Start -> "Windows Defender
   Firewall" -> **Allow an app or feature through Windows
   Defender Firewall** -> **Change settings** -> tick **Private** for
   Project Hullbreach (or remove it and start the host again to get the
   prompt). Linux: `sudo ufw allow 7979/udp`. If someone clicked
   **Cancel** on the Windows prompt, Windows adds a **Block** rule named
   `project hullbreach.exe` that beats any Allow rule; the doctor's
   `net.firewall` check says so and shows how to delete it.
3. Wrong IP: re-check with `ipconfig` / `hostname -I` on the host; check
   the log for `Invalid -hullbreachConnect address`.
4. The computers cannot reach each other (guest or campus Wi-Fi). Test
   with `ping 192.168.1.23` from the joiner; if it times out, use another
   network.
5. Different ports: both sides must use the same `-hullbreachPort` (or
   neither).

**Second copy on the same computer fails or the log mentions binding the
port / address in use**
Cause: both copies try to listen on 7979 (two hosts, or a host plus a
server).
Fix: only one copy may host. For a one-computer test use
`.\play.cmd two-local`, which starts one host and one joiner. To run two
hosts, give the second one another port: `.\play.cmd host --port 7980`.

### Platform sign-in (Guide 4)

**Sign-in shows `Cannot reach the Hullbreach servers (...). You can
still play offline.`**
Cause: the platform API is not running, or it is at a different address.
Fix: open `http://127.0.0.1:8000/api/docs` in a browser on the same
computer. If it does not load, start the platform (platform README "Run
it"). If the platform is elsewhere, pass
`-hullbreachPlatformUrl http://<ip>:8000`.

**Sign-in error mentions `request blocked`**
Cause: a plain `http://` address in a non-development build.
Fix: rebuild with **Development Build** ticked, or use an `https://`
platform address.

**`Wrong username or password.`**
Cause: what it says; usernames are exact.
Fix: re-type, or register a new account on the website.

**`Too many sign-in attempts. Wait a minute and try again.`**
Cause: the platform rate-limits failed logins per username.
Fix: wait a minute.

**`Enter a username and password.`** Both boxes must be filled.

**There is no sign-in screen at all**
Cause: your copy is older than the sign-in screen, or someone removed
it from `LobbyScene` without saving the regenerated scene.
Fix: `git pull`, check for `Assets/Prefabs/UI/SignInCanvas.prefab`, run
**Hullbreach -> Online -> Add sign-in screen to lobby scene**, save, and
rebuild.

**HUD says `Platform: guest (...)` although you signed in**
Cause: the server could not confirm the session (platform down or
unreachable *from the host*, session expired), or you signed out.
Fix: the reason is printed after the name, e.g. `Platform: guest (Guest
#2) - <reason>`. Make sure the host can reach the platform (the host's
`-hullbreachPlatformUrl` must work from the host's computer), sign in
again, rejoin.

**Kicked with `Platform: rejected - ...`**
Cause and fix by message (the server has `-hullbreachRequirePlatformAuth`):
- `This server requires a Hullbreach account. Sign in and reconnect.`
  You chose Play offline. Sign in and rejoin, or restart the host
  without the flag.
- `Your platform session is invalid or expired. Sign in again.` Sign
  out and in again.
- `Could not verify your platform session (platform refused this game
  server's key ...)` The host's `HULLBREACH_PLATFORM_SERVER_KEY` is
  missing or not one of the platform's game server keys. Fix it on the
  host (Guide 4.5); doctor check `platform.serverkey.accepted` tests it.
- `Could not verify your platform session (...)` with any other reason:
  the host cannot reach the platform; the server refuses when unsure.
  Fix the host's platform URL or start the platform.
- `No platform sign-in received in time.` The player's game did not
  send its sign-in within 10 seconds; usually a stalled connection.
  Rejoin.

### Unsigned build warnings

**Windows: "Windows protected your PC"** Click **More info** -> **Run
anyway**. Our builds are not code-signed.

**macOS: "Project Hullbreach" cannot be opened / Apple cannot check it**
Right-click (Control-click) the app -> **Open** -> **Open**; on newer
macOS go to **System Settings -> Privacy & Security** and click **Open
Anyway**. If it says the app is "damaged" after copying it from another
computer, run `xattr -dr com.apple.quarantine "Project Hullbreach.app"`
in its folder.

---

# Developer guide

Everything below is the reference material for working on the code.
Some of it predates the networking work; where it disagrees with
**Start here** above, Start here is newer.


## Where the project actually is right now

(This section was written before the networking work; for what runs
today see **Start here** above and [docs/netcode.md](docs/netcode.md).)

Read this before anything else, because the shape of the repo changed and
the old README did not keep up.

We started from Unity's **Competitive Action Multiplayer** template: a
first-person shooter with rockets, Netcode for Entities, a menu flow, and
a dedicated-server build. That template is **no longer in `Assets/`**. In
`feat(player): Added basic up and down force additions.` the 751 template
asset files were deleted and replaced with a small 2D prototype: one
scene, one script, a rocket with two thrusters and a gun, and a couple of
planets.

So there are two truths to hold at once:

- **`Packages/manifest.json` and `ProjectSettings/` still largely describe
  the template.** Netcode for Entities and Unity Transport are now used by
  the `Hullbreach.NetCode.Entities` adapter; Unity Physics, Entities Graphics,
  UGS, URP, and the Input System remain installed from the template.
- **`Assets/` is a 2D sandbox.** `Rigidbody2D`, the legacy `Input` class,
  sprites. It is the fastest thing that could demonstrate S39, and it is
  what you will be editing.

That gap is the main thing that will confuse you. When the ship builder
and the netcode land, one of the two has to move. Nothing has been
decided; `TODO.md` tracks it.

`ProjectSettings/EditorBuildSettings.asset` is downstream of the same
deletion: it still lists `MainMenu.unity`, `GameSceneOld.unity`, and
`GameResources.unity`, none of which exist. Builds and
`SceneManager.LoadScene` by index will not behave until that list is
repointed at `RocketScene`.

## How a Unity project is laid out

If you have not used Unity, this is the whole mental model. Unity is not
a normal C# solution; the code is the smaller half of the project.

| Folder | Committed? | What it is |
| --- | --- | --- |
| `Assets/` | **yes** | Everything you author: scripts, scenes, prefabs, sprites, materials. This is the project. |
| `Packages/` | **yes** | `manifest.json` names the package versions; `packages-lock.json` pins the resolved graph. Edit it through Window -> Package Manager, not by hand. |
| `ProjectSettings/` | **yes** | Editor and player settings as YAML. Physics, tags, layers, input, build settings, the pinned editor version. Every checkbox you tick in Edit -> Project Settings lands here, so these are shared config and they show up in diffs. |
| `Library/` | no | The import cache. Unity rebuilds it from `Assets/` on first open, and it is huge. Ignored. |
| `Temp/`, `Logs/`, `obj/` | no | Scratch, logs, and build intermediates. Ignored. |
| `UserSettings/` | no | Your personal editor layout. Ignored. |
| `*.csproj`, `*.sln` | no | Regenerated by Unity for your IDE on every reimport. Never commit them; never edit them. Ignored. |

Three Unity-specific facts cause most of the pain on a team:

1. **Every asset has a `.meta` file next to it, and the `.meta` is the
   important one.** It holds the asset's GUID, and every reference in
   every scene and prefab points at that GUID rather than at a path. Move
   or rename an asset *from inside the Unity editor* and the meta comes
   along and nothing breaks. Move it in Explorer or with `git mv` and
   Unity mints a fresh GUID on the next import, which silently detaches
   every reference to it for everyone. Always `git add` the `.meta`
   alongside the asset. `scripts/check_unity_tree.sh` fails the build on
   a missing or orphaned meta for exactly this reason.

2. **Scenes and prefabs are large YAML files full of numeric fileIDs.**
   They do not merge. Two people editing the same scene produces a
   conflict no human should resolve by hand. Say in chat which scene you
   are in.

3. **A scene is a graph of GameObjects; a GameObject is a bag of
   Components; a `MonoBehaviour` is your C# attached as a component.**
   Public fields on a `MonoBehaviour` appear in the Inspector and are
   serialized *into the scene*, not into the script. `PlayerSingle.cs`
   declares `public float speedPerThruster;` with no value; the `10` it
   actually runs with lives in `RocketScene.unity`. Changing the default
   in C# will not change a value already saved in the scene.

## Where things live

```
Assets/
  PlayerSingle.cs           the only gameplay script; ship control
  Scenes/RocketScene.unity  the only scene
  DefaultVolumeProfile.asset
  UniversalRenderPipelineGlobalSettings.asset
  UIToolkit/SessionInfo/    one leftover font asset from the template
Packages/manifest.json      package versions (edit via Package Manager)
ProjectSettings/            editor and player settings, all committed
scripts/check_unity_tree.sh what CI runs on every PR
.github/workflows/ci.yml    the PR gate
```

That is the entire authored tree. Do not go looking for `Assets/Scripts/`
or `Assets/Prefabs/`: nothing has needed them yet. When you add the
second or third script, make `Assets/Scripts/` and move `PlayerSingle.cs`
into it **from the Unity Project window** so the meta travels with it.

### RocketScene

Two root objects:

- **`Camera`**: a plain orthographic 2D camera, not parented to the ship.
- **`2D world`**: everything else. Two `Circle` planets, two loose
  `Triangle` sprites, and `PlayerShip`.

`PlayerShip` carries a `Rigidbody2D` and the `PlayerSingle` component,
and has five children:

| Child | Role |
| --- | --- |
| `Square`, `Square (1)` | hull sprites, no script |
| `Thruster`, `Thruster (1)` | thrust application points; each has one child, `Fire`, the flame sprite that gets toggled |
| `Gun` | the turret's application point |

The `Rigidbody2D` is worth knowing in detail, because it is what "feels"
like the ship:

- `Use Auto Mass` is **on**, so mass (currently ~5.69) is derived from the
  child colliders. Add hull, get heavier, for free. That is already half
  of S39's "a heavy ship feels heavy".
- `Gravity Scale` is **0**: it is space, there is no world down.
- `Angular Damping` is **2**, which is why the ship stops spinning on its
  own. `Linear Damping` is 0, so it never stops drifting.

## Player movement (S39)

This is the story being worked on:
[#19 S39 Fly a ship that handles like it was built](https://github.com/lognd/project-hullbreach-game/issues/19),
under epic [#18 E11 Flight physics and gravity](https://github.com/lognd/project-hullbreach-game/issues/18),
Sprint 1.

> As a player, I want thrusters and control fins to apply force and torque
> at the point where they are mounted, so that where I put a thruster
> changes how the ship turns, and a heavy ship feels heavy.

Acceptance criteria, from the story:

1. A thruster mounted off the center of mass produces rotation as well as
   translation.
2. Doubling a ship's mass with no added thrust roughly halves its
   acceleration.
3. Control fins change turning behavior in a way a player can feel.

All of it used to live in `Assets/PlayerSingle.cs`, which has since been
replaced by `Hullbreach.Ship.ShipBody`/`Hullbreach.Game.ShipController`
(see `docs/architecture.md`); this section is kept for the physics
reasoning, which still applies.
The approach there is already the right one for criterion 1:
`AddForceAtPosition` applies force *at the thruster's world position*, so
an off-center thruster generates torque as a consequence of the physics
rather than as a special case. Criterion 2 comes free from `Use Auto Mass`.

Four things to know before you touch it.

**Input is the legacy system, and that is currently fine.**
`ProjectSettings` has `activeInputHandler: 2`, which is "Both", so the old
`Input.GetButton("Jump")` calls work even though `com.unity.inputsystem`
is installed. `Jump`, `Fire1`, and `Horizontal` are the stock axes defined
in `ProjectSettings/InputManager.asset`. If you switch the project to the
new Input System only, every line in `PlayerSingle.cs` that reads input
breaks at once. S27
([#4, Adjust settings](https://github.com/lognd/project-hullbreach-game/issues/4))
wants rebindable keys, which is the natural moment to migrate, but that
is a separate change from this one.

**Forces are applied in `Update()`, and should be in `FixedUpdate()`.**
`Update` runs once per rendered frame; the 2D physics step runs on a fixed
timer. Applying force from `Update` means a 144 Hz machine accelerates the
ship differently from a 60 Hz machine, and it will differ again on the
headless server (S47). Move the `AddForceAtPosition` / `AddTorque` calls
into `FixedUpdate`, and leave edge-triggered input reads (`GetButtonDown`
/ `GetButtonUp`) in `Update`, latching them into a field that
`FixedUpdate` consumes: `GetButtonDown` is true for exactly one frame and
will be missed outright if you poll it from `FixedUpdate`.

**Steering is currently coupled to thrust, and probably by accident:**

```csharp
rb.AddTorque(Input.GetAxis("Horizontal") * -0.1f * rb.totalForce.y, ForceMode2D.Force);
```

`rb.totalForce` is the force accumulated on the body *this physics step*.
With the thrusters off it is near zero, so turning does nothing at all
until you hold Jump. That may be an intentional "you can only steer under
power" rule, or it may be a leftover. It should be a decision rather than
an accident, and criterion 3 (control fins you can feel) is where it
gets settled.

**The flame toggle is edge-triggered and will desync.** The `Fire` child
sprites are enabled inside `if (Input.GetButton("Jump"))`, which runs
every frame the key is held, but disabled only in the
`else if (Input.GetButtonUp("Jump"))` branch. Anything that stops thrust
without a key release (running out of fuel, a destroyed thruster, losing
window focus) leaves the flame on. Drive the sprite from the same boolean
that drives the thrust.

None of this is blocking. Criterion 1 essentially passes already; the work
is in making the handling deliberate and framerate-independent.

## What is installed

- **Unity 6000.0.43f1** (Unity 6). Everyone uses exactly this version.
  `ProjectSettings/ProjectVersion.txt` pins it and CI checks it.
- **Universal Render Pipeline** for rendering; URP 2D sprites are what the
  current scene uses.
- **Input System** package installed, legacy Input Manager still active
  (`activeInputHandler: 2`, "Both"). The current script uses the legacy one.
- **Netcode for Entities**, **Unity Transport**, and **Unity Multiplayer
  Services** back the runtime bridge in
  `Assets/Scripts/Hullbreach.NetCode.Entities`; the existing engine-free
  protocol remains in `Hullbreach.Net`. The pre-game `LobbyScene` uses an
  editable uGUI/TextMesh Pro prefab for Relay hosting, public browsing, join
  codes, private lobbies, optional native Lobby passwords, a waiting room, and
  host-controlled game start. `MultiplayerGame` reuses the demo environment and
  adds ghost-synchronized ship/projectile motion, server-authoritative flight,
  collisions, weapons, damage, structural failure, and in-match ship building.
  No Asset Store content is required.
- **UI Toolkit** for menus, when menus exist (S26).

## Getting started with Unity

1. **Install Unity Hub** from https://unity.com/download. Make a Unity
   account if you do not have one; the Personal license is free and is
   what we use.
2. **Install the editor.** In the Hub: Installs -> Install Editor ->
   Archive -> pick **6000.0.43f1** exactly. Not the newest 6000.x, this
   one. During the install tick these modules:
   - Microsoft Visual Studio Community (Windows) or leave it off and use
     VS Code / Rider, your call
   - **Linux Dedicated Server Build Support** (the server we deploy)
   - Windows Build Support (IL2CPP) if you are on Windows, Mac Build
     Support if you are on a Mac

   It is a big download. Start it before you clone.
3. **Clone the repo** somewhere without spaces in the path:
   ```
   git clone https://github.com/lognd/project-hullbreach-game.git
   ```
   Send me your GitHub username first if you are not a collaborator yet.
   Git will want a personal access token instead of your password; the
   platform README walks through making one.
4. **Open the project.** Hub -> Projects -> Add -> Add project from disk
   -> pick the cloned folder. The first open imports everything and
   builds the `Library/` folder. That takes a while, five to fifteen
   minutes depending on your machine. It is normal. Do not commit
   `Library/`; it is ignored.
5. **Play it.** Open `Assets/Scenes/DemoScene.unity` (double-click in
   the Project window) and press Play; see [Guide 1](#guide-1-single-player-quick-look)
   and [Playing the demo](#playing-the-demo). For multiplayer start from
   `Assets/Scenes/LobbyScene.unity` ([Guide 2](#guide-2-multiplayer-through-the-relay-lobby)).
   The older `RocketScene` is still there: hold **Space** to fire both
   thrusters, **A**/**D** to turn while thrusting, and **left mouse** to
   fire the gun, which shoves the ship backward.
6. **Pick an editor for code.** Edit -> Preferences -> External Tools ->
   External Script Editor. VS Code with the "Unity" extension works, and
   `.vscode/` already has the attach-debugger config. Rider is great if
   you have the student license. Whatever you pick, let Unity generate
   the `.csproj` files (they are ignored by git).

### Building the dedicated server

`./play.sh build server --target linux` (or `.\play.cmd build server`
for a Windows server). It calls `HullbreachBuild.BuildFromCommandLine` in
`Assets/Editor/Hullbreach.Editor/HullbreachBuild.cs` headless and writes
to `Builds/<target>-server/`; the same entry point is under
**Hullbreach -> Build** menu in the editor. The result is a headless binary
that hosts a match: it opens `MultiplayerGame` and listens on UDP 7979
(or `-hullbreachPort`) without further options; the full steps are in
[Guide 5](#guide-5-dedicated-linux-server). A CI build for the server is
on the list in `TODO.md`.

## Sprint 1, and where each story lives

Stories come from the Module 4 story map; full detail, points, and status
live in Jira, and each one is mirrored as a GitHub issue here. The story
map's backbone is the activity a player moves through; for this repo the
relevant activities are **Build a ship** and **Fight**.

| Issue | Story | Epic |
| --- | --- | --- |
| [#3](https://github.com/lognd/project-hullbreach-game/issues/3) | S26 Start from a title screen | E8 Game shell |
| [#4](https://github.com/lognd/project-hullbreach-game/issues/4) | S27 Adjust settings | E8 Game shell |
| [#5](https://github.com/lognd/project-hullbreach-game/issues/5) | S28 Host or join a LAN match | E8 Game shell |
| [#8](https://github.com/lognd/project-hullbreach-game/issues/8) | S30 Place a block with two clicks | E9 Ship builder |
| [#9](https://github.com/lognd/project-hullbreach-game/issues/9) | S31 Remove and undo blocks | E9 Ship builder |
| [#10](https://github.com/lognd/project-hullbreach-game/issues/10) | S32 Build around a core | E9 Ship builder |
| [#11](https://github.com/lognd/project-hullbreach-game/issues/11) | S33 Choose from a block palette | E9 Ship builder |
| [#19](https://github.com/lognd/project-hullbreach-game/issues/19) | **S39 Fly a ship that handles like it was built** | E11 Flight physics |
| [#23](https://github.com/lognd/project-hullbreach-game/issues/23) | S42 Shoot a basic cannon | E12 Combat |
| [#27](https://github.com/lognd/project-hullbreach-game/issues/27) | S46 Win by breaching the core | E12 Combat |
| [#29](https://github.com/lognd/project-hullbreach-game/issues/29) | S47 Run matches on an authoritative server | E13 Networking |

Sprint 1 is **9/16 - 9/27**, and its goal is a walking skeleton: an
account, a ship, a LAN fight. Sprint 2 (**10/5 - 10/23**) is real physics,
real internet, real records: that is where the structural stress model
(E10) and gravity fields land. Sprint 3 (**11/2 - 11/20**) is store,
admin, hazards, and polish.

Issues labeled `needs-platform` depend on the platform repo's API and
cannot be finished here alone.

## Checks

```
scripts/check_unity_tree.sh
```

That is what CI runs on every PR (the doctor and play scripts have their
own tests: `cd tools/hbtools && uv run pytest`): meta files pair with assets, nothing
generated is committed, the editor version is still pinned, manifests
parse, nothing looks like a credential. It needs bash and python3, so on
Windows run it from Git Bash.

**Git Bash on Windows ships `python`, not `python3`.** The script's JSON
check calls `python3` and swallows stderr, so a missing `python3` is
reported as `FAIL: invalid JSON: Packages/manifest.json` even when the
manifest is perfectly valid. If you see that locally and the file looks
fine, that is why: CI runs on Ubuntu, where `python3` exists, and passes.

Editor tests and a headless server build in CI are not there yet; see
`TODO.md`. frob v2 tracks the Jira backlog as tickets under `tickets/`
(`frob ticket list`, see [docs/frob.md](docs/frob.md)); its `frob check`
runs in CI as a non-blocking job because C# support is still partial.
Deferred engineering work lives in `TODO.md`.

## Things that bite people

- **"This project was saved with a different editor version"**: you
  installed the wrong 6000.x. Install 6000.0.43f1 and open with that.
  Never click "Continue" and upgrade the project on your own.
- **A guide says `Assets/Scripts/` but it is not there**: it is not there.
  See the top of this file. The template was deleted.
- **Missing `.meta` files / broken references after a pull**: someone
  moved an asset outside Unity. Move it back inside Unity so the meta
  travels with it. CI catches missing or orphaned metas.
- **Scene or prefab merge conflict**: do not hand-merge YAML. Take one
  side (`git checkout --theirs path/to/Scene.unity`) and redo the smaller
  change in the editor. Say in chat which scene you are working in so
  this does not happen.
- **A JSON file under `ProjectSettings/` shows as modified with an
  identical diff**: that is a line-ending flip, not a change.
  `.gitattributes` gives `*.asmdef`, `*.inputactions` and friends
  `eol=lf`, but plain `*.json` has no rule, so with `core.autocrlf=true` a
  checkout rewrites them CRLF and they read as dirty forever.
  `git checkout --` the file to drop it.
- **Everything is pink**: URP shaders did not compile yet or the project
  opened with the wrong render pipeline asset. Wait for import to finish,
  then Edit -> Render Pipeline -> URP -> Upgrade Project Materials if it
  persists.
- **Changing a default in C# does nothing**: the value is serialized in
  the scene. Change it in the Inspector, or right-click the component ->
  Reset.
- **Slow on WSL**: do not put the project inside WSL. Unity is a Windows
  or Mac app here; clone with Git for Windows (or from your Mac terminal).
  Keep `core.autocrlf=true` on Windows; `.gitattributes` normalizes line
  endings on commit.

## Playing the demo

Open `Assets/Scenes/DemoScene.unity` and press Play. It is registered in
`ProjectSettings/EditorBuildSettings.asset` after `LobbyScene` and
`MultiplayerGame`, alongside `RocketScene`.

The scene starts in **Build** mode with the player ship paused on a
circular orbit around the big planet, and a `TargetShip` flying the same
orbit about ten units ahead. Press **Tab** at any time to switch between
Build and Fly. Tab only pauses and resumes: it never moves the ship, and
**R** is the only control that repositions anything.

**Build mode:**

- **1-7** select a palette entry (Core, Hull, Armor, Thruster, Cannon,
  Fin, RetroThruster, in `BlockTypes` order). The ship already has a core,
  so selecting Core again is harmless (it will just refuse to place a
  second one).
- Hover a cell to preview it: **green** means the current selection can be
  placed there right now, **red** means it cannot, and the HUD's `Hover:`
  line explains why (e.g. "must touch an existing block", "blocks the
  cannon's muzzle").
- **Left click** places the hovered/oriented block; directional blocks
  (Cannon, Fin) take a second click to choose a facing after the first.
  **Right click** removes a block (this can strand and remove other
  blocks too, if they were only connected through it).
- **Ctrl+Z** undoes the last placement or removal; **Ctrl+Shift+Z** redoes
  it. **Esc** cancels an in-progress facing choice.

**Fly mode:**

- **W/S** or **Up/Down** fire the forward thrusters / retro thrusters.
- **A/D** or **Left/Right** steer via the fins.
- **Space** fires every cannon that is off cooldown.
- **O** cycles the overlay: **None** (plain per-type colors) -> **Stress**
  (green-to-red by how close a block is to structural failure) ->
  **LoadBearing** (magenta on articulation points: the blocks whose loss
  would split the ship) -> **Damage** (white-to-black by accumulated hit
  damage) -> **Buckling** (blue-to-red by how close the ship is to folding
  along its weakest buckling mode) -> back to None.
- **R** resets the player ship. If the scene's DemoMode has `startInOrbit`
  set (the shipped DemoScene does), this drops the player back onto its
  preset circular orbit around the big planet instead of dead rest at the
  origin; otherwise it resets to rest at the world origin.

Forward thrusters burn **red** out the back, with a particle trail left
behind in world space; retro thrusters burn **green** out two nozzles
pointing forward. Flame size and particle rate both follow that
thruster's actual throttle, so you can read the ramp on the ship as well
as on the bars.

The always-on panel in the corner shows the current mode, its controls,
the current overlay, total mass, block count, and (in Fly mode) speed,
angular speed and three live control bars: **Thrust** 0-100% in red,
**Reverse** 0-100% in green, and **Steer** -100 to +100% in white
filling left or right from center. All three ramp over about a second
for stock parts rather than snapping to full, so the bars show what the
ship is actually doing, not what you asked for.

A separate readout sits top-center in Fly mode: **`Hull: OK`** in green,
**`Hull: STRAIN`** in yellow once the most loaded block passes half its
yield, and a flashing red **`Hull: CRITICAL`** past 0.8 or when the ship
gets near its buckling load, with how many blocks are in the red and a
one-line hint. Any block in the red also pulses red on the ship itself,
whatever overlay you are in, so you can see where the problem is without
switching to the Stress overlay.

The stock ship cannot break itself: full thrust, full reverse, full
steer and every combination of them peak at 0.40 of yield with a
critical load factor above 13 (measured, see `docs/demo-scene.md`).
Structural failure is something you build your way into, by hanging mass
off a long unsupported arm.

Shooting the `TargetShip` knocks it back and tints it under the Damage
overlay; sustained fire on the same block will eventually push its
Stress ratio past 1 and detach it (and anything only connected through
it) from the target.

A ship also folds under its own loads without being shot at all: once the
critical load factor drops below 1, the weakest buckling mode's blocks
detach the same way a stress failure does. The Buckling overlay shows this
coming before it happens: it tints purely on buckling risk, so a hull
that looks fine under Stress can still show red under Buckling if a long,
thin section is about to fold.

## Powerups

Three pickups float near the player's orbit start, around `(0, -30)`: a
purple **gravity gun**, a red **anti-gravity gun**, and a green **seeking
thruster**. Flying (or falling) into one transforms the nearest block of
the matching type on your ship into that variant for 8 seconds, tinted
with a slow white pulse so it reads at a glance, and lists the block, the
variant, and the time remaining in the HUD (e.g. "Cannon (0,2): Gravity
gun 6.2 s"). The transform reverts to the block's plain behaviour on its
own once the timer runs out, and the pickup respawns in the same spot 10
seconds after being collected.

- The **gravity gun** (turns a Cannon into a gravity gun) fires a round
  that, on impact, drops a short-lived gravitating well at the hit point:
  anything nearby (including your own ship) gets pulled toward it
  until it expires.
- The **anti-gravity gun** is the same idea with the sign flipped: its
  well pushes things away instead of pulling them in.
- The **seeking thruster** (turns a Thruster into a seeking thruster) is
  an "inconvenient" upgrade: while thrusting forward it drags the ship
  toward the nearest enemy ship instead of pushing straight ahead, so it
  is a downside as often as an advantage depending on where you want to go.

## Planets and gravity

The scene's `Gravity` object holds a `GravityWorld` (plain-C#
`Hullbreach.World.GravityField` underneath) with two bodies: a big planet
at `(0, -60)` and a small moon at `(45, 20)`, between which the
`TargetShip` sits at `(0, 24)`. Every ship's blocks feel gravity as a
per-block body force (torque-correct, so a lopsided ship spins under a
strong tidal gradient near the moon), and cannon rounds fall the same
way. Pressing **R** in Fly mode drops the player back onto a preset
circular orbit around the big planet (see `startInOrbit` on `DemoMode`).

Flying (or falling) into a planet's surface stops the ship at the surface
and bounces it along the surface normal with that planet's restitution;
landing too hard (above `ShipBody.ContactDamageSpeed`, 3 m/s by default)
damages whichever block hit first, exactly like a projectile impact.

## Making a change

Same as platform. `main` is protected; every change is a branch, a PR,
green CI, one approval, merge.

```
git switch main && git pull
git switch -c feat/ship-builder          # feat/ fix/ docs/ chore/
# ...work in Unity, save the scene, scripts/check_unity_tree.sh...
git add -A
git commit -m "feat: place hull blocks on a grid"
git push -u origin feat/ship-builder
```

Commit messages are [Conventional Commits](https://www.conventionalcommits.org/).
Always `git add` the `.meta` next to any new asset. Read
[CONTRIBUTING.md](CONTRIBUTING.md) once; the Unity-specific rules there
are the ones that save us from a bad week.

[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) has the expectations and
[SECURITY.md](SECURITY.md) how to report a vulnerability.

## Handoff docs

Deeper reference than this README, for whoever is picking this codebase
up next:

- [docs/architecture.md](docs/architecture.md): the assembly graph and
  why it is split that way, the block grid conventions, `ShipBody.Step`'s
  per-tick order of operations, the structural (Q8 FE / buckling)
  pipeline, and the placement rule system.
- [docs/adding-a-block-behaviour.md](docs/adding-a-block-behaviour.md):
  the exact recipe for a new block behaviour variant (worked example: a
  scatter-shot cannon) and for a brand-new block type.
- [docs/demo-scene.md](docs/demo-scene.md): `DemoScene.unity`'s object
  layout, controls, and a "first open in Unity" checklist of everything
  hand-authored without an editor.
- [docs/testing.md](docs/testing.md): running `tools/plaincs`, what CI
  checks, which assemblies need the editor, and how to write a new test.
- [docs/netcode.md](docs/netcode.md): the wire message design and
  quantization in `Hullbreach.Net`, and the "send causes, not effects"
  server-authority rule, plus the Netcode for Entities transport bridge.
- [docs/roadmap.md](docs/roadmap.md): sprint status against the GitHub
  story numbers and known engineering debt. `TODO.md` keeps only the
  short version.
