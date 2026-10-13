using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>충돌 연출을 재생한다. 적끼리 충돌: 맞닿음 → 멈춤 → 충격·흔들림·효과음 → 튕겨 날아가며 퇴장.
    /// 플레이어 피격(패배): 때린 적이 몸을 내밂 → 충격·흔들림·효과음 → 플레이어가 적 반대쪽으로 조금 밀림(날아가지 않음).
    /// GridSession의 기록을 받아 표시만 바꾸며 판정에는 관여하지 않는다. 연출 중인 외형은 이 클래스가 맡는다.</summary>
    public sealed class CollisionFeedback : IDisposable
    {
        private sealed class Actor
        {
            public int Index;
            public Transform Root, Visual;
            public Vector3 RootPosition, VisualScale, Away;
            public Quaternion RootRotation, VisualRotation;
            // 적마다 범위 안에서 한 번 고른 값. Spin은 방향 부호를 포함한 바퀴 수.
            public float Spin, Distance, Height;
            public SpriteRenderer[] Bodies, Markers;
            public Color[] BodyColors;
        }
        private sealed class Effect
        {
            public float Time;
            public readonly List<Actor> Actors = new List<Actor>();
            public GameObject Impact;
            public SpriteRenderer[] ImpactRenderers;
            public Color[] ImpactColors;
        }
        // 플레이어 피격 연출: 때린 적의 내밀기(From에서 Reach만큼), 충격 이펙트, 플레이어 밀림.
        private sealed class Lunger { public int Index; public Transform Root; public Vector3 From, Reach; }
        private sealed class PlayerHit
        {
            public float Time;
            public bool Impacted;
            public Vector3 Center, Away;
            public readonly List<Lunger> Lungers = new List<Lunger>();
            public GameObject Impact;
            public SpriteRenderer[] ImpactRenderers;
            public Color[] ImpactColors;
        }
        private static readonly Color FlashTint = new Color(1f, .55f, .55f, 1f);
        private readonly CollisionFeedbackSettings settings;
        private readonly bool ownsSettings;
        private readonly Transform parent;
        private readonly Func<Vector2Int, Vector3> cellPosition;
        private readonly float cellSize;
        private readonly Camera camera;
        private readonly List<Effect> effects = new List<Effect>();
        private readonly HashSet<int> owned = new HashSet<int>();
        private AudioSource audio;
        private bool shaking;
        private float shakeTime;
        private Vector3 shakeBase;
        private PlayerHit hit;

        /// <param name="parent">이펙트·효과음 오브젝트를 둘 부모(맵 생성 루트). cellPosition은 이 부모 기준 칸 위치를 돌려준다.</param>
        public CollisionFeedback(CollisionFeedbackSettings settings, Transform parent, Func<Vector2Int, Vector3> cellPosition, float cellSize, Camera camera)
        {
            ownsSettings = settings == null;
            this.settings = settings != null ? settings : CreateDefaults();
            this.parent = parent; this.cellPosition = cellPosition; this.cellSize = cellSize; this.camera = camera;
            RuntimeObjects.Prepare(ref audio, parent, "Collision Audio", this.settings.ImpactSound); // 첫 효과음이 빠지지 않게 미리 준비
            if (this.settings.PlayerHitSound != null) this.settings.PlayerHitSound.LoadAudioData();
        }
        private static CollisionFeedbackSettings CreateDefaults()
        {
            var defaults = ScriptableObject.CreateInstance<CollisionFeedbackSettings>(); defaults.hideFlags = HideFlags.HideAndDontSave; return defaults;
        }
        public CollisionFeedbackSettings Settings => settings;
        /// <summary>재생 중인 연출이 있으면 true. 이 동안 다음 입력을 받지 않는다.</summary>
        public bool IsPlaying => effects.Count > 0 || shaking || hit != null;
        /// <summary>플레이어 피격 연출로 밀린 거리(맵 기준). 되돌리기·재시작(Clear) 전까지 유지된다.</summary>
        public Vector3 PlayerOffset { get; private set; }
        /// <summary>피격 연출이 없거나 맞는 순간이 지났으면 true. 패배 그림은 이때부터 보인다.</summary>
        public bool PlayerHitLanded => hit == null || hit.Impacted;
        /// <summary>다른 움직임을 멈춰야 하는 남은 시간(맞닿음 + 멈춤).</summary>
        public float HoldRemaining
        {
            get { float hold = 0; foreach (var effect in effects) hold = Mathf.Max(hold, settings.HoldTime - effect.Time); return hold; }
        }
        /// <summary>해당 인덱스의 적 외형을 연출이 맡고 있으면 true. 그동안 일반 위치·표시 갱신을 건너뛴다.</summary>
        public bool Owns(int enemyIndex) => enemyIndex >= 0 && owned.Contains(enemyIndex);

        public void Begin(EnemyCollision collision, IReadOnlyList<EnemyDefinition> views, IReadOnlyList<EnemyState> states)
        {
            if (!settings.Enabled || collision == null) return;
            var effect = new Effect();
            var center = cellPosition(collision.Cell);
            for (int n = 0; n < collision.Enemies.Count; n++)
            {
                int index = collision.Enemies[n];
                if (index < 0 || index >= views.Count || views[index] == null || owned.Contains(index)) continue;
                var direction = states[index].Direction;
                // 들어온 방향의 반대로 튕긴다. 같은 쪽으로 겹치면 옆으로 벌린다.
                var away = -new Vector3(direction.x, 0, direction.y);
                foreach (var other in effect.Actors) if (Vector3.Dot(other.Away, away) > .9f) away = Quaternion.Euler(0, n % 2 == 0 ? 70 : -70, 0) * away;
                effect.Actors.Add(CreateActor(index, views[index].transform, views[index].Visual, center, away, n % 2 == 0 ? 1 : -1));
            }
            Start(effect, center);
        }
        /// <summary>플레이어 피격 연출을 시작한다. 연출이 꺼져 있으면 아무것도 하지 않는다(PlayerHitLanded가 바로 true).</summary>
        /// <param name="playerCenter">플레이어 칸 위치(맵 기준). attackers: 때린 적 인덱스(GridSession.Attackers).</param>
        public void BeginPlayerHit(Vector3 playerCenter, IReadOnlyList<int> attackers, IReadOnlyList<EnemyDefinition> views, IReadOnlyList<EnemyState> states)
        {
            if (!settings.Enabled) return;
            ClearPlayerHit();
            hit = new PlayerHit { Center = playerCenter };
            var away = Vector3.zero;
            foreach (int index in attackers)
            {
                if (index < 0 || index >= views.Count || views[index] == null || owned.Contains(index)) continue;
                var root = views[index].transform; var from = root.localPosition;
                var toPlayer = playerCenter - from; toPlayer.y = 0;
                // 적이 플레이어 칸 안에 있으면(겹침) 들어온 방향을 쓴다.
                var direction = toPlayer.sqrMagnitude > 1e-6f ? toPlayer.normalized : new Vector3(states[index].Direction.x, 0, states[index].Direction.y).normalized;
                away += direction;
                // 몸을 내밀어도 플레이어 중심에서 맞닿음 거리의 절반보다 가까이 오지 않는다.
                float reach = Mathf.Min(settings.LungeDistance * cellSize, Mathf.Max(0, toPlayer.magnitude - settings.ContactDistance * .5f * cellSize));
                if (reach > 0) { hit.Lungers.Add(new Lunger { Index = index, Root = root, From = from, Reach = direction * reach }); owned.Add(index); }
            }
            hit.Away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero;
            ApplyPlayerHit();
        }
        private void ApplyPlayerHit()
        {
            float t = hit.Time;
            foreach (var lunger in hit.Lungers)
            {
                if (lunger.Root == null) continue;
                float u = Mathf.Clamp01(t / settings.LungeTime);
                lunger.Root.localPosition = lunger.From + lunger.Reach * Mathf.Sin(Mathf.PI * u);
                if (u >= 1) { lunger.Root.localPosition = lunger.From; owned.Remove(lunger.Index); }
            }
            float impactAt = hit.Lungers.Count > 0 ? settings.LungeImpactTime : 0;
            if (t < impactAt) return;
            if (!hit.Impacted)
            {
                hit.Impacted = true;
                var at = hit.Center - hit.Away * .3f * cellSize;
                hit.Impact = SpawnImpact(settings.PlayerHitPrefab, at, "Player Hit Impact", out hit.ImpactRenderers, out hit.ImpactColors);
                if (settings.PlayerHitSound != null) PlaySound(settings.PlayerHitSound, settings.PlayerHitVolume);
                StartShake();
            }
            float since = t - impactAt;
            float k = Mathf.Clamp01(since / settings.KnockbackTime);
            PlayerOffset = hit.Away * settings.KnockbackDistance * cellSize * (1 - (1 - k) * (1 - k));
            if (hit.Impact != null && !AnimateImpact(hit.Impact, hit.ImpactRenderers, hit.ImpactColors, since)) { Release(hit.Impact); hit.Impact = null; }
            if (t >= settings.LungeTime && since >= settings.KnockbackTime && hit.Impact == null) hit = null;
        }
        private void ClearPlayerHit()
        {
            if (hit == null) return;
            foreach (var lunger in hit.Lungers) { if (lunger.Root != null) lunger.Root.localPosition = lunger.From; owned.Remove(lunger.Index); }
            if (hit.Impact != null) Release(hit.Impact);
            hit = null;
        }
        private Actor CreateActor(int index, Transform root, Transform visual, Vector3 center, Vector3 away, float spinSign)
        {
            away.y = 0; away = away.sqrMagnitude < 1e-6f ? Vector3.forward : away.normalized;
            var actor = new Actor
            {
                Index = index, Root = root, Visual = visual, RootPosition = center, RootRotation = root.localRotation,
                VisualScale = visual != null ? visual.localScale : Vector3.one, VisualRotation = visual != null ? visual.localRotation : Quaternion.identity,
                Away = away, Spin = spinSign * settings.SpinTurns.Sample(), Distance = settings.FlyDistance.Sample(), Height = settings.FlyHeight.Sample(),
            };
            var bodies = new List<SpriteRenderer>(); var markers = new List<SpriteRenderer>();
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                (visual != null && renderer.transform.IsChildOf(visual) ? bodies : markers).Add(renderer);
            actor.Bodies = bodies.ToArray(); actor.Markers = markers.ToArray();
            actor.BodyColors = Array.ConvertAll(actor.Bodies, r => r.color);
            root.gameObject.SetActive(true); root.localPosition = center;
            owned.Add(index);
            return actor;
        }
        private void Start(Effect effect, Vector3 center)
        {
            if (effect.Actors.Count == 0) return;
            effect.Impact = SpawnImpact(settings.ImpactPrefab, center, "Collision Impact", out effect.ImpactRenderers, out effect.ImpactColors);
            if (settings.ImpactSound != null) PlaySound(settings.ImpactSound, settings.ImpactVolume);
            StartShake();
            effects.Add(effect);
            Apply(effect);
        }
        private GameObject SpawnImpact(GameObject prefab, Vector3 at, string name, out SpriteRenderer[] renderers, out Color[] colors)
        {
            renderers = null; colors = null;
            if (prefab == null) return null;
            var impact = UnityEngine.Object.Instantiate(prefab, parent, false);
            impact.name = name;
            impact.transform.localPosition = at + Vector3.up * .45f * cellSize;
            renderers = impact.GetComponentsInChildren<SpriteRenderer>(true);
            colors = Array.ConvertAll(renderers, r => r.color);
            AnimateImpact(impact, renderers, colors, 0);
            return impact;
        }
        // 충격 이펙트를 커지며 사라지게 한다. 시간이 끝났으면 false.
        private bool AnimateImpact(GameObject impact, SpriteRenderer[] renderers, Color[] colors, float t)
        {
            float v = Mathf.Clamp01(t / settings.ImpactTime);
            if (v >= 1) return false;
            impact.transform.localScale = Vector3.one * settings.ImpactScale * cellSize * Mathf.Lerp(.45f, 1, 1 - (1 - v) * (1 - v));
            if (camera != null) impact.transform.rotation = camera.transform.rotation;
            for (int r = 0; r < renderers.Length; r++) { var c = colors[r]; renderers[r].color = new Color(c.r, c.g, c.b, c.a * (1 - v * v)); }
            return true;
        }
        private void StartShake()
        {
            if (camera == null || settings.ShakeAmplitude <= 0 || settings.ShakeTime <= 0) return;
            if (!shaking) shakeBase = camera.transform.position;
            shaking = true; shakeTime = 0;
        }
        private void PlaySound(AudioClip clip, float volume) => RuntimeObjects.PlayOneShot(ref audio, parent, "Collision Audio", clip, volume);

        public void Tick(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds)) return;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i]; effect.Time += seconds;
                if (effect.Time >= settings.TotalTime) { Finish(effect, true); effects.RemoveAt(i); }
                else Apply(effect);
            }
            if (hit != null) { hit.Time += seconds; ApplyPlayerHit(); }
            if (shaking)
            {
                shakeTime += seconds;
                if (camera == null || shakeTime >= settings.ShakeTime) StopShake();
                else camera.transform.position = shakeBase + UnityEngine.Random.insideUnitSphere * settings.ShakeAmplitude * cellSize * (1 - shakeTime / settings.ShakeTime);
            }
        }
        private void Apply(Effect effect)
        {
            float t = effect.Time;
            foreach (var actor in effect.Actors)
            {
                if (t < settings.SquashTime)
                {
                    float k = Mathf.Sin(Mathf.PI * t / Mathf.Max(.0001f, settings.SquashTime));
                    if (actor.Visual != null) actor.Visual.localScale = Vector3.Scale(actor.VisualScale, new Vector3(1 + settings.SquashAmount * k, 1 - settings.SquashAmount * k, 1));
                    for (int b = 0; b < actor.Bodies.Length; b++) actor.Bodies[b].color = actor.BodyColors[b] * Color.Lerp(Color.white, FlashTint, k);
                }
                else if (t < settings.HoldTime)
                {
                    if (actor.Visual != null) actor.Visual.localScale = actor.VisualScale;
                    for (int b = 0; b < actor.Bodies.Length; b++) actor.Bodies[b].color = actor.BodyColors[b];
                }
                else
                {
                    float u = Mathf.Clamp01((t - settings.HoldTime) / settings.FlyTime);
                    foreach (var marker in actor.Markers) marker.enabled = false;
                    actor.Root.localPosition = actor.RootPosition + (actor.Away * actor.Distance * u + Vector3.up * actor.Height * 4 * u * (1 - u)) * cellSize;
                    if (actor.Visual != null)
                    {
                        actor.Visual.localScale = actor.VisualScale * (1 - .4f * u);
                        var facing = camera != null ? camera.transform.rotation : actor.Visual.rotation;
                        actor.Visual.rotation = facing * Quaternion.Euler(0, 0, actor.Spin * 360 * u);
                    }
                    float alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1, u));
                    for (int b = 0; b < actor.Bodies.Length; b++) { var c = actor.BodyColors[b]; actor.Bodies[b].color = new Color(c.r, c.g, c.b, c.a * alpha); }
                }
            }
            if (effect.Impact != null && !AnimateImpact(effect.Impact, effect.ImpactRenderers, effect.ImpactColors, t)) { Release(effect.Impact); effect.Impact = null; }
        }
        // 연출이 끝나면 외형을 원래 상태로 되돌린다. finished면 사라진 적을 숨기고, 아니면(되돌리기 등) 표시 여부를 호출한 쪽에 맡긴다.
        private void Finish(Effect effect, bool finished)
        {
            foreach (var actor in effect.Actors)
            {
                if (actor.Root == null) { owned.Remove(actor.Index); continue; }
                actor.Root.localPosition = actor.RootPosition; actor.Root.localRotation = actor.RootRotation;
                if (actor.Visual != null) { actor.Visual.localScale = actor.VisualScale; actor.Visual.localRotation = actor.VisualRotation; }
                for (int b = 0; b < actor.Bodies.Length; b++) if (actor.Bodies[b] != null) actor.Bodies[b].color = actor.BodyColors[b];
                foreach (var marker in actor.Markers) if (marker != null) marker.enabled = true;
                if (finished) actor.Root.gameObject.SetActive(false);
                owned.Remove(actor.Index);
            }
            if (effect.Impact != null) { Release(effect.Impact); effect.Impact = null; }
        }
        private void StopShake()
        {
            if (shaking && camera != null) camera.transform.position = shakeBase;
            shaking = false;
        }
        /// <summary>재생 중인 연출을 즉시 멈추고 외형·카메라를 원래대로 되돌린다(되돌리기·재시작).</summary>
        public void Clear()
        {
            foreach (var effect in effects) Finish(effect, false);
            ClearPlayerHit(); PlayerOffset = Vector3.zero;
            effects.Clear(); owned.Clear(); StopShake();
        }
        public void Dispose()
        {
            Clear();
            if (audio != null) { Release(audio.gameObject); audio = null; }
            if (ownsSettings && settings != null) Release(settings);
        }
        private static void Release(UnityEngine.Object value) => RuntimeObjects.Release(value);
    }
}
