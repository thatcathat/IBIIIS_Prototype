using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

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
        // 되돌리기용: 행동 직전의 마지막 행동·방향과 바라보던 방향(대기는 방향을 바꾸지 않으므로 따로 기억한다).
        private struct ActionMemo { public PlayerAction Action; public Vector2Int Direction; public PlayerFacing Facing; }
        private readonly Stack<ActionMemo> actionHistory = new Stack<ActionMemo>();
        // 매 프레임 쓰는 콜백과 HUD 문자열은 한 번만 만든다.
        private Func<int, bool> ownsEnemy;
        private string controlsHint = "";
        // 정식 전투 HUD(UI Toolkit). Game UI 설정이 없으면 null이고 OnGUI의 임시 표시를 쓴다.
        private BattleHud hud;
        // HUD의 다시 시작 버튼은 그 HUD를 지우고 다시 만들므로, 클릭 처리 중이 아니라 다음 Update에서 실행한다.
        private bool restartRequested;
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
        // 이번 행동의 충돌 기록 중 연출을 시작한 개수
        private int shownCollisions;
        // 상황 반응(표시 전용): 행동 시작 때 인식 범위 안이었는지·제자리에 있으면 맞았는지 기록하고, 판정이 끝나면 반응 그림을 고른다.
        private bool reactionPending, startedRecognized, startedThreatened;
        private Sprite[] reactionFrames;
        private float reactionTime;
        private bool reactionHold;
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
            alert = new EnemyAlert(playerSettings != null ? playerSettings.EnemyAlert : null, generated, viewCamera, cellSize, player);
            UpdateMotion(0); // 적 방향 등 현재 상태를 기준으로 기억해 첫 행동의 방향 전환도 반응하게 한다
            EnsureRuntimeEnvironment();
            if (Application.IsPlaying(gameObject)) { input = new BattleInput(playerSettings != null ? playerSettings.InputActions : null); input.Enable(); controlsHint = ControlsHint(); CreateHud(); }
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
            shownCollisions = 0; ClearReaction();
            if (generated != null) { generated.gameObject.SetActive(false); Release(generated.gameObject); }
            MovementWorldTime.Unregister(this);
            generated = null; player = null; playerView = null; lastAction = PlayerAction.Wait; lastDirection = Vector2Int.zero; session = null; moveHints = null; enemyViews.Clear();
            if (input != null) { input.Dispose(); input = null; }
            hud = null; restartRequested = false; // HUD 오브젝트는 generated 아래에 있어 함께 지워진다
            poseSprite = null; poseTime = poseDuration = 0; poseMotion = poseEndMotion = PoseMotion.None; poseUntilAction = false;
            guideMarks.Clear(); guideMaterial = null; // 안내 칸도 generated 아래(재질은 materials 목록이 지운다)
            actionHistory.Clear(); rangeRoot = null; attackMarks.Clear(); recognitionMarks.Clear(); attackMaterial = recognitionMaterial = null;
            foreach (var material in materials) if (material != null) Release(material);
            materials.Clear();
        }
        private static void Release(UnityEngine.Object value) => RuntimeObjects.Release(value);
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
                    // 이동 칸은 칸 크기 그대로 깔아 이웃 칸과 틈 없이 붙인다.
                    var visual = FlatSurface($"Cell {x},{y}", new Vector3(cellSize, cellSize, 1));
                    visual.localPosition = LocalPosition(p) + Vector3.up * .01f;
                    var tile = map.GetTile(p);
                    if (tile != null && tile.SurfaceMaterial != null) visual.GetComponent<Renderer>().sharedMaterial = tile.SurfaceMaterial;
                    else Tint(visual.gameObject, map.GetFloorColor(p));
                }
            if (map.HasStart && map.IsWalkable(map.Start)) CreatePlayer(preview ? map.Start : session.Position, preview);
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
        private void CreatePlayer(Vector2Int position, bool preview)
        {
            player = new GameObject("Player Logic Anchor").transform;
            player.SetParent(generated, false); player.localPosition = LocalPosition(position);
            if (PlayerVisualPrefab != null)
            {
                var instance = Instantiate(PlayerVisualPrefab, player, false);
                instance.transform.localScale = Vector3.one * cellSize;
                playerView = instance.GetComponentInChildren<PlayerVisual>(true);
                if (playerView != null) ShowPlayerVisual();
                // 미리보기는 자주 다시 만들므로 실행할 때만 알린다.
                else if (!preview) Debug.LogWarning($"[IBIIIS] {name}: Visual Prefab '{PlayerVisualPrefab.name}'에 PlayerVisual이 없어 방향 그림·손맛·패배 연출 없이 표시합니다.", PlayerVisualPrefab);
            }
            else
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
                visual.name = "Temporary Player Visual"; visual.transform.SetParent(player, false);
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
            foreach (var spawn in map.Enemies) AddEnemyView(spawn.Prefab, spawn.Position, spawn.Direction);
        }
        private void AddEnemyView(GameObject prefab, Vector2Int position, Vector2Int direction)
        {
            var root = generated.Find("Enemies");
            if (root == null) { root = new GameObject("Enemies").transform; root.SetParent(generated, false); }
            var instance = Instantiate(prefab, root, false);
            instance.transform.localPosition = LocalPosition(position);
            instance.transform.localScale = Vector3.one * cellSize;
            instance.transform.localRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.y));
            var view = instance.GetComponent<EnemyDefinition>(); view.FaceCamera(viewCamera); enemyViews.Add(view);
        }
        /// <summary>입력 대기 중에 적을 합류시킨다(대본의 몬스터 합류). 판정 규칙은 GridSession.TryAddEnemy를 따른다.</summary>
        public bool SpawnEnemy(GameObject prefab, Vector2Int cell, Vector2Int direction)
        {
            if (session == null || IsPresenting || !session.TryAddEnemy(prefab, cell, direction)) return false;
            AddEnemyView(prefab, cell, direction); UpdateEnemyViews(); RefreshMoveHints();
            return true;
        }
        /// <summary>칸을 막거나 푼다(칸만 막는 오브젝트). 판정 규칙은 GridSession.SetBlocked를 따른다.</summary>
        public bool SetCellBlocked(Vector2Int cell, bool value)
        {
            if (session == null || !session.SetBlocked(cell, value)) return false;
            RefreshMoveHints(); return true;
        }
        /// <summary>칸 가운데의 월드 위치(바닥 높이).</summary>
        public Vector3 CellWorldPosition(Vector2Int cell) => transform.TransformPoint(LocalPosition(cell));
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
            for (int i = 0; i < moveHints.Length; i++)
            {
                var color = i == 4 ? (playerSettings != null ? playerSettings.DestinationColor : Color.yellow) :
                    i >= 9 ? new Color(.8f, .4f, 1f) : i >= 5 ? new Color(.3f, .5f, 1f) : (playerSettings != null ? playerSettings.MoveHintColor : Color.cyan);
                var material = OverlayMaterial(color);
                if (material == null) return; // 셰이더가 없으면 힌트 없이 진행(RefreshMoveHints가 빈 칸을 건너뜀)
                var ring = Outline(i == 4 ? "Destination" : i < 4 ? "Adjacent " + Directions[i] : i < 9 ? "Dash " + Directions[i - 5] : "Roll " + RollDirections[i - 9], root, .9f, .035f, .435f, material);
                moveHints[i] = ring;
            }
        }
        // 칸 테두리 4줄(길이 length, 두께 width, 중심에서 offset만큼 떨어짐, 칸 단위)
        private Transform Outline(string label, Transform parent, float length, float width, float offset, Material material)
        {
            var ring = new GameObject(label).transform; ring.SetParent(parent, false);
            for (int edge = 0; edge < 4; edge++)
            {
                bool horizontal = edge < 2; float sign = edge % 2 == 0 ? -1 : 1;
                var strip = FlatSurface("Border", new Vector3(cellSize * (horizontal ? length : width), cellSize * (horizontal ? width : length), 1));
                strip.SetParent(ring, false);
                strip.localPosition = horizontal ? new Vector3(0, 0, sign * cellSize * offset) : new Vector3(sign * cellSize * offset, 0, 0);
                if (material != null) strip.GetComponent<Renderer>().sharedMaterial = material;
            }
            return ring;
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
        private Transform CreateRecognitionMark() => Outline("Recognition Mark", rangeRoot, .96f, .04f, .46f, recognitionMaterial);
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
                attackMaterial = OverlayMaterial(EnemyRangeColors.Attack); recognitionMaterial = OverlayMaterial(EnemyRangeColors.Recognition);
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
            if (session == null || !session.CanAct(action, direction) || session.Phase != BattlePhase.Waiting) return false;
            // 반응 판정용(표시 전용): 행동 전 인식 여부(위험 그림이 보이는 상황), 회피기라면 제자리에 있었을 때 맞았을지.
            bool recognized = AnyRecognizing();
            bool threatened = (action == PlayerAction.Dash || action == PlayerAction.Roll) && session.Predict(PlayerAction.Wait, Vector2Int.zero) == BattlePhase.Lost;
            if (!session.TryAct(action, direction, duration, playerSettings != null ? playerSettings.EnemyStepDuration : .25f)) return false;
            if (Application.IsPlaying(gameObject)) MovementWorldTime.SetMoving(this, true);
            shownCollisions = 0; motion?.BeginAction(action, session.Position, direction);
            ClearReaction(); reactionPending = true; startedRecognized = recognized; startedThreatened = threatened;
            if (poseUntilAction) EndPose();
            actionHistory.Push(new ActionMemo { Action = lastAction, Direction = lastDirection, Facing = playerView != null ? playerView.Facing : PlayerFacing.Front });
            lastAction = action; lastDirection = direction; ShowPlayerVisual();
            UpdateEnemyViews(); RefreshMoveHints();
            // 첫 프레임이 적 이동 단계보다 길어도 첫 칸 착지를 놓치지 않도록 행동 시작 상태를 바로 알린다.
            TickEnemyMotion(0);
            return true;
        }
        /// <summary>마지막 플레이어 행동 한 번을 되돌린다. 행동 진행 중에는 무시한다.</summary>
        public bool TryUndo()
        {
            if (session == null || !session.TryUndo()) return false;
            feedback?.Clear(); motion?.Clear(); shownCollisions = 0; ClearReaction();
            if (playerView != null) playerView.SetPose(MotionPose.Identity, viewCamera);
            var previous = actionHistory.Count > 0 ? actionHistory.Pop() : new ActionMemo { Action = PlayerAction.Wait, Facing = PlayerFacing.Front };
            lastAction = previous.Action; lastDirection = previous.Direction;
            if (playerView != null) playerView.SetFacing(previous.Facing);
            if (player != null) player.localPosition = LocalPosition(session.Position);
            // 되돌리기로 빠진 합류 적의 외형을 지운다.
            while (enemyViews.Count > session.Enemies.Count) { var last = enemyViews[enemyViews.Count - 1]; enemyViews.RemoveAt(enemyViews.Count - 1); if (last != null) Release(last.gameObject); }
            if (Application.IsPlaying(gameObject)) MovementWorldTime.SetMoving(this, false);
            ShowPlayerVisual(); UpdateEnemyViews(); RefreshMoveHints();
            alert?.Resync(session); UpdateAlert(0);
            return true;
        }
        private void ShowPlayerVisual()
        {
            if (playerView == null) return;
            bool moving = session != null && session.IsMoving;
            playerView.InDanger = dangerIdleEnabled && session != null && session.Phase == BattlePhase.Waiting && !session.IsBusy && AnyRecognizing();
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
            if (reactionPending && !session.IsBusy) { reactionPending = false; ChooseReaction(); }
            player.localPosition = session.IsMoving ? Vector3.Lerp(LocalPosition(session.Position), LocalPosition(session.Destination), session.Progress) : LocalPosition(session.Position);
            ShowPlayerVisual(); UpdateEnemyViews();
            if (Application.IsPlaying(gameObject)) MovementWorldTime.SetMoving(this, session.IsBusy || IsPresenting);
            RefreshMoveHints();
        }
        private bool AnyRecognizing()
        {
            if (session == null) return false;
            foreach (var e in session.Enemies) if (session.IsRecognizing(e)) return true;
            return false;
        }
        /// <summary>지금 보이는 상황 반응 그림(없으면 null). 테스트·디버그용.</summary>
        public Sprite ReactionSprite => playerView != null ? playerView.ReactionSprite : null;
        // 판정이 끝난 행동의 반응: 패배 → 패배 그림(유지)·떨림, 적에게 인식된 상태이거나 맞을 공격이 있을 때 대시·구르기를 씀 → 회피 그림들 차례로·으쓱,
        // 이동만으로 인식 범위를 벗어남 → 벗어남 그림·한숨. 그림이 비어 있으면 그 반응은 건너뛴다(진행에는 영향 없음).
        private void ChooseReaction()
        {
            if (playerView == null || session == null) return;
            if (session.Phase == BattlePhase.Lost) { StartReaction(playerView.DefeatedSprite != null ? new[] { playerView.DefeatedSprite } : null, true, PoseMotion.Shake); return; }
            bool skill = lastAction == PlayerAction.Dash || lastAction == PlayerAction.Roll;
            if (skill && (startedThreatened || startedRecognized))
            {
                var frames = new List<Sprite>(); foreach (var s in playerView.EvadedSprites) if (s != null) frames.Add(s);
                StartReaction(frames.ToArray(), false, PoseMotion.Proud);
            }
            else if (lastAction == PlayerAction.Move && startedRecognized && !AnyRecognizing())
                StartReaction(playerView.EscapedSprite != null ? new[] { playerView.EscapedSprite } : null, false, PoseMotion.Sigh);
        }
        private void StartReaction(Sprite[] frames, bool hold, PoseMotion motionKind)
        {
            if (frames == null || frames.Length == 0) return;
            reactionFrames = frames; reactionTime = 0; reactionHold = hold;
            if (poseSprite == null) { poseMotion = motionKind; poseEndMotion = PoseMotion.None; poseTime = 0; } // 대본 자세가 보이는 중이면 움직임은 그쪽을 따른다
            playerView.ReactionSprite = frames[0]; ShowPlayerVisual();
        }
        private void ClearReaction()
        {
            reactionPending = false; reactionFrames = null; reactionTime = 0; reactionHold = false;
            if (playerView != null) playerView.ReactionSprite = null;
        }
        // 반응 그림을 장마다 정한 시간씩 넘기고 끝나면 원래 그림으로 돌아간다. 패배 그림은 되돌리기·재시작까지 유지.
        private void TickReaction(float seconds)
        {
            if (reactionFrames == null || reactionHold || playerView == null) return;
            reactionTime += seconds;
            int index = Mathf.FloorToInt(reactionTime / playerView.ReactionFrameSeconds);
            var sprite = index < reactionFrames.Length ? reactionFrames[index] : null;
            if (sprite == null) reactionFrames = null;
            if (playerView.ReactionSprite != sprite) { playerView.ReactionSprite = sprite; ShowPlayerVisual(); }
        }
        /// <summary>표시 연출을 갱신한다(매 프레임): 적 인식 표시, 먼지·잔상, 적 손맛, 플레이어 손맛 자세. 충돌·패배 연출이 플레이어를 맡고 있으면 플레이어 자세는 건드리지 않는다.</summary>
        public void UpdateMotion(float seconds)
        {
            UpdateAlert(seconds);
            TickReaction(seconds);
            if (motion == null || session == null) return;
            motion.TickEffects(seconds);
            TickEnemyMotion(seconds);
            if (playerView == null) return;
            var bodyPose = motion.Tick(seconds, session, lastAction, lastDirection, playerView.Renderer);
            if (poseSprite != null || poseMotion != PoseMotion.None)
            {
                poseTime += seconds;
                if (poseSprite != null && poseDuration > 0 && poseTime >= poseDuration) EndPose();
                else
                {
                    var extra = PoseOffset();
                    bodyPose.Lift += extra.Lift; bodyPose.Shift += extra.Shift; bodyPose.Tilt += extra.Tilt;
                    bodyPose.Squash = Vector2.Scale(bodyPose.Squash, extra.Squash);
                    // 기본 그림으로 돌아온 뒤의 움직임은 한 번 끝나면 멈춘다.
                    if (poseSprite == null && poseTime >= MotionLength(poseMotion)) poseMotion = PoseMotion.None;
                }
            }
            playerView.SetPose(bodyPose, viewCamera);
        }
        private bool OwnsEnemy(int i) => feedback != null && feedback.Owns(i);
        private void TickEnemyMotion(float seconds) { if (motion != null && session != null) motion.TickEnemies(seconds, session, enemyViews, ownsEnemy ??= OwnsEnemy); }
        // 적 인식 표시는 판정이 확정된 입력 대기·승패 상태에서만 보인다(행동·연출 중에는 숨김). 충돌로 날아가는 적은 건너뛴다.
        private void UpdateAlert(float seconds)
        {
            if (alert == null || session == null) return;
            alert.Update(seconds, session, enemyViews, alertsEnabled && !session.IsBusy && !IsPresenting, ownsEnemy ??= OwnsEnemy);
        }
        /// <summary>행동을 시작한다. 입력 대기 중인데 갈 수 없는 방향이면 그쪽으로 부딪히는 반응만 보이고 false를 돌려준다(시간·판정 변화 없음).</summary>
        public bool TryActionOrBump(PlayerAction action, Vector2Int direction)
        {
            if (TryBeginAction(action, direction)) return true;
            if (session != null && session.Phase == BattlePhase.Waiting && !IsPresenting && action != PlayerAction.Wait) motion?.Bump(direction);
            return false;
        }
        /// <summary>대본 진행(튜토리얼 등)이 전투 입력을 제한할 때 쓰는 판정. 행동·방향을 받아 허용하면 true. null이면 제한 없음.
        /// 키보드 입력과 TryPlayerCommand가 이 판정을 거친다(테스트·연출용 TryBeginAction은 거치지 않는다).</summary>
        public Func<PlayerAction, Vector2Int, bool> CommandFilter { get; set; }
        /// <summary>false면 되돌리기·다시 시작(키·버튼)을 막고 HUD 버튼을 숨긴다.</summary>
        public bool AllowUndoRestart { get; set; } = true;
        /// <summary>false면 범위 표시 전환(키·버튼)을 막고 HUD 버튼을 숨긴다.</summary>
        public bool AllowRangeToggle { get; set; } = true;
        /// <summary>false면 적 인식 말풍선을 띄우지 않는다(대본 대사와 겹치지 않게). 다시 켜면 현재 인식 상태로 조용히 맞춘다.</summary>
        public bool AlertsEnabled { get => alertsEnabled; set { if (alertsEnabled == value) return; alertsEnabled = value; alert?.Resync(session); } }
        private bool alertsEnabled = true;
        /// <summary>제한 판정에 막힌 행동(대본이 경고를 띄우는 데 쓴다).</summary>
        public event Action<PlayerAction, Vector2Int> CommandRejected;
        /// <summary>플레이어 명령 하나를 실행한다(키 입력과 같은 경로). 제한에 막히면 false, 갈 수 없는 방향이면 부딪힘 반응 후 false.</summary>
        public bool TryPlayerCommand(PlayerAction action, Vector2Int direction)
        {
            if (CommandFilter != null && !CommandFilter(action, direction)) { CommandRejected?.Invoke(action, direction); return false; }
            return action == PlayerAction.Wait ? TryBeginAction(action, direction) : TryActionOrBump(action, direction);
        }
        /// <summary>화면 좌표가 가리키는 칸(맵 바닥 평면 기준). 맵 밖이거나 카메라가 없으면 false.</summary>
        public bool TryGetCell(Vector2 screenPoint, out Vector2Int cell)
        {
            cell = default;
            if (viewCamera == null || map == null) return false;
            var ray = viewCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(transform.up, transform.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            var local = transform.InverseTransformPoint(ray.GetPoint(distance));
            cell = new Vector2Int(Mathf.RoundToInt(local.x / cellSize), Mathf.RoundToInt(local.z / cellSize));
            return map.Contains(cell);
        }
        private Sprite poseSprite;
        private PoseMotion poseMotion, poseEndMotion;
        private bool poseUntilAction;
        private bool dangerIdleEnabled = true;
        /// <summary>false면 인식 범위 안 대기의 위험 그림을 자동으로 보이지 않는다(대본이 직접 정할 때). 회피·벗어남·패배 그림은 그대로.</summary>
        public bool DangerIdleEnabled { get => dangerIdleEnabled; set { if (dangerIdleEnabled == value) return; dangerIdleEnabled = value; ShowPlayerVisual(); } }
        private float poseTime, poseDuration;
        /// <summary>지금 보이는 자세 그림(없으면 null). 테스트·디버그용.</summary>
        public Sprite PoseSprite => poseSprite;
        /// <summary>플레이어 그림을 자세 그림으로 바꾸고 움직임을 준다(표시 전용). seconds가 0 이하면 EndPose까지 유지.</summary>
        /// <param name="untilAction">true면 다음 플레이어 행동을 시작할 때 끝낸다(seconds 무시).</param>
        public void PlayPose(Sprite sprite, PoseMotion motionKind, float seconds, PoseMotion endMotion = PoseMotion.None, bool untilAction = false)
        {
            poseUntilAction = untilAction; if (untilAction) seconds = 0;
            poseSprite = sprite; poseMotion = motionKind; poseEndMotion = endMotion; poseTime = 0; poseDuration = seconds;
            if (playerView != null) { playerView.OverrideSprite = sprite; ShowPlayerVisual(); }
        }
        /// <summary>자세 그림을 끝내고 기본 그림으로 돌아간다. PlayPose에 돌아올 때 움직임을 정했으면 그 움직임을 한 번 준다.</summary>
        public void EndPose()
        {
            if (poseSprite == null) return;
            poseSprite = null; poseTime = poseDuration = 0; poseMotion = poseEndMotion; poseEndMotion = PoseMotion.None; poseUntilAction = false;
            if (playerView != null) { playerView.OverrideSprite = null; ShowPlayerVisual(); }
        }
        // 자세 움직임(처음 0.5초 안팎): 펄쩍 뜀, 좌우 떨림, 한숨처럼 납작했다 펴짐, 으쓱하며 위로 늘어났다 돌아옴.
        /// <summary>지금 주고 있는 자세 움직임(없으면 None). 테스트·디버그용.</summary>
        public PoseMotion PoseMotionPlaying => poseMotion;
        private static float MotionLength(PoseMotion kind) => kind == PoseMotion.Hop ? .35f : kind == PoseMotion.Sigh ? .6f : kind == PoseMotion.None ? 0 : .5f;
        private MotionPose PoseOffset()
        {
            var pose = MotionPose.Identity;
            float t = poseTime;
            switch (poseMotion)
            {
                case PoseMotion.Hop: { float u = Mathf.Clamp01(t / .35f); pose.Lift = .3f * Mathf.Sin(Mathf.PI * u); break; }
                case PoseMotion.Shake: { float u = Mathf.Clamp01(t / .5f); pose.Shift = new Vector3(.06f * Mathf.Sin(2 * Mathf.PI * 7 * t) * (1 - u), 0, 0); break; }
                case PoseMotion.Sigh: { float u = Mathf.Clamp01(t / .6f); pose = MotionPoses.Squashed(.14f * Mathf.Sin(Mathf.PI * u)); break; }
                case PoseMotion.Proud: { float u = Mathf.Clamp01(t / .5f); pose = MotionPoses.Squashed(-.12f * Mathf.Sin(Mathf.PI * u)); pose.Lift = .08f * Mathf.Sin(Mathf.PI * u); break; }
            }
            return pose;
        }
        /// <summary>플레이어 외형 기준점(발밑). 전투가 없으면 null. 말풍선·감정 표시 위치용.</summary>
        public Transform PlayerAnchor => player;
        public float CellSize => cellSize;
        private readonly List<Transform> guideMarks = new List<Transform>();
        private Material guideMaterial;
        /// <summary>점멸하는 안내 칸(노란 테두리)을 표시한다. 빈 목록이면 지운다. 표시 전용.</summary>
        public void SetGuideCells(IEnumerable<Vector2Int> cells)
        {
            foreach (var mark in guideMarks) if (mark != null) Release(mark.gameObject);
            guideMarks.Clear();
            if (cells == null || generated == null) return;
            if (guideMaterial == null) guideMaterial = OverlayMaterial(new Color(1f, .78f, .1f));
            foreach (var cell in cells)
            {
                var mark = Outline("Guide Mark", generated, .94f, .07f, .44f, guideMaterial);
                mark.localPosition = LocalPosition(cell) + Vector3.up * .03f; guideMarks.Add(mark);
            }
        }
        public int GuideCellCount => guideMarks.Count;
        // 안내 칸은 0.5초마다 켜졌다 꺼진다(실시간 기준, 게임 시간 정지와 무관).
        private void BlinkGuides() { bool on = Mathf.Repeat(Time.unscaledTime, 1f) < .5f; foreach (var mark in guideMarks) if (mark != null) mark.gameObject.SetActive(on); }
        /// <summary>전투를 처음 상태로 다시 만든다(R 키·다시 시작 버튼).</summary>
        public void Restart() { ClearGenerated(); Build(); }
        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || session == null) return;
            float dt = Time.unscaledDeltaTime;
            if (session.IsBusy || IsPresenting) { AdvanceMovement(dt); UpdateMotion(dt); RefreshHud(); return; }
            UpdateMotion(dt);
            BlinkGuides();
            if (restartRequested) { Restart(); return; }
            if (input != null) ReadCommand();
            RefreshHud();
        }
        private void ReadCommand()
        {
            var command = input.Read();
            switch (command.Kind)
            {
                case BattleCommandKind.Restart: if (AllowUndoRestart) Restart(); break;
                case BattleCommandKind.ToggleRanges: if (AllowRangeToggle) ToggleEnemyRanges(); break;
                case BattleCommandKind.Undo: if (AllowUndoRestart) TryUndo(); break;
                case BattleCommandKind.Wait: TryPlayerCommand(PlayerAction.Wait, Vector2Int.zero); break;
                case BattleCommandKind.Roll: TryPlayerCommand(PlayerAction.Roll, command.Direction); break;
                case BattleCommandKind.Move: TryPlayerCommand(PlayerAction.Move, command.Direction); break;
                case BattleCommandKind.Dash: TryPlayerCommand(PlayerAction.Dash, command.Direction); break;
            }
        }
        // Game UI 설정이 없을 때만 쓰는 임시 표시(IMGUI). 정식 HUD가 있으면 그리지 않는다.
        private void OnGUI()
        {
            if (hud != null || Event.current.type != EventType.Repaint || !Application.IsPlaying(gameObject) || session == null) return;
            GUI.Box(new Rect(12, 12, 560, 96),
                $"{session.Phase} | 남은 적 {session.AliveCount} | 회피 {(session.EvasionLocked ? $"쿨타임 {session.EvasionCooldownTurns}" : "가능")}\n{controlsHint}\n행동 {session.UndoCount} | 범위 표시 {(showRanges ? "켬" : "끔")} (빨강=공격, 노랑=인식)\n칸 ({session.Position.x}, {session.Position.y})");
        }
        // 안내 문구는 현재 입력 에셋의 첫 번째 바인딩을 보여 준다(재바인딩 반영).
        private string ControlsHint()
        {
            if (input == null) return "";
            return UiText.BattleControls.Format(
                ("move", $"{input.Key(BattleInput.MoveUp)}{input.Key(BattleInput.MoveLeft)}{input.Key(BattleInput.MoveDown)}{input.Key(BattleInput.MoveRight)}"),
                ("dash", input.Key(BattleInput.DashModifier)),
                ("roll", $"{input.Key(BattleInput.RollUpLeft)}/{input.Key(BattleInput.RollUpRight)}/{input.Key(BattleInput.RollDownLeft)}/{input.Key(BattleInput.RollDownRight)}"),
                ("wait", input.Key(BattleInput.Wait)), ("undo", input.Key(BattleInput.Undo)), ("ranges", input.Key(BattleInput.ToggleRanges)), ("restart", input.Key(BattleInput.Restart)));
        }
        private void CreateHud()
        {
            var ui = playerSettings != null ? playerSettings.GameUi : null;
            var document = ui != null ? ui.CreateDocument("Battle HUD", ui.BattleHud, generated, 0) : null;
            if (document == null) return;
            hud = new BattleHud(document.rootVisualElement, controlsHint, input.Key(BattleInput.Undo), input.Key(BattleInput.Restart), input.Key(BattleInput.ToggleRanges),
                ui.EvasionIcon, ui.ShowDebugInfo, () => TryUndo(), () => restartRequested = true, ToggleEnemyRanges);
            alert?.AttachView(hud.Root.Q("bubbles"), ui.SpeechBubble); // 말풍선은 HUD 화면의 말풍선 층에 그린다
            RefreshHud();
        }
        private void RefreshHud() { if (hud != null && session != null) hud.Refresh(HudState()); }
        /// <summary>전투 HUD에 보일 현재 값. 미니맵에서 들어온 전투면 스테이지 이름, 아니면 맵 이름을 쓴다.</summary>
        public BattleHudState HudState()
        {
            var run = StageFlow.Current;
            return new BattleHudState
            {
                StageName = run != null && run.BattleScene == gameObject.scene.path ? run.DisplayName : map != null ? map.name : "",
                AliveEnemies = session.AliveCount, TotalEnemies = session.Enemies.Count, Actions = session.UndoCount,
                EvasionCooldown = session.EvasionCooldownTurns, Ready = !session.IsBusy && !IsPresenting, CanUndo = session.CanUndo,
                RangesOn = showRanges, Phase = session.Phase, Cell = session.Position,
                UndoRestartAllowed = AllowUndoRestart, RangesAllowed = AllowRangeToggle,
            };
        }
        public BattleHud Hud => hud;
        private void OnDisable() { ClearGenerated(); }
        private void OnDestroy() { ClearGenerated(); }
    }
}
