using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    [ExecuteAlways]
    public sealed class GridMapPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. 맵 에디터에서 작성한 맵")] private GridMap map;
        [SerializeField, Min(.1f), Tooltip("한 칸의 월드 크기")] private float cellSize = 1;
        [SerializeField, Tooltip("공용 플레이어 외형·이동 시간·이동 표시 설정. 새 테스트 씬은 기본 설정을 자동 연결합니다.")] private PlayerSettings playerSettings;
        [SerializeField, HideInInspector] private GameObject playerVisualPrefab;
        [SerializeField, Tooltip("선택. 이동 영역과 임시 플레이어 외형에 쓸 머티리얼")] private Material fallbackMaterial;
        [SerializeField, Tooltip("맵 전체를 비추는 테스트 카메라")] private Camera viewCamera;
        [SerializeField, Tooltip("선택. 자동 맞춤 시 적용하는 공용 Projection·각도·화각 설정")] private MapCameraSettings cameraSettings;
        [SerializeField, Tooltip("Scene과 Play에서 맵 전체가 보이도록 카메라 위치·크기를 자동 계산합니다. 끄면 직접 편집한 카메라 구도를 유지합니다.")] private bool autoFitCamera = true;
        private readonly List<Material> materials = new List<Material>();
        private GridSession session;
        private Transform generated;
        private Transform player;
        private PlayerVisual playerView;
        private PlayerAction lastAction = PlayerAction.Wait;
        private Vector2Int lastDirection;
        private Transform[] moveHints;
        private readonly List<EnemyDefinition> enemyViews = new List<EnemyDefinition>();
        // 되돌리기 시 외형(마지막 행동·방향)을 복원하기 위해 GridSession의 행동 기록과 같은 순서로 쌓는다.
        private readonly Stack<KeyValuePair<PlayerAction, Vector2Int>> actionHistory = new Stack<KeyValuePair<PlayerAction, Vector2Int>>();
        private Transform rangeRoot;
        private readonly List<Transform> attackMarks = new List<Transform>(), recognitionMarks = new List<Transform>();
        private Material attackMaterial, recognitionMaterial;
        private bool showRanges, rangesInitialized;
        private BattleInput input;
        private CollisionFeedback feedback;
        private MotionFeedback motion;
        // 적 인식 표시(머리 위 !·?, 표시 전용)
        private EnemyAlert alert;
        public EnemyAlert Alert => alert;
        public MotionFeedback Motion => motion;
        // 이번 행동의 충돌 기록 중 연출을 시작한 개수와 패배 연출 시작 여부
        private int shownCollisions;
        private bool shownDefeat;
        private Transform fallbackPlayerVisual;
        /// <summary>충돌 연출이 재생 중이면 true. 이 동안 입력을 받지 않는다.</summary>
        public bool IsPresenting => feedback != null && feedback.IsPlaying;
        public CollisionFeedback Feedback => feedback;
        public bool ShowEnemyRanges => showRanges;
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        private static readonly Vector2Int[] RollDirections = { new Vector2Int(-1,1), new Vector2Int(1,1), new Vector2Int(-1,-1), new Vector2Int(1,-1) };
        public float MoveDuration => playerSettings != null ? playerSettings.MoveDuration : .25f;
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
            try { session = new GridSession(map); }
            catch (Exception e) { Debug.LogError($"[IBIIIS] {name}: {e.Message}", this); enabled = false; return; }
            if (!rangesInitialized) { showRanges = playerSettings == null || playerSettings.ShowEnemyRanges; rangesInitialized = true; }
            CreateVisuals(false);
            feedback = new CollisionFeedback(playerSettings != null ? playerSettings.CollisionFeedback : null, generated, LocalPosition, cellSize, viewCamera);
            motion = new MotionFeedback(playerSettings != null ? playerSettings.MotionFeedback : null, generated, LocalPosition, cellSize, viewCamera);
            alert = new EnemyAlert(playerSettings != null ? playerSettings.EnemyAlert : null, generated, viewCamera, cellSize);
            UpdateMotion(0); // 적 방향 등 현재 상태를 기준으로 기억해 첫 행동의 방향 전환도 반응하게 한다
            EnsureRuntimeEnvironment();
            if (Application.IsPlaying(gameObject)) { input = new BattleInput(playerSettings != null ? playerSettings.InputActions : null); input.Enable(); }
            if (Application.IsPlaying(gameObject)) MovementWorldTime.Register(this);
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
            if (feedback != null) { feedback.Dispose(); feedback = null; }
            if (motion != null) { motion.Dispose(); motion = null; }
            if (alert != null) { alert.Dispose(); alert = null; }
            shownCollisions = 0; shownDefeat = false; fallbackPlayerVisual = null;
            if (generated != null) { generated.gameObject.SetActive(false); Release(generated.gameObject); }
            MovementWorldTime.Unregister(this);
            generated = null; player = null; playerView = null; lastAction = PlayerAction.Wait; lastDirection = Vector2Int.zero; session = null; moveHints = null; enemyViews.Clear();
            if (input != null) { input.Dispose(); input = null; }
            actionHistory.Clear(); rangeRoot = null; attackMarks.Clear(); recognitionMarks.Clear(); attackMaterial = recognitionMaterial = null;
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
                    var p = new Vector2Int(x, y);
                    if (!map.IsWalkable(p)) continue;
                    var visual = FlatSurface($"Cell {x},{y}", new Vector3(cellSize * .96f, cellSize * .96f, 1));
                    visual.localPosition = LocalPosition(p) + Vector3.up * .01f;
                    var tile = map.GetTile(p);
                    if (tile != null && tile.SurfaceMaterial != null) visual.GetComponent<Renderer>().sharedMaterial = tile.SurfaceMaterial;
                    else Tint(visual.gameObject, map.GetFloorColor(p));
                }
            if (map.HasStart && map.IsWalkable(map.Start)) CreatePlayer(preview ? map.Start : session.Position);
            var ground = FlatSurface("Background Ground", new Vector3((map.Width + map.GroundMargin * 2) * cellSize, (map.Height + map.GroundMargin * 2) * cellSize, 1));
            ground.localPosition = new Vector3((map.Width - 1) * cellSize / 2, 0, (map.Height - 1) * cellSize / 2);
            var groundMaterial = map.GroundMaterial != null ? map.GroundMaterial : Resources.Load<Material>("IBIIIS/DefaultGround");
            if (groundMaterial != null) ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            else Tint(ground.gameObject, new Color(.22f, .24f, .26f));
            CreateEnemyViews();
            CreateMoveHints(); RefreshMoveHints();
            if (preview)
                foreach (var child in generated.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }
        private void CreatePlayer(Vector2Int position)
        {
            player = new GameObject("Player Logic Anchor").transform;
            player.SetParent(generated, false); player.localPosition = LocalPosition(position);
            if (PlayerVisualPrefab != null)
            {
                var instance = Instantiate(PlayerVisualPrefab, player, false);
                playerView = instance.GetComponentInChildren<PlayerVisual>();
                if (playerView != null) { instance.transform.localScale = Vector3.one * cellSize; ShowPlayerVisual(); }
            }
            else
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
                visual.name = "Temporary Player Visual"; visual.transform.SetParent(player, false); fallbackPlayerVisual = visual.transform;
                visual.transform.localPosition = new Vector3(0, cellSize * .45f, 0);
                visual.transform.localScale = Vector3.one * cellSize * .7f;
                CameraFacingSprite.Face(visual.transform, viewCamera);
                Release(visual.GetComponent<Collider>()); Tint(visual, new Color(1f, .76f, .25f));
            }
        }
        private Vector3 LocalPosition(Vector2Int p) => new Vector3(p.x * cellSize, 0, p.y * cellSize);
        private Transform FlatSurface(string label, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = label; go.transform.SetParent(generated, false);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0); go.transform.localScale = scale;
            Release(go.GetComponent<Collider>()); return go.transform;
        }
        private void Tint(GameObject go, Color color)
        {
            if (fallbackMaterial == null) return;
            var material = new Material(fallbackMaterial) { hideFlags = HideFlags.HideAndDontSave }; material.color = color; materials.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        private void CreateEnemyViews()
        {
            if (map.Enemies.Count == 0) return;
            var root = new GameObject("Enemies").transform; root.SetParent(generated, false);
            foreach (var spawn in map.Enemies)
            {
                var instance = Instantiate(spawn.Prefab, root, false);
                instance.transform.localPosition = LocalPosition(spawn.Position);
                instance.transform.localScale = Vector3.one * cellSize;
                instance.transform.localRotation = Quaternion.LookRotation(new Vector3(spawn.Direction.x, 0, spawn.Direction.y));
                var view = instance.GetComponent<EnemyDefinition>(); view.FaceCamera(viewCamera); enemyViews.Add(view);
            }
        }
        private void UpdateEnemyViews()
        {
            if (session == null) return;
            for (int i = 0; i < enemyViews.Count; i++)
            {
                if (feedback != null && feedback.Owns(i)) continue;
                var state = session.Enemies[i]; var view = enemyViews[i]; view.gameObject.SetActive(state.Alive);
                view.transform.localPosition = session.IsEnemiesMoving ? Vector3.Lerp(LocalPosition(state.StepFrom), LocalPosition(state.StepTo), session.EnemyProgress) : LocalPosition(state.Position);
                view.transform.localRotation = Quaternion.LookRotation(new Vector3(state.Direction.x, 0, state.Direction.y)); view.FaceCamera(viewCamera);
            }
        }
        private void CreateMoveHints()
        {
            var root = new GameObject("Movement Hints").transform; root.SetParent(generated, false);
            moveHints = new Transform[13];
            var basis = playerSettings != null ? playerSettings.MoveHintMaterial : null;
            if (basis == null) basis = fallbackMaterial != null ? fallbackMaterial : Resources.Load<Material>("IBIIIS/DefaultGround");
            var shader = basis != null ? basis.shader : Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            for (int i = 0; i < moveHints.Length; i++)
            {
                var ring = new GameObject(i == 4 ? "Destination" : i < 4 ? "Adjacent " + Directions[i] : i < 9 ? "Dash " + Directions[i - 5] : "Roll " + RollDirections[i - 9]).transform;
                ring.SetParent(root, false); moveHints[i] = ring;
                var material = basis != null ? new Material(basis) : new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
                material.color = i == 4 ? (playerSettings != null ? playerSettings.DestinationColor : Color.yellow) :
                    i >= 9 ? new Color(.8f, .4f, 1f) : i >= 5 ? new Color(.3f, .5f, 1f) : (playerSettings != null ? playerSettings.MoveHintColor : Color.cyan);
                materials.Add(material);
                for (int edge = 0; edge < 4; edge++)
                {
                    bool horizontal = edge < 2; float sign = edge % 2 == 0 ? -1 : 1;
                    var strip = FlatSurface("Border", new Vector3(cellSize * (horizontal ? .9f : .035f), cellSize * (horizontal ? .035f : .9f), 1));
                    strip.SetParent(ring, false); strip.localRotation = Quaternion.Euler(90, 0, 0);
                    strip.localPosition = horizontal ? new Vector3(0, 0, sign * cellSize * .435f) : new Vector3(sign * cellSize * .435f, 0, 0);
                    strip.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
        }
        public void ToggleEnemyRanges() { showRanges = !showRanges; RefreshRanges(); }
        private Material OverlayMaterial(Color color)
        {
            var basis = playerSettings != null ? playerSettings.MoveHintMaterial : null;
            if (basis == null) basis = fallbackMaterial != null ? fallbackMaterial : Resources.Load<Material>("IBIIIS/DefaultGround");
            var shader = basis != null ? basis.shader : Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            var material = basis != null ? new Material(basis) : new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave; material.color = color; materials.Add(material);
            return material;
        }
        private Transform CreateAttackMark()
        {
            var mark = FlatSurface("Attack Mark", new Vector3(cellSize * .7f, cellSize * .7f, 1)); mark.SetParent(rangeRoot, false);
            if (attackMaterial != null) mark.GetComponent<Renderer>().sharedMaterial = attackMaterial;
            return mark;
        }
        private Transform CreateRecognitionMark()
        {
            var ring = new GameObject("Recognition Mark").transform; ring.SetParent(rangeRoot, false);
            for (int edge = 0; edge < 4; edge++)
            {
                bool horizontal = edge < 2; float sign = edge % 2 == 0 ? -1 : 1;
                var strip = FlatSurface("Border", new Vector3(cellSize * (horizontal ? .96f : .04f), cellSize * (horizontal ? .04f : .96f), 1));
                strip.SetParent(ring, false);
                strip.localPosition = horizontal ? new Vector3(0, 0, sign * cellSize * .46f) : new Vector3(sign * cellSize * .46f, 0, 0);
                if (recognitionMaterial != null) strip.GetComponent<Renderer>().sharedMaterial = recognitionMaterial;
            }
            return ring;
        }
        private static void PlaceMarks(List<Transform> marks, HashSet<Vector2Int> cells, Func<Transform> create, Func<Vector2Int, Vector3> position)
        {
            int used = 0;
            foreach (var cell in cells)
            {
                if (used == marks.Count) marks.Add(create());
                marks[used].gameObject.SetActive(true); marks[used].localPosition = position(cell); used++;
            }
            for (; used < marks.Count; used++) marks[used].gameObject.SetActive(false);
        }
        /// <summary>입력 대기·승패 상태에서 적의 현재 인식 범위(노란 테두리)와 공격 범위(빨간 칸)를 표시한다. 표시 전용이며 판정에 쓰지 않는다.</summary>
        private void RefreshRanges()
        {
            bool show = showRanges && session != null && !session.IsBusy && !IsPresenting;
            if (!show) { if (rangeRoot != null) rangeRoot.gameObject.SetActive(false); return; }
            if (rangeRoot == null)
            {
                rangeRoot = new GameObject("Enemy Ranges").transform; rangeRoot.SetParent(generated, false);
                attackMaterial = OverlayMaterial(new Color(.95f, .2f, .2f)); recognitionMaterial = OverlayMaterial(new Color(.95f, .88f, .25f));
            }
            rangeRoot.gameObject.SetActive(true);
            var attack = new HashSet<Vector2Int>(); var recognition = new HashSet<Vector2Int>();
            foreach (var enemy in session.Enemies)
            {
                if (!enemy.Alive) continue;
                foreach (var cell in session.RangeCells(enemy, EnemyRange.Attack)) attack.Add(cell);
                foreach (var cell in session.RangeCells(enemy, EnemyRange.Recognition)) recognition.Add(cell);
            }
            PlaceMarks(attackMarks, attack, CreateAttackMark, c => LocalPosition(c) + Vector3.up * .015f);
            PlaceMarks(recognitionMarks, recognition, CreateRecognitionMark, c => LocalPosition(c) + Vector3.up * .02f);
        }
        public void RefreshMoveHints()
        {
            RefreshRanges();
            if (moveHints == null) return;
            bool enabledHints = playerSettings == null || playerSettings.ShowMoveHints;
            bool valid = session != null || (map != null && map.HasStart && map.IsWalkable(map.Start));
            bool moving = session != null && session.IsMoving;
            var position = session != null ? session.Position : map != null ? map.Start : Vector2Int.zero;
            for (int i = 0; i < moveHints.Length; i++)
            {
                if (moveHints[i] == null) continue;
                bool waiting = session == null || (session.Phase == BattlePhase.Waiting && !IsPresenting);
                var direction = i < 4 ? Directions[i] : i == 4 ? Vector2Int.zero : i < 9 ? Directions[i - 5] : RollDirections[i - 9];
                var action = i < 5 ? PlayerAction.Move : i < 9 ? PlayerAction.Dash : PlayerAction.Roll;
                bool can = i != 4 && waiting && (session != null ? session.CanAct(action, direction) : i < 4 && GridSession.CanStep(position, direction, p => map.IsWalkable(p) && map.EnemyAt(p) == null));
                bool visible = enabledHints && valid && (i == 4 ? moving : can);
                moveHints[i].gameObject.SetActive(visible);
                if (visible) moveHints[i].localPosition = LocalPosition(i == 4 ? session.Destination : position + direction * (action == PlayerAction.Dash ? 2 : 1)) + Vector3.up * .025f;
            }
        }
        public bool TryBeginMove(Vector2Int direction) => TryBeginAction(PlayerAction.Move, direction);
        public bool TryBeginAction(PlayerAction action, Vector2Int direction)
        {
            float duration = MoveDuration * (action == PlayerAction.Dash ? 2 : 1);
            if (session == null || !session.TryAct(action, direction, duration, playerSettings != null ? playerSettings.EnemyStepDuration : .25f)) return false;
            if (Application.IsPlaying(gameObject)) MovementWorldTime.SetMoving(this, true);
            shownCollisions = 0; shownDefeat = false; motion?.BeginAction(action, session.Position, direction);
            actionHistory.Push(new KeyValuePair<PlayerAction, Vector2Int>(lastAction, lastDirection));
            lastAction = action; lastDirection = direction; ShowPlayerVisual();
            UpdateEnemyViews(); RefreshMoveHints(); return true;
        }
        /// <summary>마지막 플레이어 행동 한 번을 되돌린다. 행동 진행 중에는 무시한다.</summary>
        public bool TryUndo()
        {
            if (session == null || !session.TryUndo()) return false;
            feedback?.Clear(); motion?.Clear(); shownCollisions = 0; shownDefeat = false;
            if (playerView != null) playerView.SetPose(MotionPose.Identity, viewCamera);
            var previous = actionHistory.Count > 0 ? actionHistory.Pop() : new KeyValuePair<PlayerAction, Vector2Int>(PlayerAction.Wait, Vector2Int.zero);
            lastAction = previous.Key; lastDirection = previous.Value;
            if (player != null) player.localPosition = LocalPosition(session.Position);
            if (Application.IsPlaying(gameObject)) MovementWorldTime.SetMoving(this, false);
            ShowPlayerVisual(); UpdateEnemyViews(); RefreshMoveHints();
            alert?.Resync(session); UpdateAlert(0);
            return true;
        }
        private void ShowPlayerVisual()
        {
            if (playerView == null || (feedback != null && feedback.OwnsPlayer)) return;
            bool moving = session != null && session.IsMoving;
            playerView.Show(lastAction, lastDirection, session != null ? session.Progress : 0, moving, viewCamera);
        }
        /// <summary>행동과 충돌 연출을 진행한다. 충돌 직후 맞닿음·멈춤 시간 동안은 다른 움직임을 멈춘다(연출 전용, 판정 결과는 같다).</summary>
        public void AdvanceMovement(float seconds)
        {
            if (session == null || (!session.IsBusy && !IsPresenting)) return;
            float hold = feedback != null ? feedback.HoldRemaining : 0;
            feedback?.Tick(seconds);
            float left = Mathf.Max(0, seconds - hold);
            if (left > 0 && session.IsBusy) session.Advance(left, feedback != null && feedback.Settings.Enabled);
            for (; feedback != null && shownCollisions < session.Collisions.Count; shownCollisions++)
                feedback.Begin(session.Collisions[shownCollisions], enemyViews, session.Enemies);
            if (!shownDefeat && feedback != null && player != null && session.Phase == BattlePhase.Lost)
            {
                shownDefeat = true;
                feedback.BeginPlayer(session.Position, player, playerView != null && playerView.Renderer != null ? playerView.Renderer.transform : fallbackPlayerVisual, DefeatAway());
            }
            if (feedback == null || !feedback.OwnsPlayer) player.localPosition = session.IsMoving ? Vector3.Lerp(LocalPosition(session.Position), LocalPosition(session.Destination), session.Progress) : LocalPosition(session.Position);
            ShowPlayerVisual(); UpdateEnemyViews();
            if (Application.IsPlaying(gameObject)) MovementWorldTime.SetMoving(this, session.IsBusy || IsPresenting);
            RefreshMoveHints();
        }
        // 플레이어가 튕겨 나갈 방향: 맞힌 적들이 바라보는 방향(여럿이면 합).
        // 서로 반대를 봐서 상쇄되면 적에게서 멀어지는 쪽, 그것도 없으면 마지막 이동의 반대 방향을 쓴다.
        private Vector3 DefeatAway()
        {
            var away = Vector2.zero;
            foreach (var index in session.Attackers) away += (Vector2)session.Enemies[index].Direction;
            if (away.sqrMagnitude < 1e-6f)
                foreach (var index in session.Attackers) { var delta = session.Position - session.Enemies[index].Position; if (delta != Vector2Int.zero) away += ((Vector2)delta).normalized; }
            if (away.sqrMagnitude < 1e-6f) away = -(Vector2)lastDirection;
            return new Vector3(away.x, 0, away.y);
        }
        /// <summary>플레이어 이동 손맛 자세를 갱신한다(매 프레임). 충돌·패배 연출이 플레이어를 맡고 있으면 건드리지 않는다.</summary>
        public void UpdateMotion(float seconds)
        {
            UpdateAlert(seconds);
            if (motion == null || session == null) return;
            motion.TickEffects(seconds);
            motion.TickEnemies(seconds, session, enemyViews, i => feedback != null && feedback.Owns(i), p => map != null && map.IsWalkable(p));
            if (playerView == null || (feedback != null && feedback.OwnsPlayer)) return;
            playerView.SetPose(motion.Tick(seconds, session, lastAction, lastDirection, playerView.Renderer), viewCamera);
        }
        // 적 인식 표시는 판정이 확정된 입력 대기·승패 상태에서만 보인다(행동·연출 중에는 숨김). 충돌로 날아가는 적은 건너뛴다.
        private void UpdateAlert(float seconds)
        {
            if (alert == null || session == null) return;
            alert.Update(seconds, session, enemyViews, !session.IsBusy && !IsPresenting, i => feedback != null && feedback.Owns(i));
        }
        /// <summary>행동을 시작한다. 입력 대기 중인데 갈 수 없는 방향이면 그쪽으로 부딪히는 반응만 보이고 false를 돌려준다(시간·판정 변화 없음).</summary>
        public bool TryActionOrBump(PlayerAction action, Vector2Int direction)
        {
            if (TryBeginAction(action, direction)) return true;
            if (session != null && session.Phase == BattlePhase.Waiting && !IsPresenting && action != PlayerAction.Wait) motion?.Bump(direction);
            return false;
        }
        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || session == null) return;
            float dt = Time.unscaledDeltaTime;
            if (session.IsBusy || IsPresenting) { AdvanceMovement(dt); UpdateMotion(dt); return; }
            UpdateMotion(dt);
            if (input == null) return;
            var command = input.Read();
            switch (command.Kind)
            {
                case BattleCommandKind.Restart: ClearGenerated(); Build(); break;
                case BattleCommandKind.ToggleRanges: ToggleEnemyRanges(); break;
                case BattleCommandKind.Undo: TryUndo(); break;
                case BattleCommandKind.Wait: TryBeginAction(PlayerAction.Wait, Vector2Int.zero); break;
                case BattleCommandKind.Roll: TryActionOrBump(PlayerAction.Roll, command.Direction); break;
                case BattleCommandKind.Move: TryActionOrBump(PlayerAction.Move, command.Direction); break;
                case BattleCommandKind.Dash: TryActionOrBump(PlayerAction.Dash, command.Direction); break;
            }
        }
        private void OnGUI()
        {
            if (Application.IsPlaying(gameObject) && session != null) GUI.Box(new Rect(12, 12, 560, 96),
                $"{session.Phase} | Enemies {session.AliveCount} | Evasion {(session.EvasionLocked ? "cooldown" : "ready")}\n{ControlsHint()}\nUndo ({session.UndoCount}) | Enemy ranges {(showRanges ? "ON" : "OFF")} (red=attack, yellow=recognition)\nCell ({session.Position.x}, {session.Position.y})");
        }
        // 안내 문구는 현재 입력 에셋의 첫 번째 바인딩을 보여 준다(재바인딩 반영).
        private string ControlsHint()
        {
            if (input == null) return "";
            return $"Move {input.Key(BattleInput.MoveUp)}{input.Key(BattleInput.MoveLeft)}{input.Key(BattleInput.MoveDown)}{input.Key(BattleInput.MoveRight)} | {input.Key(BattleInput.DashModifier)}+Move Dash | " +
                $"Roll {input.Key(BattleInput.RollUpLeft)}/{input.Key(BattleInput.RollUpRight)}/{input.Key(BattleInput.RollDownLeft)}/{input.Key(BattleInput.RollDownRight)} | {input.Key(BattleInput.Wait)} Wait | {input.Key(BattleInput.Undo)} Undo | {input.Key(BattleInput.ToggleRanges)} Ranges | {input.Key(BattleInput.Restart)} Restart";
        }
        private void OnDisable() { ClearGenerated(); }
        private void OnDestroy() { ClearGenerated(); }
    }
}
