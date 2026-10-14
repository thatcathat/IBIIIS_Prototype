using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>근접 대화(기획서 7.2): 어드벤처씬에서 플레이어가 이 구역에 들어오면 미연시 대화창으로 대화를 보여 준다(그동안 이동을 막음).
    /// 구역은 이 오브젝트 위치를 가운데로 한 바닥 평면(XZ)의 사각형이며 Collider와 무관하다. 구역 밖에서 안으로 들어올 때만 시작하고,
    /// '한 번만'이면 끝까지 본 뒤로는 다시 시작하지 않는다(본 기록은 Zone Id로 클리어 기록 파일에 남는다).</summary>
    public sealed class DialogueZone : MonoBehaviour
    {
        private static readonly List<DialogueZone> active = new List<DialogueZone>();
        [SerializeField, Tooltip("필수('한 번만'일 때). 본 기록에 쓰는 고유 ID. 어드벤처씬 전체에서 겹치면 안 되며 정한 뒤 바꾸지 않습니다.")] private string zoneId;
        [SerializeField, Tooltip("필수. 보여 줄 대화(Dialogue Script). 인물·초상화·대사를 대화 에셋에서 편집합니다.")] private DialogueScript dialogue;
        [SerializeField, Tooltip("구역 크기(칸). X = 좌우, Y = 앞뒤(바닥 평면). 이 오브젝트 위치가 가운데입니다.")] private Vector2 size = new Vector2(3, 3);
        [SerializeField, Tooltip("체크하면 끝까지 본 뒤로는 다시 시작하지 않습니다(다시 실행해도). 끄면 구역에 새로 들어올 때마다 시작합니다.")] private bool onlyOnce = true;
        private bool inside, playing;
        public static IReadOnlyList<DialogueZone> Active => active;
        public string ZoneId => zoneId;
        public DialogueScript Dialogue => dialogue;
        public Vector2 Size => new Vector2(Mathf.Max(.1f, size.x), Mathf.Max(.1f, size.y));
        public bool OnlyOnce => onlyOnce;
        /// <summary>본 기록 ID(클리어 기록 파일의 본 대본 목록에 '구역:Zone Id'로 남는다).</summary>
        public string SeenId => "zone:" + zoneId;
        public bool Seen => onlyOnce && !string.IsNullOrEmpty(zoneId) && ProgressStore.IsSeen(SeenId);
        public bool Playing => playing;
        /// <summary>설정 오류. 정상이면 null.</summary>
        public string Problem => dialogue == null ? "대화(Dialogue)가 비어 있습니다." : onlyOnce && string.IsNullOrWhiteSpace(zoneId) ? "'한 번만'에는 Zone Id가 필요합니다." : null;
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); inside = false; }
        private void OnDisable() { active.Remove(this); }
        private void Start() { if (Problem != null) Debug.LogError($"[IBIIIS] 근접 대화 구역 '{name}': {Problem} 이 구역은 대화를 시작하지 않습니다.", this); }
        public bool Contains(Vector3 position)
        {
            var local = position - transform.position; var half = Size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y;
        }
        /// <summary>플레이어 위치로 구역 출입을 갱신하고, 이번에 새로 들어온 구역이 대화를 시작할 수 있으면 돌려준다(없으면 null).</summary>
        public static DialogueZone UpdateInside(Vector3 position)
        {
            DialogueZone entered = null;
            foreach (var zone in active)
            {
                if (zone == null || !zone.isActiveAndEnabled) continue;
                bool now = zone.Contains(position);
                if (now && !zone.inside && entered == null && !zone.playing && zone.Problem == null && !zone.Seen) entered = zone;
                zone.inside = now;
            }
            return entered;
        }
        /// <summary>대화를 시작한다(플레이어 이동을 막고, 끝나면 풀며 본 기록을 남긴다). 시작했으면 true.</summary>
        public bool Play(OverworldPlayer player)
        {
            if (playing || Problem != null || Seen) return false;
            var lines = dialogue.LocalizedLines();
            if (lines.Length == 0) return false;
            playing = true;
            player?.SetInputLocked(true);
            var ui = player != null && player.Settings != null ? player.Settings.GameUi : null;
            DialogueController.Play(ui, lines, () =>
            {
                playing = false;
                if (onlyOnce)
                {
                    try { ProgressStore.MarkSeen(SeenId); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                if (player != null) player.SetInputLocked(false);
            });
            return true;
        }
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(.35f, .75f, 1f, .9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * .05f, new Vector3(Size.x, .1f, Size.y));
        }
#endif
    }
}
