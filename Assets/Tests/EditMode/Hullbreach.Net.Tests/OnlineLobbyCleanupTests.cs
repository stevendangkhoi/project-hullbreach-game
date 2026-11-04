#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Hullbreach.Core;
using Hullbreach.Net;
using Hullbreach.NetCode.Entities;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine.TestTools;
using EntityWorld = Unity.Entities.World;

namespace Hullbreach.Net.Tests
{
    public sealed class OnlineLobbyCleanupTests
    {
        [Test]
        public void LeavingStopsTheServerListenerAndAllowsHostingAgain()
        {
            using var server = CreateServer();
            using var query = server.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
            var driver = query.GetSingleton<NetworkStreamDriver>();
            Assert.IsTrue(driver.Listen(NetworkEndpoint.LoopbackIpv4));
            Assert.IsTrue(IsListening(driver));

            Task cleanup = Disconnect(server);
            Assert.IsTrue(cleanup.IsCompleted, "an idle listener should close immediately");
            cleanup.GetAwaiter().GetResult();
            Assert.IsFalse(IsListening(driver), "leaving must release the old relay/listen driver");
            Assert.IsTrue(driver.Listen(NetworkEndpoint.AnyIpv4), "the reset server must be reusable");
        }

        [UnityTest]
        public IEnumerator DriverResetWaitsUntilConnectionCleanupFinishes()
        {
            using var server = CreateServer();
            using var query = server.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
            var driver = query.GetSingleton<NetworkStreamDriver>();
            Assert.IsTrue(driver.Listen(NetworkEndpoint.LoopbackIpv4));
            Entity connection = server.EntityManager.CreateEntity(typeof(NetworkStreamConnection));
            Task cleanup = Disconnect(server);
            try
            {
                Assert.IsFalse(cleanup.IsCompleted, "resetting a live connection would throw inside netcode");
                Assert.IsTrue(server.EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection));
                Assert.IsTrue(IsListening(driver), "the driver must survive until its connection is removed");

                // this placeholder has no transport peer; emulate netcode's later cleanup update.
                server.EntityManager.RemoveComponent<NetworkStreamConnection>(connection);
                server.EntityManager.DestroyEntity(connection);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!cleanup.IsCompleted && timer.Elapsed.TotalSeconds < 5)
                    yield return null;

