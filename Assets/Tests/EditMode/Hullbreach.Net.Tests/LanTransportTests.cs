#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading;
using Hullbreach.NetCode.Entities;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using EntityWorld = Unity.Entities.World;

namespace Hullbreach.Net.Tests
{
    public sealed class LanTransportTests
    {
        [Test]
        public void LobbyIpcClientCanReachUdpServerAfterLanPreparation()
        {
            using var client = CreateIpcClient();
            using var query = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
            var stream = query.GetSingleton<NetworkStreamDriver>();
            Assert.AreEqual(TransportType.IPC, stream.DriverStore.GetDriverType(NetworkDriverStore.FirstDriverId));

            Assert.IsTrue(HullbreachLanLobby.TryPrepareLanClient(client, out string error), error);
            Assert.AreEqual(TransportType.Socket, stream.DriverStore.GetDriverType(NetworkDriverStore.FirstDriverId));

            // this udp listener cannot be reached by the lobby's old ipc driver.
            var server = NetworkDriver.Create(new UDPNetworkInterface());
            try
            {
                Assert.AreEqual(0, server.Bind(NetworkEndpoint.LoopbackIpv4));
                Assert.AreEqual(0, server.Listen());
                ref var driver = ref stream.DriverStore.GetDriverRW(NetworkDriverStore.FirstDriverId);
                NetworkConnection connection = driver.Connect(server.GetLocalEndpoint());
                NetworkConnection accepted = default;
                bool connectedEvent = false;
                var elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < 3000)
                {
                    driver.ScheduleUpdate().Complete();
                    server.ScheduleUpdate().Complete();
                    if (!accepted.IsCreated) accepted = server.Accept();
                    NetworkEvent.Type eventType;
                    while ((eventType = driver.PopEvent(out _, out _)) != NetworkEvent.Type.Empty)
                    {
                        Assert.AreNotEqual(NetworkEvent.Type.Disconnect, eventType);
                        if (eventType == NetworkEvent.Type.Connect) connectedEvent = true;
                    }
                    while ((eventType = server.PopEvent(out _, out _)) != NetworkEvent.Type.Empty)
                        Assert.AreNotEqual(NetworkEvent.Type.Disconnect, eventType);
                    if (accepted.IsCreated && driver.GetConnectionState(connection) == NetworkConnection.State.Connected)
                        break;
                    Thread.Sleep(1);
                }

                Assert.IsTrue(accepted.IsCreated, "the udp server never received the lan client");
                Assert.IsTrue(connectedEvent, "the client never received its connection event");
                Assert.AreEqual(NetworkConnection.State.Connected, driver.GetConnectionState(connection));
            }
            finally
            {
                server.Dispose();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LanPreparationKeepsActiveOrPendingConnection(bool pending)
        {
            using var client = CreateIpcClient();
            Entity connection = client.EntityManager.CreateEntity(pending
                ? typeof(NetworkStreamRequestConnect)
                : typeof(NetworkStreamConnection));
            try
            {
                Assert.IsFalse(HullbreachLanLobby.TryPrepareLanClient(client, out string error));
                StringAssert.Contains("already active or starting", error);
                using var query = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
                var stream = query.GetSingleton<NetworkStreamDriver>();
                Assert.AreEqual(TransportType.IPC, stream.DriverStore.GetDriverType(NetworkDriverStore.FirstDriverId));
            }
            finally
            {
                // the placeholder has no real transport connection to disconnect.
                if (!pending) client.EntityManager.RemoveComponent<NetworkStreamConnection>(connection);
            }
        }

        static EntityWorld CreateIpcClient()
        {
            var world = new EntityWorld("lan transport test", WorldFlags.GameClient);
            INetworkStreamDriverConstructor previous = NetworkStreamReceiveSystem.DriverConstructor;
            try
            {
                NetworkStreamReceiveSystem.DriverConstructor = new IpcClientConstructor();
                Unity.Entities.WorldExtensions.GetOrCreateSystem<NetDebugSystem>(world);
                Unity.Entities.WorldExtensions.GetOrCreateSystem<RpcSystem>(world);
                Unity.Entities.WorldExtensions.GetOrCreateSystem<NetworkStreamReceiveSystem>(world);
                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
            finally
            {
                NetworkStreamReceiveSystem.DriverConstructor = previous;
            }
        }

        sealed class IpcClientConstructor : INetworkStreamDriverConstructor
        {
            public void CreateClientDriver(EntityWorld world, ref NetworkDriverStore store, NetDebug debug)
            {
                using var settings = DefaultDriverBuilder.GetNetworkClientSettings();
                DefaultDriverBuilder.RegisterClientIpcDriver(world, ref store, debug, settings);
            }

            public void CreateServerDriver(EntityWorld world, ref NetworkDriverStore store, NetDebug debug)
            {
                throw new NotSupportedException();
            }
        }
    }
}
#endif
