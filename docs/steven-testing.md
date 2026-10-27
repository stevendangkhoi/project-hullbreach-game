# steven's sign-in and netcode check

this branch connects the account session lifecycle to the existing unity
netcode for entities game. it fixes saved login restoration, revokes the
session on sign-out, ignores late replies after the sign-in screen closes,
and keeps a server rejection readable after disconnect. the existing
50 hz networking, ship replication, movement, combat, and lan/relay launch
paths are retained.

## before starting

1. use unity `6000.0.43f1`, the version pinned by the project. activate a
   personal license in unity hub if needed. do not upgrade the project.
2. start the platform api and create an account through the website.
   the game defaults to `http://127.0.0.1:8000`. if the api is elsewhere,
   set `HULLBREACH_PLATFORM_URL` in the terminal that launches the game.
3. close the editor and run `.\play.cmd build` from this repository.
   the development build goes in `Builds\windows-client`.
4. for a verified account inside a match, the hosting process needs
   `HULLBREACH_PLATFORM_SERVER_KEY` to match a key configured on the api
   through `HULLBREACH_GAME_SERVER_API_KEYS`. keep the key on the host;
   joining players do not need it. see [platform setup](platform-link.md#configuration).

## sign in, restore, and sign out

1. open the built game executable in `Builds\windows-client` without
   direct-connect arguments so it starts at the lobby.
2. sign in with the account created on the website. the account bar
   should show the returned username. try a wrong password first to
   check that the error stays on the form.
3. close and reopen the game. the saved session should restore through
   `/api/v1/me` even when this player has no server key.
4. select **sign out**, close the game, and open it again. the form should
   ask for credentials. sign-out clears the saved token and calls the
   api to invalidate that session.
5. stop the api temporarily and reopen a game with a remembered session.
   after the timeout, the form should explain the restore failure and
   still allow **play offline**. restart the api afterward.

two instances under one windows account share the saved session file.
if both restore the same token, signing out in either copy revokes that
token for both. for separate identities, sign out and close any existing
copies, open both copies at the signed-out form before signing in to
either, then enter a different account in each. separate windows user
accounts or computers also keep their saved session files separate.

## two players over lan

1. open both copies at the signed-out form as described above. sign in
   with a different account in each, or use **play offline** for a guest
   networking check.
2. in the first copy, select **host lan**.
3. in the second copy, enter `127.0.0.1` and select **join address**.
   for a second computer, enter the host computer's local ipv4 address
   and allow the game through the firewall on the private network.
4. both windows should enter `MultiplayerGame`, show different network
   ids, and show `Replicated ships: 2` after both are admitted.
5. use w/s to thrust, a/d to steer, and space to fire. the other window
   should see movement and shots. tab changes build/flight mode; 1-7
   selects a block, left-click places, and right-click removes.
6. close the host. the other window should change to a disconnected
   message. use **leave** to exit the match.

for the existing automatic guest smoke check, run:

```powershell
.\play.cmd two-local --headless --seconds 20
```

this direct path skips the sign-in screen. it is a networking check,
not a replacement for the account checks above. see [the full lan
guide](../README.md#guide-3-direct-lan-connection) for remote addresses
and the launch scripts.

## required account check

start the host with its server key configured and
`HULLBREACH_REQUIRE_PLATFORM_AUTH=1`. open both copies at the signed-out
form before signing either in. sign in on the host and select **host lan**.
join from the other process after choosing **play offline**. that player should
be rejected; the reason should remain visible after the disconnect.
restart the joining copy, sign in, and join again. the hud should show
`Platform: signed in as <username>` and replicated ships.

## verification for this branch

the plain c# tests cover successful and expired session restoration,
malformed api replies, offline failures, sign-out revocation, late
callbacks, and keeping secrets out of logs. the live api integration
test additionally checks login, restoration without a server key,
required server admission, forged-token rejection, logout, and rejection
of the revoked token. see [live test setup](platform-link.md#testing).

the local machine did not have the pinned editor version available.
a disposable-copy compile attempt with installed unity `6000.2.9f1`
exited before compilation and reported missing headless licensing
entitlements. the project's version and packages were left unchanged.
the unity build and two-player visual checks above still need to be run
with the pinned editor and an active license.
