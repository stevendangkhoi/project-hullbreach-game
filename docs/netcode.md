# Netcode

Everything under `Assets/Scripts/Hullbreach.Net` is plain C#: no
`UnityEngine`, no specific transport package. It compiles and tests under
the stock .NET SDK (`tools/plaincs/run_tests.sh`) exactly like Ship, World
and Structure do, and for the same reason: a headless server process can
run `ServerSimulation` with no Unity install, and a transport implementer
can build and unit-test their half of the wire without touching a scene.

The Unity-specific adapter lives separately under
`Assets/Scripts/Hullbreach.NetCode.Entities`. It uses Netcode for Entities
for client/server worlds, Unity Transport connections, and RPC delivery while
leaving this assembly and its wire contract engine-independent.

## Netcode for Entities adapter

`HullbreachNetCodeBootstrap` creates 50 Hz server and client worlds, but leaves
their transports idle until the player hosts or joins (direct launches differ;
see [Direct and LAN launch](#direct-and-lan-launch)). `HullbreachLobbyService`
then uses Unity Authentication plus the Multiplayer Services session API to
create Lobby and Relay resources; the package's built-in Entities handler
configures the worlds, starts the host, and connects clients. Relay avoids port
forwarding and does not expose the host's address.

Online play now begins in `Assets/Scenes/LobbyScene.unity`, before gameplay.
Its `LobbyCanvas` instance comes from the editable uGUI/TextMesh Pro prefab at
`Assets/Prefabs/UI/LobbyCanvas.prefab`, so layout, colors, fonts, labels, and
controls can all be restyled in the Inspector without changing networking
code. It can create public or code-only private lobbies, optionally protect
either kind with Unity Lobby's native 8-64 character password, list open public
lobbies, and join by lobby code. The short `ISession.Code` in the waiting room
is the player-facing invite code.

Before the lobby, the scene's `SignInCanvas` overlay asks the player to sign in
with a Hullbreach platform account or to play offline; the token it obtains is
what the client presents in the platform handshake. See
[platform-link.md](platform-link.md#sign-in-screen).

Only the host sees the waiting room's **Start Game** button. Pressing it locks
the lobby and publishes the gameplay scene name as a member-visible session
property; every member then loads `Assets/Scenes/MultiplayerGame.unity`. The
Lobby scene is first in Build Settings, and the multiplayer scene is second.
Use `Hullbreach > Online > Rebuild lobby and multiplayer scenes` only if the
generated scene/prefab structure needs to be reset; normal visual edits should
be made directly to the prefab.

Before `HullbreachNetCodeServerSystem` joins a connection to the match, the
platform handshake must admit it: the client sends its platform token in a
hello RPC and the server validates it against the platform API (optional by
default, enforced with `-hullbreachRequirePlatformAuth`). See
[platform-link.md](platform-link.md) for the flags, environment variables and
failure modes.

`HullbreachNetCodeServerSystem` owns the existing `ServerSimulation`. Gameplay
does not start while the connection is still in the lobby: loading the
multiplayer ghost SubScene joins each `NetworkId` with the demo's nine-block
starter ship, installs the demo planet/moon gravity field, and marks the
connections in-game. Input RPCs feed `SetInput`, and each `ServerOutbox` byte
payload is forwarded without changing its format.
`HullbreachNetCodeClientSystem` reassembles those bytes and hands them to the
existing `ClientReplica`.

Netcode RPC serialization has a 1 KiB payload ceiling. The bridge therefore
splits each existing wire message into 900-byte `HullbreachPayloadRpc` chunks
and reassembles up to 1 MiB per message on the client. This is required for
full `ShipSnapshot` messages: a large ship must not depend on fitting in one
transport packet.

Netcode for Entities RPCs carry reliable topology, damage, and build events.
High-frequency ship and projectile position, rotation, linear velocity, and
angular velocity use an interpolated `HullbreachGhostPose` snapshot instead of
the RPC payload bridge. The engine-independent `ShipState` wire format remains
available for non-Entities transports and tests.

GameObject presentation reads `HullbreachNetCodeClient.Replica`.
`MultiplayerGame` is generated from `DemoScene`, retaining its environment and
gravity visuals while replacing the single-player ships and controllers with
the network presenter. `HullbreachNetCodeView` renders block-type-colored ships
(local blue, remote orange), damage tint, and ghosted projectiles. The editable
HUD displays the local Netcode ID and replicated ship count. With two connected
players that count should become 2 in both instances. No Asset Store content is
required.

The scene starts in **Build** mode. Use `1`-`7` to select a block, left click to
place, right click to remove, and `Esc` to cancel an oriented placement. Cannons
and fins use the same two-click facing selection as the demo. Press `Tab` to
switch between Build and Flight. In Flight, `W/S` control thrust, `A/D` steer,
and Space or left click fires. Placement/removal, collision resolution,
projectile hits, damage, structural failure, and gravity are server-authoritative.

### Direct and LAN launch

<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachNetCodeBootstrap.cs::HullbreachDirectLaunch -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachNetCodeBootstrap.cs::HullbreachDirectLaunch.Plan -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachNetCodeBootstrap.cs::HullbreachNetCodeBootstrap -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachNetCodeBootstrap.cs::HullbreachNetCodeBootstrap.Initialize -->

Without the lobby, a player or server is started from the command line.
`HullbreachDirectLaunch`
parses the arguments once into a
[`DirectLaunchPlan`](reference/hullbreach-net.md#directlaunchplan) and logs
its `Describe()` line; `HullbreachNetCodeBootstrap` then creates exactly the
worlds that role needs:

| Launch | Worlds | Network |
| --- | --- | --- |
| no direct arguments | client + server (idle) | none until the lobby hosts or joins |
| `-hullbreachDirect [-hullbreachPort N]` (host) | client + server | server listens on 0.0.0.0:N, local client joins it |
| `-hullbreachConnect <ip> [-hullbreachPort N]` (client) | client only | dials `<ip>:N` |
| `-hullbreachServer [-hullbreachPort N]`, or a dedicated server build | server only | listens on 0.0.0.0:N |

N defaults to 7979 (UDP). A client-only process matters: NetCode's own
autoconnect forces loopback in a client+server player, so before S47-5 a
`-hullbreachConnect` player opened its own server and dialled itself.

Every direct role loads `MultiplayerGame` right after the first scene
(`RuntimeInitializeLoadType.AfterSceneLoad`), because nothing else would:
in the lobby flow only START GAME loads it. The lobby view does not start
Unity Services in a direct launch, and the HUD's Leave button closes the
game instead of returning to a lobby that was never joined. A desktop
player run with `-batchmode -nographics -hullbreachServer` is a headless
dedicated server, which is how the scripts run one where the Dedicated
Server build module is not installed.

Log lines to look for in `Player.log`: `Hullbreach direct: role=...`,
`Hullbreach direct: loading MultiplayerGame for role=...`,
`Hullbreach server: peer #N joined the match (K in match).` and, on each
client, `Hullbreach match: connected as #N; replicated ships: K`.

### LAN host and join from the lobby

<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachLanLobby.cs::HullbreachLanLobby -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachLanLobby.cs::HullbreachLanLobby.TryHost -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachLanLobby.cs::HullbreachLanLobby.TryJoin -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachLanLobby.cs::HullbreachLanLobby.TryPrepareLanClient -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachLobbyView.cs::HullbreachLobbyView.HostLan -->
<!-- describes: Assets/Scripts/Hullbreach.NetCode.Entities/HullbreachLobbyView.cs::HullbreachLobbyView.JoinLan -->
<!-- describes: Assets/Editor/Hullbreach.Editor/LobbySceneBuilder.cs::LobbySceneBuilder.AddLanControlsMenu -->

The lobby has a PLAY ON LAN section that needs no Unity Services account.
HOST LAN asks the idle server world to listen on 0.0.0.0:7979 and the idle
client world to dial loopback, then loads `MultiplayerGame`. JOIN ADDRESS
reads the typed text with [`LanEndpoint`](reference/hullbreach-net.md#lanendpoint),
dials it from the client world and loads the same scene. A bad address is
shown in the lobby's error line and nothing is started. Log lines:
`Hullbreach LAN: hosting on ...`, `Hullbreach LAN: joining ...` and
`Hullbreach LAN: ... refused: ...`.

Joining switches the idle client's transport to UDP before connecting. Netcode's
default client uses IPC when this process also has a server world, but IPC cannot
reach another game process. The host keeps its normal IPC and UDP listeners.
An existing or pending connection is rejected before changing its driver; restart
the game to join a different LAN match after leaving one.

The three controls are serialized fields on `HullbreachLobbyView` and live
at the top of the right column, above the public lobby list. To update an
older prefab, run `Hullbreach > Online > Add LAN controls to lobby prefab`.
This adds and wires the controls without rebuilding the Relay controls or
scenes. Running it again reuses the same controls.

### Unity Services setup

The project already contains `com.unity.services.multiplayer` and its
Authentication/Lobby/Relay dependencies. No Asset Store download is needed.
The Unity project must be linked to the intended Dashboard project and that
project must allow Authentication, Lobby, and Relay. At runtime players sign
in anonymously; Unity Authentication persists that player identity locally.

For a real two-player check, make two standalone development builds (or use
Multiplayer Play Mode with isolated player profiles). Both instances begin in
`LobbyScene`: host in one, then either select its public listing or enter its
join code in the other. Confirm both names appear in the waiting room, then
press **Start Game** in the host. Both instances should load the multiplayer
scene, show two ships, and report `Replicated ships: 2`. A password is optional
for public and private lobbies, but when present must be 8-64 characters
because that is the Unity Lobby service contract.

## The governing rule: send causes, not effects

Integer flood fill (`Hullbreach.Core.Topology.Connectivity`) is
bit-deterministic on every platform. The float FE solve
(`Hullbreach.Structure.StructuralSolver`) is not: SIMD width, FMA
contraction, and iteration counts all diverge across machines. So the
server never puts a block list on the wire when a ship splits. It sends
"block (x,y) died" (`BlockDestroyed`), and BOTH sides independently run
`Connectivity.FindDetached`/`SplitIntoComponents` over their own copy of the
grid to derive the identical set of detached fragments. A hit that splits a
10,000-block ship in half costs 9 bytes on the wire (`BlockDestroyed`), not
5,000 blocks. `FragmentSpawned` then announces the new body's pose with NO
block list at all, because both sides already agree on which blocks it
contains.

The FE result (`StructuralSolver.BlockStresses`, `BuckledBlocks`) never goes
on the wire. Clients run their own FE solve purely to drive the S37 stress
tint locally; divergence there is cosmetic (a slightly different color) and
invisible to gameplay. `StructuralSolver.BuckledBlocks` is explicitly
documented SERVER-AUTHORITATIVE for this reason: only `ServerSimulation`
acts on it to break a block and broadcast `BlockDestroyed`; a client must
never detach a block from its own buckling analysis, or it desyncs.

## Message table

Every message starts with a `MessageKind` byte (`NetMessages.cs`). Every
reliable message that participates in the ordered event stream also carries
a `u32 Sequence` right after the kind byte (see "Ordering" below for exactly
which ones and why `ShipSnapshot`/`BlockDamaged`/`PowerupApplied` carry one
too, even though the original design note only called it out for
`BlockPlaced`/`BlockDestroyed`/`FragmentSpawned`).

| Kind | Channel | Layout (after the kind byte) | Size |
|---|---|---|---|
| `InputMessage` | client->server, unreliable | `u16 netId, u32 tick, i8 thrustAxis, i8 steer, u8 flags` | 9 B |
| `ShipSnapshot` | server->client, reliable | `u32 seq, u16 netId, u16 count, {i8 x, i8 y, u8 typeId, u8 mods, u8 damage}[count], i16 px, i16 py, u16 rot, i16 vx, i16 vy, u16 av` | 17 + 5*count B |
| `ShipState` | server->client, unreliable, ~50 Hz | `u16 netId, i16 px, i16 py, u16 rot, i16 vx, i16 vy, u16 av, u32 ackedInputTick, u32 serverTick` | 23 B |
| `BlockPlaced` | server->client, reliable ordered | `u32 seq, u16 netId, i8 x, i8 y, u8 typeId, u8 mods` | 11 B |
| `BlockDestroyed` | server->client, reliable ordered | `u32 seq, u16 netId, i8 x, i8 y` | 9 B |
| `FragmentSpawned` | server->client, reliable ordered | `u32 seq, u16 parentId, u16 newId, i16 px, i16 py, u16 rot, i16 vx, i16 vy, u16 av` | 21 B |
| `BlockDamaged` | server->client, reliable | `u32 seq, u16 netId, i8 x, i8 y, u8 damage` | 10 B |
| `PowerupApplied` | server->client, reliable | `u32 seq, u16 netId, i8 x, i8 y, u8 variant, u16 seconds10` | 12 B |
| `GravityWellSpawned` | server->client, reliable | `i16 px, i16 py, i16 mu, u8 radius, u16 seconds10` | 10 B |
| `ShipRemoved` | server->client, reliable ordered | `u32 seq, u16 netId` | 7 B |
| `ReadyUp` | client->server, reliable | `u8 ready` | 2 B |
| `MatchStarted` | server->client, reliable ordered | `u32 seq, i32 peerA, i32 peerB` | 13 B |

### Ready-up

With `ServerSimulation.RequireReady` on, a full lobby waits: each client sends
`ReadyUp`, the engine-free `ReadyGate` (Hullbreach.Match) tracks both flags,
and when both are set the server starts the match and sends `MatchStarted`
to both. A disconnect before the start removes that peer and its flag.

### Decoding untrusted bytes

Decoding never trusts the sender (INV-001). `ByteReader` is constructed
with the received length and, instead of throwing or reading stale bytes,
sets `Failed` on any overrun. `ShipSnapshot.Read` refuses a count above
`ShipSnapshot.MaxBlocks` (4096) or larger than the bytes that actually
arrived before it allocates, and `Write` throws instead of wrapping the u16
count. `ClientReplica.ApplyReceived` honors `length`, rejects kinds a
server never sends (including `Input`) and returns a `ReplicaApplyResult`
rather than throwing.

### Identity and validation

Each peer's `NetId` is its transport id, accepted only in
`1..ServerSimulation.MaxPeerId` (0x7FFF); fragment ids are allocated from
`0x8000..0xFFFF`, so the two spaces cannot collide. `Join` returns a
`JoinResult` and rejects a bad id, a peer that already has a ship, and any
design `DesignValidator.ValidateDesign` refuses (INV-003). Input axes
decode into -1..1 (-128 included) and a delayed input older than the last
accepted one is ignored.

All multi-byte fields are little-endian (`ByteWriter`/`ByteReader` in
`Wire.cs`). Position/velocity are quantized at 1/256 world unit
(`Quantization.PackPosition`); angles as a 16-bit turn fraction
(`Quantization.PackAngle`), which wraps for free on any input, including
many-turn or negative radian values. `GravityWellSpawned.Mu` is a signed
fixed-point value at 1/16 per unit (`GravityWellSpawned.MuScale`), wide
enough to carry the anti-gravity gun's negative `Mu`. `seconds10` fields are
tenths of a second in a `u16`, so a multi-minute buff still fits without a
float on the wire.

Two ships' `ShipState` traffic is 2 * 23 = 46 bytes/tick of unreliable
traffic at the default 50 Hz tick rate (see
`ServerClientEndToEndTests.Poses_Events_Ordering_Timeout_And_Bandwidth`,
which asserts this stays under 200 B/tick).

## Ordering: what the transport must (and must not) guarantee

`ITransport.SendReliable` guarantees **exactly-once delivery**, never drop,
never duplicate. It does **not** guarantee order relative to other
`SendReliable` calls to the same peer. That is deliberate: a real transport
(Unity Transport, Netcode for GameObjects) is free to fan reliable traffic
across internal channels or priorities that reorder relative to each other,
and building an ordering guarantee on top of that in the transport layer is
unnecessary work the application layer already does better.

Ordering instead lives in the `u32 Sequence` on `ShipSnapshot`,
`BlockPlaced`, `BlockDestroyed`, `FragmentSpawned`, `BlockDamaged` and
`PowerupApplied`: `ServerSimulation` hands out one strictly increasing
sequence number across ALL of these (not per-message-kind, one global
counter), and `ClientReplica.ApplyReliable` buffers by sequence, applying a
message only when it is exactly `lastApplied + 1` and draining whatever that
unblocks. The buffer is bounded: a sequence more than
`ClientReplica.MaxReliableWindow` (512) past the last applied one is
dropped (INV-002), so a sender cannot stall delivery and grow memory by
skipping ahead. A payload that fails to decode still consumes its slot.

Snapshots are per ship, not one global baseline. A snapshot already reflects
every event for that ship up to its own sequence, so later-arriving events
for that ship at or below it are skipped, while other ships' events are
unaffected; a snapshot that is not newer than what the replica has for that
ship is ignored. The first snapshot the replica sees sets the stream cursor;
a later one occupies its own slot in the ordered stream. The snapshots a
joiner receives for existing ships are stamped with the current sequence
(not a fresh one), and an event that arrives before its ship's snapshot is
held and replayed when it does. `ShipRemoved` ends a ship's life in the
same ordered stream.

`ShipState` and `GravityWellSpawned` are the two exceptions:

- `ShipState` is unreliable and carries no sequence at all: a dropped,
  duplicated, or reordered pose update is harmless by design (the next one
  supersedes it), so `ClientReplica.ApplyState` just overwrites the latest
  pose sample unconditionally.
- `GravityWellSpawned` carries no sequence either (see the literal layout
  above): a well's effect on gravity does not depend on destruction
  ordering the way `FragmentSpawned` does, so it is applied to the client's
  `GravityField`-equivalent state immediately on arrival.

`LoopbackTransport` (the in-memory transport used by tests and local play)
exercises this on purpose: its `JitterTicks` knob lets two reliable messages
sent in order arrive out of order, and
`LoopbackTransportTests.Jitter_CanReorderTwoReliableMessages` proves it can.
`ServerClientEndToEndTests` runs the whole pipeline against a jittery hub
and still ends up with matching client/server state, because the sequence
buffer (not arrival order) is what the client trusts.

## The tick loop

`ServerSimulation.Tick()`, called once per fixed tick (default 50 Hz,
`TickRate`):

1. Time out peers that have gone `TimeoutSeconds` (default 5 s) without a
   fresh `SetInput` call, removing their ship (S47 criterion 2) and
   broadcasting `ShipRemoved`; the dropped peers are queued for
   `TryDequeueDroppedPeer` so the host can close the connection.
2. Tick the shared `GravityField` (expires temporary wells) and step the
   server's own point-body projectile simulation (gravity + block hit
   tests), which is this class's own minimal `IWorldSink`.
3. For each connected peer in flight mode, in ascending peer-id order
   (deterministic):
   apply its latest buffered input (or a neutral input if none arrived this
   tick), `ShipBody.Step`, drain `PendingShots` into the projectile sim,
   and resolve authoritative ship-to-ship block contacts.
4. Run `StructuralSolver.Tick` and resolve any resulting damage/detach/buckling
   while broadcasting every topology and damage event. Build-mode ships remain
   frozen and accept only server-validated placement/removal requests.
5. Emit one `ShipState` per surviving ship for plain transports. The Entities
   adapter disables this legacy stream and updates the ghost snapshot instead.

Every reliable event goes through `IServerOutbox`
(`ServerOutbox`/`OutboxEntry`): `ServerSimulation` never touches
`ITransport` directly. Whatever owns the real transport (a MonoBehaviour's
`FixedUpdate`, a headless loop) drains `ServerOutbox.Entries` once per tick,
forwards each to `transport.SendReliable`/`SendUnreliable`, and calls
`Clear()`. This is what lets `ServerSimulation` be tested with nothing but a
`ServerOutbox` and no transport at all (see `ServerClientEndToEndTests` and
every `ServerSimulation`-touching test in `MessageRoundTripTests`'
neighbors).

`Join(peer, ShipSnapshot)` validates the design (see "Identity and
validation") and then sends every already-connected ship's snapshot to
the newcomer, then broadcasts the newcomer's own ship's snapshot to
**every** connected peer, including the newcomer itself: a player needs a
snapshot of their own ship exactly like everyone else does, since the
server is the only place the authoritative block layout lives.

## What a transport implementer must do

Implement `ITransport` (`SendReliable`, `SendUnreliable`, `TryReceive`,
`PeerConnected`, `PeerDisconnected`) against Unity Transport, Netcode for
GameObjects, or a raw socket:

- `SendReliable`/`SendUnreliable` copy `payload` before returning (the
  caller reuses its buffer immediately after the call).
- `TryReceive` never blocks; it returns `false` when nothing is queued.
  Reliable and unreliable payloads share one receive queue: the
  `MessageKind` byte at offset 0 is how a caller distinguishes them, not
  which channel they arrived on.
- Fire `PeerConnected`/`PeerDisconnected` exactly once per connect/
  disconnect.
- Do **not** try to guarantee order on the reliable channel beyond
  exactly-once delivery; see "Ordering" above. A transport that already
  happens to preserve order (many do) is not wrong to use here, it is just
  not required, and `ClientReplica` does not rely on it.

Everything above `ITransport` (`ServerSimulation`, `ClientReplica`, every
message struct) is done: a transport implementer's entire job is one class
satisfying that interface, then pumping `ServerSimulation.Tick`/
`IServerOutbox.Entries` on one side and `ClientReplica.ApplyReceived` on the
other. `LoopbackTransport` is a complete, if intentionally lossy and
jittery, reference implementation to copy the shape of.

## What is deliberately NOT sent, and why

- **FE results** (`StructuralSolver.BlockStresses`, in particular
  `BlockStress.VonMises`/`DuctileRatio`/`BrittleRatio`/`BucklingRatio`).
  Not bit-identical across machines; sending them would either desync (if
  trusted) or be pure waste (if only used for a cosmetic tint, which every
  client can compute for itself from data it already has).
- **`StructuralSolver.BuckledBlocks`**. Same reason, doubly so: this is the
  list a client must NEVER act on locally (see its own doc comment). Only
  the server reads it, and only to decide to broadcast `BlockDestroyed`.
- **Detached block lists**. `FragmentSpawned` carries a pose and two IDs,
  nothing else. Both sides derive the block membership from
  `Connectivity.FindDetached` after applying the same ordered
  `BlockDestroyed` events, so listing the blocks would be redundant data
  that also does not survive a 10,000-block ship splitting in half.
- **Full grid diffs for damage**. `BlockDamaged` sends exactly one block's
  new damage byte, because unlike destruction, damage accumulation is a
  genuine cause the client cannot derive (the FE solve that computed the
  overshoot is not deterministic across machines).
