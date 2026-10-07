using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>적 인식 표시의 임시 그림(! · ?)·효과음과 설정 에셋을 만들고 공용 플레이어 설정에 연결한다. 이미 있는 파일·연결은 덮어쓰지 않는다.
    /// 정식 그림·소리는 생성된 파일을 교체하거나 설정 슬롯을 바꾼다.</summary>
    public static class EnemyAlertSetup
    {
        public const string AlertSprite = AssetPaths.Effects + "/EnemyAlert.png", LostSprite = AssetPaths.Effects + "/EnemyLost.png";
        public const string AlertSound = AssetPaths.Audio + "/EnemyAlert.wav";

        [MenuItem("IBIIIS/Create Default Enemy Alert")]
        public static EnemyAlertSettings EnsureDefault()
        {
            Directory.CreateDirectory(AssetPaths.Effects); Directory.CreateDirectory(AssetPaths.Audio); AssetDatabase.Refresh();
            var alertSprite = EnsureAlertSprite(); var lostSprite = EnsureLostSprite();
            // 짧게 올라가는 두 음 "띵-딩!"
            var sound = MotionFeedbackSetup.EnsureSound(AlertSound, .2f, (t, noise) =>
                (float)Math.Sin(2 * Math.PI * (t < .06f ? 880 : 1320) * t) * Mathf.Exp(-(t < .06f ? t : t - .06f) * 18));
            var settings = AssetDatabase.LoadAssetAtPath<EnemyAlertSettings>(AssetPaths.EnemyAlert);
            if (settings == null) { settings = ScriptableObject.CreateInstance<EnemyAlertSettings>(); AssetDatabase.CreateAsset(settings, AssetPaths.EnemyAlert); }
            var so = new SerializedObject(settings); bool filled = false;
            void Fill(string name, UnityEngine.Object value) { var slot = so.FindProperty(name); if (slot.objectReferenceValue == null && value != null) { slot.objectReferenceValue = value; filled = true; } }
            Fill("alertSprite", alertSprite); Fill("lostSprite", lostSprite); Fill("alertSound", sound);
            if (filled) { so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
            AssetDatabase.SaveAssetIfDirty(settings);
            var player = AssetDatabase.LoadAssetAtPath<PlayerSettings>(AssetPaths.PlayerSettings);
            if (player != null)
            {
                var ps = new SerializedObject(player); var slot = ps.FindProperty("enemyAlert");
                if (slot.objectReferenceValue == null) { slot.objectReferenceValue = settings; ps.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player); }
            }
            Selection.activeObject = settings;
            return settings;
        }
        /// <summary>노란 "!": 둥근 막대와 점, 진한 테두리.</summary>
        public static Sprite EnsureAlertSprite()
        {
            var fill = new Color(1f, .82f, .2f);
            return GeneratedSprites.EnsureGeneratedSprite(AlertSprite, (u, v) =>
                GeneratedSprites.Shade(Mathf.Min(GeneratedSprites.RoundedBox(u, v - .56f, .08f, .26f, .07f), new Vector2(u, v - .15f).magnitude - .085f), fill));
        }
        /// <summary>하늘색 "?": 위쪽 고리, 아래로 꺾이는 줄기, 점.</summary>
        public static Sprite EnsureLostSprite()
        {
            var fill = new Color(.62f, .85f, 1f);
            var center = new Vector2(0, .66f); const float radius = .15f, half = .06f;
            var arcEnd = center + new Vector2(Mathf.Cos(-60 * Mathf.Deg2Rad), Mathf.Sin(-60 * Mathf.Deg2Rad)) * radius;
            var arcStart = center + new Vector2(Mathf.Cos(200 * Mathf.Deg2Rad), Mathf.Sin(200 * Mathf.Deg2Rad)) * radius;
            return GeneratedSprites.EnsureGeneratedSprite(LostSprite, (u, v) =>
            {
                var p = new Vector2(u, v);
                // 고리: -60°(오른쪽 아래)부터 위를 지나 200°(왼쪽)까지
                float angle = Mathf.Atan2(p.y - center.y, p.x - center.x) * Mathf.Rad2Deg; if (angle < -90) angle += 360;
                float ring = angle >= -60 && angle <= 200 ? Mathf.Abs((p - center).magnitude - radius) - half : 1;
                float cap = (p - arcStart).magnitude - half;
                float neck = Segment(p, arcEnd, new Vector2(0, .45f)) - half;
                float stem = Segment(p, new Vector2(0, .45f), new Vector2(0, .37f)) - half;
                float dot = (p - new Vector2(0, .17f)).magnitude - .08f;
                return GeneratedSprites.Shade(Mathf.Min(Mathf.Min(ring, cap), Mathf.Min(Mathf.Min(neck, stem), dot)), fill);
            });
        }
        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return (p - (a + ab * t)).magnitude;
        }
    }
}
