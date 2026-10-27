# Platform link

How the game talks to the Hullbreach platform API (the FastAPI service in
the sibling `platform` repo): a player signs in from the game client, and
the game server checks that player's platform session before letting the
connection into the match. Tickets: S07-2 (sign-in screen, client side) and
S07-4 (server-side session check at connect), both under S07.

Two layers, split the same way as `Hullbreach.Net` and
`Hullbreach.NetCode.Entities` (see [netcode.md](netcode.md)):

- `Assets/Scripts/Hullbreach.Platform` is plain C# with no engine
  reference: configuration, the typed client for the auth endpoints, the
  outcome types, and the server's admission policy. It builds and tests
  under `tools/plaincs/run_tests.sh` with a fake HTTP transport.
- `Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatform*.cs` is
  the thin Unity layer: the `UnityWebRequest` transport, the hello and
  admission RPCs, and the two ECS systems that drive the policy.

## Configuration

Read once per process by `HullbreachPlatformRuntime.Config` and logged as
one line, e.g. `Hullbreach platform: config url=http://127.0.0.1:8000
timeout=5s requireAuth=no serverKey=not set`. Precedence is command-line
argument, then environment variable, then default. A malformed URL or
timeout is logged as a warning and replaced by the default rather than
aborting startup.

A committed `.env.example` lists these variables with placeholder values.
Nothing in the repo loads a `.env` file (the game, `play.sh` and `play.cmd`
read the process environment only), so export the values in your shell
yourself; a real `.env` is gitignored and never read.

| Argument | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `-hullbreachPlatformUrl <url>` | `HULLBREACH_PLATFORM_URL` | `http://127.0.0.1:8000` | Base URL of the platform API (http or https; a trailing `/` is dropped). Used by the client to sign in and by the server to validate sessions. |
| `-hullbreachPlatformTimeout <seconds>` | (none) | `5` | Per-request HTTP timeout. |
| `-hullbreachRequirePlatformAuth` | `HULLBREACH_REQUIRE_PLATFORM_AUTH` (`1`, `true` or `yes`) | not required | Server only. When set, every connection must present a platform session the platform accepts, or it is disconnected. When not set, everyone is admitted and signed-in players are shown as verified. |
| (never an argument) | `HULLBREACH_PLATFORM_SERVER_KEY` | unset | Server only. Sent as `X-Server-Key` on session checks. Must match one of the platform's `HULLBREACH_GAME_SERVER_API_KEYS`. Env-only so it never lands in shell history, process listings or committed launch scripts; `-hullbreachPlatformServerKey` is recognised only to log a warning and is otherwise ignored. |
| (internal) | (none) | `10` s | How long a required-auth server waits for a connection's hello before rejecting it (`PlatformConfig.HelloTimeoutSeconds`). |

The server logs its mode at startup either way: `server REQUIRES platform
sign-in; unverified players are disconnected.` or `server allows guests;
platform sign-in is optional`. A required-auth server without a server key
logs a warning.

Secrets (password, token, server key) never reach a log line: `Describe()`
prints only `serverKey=set|not set`, `LoginOutcome.ToString()` omits the
token, the client logs usernames only, and the tests assert all of this
against recorded logs, including the live integration test.

<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.DefaultBaseUrl -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.DefaultTimeoutSeconds -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.DefaultHelloTimeoutSeconds -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.UrlArg -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.TimeoutArg -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.RequireAuthArg -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.RejectedServerKeyArg -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.UrlEnv -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.RequireAuthEnv -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.ServerKeyEnv -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.PlatformConfig -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.HasServerKey -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.FromEnvironment -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.Describe -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformConfig.cs::PlatformConfig.ToString -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachPlatformRuntime -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachPlatformRuntime.Log -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachPlatformRuntime.Config -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachPlatformRuntime.Client -->

## Handshake

