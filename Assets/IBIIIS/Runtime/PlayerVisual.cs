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
    [Serializable]
    public sealed class RollFrames { public Sprite first, second; }
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
        [SerializeField, Tooltip("표시 전용 발 위치 보정(칸 단위). 스프라이트 아래 여백만큼 그림이 떠 보일 때 양수로 하면 화면상 아래(카메라 기준)로 내립니다. 격자 위치·판정에는 영향이 없습니다.")]
        private float footOffset;
        // 프리팹에서 지정한 스프라이트 자식의 원래 위치. 발 보정은 이 위치에서 더한다.
        [NonSerialized] private bool hasBasePosition;
        [NonSerialized] private Vector3 basePosition;
        public float FootOffset => footOffset;
        private readonly HashSet<string> warned = new HashSet<string>();
        private PlayerFacing facing = PlayerFacing.Front;
        public PlayerFacing Facing => facing;
        public SpriteRenderer Renderer => spriteRenderer;
        public static PlayerFacing FacingOf(Vector2Int direction)
            => Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) ? (direction.x < 0 ? PlayerFacing.Left : PlayerFacing.Right) : (direction.y > 0 ? PlayerFacing.Back : PlayerFacing.Front);
        /// <summary>현재 행동에 맞는 스프라이트를 표시한다. moving=false면 대기 표시.</summary>
        public void Show(PlayerAction action, Vector2Int direction, float progress, bool moving, Camera camera)
        {
            if (action != PlayerAction.Wait && direction != Vector2Int.zero)
                facing = action == PlayerAction.Roll ? (direction.x < 0 ? PlayerFacing.Left : PlayerFacing.Right) : FacingOf(direction);
            var sprite = Select(action, direction, progress, moving, out var slot);
            if (sprite == null) Warn(slot);
            else if (spriteRenderer != null) spriteRenderer.sprite = sprite;
            if (spriteRenderer != null && camera != null) Face(camera);
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
        // 카메라를 향하게 돌린 뒤, 카메라 화면의 아래쪽 방향으로 footOffset만큼 옮긴다.
        private void Face(Camera camera)
        {
            var t = spriteRenderer.transform;
            if (!hasBasePosition) { basePosition = t.localPosition; hasBasePosition = true; }
            t.rotation = camera.transform.rotation;
            var down = t.parent != null ? t.parent.InverseTransformDirection(t.rotation * Vector3.down) : t.rotation * Vector3.down;
            t.localPosition = basePosition + down * footOffset;
        }
        private void Warn(string slot)
        {
            if (warned.Add(slot)) Debug.LogWarning($"[IBIIIS] {name}: PlayerVisual의 '{slot}' 스프라이트가 비어 있어 이전 표시를 유지합니다.", this);
        }
    }
}
