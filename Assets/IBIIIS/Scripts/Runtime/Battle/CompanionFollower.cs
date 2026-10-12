using UnityEngine;

namespace IBIIIS
{
    /// <summary>전투에서 플레이어 옆을 따라다니는 동반 캐릭터(예: 어시파리, 표시 전용). 플레이어 외형 기준점에서 화면 기준 오른쪽 위에 떠서 살짝 위아래로 흔들린다.
    /// 판정·점유와 무관하며, 플레이어가 움직이면 부드럽게 따라간다.</summary>
    public sealed class CompanionFollower : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. 따라갈 전투(Grid Map Player)의 플레이어.")] private GridMapPlayer battle;
        [SerializeField, Tooltip("플레이어 발밑 기준 자리(칸). x = 화면 오른쪽, y = 화면 위쪽. 임시 (0.55, 0.9).")] private Vector2 offset = new Vector2(.55f, .9f);
        [SerializeField, Min(0), Tooltip("위아래로 흔들리는 폭(칸). 임시 0.06.")] private float bobAmount = .06f;
        [SerializeField, Min(.1f), Tooltip("흔들림 주기(초). 임시 1.2.")] private float bobPeriod = 1.2f;
        [SerializeField, Min(0), Tooltip("따라가는 빠르기. 0이면 바로 붙습니다. 임시 12.")] private float followSharpness = 12;
        private bool placed;
        public void Configure(GridMapPlayer value) { battle = value; placed = false; }
        private void OnEnable() { placed = false; }
        private void LateUpdate()
        {
            if (battle == null || battle.PlayerAnchor == null) return;
            var camera = battle.ViewCamera;
            Vector3 right = camera != null ? camera.transform.right : Vector3.right, up = camera != null ? camera.transform.up : Vector3.up;
            float bob = bobAmount * Mathf.Sin(2 * Mathf.PI * Time.unscaledTime / bobPeriod);
            var goal = battle.PlayerAnchor.position + (right * offset.x + up * (offset.y + bob)) * battle.CellSize;
            transform.position = !placed || followSharpness <= 0 ? goal : Vector3.Lerp(transform.position, goal, 1 - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime));
            placed = true;
            if (camera != null) CameraFacingSprite.Face(transform, camera);
        }
    }
}
