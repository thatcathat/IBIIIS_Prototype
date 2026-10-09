using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>충돌 연출의 임시 에셋(별 모양 충격 스프라이트·프리팹, 효과음 WAV)과 설정 에셋을 만들고 공용 플레이어 설정에 연결한다.
    /// 이미 있는 파일·연결은 덮어쓰지 않는다. 정식 그림·소리는 생성된 파일을 교체하거나 설정 슬롯을 바꿔 연결한다.</summary>
    public static class CollisionFeedbackSetup
    {
        public const string BurstTexture = AssetPaths.Effects + "/CollisionBurst.png", BurstPrefab = AssetPaths.Effects + "/CollisionBurst.prefab";
        public const string ImpactSound = AssetPaths.Audio + "/CollisionImpact.wav";
        private const int Size = 128, SampleRate = 44100;

        [MenuItem("IBIIIS/Create Default Collision Feedback")]
        public static CollisionFeedbackSettings EnsureDefault()
        {
            AssetPaths.EnsureFolder(AssetPaths.Effects); AssetPaths.EnsureFolder(AssetPaths.Audio);
            var burst = EnsureBurstPrefab();
            var sound = EnsureSound();
            var settings = AssetDatabase.LoadAssetAtPath<CollisionFeedbackSettings>(AssetPaths.CollisionFeedback);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CollisionFeedbackSettings>();
                AssetDatabase.CreateAsset(settings, AssetPaths.CollisionFeedback);
            }
            // 에셋이 이미 있어도 비어 있는 슬롯만 기본 충격 연출로 채운다. 연결된 슬롯은 그대로 둔다.
            var serialized = new SerializedObject(settings); bool filled = false;
            void Fill(string name, UnityEngine.Object value) { var slot = serialized.FindProperty(name); if (slot.objectReferenceValue == null && value != null) { slot.objectReferenceValue = value; filled = true; } }
            Fill("impactPrefab", burst); Fill("impactSound", sound);
            if (filled) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
            AssetDatabase.SaveAssetIfDirty(settings);
            var player = AssetDatabase.LoadAssetAtPath<PlayerSettings>(AssetPaths.PlayerSettings);
            if (player != null)
            {
                var so = new SerializedObject(player); var slot = so.FindProperty("collisionFeedback");
                if (slot.objectReferenceValue == null) { slot.objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player); }
            }
            Selection.activeObject = settings;
            return settings;
        }

        private static GameObject EnsureBurstPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPrefab);
            if (existing != null) return existing;
            if (!File.Exists(BurstTexture))
            {
                // 만화풍 8각 별: 노란 몸체, 주황 테두리. 4×4 표본으로 가장자리를 부드럽게 한다.
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false); var pixels = new Color[Size * Size];
                var fill = new Color(1f, .93f, .35f); var edge = new Color(1f, .45f, .1f);
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    float body = 0, outline = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                    {
                        float px = (x + (sx + .5f) / 4) / Size * 2 - 1, py = (y + (sy + .5f) / 4) / Size * 2 - 1;
                        float r = Mathf.Sqrt(px * px + py * py), angle = Mathf.Atan2(py, px);
                        float limit = Mathf.Lerp(.5f, .98f, Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4)), 3));
                        if (r <= limit * .82f) body++; else if (r <= limit) outline++;
                    }
                    float a = (body + outline) / 16;
                    var c = body + outline > 0 ? Color.Lerp(edge, fill, body / (body + outline)) : fill;
                    pixels[y * Size + x] = new Color(c.r, c.g, c.b, a);
                }
                texture.SetPixels(pixels); File.WriteAllBytes(BurstTexture, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(BurstTexture);
                var importer = (TextureImporter)AssetImporter.GetAtPath(BurstTexture);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = Size; importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            }
            var root = new GameObject("CollisionBurst");
            try
            {
                var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BurstTexture);
                renderer.sortingOrder = 5; // 캐릭터 위에 그린다
                return PrefabUtility.SaveAsPrefabAsset(root, BurstPrefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static AudioClip EnsureSound()
        {
            if (!File.Exists(ImpactSound))
            {
                // 임시 "쿵·퍽" 소리: 낮게 떨어지는 사인파 + 짧은 잡음 + 높은 톡 소리, 0.3초 모노 16비트.
                int count = (int)(SampleRate * .3f); var samples = new float[count]; var random = new System.Random(7);
                double phase = 0, popPhase = 0; float peak = 0;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    float frequency = 55 + 130 * Mathf.Exp(-t * 18);
                    phase += 2 * Math.PI * frequency / SampleRate; popPhase += 2 * Math.PI * 620 / SampleRate;
                    float thump = (float)Math.Sin(phase) * Mathf.Exp(-t * 12);
                    float noise = ((float)random.NextDouble() * 2 - 1) * Mathf.Exp(-t * 45) * .6f;
                    float pop = (float)Math.Sin(popPhase) * Mathf.Exp(-t * 60) * .35f;
                    float attack = Mathf.Clamp01(t / .002f);
                    samples[i] = (thump + noise + pop) * attack; peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                }
                using (var stream = new FileStream(ImpactSound, FileMode.CreateNew))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(new[] { 'R', 'I', 'F', 'F' }); writer.Write(36 + count * 2); writer.Write(new[] { 'W', 'A', 'V', 'E' });
                    writer.Write(new[] { 'f', 'm', 't', ' ' }); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                    writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(new[] { 'd', 'a', 't', 'a' }); writer.Write(count * 2);
                    foreach (var sample in samples) writer.Write((short)Mathf.Clamp(sample / Mathf.Max(.0001f, peak) * .85f * short.MaxValue, short.MinValue, short.MaxValue));
                }
                AssetDatabase.ImportAsset(ImpactSound);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactSound);
        }
    }
}
