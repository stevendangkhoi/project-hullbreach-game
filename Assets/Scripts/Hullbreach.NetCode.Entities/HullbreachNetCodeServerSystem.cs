using System;
using System.Collections.Generic;
using Hullbreach.Core;
using Hullbreach.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Hullbreach.NetCode.Entities
{
    /// <summary>Runs ServerSimulation in the Netcode for Entities server world.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HullbreachNetCodeServerSystem : SystemBase
    {
        readonly HashSet<int> _joinedPeers = new HashSet<int>();
        readonly Dictionary<int, Entity> _connections = new Dictionary<int, Entity>();
        readonly Dictionary<int, Entity> _shipGhosts = new Dictionary<int, Entity>();
        readonly Dictionary<uint, Entity> _projectileGhosts = new Dictionary<uint, Entity>();
        ServerOutbox _outbox;
        ServerSimulation _simulation;
        EntityQuery _connectionQuery;
        EntityQuery _inputQuery;
        EntityQuery _buildQuery;
        uint _nextMessageId = 1;
        bool _arenaConfigured;

        protected override void OnCreate()
        {
            _connectionQuery = GetEntityQuery(
                ComponentType.ReadOnly<NetworkId>(),
                ComponentType.ReadOnly<NetworkStreamConnection>());
            _inputQuery = GetEntityQuery(
                ComponentType.ReadOnly<HullbreachInputRpc>(),
                ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _buildQuery = GetEntityQuery(
                ComponentType.ReadOnly<HullbreachBuildRpc>(),
                ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            ResetMatch();
            RequireForUpdate<NetworkStreamDriver>();
        }

        // reset only after all connections leave, so a new room cannot inherit old ships or shots.
        internal void ResetMatch()
        {
            EntityManager.CompleteAllTrackedJobs();
            using var ghosts = EntityManager.CreateEntityQuery(typeof(HullbreachGhostPose));
            using var payloads = EntityManager.CreateEntityQuery(typeof(HullbreachPayloadRpc));
            EntityManager.DestroyEntity(ghosts);
            EntityManager.DestroyEntity(payloads);
            EntityManager.DestroyEntity(_inputQuery);
            EntityManager.DestroyEntity(_buildQuery);
            _joinedPeers.Clear();
            _connections.Clear();
            _shipGhosts.Clear();
            _projectileGhosts.Clear();
            _nextMessageId = 1;
            _arenaConfigured = false;
            _outbox = new ServerOutbox();
            _simulation = new ServerSimulation(_outbox)
            {
                TickRate = HullbreachNetCodeConstants.SimulationTickRate,
                EmitPoseMessages = false,
            };
        }

        protected override void OnUpdate()
        {
            SynchronizeConnections();
            PrepareGameplayIfLoaded();
            if (!_arenaConfigured) return;
            ConsumeInputs();
            ConsumeBuildRequests();
            _simulation.Tick();
            SynchronizeGhosts();
            FlushOutbox();
        }

        void PrepareGameplayIfLoaded()
        {
            if (!SystemAPI.TryGetSingleton<HullbreachGhostPrefab>(out _)) return;

            if (!_arenaConfigured)
            {
                _simulation.ConfigureDemoArena();
                _arenaConfigured = true;
            }

            // Only peers the platform gate admitted get snapshots (docs/platform-link.md#handshake).
            foreach (Entity connection in _connections.Values)
                if (EntityManager.HasComponent<HullbreachPlatformAdmitted>(connection) &&
                    !EntityManager.HasComponent<NetworkStreamInGame>(connection))
                    EntityManager.AddComponent<NetworkStreamInGame>(connection);
        }

        void SynchronizeConnections()
        {
            _connections.Clear();
            using var entities = _connectionQuery.ToEntityArray(Allocator.Temp);
            using var ids = _connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            var active = new HashSet<int>();
            bool gameplayLoaded = SystemAPI.HasSingleton<HullbreachGhostPrefab>();

            for (int i = 0; i < entities.Length; i++)
            {
                int peer = ids[i].Value;
                active.Add(peer);
                _connections[peer] = entities[i];
                bool admitted = EntityManager.HasComponent<HullbreachPlatformAdmitted>(entities[i]);
                if (gameplayLoaded && admitted && _joinedPeers.Add(peer))
                {
                    _simulation.Join(peer, CreateStarterShip(peer));
                    Debug.Log($"Hullbreach server: peer #{peer} joined the match ({_joinedPeers.Count} in match).");
                }
            }

            var disconnected = new List<int>();
            foreach (int peer in _joinedPeers)
                if (!active.Contains(peer)) disconnected.Add(peer);

            foreach (int peer in disconnected)
            {
                _joinedPeers.Remove(peer);
                _simulation.Leave(peer);
                Debug.Log($"Hullbreach server: peer #{peer} left the match ({_joinedPeers.Count} in match).");
                if (_shipGhosts.TryGetValue(peer, out Entity ghost) && EntityManager.Exists(ghost))
                    EntityManager.DestroyEntity(ghost);
                _shipGhosts.Remove(peer);
            }
        }

        void ConsumeInputs()
        {
            using var entities = _inputQuery.ToEntityArray(Allocator.Temp);
            using var inputs = _inputQuery.ToComponentDataArray<HullbreachInputRpc>(Allocator.Temp);
            using var requests = _inputQuery.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity source = requests[i].SourceConnection;
                if (EntityManager.HasComponent<NetworkId>(source))
                {
                    int peer = EntityManager.GetComponentData<NetworkId>(source).Value;
                    var rpc = inputs[i];
                    _simulation.SetInput(peer, new InputMessage((ushort)peer, rpc.Tick, rpc.ThrustAxis, rpc.Steer, rpc.Flags));
                }
            }

            if (entities.Length > 0) EntityManager.DestroyEntity(entities);
        }

        void ConsumeBuildRequests()
        {
            using var entities = _buildQuery.ToEntityArray(Allocator.Temp);
            using var requests = _buildQuery.ToComponentDataArray<HullbreachBuildRpc>(Allocator.Temp);
            using var sources = _buildQuery.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity source = sources[i].SourceConnection;
                if (!EntityManager.HasComponent<NetworkId>(source)) continue;
                int peer = EntityManager.GetComponentData<NetworkId>(source).Value;
                HullbreachBuildRpc request = requests[i];
                if (request.Action == (byte)HullbreachBuildAction.Place)
                    _simulation.TryPlaceBlock(peer, request.X, request.Y, request.TypeId, request.Modifiers);
                else if (request.Action == (byte)HullbreachBuildAction.Remove)
                    _simulation.TryRemoveBlock(peer, request.X, request.Y);
            }

            if (entities.Length > 0) EntityManager.DestroyEntity(entities);
        }

        void SynchronizeGhosts()
        {
            if (!SystemAPI.TryGetSingleton<HullbreachGhostPrefab>(out var prefab) || prefab.Value == Entity.Null)
                return;

            var ships = _simulation.Ships;
            foreach (var pair in ships)
            {
                if (!_shipGhosts.TryGetValue(pair.Key, out Entity entity) || !EntityManager.Exists(entity))
                {
                    entity = EntityManager.Instantiate(prefab.Value);
                    _shipGhosts[pair.Key] = entity;
                    if (EntityManager.HasComponent<GhostOwner>(entity))
                        EntityManager.SetComponentData(entity, new GhostOwner { NetworkId = pair.Key });
                }

                var ship = pair.Value;
                EntityManager.SetComponentData(entity, new HullbreachGhostPose
                {
                    Kind = (byte)HullbreachGhostKind.Ship,
                    NetId = (ushort)pair.Key,
                    SimulationId = (uint)pair.Key,
                    Position = ship.Position,
                    Rotation = ship.Rotation,
                    Velocity = ship.Velocity,
                    AngularVelocity = ship.AngularVelocity,
                    Radius = 0f,
                });
            }

            var liveShips = new HashSet<int>(ships.Keys);
            var staleShips = new List<int>();
            foreach (var pair in _shipGhosts)
                if (!liveShips.Contains(pair.Key)) staleShips.Add(pair.Key);
            foreach (int peer in staleShips)
            {
                if (EntityManager.Exists(_shipGhosts[peer])) EntityManager.DestroyEntity(_shipGhosts[peer]);
                _shipGhosts.Remove(peer);
            }

            IReadOnlyList<ServerSimulation.ProjectileSnapshot> projectiles = _simulation.Projectiles;
            var liveProjectiles = new HashSet<uint>();
            foreach (var projectile in projectiles)
            {
                liveProjectiles.Add(projectile.Id);
                if (!_projectileGhosts.TryGetValue(projectile.Id, out Entity entity) || !EntityManager.Exists(entity))
                {
                    entity = EntityManager.Instantiate(prefab.Value);
                    _projectileGhosts[projectile.Id] = entity;
                    if (EntityManager.HasComponent<GhostOwner>(entity))
                        EntityManager.SetComponentData(entity, new GhostOwner { NetworkId = math.max(0, projectile.OwnerPeer) });
                }

                EntityManager.SetComponentData(entity, new HullbreachGhostPose
                {
                    Kind = (byte)HullbreachGhostKind.Projectile,
                    NetId = (ushort)math.max(0, projectile.OwnerPeer),
                    SimulationId = projectile.Id,
                    Position = projectile.Position,
                    Rotation = math.atan2(projectile.Velocity.y, projectile.Velocity.x),
                    Velocity = projectile.Velocity,
                    AngularVelocity = 0f,
                    Radius = projectile.Radius,
                });
            }

            var staleProjectiles = new List<uint>();
            foreach (var pair in _projectileGhosts)
                if (!liveProjectiles.Contains(pair.Key)) staleProjectiles.Add(pair.Key);
            foreach (uint id in staleProjectiles)
            {
                if (EntityManager.Exists(_projectileGhosts[id])) EntityManager.DestroyEntity(_projectileGhosts[id]);
                _projectileGhosts.Remove(id);
            }
        }

        void FlushOutbox()
        {
            foreach (var entry in _outbox.Entries)
            {
                if (!_connections.TryGetValue(entry.Peer, out Entity target)) continue;
                SendPayload(target, entry.Bytes);
            }
            _outbox.Clear();
        }

        void SendPayload(Entity target, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > HullbreachNetCodeConstants.MaximumMessageBytes)
                throw new InvalidOperationException($"Invalid Hullbreach network payload size: {bytes?.Length ?? 0}");

            int chunkSize = HullbreachNetCodeConstants.PayloadBytesPerChunk;
            int chunkCount = (bytes.Length + chunkSize - 1) / chunkSize;
            uint messageId = _nextMessageId++;
            if (_nextMessageId == 0) _nextMessageId = 1;

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int start = chunkIndex * chunkSize;
                int count = math.min(chunkSize, bytes.Length - start);
                var payload = new FixedList4096Bytes<byte>();
                for (int i = 0; i < count; i++) payload.Add(bytes[start + i]);

                Entity rpcEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(rpcEntity, new HullbreachPayloadRpc
                {
                    MessageId = messageId,
                    ChunkIndex = (ushort)chunkIndex,
                    ChunkCount = (ushort)chunkCount,
                    Payload = payload,
                });
                EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest { TargetConnection = target });
            }
        }

        static ShipSnapshot CreateStarterShip(int peer)
        {
            float x = ((peer & 1) == 0 ? 1f : -1f) * (3f + (peer % 4) * 2f);
            var blocks = new[]
            {
                new SnapshotBlock(0, 0, BlockTypes.Core, 0, 0),
                new SnapshotBlock(0, 1, BlockTypes.Hull, 0, 0),
                new SnapshotBlock(0, -1, BlockTypes.Hull, 0, 0),
                new SnapshotBlock(-1, -1, BlockTypes.Thruster, 0, 0),
                new SnapshotBlock(1, -1, BlockTypes.Thruster, 0, 0),
                new SnapshotBlock(0, -2, BlockTypes.RetroThruster, 0, 0),
                new SnapshotBlock(0, 2, BlockTypes.Cannon, 0, 0),
                new SnapshotBlock(-1, 1, BlockTypes.Fin, 3, 0),
                new SnapshotBlock(1, 1, BlockTypes.Fin, 1, 0),
            };
            return new ShipSnapshot(0, (ushort)peer, blocks,
                Quantization.PackPosition(x), 0, 0, 0, 0, 0);
        }
    }
}