1. The player signs in on the client (the S07-2 sign-in screen calls
   `PlatformClient.Login`); the token goes into `PlatformSession` and the
   per-user [remembered session](#remembered-session) file.
2. The client connects through the lobby/Relay or direct path as before.
   When the client world first sees its `NetworkId`,
   `HullbreachPlatformAuthClientSystem` sends one
   `HullbreachPlatformHelloRpc { Token, Username }`. A player who chose
   to play offline (and every thin client) sends the hello with an empty
   token.
3. On the server, `HullbreachPlatformAuthServerSystem` feeds connects,
   hellos and disconnects into `PlatformAdmissionGate`. For a non-empty
   token the gate calls `PlatformClient.ValidateSession`, i.e.
   `GET /api/v1/auth/session` with `Authorization: Bearer <token>` and
   `X-Server-Key: <key>`.
4. The gate emits one `AdmissionDecision` per verdict. The server system
   tags admitted connections with `HullbreachPlatformAdmitted` (only
   those are joined to the match and marked `NetworkStreamInGame` by
   `HullbreachNetCodeServerSystem`), sends the player a
   `HullbreachPlatformAdmissionRpc { Admitted, Verified, DisplayName,
   Reason }`, and disconnects rejected peers 0.5 s later so the reason
   arrives first.
5. The client stores the verdict in `HullbreachPlatformAdmission`;
   `HullbreachNetworkGameHud` shows its `StatusLine` (for example
   `Platform: signed in as pilot`).

the rejection reason stays on the hud after the server disconnects the
player. a new connection clears the previous verdict. a lost connection
after admission shows a disconnected message with the leave action.

Policy by mode:

| Situation | Not required (default) | Required |
| --- | --- | --- |
| Connect | Admitted at once as `Guest #<id>` | Held, not in the match |
| Hello with empty token | Stays guest | Rejected: "This server requires a Hullbreach account. Sign in and reconnect." |
| Token valid | Upgraded to verified, platform username shown | Admitted verified |
| Token invalid/expired (401) | Stays guest, reason shown in HUD | Rejected: "Your platform session is invalid or expired. Sign in again." |
| Platform unreachable / unexpected answer | Stays guest, reason shown | Rejected (fail closed): "Could not verify your platform session (...)" |
| No hello within 10 s | Unaffected | Rejected: "No platform sign-in received in time." |

Edge cases the gate handles: a second hello from the same connection and
a hello from an unknown connection are ignored with a warning; a session
check that completes after its peer left (or after the `NetworkId` was
reused by a new connection) is dropped by a per-connection generation
number; a peer whose check is in flight is not timed out.

Known limits, recorded for follow-up:

- `/api/v1/auth/session` returns `user_id` and `role`, not a username, so
  the displayed name is the one the client claims in its hello. It is
  only shown as verified when it arrives together with a token the
  platform accepted, but the platform does not confirm the pair. A
  platform change to return `username` from `/session` would close this.
- Newer platforms (platform PR #41) require `X-Server-Key` on
  `GET /api/v1/auth/session` and check it before the token; older ones
  ignore it. The game server always sends the key, so both work. A key
  problem is reported as the operator's fault, not the player's (see
  [failure modes](#failure-modes)): every player is then refused or kept
  a guest with "platform refused this game server's key", and the fix is
  to set `HULLBREACH_PLATFORM_SERVER_KEY` on the game server to one of the
  platform's `HULLBREACH_GAME_SERVER_API_KEYS`.
- The hello travels inside the Netcode connection, which is not
  encrypted on the direct path. Relay connections are DTLS-protected.

<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::AdmissionDecision -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::AdmissionDecision.AdmissionDecision -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::AdmissionDecision.ToString -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.MaxDisplayNameLength -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.PlatformAdmissionGate -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.RequireAuth -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.OnConnected -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.OnHello -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.OnDisconnected -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.Tick -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.IsAdmitted -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.TryTakeDecision -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.GuestName -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.SanitizeName -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission.HasVerdict -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission.Admitted -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission.Verified -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission.DisplayName -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission.Reason -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAdmission.StatusLine -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthClientSystem.cs::HullbreachPlatformAuthClientSystem -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthServerSystem.cs::HullbreachPlatformAuthServerSystem -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRpc.cs::HullbreachPlatformHelloRpc -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRpc.cs::HullbreachPlatformAdmissionRpc -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRpc.cs::HullbreachPlatformAdmitted -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRpc.cs::HullbreachPlatformTracked -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRpc.cs::HullbreachPlatformHelloSent -->

## Failure modes

What each platform answer becomes, and what the player sees.

Login (`PlatformClient.MapLogin`):

| Platform answer | `LoginStatus` | Message shown |
| --- | --- | --- |
| 200 with `token` and `user.username` | `Success` | "Signed in." |
| 200 with an unexpected body | `Failed` | "The server sent an unexpected sign-in response." |
| 401 | `InvalidCredentials` | "Wrong username or password." |
| 429 (5 failed attempts per username per minute) | `RateLimited` | "Too many sign-in attempts. Wait a minute and try again." |
| 422 | `Failed` | "The server rejected the sign-in request (HTTP 422)." |
| any other status | `Failed` | "Sign-in failed (HTTP n)." |
| no HTTP answer (refused, DNS, timeout, insecure HTTP blocked) | `Unreachable` | "Cannot reach the Hullbreach servers (...)." |
| empty username or password (no request sent) | `InvalidCredentials` | "Enter a username and password." |

Session check (`PlatformClient.MapSession`). The platform's error body is
`{"detail": "..."}`; the key is checked before the token:

| Platform answer | `SessionStatus` | Reason the gate shows |
| --- | --- | --- |
| 200 with `user_id` | `Valid` | (admitted, verified) |
| 401 `not authenticated` (or any 401 not about the key) | `Invalid` | "Your platform session is invalid or expired. Sign in again." |
| 401 `server key missing` / `invalid server key` | `Failed` | "Could not verify your platform session (platform refused this game server's key (invalid server key); set HULLBREACH_PLATFORM_SERVER_KEY to one of the platform's game server keys)." |
| 503 `server key not configured` | `Failed` | "Could not verify your platform session (platform has no game server keys configured (...))." |
| no HTTP answer | `Unreachable` | "Could not verify your platform session (...)." |
| anything else | `Failed` | "Could not verify your platform session (session check failed (HTTP n))." |

The handshake table above shows how the gate reacts to each.

<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.MapLogin -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.MapSession -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::LoginStatus -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::LoginOutcome -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::LoginOutcome.LoginOutcome -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::LoginOutcome.IsSuccess -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::LoginOutcome.ToString -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::SessionStatus -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::SessionOutcome -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::SessionOutcome.SessionOutcome -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::SessionOutcome.IsValid -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::SessionOutcome.ToString -->

## Reporting a finished match

The dedicated server posts the result of a finished match to
`POST /api/v1/matches` with the `X-Server-Key` header (never a user token).
The body is `match_id` (a client-chosen UUID, the idempotency key),
`winner_id`, `duration_seconds` and exactly two distinct `players`, each with
`user_id`, `damage_dealt`, `blocks_destroyed`, `blocks_placed` and
`time_alive_seconds`. `MatchResultReport.Validate` checks what the platform
would answer 422 to, so a bad report is never sent.

| Platform answer | `MatchReportStatus` | Retried |
| --- | --- | --- |
| 201 first report | `Recorded` | no |
| 200 identical replay | `AlreadyRecorded` | no |
| 404 unknown player | `UnknownPlayer` | no |
| 409 `match_id` reused with a different body | `Conflict` | no |
| 401 bad server key, 422, other 4xx | `Rejected` | no |
| 503 no server keys configured, other 5xx | `ServerError` | yes |
| no HTTP answer | `Unreachable` | yes |
| invalid report or no server key (nothing sent) | `InvalidReport` / `Rejected` | no |

Retry safety: `MatchResultReporter` resends the byte-identical body with the
same `match_id` after 2, 5, 15, 30, 60 and 120 seconds (seven attempts in
all), so a lost answer turns into a harmless 200 replay. It then gives up
with a warning. The server key never appears in a log line or outcome.

Guests: the reporter is a no-op when the server key is not configured, when
either player is a guest (no verified account id), or when the winner is not
one of the two players. Account ids come from `PlatformAdmissionGate.TryGetUserId`.

Hook: the match-end flow raises `MatchEndFeed.Shared` with a `MatchEndedArgs`
(match id, winner peer, both peers with their stats, duration). The
`HullbreachPlatformAuthServerSystem` attaches `MatchResultReporter.Attach` to
that feed in `OnCreate` (and detaches in `OnDestroy`), so a raised match end
becomes `POST /api/v1/matches`; the system ticks the reporter every update.
`HullbreachPlatformAuthServerSystem.ReportFinishedMatch` stays as a direct
entry point. `Hullbreach.Match.MatchStateMachine` (roadmap S46-1) raises the feed once, with a
fresh uuid `match_id`, when a core is breached, a peer forfeits or the time limit
expires, and also for a draw. `MatchEndedArgs.Result` carries the `MatchResult`;
`MatchResultReporter.Attach` skips a `Draw` (no winner to report) with an info log.
The server replicates the end to clients as `MatchEnded`
(`reference/hullbreach-net.md#matchended`).

<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.PostMatchResult -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.MapMatchReport -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.MatchesPath -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::MatchReportStatus -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::MatchReportOutcome -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::MatchReportOutcome.IsStored -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformOutcomes.cs::MatchReportOutcome.IsRetryable -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/MatchResultReport.cs::PlayerMatchStats -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/MatchResultReport.cs::MatchResultReport -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/MatchResultReporter.cs::MatchResultReporter -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/MatchEndFeed.cs::MatchEndFeed -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/MatchEndFeed.cs::MatchEndedArgs -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/MatchResultReporter.cs::MatchResultReporter.Attach -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformAdmissionGate.cs::PlatformAdmissionGate.TryGetUserId -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformAuthServerSystem.cs::HullbreachPlatformAuthServerSystem.ReportFinishedMatch -->

## HTTP transport

`IPlatformHttp` is the seam between the engine-free code and a real HTTP
stack. In the game it is `HullbreachUnityPlatformHttp`, built on
`UnityWebRequest`, chosen over `System.Net.Http.HttpClient` because:

- it works on every target the project builds, including the Linux
  dedicated server, IL2CPP players (where `HttpClient` needs extra
  stripping care) and WebGL (where `HttpClient` cannot open sockets);
- its completion callback runs on the main thread, which the ECS systems
  consuming the result require; an `HttpClient` task would need marshalling
  back to the main thread;
- it honours the project's Player Settings for insecure HTTP and
  certificates in one place.

Plain `http://` URLs are refused by Unity unless Player Settings allow
them. `ProjectSettings.asset` sets `insecureHttpOption: 1` ("Allowed in
development builds"), so the default `http://127.0.0.1:8000` works in
the editor and development builds; a release build must point at an
`https://` platform URL. A blocked request comes back as `Unreachable`
with the reason "request blocked: ...".

The live integration test uses its own blocking `HttpClient` transport,
which is fine under the .NET SDK where the test runs.

<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachUnityPlatformHttp -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachUnityPlatformHttp.Send -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachUnityPlatformLog -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachUnityPlatformLog.Info -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachUnityPlatformLog.Warning -->

## Client session

`PlatformSession` holds the signed-in username and token for this run of
the game only; the [remembered session](#remembered-session) is what
carries it to the next launch. The sign-in screen writes it; the client handshake
system reads it when it says hello. `PlayOffline` records that the player
chose to play without an account (the hello then carries no token, so the
server treats them as a guest or, in required mode, turns them away);
`HasChosen` is true once either answer was given this run. Treat `Token`
as a secret: it may be passed to the handshake and nowhere else.

Demo/testing aid: a headless client cannot click the sign-in screen, so
at startup (before the first scene) `HullbreachPlatformRuntime` calls
`SignInFromEnvironment`. When `HULLBREACH_PLATFORM_TOKEN` is set it signs
in with that token and the name in `HULLBREACH_PLATFORM_USERNAME`, and
logs `Hullbreach platform: signed in from HULLBREACH_PLATFORM_TOKEN as
'<name>' (demo/testing aid).` (the name only, never the token). It is
environment-only, never a command-line argument, so the token does not
show up in process listings.

<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.IsSignedIn -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.IsOffline -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.HasChosen -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.Username -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.Token -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.SignIn -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.PlayOffline -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.SignOut -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.TokenEnv -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.UsernameEnv -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformSession.cs::PlatformSession.SignInFromEnvironment -->

## Sign-in screen

S07-2. The first thing `LobbyScene` shows is the sign-in form, an overlay
canvas (`Assets/Prefabs/UI/SignInCanvas.prefab`, sort order 10) above the
lobby canvas. It blocks the lobby until the player either signs in or
presses **Play offline**; after that only a small account bar stays in the
top-right corner ("Signed in as name" with **Sign out**, or "Playing
offline" with **Sign in**, which brings the form back). Returning to the
lobby later in the same run does not ask again: `SignInModel` starts from
whatever `PlatformSession` already holds.

`SignInModel` (engine-free, unit-tested in `SignInModelTests`) owns all
of the screen's behaviour; `HullbreachSignInView` only copies its
properties onto the widgets and forwards clicks.

| State | Form | Account bar | Notes |
| --- | --- | --- | --- |
| `Editing` | shown, editable | hidden | message line shows the last error in red |
| `Busy` | shown, read-only | hidden | "Signing in..."; a second submit is ignored; **Play offline** still works and a late answer is dropped |
| `SignedIn` | hidden | "Signed in as name" | token kept in `PlatformSession` and the per-user session file |
| `Offline` | hidden | "Playing offline" | no token; hello carries none |

The message for each failed sign-in is the `LoginOutcome` message from
the [failure modes](#failure-modes) table; an unreachable platform adds
"You can still play offline." The password field is cleared as soon as a
request is sent, and neither the password nor the token is ever logged or
shown. Enter in the password field submits.

**sign out** clears the local session and saved file immediately, then
calls `POST /api/v1/auth/logout` with that session's bearer token. this
revokes only the current session. if the platform cannot be reached,
the form explains that local sign-out succeeded but remote revocation
did not. a 401 means the session is already invalid. a delayed logout
response cannot replace a newer sign-in attempt. disabling the view calls
`SignInModel.CancelPending` so a late login or restore response cannot
sign the player in after the screen closes.

Tab moves to the next widget (username, password, **Sign in**, **Play
offline**, then back to username) and Shift+Tab to the previous one;
the up and down arrows follow the same order. `HullbreachSignInView`
adds a `HullbreachTabNavigation` at runtime (no prefab change) that reads
Tab from the legacy Input Manager or the Input System, whichever the
player settings enable; `TabOrder` (engine-free, `TabOrderTests`) picks
the next enabled widget. Enter on a focused button presses it.

The prefab is generated by `LobbySceneBuilder`. For an existing project,
`Hullbreach > Online > Add sign-in screen to lobby scene` creates the
prefab and adds it to `LobbyScene` without resetting the lobby prefab or
the multiplayer scenes; the full `Rebuild lobby and multiplayer scenes`
also includes it. A lobby scene without the sign-in canvas still works:
the player is simply offline.

<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInState -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.UnreachableHint -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.SignInModel -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.Changed -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.State -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.Message -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.IsError -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.LastStatus -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.CanSubmit -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.IsDone -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.AccountLine -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.Submit -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.PlayOffline -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.SignOut -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.CancelPending -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.LogoutPath -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.Logout -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.MessageFor -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachSignInView.cs::HullbreachSignInView -->
<!-- describes: Assets/Editor/Hullbreach.Editor/LobbySceneBuilder.cs::LobbySceneBuilder.AddSignInMenu -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/TabOrder.cs::TabOrder -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/TabOrder.cs::TabOrder.Step -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/TabOrder.cs::TabOrder.IndexOf -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachTabNavigation.cs::HullbreachTabNavigation -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachTabNavigation.cs::HullbreachTabNavigation.SetOrder -->

## Remembered session

S07-3. After a successful sign-in `SessionTokenStore` writes the username
and token to `platform-session.txt` under Unity's per-user
`Application.persistentDataPath` (two lines: username, token; the file
is plain text, so it relies on the per-user folder's permissions). On the
next launch the sign-in screen calls `SignInModel.TryRestore`, which
checks the stored token with `GET /api/v1/me` while the form
shows "Restoring your session...":

| Platform answer | Result | Stored file |
| --- | --- | --- |
| 200 with a valid profile | `SignedIn` as the current profile username; `PlatformSession.Token` carries the token | updated with the current username |
| 401 | form shown with "Your saved session expired. Sign in again." | deleted |
| unreachable / malformed / other | form shown with a restore error; sign-in and offline play remain available | kept for next launch |

the player request sends only the bearer token. `/auth/session` remains
the game-server validation route and requires `X-Server-Key`; a player
client must not need that key to restore its own login.

The stored token is presented when joining a match exactly as a fresh one
is: the client handshake reads `PlatformSession.Token` into the hello (see
[the handshake](#handshake)). **Sign out** deletes
the file and asks the platform to revoke the session; **Play offline** leaves it alone. A malformed file is deleted,
and an unwritable folder only logs a warning (the player signs in again
next launch). Neither the token nor the password is ever logged.

<!-- describes: Assets/Scripts/Hullbreach.Platform/SessionTokenStore.cs::SessionTokenStore -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SessionTokenStore.cs::SessionTokenStore.SessionTokenStore -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SessionTokenStore.cs::SessionTokenStore.Save -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SessionTokenStore.cs::SessionTokenStore.TryLoad -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SessionTokenStore.cs::SessionTokenStore.Clear -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.TryRestore -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.ProfilePath -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.RestoreSession -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachPlatformRuntime.cs::HullbreachPlatformRuntime.TokenStore -->

## Hullbreach.Platform module reference

The remaining engine-free types: `PlatformClient` (typed calls to
`POST /api/v1/auth/login`, `GET /api/v1/me`, `POST /api/v1/auth/logout`,
and `GET /api/v1/auth/session` that never throw
for network or HTTP failures), the `IPlatformHttp` request/response
types, `PlatformJson` (a minimal JSON reader/writer: the assembly has no
engine or package references, so neither `JsonUtility` nor
`System.Text.Json` is available on every target), and the `IPlatformLog`
logging seam (`HullbreachUnityPlatformLog` adapts it to `Debug.Log`).

<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.LoginPath -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.SessionPath -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.ServerKeyHeader -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.PlatformClient -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.Config -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.Login -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.ValidateSession -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpMethod -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpRequest -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpRequest.PlatformHttpRequest -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpRequest.Header -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpResponse -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpResponse.IsTransportFailure -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpResponse.Http -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::PlatformHttpResponse.Transport -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::IPlatformHttp -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformHttp.cs::IPlatformHttp.Send -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformJson.cs::PlatformJson -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformJson.cs::PlatformJson.Quote -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformJson.cs::PlatformJson.TryParseObject -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformJson.cs::PlatformJson.GetString -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformJson.cs::PlatformJson.GetObject -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::IPlatformLog -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::IPlatformLog.Info -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::IPlatformLog.Warning -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::NullPlatformLog -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::NullPlatformLog.Instance -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::NullPlatformLog.Info -->
<!-- describes: Assets/Scripts/Hullbreach.Platform/PlatformLog.cs::NullPlatformLog.Warning -->

`PlatformHttpMethod` has `Get`, `Post` and `Delete`; `Delete` exists for the
matchmaking queue's leave call (`reference/hullbreach-net.md#matchmaking`).

## Testing

- `tools/plaincs/run_tests.sh --filter "FullyQualifiedName~Hullbreach.Platform"`
  runs the unit tests in `Assets/Tests/EditMode/Hullbreach.Platform.Tests`
  against fakes.
- `PlatformLiveIntegrationTests` runs only when `HULLBREACH_PLATFORM_IT_URL`
  is set. It signs in with `HULLBREACH_PLATFORM_IT_USER` /
  `HULLBREACH_PLATFORM_IT_PASSWORD` (wrong password first, to prove the
  401 mapping), restores the session through `/me` without a server key,
  then runs the admission gate in required mode with
  `HULLBREACH_PLATFORM_SERVER_KEY`, admitting the real token and
  rejecting a forged one. it signs out and checks that both restoration
  and admission reject the revoked token, and asserts no secret appeared
  in any log line.
  Pass the variables to the test host with `dotnet test -e`, which also
  works under WSL with the Windows `dotnet.exe` (WSLENV forwarding does
  not reach the test host there), e.g.
  `tools/plaincs/run_tests.sh --filter "FullyQualifiedName~PlatformLiveIntegrationTests" -e HULLBREACH_PLATFORM_IT_URL=http://127.0.0.1:8002 -e HULLBREACH_PLATFORM_IT_USER=<user> -e HULLBREACH_PLATFORM_IT_PASSWORD=<password> -e HULLBREACH_PLATFORM_SERVER_KEY=<dev key>`.
  `ReporterPostsAMatch_AgainstRealPlatform_Is201AndMovesRatings` also needs a
  second account (`HULLBREACH_PLATFORM_IT_USER2` / `_PASSWORD2`): it admits both
  through the gate, reports a match with `MatchResultReporter`, expects
  `Recorded`, and checks both ratings moved via `GET /api/v1/me`.
  Use a throwaway account and a dev-only key: `-e` values are visible in
  the process list.
  Add `--logger "console;verbosity=detailed"` to see its `[platform-it]`
  transcript.
