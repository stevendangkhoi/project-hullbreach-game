# Steven's sign-in and Netcode check

This branch connects the account session lifecycle to the existing Unity
Netcode for Entities game. It fixes saved login restoration, revokes the
session on sign-out, ignores late replies after the sign-in screen closes,
and keeps a server rejection readable after disconnect. The existing
50 Hz networking, ship replication, movement, combat, and LAN/Relay launch
paths are retained.

## Before starting

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
message on the second player. Restart the game copies between matches.

The development client logged server-tick prediction corrections during
the test, including while waiting for the closed host to time out. The
exact timing/performance cause is not established. Local connection and
account checks passed, but this is not a clean performance benchmark.
Relay and play between separate computers were not tested in this run.

## Team review and integration

This fork extends the team's existing Netcode for Entities implementation.
Review the account-session fixes, LAN lobby controls and transport setup,
and saved rendering assets as the changes from this branch.

The existing [draft PR](https://github.com/stevendangkhoi/project-hullbreach-game/pull/1)
targets Steven's fork. Merging it would not update the team's repository.
To propose these changes to the team:

1. Fetch the latest team `main` and check for newer changes before opening the PR.
   Follow [Contributing](../CONTRIBUTING.md) to bring the branch up to date.
2. In `lognd/project-hullbreach-game`, open a pull request and choose
   **Compare across forks**. Set the base to `lognd/project-hullbreach-game`
   on `main`, and the head to `stevendangkhoi/project-hullbreach-game`
   on `steven/login-mockup-netcode`.
3. Link the companion website/API PR from `steven/pilot-login` in
   `stevendangkhoi/project-hullbreach-platform`. Review and merge the platform
   changes first, then repeat the account and two-player checks above with both
   branches before merging the game changes.
4. Coordinate the lobby prefab and multiplayer scene edits with anyone working
   on those assets. Keep Unity assets, their `.meta` files, and project settings
   together. Keep the editor at `6000.0.43f1`.
5. Have a teammate review the changes and wait for the required checks.
   The game contribution guide allows squash or rebase merges only.
   Follow the team's current branch protection settings; do not push to `main`.

Use the same platform API for the website and game. Configure its server key
only for the hosting process, and keep test credentials, logs, and builds local.
Include the timing warnings and untested Relay/separate-computer cases in the PR
so reviewers know the limits of the current checks.
