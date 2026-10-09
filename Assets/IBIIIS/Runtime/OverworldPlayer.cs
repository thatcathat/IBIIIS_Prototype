using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵(Overworld)에서 자유 이동하는 플레이어. 격자 전투(GridSession)와 무관하며 게임 시간을 멈추지 않는다.
    /// 막힘은 CharacterController와 장애물의 Collider로 처리하고, 상호작용 대상은 바닥 평면 거리로 고른다.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class OverworldPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. 이동 속도·카메라·입력 설정")] private OverworldSettings settings;
        [SerializeField, Tooltip("선택. 스프라이트가 바라볼 카메라. 비우면 Main Camera를 사용합니다.")] private Camera viewCamera;
        private CharacterController controller;
        private PlayerVisual visual;
        private OverworldInput input;
        private OverworldInteractable target;
        private bool frozen;
        // 걷기 손맛(표시 전용)과 먼지·발소리를 둘 월드 기준 부모
        private OverworldMotion motion;
        private Transform effectsRoot;
        // 구르기 진행(이동량·회복·쿨다운). 진행·회복 중에는 걷기·상호작용 입력을 받지 않는다.
        private readonly OverworldEvade evade = new OverworldEvade();
        // 바라보는 방향의 유일한 기준. 외형(PlayerVisual)이 없어도 구르기 방향·전투 복귀 방향이 맞도록 여기서 갱신한다.
        private PlayerFacing facing = PlayerFacing.Front;
        // 안내 문구의 키 이름(입력을 켤 때 한 번 만든다).
        private string controlsHint = "", interactKey = "";
        public OverworldSettings Settings => settings;
        public PlayerFacing Facing => facing;
        public OverworldInteractable Target => target;
        /// <summary>이번 프레임에 실제로 걸었으면 true(입력이 있어도 막혀 거의 못 움직이면 false).</summary>
        public bool IsMoving { get; private set; }
        /// <summary>대시 키를 누른 채 달리고 있으면 true.</summary>
        public bool IsRunning { get; private set; }
        public OverworldMotion Motion => motion;
        public OverworldEvade Evade => evade;
        private Camera ViewCamera => viewCamera != null ? viewCamera : Camera.main;
        /// <summary>이동 입력을 스프라이트 방향으로 바꾼다. 좌우 성분이 있으면(대각선 포함) 옆모습, 위만 누르면 뒷모습, 아래만 누르면 앞모습.
        /// 반환값은 PlayerVisual이 쓰는 격자 방향(+Y=뒤, -Y=앞, ±X=옆). 입력이 없으면 zero.</summary>
        public static Vector2Int DirectionOf(Vector2 move, float deadZone = .1f)
        {
            int x = Mathf.Abs(move.x) >= deadZone ? (move.x > 0 ? 1 : -1) : 0;
            int y = Mathf.Abs(move.y) >= deadZone ? (move.y > 0 ? 1 : -1) : 0;
            return x != 0 ? new Vector2Int(x, 0) : new Vector2Int(0, y);
        }
        /// <summary>구르기 방향: 이동 입력이 있으면 그 방향(대각선 포함), 없으면 바라보는 방향. 바닥 평면의 단위 벡터.</summary>
        public static Vector3 EvadeDirection(Vector2 move, PlayerFacing facing, float deadZone = .1f)
        {
            if (move.magnitude >= deadZone) return new Vector3(move.x, 0, move.y).normalized;
            var d = DirectionOf(facing); return new Vector3(d.x, 0, d.y);
        }
        /// <summary>구르기 그림 방향(대각선 넷 중 하나). 좌우 성분이 없으면 바라보는 쪽(왼쪽이 아니면 오른쪽), 앞뒤 성분이 없으면 앞.</summary>
        public static Vector2Int RollSpriteDirection(Vector3 direction, PlayerFacing facing)
        {
            int x = Mathf.Abs(direction.x) > .1f ? (direction.x > 0 ? 1 : -1) : facing == PlayerFacing.Left ? -1 : 1;
            int y = Mathf.Abs(direction.z) > .1f ? (direction.z > 0 ? 1 : -1) : -1;
            return new Vector2Int(x, y);
        }
        private static Vector2Int DirectionOf(PlayerFacing value)
            => value == PlayerFacing.Back ? Vector2Int.up : value == PlayerFacing.Right ? Vector2Int.right : value == PlayerFacing.Left ? Vector2Int.left : Vector2Int.down;
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            visual = GetComponentInChildren<PlayerVisual>(true);
            if (visual == null) Debug.LogWarning($"[IBIIIS] {name}: 자식에 PlayerVisual이 없어 스프라이트 방향·손맛 자세를 표시하지 않습니다(이동·먼지·발소리는 그대로).", this);
            if (settings == null) Debug.LogWarning($"[IBIIIS] {name}: Overworld Settings가 없어 기본 이동 속도·키 배치를 사용합니다.", this);
            input = new OverworldInput(settings != null ? settings.InputActions : null); input.Enable();
            interactKey = input.Key(OverworldInput.Interact);
            controlsHint = $"미니맵 | 이동 {input.Key(OverworldInput.Move)} | 달리기 {input.Key(OverworldInput.Dash)}(누르고 있기) | 구르기 {input.Key(OverworldInput.Roll)} | 상호작용 {interactKey}";
            effectsRoot = new GameObject("Overworld Motion Effects").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(effectsRoot.gameObject, gameObject.scene);
            motion = new OverworldMotion(settings != null ? settings.MotionFeedback : null, settings, effectsRoot, ViewCamera);
        }
        private void Start()
        {
            // 미니맵은 전투의 시간 정지(MovementWorldTime)를 쓰지 않는다. 전투 씬이 정리되며 복구했어야 할 시간이 멈춰 있으면 되살린다.
            if (Time.timeScale == 0 && !MovementWorldTime.HasOwners)
            {
                Time.timeScale = 1;
                Debug.LogWarning("[IBIIIS] 미니맵 시작 시 게임 시간이 멈춰 있어 1로 되돌렸습니다.");
            }
            if (StageFlow.TakeReturnPoint(gameObject.scene.path, out var position, out var returnFacing)) Teleport(position, returnFacing);
            else ShowVisual(Vector2Int.zero, false);
        }
        /// <summary>위치와 바라보는 방향을 바로 바꾼다(전투에서 돌아올 때).</summary>
        public void Teleport(Vector3 position, PlayerFacing value)
        {
            controller.enabled = false; transform.position = position; controller.enabled = true;
            facing = value; motion?.Reset(); evade.Reset();
            if (visual != null) { visual.Show(PlayerAction.Move, DirectionOf(value), 0, false, ViewCamera); visual.SetPose(MotionPose.Identity, ViewCamera); }
        }
        /// <summary>입력을 멈춘다(전투 씬으로 넘어가는 동안 중복 입력 방지).</summary>
        public void Freeze()
        {
            frozen = true; IsMoving = IsRunning = false; evade.Reset(); ShowVisual(Vector2Int.zero, false);
            if (visual != null) visual.SetPose(MotionPose.Identity, ViewCamera);
        }
        private void Update()
        {
            if (frozen || input == null) return;
            if (evade.Recovering) { UpdateRecovery(); return; }
            var move = input.ReadMove();
            if (UpdateEvade(move)) { UpdateTarget(); return; }
            var direction = DirectionOf(move);
            var intended = Vector3.zero; var before = transform.position;
            // 대시 키를 누르고 있는 동안 달린다(홀드식).
            bool running = direction != Vector2Int.zero && input.DashHeld();
            if (direction != Vector2Int.zero)
            {
                float speed = running ? (settings != null ? settings.RunSpeed : OverworldSettings.DefaultRunSpeed) : (settings != null ? settings.MoveSpeed : OverworldSettings.DefaultMoveSpeed);
                intended = new Vector3(move.x, 0, move.y) * (speed * Time.deltaTime);
                controller.Move(intended);
            }
            var actual = transform.position - before; actual.y = 0;
            float minMove = OverworldMotion.MinMoveFor(controller);
            IsMoving = direction != Vector2Int.zero && !OverworldMotion.IsBlocked(intended, actual, minMove);
            IsRunning = running && IsMoving;
            ShowVisual(direction, IsMoving, IsRunning);
            // 손맛(먼지·발소리 포함)은 외형이 없어도 진행하고, 자세 적용만 외형이 있을 때 한다.
            var pose = motion.Tick(Time.deltaTime, transform.position, intended, actual, IsRunning, minMove);
            if (visual != null) visual.SetPose(pose, ViewCamera);
            UpdateTarget();
            if (target != null && input.InteractPressed()) target.Interact(this);
        }
        // 구르기(Space)를 시작하거나 진행한다. 진행 중이었으면 true(이번 프레임의 걷기·상호작용은 하지 않음).
        private bool UpdateEvade(Vector2 move)
        {
            float dt = Time.deltaTime;
            if (!evade.Active)
            {
                evade.TickCooldown(dt);
                if (!input.RollPressed() || !evade.CanStart) return false;
                float distance = settings != null ? settings.RollDistance : OverworldSettings.DefaultRollDistance, duration = settings != null ? settings.RollDuration : OverworldSettings.DefaultRollDuration;
                float easeOut = settings != null ? settings.RollEaseOut : OverworldSettings.DefaultRollEaseOut, recovery = settings != null ? settings.RollRecovery : OverworldSettings.DefaultRollRecovery;
                float cooldown = settings != null ? settings.EvadeCooldown : OverworldSettings.DefaultEvadeCooldown;
                if (!evade.TryStart(EvadeDirection(move, Facing), distance, duration, cooldown, easeOut, recovery)) return false;
                IsRunning = false;
                motion.BeginRoll();
            }
            var intended = evade.Advance(dt); var before = transform.position;
            controller.Move(intended);
            var actual = transform.position - before; actual.y = 0;
            IsMoving = true;
            var rollSprite = RollSpriteDirection(evade.Direction, facing);
            facing = rollSprite.x < 0 ? PlayerFacing.Left : PlayerFacing.Right; // 구르기 그림은 좌우 옆모습 기준
            var pose = motion.TickRoll(dt, evade.Progress);
            if (visual != null)
            {
                visual.Show(PlayerAction.Roll, rollSprite, evade.Progress, true, ViewCamera);
                visual.SetPose(pose, ViewCamera);
            }
            // 다 나아갔거나 벽·NPC에 막히면 끝낸다. 감속 끝무렵처럼 CharacterController가 무시하는 아주 작은 이동은 막힘으로 보지 않는다.
            if (evade.ReachedEnd || OverworldMotion.IsBlocked(intended, actual, OverworldMotion.MinMoveFor(controller)))
            {
                motion.EndRoll(transform.position);
                evade.Finish();
            }
            return true;
        }
        // 구르기 뒤 일어나는 중: 입력을 모두 무시하고 제자리에서 착지 납작함만 보인다.
        private void UpdateRecovery()
        {
            evade.TickCooldown(Time.deltaTime);
            IsMoving = IsRunning = false;
            ShowVisual(Vector2Int.zero, false);
            var pose = motion.Tick(Time.deltaTime, transform.position, Vector3.zero, Vector3.zero);
            if (visual != null) visual.SetPose(pose, ViewCamera);
            UpdateTarget();
        }
        private void ShowVisual(Vector2Int direction, bool moving, bool running = false)
        {
            // 막혀서 못 움직여도 누른 방향은 바라본다(그림은 대기). 달릴 때는 대시 그림.
            if (direction != Vector2Int.zero) facing = PlayerVisual.FacingOf(direction);
            if (visual == null) return;
            if (direction != Vector2Int.zero) visual.Show(running ? PlayerAction.Dash : PlayerAction.Move, direction, 0, moving, ViewCamera);
            else visual.Show(PlayerAction.Wait, Vector2Int.zero, 0, false, ViewCamera);
        }
        private void UpdateTarget()
        {
            var next = OverworldInteractable.FindNearest(transform.position, OverworldInteractable.Active);
            if (next == target) return;
            if (target != null) target.OnLeft();
            target = next;
        }
        private void OnGUI()
        {
            if (input == null) return;
            if (Event.current.type != EventType.Repaint) return;
            GUI.Box(new Rect(12, 12, 560, 30), $"{controlsHint} | 클리어 {ProgressStore.ClearedStages.Count}");
            if (frozen || target == null || !OverworldGui.ToGui(ViewCamera, target.LabelPosition, out var anchor)) return;
            var prompt = target.PromptText;
            if (prompt == null) return;
            var label = $"[{interactKey}] {prompt}";
            // 말풍선은 기준점 위, 안내 문구는 기준점 아래에 그려 겹치지 않게 한다.
            var content = new GUIContent(label); var size = OverworldGui.Prompt.CalcSize(content);
            GUI.Box(new Rect(anchor.x - size.x / 2, anchor.y + 6, size.x, size.y), content, OverworldGui.Prompt);
        }
        private void OnDestroy()
        {
            if (target != null) target.OnLeft();
            input?.Dispose(); input = null;
            motion?.Dispose(); motion = null;
            if (effectsRoot != null) Destroy(effectsRoot.gameObject);
        }
    }
}
