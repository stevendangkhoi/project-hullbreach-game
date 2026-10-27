using Hullbreach.Platform;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Hullbreach.NetCode.Entities
{
    // Latest admission verdict for the local player, read by the HUDs.
    // frob:doc docs/platform-link.md#handshake
    public static class HullbreachPlatformAdmission
    {
        // frob:doc docs/platform-link.md#handshake
        public static bool HasVerdict { get; internal set; }

        // frob:doc docs/platform-link.md#handshake
        public static bool Admitted { get; internal set; }

        // frob:doc docs/platform-link.md#handshake
        public static bool Verified { get; internal set; }

        // frob:doc docs/platform-link.md#handshake
        public static string DisplayName { get; internal set; } = string.Empty;

        // frob:doc docs/platform-link.md#handshake
        public static string Reason { get; internal set; } = string.Empty;

        // One HUD line; never includes the token.
        // frob:doc docs/platform-link.md#handshake
        public static string StatusLine
        {
            get
            {
                if (!HasVerdict) return "Platform: waiting for server admission...";
                if (!Admitted) return "Platform: rejected - " + Reason;
                if (Verified) return "Platform: signed in as " + DisplayName;
                return Reason.Length > 0 ? $"Platform: guest ({DisplayName}) - {Reason}" : $"Platform: guest ({DisplayName})";
            }
        }

        internal static void Clear()
        {
            HasVerdict = false;
            Admitted = false;
            Verified = false;
            DisplayName = string.Empty;
            Reason = string.Empty;
        }
    }

    // Sends the platform hello once per connection and records the server's verdict.
    // Thin clients are bots without a player session, so they always say hello as guests.
    // frob:doc docs/platform-link.md#handshake
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HullbreachPlatformAuthClientSystem : SystemBase
    {
        EntityQuery _helloPendingQuery;
        EntityQuery _admissionQuery;

        protected override void OnCreate()
        {
            _helloPendingQuery = GetEntityQuery(
                ComponentType.ReadOnly<NetworkId>(),
                ComponentType.Exclude<HullbreachPlatformHelloSent>());
            _admissionQuery = GetEntityQuery(
                ComponentType.ReadOnly<HullbreachPlatformAdmissionRpc>(),
                ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            RequireForUpdate<NetworkStreamDriver>();
        }

        protected override void OnDestroy()
        {
            if (!World.IsThinClient()) HullbreachPlatformAdmission.Clear();
        }

        protected override void OnUpdate()
        {
            bool thin = World.IsThinClient();
            SendHello(thin);
            ReceiveAdmission(thin);
        }

        void SendHello(bool thin)
        {
            using var connections = _helloPendingQuery.ToEntityArray(Allocator.Temp);
            foreach (Entity connection in connections)
            {
                // keep a rejection visible after disconnect; clear it for a new connection.
                if (!thin) HullbreachPlatformAdmission.Clear();
                EntityManager.AddComponent<HullbreachPlatformHelloSent>(connection);
                var hello = new HullbreachPlatformHelloRpc();
                bool signedIn = !thin && PlatformSession.IsSignedIn;
                if (signedIn)
                {
                    hello.Token.CopyFromTruncated(PlatformSession.Token);
                    hello.Username.CopyFromTruncated(PlatformSession.Username);
                }
                Entity rpcEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(rpcEntity, hello);
                EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest { TargetConnection = Entity.Null });
                if (!thin)
                    Debug.Log(signedIn
                        ? $"Hullbreach platform: presenting platform session for '{PlatformSession.Username}' to the server."
                        : "Hullbreach platform: not signed in; joining as a guest.");
            }
        }

        void ReceiveAdmission(bool thin)
        {
            using var entities = _admissionQuery.ToEntityArray(Allocator.Temp);
            using var verdicts = _admissionQuery.ToComponentDataArray<HullbreachPlatformAdmissionRpc>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (thin) continue;
                var verdict = verdicts[i];
                HullbreachPlatformAdmission.HasVerdict = true;
                HullbreachPlatformAdmission.Admitted = verdict.Admitted;
                HullbreachPlatformAdmission.Verified = verdict.Verified;
                HullbreachPlatformAdmission.DisplayName = verdict.DisplayName.ToString();
                HullbreachPlatformAdmission.Reason = verdict.Reason.ToString();
                if (verdict.Admitted)
                    Debug.Log("Hullbreach platform: " + HullbreachPlatformAdmission.StatusLine);
                else
                    Debug.LogWarning("Hullbreach platform: server rejected this client: " + HullbreachPlatformAdmission.Reason);
            }
            if (entities.Length > 0) EntityManager.DestroyEntity(entities);
        }
    }
}
