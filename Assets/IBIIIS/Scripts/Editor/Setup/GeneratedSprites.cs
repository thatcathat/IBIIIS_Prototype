using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IBIIIS.Editor
{
    /// <summary>코드로 그리는 임시 스프라이트(256×256, PPU 256, 기준점 아래 중앙)와 도형 거리 함수. NPC 실루엣·클리어 깃발·적 인식 표시가 함께 쓴다.</summary>
    public static class GeneratedSprites
    {
        public static readonly Color Outline = new Color(.18f, .22f, .32f);
        // 거리 d(안쪽 음수)에 따라 채움/테두리/투명을 고른다. 테두리 두께는 캔버스 너비의 2%.
        public static Color Shade(float d, Color fill) => d < -.02f ? new Color(fill.r, fill.g, fill.b, 1) : d < 0 ? new Color(Outline.r, Outline.g, Outline.b, 1) : Color.clear;
        /// <summary>코드로 그린 256×256 임시 스프라이트를 PNG로 저장하고 스프라이트(PPU 256, 기준점 아래 중앙)로 가져온다. 파일이 있으면 그대로 쓴다.
        /// sample(u, v): u는 -0.5~0.5(가로), v는 0~1(아래→위). 4×4 표본 평균으로 가장자리를 부드럽게 한다.</summary>
        public static Sprite EnsureGeneratedSprite(string path, Func<float, float, Color> sample)
        {
            const int size = 256;
            if (!File.Exists(path))
            {
                AssetPaths.EnsureFolder(Path.GetDirectoryName(path));
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                    {
                        var c = sample((x + (sx + .5f) / 4) / size - .5f, (y + (sy + .5f) / 4) / size);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                    var avg = sum / 16; pixels[y * size + x] = avg.a > 0 ? new Color(avg.r / avg.a, avg.g / avg.a, avg.b / avg.a, avg.a) : Color.clear;
                }
                texture.SetPixels(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter; settings.spritePivot = new Vector2(.5f, 0);
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        public static float RoundedBox(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            var q = new Vector2(Mathf.Abs(x) - halfWidth + radius, Mathf.Abs(y) - halfHeight + radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
        }
        // 삼각형까지의 부호 있는 거리(안쪽 음수).
        public static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 e0 = b - a, e1 = c - b, e2 = a - c, v0 = p - a, v1 = p - b, v2 = p - c;
            Vector2 q0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 q1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 q2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            var d = Vector2.Min(Vector2.Min(new Vector2(Vector2.Dot(q0, q0), s * (v0.x * e0.y - v0.y * e0.x)),
                new Vector2(Vector2.Dot(q1, q1), s * (v1.x * e1.y - v1.y * e1.x))), new Vector2(Vector2.Dot(q2, q2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }
    }
}
