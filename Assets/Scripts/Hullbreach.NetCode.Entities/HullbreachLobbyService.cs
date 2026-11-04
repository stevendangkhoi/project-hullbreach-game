using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hullbreach.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hullbreach.NetCode.Entities
{
    /// <summary>
    /// Owns Unity Authentication, Lobby, and Relay-backed Multiplayer sessions.
    /// Creating or joining a session lets the Multiplayer package configure the
    /// existing Netcode for Entities client/server worlds automatically.
    /// </summary>
    public sealed class HullbreachLobbyService : MonoBehaviour
    {
        static HullbreachLobbyService s_Instance;

        Task _initializationTask;
        Task _networkCleanupTask = Task.CompletedTask;
        ISession _session;
        IReadOnlyList<ISessionInfo> _publicSessions = Array.Empty<ISessionInfo>();
        bool _loadingGameScene;
        bool _leavingSession;
        bool _operationBusy;
        string _loggedStatus;

        public const string DefaultLobbyScene = "LobbyScene";
        public const string DefaultGameScene = "MultiplayerGame";
        const string GameSceneProperty = "hullbreach.gameScene";

        public static HullbreachLobbyService Instance
        {
            get
            {
                if (s_Instance == null)
                    CreateRuntimeObject();
                return s_Instance;
            }
        }

        public event Action Changed;

        public bool IsReady { get; private set; }
        public bool IsBusy => _operationBusy || !_networkCleanupTask.IsCompleted;
        public string Status { get; private set; } = "Starting Unity Services...";
        public string LastError { get; private set; }
        public ISession Session => _session;
        public IReadOnlyList<ISessionInfo> PublicSessions => _publicSessions;

        static void CreateRuntimeObject()
        {
            var gameObject = new GameObject("Hullbreach Online Lobby");
            DontDestroyOnLoad(gameObject);
            s_Instance = gameObject.AddComponent<HullbreachLobbyService>();
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            bool smoke = OnlineSmoke.Requested(Environment.GetEnvironmentVariable(OnlineSmoke.EnvVar));
            try
            {
                await InitializeAsync();
                await RefreshPublicSessionsAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Hullbreach online services could not start: {exception.Message}");
                if (smoke) Debug.LogWarning($"{OnlineSmoke.LogPrefix} smoke test FAILED: Unity Services did not start: {exception.Message}");
                return;
            }

            if (smoke) await RunSmokeAsync();
        }

        // Demo/testing aid (`play online --headless`): create a private Relay lobby,
        // log its code, leave it again. No clicks, so it runs on a headless player.
        async Task RunSmokeAsync()
        {
            try
            {
                await HostAsync(OnlineSmoke.LobbyName(DateTime.UtcNow), 2, isPrivate: true);
                Debug.Log($"{OnlineSmoke.LogPrefix} smoke lobby created, join code {_session?.Code}");
                await LeaveAsync();
                Debug.Log($"{OnlineSmoke.LogPrefix} smoke test PASSED");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{OnlineSmoke.LogPrefix} smoke test FAILED: {exception.Message}");
            }
        }

        public Task InitializeAsync()
        {
            return _initializationTask ??= InitializeServicesAsync();
        }

        async Task InitializeServicesAsync()
        {
            SetBusy(true, "Connecting to Unity Services...");
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    // A profile makes a second copy on this computer a different player.
                    string profile = OnlineSmoke.Profile(Environment.GetCommandLineArgs());
                    var options = new InitializationOptions();
                    if (profile != null)
                    {
                        options.SetProfile(profile);
                        Debug.Log($"{OnlineSmoke.LogPrefix} Unity Services profile '{profile}'");
                    }
                    await UnityServices.InitializeAsync(options);
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                IsReady = true;
                LastError = null;
                Status = "Online";
            }
            catch (Exception exception)
            {
                IsReady = false;
                SetError(exception);
                _initializationTask = null;
                throw;
            }
            finally
            {
                _operationBusy = false;
                NotifyChanged();
            }
        }

        public async Task RefreshPublicSessionsAsync()
        {
            await InitializeAsync();
            await RunOperationAsync("Refreshing public lobbies...", async () =>
            {
                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(
                    new QuerySessionsOptions { Count = 50 });

                _publicSessions = results.Sessions
                    .Where(session => !session.IsLocked && session.AvailableSlots > 0)
                    .OrderByDescending(session => session.LastUpdated)
                    .ToArray();
                Status = $"Found {_publicSessions.Count} public lobby" + (_publicSessions.Count == 1 ? string.Empty : "ies");
            });
        }

        public async Task HostAsync(string lobbyName, int maxPlayers, bool isPrivate, string password = null)
        {
            ValidatePassword(password);
            string cleanName = string.IsNullOrWhiteSpace(lobbyName) ? "Hullbreach Lobby" : lobbyName.Trim();
            int cleanMaxPlayers = Mathf.Clamp(maxPlayers, 2, 100);

            await InitializeAsync();
            await RunOperationAsync("Creating lobby and Relay allocation...", async () =>
            {
                await LeaveCurrentSessionInternalAsync();

                var options = new SessionOptions
                {
                    Name = cleanName,
                    MaxPlayers = cleanMaxPlayers,
                    IsPrivate = isPrivate,
                    Password = NormalizePassword(password),
                }.WithRelayNetwork();

                AttachSession(await MultiplayerService.Instance.CreateSessionAsync(options));
                Status = $"Hosting {cleanName}";
            });
        }

        public async Task JoinByCodeAsync(string joinCode, string password = null)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
                throw new ArgumentException("Enter a lobby code.", nameof(joinCode));
            ValidatePassword(password);

            await InitializeAsync();
            await RunOperationAsync("Joining lobby by code...", async () =>
            {
                await LeaveCurrentSessionInternalAsync();
                var options = new JoinSessionOptions { Password = NormalizePassword(password) };
                AttachSession(await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim(), options));
                Status = $"Joined {_session.Name}";
            });
        }

        public async Task JoinPublicSessionAsync(string sessionId, string password = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("A session ID is required.", nameof(sessionId));
            ValidatePassword(password);

            await InitializeAsync();
            await RunOperationAsync("Joining public lobby...", async () =>
            {
                await LeaveCurrentSessionInternalAsync();
                var options = new JoinSessionOptions { Password = NormalizePassword(password) };
                AttachSession(await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, options));
                Status = $"Joined {_session.Name}";
            });
        }

        public async Task LeaveAsync()
        {
            await RunOperationAsync("Leaving lobby...", async () =>
            {
                await LeaveCurrentSessionInternalAsync();
                Status = "Online";
            });
        }

        public async Task StartGameAsync(string sceneName = DefaultGameScene)
        {
            if (_session == null || !_session.IsHost)
                throw new InvalidOperationException("Only the lobby host can start the game.");
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("A gameplay scene is required.", nameof(sceneName));

            await RunOperationAsync("Starting game...", async () =>
            {
                IHostSession host = _session.AsHost();
                host.IsLocked = true;
                host.SetProperty(GameSceneProperty,
                    new SessionProperty(sceneName.Trim(), VisibilityPropertyOptions.Member));
                await host.SavePropertiesAsync();
                Status = "Loading game...";
                LoadGameSceneIfReady();
            });
        }

        public async Task LeaveAndReturnToLobbyAsync(string lobbySceneName = DefaultLobbyScene)
        {
            await LeaveAsync();
            if (!string.IsNullOrWhiteSpace(lobbySceneName))
                await SceneManager.LoadSceneAsync(lobbySceneName.Trim(), LoadSceneMode.Single);
        }

        async Task LeaveCurrentSessionInternalAsync()
        {
            await _networkCleanupTask;
            if (_session == null)
                return;

            ISession leavingSession = _session;
            _leavingSession = true;
            try
            {
                // the allocation belongs to its host, so end the room when the host leaves.
                if (leavingSession.IsHost)
                    await leavingSession.AsHost().DeleteAsync();
                else
                    await leavingSession.LeaveAsync();

                if (_session == leavingSession)
                    DetachSession();
            }
            finally
            {
                try
                {
                    _networkCleanupTask = DisconnectNetworkAsync();
                    await _networkCleanupTask;
                }
                finally
                {
                    _leavingSession = false;
                }
            }
        }

        void AttachSession(ISession session)
        {
            DetachSession();
            _session = session;
            if (_session == null)
                return;

            _session.Changed += OnSessionChanged;
            _session.Deleted += OnSessionDeleted;
            _session.RemovedFromSession += OnSessionRemoved;
            NotifyChanged();
            // the host may have started while this player's join request was finishing.
            LoadGameSceneIfReady();
        }

        void DetachSession()
        {
            if (_session != null)
            {
                _session.Changed -= OnSessionChanged;
                _session.Deleted -= OnSessionDeleted;
                _session.RemovedFromSession -= OnSessionRemoved;
            }
            _session = null;
            _loadingGameScene = false;
            NotifyChanged();
        }

        void OnSessionChanged()
        {
            NotifyChanged();
            LoadGameSceneIfReady();
        }

        async void LoadGameSceneIfReady()
        {
            if (_loadingGameScene || _leavingSession || _session == null ||
                !_session.Properties.TryGetValue(GameSceneProperty, out SessionProperty property) ||
                string.IsNullOrWhiteSpace(property?.Value) ||
                SceneManager.GetActiveScene().name == property.Value)
                return;

            _loadingGameScene = true;
            try
            {
                await SceneManager.LoadSceneAsync(property.Value, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                _loadingGameScene = false;
                SetError(exception);
                Debug.LogException(exception);
            }
        }

        void OnSessionDeleted() => OnSessionEnded(true);
        void OnSessionRemoved() => OnSessionEnded(false);

        async void OnSessionEnded(bool leaveDeletedSession)
        {
            if (_leavingSession) return;
            ISession endedSession = _session;
            DetachSession();
            _networkCleanupTask = CleanupEndedSessionAsync(endedSession, leaveDeletedSession);
            Status = "Session ended. Disconnecting...";
            NotifyChanged();
            try
            {
                await _networkCleanupTask;
                Status = "Session ended";
            }
            catch (Exception exception)
            {
                SetError(exception);
                Debug.LogWarning($"Hullbreach online cleanup failed: {exception.Message}");
            }
            finally
            {
                NotifyChanged();
            }
        }

        static async Task CleanupEndedSessionAsync(ISession session, bool leaveDeletedSession)
        {
            try
            {
                // deleted rooms leave sdk callbacks attached; kicked players already get sdk cleanup.
                if (leaveDeletedSession && session != null)
                    await session.LeaveAsync();
            }
            catch (SessionException exception) when (exception.Error == SessionError.SessionNotFound ||
                exception.Error == SessionError.SessionDeleted || exception.Error == SessionError.NotInLobby)
            {
                // the host already removed the room, so there may be no membership left to delete.
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Hullbreach online: SDK session cleanup failed: {exception.Message}");
            }
            finally
            {
                await DisconnectNetworkAsync();
            }
        }

        static async Task DisconnectNetworkAsync()
        {
            await Task.WhenAll(
                DisconnectWorldAsync(ClientServerBootstrap.ClientWorld),
                DisconnectWorldAsync(ClientServerBootstrap.ServerWorld));
            NetworkStreamReceiveSystem.DriverConstructor = new IPCAndSocketDriverConstructor();
        }

        // the sdk queues one client disconnect but does not stop the host's server driver.
        // wait for netcode to remove connection cleanup components before replacing drivers.
        static async Task DisconnectWorldAsync(Unity.Entities.World world)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (world != null && world.IsCreated)
            {
                if (RequestDisconnects(world))
                {
                    ResetIdleDriver(world);
                    world.GetExistingSystemManaged<HullbreachNetCodeClientSystem>()?.ResetMatch();
                    world.GetExistingSystemManaged<HullbreachNetCodeServerSystem>()?.ResetMatch();
                    return;
                }
                if (timer.Elapsed.TotalSeconds >= 10)
                    throw new InvalidOperationException("The old connection did not close. Restart the game before joining another lobby.");
                await Task.Delay(25);
            }
        }

        static bool RequestDisconnects(Unity.Entities.World world)
        {
            var manager = world.EntityManager;
            manager.CompleteAllTrackedJobs();
            using var connects = manager.CreateEntityQuery(typeof(NetworkStreamRequestConnect));
            using var listens = manager.CreateEntityQuery(typeof(NetworkStreamRequestListen));
            manager.DestroyEntity(connects);
            manager.DestroyEntity(listens);
            using var connections = manager.CreateEntityQuery(typeof(NetworkStreamConnection));
            using var entities = connections.ToEntityArray(Allocator.Temp);
            foreach (Entity connection in entities)
                if (!manager.HasComponent<NetworkStreamRequestDisconnect>(connection))
                    manager.AddComponent<NetworkStreamRequestDisconnect>(connection);
            return entities.Length == 0;
        }

        static void ResetIdleDriver(Unity.Entities.World world)
        {
            using var drivers = world.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
            if (drivers.IsEmptyIgnoreFilter) return;
            using var debug = world.EntityManager.CreateEntityQuery(typeof(NetDebug));
            var constructor = new IPCAndSocketDriverConstructor();
            var store = new NetworkDriverStore();
            if (world.IsServer())
                constructor.CreateServerDriver(world, ref store, debug.GetSingleton<NetDebug>());
            else
                constructor.CreateClientDriver(world, ref store, debug.GetSingleton<NetDebug>());
            ref var driver = ref drivers.GetSingletonRW<NetworkStreamDriver>().ValueRW;
            driver.ResetDriverStore(world.Unmanaged, ref store);
        }

        async Task RunOperationAsync(string busyStatus, Func<Task> operation)
        {
            if (IsBusy)
                throw new InvalidOperationException("Another online operation is already in progress.");
            await _networkCleanupTask;

            SetBusy(true, busyStatus);
            try
            {
                await operation();
                LastError = null;
            }
            catch (Exception exception)
            {
                SetError(exception);
                throw;
            }
            finally
            {
                _operationBusy = false;
                NotifyChanged();
            }
        }

        void SetBusy(bool busy, string status)
        {
            _operationBusy = busy;
            Status = status;
            LastError = null;
            NotifyChanged();
        }

        void SetError(Exception exception)
        {
            LastError = exception.Message;
            Status = "Online service error";
            NotifyChanged();
        }

        static void ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return;
            if (password.Length < 8 || password.Length > 64)
                throw new ArgumentException("Lobby passwords must be 8 to 64 characters.", nameof(password));
        }

        static string NormalizePassword(string password)
        {
            return string.IsNullOrEmpty(password) ? null : password;
        }

        void NotifyChanged()
        {
            // One log line per status change, so Player.log shows how far the lobby got.
            if (Status != _loggedStatus)
            {
                _loggedStatus = Status;
                Debug.Log(OnlineSmoke.StatusLine(Status, LastError));
            }
            Changed?.Invoke();
        }
    }
}
