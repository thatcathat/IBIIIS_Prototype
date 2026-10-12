using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Editor
{
    /// <summary>기본 적 3종(Enemy001~003) 프리팹을 현재 규격으로 만든다: 루트에 EnemyDefinition, 자식 Visual(SpriteRenderer, 1.5배, 발밑 기준점 스프라이트),
    /// 발밑 그림자·진행 방향 화살표. 이미 있는 프리팹은 건드리지 않는다. 그림은 폴더의 &lt;이름&gt;.png가 있으면 그것을, 없으면 코드로 그린 임시 그림을 쓴다.</summary>
    public static class EnemyPrefabSetup
    {
        public const string Folder = AssetPaths.Enemies;
        // 임시 그림 색(맵 에디터 표시 색과 같음)
        private static readonly Color[] Colors = { new Color(.95f, .5f, .3f), new Color(.3f, .8f, .4f), new Color(.35f, .5f, 1f) };
        [MenuItem("IBIIIS/Create Default Enemy Prefabs")]
        public static void EnsureDefaults() => EnsureDefaults(Folder);
        /// <summary>folder 아래 Enemy00N/Enemy00N.prefab이 없을 때만 만든다(테스트는 임시 폴더를 쓴다). 만든 프리팹 경로를 돌려준다.</summary>
        public static List<string> EnsureDefaults(string folder)
        {
            var created = new List<string>();
            for (int type = 1; type <= 3; type++)
            {
                string name = "Enemy00" + type, typeFolder = folder + "/" + name, path = typeFolder + "/" + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                AssetPaths.EnsureFolder(typeFolder);
                var color = Colors[type - 1];
                var sprite = EnsureSprite(typeFolder, name, color);
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene);
                    var definition = root.AddComponent<EnemyDefinition>();
                    var so = new SerializedObject(definition);
                    so.FindProperty("enemyId").stringValue = name; so.FindProperty("moveCells").intValue = type == 2 ? 2 : 1;
                    so.ApplyModifiedPropertiesWithoutUndo(); EnemyDefinitionEditor.ConvertLegacy(so);
                    so.FindProperty("editorColor").colorValue = color;
                    if (type == 3) SetOffsets(so.FindProperty("recognition"), new[] { Vector2Int.left, Vector2Int.right, new Vector2Int(-1,1), Vector2Int.up, Vector2Int.one });
                    SetOffsets(so.FindProperty("attack"), type == 1 ? new[] { Vector2Int.zero } : type == 2 ? new[] { Vector2Int.zero, Vector2Int.up } : new[] { Vector2Int.zero, Vector2Int.up, Vector2Int.one });
                    SetOffsets(so.FindProperty("recognizedAttack"), RecognizedAttack(type));
                    // 플레이어·NPC와 같은 2.5D 표시: 카메라와 나란히 서는 스프라이트(GridMapPlayer가 Visual을 카메라 쪽으로 돌린다).
                    var visual = new GameObject("Visual", typeof(SpriteRenderer)); visual.transform.SetParent(root.transform, false);
                    visual.transform.localPosition = new Vector3(0, .02f, 0); visual.transform.localScale = new Vector3(1.5f, 1.5f, 1);
                    visual.GetComponent<SpriteRenderer>().sprite = sprite;
                    so.FindProperty("visual").objectReferenceValue = visual.transform; so.ApplyModifiedPropertiesWithoutUndo();
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new System.IO.IOException($"적 프리팹을 저장하지 못했습니다: {path}");
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
                // 진행 방향은 공용 발밑 화살표로 표시한다(4방향 스프라이트 도입 전 임시). 새로 만든 프리팹에만 붙이고 기존 적은 건드리지 않는다.
                GroundMarkerSetup.AttachTo(path, true);
                created.Add(path);
            }
            return created;
        }
        private static Vector2Int[] RecognizedAttack(int type)
            => type == 1 ? new[] { Vector2Int.zero, Vector2Int.up } : type == 2 ? new[] { Vector2Int.zero, Vector2Int.up, Vector2Int.up * 2 } : new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Vector2Int.one };
        // 그림: 폴더에 사용자 그림(<이름>.png)이 있으면 그대로 쓰고, 없으면 둥근 몸통과 눈의 임시 그림(<이름>_Placeholder.png)을 만든다.
        private static Sprite EnsureSprite(string folder, string name, Color color)
        {
            var drawn = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/" + name + ".png");
            if (drawn != null) return drawn;
            return GeneratedSprites.EnsureGeneratedSprite(folder + "/" + name + "_Placeholder.png", (u, v) =>
            {
                float eye = Mathf.Min(new Vector2(u + .1f, v - .5f).magnitude, new Vector2(u - .1f, v - .5f).magnitude) - .045f;
                if (eye < 0) return new Color(.1f, .1f, .14f, 1);
                return GeneratedSprites.Shade(GeneratedSprites.RoundedBox(u, v - .38f, .3f, .34f, .2f), color);
            });
        }
        private static void SetOffsets(SerializedProperty property, Vector2Int[] offsets)
        {
            property.arraySize = offsets.Length;
            for (int i = 0; i < offsets.Length; i++) property.GetArrayElementAtIndex(i).vector2IntValue = offsets[i];
        }
    }
}
