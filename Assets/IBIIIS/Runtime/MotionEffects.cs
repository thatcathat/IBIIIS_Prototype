using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>손맛 자세 계산(표시 전용). 전투·미니맵이 함께 쓴다. 진행도 0과 1에서는 원래 자세.</summary>
    /// <summary>실행 중 만든 오브젝트·효과음 공용 처리. Play 중이면 Destroy, 에디터(테스트·미리보기)면 DestroyImmediate.</summary>
    public static class RuntimeObjects
    {
        public static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
        /// <summary>parent 아래의 AudioSource 하나로 효과음을 낸다(처음 쓸 때 name으로 만든다). 소리나 parent가 없으면 아무것도 하지 않는다.</summary>
        public static void PlayOneShot(ref AudioSource audio, Transform parent, string name, AudioClip clip, float volume)
        {
            if (clip == null || parent == null) return;
            if (audio == null)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                audio = go.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0;
            }
            audio.PlayOneShot(clip, volume);
        }
    }
    public static class MotionPoses
    {
        /// <summary>뜀 자세: 시작 구간(takeoffPortion)에 웅크렸다가 포물선으로 뜨고, 공중에서 위아래로 늘어난다.</summary>
        public static MotionPose Hop(float progress, float height, float takeoffSquash, float airStretch, float takeoffPortion)
        {
            float p = Mathf.Clamp01(progress);
            var pose = MotionPose.Identity;
            pose.Lift = height * Mathf.Sin(Mathf.PI * p);
            if (p < takeoffPortion) return WithSquash(pose, takeoffSquash * Mathf.Sin(Mathf.PI * p / takeoffPortion));
            float air = Mathf.Sin(Mathf.PI * (p - takeoffPortion) / (1 - takeoffPortion));
            return WithSquash(pose, -airStretch * air);
        }
        /// <summary>대시 자세: 시작·끝 15% 구간에서 부드럽게 들어갔다 빠지며, 그 사이 낮게 웅크리고 좌우 대시(directionX ≠ 0)면 진행 방향으로 기운다.</summary>
        public static MotionPose Dash(float progress, int directionX, float squash, float lean)
        {
            float p = Mathf.Clamp01(progress);
            float e = Mathf.Clamp01(Mathf.Min(p, 1 - p) / .15f); e = e * e * (3 - 2 * e);
            var pose = Squashed(squash * e);
            pose.Tilt = -Mathf.Sign(directionX) * (directionX != 0 ? 1 : 0) * lean * e;
            return pose;
        }
        /// <summary>숨쉬기: 주기마다 위아래로 살짝 늘었다 줄어든다. 시간 0에서 원래 자세.</summary>
        public static MotionPose Breath(float time, float amount, float period)
            => amount <= 0 || period <= 0 ? MotionPose.Identity : Squashed(-amount * Mathf.Sin(2 * Mathf.PI * time / period));
        /// <summary>착지 납작함: time이 0→duration 동안 squash까지 납작해졌다 돌아온다.</summary>
        public static MotionPose Landing(float time, float duration, float squash) => Squashed(squash * Mathf.Sin(Mathf.PI * time / duration));
        /// <summary>부딪힘: direction(맵 평면, 단위 벡터) 쪽으로 distance만큼 밀렸다 돌아오며 squash만큼 눌린다.</summary>
        public static MotionPose Bump(float time, float duration, float squash, float distance, Vector3 direction)
        {
            float k = Mathf.Sin(Mathf.PI * time / duration);
            var pose = Squashed(squash * k); pose.Shift = direction * distance * k; return pose;
        }
        /// <summary>amount > 0: 납작(가로로 넓고 세로로 낮게), amount < 0: 늘어남.</summary>
        public static MotionPose Squashed(float amount) => WithSquash(MotionPose.Identity, amount);
        public static MotionPose WithSquash(MotionPose pose, float amount) { pose.Squash = new Vector2(1 + amount, 1 - amount); return pose; }
    }

    /// <summary>손맛 연출 부품: 착지 먼지, 대시 잔상, 효과음. 만든 오브젝트는 parent 아래에 두고 Tick으로 흐리게 하다 지운다. 전투·미니맵이 함께 쓴다.</summary>
    public sealed class MotionEffects : IDisposable
    {
        private sealed class Fade { public GameObject Go; public SpriteRenderer[] Renderers; public Color[] Colors; public float Time, Duration; public Vector3 Base, Drift; public float Scale; public bool Grow; }
        private readonly Transform parent;
        private readonly Camera camera;
        private readonly List<Fade> puffs = new List<Fade>(), ghosts = new List<Fade>();
        private AudioSource audio;
        public MotionEffects(Transform parent, Camera camera) { this.parent = parent; this.camera = camera; }
        public int DustCount => puffs.Count;
        public int AfterimageCount => ghosts.Count;
        public bool IsAnimating => puffs.Count > 0 || ghosts.Count > 0;
        /// <summary>먼지 두 덩이를 position(parent 기준)에 낸다. away가 0이면 화면 좌우로, 아니면 그 방향(parent 기준 수평) 양옆으로 퍼진다.
        /// unit은 퍼지는 거리·높이의 기준 길이(전투는 칸 크기).</summary>
        public void SpawnDust(GameObject prefab, Vector3 position, Vector3 away, float scale, float duration, float unit)
        {
            if (prefab == null || parent == null) return;
            Vector3 spread;
            if (away.sqrMagnitude < 1e-8f)
            {
                var right = camera != null ? camera.transform.right : Vector3.right; right.y = 0; right = right.sqrMagnitude > 0 ? right.normalized : Vector3.right;
                spread = parent.InverseTransformDirection(right);
            }
            else { away.y = 0; spread = away.normalized; }
            var side = new Vector3(-spread.z, 0, spread.x);
            bool sideways = away.sqrMagnitude < 1e-8f;
            foreach (var drift in sideways ? new[] { -spread, spread } : new[] { spread + side * .5f, spread - side * .5f })
            {
                var go = UnityEngine.Object.Instantiate(prefab, parent, false); go.name = "Landing Dust";
                var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
                var puff = new Fade { Go = go, Renderers = renderers, Colors = Array.ConvertAll(renderers, r => r.color), Duration = duration, Grow = true,
                    Base = position + Vector3.up * .05f * unit, Drift = drift * .35f * unit, Scale = scale };
                puffs.Add(puff); ApplyFade(puff);
            }
        }
        /// <summary>현재 스프라이트를 그 자리에 복사해 잔상으로 남긴다.</summary>
        public void SpawnAfterimage(SpriteRenderer sprite, Color color, float duration)
        {
            if (sprite == null || parent == null) return;
            var go = new GameObject("Dash Afterimage"); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(sprite.transform.position, sprite.transform.rotation);
            go.transform.localScale = parent.lossyScale.x != 0 ? sprite.transform.lossyScale / parent.lossyScale.x : sprite.transform.lossyScale;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite.sprite; renderer.flipX = sprite.flipX; renderer.sortingOrder = sprite.sortingOrder - 1;
            renderer.color = color;
            ghosts.Add(new Fade { Go = go, Renderers = new[] { renderer }, Colors = new[] { color }, Duration = duration, Base = go.transform.localPosition });
        }
        /// <summary>먼지·잔상을 흐리게 하고 다 사라지면 지운다(매 프레임).</summary>
        public void Tick(float seconds) { TickFades(puffs, seconds); TickFades(ghosts, seconds); }
        public void Play(AudioClip clip, float volume)
        {
            RuntimeObjects.PlayOneShot(ref audio, parent, "Motion Audio", clip, volume);
        }
        /// <summary>먼지·잔상을 즉시 지운다.</summary>
        public void Clear()
        {
            foreach (var fade in puffs) Release(fade.Go);
            foreach (var fade in ghosts) Release(fade.Go);
            puffs.Clear(); ghosts.Clear();
        }
        public void Dispose()
        {
            Clear();
            if (audio != null) { Release(audio.gameObject); audio = null; }
        }
        private void TickFades(List<Fade> list, float seconds)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var fade = list[i]; fade.Time += seconds;
                if (fade.Go == null || fade.Time >= fade.Duration) { Release(fade.Go); list.RemoveAt(i); continue; }
                ApplyFade(fade);
            }
        }
        private void ApplyFade(Fade fade)
        {
            float v = Mathf.Clamp01(fade.Time / fade.Duration), eased = 1 - (1 - v) * (1 - v);
            if (fade.Grow)
            {
                fade.Go.transform.localPosition = fade.Base + fade.Drift * eased;
                fade.Go.transform.localScale = Vector3.one * fade.Scale * Mathf.Lerp(.4f, 1, eased);
                if (camera != null) fade.Go.transform.rotation = camera.transform.rotation;
            }
            for (int r = 0; r < fade.Renderers.Length; r++) { var c = fade.Colors[r]; fade.Renderers[r].color = new Color(c.r, c.g, c.b, c.a * (1 - v)); }
        }
        // Play 중 Destroy는 프레임 끝에 지우므로, 그 전에 숨기고 부모에서 떼어 남은 자식 수·표시에 섞이지 않게 한다.
        internal static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying && value is GameObject go) { go.SetActive(false); go.transform.SetParent(null, false); }
            RuntimeObjects.Release(value);
        }
    }
}
