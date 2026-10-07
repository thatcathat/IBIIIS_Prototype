using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>적 인식 표시(표시 전용). 입력 대기·승패 상태에서 플레이어의 현재 칸이 적의 인식 범위 안이면 머리 위에 "!"를 띄운다.
    /// 새로 인식하면 톡 튀어나오며 효과음, 범위를 벗어나면 "?"를 잠깐 보여 준다. 행동 진행 중에는 판정이 확정되지 않았으므로 숨기고,
    /// 행동이 끝난 뒤 행동 전 상태와 비교해 반응한다. 판정에는 관여하지 않는다.</summary>
    public sealed class EnemyAlert : IDisposable
    {
        private sealed class Mark
        {
            public SpriteRenderer Alert, Lost;
            public bool Recognizing;
            public float AlertTime = -1, LostTime = -1;
        }
        private readonly EnemyAlertSettings settings;
        private readonly Transform parent;
        private readonly Camera camera;
        private readonly float cellSize;
        private readonly Dictionary<int, Mark> marks = new Dictionary<int, Mark>();
        private AudioSource audio;
        // 처음 평가(전투 시작)는 비교할 이전 상태가 없으므로 인식 중이면 그대로 튀어나온다.
        public EnemyAlert(EnemyAlertSettings settings, Transform parent, Camera camera, float cellSize)
        {
            this.settings = settings; this.parent = parent; this.camera = camera; this.cellSize = cellSize;
        }
        private bool Active => settings != null && settings.Enabled && settings.AlertSprite != null && parent != null;
        /// <summary>!를 보이고 있는 적 수(테스트·디버그용).</summary>
        public int ShownAlerts { get { int n = 0; foreach (var m in marks.Values) if (m.Alert != null && m.Alert.gameObject.activeSelf) n++; return n; } }
        public int ShownLost { get { int n = 0; foreach (var m in marks.Values) if (m.Lost != null && m.Lost.gameObject.activeSelf) n++; return n; } }
        public int AlertsPlayed { get; private set; }
        public Transform AlertMark(int enemy) => marks.TryGetValue(enemy, out var m) && m.Alert != null ? m.Alert.transform : null;
        public Transform LostMark(int enemy) => marks.TryGetValue(enemy, out var m) && m.Lost != null ? m.Lost.transform : null;
        /// <summary>매 프레임 호출한다. show가 false(행동 진행 중 등)면 모두 숨기고 이전 상태를 유지한다. skip이 true인 적(충돌 연출 중)도 숨긴다.</summary>
        public void Update(float seconds, GridSession session, IReadOnlyList<EnemyDefinition> views, bool show, Func<int, bool> skip)
        {
            if (!Active || session == null || views == null) { HideAll(); return; }
            bool newAlert = false;
            for (int i = 0; i < views.Count && i < session.Enemies.Count; i++)
            {
                var view = views[i]; var state = session.Enemies[i];
                if (view == null) continue;
                var m = Get(i);
                bool visible = show && state.Alive && (skip == null || !skip(i));
                if (!visible) { Hide(m); if (!state.Alive) { m.Recognizing = false; m.AlertTime = m.LostTime = -1; } continue; }
                bool now = session.IsRecognizing(state);
                if (now && !m.Recognizing) { m.AlertTime = 0; m.LostTime = -1; newAlert = true; }
                else if (!now && m.Recognizing) { m.LostTime = settings.LostSprite != null ? 0 : -1; m.AlertTime = -1; }
                m.Recognizing = now;
                if (m.AlertTime >= 0) m.AlertTime += seconds;
                if (m.LostTime >= 0) { m.LostTime += seconds; if (m.LostTime >= settings.LostTime) m.LostTime = -1; }
                // 적 그림은 카메라 쪽으로 기울어 서 있으므로, 발밑에서 화면 위쪽(카메라 위 방향)으로 올려야 그림 머리 바로 위에 보인다.
                var up = camera != null ? camera.transform.up : Vector3.up;
                var top = view.transform.position + up * settings.Height * cellSize;
                Place(ref m.Alert, settings.AlertSprite, "Enemy Alert", now, top, AlertScale(m.AlertTime), 1);
                Place(ref m.Lost, settings.LostSprite, "Enemy Lost", !now && m.LostTime >= 0, top, 1, LostAlpha(m.LostTime));
            }
            if (newAlert) { AlertsPlayed++; Play(settings.AlertSound, settings.AlertVolume); }
        }
        /// <summary>되돌리기 등으로 상태가 순간 바뀌었을 때, 튀어나옴·?·효과음 없이 현재 인식 상태로 맞춘다.</summary>
        public void Resync(GridSession session)
        {
            foreach (var pair in marks)
            {
                var m = pair.Value; m.AlertTime = m.LostTime = -1;
                m.Recognizing = session != null && pair.Key < session.Enemies.Count && session.Enemies[pair.Key].Alive && session.IsRecognizing(session.Enemies[pair.Key]);
                Hide(m);
            }
            if (session != null) for (int i = 0; i < session.Enemies.Count; i++) if (!marks.ContainsKey(i)) { var m = Get(i); m.Recognizing = session.Enemies[i].Alive && session.IsRecognizing(session.Enemies[i]); }
        }
        // 튀어나옴: 앞 절반에 0에서 최대 배율까지 커지고 뒤 절반에 1로 줄어든다. 끝나면 1.
        private float AlertScale(float time)
        {
            if (time < 0 || time >= settings.PopTime) return 1;
            float u = time / settings.PopTime;
            return u < .5f ? Mathf.Lerp(0, settings.PopScale, u / .5f) : Mathf.Lerp(settings.PopScale, 1, (u - .5f) / .5f);
        }
        private float LostAlpha(float time) => time < 0 ? 0 : Mathf.Clamp01((1 - time / settings.LostTime) / .4f);
        private Mark Get(int i) { if (!marks.TryGetValue(i, out var m)) marks[i] = m = new Mark(); return m; }
        private void Place(ref SpriteRenderer renderer, Sprite sprite, string name, bool visible, Vector3 position, float scale, float alpha)
        {
            if (!visible || sprite == null) { if (renderer != null) renderer.gameObject.SetActive(false); return; }
            if (renderer == null)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 5;
            }
            renderer.gameObject.SetActive(true);
            renderer.transform.position = position;
            renderer.transform.localScale = Vector3.one * settings.Size * cellSize * scale;
            CameraFacingSprite.Face(renderer.transform, camera);
            renderer.color = new Color(1, 1, 1, alpha);
        }
        private static void Hide(Mark m)
        {
            if (m.Alert != null) m.Alert.gameObject.SetActive(false);
            if (m.Lost != null) m.Lost.gameObject.SetActive(false);
        }
        private void HideAll() { foreach (var m in marks.Values) Hide(m); }
        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || parent == null) return;
            if (audio == null)
            {
                var go = new GameObject("Alert Audio"); go.transform.SetParent(parent, false);
                audio = go.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0;
            }
            audio.PlayOneShot(clip, volume);
        }
        public void Dispose()
        {
            foreach (var m in marks.Values) { MotionEffects.Release(m.Alert != null ? m.Alert.gameObject : null); MotionEffects.Release(m.Lost != null ? m.Lost.gameObject : null); }
            marks.Clear();
            if (audio != null) { MotionEffects.Release(audio.gameObject); audio = null; }
        }
    }
}
