using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>근접 대화 구역(기획서 7.2·7.6, 대화 대상): 어드벤처씬에서 플레이어가 이 구역에 들어오면 미연시 대화창으로 대본을 보여 준다(그동안 이동을 막음).
    /// 구역은 이 오브젝트 위치를 가운데로 한 바닥 평면(XZ)의 사각형이며 Collider와 무관하다. 구역 밖에서 안으로 들어올 때만 시작한다.
    /// Zone Id가 대화 그래프(DialogueProgress.Graph)의 오브젝트 ID이며, 그래프의 대본 순서에서 지금 대본을 고르되 그 대본은 이 구역에서 처음 들어갈 때 한 번만 보여 준다
    /// (구역 기록은 클리어 기록 파일의 zoneShown). 새 대본이 열리면 다음에 들어올 때 그 대본이 한 번 나온다.</summary>
    public sealed class DialogueZone : MonoBehaviour
    {
        private static readonly List<DialogueZone> active = new List<DialogueZone>();
        [SerializeField, Tooltip("필수. 이 구역의 고유 ID(대화 그래프의 오브젝트 ID, 구역 기록의 키). 어드벤처씬 전체에서 겹치면 안 되며 정한 뒤 바꾸지 않습니다.")] private string zoneId;
        [SerializeField, Tooltip("구역 크기(칸). X = 좌우, Y = 앞뒤(바닥 평면). 이 오브젝트 위치가 가운데입니다.")] private Vector2 size = new Vector2(3, 3);
        private bool inside, playing;
        public static IReadOnlyList<DialogueZone> Active => active;
        public string ZoneId => zoneId;
        public Vector2 Size => new Vector2(Mathf.Max(.1f, size.x), Mathf.Max(.1f, size.y));
        public bool Playing => playing;
        /// <summary>대화 그래프에 이 구역(Zone Id)이 있으면 true.</summary>
        public bool UsesGraph => !string.IsNullOrEmpty(zoneId) && DialogueProgress.Graph != null && DialogueProgress.Graph.HasObject(zoneId);
        /// <summary>들어오면 보여 줄 대본(지금 대본이 이 구역에서 이미 나왔으면 null).</summary>
        public ScenarioScript PendingScript => UsesGraph ? DialogueProgress.ZoneCurrent(DialogueProgress.Graph, zoneId) : null;
        /// <summary>설정 오류. 정상이면 null.</summary>
        public string Problem => string.IsNullOrWhiteSpace(zoneId) ? "Zone Id가 비어 있습니다." : DialogueProgress.Graph == null ? "대화 그래프가 없습니다(Overworld Settings의 Dialogue Graph)." : !UsesGraph ? $"대화 그래프에 Zone Id '{zoneId}'가 없습니다." : null;
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); inside = false; }
        private void OnDisable() { active.Remove(this); }
        private void Start() { if (Problem != null) Debug.LogError($"[IBIIIS] 근접 대화 구역 '{name}': {Problem} 이 구역은 대화를 시작하지 않습니다.", this); }
        public bool Contains(Vector3 position)
        {
            var local = position - transform.position; var half = Size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y;
        }
        /// <summary>플레이어 위치로 구역 출입을 갱신하고, 이번에 새로 들어온 구역이 보여 줄 대본이 있으면 돌려준다(없으면 null).</summary>
        public static DialogueZone UpdateInside(Vector3 position)
        {
            DialogueZone entered = null;
            foreach (var zone in active)
            {
                if (zone == null || !zone.isActiveAndEnabled) continue;
                bool now = zone.Contains(position);
                if (now && !zone.inside && entered == null && !zone.playing && zone.PendingScript != null) entered = zone;
                zone.inside = now;
            }
            return entered;
        }
        /// <summary>지금 대본을 보여 준다(플레이어 이동을 막고, 끝나면 풀며 이 구역에서 나온 대본으로 기록한다). 시작했으면 true.</summary>
        public bool Play(OverworldPlayer player)
        {
            var script = playing ? null : PendingScript;
            if (script == null) return false;
            playing = true;
            var shown = AdventureDialogue.PlayTalk(script, player, () => { playing = false; MarkShown(script); });
            if (shown == null) { playing = false; MarkShown(script); return false; } // 보여 줄 대사가 없음
            return true;
        }
        private void MarkShown(ScenarioScript script)
        {
            try { ProgressStore.MarkZoneShown(zoneId, script.TalkSeenId); }
            catch (System.Exception e) { Debug.LogException(e); }
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
