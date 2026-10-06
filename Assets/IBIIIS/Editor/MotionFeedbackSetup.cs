using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>이동 손맛의 임시 에셋(착지 먼지 스프라이트·프리팹, 발소리·막힌 입력 효과음 WAV)과 설정 에셋을 만들고 공용 플레이어 설정에 연결한다.
    /// 이미 있는 파일·연결은 덮어쓰지 않는다. 정식 그림·소리는 생성된 파일을 교체하거나 설정 슬롯을 바꿔 연결한다.</summary>
    public static class MotionFeedbackSetup
    {
        public const string DustTexture = AssetPaths.Effects + "/LandingDust.png", DustPrefab = AssetPaths.Effects + "/LandingDust.prefab";
        public const string FootstepSound = AssetPaths.Audio + "/Footstep.wav", BumpSound = AssetPaths.Audio + "/Bump.wav";
        public const string DashSound = AssetPaths.Audio + "/DashWhoosh.wav", RollSound = AssetPaths.Audio + "/RollSwish.wav";
        public const string EnemyFootstepSound = AssetPaths.Audio + "/EnemyStep.wav";
        private const int Size = 64, SampleRate = 44100;

        [MenuItem("IBIIIS/Create Default Motion Feedback")]
        public static MotionFeedbackSettings EnsureDefault()
        {
            Directory.CreateDirectory(AssetPaths.Effects); Directory.CreateDirectory(AssetPaths.Audio); AssetDatabase.Refresh();
            var dust = EnsureDustPrefab();
            // 가볍고 짧은 "톡": 높게 시작해 내려가는 짧은 음 + 아주 짧은 잡음
            var footstep = EnsureSound(FootstepSound, .09f, (t, noise) => (float)Math.Sin(2 * Math.PI * Mathf.Lerp(900, 520, t / .09f) * t) * Mathf.Exp(-t * 45) + noise * Mathf.Exp(-t * 120) * .4f);
            // 둔한 "툭": 낮은 음이 빨리 사라짐
            var bump = EnsureSound(BumpSound, .12f, (t, noise) => (float)Math.Sin(2 * Math.PI * Mathf.Lerp(260, 140, t / .12f) * t) * Mathf.Exp(-t * 30) + noise * Mathf.Exp(-t * 90) * .3f);
            // 대시 "휙": 점점 커졌다 사라지는 바람 소리(잡음을 부드럽게 거른 것). 구르기 "슥": 더 짧고 낮은 바람 소리.
            var dash = EnsureNoise(DashSound, .2f, .35f, t => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / .2f)) * Mathf.Exp(-t * 4));
            var roll = EnsureNoise(RollSound, .15f, .15f, t => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / .15f)));
            // 적 발소리 "쿵": 낮게 떨어지는 음 + 짧은 잡음, 플레이어 발소리보다 무겁게.
            var enemyStep = EnsureSound(EnemyFootstepSound, .16f, (t, noise) => (float)Math.Sin(2 * Math.PI * Mathf.Lerp(150, 70, t / .16f) * t) * Mathf.Exp(-t * 22) + noise * Mathf.Exp(-t * 70) * .35f);
            var settings = AssetDatabase.LoadAssetAtPath<MotionFeedbackSettings>(AssetPaths.MotionFeedback);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MotionFeedbackSettings>();
                AssetDatabase.CreateAsset(settings, AssetPaths.MotionFeedback);
            }
            // 비어 있는 슬롯만 채운다(사용자가 연결한 에셋은 덮어쓰지 않음).
            var serialized = new SerializedObject(settings); bool filled = false;
            void Fill(string name, UnityEngine.Object value) { var slot = serialized.FindProperty(name); if (slot.objectReferenceValue == null && value != null) { slot.objectReferenceValue = value; filled = true; } }
            Fill("landingDust", dust); Fill("footstepSound", footstep); Fill("bumpSound", bump); Fill("dashSound", dash); Fill("rollSound", roll); Fill("enemyFootstepSound", enemyStep);
            if (filled) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
            AssetDatabase.SaveAssetIfDirty(settings);
            var player = AssetDatabase.LoadAssetAtPath<PlayerSettings>(AssetPaths.PlayerSettings);
            if (player != null)
            {
                var so = new SerializedObject(player); var slot = so.FindProperty("motionFeedback");
                if (slot.objectReferenceValue == null) { slot.objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player); }
            }
            Selection.activeObject = settings;
            return settings;
        }

        private static GameObject EnsureDustPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DustPrefab);
            if (existing != null) return existing;
            if (!File.Exists(DustTexture))
            {
                // 뭉게뭉게한 흰 먼지 한 덩이: 원 세 개를 겹친 부드러운 모양
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false); var pixels = new Color[Size * Size];
                var blobs = new[] { new Vector3(-.3f, -.15f, .55f), new Vector3(.3f, -.2f, .5f), new Vector3(0, .15f, .6f) };
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    float px = (x + .5f) / Size * 2 - 1, py = (y + .5f) / Size * 2 - 1, a = 0;
                    foreach (var b in blobs) { float d = Vector2.Distance(new Vector2(px, py), new Vector2(b.x, b.y)) / b.z; a = Mathf.Max(a, Mathf.Clamp01((1 - d) * 3)); }
                    pixels[y * Size + x] = new Color(.92f, .9f, .86f, a * .85f);
                }
                texture.SetPixels(pixels); File.WriteAllBytes(DustTexture, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(DustTexture);
                var importer = (TextureImporter)AssetImporter.GetAtPath(DustTexture);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = Size; importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            }
            var root = new GameObject("LandingDust");
            try
            {
                var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DustTexture);
                renderer.sortingOrder = 2; // 발밑 앞쪽에 보이도록 캐릭터 위에 그린다
                return PrefabUtility.SaveAsPrefabAsset(root, DustPrefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // 잡음을 한 단계 저역 통과로 부드럽게 만든 바람 소리. smoothing이 클수록 낮고 부드럽다.
        private static AudioClip EnsureNoise(string path, float seconds, float smoothing, Func<float, float> envelope)
        {
            float filtered = 0;
            return EnsureSound(path, seconds, (t, noise) => { filtered += (noise - filtered) * (1 - smoothing); return filtered * envelope(t); });
        }
        private static AudioClip EnsureSound(string path, float seconds, Func<float, float, float> wave)
        {
            if (!File.Exists(path))
            {
                int count = (int)(SampleRate * seconds); var samples = new float[count]; var random = new System.Random(path.Length); float peak = 0;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    samples[i] = wave(t, (float)random.NextDouble() * 2 - 1) * Mathf.Clamp01(t / .002f);
                    peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                }
                using (var stream = new FileStream(path, FileMode.CreateNew))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(new[] { 'R', 'I', 'F', 'F' }); writer.Write(36 + count * 2); writer.Write(new[] { 'W', 'A', 'V', 'E' });
                    writer.Write(new[] { 'f', 'm', 't', ' ' }); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                    writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(new[] { 'd', 'a', 't', 'a' }); writer.Write(count * 2);
                    foreach (var sample in samples) writer.Write((short)Mathf.Clamp(sample / Mathf.Max(.0001f, peak) * .8f * short.MaxValue, short.MinValue, short.MaxValue));
                }
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
