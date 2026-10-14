using UnityEngine;

namespace IBIIIS
{
    /// <summary>충격 이펙트 한 번(표시 전용): 생성된 뒤 Time 동안 커지며 흐려지고 카메라를 향하다가 스스로 사라진다(어드벤처씬 적 접촉 등).
    /// SpriteRenderer가 있는 프리팹(예: 전투 충돌의 CollisionBurst)에 붙여 쓴다.</summary>
    public sealed class ImpactPop : MonoBehaviour
    {
        private float time, duration = .3f, scale = 1.2f;
        private SpriteRenderer[] renderers;
        private Color[] colors;
        public float Elapsed => time;
        /// <summary>prefab을 position에 만들어 seconds 동안 maxScale(칸)까지 커지며 흐려지게 한다. prefab이 없으면 null.</summary>
        public static ImpactPop Spawn(GameObject prefab, Vector3 position, float maxScale, float seconds)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, position, Quaternion.identity);
            go.name = "Contact Impact";
            var pop = go.AddComponent<ImpactPop>();
            pop.scale = Mathf.Max(0, maxScale); pop.duration = Mathf.Max(.01f, seconds);
            pop.renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            pop.colors = System.Array.ConvertAll(pop.renderers, r => r.color);
            pop.Tick(0);
            return pop;
        }
        private void Update() => Tick(Time.deltaTime);
        /// <summary>진행한다(테스트에서는 직접 호출). 끝나면 오브젝트를 지우고 false.</summary>
        public bool Tick(float seconds)
        {
            time += seconds;
            float v = Mathf.Clamp01(time / duration);
            if (v >= 1) { RuntimeObjects.Release(gameObject); return false; }
            transform.localScale = Vector3.one * scale * Mathf.Lerp(.45f, 1, 1 - (1 - v) * (1 - v));
            var camera = Camera.main; if (camera != null) transform.rotation = camera.transform.rotation;
            for (int i = 0; i < renderers.Length; i++) { var c = colors[i]; renderers[i].color = new Color(c.r, c.g, c.b, c.a * (1 - v * v)); }
            return true;
        }
    }
}