                Assert.IsTrue(cleanup.IsCompleted, "cleanup did not finish after the connection was removed");
                cleanup.GetAwaiter().GetResult();
                Assert.IsFalse(IsListening(driver));
            }
            finally
            {
                if (server.EntityManager.Exists(connection))
                {
                    server.EntityManager.RemoveComponent<NetworkStreamConnection>(connection);
                    server.EntityManager.DestroyEntity(connection);
                }
            }
        }

        [Test]
        public void LeavingCancelsQueuedListenAndConnectRequests()
        {
            using var server = CreateServer();
            Entity listen = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestListen));
            Entity connect = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));

            Disconnect(server).GetAwaiter().GetResult();

            Assert.IsFalse(server.EntityManager.Exists(listen));
            Assert.IsFalse(server.EntityManager.Exists(connect));
        }

        [Test]
        public void SecondMatchClientContainsOnlyTheNewPlayers()
        {
            using var client = CreateWorld(WorldFlags.GameClient);
            client.GetOrCreateSystemManaged<HullbreachNetCodeClientSystem>();
            FeedMatch(HullbreachNetCodeClient.Replica, 1, 2);
            Assert.AreEqual(2, HullbreachNetCodeClient.Replica.Ships.Count);
            Entity oldGhost = client.EntityManager.CreateEntity(typeof(HullbreachGhostPose));
            Entity prefab = client.EntityManager.CreateEntity(typeof(HullbreachGhostPose), typeof(Prefab));
            Entity oldPayload = client.EntityManager.CreateEntity(typeof(HullbreachPayloadRpc), typeof(ReceiveRpcCommandRequest));

            Disconnect(client).GetAwaiter().GetResult();

            Assert.IsEmpty(HullbreachNetCodeClient.Replica.Ships);
            Assert.IsEmpty(HullbreachNetCodeClient.Projectiles);
            Assert.AreEqual(0, HullbreachNetCodeClient.LocalNetworkId);
            Assert.IsFalse(client.EntityManager.Exists(oldGhost));
            Assert.IsFalse(client.EntityManager.Exists(oldPayload));
            Assert.IsTrue(client.EntityManager.Exists(prefab), "the baked ghost prefab belongs to the scene");
            FeedMatch(HullbreachNetCodeClient.Replica, 3, 4);
            CollectionAssert.AreEquivalent(new ushort[] { 3, 4 }, HullbreachNetCodeClient.Replica.Ships.Keys);
        }

        [Test]
        public void SecondMatchServerStartsWithFreshSimulationAndGhosts()
        {
            using var server = CreateServer();
            var system = server.GetOrCreateSystemManaged<HullbreachNetCodeServerSystem>();
            var simulationField = typeof(HullbreachNetCodeServerSystem).GetField("_simulation",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var first = (ServerSimulation)simulationField.GetValue(system);
            first.Join(1, StarterShip(1));
            first.Join(2, StarterShip(2));
            Assert.AreEqual(2, first.Ships.Count);
            Entity oldGhost = server.EntityManager.CreateEntity(typeof(HullbreachGhostPose));
            Entity prefab = server.EntityManager.CreateEntity(typeof(HullbreachGhostPose), typeof(Prefab));
            Entity oldPayload = server.EntityManager.CreateEntity(typeof(HullbreachPayloadRpc), typeof(SendRpcCommandRequest));

            Disconnect(server).GetAwaiter().GetResult();

            var second = (ServerSimulation)simulationField.GetValue(system);
            Assert.AreNotSame(first, second);
            Assert.IsEmpty(second.Ships);
            Assert.IsEmpty(second.Projectiles);
            Assert.IsFalse(server.EntityManager.Exists(oldGhost));
            Assert.IsFalse(server.EntityManager.Exists(oldPayload));
            Assert.IsTrue(server.EntityManager.Exists(prefab));
            second.Join(3, StarterShip(3));
            second.Join(4, StarterShip(4));
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, second.Ships.Keys);
        }

        static void FeedMatch(ClientReplica replica, int firstPeer, int secondPeer)
        {
            var outbox = new ServerOutbox();
            var simulation = new ServerSimulation(outbox);
            simulation.Join(firstPeer, StarterShip(firstPeer));
            simulation.Join(secondPeer, StarterShip(secondPeer));
            foreach (var entry in outbox.Entries)
                if (entry.Peer == firstPeer)
                    replica.ApplyReceived(entry.Bytes, entry.Bytes.Length);
        }

        static ShipSnapshot StarterShip(int peer) => new ShipSnapshot(0, (ushort)peer,
            new[] { new SnapshotBlock(0, 0, BlockTypes.Core, 0, 0) }, 0, 0, 0, 0, 0, 0);

        static Task Disconnect(EntityWorld world)
        {
            // exercise the service's cleanup without signing in to cloud services in edit mode.
            var method = typeof(HullbreachLobbyService).GetMethod("DisconnectWorldAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            return (Task)method.Invoke(null, new object[] { world });
        }

        static bool IsListening(NetworkStreamDriver stream)
        {
            for (int i = stream.DriverStore.FirstDriver; i < stream.DriverStore.LastDriver; i++)
                if (stream.DriverStore.GetDriverRW(i).Listening) return true;
            return false;
        }

        static EntityWorld CreateServer() => CreateWorld(WorldFlags.GameServer);

        static EntityWorld CreateWorld(WorldFlags flags)
        {
            var world = new EntityWorld("online cleanup test", flags);
            INetworkStreamDriverConstructor previous = NetworkStreamReceiveSystem.DriverConstructor;
            try
            {
                NetworkStreamReceiveSystem.DriverConstructor = new UdpServerConstructor();
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

        sealed class UdpServerConstructor : INetworkStreamDriverConstructor
        {
            public void CreateClientDriver(EntityWorld world, ref NetworkDriverStore store, NetDebug debug)
            {
                using var settings = DefaultDriverBuilder.GetNetworkClientSettings();
                DefaultDriverBuilder.RegisterClientUdpDriver(world, ref store, debug, settings);
            }

            public void CreateServerDriver(EntityWorld world, ref NetworkDriverStore store, NetDebug debug)
            {
                using var settings = DefaultDriverBuilder.GetNetworkServerSettings();
                DefaultDriverBuilder.RegisterServerUdpDriver(world, ref store, debug, settings);
            }
        }
    }
}
#endif
