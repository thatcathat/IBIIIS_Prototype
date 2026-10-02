using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IBIIIS
{
    [ExecuteAlways]
    public sealed class GridMapPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. 맵 에디터에서 작성한 맵")] private GridMap map;
        [SerializeField, Min(.1f), Tooltip("한 칸의 월드 크기")] private float cellSize = 1;
        [SerializeField, Tooltip("공용 플레이어 외형·이동 비용 설정. 새 테스트 씬은 기본 설정을 자동 연결합니다.")] private PlayerSettings playerSettings;
        [SerializeField, HideInInspector] private int moveTurnCost = 1;
        [SerializeField, HideInInspector] private int blockedTurnCost;
        [SerializeField, HideInInspector] private GameObject playerVisualPrefab;
        [SerializeField, Tooltip("선택. 기본 블록에 쓸 머티리얼")] private Material fallbackMaterial;
        [SerializeField, Tooltip("맵 전체를 비추는 테스트 카메라")] private Camera viewCamera;
        [SerializeField, Tooltip("선택. 자동 맞춤 시 적용하는 공용 Projection·각도·화각 설정")] private MapCameraSettings cameraSettings;
        [SerializeField, Tooltip("Scene과 Play에서 맵 전체가 보이도록 카메라 위치·크기를 자동 계산합니다. 끄면 직접 편집한 카메라 구도를 유지합니다.")] private bool autoFitCamera = true;
        private readonly List<Material> materials = new List<Material>();
        private GridSession session;
        private Transform generated;
        private Transform player;
        [SerializeField, HideInInspector] private GameObject environmentInstance;
        [SerializeField, HideInInspector] private GameObject environmentSource;
        public GameObject EnvironmentInstance => environmentInstance;
        public GameObject EnvironmentSource => environmentSource;
        public void SetEnvironmentInstance(GameObject instance, GameObject source)
        { environmentInstance = instance; environmentSource = source; }
        public void EnsureRuntimeEnvironment()
        {
            var desired = map != null ? map.EnvironmentPrefab : null;
            if (environmentInstance != null && environmentSource == desired) return;
            if (environmentInstance != null) { environmentInstance.SetActive(false); Release(environmentInstance); }
            environmentInstance = null; environmentSource = desired;
            if (desired != null)
            {
                environmentInstance = Instantiate(desired, transform, false);
                environmentInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                environmentInstance.transform.localScale = Vector3.one;
            }
        }
        public GridSession Session => session;
        public GridMap Map => map;
        public Camera ViewCamera => viewCamera;
        public Transform Generated => generated;
        public bool AutoFitCamera => autoFitCamera;
        public PlayerSettings SharedPlayerSettings => playerSettings;
        public int MoveTurnCost => playerSettings != null ? playerSettings.MoveTurnCost : moveTurnCost;
        public int BlockedTurnCost => playerSettings != null ? playerSettings.BlockedTurnCost : blockedTurnCost;
        public GameObject PlayerVisualPrefab => playerSettings != null ? playerSettings.VisualPrefab : playerVisualPrefab;
        public Material FallbackMaterial => fallbackMaterial;
        public MapCameraSettings CameraSettings => cameraSettings;
        public Vector3 CameraFocus => map == null ? transform.position : transform.TransformPoint(new Vector3((map.Width - 1) * cellSize / 2, 0, (map.Height - 1) * cellSize / 2));
        public void Configure(GridMap value, Material material, Camera camera) { map = value; fallbackMaterial = material; viewCamera = camera; }
        private void OnEnable() { if (Application.IsPlaying(gameObject)) Build(); }
        private void Start() { if (Application.IsPlaying(gameObject)) Build(); }
        public void Build()
        {
            if (session != null) return;
            ClearGenerated();
            try { session = new GridSession(map, MoveTurnCost, BlockedTurnCost); }
            catch (Exception e) { Debug.LogError($"[IBIIIS] {name}: {e.Message}", this); enabled = false; return; }
            CreateVisuals(false);
            EnsureRuntimeEnvironment();
        }
        public void RefreshPreview()
        {
            if (Application.IsPlaying(gameObject)) return;
            ClearGenerated();
            if (map == null || !isActiveAndEnabled || map.ValidateMap(false).Count > 0) return;
            CreateVisuals(true);
        }
        public void ClearGenerated()
        {
            if (generated != null) { generated.gameObject.SetActive(false); Release(generated.gameObject); }
            generated = null; player = null; session = null;
            foreach (var material in materials) if (material != null) Release(material);
            materials.Clear();
        }
        private static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        public void FitCamera()
        {
            if (map == null || viewCamera == null) return;
            if (cameraSettings != null) cameraSettings.Apply(viewCamera);
            var center = CameraFocus;
            float tanV = Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * .5f);
            float aspect = Mathf.Max(.01f, viewCamera.aspect);
            float distance = 1, extent = .1f, maxDepth = 0;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                var corner = transform.TransformPoint(new Vector3((x == 0 ? -.5f : map.Width - .5f) * cellSize,
                    y == 0 ? -.12f : Mathf.Max(.7f, cellSize), (z == 0 ? -.5f : map.Height - .5f) * cellSize));
                var local = Quaternion.Inverse(viewCamera.transform.rotation) * (corner - center);
                extent = Mathf.Max(extent, Mathf.Abs(local.y), Mathf.Abs(local.x) / aspect);
                distance = Mathf.Max(distance, Mathf.Abs(local.y) * 1.1f / tanV - local.z,
                    Mathf.Abs(local.x) * 1.1f / (tanV * aspect) - local.z, viewCamera.nearClipPlane + .1f - local.z);
                maxDepth = Mathf.Max(maxDepth, Mathf.Abs(local.z));
            }
            viewCamera.orthographicSize = extent * 1.1f;
            if (viewCamera.orthographic) distance = Mathf.Max(100, maxDepth + viewCamera.nearClipPlane + 1);
            viewCamera.transform.position = center - viewCamera.transform.forward * distance;
            viewCamera.farClipPlane = Mathf.Max(viewCamera.farClipPlane, distance + maxDepth + 10);
        }
        private void CreateVisuals(bool preview)
        {
            if (autoFitCamera) FitCamera();
            generated = new GameObject("Generated Map").transform;
            generated.SetParent(transform, false);
            if (preview) generated.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    var p = new Vector2Int(x, y); var tile = map.GetTile(p);
                    if (tile == null) continue;
                    var anchor = new GameObject($"Cell {x},{y}").transform;
                    anchor.SetParent(generated, false); anchor.localPosition = LocalPosition(p);
                    if (tile.VisualPrefab != null) Instantiate(tile.VisualPrefab, anchor, false);
                    else
                    {
                        var visual = Block(anchor, tile.Color);
                        visual.localScale = new Vector3(cellSize * .96f, tile.Walkable ? .12f : .7f, cellSize * .96f);
                        visual.localPosition = new Vector3(0, tile.Walkable ? -.06f : .35f, 0);
                    }
                }
            if (map.HasStart && map.IsWalkable(map.Start)) CreatePlayer(preview ? map.Start : session.Position);
            if (preview)
                foreach (var child in generated.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }
        private void CreatePlayer(Vector2Int position)
        {
            player = new GameObject("Player Logic Anchor").transform;
            player.SetParent(generated, false); player.localPosition = LocalPosition(position);
            if (PlayerVisualPrefab != null) Instantiate(PlayerVisualPrefab, player, false);
            else
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
                visual.name = "Temporary Player Visual"; visual.transform.SetParent(player, false);
                visual.transform.localPosition = new Vector3(0, cellSize * .45f, 0);
                visual.transform.localScale = Vector3.one * cellSize * .7f;
                if (viewCamera != null) visual.transform.rotation = viewCamera.transform.rotation;
                Release(visual.GetComponent<Collider>()); Tint(visual, new Color(1f, .76f, .25f));
            }
        }
        private Vector3 LocalPosition(Vector2Int p) => new Vector3(p.x * cellSize, 0, p.y * cellSize);
        private Transform Block(Transform parent, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(parent, false);
            Release(go.GetComponent<Collider>()); Tint(go, color); return go.transform;
        }
        private void Tint(GameObject go, Color color)
        {
            if (fallbackMaterial == null) return;
            var material = new Material(fallbackMaterial) { hideFlags = HideFlags.HideAndDontSave }; material.color = color; materials.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || session == null || Keyboard.current == null) return;
            var k = Keyboard.current;
            var direction = k.wKey.wasPressedThisFrame ? Vector2Int.up : k.sKey.wasPressedThisFrame ? Vector2Int.down :
                k.aKey.wasPressedThisFrame ? Vector2Int.left : k.dKey.wasPressedThisFrame ? Vector2Int.right : Vector2Int.zero;
            if (direction == Vector2Int.zero) return;
            session.TryMove(direction); player.localPosition = LocalPosition(session.Position);
        }
        private void OnGUI()
        {
            if (Application.IsPlaying(gameObject) && session != null) GUI.Box(new Rect(12, 12, 360, 52), $"WASD: move one cell   |   Turn {session.Turn}\nCell ({session.Position.x}, {session.Position.y})");
        }
        private void OnDisable() { ClearGenerated(); }
        private void OnDestroy() { ClearGenerated(); }
    }
}
