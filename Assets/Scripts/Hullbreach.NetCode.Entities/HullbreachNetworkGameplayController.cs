using System.Collections.Generic;
using Hullbreach.Builder;
using Hullbreach.Core;
using Hullbreach.Ship;
using Unity.Mathematics;
using UnityEngine;

namespace Hullbreach.NetCode.Entities
{
    /// <summary>
    /// Local input and build-mode presentation. Simulation and validation stay
    /// server-authoritative; this component only previews and queues requests.
    /// </summary>
    [AddComponentMenu("Hullbreach/Online/Network Gameplay Controller")]
    public sealed class HullbreachNetworkGameplayController : MonoBehaviour
    {
        internal readonly struct BuildRequest
        {
            public readonly sbyte X;
            public readonly sbyte Y;
            public readonly byte TypeId;
            public readonly byte Modifiers;
            public readonly HullbreachBuildAction Action;

            public BuildRequest(int x, int y, byte typeId, byte modifiers, HullbreachBuildAction action)
            {
                X = (sbyte)x;
                Y = (sbyte)y;
                TypeId = typeId;
                Modifiers = modifiers;
                Action = action;
            }
        }

        static readonly Queue<BuildRequest> Requests = new Queue<BuildRequest>();

        [SerializeField] Camera gameplayCamera;
        [SerializeField] float cameraSmoothing = 6f;

        GameObject _hover;
        Sprite _hoverSprite;
        int _hoverKey;
        bool _hasHover;
        bool _hoverValid;
        int? _pendingKey;
        string _buildStatus = "Select a block with 1-7";

        public static bool Active { get; private set; }
        public static bool IsBuildMode { get; private set; } = true;
        public static byte SelectedTypeId { get; private set; } = BlockTypes.Hull;
        public static string BuildStatus { get; private set; } = "Select a block with 1-7";

        void OnEnable()
        {
            Active = true;
            IsBuildMode = true;
            Requests.Clear();
            if (gameplayCamera == null) gameplayCamera = Camera.main;
            PublishStatus();
        }

        void OnDisable()
        {
            Active = false;
            Requests.Clear();
            if (_hover != null) Destroy(_hover);
            if (_hoverSprite != null) Destroy(_hoverSprite);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                IsBuildMode = !IsBuildMode;
                _pendingKey = null;
                _buildStatus = IsBuildMode ? "Build mode" : "Flight mode";
                PublishStatus();
            }

            if (!IsBuildMode)
            {
                SetHoverVisible(false);
                return;
            }

            for (int i = 0; i < BlockTypes.Count && i < 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                SelectedTypeId = (byte)i;
                _pendingKey = null;
                _buildStatus = $"Selected {BlockTypes.Get(SelectedTypeId).Name}";
                PublishStatus();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _pendingKey = null;
                _buildStatus = "Placement cancelled";
                PublishStatus();
            }

            if (!TryGetLocalShip(out ShipBody ship) || !TryGetHoveredKey(ship, out _hoverKey))
            {
                _hasHover = false;
                SetHoverVisible(false);
                return;
            }

            _hasHover = true;
            byte modifiers = _pendingKey.HasValue ? FacingTowards(_pendingKey.Value, _hoverKey) : (byte)0;
            int placementKey = _pendingKey ?? _hoverKey;
            _hoverValid = _pendingKey.HasValue
                ? PlacementRules.CanPlace(ship.Grid, placementKey, SelectedTypeId, modifiers, out _)
                : BlockPalette.IsSymmetric(SelectedTypeId)
                    ? PlacementRules.CanPlace(ship.Grid, placementKey, SelectedTypeId, 0, out _)
                    : CanPlaceAnyFacing(ship, placementKey);
            UpdateHover(ship, placementKey);

            if (Input.GetMouseButtonDown(1))
            {
                if (PlacementRules.CanRemove(ship.Grid, _hoverKey))
                {
                    BlockKey.Unpack(_hoverKey, out int x, out int y);
                    Requests.Enqueue(new BuildRequest(x, y, 0, 0, HullbreachBuildAction.Remove));
                    _buildStatus = $"Removing ({x}, {y})";
                }
                else _buildStatus = "That block cannot be removed";
                _pendingKey = null;
                PublishStatus();
            }

            if (!Input.GetMouseButtonDown(0)) return;

