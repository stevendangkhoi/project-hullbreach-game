using System;
using System.Collections.Generic;
using Hullbreach.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;

namespace Hullbreach.NetCode.Entities
{
    /// <summary>Shared client-side state for presentation code in the GameObject world.</summary>
    public static class HullbreachNetCodeClient
    {
        public readonly struct ProjectileVisual
        {
            public readonly ushort OwnerNetId;
            public readonly float2 Position;
            public readonly float2 Velocity;
            public readonly float Radius;

            public ProjectileVisual(ushort ownerNetId, float2 position, float2 velocity, float radius)
            {
                OwnerNetId = ownerNetId;
                Position = position;
                Velocity = velocity;
                Radius = radius;
            }
        }

        public static ClientReplica Replica { get; internal set; }
        public static ushort LocalNetworkId { get; internal set; }
        public static IReadOnlyDictionary<uint, ProjectileVisual> Projectiles { get; internal set; }
        public static bool IsConnected => Replica != null && LocalNetworkId != 0;
    }

    sealed class PendingPayload
    {
        public readonly byte[][] Chunks;
        public int Received;
        public int TotalBytes;

        public PendingPayload(int count) => Chunks = new byte[count][];
    }

    /// <summary>Bridges local input and received RPC payloads into ClientReplica.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HullbreachNetCodeClientSystem : SystemBase
    {
        readonly Dictionary<uint, PendingPayload> _pending = new Dictionary<uint, PendingPayload>();
        ClientReplica _replica;
        EntityQuery _payloadQuery;
        EntityQuery _ghostQuery;
        readonly Dictionary<uint, HullbreachNetCodeClient.ProjectileVisual> _projectiles =
            new Dictionary<uint, HullbreachNetCodeClient.ProjectileVisual>();
        uint _clientTick;
        string _lastMatchStatus;
        int _loggedTransportId;

        protected override void OnCreate()
        {
            _payloadQuery = GetEntityQuery(
                ComponentType.ReadOnly<HullbreachPayloadRpc>(),
                ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _ghostQuery = GetEntityQuery(ComponentType.ReadOnly<HullbreachGhostPose>());
            ResetMatch();
            RequireForUpdate<NetworkStreamDriver>();
        }

        // worlds survive returning to the lobby; each match needs a fresh replica and sequence history.
        internal void ResetMatch()
        {
            EntityManager.CompleteAllTrackedJobs();
            EntityManager.DestroyEntity(_payloadQuery);
            EntityManager.DestroyEntity(_ghostQuery);
            using var inputs = EntityManager.CreateEntityQuery(typeof(HullbreachInputRpc));
            using var builds = EntityManager.CreateEntityQuery(typeof(HullbreachBuildRpc));
            EntityManager.DestroyEntity(inputs);
            EntityManager.DestroyEntity(builds);
            _pending.Clear();
            _projectiles.Clear();
            _clientTick = 0;
            _lastMatchStatus = null;
            _loggedTransportId = 0;
            _replica = new ClientReplica();
            // Remote clients never see the server's feed, so the replicated match end feeds the results screen.
            if (!World.IsThinClient()) _replica.MatchFeed = Hullbreach.Platform.MatchEndFeed.Shared;
            if (!World.IsThinClient())
            {
                HullbreachNetCodeClient.Replica = _replica;
                HullbreachNetCodeClient.Projectiles = _projectiles;
                HullbreachNetCodeClient.LocalNetworkId = 0;
            }
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(HullbreachNetCodeClient.Replica, _replica))
            {
                HullbreachNetCodeClient.Replica = null;
                HullbreachNetCodeClient.LocalNetworkId = 0;
                HullbreachNetCodeClient.Projectiles = null;
            }
            _pending.Clear();
            _projectiles.Clear();
        }

        protected override void OnUpdate()
        {
            ReceivePayloads();
            ReceiveGhostPoses();

            if (!SystemAPI.TryGetSingleton<NetworkId>(out var networkId))
            {
                if (!World.IsThinClient()) HullbreachNetCodeClient.LocalNetworkId = 0;
                _loggedTransportId = 0;
                return;
            }

            if (World.IsThinClient()) return;
            HullbreachNetCodeClient.LocalNetworkId = (ushort)networkId.Value;

            // Lobby connections intentionally carry no gameplay traffic. The
            // controller only exists in MultiplayerGame after the host starts.
            if (!HullbreachNetworkGameplayController.Active) return;

            // Greppable proof that this client reached the match (scripts and docs use it).
            string matchStatus = DirectLaunchPlan.MatchStatus(networkId.Value, _replica.Ships.Count);
            if (matchStatus != _lastMatchStatus)
            {
                _lastMatchStatus = matchStatus;
                Debug.Log(matchStatus);
            }

            Entity connection = SystemAPI.GetSingletonEntity<NetworkId>();
            if (_loggedTransportId != networkId.Value)
            {
                // log the actual transport once so online tests can distinguish relay from lan.
                var stream = EntityManager.GetComponentData<NetworkStreamConnection>(connection);
                bool relay = SystemAPI.GetSingleton<NetworkStreamDriver>().UseRelay(stream);
                Debug.Log($"Hullbreach match: transport={(relay ? "Relay" : "direct/local")} for #{networkId.Value}");
                _loggedTransportId = networkId.Value;
            }
            if (!EntityManager.HasComponent<NetworkStreamInGame>(connection))
                EntityManager.AddComponent<NetworkStreamInGame>(connection);

            _clientTick++;
            bool building = HullbreachNetworkGameplayController.IsBuildMode;
            InputMessage basicInput = ClientReplica.BuildInput(
                (ushort)networkId.Value,
                _clientTick,
                !building ? Input.GetAxisRaw("Vertical") : 0f,
                !building ? Input.GetAxisRaw("Horizontal") : 0f,
                !building && (Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)));
            byte flags = basicInput.Flags;
            if (building) flags |= InputMessage.BuildModeBit;
            InputMessage input = new InputMessage(basicInput.NetId, basicInput.Tick,
                basicInput.ThrustAxis, basicInput.Steer, flags);

            Entity rpcEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rpcEntity, new HullbreachInputRpc
            {
                Tick = input.Tick,
                ThrustAxis = input.ThrustAxis,
                Steer = input.Steer,
                Flags = input.Flags,
            });
            EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest { TargetConnection = Entity.Null });

            while (HullbreachNetworkGameplayController.TryDequeueBuild(out var build))
            {
                Entity buildEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(buildEntity, new HullbreachBuildRpc
                {
                    X = build.X,
                    Y = build.Y,
                    TypeId = build.TypeId,
                    Modifiers = build.Modifiers,
                    Action = (byte)build.Action,
                });
                EntityManager.AddComponentData(buildEntity,
                    new SendRpcCommandRequest { TargetConnection = Entity.Null });
            }
        }

        void ReceiveGhostPoses()
        {
            if (World.IsThinClient()) return;

            using var poses = _ghostQuery.ToComponentDataArray<HullbreachGhostPose>(Allocator.Temp);
            var liveProjectiles = new HashSet<uint>();
            for (int i = 0; i < poses.Length; i++)
            {
                HullbreachGhostPose pose = poses[i];
                if (pose.Kind == (byte)HullbreachGhostKind.Ship)
                {
                    _replica.ApplyGhostPose(pose.NetId, pose.Position, pose.Rotation,
                        pose.Velocity, pose.AngularVelocity, _clientTick);
                }
                else if (pose.Kind == (byte)HullbreachGhostKind.Projectile)
                {
                    liveProjectiles.Add(pose.SimulationId);
                    _projectiles[pose.SimulationId] = new HullbreachNetCodeClient.ProjectileVisual(
                        pose.NetId, pose.Position, pose.Velocity, pose.Radius);
                }
            }

            var stale = new List<uint>();
            foreach (uint id in _projectiles.Keys)
                if (!liveProjectiles.Contains(id)) stale.Add(id);
            foreach (uint id in stale) _projectiles.Remove(id);
        }

        void ReceivePayloads()
        {
            using var entities = _payloadQuery.ToEntityArray(Allocator.Temp);
            using var payloads = _payloadQuery.ToComponentDataArray<HullbreachPayloadRpc>(Allocator.Temp);

            for (int i = 0; i < payloads.Length; i++) AcceptChunk(payloads[i]);
            if (entities.Length > 0) EntityManager.DestroyEntity(entities);
        }

        void AcceptChunk(HullbreachPayloadRpc rpc)
        {
            if (rpc.ChunkCount == 0 || rpc.ChunkIndex >= rpc.ChunkCount ||
                rpc.ChunkCount > (HullbreachNetCodeConstants.MaximumMessageBytes / HullbreachNetCodeConstants.PayloadBytesPerChunk) + 1)
                return;

            if (!_pending.TryGetValue(rpc.MessageId, out PendingPayload pending))
            {
                pending = new PendingPayload(rpc.ChunkCount);
                _pending.Add(rpc.MessageId, pending);
            }
            if (pending.Chunks.Length != rpc.ChunkCount || pending.Chunks[rpc.ChunkIndex] != null) return;

            var chunk = new byte[rpc.Payload.Length];
            for (int i = 0; i < chunk.Length; i++) chunk[i] = rpc.Payload[i];
            pending.Chunks[rpc.ChunkIndex] = chunk;
            pending.Received++;
            pending.TotalBytes += chunk.Length;

            if (pending.TotalBytes > HullbreachNetCodeConstants.MaximumMessageBytes)
            {
                _pending.Remove(rpc.MessageId);
                return;
            }
            if (pending.Received != pending.Chunks.Length) return;

            var message = new byte[pending.TotalBytes];
            int offset = 0;
            foreach (byte[] part in pending.Chunks)
            {
                Buffer.BlockCopy(part, 0, message, offset, part.Length);
                offset += part.Length;
            }
            _pending.Remove(rpc.MessageId);
            _replica.ApplyReceived(message, message.Length);
        }
    }
}
