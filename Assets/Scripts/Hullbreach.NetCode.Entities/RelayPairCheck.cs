#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hullbreach.NetCode.Entities
{
    // opt-in development check: two processes share a temporary folder, never credentials.
    // frob:doc docs/steven-testing.md#repeatable-relay-pair-check
    public sealed class RelayPairCheck : MonoBehaviour
    {
        string _folder;
        bool _host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            string role = Environment.GetEnvironmentVariable("HULLBREACH_RELAY_CHECK_ROLE");
            string folder = Environment.GetEnvironmentVariable("HULLBREACH_RELAY_CHECK_FOLDER");
            if ((role != "host" && role != "client") || string.IsNullOrWhiteSpace(folder)) return;
            var check = new GameObject("Relay pair check").AddComponent<RelayPairCheck>();
            check._folder = folder;
            check._host = role == "host";
            DontDestroyOnLoad(check.gameObject);
        }

        async void Start()
        {
            string role = _host ? "host" : "client";
            HullbreachLobbyService lobby = HullbreachLobbyService.Instance;
            try
            {
                Directory.CreateDirectory(_folder);
                await WaitFor(() => lobby.IsReady && !lobby.IsBusy, "online initialization");
                for (int round = 1; round <= 2; round++)
                {
                    // leave time between service requests when the test changes rooms.
                    if (round > 1) await Task.Delay(2500);
                    string codeFile = Path.Combine(_folder, $"code-{round}.txt");
                    string clientReady = Path.Combine(_folder, $"client-{round}.txt");
                    string hostDone = Path.Combine(_folder, $"host-left-{round}.txt");
                    string clientDone = Path.Combine(_folder, $"client-left-{round}.txt");
                    if (_host)
                    {
                        await lobby.HostAsync("Hullbreach Relay check", 2, true);
                        File.WriteAllText(codeFile, lobby.Session.Code);
                        await WaitFor(() => lobby.Session != null && lobby.Session.Players.Count == 2, "second player");
                        await lobby.StartGameAsync();
                    }
                    else
                    {
                        await WaitFor(() => File.Exists(codeFile) && new FileInfo(codeFile).Length > 0, "invite code");
                        await lobby.JoinByCodeAsync(File.ReadAllText(codeFile).Trim());
                    }

                    await WaitFor(() => SceneManager.GetActiveScene().name == HullbreachLobbyService.DefaultGameScene
                        && HullbreachNetCodeClient.IsConnected
                        && HullbreachNetCodeClient.Replica.Ships.Count == 2, "two replicated ships");
                    using (var connectionQuery = ClientServerBootstrap.ClientWorld.EntityManager
                        .CreateEntityQuery(typeof(NetworkStreamConnection), typeof(NetworkId)))
                    using (var driverQuery = ClientServerBootstrap.ClientWorld.EntityManager
                        .CreateEntityQuery(typeof(NetworkStreamDriver)))
                    {
                        bool relay = driverQuery.GetSingleton<NetworkStreamDriver>()
                            .UseRelay(connectionQuery.GetSingleton<NetworkStreamConnection>());
                        if (!_host && !relay) throw new InvalidOperationException("The joining player did not use Relay.");
                        Debug.Log($"Hullbreach Relay check: {role} round {round}, id={HullbreachNetCodeClient.LocalNetworkId}, ships=2, relay={relay}");
                    }

                    if (_host)
                    {
                        await WaitFor(() => File.Exists(clientReady), "client match confirmation");
                        await lobby.LeaveAndReturnToLobbyAsync();
                        File.WriteAllText(hostDone, "left");
                        await WaitFor(() => File.Exists(clientDone), "client cleanup");
                    }
                    else
                    {
                        File.WriteAllText(clientReady, "ready");
                        await WaitFor(() => File.Exists(hostDone) && lobby.Session == null
                            && !lobby.IsBusy && !HullbreachNetCodeClient.IsConnected, "host disconnect");
                        await lobby.LeaveAndReturnToLobbyAsync();
                        File.WriteAllText(clientDone, "left");
                    }
                    if (HullbreachNetCodeClient.Replica.Ships.Count != 0)
                        throw new InvalidOperationException("Ships remained after leaving the match.");
                }
                File.WriteAllText(Path.Combine(_folder, role + "-passed.txt"), "Two Relay matches and cleanup passed.");
                Debug.Log($"Hullbreach Relay check: {role} PASSED");
                Application.Quit(0);
            }
            catch (Exception error)
            {
                Debug.LogError($"Hullbreach Relay check: {role} FAILED: {error.Message}");
                try { File.WriteAllText(Path.Combine(_folder, role + "-failed.txt"), error.Message); }
                catch (Exception markerError) { Debug.LogWarning("Could not write the failure marker: " + markerError.Message); }
                try { await lobby.LeaveAsync(); }
                catch (Exception) { /* the test still reports the original failure. */ }
                finally { Application.Quit(1); }
            }
        }

        static async Task WaitFor(Func<bool> ready, string step)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!ready())
            {
                if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException("Timed out waiting for " + step + ".");
                await Task.Delay(100);
            }
        }
    }
}
#endif