            if (!BlockPalette.IsSymmetric(SelectedTypeId) && !_pendingKey.HasValue)
            {
                if (!_hoverValid) return;
                _pendingKey = _hoverKey;
                _buildStatus = "Choose a facing with the second click";
                PublishStatus();
                return;
            }

            int key = _pendingKey ?? _hoverKey;
            modifiers = _pendingKey.HasValue ? FacingTowards(key, _hoverKey) : (byte)0;
            if (!PlacementRules.CanPlace(ship.Grid, key, SelectedTypeId, modifiers, out PlacementVerdict why))
            {
                _buildStatus = BuilderSession.DescribeVerdict(why);
                PublishStatus();
                return;
            }

            BlockKey.Unpack(key, out int px, out int py);
            Requests.Enqueue(new BuildRequest(px, py, SelectedTypeId, modifiers, HullbreachBuildAction.Place));
            _pendingKey = null;
            _buildStatus = $"Placing {BlockTypes.Get(SelectedTypeId).Name}";
            PublishStatus();
        }

        void LateUpdate()
        {
            if (gameplayCamera == null || !TryGetLocalShip(out ShipBody ship)) return;
            Vector3 desired = new Vector3(ship.Position.x, ship.Position.y, gameplayCamera.transform.position.z);
            float t = 1f - Mathf.Exp(-cameraSmoothing * Time.deltaTime);
            gameplayCamera.transform.position = Vector3.Lerp(gameplayCamera.transform.position, desired, t);
        }

        bool TryGetHoveredKey(ShipBody ship, out int key)
        {
            key = 0;
            if (gameplayCamera == null) return false;
            Vector3 world = gameplayCamera.ScreenToWorldPoint(Input.mousePosition);
            float2 local = ship.WorldToLocal(new float2(world.x, world.y));
            int x = Mathf.FloorToInt(local.x);
            int y = Mathf.FloorToInt(local.y);
            if (!BlockKey.InRange(x, y)) return false;
            key = BlockKey.Pack(x, y);
            return true;
        }

        void UpdateHover(ShipBody ship, int key)
        {
            if (_hover == null)
            {
                _hover = new GameObject("NetworkBuildPreview");
                _hoverSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                    new Vector2(0.5f, 0.5f), 1f);
                _hover.AddComponent<SpriteRenderer>().sprite = _hoverSprite;
            }
            _hover.SetActive(_hasHover);
            float2 world = ship.LocalToWorld(BlockGrid.CenterOf(key));
            _hover.transform.SetPositionAndRotation(new Vector3(world.x, world.y, -0.2f),
                Quaternion.Euler(0f, 0f, ship.Rotation * Mathf.Rad2Deg));
            Color color = _hoverValid ? Color.green : Color.red;
            color.a = 0.45f;
            _hover.GetComponent<SpriteRenderer>().color = color;
        }

        void SetHoverVisible(bool visible)
        {
            if (_hover != null) _hover.SetActive(visible);
        }

        static bool TryGetLocalShip(out ShipBody ship)
        {
            ship = null;
            var replica = HullbreachNetCodeClient.Replica;
            return replica != null && replica.Ships.TryGetValue(HullbreachNetCodeClient.LocalNetworkId, out ship);
        }

        static bool CanPlaceAnyFacing(ShipBody ship, int key)
        {
            for (byte facing = 0; facing < 4; facing++)
                if (PlacementRules.CanPlace(ship.Grid, key, SelectedTypeId, facing, out _)) return true;
            return false;
        }

        static byte FacingTowards(int from, int to)
        {
            BlockKey.Unpack(from, out int fx, out int fy);
            BlockKey.Unpack(to, out int tx, out int ty);
            int dx = tx - fx;
            int dy = ty - fy;
            if (dx == 0 && dy == 0) return 0;
            if (System.Math.Abs(dx) >= System.Math.Abs(dy)) return dx >= 0 ? (byte)1 : (byte)3;
            return dy >= 0 ? (byte)0 : (byte)2;
        }

        void PublishStatus()
        {
            BuildStatus = _buildStatus;
        }

        internal static bool TryDequeueBuild(out BuildRequest request)
        {
            if (Requests.Count == 0)
            {
                request = default;
                return false;
            }
            request = Requests.Dequeue();
            return true;
        }
    }
}
