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
        private PlayerFacing facing = PlayerFacing.Front;
        public OverworldSettings Settings => settings;
        public PlayerFacing Facing => visual != null ? visual.Facing : facing;
        public OverworldInteractable Target => target;
        public bool IsMoving { get; private set; }
        private Camera ViewCamera => viewCamera != null ? viewCamera : Camera.main;
        /// <summary>이동 입력을 스프라이트 방향으로 바꾼다. 좌우 성분이 있으면(대각선 포함) 옆모습, 위만 누르면 뒷모습, 아래만 누르면 앞모습.
        /// 반환값은 PlayerVisual이 쓰는 격자 방향(+Y=뒤, -Y=앞, ±X=옆). 입력이 없으면 zero.</summary>
        public static Vector2Int DirectionOf(Vector2 move, float deadZone = .1f)
        {
            int x = Mathf.Abs(move.x) >= deadZone ? (move.x > 0 ? 1 : -1) : 0;
            int y = Mathf.Abs(move.y) >= deadZone ? (move.y > 0 ? 1 : -1) : 0;
            return x != 0 ? new Vector2Int(x, 0) : new Vector2Int(0, y);
        }
        private static Vector2Int DirectionOf(PlayerFacing value)
            => value == PlayerFacing.Back ? Vector2Int.up : value == PlayerFacing.Right ? Vector2Int.right : value == PlayerFacing.Left ? Vector2Int.left : Vector2Int.down;
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            visual = GetComponentInChildren<PlayerVisual>(true);
            if (visual == null) Debug.LogWarning($"[IBIIIS] {name}: 자식에 PlayerVisual이 없어 스프라이트 방향을 바꾸지 않습니다.", this);
            if (settings == null) Debug.LogWarning($"[IBIIIS] {name}: Overworld Settings가 없어 기본 이동 속도·키 배치를 사용합니다.", this);
            input = new OverworldInput(settings != null ? settings.InputActions : null); input.Enable();
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
            facing = value;
            if (visual != null) visual.Show(PlayerAction.Move, DirectionOf(value), 0, false, ViewCamera);
        }
        /// <summary>입력을 멈춘다(전투 씬으로 넘어가는 동안 중복 입력 방지).</summary>
        public void Freeze() { frozen = true; IsMoving = false; ShowVisual(Vector2Int.zero, false); }
        private void Update()
        {
            if (frozen || input == null) return;
            var move = input.ReadMove();
            var direction = DirectionOf(move);
            IsMoving = direction != Vector2Int.zero;
            if (IsMoving)
            {
                float speed = settings != null ? settings.MoveSpeed : 3.5f;
                controller.Move(new Vector3(move.x, 0, move.y) * (speed * Time.deltaTime));
            }
            ShowVisual(direction, IsMoving);
            UpdateTarget();
            if (target != null && input.InteractPressed()) target.Interact(this);
        }
        private void ShowVisual(Vector2Int direction, bool moving)
        {
            if (visual == null) return;
            if (moving) { visual.Show(PlayerAction.Move, direction, 0, true, ViewCamera); facing = visual.Facing; }
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
            GUI.Box(new Rect(12, 12, 420, 30), $"미니맵 | 이동 {input.Key(OverworldInput.Move)} | 상호작용 {input.Key(OverworldInput.Interact)} | 클리어 {ProgressStore.ClearedStages.Count}");
            if (frozen || target == null || target.PromptVerb == null || !OverworldGui.ToGui(ViewCamera, target.LabelPosition, out var anchor)) return;
            var label = target is StageEntrance stage ? $"[{input.Key(OverworldInput.Interact)}] {stage.DisplayName} {target.PromptVerb}" : $"[{input.Key(OverworldInput.Interact)}] {target.PromptVerb}";
            // 말풍선은 기준점 위, 안내 문구는 기준점 아래에 그려 겹치지 않게 한다.
            var content = new GUIContent(label); var size = OverworldGui.Prompt.CalcSize(content);
            GUI.Box(new Rect(anchor.x - size.x / 2, anchor.y + 6, size.x, size.y), content, OverworldGui.Prompt);
        }
        private void OnDestroy() { if (target != null) target.OnLeft(); input?.Dispose(); input = null; }
    }
}
