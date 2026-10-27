using System.IO;
using System.Linq;
using Hullbreach.NetCode.Entities;
using Hullbreach.Game;
using TMPro;
using Unity.NetCode;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Hullbreach.Editor
{
    /// <summary>Creates the editable uGUI lobby prefab and the two online-flow scenes.</summary>
    public static class LobbySceneBuilder
    {
        public const string LobbyPrefabPath = "Assets/Prefabs/UI/LobbyCanvas.prefab";
        public const string SignInPrefabPath = "Assets/Prefabs/UI/SignInCanvas.prefab";
        public const string LobbyScenePath = "Assets/Scenes/LobbyScene.unity";
        public const string GameScenePath = "Assets/Scenes/MultiplayerGame.unity";
        public const string GhostPrefabPath = "Assets/Prefabs/NetCode/HullbreachGameplayGhost.prefab";
        public const string GhostSubScenePath = "Assets/Scenes/MultiplayerGhosts.unity";

        [MenuItem("Hullbreach/Online/Rebuild lobby and multiplayer scenes")]
        public static void RebuildMenu() => Build(force: true);

        // Adds the sign-in screen to an existing lobby scene without resetting the lobby
        // prefab or the multiplayer scenes, so restyled layouts survive.
        // frob:doc docs/platform-link.md#sign-in-screen
        [MenuItem("Hullbreach/Online/Add sign-in screen to lobby scene")]
        public static void AddSignInMenu()
        {
            Directory.CreateDirectory("Assets/Prefabs/UI");
            if (!File.Exists(SignInPrefabPath)) BuildSignInPrefab();
            Scene scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<HullbreachSignInView>() == null)
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SignInPrefabPath));
            EditorSceneManager.SaveScene(scene, LobbyScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("LobbySceneBuilder: lobby scene has the sign-in screen.");
        }

        // update the existing prefab without rebuilding its relay controls or scenes.
        // frob:doc docs/netcode.md#lan-host-and-join-from-the-lobby
        [MenuItem("Hullbreach/Online/Add LAN controls to lobby prefab")]
        public static void AddLanControlsMenu()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                AddLanControls(root);
                PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("LobbySceneBuilder: lobby prefab has the lan controls.");
        }

        static void AddLanControls(GameObject root)
        {
            Transform publicPanel = root.transform.Find("Background/MainPanel/BrowserRoot/PublicLobbies");
            HullbreachLobbyView view = root.GetComponent<HullbreachLobbyView>();
            if (publicPanel == null || view == null)
                throw new System.InvalidOperationException("The lobby prefab is missing its public panel or view.");

            Transform controls = publicPanel.Find("LanControls");
            if (controls == null)
            {
                // the right column has room; adding more rows on the left would overflow.
                GameObject group = AddUiObject(publicPanel, "LanControls");
                controls = group.transform;
                UnityEngine.UI.LayoutElement size = group.AddComponent<UnityEngine.UI.LayoutElement>();
                size.minHeight = 184f;
                size.preferredHeight = 184f;
                UnityEngine.UI.VerticalLayoutGroup layout = group.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                layout.spacing = 8f;
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                AddSectionTitle(controls, "PLAY ON LAN");
            }
            controls.SetSiblingIndex(0);

            UnityEngine.UI.Button host = controls.Find("LanHostButton")?.GetComponent<UnityEngine.UI.Button>()
                ?? AddButton(controls, "LanHostButton", "HOST LAN", 44f);
            TMP_InputField address = controls.Find("LanAddressInput")?.GetComponent<TMP_InputField>()
                ?? AddInput(controls, "LanAddressInput", "Host IP, e.g. 127.0.0.1", false);
            UnityEngine.UI.Button join = controls.Find("LanJoinButton")?.GetComponent<UnityEngine.UI.Button>()
                ?? AddButton(controls, "LanJoinButton", "JOIN ADDRESS", 44f);
            SerializedObject serialized = new SerializedObject(view);
            Set(serialized, "lanHostButton", host);
            Set(serialized, "lanAddressInput", address);
            Set(serialized, "lanJoinButton", join);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Build(bool force)
        {
            MultiplayerRenderingSetup.EnsureRenderPipeline();
            Directory.CreateDirectory("Assets/Prefabs/UI");
            Directory.CreateDirectory("Assets/Prefabs/NetCode");
            Directory.CreateDirectory("Assets/Scenes");

            if (force || !File.Exists(LobbyPrefabPath)) BuildLobbyPrefab();
            if (force || !File.Exists(SignInPrefabPath)) BuildSignInPrefab();
            if (force || !File.Exists(LobbyScenePath)) BuildLobbyScene();
            if (force || !File.Exists(GhostPrefabPath)) BuildGhostPrefab();
            if (force || !File.Exists(GhostSubScenePath)) BuildGhostSubScene();
            if (force || !File.Exists(GameScenePath)) BuildGameScene();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("LobbySceneBuilder: online lobby prefab and scenes are ready.");
        }

        static void BuildLobbyPrefab()
        {
            var canvasRoot = new GameObject("LobbyCanvas", typeof(RectTransform));
            Canvas canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot.AddComponent<GraphicRaycaster>();

            Image background = AddImage(canvasRoot.transform, "Background", new Color(0.025f, 0.04f, 0.075f, 1f));
            Stretch(background.rectTransform);

            GameObject panel = AddPanel(background.transform, "MainPanel", new Vector2(1180f, 760f),
                new Color(0.06f, 0.09f, 0.15f, 0.97f));
            VerticalLayoutGroup mainLayout = panel.AddComponent<VerticalLayoutGroup>();
            mainLayout.padding = new RectOffset(34, 34, 26, 26);
            mainLayout.spacing = 12f;
            mainLayout.childControlHeight = true;
            mainLayout.childControlWidth = true;
            mainLayout.childForceExpandHeight = false;
            mainLayout.childForceExpandWidth = true;

            TMP_Text title = AddText(panel.transform, "Title", "HULLBREACH ONLINE", 34f, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            TMP_Text status = AddText(panel.transform, "StatusText", "Connecting to Unity Services...", 18f,
                TextAlignmentOptions.Center);
            TMP_Text error = AddText(panel.transform, "ErrorText", string.Empty, 17f, TextAlignmentOptions.Center);
            error.color = new Color(1f, 0.42f, 0.4f);
            error.gameObject.SetActive(false);

            GameObject browser = AddUiObject(panel.transform, "BrowserRoot");
            browser.AddComponent<LayoutElement>().flexibleHeight = 1f;
            HorizontalLayoutGroup browserLayout = browser.AddComponent<HorizontalLayoutGroup>();
            browserLayout.spacing = 24f;
            browserLayout.childControlHeight = true;
            browserLayout.childControlWidth = true;
            browserLayout.childForceExpandHeight = true;
            browserLayout.childForceExpandWidth = true;

            GameObject actions = AddPanel(browser.transform, "HostAndJoin", Vector2.zero, new Color(0.09f, 0.13f, 0.2f, 1f));
            VerticalLayoutGroup actionLayout = actions.AddComponent<VerticalLayoutGroup>();
            actionLayout.padding = new RectOffset(22, 22, 20, 20);
            actionLayout.spacing = 8f;
            actionLayout.childControlHeight = true;
            actionLayout.childControlWidth = true;
            actionLayout.childForceExpandHeight = false;
            actionLayout.childForceExpandWidth = true;

            AddSectionTitle(actions.transform, "HOST A LOBBY");
            AddText(actions.transform, "LobbyNameLabel", "Lobby name", 16f);
            TMP_InputField lobbyName = AddInput(actions.transform, "LobbyNameInput", "Hullbreach Lobby", false);
            AddText(actions.transform, "MaxPlayersLabel", "Maximum players", 16f);
            TMP_InputField maxPlayers = AddInput(actions.transform, "MaxPlayersInput", "8", false);
            maxPlayers.contentType = TMP_InputField.ContentType.IntegerNumber;
            Toggle privateToggle = AddToggle(actions.transform, "PrivateToggle", "Private (join code only)");
            AddText(actions.transform, "HostPasswordLabel", "Password (optional, 8-64 characters)", 16f);
            TMP_InputField hostPassword = AddInput(actions.transform, "HostPasswordInput", string.Empty, true);
            Button hostButton = AddButton(actions.transform, "HostButton", "HOST WITH RELAY", 46f);

            AddSpacer(actions.transform, 10f);
            AddSectionTitle(actions.transform, "JOIN BY CODE");
            TMP_InputField joinCode = AddInput(actions.transform, "JoinCodeInput", "Lobby code", false);
            TMP_InputField joinPassword = AddInput(actions.transform, "JoinPasswordInput", "Password if required", true);
            Button joinButton = AddButton(actions.transform, "JoinCodeButton", "JOIN CODE", 44f);

            GameObject publicPanel = AddPanel(browser.transform, "PublicLobbies", Vector2.zero, new Color(0.09f, 0.13f, 0.2f, 1f));
            VerticalLayoutGroup publicLayout = publicPanel.AddComponent<VerticalLayoutGroup>();
            publicLayout.padding = new RectOffset(22, 22, 20, 20);
            publicLayout.spacing = 10f;
            publicLayout.childControlHeight = true;
            publicLayout.childControlWidth = true;
            publicLayout.childForceExpandHeight = false;
            publicLayout.childForceExpandWidth = true;
            AddSectionTitle(publicPanel.transform, "PUBLIC LOBBIES");
            Button refreshButton = AddButton(publicPanel.transform, "RefreshButton", "REFRESH", 40f);
            Transform listContent = AddScrollList(publicPanel.transform, out GameObject rowTemplate);

            GameObject room = AddPanel(panel.transform, "RoomRoot", Vector2.zero, new Color(0.09f, 0.13f, 0.2f, 1f));
            room.AddComponent<LayoutElement>().flexibleHeight = 1f;
            VerticalLayoutGroup roomLayout = room.AddComponent<VerticalLayoutGroup>();
            roomLayout.padding = new RectOffset(40, 40, 30, 30);
            roomLayout.spacing = 16f;
            roomLayout.childControlHeight = true;
            roomLayout.childControlWidth = true;
            roomLayout.childForceExpandHeight = false;
            roomLayout.childForceExpandWidth = true;
            TMP_Text roomTitle = AddText(room.transform, "RoomTitleText", "Lobby", 30f, TextAlignmentOptions.Center);
            TMP_Text roomDetails = AddText(room.transform, "RoomDetailsText", string.Empty, 18f, TextAlignmentOptions.Center);
            AddText(room.transform, "JoinCodeLabel", "INVITE CODE", 16f, TextAlignmentOptions.Center);
            TMP_Text roomCode = AddText(room.transform, "JoinCodeText", "------", 38f, TextAlignmentOptions.Center);
            roomCode.fontStyle = FontStyles.Bold;
            AddText(room.transform, "PlayersLabel", "PLAYERS", 16f, TextAlignmentOptions.Center);
            TMP_Text players = AddText(room.transform, "PlayersText", string.Empty, 20f, TextAlignmentOptions.Center);
            players.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            Button start = AddButton(room.transform, "StartGameButton", "START GAME", 52f);
            Button leave = AddButton(room.transform, "LeaveButton", "LEAVE LOBBY", 44f);
            room.SetActive(false);

            HullbreachLobbyView view = canvasRoot.AddComponent<HullbreachLobbyView>();
            SerializedObject serialized = new SerializedObject(view);
            Set(serialized, "browserRoot", browser);
            Set(serialized, "roomRoot", room);
            Set(serialized, "statusText", status);
            Set(serialized, "errorText", error);
            Set(serialized, "lobbyNameInput", lobbyName);
            Set(serialized, "maxPlayersInput", maxPlayers);
            Set(serialized, "privateToggle", privateToggle);
            Set(serialized, "hostPasswordInput", hostPassword);
            Set(serialized, "hostButton", hostButton);
            Set(serialized, "joinCodeInput", joinCode);
            Set(serialized, "joinPasswordInput", joinPassword);
            Set(serialized, "joinCodeButton", joinButton);
            Set(serialized, "refreshButton", refreshButton);
            Set(serialized, "publicLobbyList", listContent);
            Set(serialized, "publicLobbyRowTemplate", rowTemplate);
            Set(serialized, "roomTitleText", roomTitle);
            Set(serialized, "roomDetailsText", roomDetails);
            Set(serialized, "joinCodeText", roomCode);
            Set(serialized, "playersText", players);
            Set(serialized, "startGameButton", start);
            Set(serialized, "leaveButton", leave);
            serialized.FindProperty("gameplaySceneName").stringValue = HullbreachLobbyService.DefaultGameScene;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AddLanControls(canvasRoot);

            PrefabUtility.SaveAsPrefabAsset(canvasRoot, LobbyPrefabPath);
            Object.DestroyImmediate(canvasRoot);
        }

        // A separate overlay canvas above the lobby: the form blocks the lobby until the
        // player signs in or picks offline, then only a small account bar remains.
        static void BuildSignInPrefab()
        {
            var canvasRoot = new GameObject("SignInCanvas", typeof(RectTransform));
            Canvas canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot.AddComponent<GraphicRaycaster>();

            Image blocker = AddImage(canvasRoot.transform, "SignInRoot", new Color(0.025f, 0.04f, 0.075f, 0.985f));
            Stretch(blocker.rectTransform);
            GameObject panel = AddPanel(blocker.transform, "SignInPanel", new Vector2(620f, 520f),
                new Color(0.06f, 0.09f, 0.15f, 0.97f));
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 32);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            TMP_Text title = AddText(panel.transform, "Title", "HULLBREACH", 34f, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            AddText(panel.transform, "Subtitle", "Sign in with your Hullbreach account", 18f, TextAlignmentOptions.Center);
            AddSpacer(panel.transform, 6f);
            AddText(panel.transform, "UsernameLabel", "Username", 16f);
            TMP_InputField username = AddInput(panel.transform, "UsernameInput", "Username", false);
            AddText(panel.transform, "PasswordLabel", "Password", 16f);
            TMP_InputField password = AddInput(panel.transform, "PasswordInput", "Password", true);
            password.characterLimit = 128;
            TMP_Text message = AddText(panel.transform, "MessageText", string.Empty, 16f, TextAlignmentOptions.Center);
            message.textWrappingMode = TextWrappingModes.Normal;
            message.gameObject.GetComponent<LayoutElement>().preferredHeight = 48f;
            Button signIn = AddButton(panel.transform, "SignInButton", "SIGN IN", 48f);
            Button offline = AddButton(panel.transform, "PlayOfflineButton", "PLAY OFFLINE", 42f);
            offline.GetComponent<Image>().color = new Color(0.16f, 0.22f, 0.32f, 1f);

            GameObject account = AddPanel(canvasRoot.transform, "AccountRoot", new Vector2(520f, 56f),
                new Color(0.04f, 0.065f, 0.11f, 0.92f));
            RectTransform accountRect = account.GetComponent<RectTransform>();
            accountRect.anchorMin = accountRect.anchorMax = accountRect.pivot = new Vector2(1f, 1f);
            accountRect.anchoredPosition = new Vector2(-18f, -18f);
            HorizontalLayoutGroup accountLayout = account.AddComponent<HorizontalLayoutGroup>();
            accountLayout.padding = new RectOffset(16, 8, 8, 8);
            accountLayout.spacing = 10f;
            accountLayout.childControlHeight = true;
            accountLayout.childControlWidth = true;
            accountLayout.childForceExpandHeight = true;
            accountLayout.childForceExpandWidth = false;
            TMP_Text accountText = AddText(account.transform, "AccountText", string.Empty, 16f);
            accountText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Button signOut = AddButton(account.transform, "SignOutButton", "SIGN OUT", 40f);
            signOut.gameObject.GetComponent<LayoutElement>().preferredWidth = 130f;
            account.SetActive(false);

            HullbreachSignInView view = canvasRoot.AddComponent<HullbreachSignInView>();
            SerializedObject serialized = new SerializedObject(view);
            Set(serialized, "formRoot", blocker.gameObject);
            Set(serialized, "usernameInput", username);
            Set(serialized, "passwordInput", password);
            Set(serialized, "signInButton", signIn);
            Set(serialized, "playOfflineButton", offline);
            Set(serialized, "messageText", message);
            Set(serialized, "accountRoot", account);
            Set(serialized, "accountText", accountText);
            Set(serialized, "signOutButton", signOut);
            Set(serialized, "signOutLabel", signOut.GetComponentInChildren<TMP_Text>());
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(canvasRoot, SignInPrefabPath);
            Object.DestroyImmediate(canvasRoot);
        }

        static void BuildLobbyScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = HullbreachLobbyService.DefaultLobbyScene;
            CreateCamera(new Color(0.025f, 0.04f, 0.075f));
            CreateEventSystem();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
            PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SignInPrefabPath));
            EditorSceneManager.SaveScene(scene, LobbyScenePath);
        }

        static void BuildGameScene()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/DemoScene.unity", OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene, GameScenePath);

            foreach (string rootName in new[] { "Demo", "PlayerShip", "TargetShip", "Powerups", "HudCanvas", "EventSystem" })
            {
                GameObject root = GameObject.Find(rootName);
                if (root != null) Object.DestroyImmediate(root);
            }

            Camera camera = Camera.main;
            if (camera == null) camera = CreateCamera(new Color(0.015f, 0.02f, 0.035f));
            CameraFollow oldFollow = camera.GetComponent<CameraFollow>();
            if (oldFollow != null) Object.DestroyImmediate(oldFollow);
            camera.orthographic = true;
            camera.orthographicSize = 16f;

            var presenter = new GameObject("NetworkShipPresenter");
            MultiplayerRenderingSetup.AssignSprites(presenter.AddComponent<HullbreachNetCodeView>());

            var gameplay = new GameObject("NetworkGameplay");
            HullbreachNetworkGameplayController controller = gameplay.AddComponent<HullbreachNetworkGameplayController>();
            SerializedObject controllerSerialized = new SerializedObject(controller);
            Set(controllerSerialized, "gameplayCamera", camera);
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var subSceneObject = new GameObject("MultiplayerGhosts");
            SubScene subScene = subSceneObject.AddComponent<SubScene>();
            subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(GhostSubScenePath);
            subScene.AutoLoadScene = true;

            GameObject canvasGo = new GameObject("NetworkHudCanvas", typeof(RectTransform));
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject statusPanel = AddPanel(canvasGo.transform, "NetworkStatusPanel", new Vector2(900f, 130f),
                new Color(0.04f, 0.065f, 0.11f, 0.92f));
            RectTransform statusRect = statusPanel.GetComponent<RectTransform>();
            statusRect.anchorMin = statusRect.anchorMax = statusRect.pivot = new Vector2(0f, 1f);
            statusRect.anchoredPosition = new Vector2(18f, -18f);
            HorizontalLayoutGroup statusLayout = statusPanel.AddComponent<HorizontalLayoutGroup>();
            statusLayout.padding = new RectOffset(18, 18, 14, 14);
            statusLayout.spacing = 12f;
            statusLayout.childControlHeight = true;
            statusLayout.childControlWidth = true;
            statusLayout.childForceExpandHeight = true;
            statusLayout.childForceExpandWidth = false;
            TMP_Text status = AddText(statusPanel.transform, "StatusText", "Waiting for Netcode connection...", 18f);
            LayoutElement statusElement = status.gameObject.AddComponent<LayoutElement>();
            statusElement.flexibleWidth = 1f;
            Button leave = AddButton(statusPanel.transform, "LeaveButton", "LEAVE", 50f);
            leave.gameObject.GetComponent<LayoutElement>().preferredWidth = 130f;

            HullbreachNetworkGameHud hud = canvasGo.AddComponent<HullbreachNetworkGameHud>();
            SerializedObject serialized = new SerializedObject(hud);
            Set(serialized, "statusText", status);
            Set(serialized, "leaveButton", leave);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        static void BuildGhostPrefab()
        {
            var root = new GameObject("HullbreachGameplayGhost");
            root.AddComponent<HullbreachGameplayGhostAuthoring>();
            GhostAuthoringComponent ghost = root.AddComponent<GhostAuthoringComponent>();
            ghost.SupportedGhostModes = GhostModeMask.Interpolated;
            ghost.DefaultGhostMode = GhostMode.Interpolated;
            ghost.OptimizationMode = GhostOptimizationMode.Dynamic;
            ghost.Importance = 100;
            ghost.MaxSendRate = HullbreachNetCodeConstants.SimulationTickRate;
            ghost.HasOwner = true;
            ghost.SupportAutoCommandTarget = false;
            ghost.TrackInterpolationDelay = true;
            PrefabUtility.SaveAsPrefabAsset(root, GhostPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildGhostSubScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HullbreachGhostPrefabs");
            HullbreachGhostPrefabAuthoring authoring = root.AddComponent<HullbreachGhostPrefabAuthoring>();
            authoring.GhostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            EditorSceneManager.SaveScene(scene, GhostSubScenePath);
        }

        static void UpdateBuildSettings()
        {
            string[] preferred = { LobbyScenePath, GameScenePath, "Assets/Scenes/DemoScene.unity", "Assets/Scenes/RocketScene.unity" };
            EditorBuildSettings.scenes = preferred.Where(File.Exists)
                .Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
        }

        static Camera CreateCamera(Color background)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            return camera;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        static GameObject AddUiObject(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            return go;
        }

        static Image AddImage(Transform parent, string name, Color color)
        {
            GameObject go = AddUiObject(parent, name);
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        static GameObject AddPanel(Transform parent, string name, Vector2 size, Color color)
        {
            Image image = AddImage(parent, name, color);
            image.rectTransform.sizeDelta = size;
            return image.gameObject;
        }

        static TMP_Text AddText(Transform parent, string name, string text, float size,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            GameObject go = AddUiObject(parent, name);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.9f, 0.95f, 1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.Max(26f, size + 8f);
            return label;
        }

        static void AddSectionTitle(Transform parent, string text)
        {
            TMP_Text label = AddText(parent, text.Replace(" ", string.Empty) + "Title", text, 22f);
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.32f, 0.82f, 1f);
        }

        static TMP_InputField AddInput(Transform parent, string name, string placeholderText, bool password)
        {
            GameObject root = AddUiObject(parent, name);
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.025f, 0.045f, 0.08f, 1f);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 42f;

            GameObject viewport = AddUiObject(root.transform, "Text Area");
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, 12f, 10f);
            viewport.AddComponent<RectMask2D>();

            TMP_Text placeholder = AddText(viewport.transform, "Placeholder", placeholderText, 17f);
            placeholder.color = new Color(0.55f, 0.63f, 0.72f);
            Stretch(placeholder.rectTransform);
            TMP_Text text = AddText(viewport.transform, "Text", string.Empty, 17f);
            Stretch(text.rectTransform);

            TMP_InputField input = root.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 64;
            return input;
        }

        static Toggle AddToggle(Transform parent, string name, string text)
        {
            GameObject root = AddUiObject(parent, name);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 34f;
            Toggle toggle = root.AddComponent<Toggle>();
            Image background = AddImage(root.transform, "Background", new Color(0.025f, 0.045f, 0.08f, 1f));
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(26f, 26f);
            backgroundRect.anchoredPosition = new Vector2(13f, 0f);
            Image check = AddImage(background.transform, "Checkmark", new Color(0.25f, 0.82f, 1f, 1f));
            Stretch(check.rectTransform, 5f, 5f);
            TMP_Text label = AddText(root.transform, "Label", text, 16f);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(38f, 0f);
            label.rectTransform.offsetMax = Vector2.zero;
            toggle.targetGraphic = background;
            toggle.graphic = check;
            toggle.isOn = false;
            return toggle;
        }

        static Button AddButton(Transform parent, string name, string text, float height)
        {
            GameObject root = AddUiObject(parent, name);
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.1f, 0.52f, 0.75f, 1f);
            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.18f, 0.68f, 0.92f, 1f);
            colors.pressedColor = new Color(0.06f, 0.37f, 0.58f, 1f);
            button.colors = colors;
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            TMP_Text label = AddText(root.transform, "Label", text, 17f, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform);
            return button;
        }

        static Transform AddScrollList(Transform parent, out GameObject rowTemplate)
        {
            GameObject scrollGo = AddPanel(parent, "LobbyScroll", Vector2.zero, new Color(0.025f, 0.045f, 0.08f, 1f));
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;

            GameObject viewport = AddUiObject(scrollGo.transform, "Viewport");
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            Stretch(viewport.GetComponent<RectTransform>(), 8f, 8f);

            GameObject content = AddUiObject(viewport.transform, "Content");
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;

            rowTemplate = AddPanel(content.transform, "LobbyRowTemplate", Vector2.zero, new Color(0.08f, 0.13f, 0.2f, 1f));
            rowTemplate.AddComponent<LayoutElement>().preferredHeight = 76f;
            HorizontalLayoutGroup rowLayout = rowTemplate.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(14, 10, 8, 8);
            rowLayout.spacing = 8f;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childForceExpandWidth = false;
            TMP_Text name = AddText(rowTemplate.transform, "NameText", "Lobby Name", 18f);
            name.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            TMP_Text details = AddText(rowTemplate.transform, "DetailsText", "1/8 • Open", 15f);
            details.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;
            Button join = AddButton(rowTemplate.transform, "JoinButton", "JOIN", 44f);
            join.gameObject.GetComponent<LayoutElement>().preferredWidth = 90f;
            rowTemplate.SetActive(false);
            return content.transform;
        }

        static void AddSpacer(Transform parent, float height)
        {
            GameObject spacer = AddUiObject(parent, "Spacer");
            spacer.AddComponent<LayoutElement>().preferredHeight = height;
        }

        static void Stretch(RectTransform rect, float horizontal = 0f, float vertical = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }

        static void Set(SerializedObject serialized, string property, Object value)
        {
            serialized.FindProperty(property).objectReferenceValue = value;
        }
    }
}
