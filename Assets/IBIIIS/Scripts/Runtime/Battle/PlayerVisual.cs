using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>화면 기준 방향. 카메라 Y 회전 0 기준으로 격자 +Y=Back, +X=Right, -Y=Front, -X=Left.</summary>
    public enum PlayerFacing { Back, Right, Front, Left }
    [Serializable]
    public sealed class FacingSprites
    {
        public Sprite back, right, front, left;
        public Sprite Get(PlayerFacing facing) => facing == PlayerFacing.Back ? back : facing == PlayerFacing.Right ? right : facing == PlayerFacing.Front ? front : left;
    }
    /// <summary>표시 전용 손맛 자세. Lift=위로 뜬 높이(칸), Shift=맵 평면 기준 밀림(칸, x=맵 X, z=맵 Y), Squash=가로·세로 배율(발밑 기준),
    /// Tilt=화면 기준 기울기(도, 양수=반시계, 발밑 기준).</summary>
    public struct MotionPose
    {
        public float Lift, Tilt;
        public Vector3 Shift;
        public Vector2 Squash;
        public static MotionPose Identity => new MotionPose { Squash = Vector2.one };
        public bool IsIdentity => Lift == 0 && Tilt == 0 && Shift == Vector3.zero && Squash == Vector2.one;
    }
    [Serializable]
    public sealed class RollFrames { public Sprite first, second; }
    /// <summary>적 범위 표시 색. 전투 화면(Tab)과 맵 에디터가 같은 값을 쓴다.</summary>
    public static class EnemyRangeColors
    {
        public static readonly Color Recognition = new Color(.95f, .88f, .25f);
        public static readonly Color Attack = new Color(.95f, .2f, .2f);
    }
    /// <summary>플레이어 외형 프리팹에 붙이는 표시 전용 컴포넌트. 격자 위치·판정에는 관여하지 않습니다.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. 스프라이트를 표시할 렌더러. 카메라를 향해 회전하는 대상입니다.")] private SpriteRenderer spriteRenderer;
        [SerializeField, Tooltip("대기 중 표시. 마지막 이동 방향을 유지합니다.")] private FacingSprites idle = new FacingSprites();
        [SerializeField, Tooltip("한 칸 이동 중 표시")] private FacingSprites move = new FacingSprites();
        [SerializeField, Tooltip("두 칸 대시 중 표시")] private FacingSprites dash = new FacingSprites();
        [SerializeField, Tooltip("구르기 앞 절반에 First, 뒤 절반에 Second를 표시합니다.")] private RollFrames rollBackLeft = new RollFrames();
        [SerializeField] private RollFrames rollBackRight = new RollFrames();
        [SerializeField] private RollFrames rollFrontLeft = new RollFrames();
        [SerializeField] private RollFrames rollFrontRight = new RollFrames();
        [Header("상황별 그림(선택, 방향 무관)")]
        [SerializeField, Tooltip("선택. 입력 대기 중 적의 인식 범위 안에 있으면 대기 그림 대신 표시합니다. 비우면 일반 대기 그림.")] private Sprite danger;
        [SerializeField, Tooltip("선택. 대시·구르기로 적의 공격을 피했을 때 위에서부터 순서대로 표시합니다.")] private Sprite[] evaded = new Sprite[0];
        [SerializeField, Tooltip("선택. 대시·구르기 없이 한 칸 이동만으로 적의 인식 범위를 벗어났을 때 표시합니다.")] private Sprite escaped;
        [SerializeField, Tooltip("선택. 패배했을 때 표시합니다. 되돌리기·재시작 전까지 유지됩니다.")] private Sprite defeated;
        [SerializeField, Min(.05f), Tooltip("회피·벗어남 그림 한 장을 보이는 시간(초). 회피 그림이 여러 장이면 장마다 이 시간씩 보입니다.")] private float reactionFrameSeconds = .4f;
        [SerializeField, Tooltip("표시 전용 발 위치 보정(칸 단위). 스프라이트 아래 여백만큼 그림이 떠 보일 때 양수로 하면 화면상 아래(카메라 기준)로 내립니다. 격자 위치·판정에는 영향이 없습니다.")]
        private float footOffset;
        // 프리팹에서 지정한 스프라이트 자식의 원래 위치. 발 보정은 이 위치에서 더한다.
        [NonSerialized] private bool hasBasePosition;
        [NonSerialized] private Vector3 basePosition, baseScale;
        [NonSerialized] private MotionPose pose = MotionPose.Identity;
        [NonSerialized] private Camera lastCamera;
        public float FootOffset => footOffset;
        private readonly HashSet<string> warned = new HashSet<string>();
        private PlayerFacing facing = PlayerFacing.Front;
        public PlayerFacing Facing => facing;
        public SpriteRenderer Renderer => spriteRenderer;
        /// <summary>바라보는 방향만 바꾼다(되돌리기 등). 그림은 다음 Show에서 바뀐다.</summary>
        public void SetFacing(PlayerFacing value) => facing = value;
        public static PlayerFacing FacingOf(Vector2Int direction)
            => Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) ? (direction.x < 0 ? PlayerFacing.Left : PlayerFacing.Right) : (direction.y > 0 ? PlayerFacing.Back : PlayerFacing.Front);
        /// <summary>설정하면 행동·방향과 관계없이 이 그림을 보인다(대본의 자세 연출 등). 가장 우선한다. null이면 원래대로.</summary>
        public Sprite OverrideSprite { get; set; }
        /// <summary>전투 상황 반응 그림(회피·벗어남·패배). OverrideSprite 다음으로 우선한다. null이면 원래대로.</summary>
        public Sprite ReactionSprite { get; set; }
        /// <summary>true면 대기 중 Danger 그림을 보인다(적의 인식 범위 안).</summary>
        public bool InDanger { get; set; }
        public Sprite DangerSprite => danger;
        public IReadOnlyList<Sprite> EvadedSprites => evaded;
        public Sprite EscapedSprite => escaped;
        public Sprite DefeatedSprite => defeated;
        public float ReactionFrameSeconds => reactionFrameSeconds;
        /// <summary>현재 행동에 맞는 스프라이트를 표시한다. moving=false면 대기 표시.</summary>
        public void Show(PlayerAction action, Vector2Int direction, float progress, bool moving, Camera camera)
        {
            if (action != PlayerAction.Wait && direction != Vector2Int.zero)
                facing = action == PlayerAction.Roll ? (direction.x < 0 ? PlayerFacing.Left : PlayerFacing.Right) : FacingOf(direction);
            if (spriteRenderer == null) { Warn("Sprite Renderer"); return; }
            var special = OverrideSprite != null ? OverrideSprite : ReactionSprite != null ? ReactionSprite : !moving && InDanger ? danger : null;
            if (special != null) { spriteRenderer.sprite = special; if (camera != null) Face(camera); return; }
            var sprite = Select(action, direction, progress, moving);
            if (sprite == null) { Select(action, direction, progress, moving, out var slot); Warn(slot); } // 슬롯 이름은 경고할 때만 만든다
            else spriteRenderer.sprite = sprite;
            if (camera != null) Face(camera);
        }
        private Sprite Select(PlayerAction action, Vector2Int direction, float progress, bool moving)
        {
            if (moving && action == PlayerAction.Move) return move.Get(facing);
            if (moving && action == PlayerAction.Dash) return dash.Get(facing);
            if (moving && action == PlayerAction.Roll)
            {
                var frames = direction.y > 0 ? (direction.x > 0 ? rollBackRight : rollBackLeft) : (direction.x > 0 ? rollFrontRight : rollFrontLeft);
                return progress >= .5f ? frames.second : frames.first;
            }
            return idle.Get(facing);
        }
        public Sprite Select(PlayerAction action, Vector2Int direction, float progress, bool moving, out string slot)
        {
            if (moving && action == PlayerAction.Move) { slot = "Move " + facing; return move.Get(facing); }
            if (moving && action == PlayerAction.Dash) { slot = "Dash " + facing; return dash.Get(facing); }
            if (moving && action == PlayerAction.Roll)
            {
                bool back = direction.y > 0, right = direction.x > 0;
                var frames = back ? (right ? rollBackRight : rollBackLeft) : (right ? rollFrontRight : rollFrontLeft);
                bool second = progress >= .5f;
                slot = "Roll " + (back ? "Back" : "Front") + (right ? "Right " : "Left ") + (second ? "Second" : "First");
                return second ? frames.second : frames.first;
            }
            slot = "Idle " + facing; return idle.Get(facing);
        }
        public MotionPose Pose => pose;
        /// <summary>손맛 자세를 바꾸고 바로 적용한다. 자세는 다음에 바꿀 때까지 유지된다.</summary>
        public void SetPose(MotionPose value, Camera camera)
        {
            pose = value;
            if (spriteRenderer != null && (camera != null || lastCamera != null)) Face(camera != null ? camera : lastCamera);
        }
        // 카메라를 향하게 돌린 뒤, 카메라 화면의 아래쪽 방향으로 footOffset만큼 옮기고 손맛 자세(뜸·밀림·납작함)를 더한다.
        private void Face(Camera camera)
        {
            var t = spriteRenderer.transform; lastCamera = camera;
            if (!hasBasePosition) { basePosition = t.localPosition; baseScale = t.localScale; hasBasePosition = true; }
            t.rotation = CameraFacingSprite.RotationFor(camera) * Quaternion.Euler(0, 0, pose.Tilt);
            var screenDown = camera.transform.rotation * Vector3.down; // 기울기와 무관하게 화면 아래 방향으로 발 보정
            var down = t.parent != null ? t.parent.InverseTransformDirection(screenDown) : screenDown;
            var up = t.parent != null ? t.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            t.localPosition = basePosition + down * footOffset + up * pose.Lift + pose.Shift;
            t.localScale = new Vector3(baseScale.x * pose.Squash.x, baseScale.y * pose.Squash.y, baseScale.z);
        }
        private void Warn(string slot)
        {
            if (warned.Add(slot)) Debug.LogWarning(slot == "Sprite Renderer" ? $"[IBIIIS] {name}: PlayerVisual의 Sprite Renderer 슬롯이 비어 있어 그림을 표시할 수 없습니다." : $"[IBIIIS] {name}: PlayerVisual의 '{slot}' 스프라이트가 비어 있어 이전 표시를 유지합니다.", this);
        }
    }
}
