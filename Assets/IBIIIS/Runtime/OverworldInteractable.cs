using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵에서 플레이어가 가까이 가서 상호작용 키로 반응시키는 대상(NPC·스테이지 입구 등).
    /// 범위는 바닥 평면(XZ)의 거리로 판정하며 Collider와 무관하다.</summary>
    public abstract class OverworldInteractable : MonoBehaviour
    {
        private static readonly List<OverworldInteractable> active = new List<OverworldInteractable>();
        [SerializeField, Min(.1f), Tooltip("상호작용 가능 거리(칸). 플레이어 발밑과 이 오브젝트 위치의 바닥 평면 거리로 판정합니다.")]
        private float radius = 1.5f;
        [SerializeField, Tooltip("안내 문구·말풍선을 띄울 높이(칸). 오브젝트 위치 기준.")] private float labelHeight = 2f;
        public float Radius => Mathf.Max(.1f, radius);
        public Vector3 LabelPosition => transform.position + Vector3.up * labelHeight;
        public static IReadOnlyList<OverworldInteractable> Active => active;
        /// <summary>안내 문구에 쓸 행동 이름(예: "말 걸기"). null이면 지금은 상호작용할 수 없다.</summary>
        public abstract string PromptVerb { get; }
        public abstract void Interact(OverworldPlayer player);
        /// <summary>플레이어가 범위를 벗어났을 때(말풍선 닫기 등).</summary>
        public virtual void OnLeft() { }
        protected virtual void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        protected virtual void OnDisable() { active.Remove(this); OnLeft(); }
        public static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        public bool InRange(Vector3 position) => FlatDistance(position, transform.position) <= Radius;
        /// <summary>범위 안에 있고 지금 상호작용할 수 있는 대상 중 가장 가까운 것. 없으면 null.</summary>
        public static OverworldInteractable FindNearest(Vector3 position, IEnumerable<OverworldInteractable> candidates)
        {
            OverworldInteractable best = null; float bestDistance = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy || candidate.PromptVerb == null || !candidate.InRange(position)) continue;
                float distance = FlatDistance(position, candidate.transform.position);
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }
    }
}
