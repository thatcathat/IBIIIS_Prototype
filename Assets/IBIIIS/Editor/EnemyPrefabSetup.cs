using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Editor
{
    public static class EnemyPrefabSetup
    {
        public const string Folder = AssetPaths.Enemies;
        [MenuItem("IBIIIS/Create Default Enemy Prefabs")]
        public static void EnsureDefaults()
        {
            for (int type = 1; type <= 3; type++)
            {
                string name = "Enemy00" + type, folder = Folder + "/" + name, path = folder + "/" + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene);
                    var definition = root.AddComponent<EnemyDefinition>();
                    var so = new SerializedObject(definition);
                    so.FindProperty("enemyId").stringValue = name; so.FindProperty("moveCells").intValue = type == 2 ? 2 : 1;
                    so.ApplyModifiedPropertiesWithoutUndo(); EnemyDefinitionEditor.ConvertLegacy(so);
                    var color = type == 1 ? new Color(.95f,.5f,.3f) : type == 2 ? new Color(.3f,.8f,.4f) : new Color(.35f,.5f,1f);
                    so.FindProperty("editorColor").colorValue = color;
                    if (type == 3) SetOffsets(so.FindProperty("recognition"), new[] { Vector2Int.left, Vector2Int.right, new Vector2Int(-1,1), Vector2Int.up, Vector2Int.one });
                    SetOffsets(so.FindProperty("attack"), type == 1 ? new[] { Vector2Int.zero } : type == 2 ? new[] { Vector2Int.zero, Vector2Int.up } : new[] { Vector2Int.zero, Vector2Int.up, Vector2Int.one });
                    SetOffsets(so.FindProperty("recognizedAttack"), type == 1 ? new[] { Vector2Int.zero, Vector2Int.up } : type == 2 ? new[] { Vector2Int.zero, Vector2Int.up, Vector2Int.up * 2 } : new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Vector2Int.one });
                    var shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) throw new System.InvalidOperationException("URP/Unlit 셰이더가 없습니다.");
                    string matPath = folder + "/" + name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null) { mat = new Material(shader) { color = color }; AssetDatabase.CreateAsset(mat, matPath); }
                    var visual = GameObject.CreatePrimitive(PrimitiveType.Quad); visual.name = "Visual"; visual.transform.SetParent(root.transform, false);
                    visual.transform.localPosition = new Vector3(0,.45f,0); visual.transform.localScale = Vector3.one * .65f;
                    Object.DestroyImmediate(visual.GetComponent<Collider>()); visual.GetComponent<Renderer>().sharedMaterial = mat;
                    so.FindProperty("visual").objectReferenceValue = visual.transform; so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssetIfDirty(mat);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            // 진행 방향은 공용 발밑 화살표로 표시한다(4방향 스프라이트 도입 전 임시). 그림자도 함께 붙인다.
            GroundMarkerSetup.EnsureAll();
        }
        private static void SetOffsets(SerializedProperty property, Vector2Int[] offsets)
        {
            property.arraySize = offsets.Length;
            for (int i = 0; i < offsets.Length; i++) property.GetArrayElementAtIndex(i).vector2IntValue = offsets[i];
        }
    }
}