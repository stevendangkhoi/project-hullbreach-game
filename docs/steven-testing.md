# Steven's sign-in and Netcode check

This branch connects the account session lifecycle to the existing Unity
Netcode for Entities game. It fixes saved login restoration, revokes the
session on sign-out, ignores late replies after the sign-in screen closes,
and keeps a server rejection readable after disconnect. The existing
50 Hz networking, ship replication, movement, combat, and LAN/Relay launch
paths are retained.

## Before starting

For internet play with guest accounts, skip to
[Play with teammates over the internet](#play-with-teammates-over-the-internet).
The platform API and server key below are only needed for account checks.

1. Use Unity `6000.0.43f1`, the version pinned by the project. Activate a
   personal license in Unity Hub if needed. Do not upgrade the project.
2. Start the platform API and create an account through the website.
   The game defaults to `http://127.0.0.1:8000`. If the API is elsewhere,
   set `HULLBREACH_PLATFORM_URL` in the terminal that launches the game.
3. Close the editor and run `.\play.cmd build --backend mono` from this repository.
   The development build goes in `Builds\windows-client`.
4. For a verified account inside a match, the hosting process needs
   `HULLBREACH_PLATFORM_SERVER_KEY` to match a key configured on the API
   through `HULLBREACH_GAME_SERVER_API_KEYS`. Keep the key on the host;
   joining players do not need it. See [platform setup](platform-link.md#configuration).

## Sign in, restore, and sign out

1. Open the built game executable in `Builds\windows-client` without
   direct-connect arguments so it starts at the lobby.
2. Sign in with the account created on the website. The account bar
   should show the returned username. Try a wrong password first to
   check that the error stays on the form.
3. Close and reopen the game. The saved session should restore through
   `/api/v1/me` even when this player has no server key.
4. Select **Sign out**, close the game, and open it again. The form should
   ask for credentials. Sign-out clears the saved token and calls the
   API to invalidate that session.
5. Stop the API temporarily and reopen a game with a remembered session.
   After the timeout, the form should explain the restore failure and
   still allow **Play offline**. Restart the API afterward.

Two instances under one Windows account share the saved session file.
If both restore the same token, signing out in either copy revokes that
token for both. For separate identities, sign out and close any existing
copies, open both copies at the signed-out form before signing in to
either, then enter a different account in each. Separate Windows user
accounts or computers also keep their saved session files separate.

## Two players over LAN

1. Open both copies at the signed-out form as described above. Sign in
   with a different account in each, or use **Play offline** for a guest
   networking check.
2. In the first copy, select **Host LAN**.
3. In the second copy, enter `127.0.0.1` and select **Join address**.
   For a second computer, enter the host computer's local IPv4 address
   and allow the game through the firewall on the private network.
4. Both windows should enter `MultiplayerGame`, show different network
   IDs, and show `Replicated ships: 2` after both are admitted.
5. Use W/S to thrust, A/D to steer, and Space to fire. The other window
   should see movement and shots. Tab changes build/flight mode; 1-7
   selects a block, left-click places, and right-click removes.
6. Close the host. The other window should change to a disconnected
   message. Use **Leave** to exit the match.

For the existing automatic guest smoke check, run:

```powershell
.\play.cmd two-local --headless --seconds 20
```

This direct path skips the sign-in screen. It is a networking check,
not a replacement for the account checks above. See [the full LAN
guide](../README.md#guide-3-direct-lan-connection) for remote addresses
and the launch scripts.

## Required account check

Start the host with its server key configured and
`HULLBREACH_REQUIRE_PLATFORM_AUTH=1`. Open both copies at the signed-out
form before signing either in. Sign in on the host and select **Host LAN**.
Join from the other process after choosing **Play offline**. That player should
be rejected; the reason should remain visible after the disconnect.
Restart the joining copy, sign in, and join again. The HUD should show
`Platform: signed in as <username>` and replicated ships.

## Verification for this branch

The plain C# tests cover successful and expired session restoration,
malformed API replies, offline failures, sign-out revocation, late
callbacks, and keeping secrets out of logs. The live API integration
test additionally checks login, restoration without a server key,
required server admission, forged-token rejection, logout, and rejection
of the revoked token. See [live test setup](platform-link.md#testing).

Unity `6000.0.43f1` is installed and licensed on the test machine. The
Windows development client builds with the Mono backend. The visible
game form passed wrong-password rejection, two separate account logins,
remembered-session restoration, and sign-out revocation checked against
the API. The authenticated direct-connect test verified two different
network IDs and two replicated ships, and a guest received the required
account rejection before the server disconnected it.

Nine Unity EditMode tests pass: four check the lobby controls, two check
saved rendering and sprite assets, and three check LAN transport setup.
The transport test starts with the lobby's IPC client and proves that
the LAN setup can connect it to a UDP listener.

The visible Windows test also passed: both accounts signed in, Host LAN
and Join address connected two separate processes, both windows showed
two ships with network IDs 1 and 2, and thrust, steering, and firing
worked. Closing the host eventually showed the persistent disconnected
message on the second player. That earlier LAN check restarted the game copies
between matches; the newer Relay check below also covers consecutive matches.

The development client logged server-tick prediction corrections during
the test, including while waiting for the closed host to time out. The
exact timing/performance cause is not established. Local connection and
account checks passed, but this is not a clean performance benchmark.
That earlier run did not test Relay. See the online check below for the newer
Relay results; play between separate computers still needs a teammate check.

### Online verification on November 4, 2026

Unity `6000.0.43f1` built the Windows Mono development client successfully.
Five EditMode cleanup tests passed, covering listener reuse, connection cleanup,
queued requests, fresh client replicas, and fresh server simulation state.
The repository's Unity tree check and `git diff --check` also passed.

Two visible game processes joined through the public room list and a private
invite code. Both entered the match with two ships; movement and firing were
checked manually. The joining player's transport log confirmed actual Relay
traffic, while the host's own player used the local connection.

The final repeatable check passed two consecutive private Relay matches in the
same processes. The first match used IDs 1 and 2; the second used IDs 3 and 4.
Both matches had exactly two replicated ships on both players. Leaving cleared
the replicas, disconnected both sides, and allowed another room without a restart.
The check pauses between rooms after an initial rapid retry hit Unity's request
limit. Both final processes wrote their pass markers and exited successfully.

These cloud tests used guest accounts on one Windows PC. They exercised the real
Unity Lobby and Relay services, but they do not replace a teammate test on a
different PC and network. Website account login was not re-tested during this
online run. The earlier timing warnings remain a separate performance concern;
the online checks establish connectivity and cleanup, not a performance target.

## Play with teammates over the internet

The game fork works with the existing website. The pilot website mockup and
email-login changes are optional and do not need to be merged for online play.
For an initial multiplayer check, use guest accounts so each teammate does not
need a locally running platform API.

1. Everyone should use the same build from `steven/login-mockup-netcode`, made
   with Unity `6000.0.43f1`. Share the whole `Builds/windows-client` folder as a
   ZIP, including the data folder and DLLs. Teammates extract it and run
   `Project Hullbreach.exe`; they do not need the Unity Editor to play.
2. Choose **Play offline** at the account form. Despite its current label, this
   skips the Hullbreach account login; the online lobby still works through
   Unity's anonymous player identity. It does not record a verified account's
   match results. To test account features, everyone needs the same reachable
   existing platform API; `127.0.0.1` on a friend's PC points to their own PC.
3. The host selects **Private (join code only)**, then **Host with Relay**.
   Wait for the waiting room and share its **Invite code** with teammates.
4. Teammates enter that code under **Join by code** and select **Join code**.
   If the host set a lobby password, enter it too. Wait until the room lists
   all players, then the host selects **Start game**.
5. Confirm different network IDs and a matching replicated-ship count. Press
   Tab for Flight, W/S for thrust, A/D to steer, and Space to fire. Each player
   should see the other ships move. Use **Leave** when finished. The host must
   keep the game running; leaving ends the room, with no host migration.

Use **Host with Relay** for this test. **Host LAN** and **Join address** are the
direct-connection controls. Relay does not require router port forwarding.
The project already links to the team's cloud project, `Orbital (1)` under
`derrickdavison`. Keep that project ID so everyone sees the same lobbies.
Teammates playing the built game do not need access to its cloud dashboard.

For two instances on one Windows account, launch each with a different profile:

```powershell
.\play.cmd online --profile player1
.\play.cmd online --profile player2
```

Run these in separate terminals. Profiles separate the Unity player identities,
but the Hullbreach platform's saved login file is still shared. On separate
computers the normal launcher is enough.

For a short cloud-service check:

```powershell
.\play.cmd online --headless --seconds 60 --profile relaycheck
```

This signs in anonymously, creates a temporary private Relay lobby, and leaves
it. It does not by itself prove two-player gameplay. Runtime logs report
`Hullbreach match: transport=Relay` on a remote client after it enters a match;
the host's own player normally reports `direct/local`.

If creating or joining fails, read the error above the lobby controls and run
`.\doctor.cmd --demo online`. A second local process needs a distinct profile.
A join code belongs to the current room and expires when that room ends.
If connection cleanup times out, close and reopen the game before retrying.
If Unity reports `request_rate_limited`, wait a few seconds before trying again.

## Repeatable Relay pair check

Development builds include `RelayPairCheck`, which is inactive during normal play.
Launch two processes with separate `-hullbreachProfile` values. Set
`HULLBREACH_RELAY_CHECK_ROLE` to `host` for one and `client` for the other, and set
`HULLBREACH_RELAY_CHECK_FOLDER` to the same new, empty local folder for both.
This folder passes temporary room codes and completion markers between processes;
it does not contain account credentials. Clear these environment variables in the
launching terminal afterward so ordinary play does not start a test.

The check hosts and joins two consecutive private rooms, starts each match,
requires two replicated ships on both players, verifies Relay on the joining
client, and checks that leaving clears all replicated ships before the next room.
It pauses briefly between rounds to respect the cloud service's request limits.
It exits each process with code 0 and writes `host-passed.txt` or
`client-passed.txt` only after both rounds complete. A failure exits with code 1
and writes a failure marker. Each wait has a 60-second limit. This needs live
Unity services and internet access, and is separate from the EditMode tests.

## Team review and integration

This fork extends the team's existing Netcode for Entities implementation.
Review the account-session fixes, LAN lobby controls and transport setup,
saved rendering assets, and online session cleanup as the changes from this branch.

The existing [draft PR](https://github.com/stevendangkhoi/project-hullbreach-game/pull/1)
targets Steven's fork. Merging it would not update the team's repository.
To propose these changes to the team:

1. Fetch the latest team `main` and check for newer changes before opening the PR.
   Follow [Contributing](../CONTRIBUTING.md) to bring the branch up to date.
2. In `lognd/project-hullbreach-game`, open a pull request and choose
   **Compare across forks**. Set the base to `lognd/project-hullbreach-game`
   on `main`, and the head to `stevendangkhoi/project-hullbreach-game`
   on `steven/login-mockup-netcode`.
3. Repeat the account and two-player checks above against the team's existing
   platform API. The website mockup PR is independent and is not required for
   this game PR. Keep username sign-in when using the unchanged platform API.
4. Coordinate the lobby prefab and multiplayer scene edits with anyone working
   on those assets. Keep Unity assets, their `.meta` files, and project settings
   together. Keep the editor at `6000.0.43f1`.
5. Have a teammate review the changes and wait for the required checks.
   The game contribution guide allows squash or rebase merges only.
   Follow the team's current branch protection settings; do not push to `main`.

Use the same platform API for the website and game. Configure its server key
only for the hosting process, and keep test credentials, logs, and builds local.
Include the timing warnings and the remaining separate-computer check in the PR
so reviewers know the limits of the current checks.
