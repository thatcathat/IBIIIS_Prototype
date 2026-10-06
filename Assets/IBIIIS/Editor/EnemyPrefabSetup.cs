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
                    for (int i = 0; i < 3; i++)
                    {
                        var line = GameObject.CreatePrimitive(PrimitiveType.Quad); line.name = "Direction"; line.transform.SetParent(root.transform, false);
                        line.transform.localPosition = new Vector3(i == 0 ? 0 : i == 1 ? -.07f : .07f, .035f, i == 0 ? .25f : .36f);
                        line.transform.localRotation = Quaternion.Euler(90, i == 0 ? 0 : i == 1 ? -45 : 45, 0);
                        line.transform.localScale = new Vector3(.04f, i == 0 ? .4f : .2f, 1);
                        Object.DestroyImmediate(line.GetComponent<Collider>()); line.GetComponent<Renderer>().sharedMaterial = mat;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssetIfDirty(mat);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
        private static void SetOffsets(SerializedProperty property, Vector2Int[] offsets)
        {
            property.arraySize = offsets.Length;
            for (int i = 0; i < offsets.Length; i++) property.GetArrayElementAtIndex(i).vector2IntValue = offsets[i];
        }
    }
}