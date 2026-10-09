using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>충돌 연출(맞닿음 → 멈춤 → 충격·흔들림·효과음 → 튕겨 날아가며 퇴장)을 재생한다.
    /// 적끼리 충돌하면 충돌한 적들에, 플레이어가 패배하면 플레이어에게만 같은 연출을 적용한다.
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
        private static readonly Color FlashTint = new Color(1f, .55f, .55f, 1f);
        private readonly CollisionFeedbackSettings settings;
        private readonly bool ownsSettings;
        private readonly Transform parent;
        private readonly Func<Vector2Int, Vector3> cellPosition;
        private readonly float cellSize;
        private readonly Camera camera;
        private readonly List<Effect> effects = new List<Effect>();
        private readonly HashSet<int> owned = new HashSet<int>();
        private const int PlayerIndex = -1;
        // 패배 연출 뒤 숨긴 플레이어. 되돌리기·재시작 때 다시 보이게 한다.
        private Transform hiddenPlayer;
        private AudioSource audio;
        private bool shaking;
        private float shakeTime;
        private Vector3 shakeBase;

        /// <param name="parent">이펙트·효과음 오브젝트를 둘 부모(맵 생성 루트). cellPosition은 이 부모 기준 칸 위치를 돌려준다.</param>
        public CollisionFeedback(CollisionFeedbackSettings settings, Transform parent, Func<Vector2Int, Vector3> cellPosition, float cellSize, Camera camera)
        {
            ownsSettings = settings == null;
            this.settings = settings != null ? settings : CreateDefaults();
            this.parent = parent; this.cellPosition = cellPosition; this.cellSize = cellSize; this.camera = camera;
        }
        private static CollisionFeedbackSettings CreateDefaults()
        {
            var defaults = ScriptableObject.CreateInstance<CollisionFeedbackSettings>(); defaults.hideFlags = HideFlags.HideAndDontSave; return defaults;
        }
        public CollisionFeedbackSettings Settings => settings;
        /// <summary>재생 중인 연출이 있으면 true. 이 동안 다음 입력을 받지 않는다.</summary>
        public bool IsPlaying => effects.Count > 0 || shaking;
        /// <summary>다른 움직임을 멈춰야 하는 남은 시간(맞닿음 + 멈춤).</summary>
        public float HoldRemaining
        {
            get { float hold = 0; foreach (var effect in effects) hold = Mathf.Max(hold, settings.HoldTime - effect.Time); return hold; }
        }
        /// <summary>해당 인덱스의 적 외형을 연출이 맡고 있으면 true. 그동안 일반 위치·표시 갱신을 건너뛴다.</summary>
        public bool Owns(int enemyIndex) => enemyIndex >= 0 && owned.Contains(enemyIndex);
        /// <summary>플레이어 외형을 연출이 맡고 있으면 true(날아가 사라진 뒤 포함). 그동안 일반 위치·표시 갱신을 건너뛴다.</summary>
        public bool OwnsPlayer => owned.Contains(PlayerIndex) || hiddenPlayer != null;

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
        /// <summary>플레이어 패배 연출: 같은 연출을 플레이어에게만 적용한다. away는 튕겨 나갈 수평 방향(맵 기준), root는 플레이어 위치 루트, visual은 카메라를 향하는 외형.</summary>
        public void BeginPlayer(Vector2Int cell, Transform root, Transform visual, Vector3 away)
        {
            if (!settings.Enabled || root == null || OwnsPlayer) return;
            var effect = new Effect(); var center = cellPosition(cell);
            effect.Actors.Add(CreateActor(PlayerIndex, root, visual, center, away, 1));
            Start(effect, center);
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
            if (settings.ImpactPrefab != null)
            {
                effect.Impact = UnityEngine.Object.Instantiate(settings.ImpactPrefab, parent, false);
                effect.Impact.name = "Collision Impact";
                effect.Impact.transform.localPosition = center + Vector3.up * .45f * cellSize;
                effect.ImpactRenderers = effect.Impact.GetComponentsInChildren<SpriteRenderer>(true);
                effect.ImpactColors = Array.ConvertAll(effect.ImpactRenderers, r => r.color);
            }
            if (settings.ImpactSound != null) PlaySound(settings.ImpactSound, settings.ImpactVolume);
            if (camera != null && settings.ShakeAmplitude > 0 && settings.ShakeTime > 0)
            {
                if (!shaking) shakeBase = camera.transform.position;
                shaking = true; shakeTime = 0;
            }
            effects.Add(effect);
            Apply(effect);
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
            if (effect.Impact != null)
            {
                float v = Mathf.Clamp01(t / settings.ImpactTime);
                if (v >= 1) { Release(effect.Impact); effect.Impact = null; return; }
                effect.Impact.transform.localScale = Vector3.one * settings.ImpactScale * cellSize * Mathf.Lerp(.45f, 1, 1 - (1 - v) * (1 - v));
                if (camera != null) effect.Impact.transform.rotation = camera.transform.rotation;
                for (int r = 0; r < effect.ImpactRenderers.Length; r++) { var c = effect.ImpactColors[r]; effect.ImpactRenderers[r].color = new Color(c.r, c.g, c.b, c.a * (1 - v * v)); }
            }
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
                if (finished) { actor.Root.gameObject.SetActive(false); if (actor.Index == PlayerIndex) hiddenPlayer = actor.Root; }
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
            effects.Clear(); owned.Clear(); StopShake();
            if (hiddenPlayer != null) { hiddenPlayer.gameObject.SetActive(true); hiddenPlayer = null; }
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
