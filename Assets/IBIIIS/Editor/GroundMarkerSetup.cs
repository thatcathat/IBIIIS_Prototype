using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>캐릭터 발밑 표시(그림자, 적 진행 방향 화살표)의 공용 텍스처·프리팹을 만들고 플레이어·적 프리팹에 중첩 프리팹으로 붙인다.
    /// 이미 있는 텍스처·프리팹·자식은 덮어쓰지 않는다. 그림을 바꾸려면 생성된 PNG를 교체하고, 크기·색·순서는 공용 프리팹에서 바꾼다.</summary>
    public static class GroundMarkerSetup
    {
        public const string Folder = AssetPaths.Root + "/Characters/Shared";
        public const string ShadowTexture = Folder + "/GroundShadow.png", ArrowTexture = Folder + "/FacingArrow.png";
        public const string ShadowPrefab = Folder + "/GroundShadow.prefab", ArrowPrefab = Folder + "/FacingArrow.prefab";
        public const string ShadowName = "GroundShadow", ArrowName = "FacingArrow";
        private const int Size = 128;

        [MenuItem("IBIIIS/Create Ground Markers (Shadow, Facing Arrow)")]
        public static void EnsureAll()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            // 그림자는 캐릭터 뒤(-1), 화살표는 4방향 스프라이트가 생기기 전까지 몸에 가려지지 않도록 캐릭터 위(1)에 그린다.
            var shadow = EnsurePrefab(ShadowPrefab, ShadowName, EnsureTexture(ShadowTexture, ShadowAlpha, Color.black), -1, .7f);
            var arrow = EnsurePrefab(ArrowPrefab, ArrowName, EnsureTexture(ArrowTexture, ArrowAlpha, Color.white), 1, .42f);
            Attach(PlayerPrefabSetup.PrefabPath, shadow, null);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { AssetPaths.Enemies }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<EnemyDefinition>() != null) Attach(path, shadow, arrow);
            }
        }

        private static Sprite EnsureTexture(string path, Func<float, float, float> alpha, Color color)
        {
            if (!File.Exists(path))
            {
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var pixels = new Color[Size * Size];
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    // 4×4 표본 평균으로 가장자리를 부드럽게 한다. 좌표는 -1~1, +y가 그림 위쪽(= 바닥에 눕혔을 때 앞쪽).
                    float sum = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                        sum += alpha((x + (sx + .5f) / 4) / Size * 2 - 1, (y + (sy + .5f) / 4) / Size * 2 - 1);
                    var a = sum / 16;
                    pixels[y * Size + x] = new Color(color.r, color.g, color.b, a);
                }
                texture.SetPixels(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = Size; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        // 부드러운 원형 그림자: 가운데 진하고 가장자리로 갈수록 사라진다.
        private static float ShadowAlpha(float x, float y)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return d >= 1 ? 0 : .55f * Mathf.Pow(1 - d, .8f);
        }
        // 앞(+y)을 가리키는 삼각형 화살촉. 흰 몸체에 반투명 테두리(색은 프리팹 렌더러 색으로 곱해진다).
        private static float ArrowAlpha(float x, float y)
        {
            var p = new Vector2(x, y); var a = new Vector2(0, .9f); var b = new Vector2(-.7f, -.5f); var c = new Vector2(.7f, -.5f);
            float inside = Mathf.Min(Side(p, a, b), Mathf.Min(Side(p, b, c), Side(p, c, a)));
            return inside >= .09f ? 1 : inside >= 0 ? .55f : 0;
        }
        private static float Side(Vector2 p, Vector2 from, Vector2 to)
        {
            var edge = (to - from).normalized; var normal = new Vector2(-edge.y, edge.x);
            return Vector2.Dot(p - from, normal);
        }

        private static GameObject EnsurePrefab(string path, string name, Sprite sprite, int sortingOrder, float scale)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject(name);
            try
            {
                // 바닥에 눕힌다: 그림의 +y가 부모의 +Z(앞)를 향한다.
                root.transform.localRotation = Quaternion.Euler(90, 0, 0); root.transform.localScale = new Vector3(scale, scale, 1);
                var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = sortingOrder;
                if (name == ArrowName) renderer.color = new Color(1, .93f, .55f, .95f);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>프리팹 루트 아래에 그림자·화살표가 없으면 붙인다. 적 프리팹의 이전 임시 방향 선(Direction 쿼드)은 화살표로 대체하므로 제거한다.</summary>
        private static void Attach(string path, GameObject shadow, GameObject arrow)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                if (shadow != null && root.transform.Find(ShadowName) == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(shadow, root.transform);
                    instance.name = ShadowName; instance.transform.localPosition = new Vector3(0, .03f, 0); changed = true;
                }
                if (arrow != null && root.transform.Find(ArrowName) == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(arrow, root.transform);
                    instance.name = ArrowName; instance.transform.localPosition = new Vector3(0, .04f, .12f); changed = true;
                    for (int i = root.transform.childCount - 1; i >= 0; i--)
                    {
                        var child = root.transform.GetChild(i);
                        if (child.name == "Direction" && child.GetComponent<MeshFilter>() != null) { UnityEngine.Object.DestroyImmediate(child.gameObject); }
                    }
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
