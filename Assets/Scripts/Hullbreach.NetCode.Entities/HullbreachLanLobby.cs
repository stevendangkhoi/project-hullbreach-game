using Hullbreach.Net;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hullbreach.NetCode.Entities
{
    // Hosts or joins a LAN match from the lobby screen with no Unity Services (S28-2).
    // It drives the idle worlds the lobby launch already created, then loads the match.
    // frob:doc docs/netcode.md#lan-host-and-join-from-the-lobby
    public static class HullbreachLanLobby
    {
        // Server listens on every interface and the local client dials loopback.
        // frob:doc docs/netcode.md#lan-host-and-join-from-the-lobby
        public static bool TryHost(ushort port, out string error)
        {
            World server = ClientServerBootstrap.ServerWorld;
            World client = ClientServerBootstrap.ClientWorld;
            if (server == null || client == null)
            {
                error = "No server or client world is running; LAN hosting needs the lobby launch.";
                Debug.LogWarning($"Hullbreach LAN: host refused: {error}");
                return false;
            }

            Request(server, new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.AnyIpv4.WithPort(port) });
            Request(client, new NetworkStreamRequestConnect { Endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port) });
            Debug.Log($"Hullbreach LAN: hosting on 0.0.0.0:{port}, loading {DirectLaunchPlan.GameScene}.");
            error = null;
            LoadMatch();
            return true;
        }

        // Dials the typed address; the endpoint was already validated by LanEndpoint.
        // frob:doc docs/netcode.md#lan-host-and-join-from-the-lobby
        public static bool TryJoin(string text, out string error)
        {
            if (!LanEndpoint.TryParse(text, DirectLaunchPlan.DefaultPort, out LanEndpoint lan, out error))
            {
                Debug.LogWarning($"Hullbreach LAN: join refused: {error}");
                return false;
            }

            World client = ClientServerBootstrap.ClientWorld;
            if (client == null)
            {
                error = "No client world is running; LAN joining needs the lobby launch.";
                Debug.LogWarning($"Hullbreach LAN: join refused: {error}");
                return false;
            }

            if (!TryPrepareLanClient(client, out error))
            {
                Debug.LogWarning($"Hullbreach LAN: join refused: {error}");
                return false;
            }

            // A validated numeric address cannot fail NetworkEndpoint parsing.
            NetworkEndpoint.TryParse(lan.Address, lan.Port, out NetworkEndpoint endpoint);
            Request(client, new NetworkStreamRequestConnect { Endpoint = endpoint });
            Debug.Log($"Hullbreach LAN: joining {lan}, loading {DirectLaunchPlan.GameScene}.");
            LoadMatch();
            return true;
        }

        // the lobby has an idle server world, so netcode initially gives its client an ipc driver.
        // lan joining needs udp to reach a server in another process or on another computer.
        // frob:doc docs/netcode.md#lan-host-and-join-from-the-lobby
        public static bool TryPrepareLanClient(World client, out string error)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            error = "LAN joining is available in the desktop game.";
            return false;
#else
            client.EntityManager.CompleteAllTrackedJobs();
            using var connections = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamConnection));
            using var pending = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamRequestConnect));
            if (!connections.IsEmptyIgnoreFilter || !pending.IsEmptyIgnoreFilter)
            {
                error = "A connection is already active or starting. Restart the game before joining another LAN match.";
                return false;
            }

            using var drivers = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
            using var debug = client.EntityManager.CreateEntityQuery(typeof(NetDebug));
            var driver = drivers.GetSingleton<NetworkStreamDriver>();
            var store = new NetworkDriverStore();
            using var settings = DefaultDriverBuilder.GetNetworkClientSettings();
            DefaultDriverBuilder.RegisterClientUdpDriver(client, ref store, debug.GetSingleton<NetDebug>(), settings);
            driver.ResetDriverStore(client.Unmanaged, ref store);
            Debug.Log("Hullbreach LAN: client transport set to UDP.");
            error = null;
            return true;
#endif
        }

        static void Request<T>(World world, T request) where T : unmanaged, IComponentData
        {
            Entity entity = world.EntityManager.CreateEntity(typeof(T));
            world.EntityManager.SetComponentData(entity, request);
        }

        static void LoadMatch() => SceneManager.LoadScene(DirectLaunchPlan.GameScene, LoadSceneMode.Single);
    }
}
