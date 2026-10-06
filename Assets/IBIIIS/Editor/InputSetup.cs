using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IBIIIS.Editor
{
    /// <summary>기본 전투 키 배치를 Input Actions 에셋으로 만들어 공용 플레이어 설정에 연결한다. 이미 있는 파일과 연결은 덮어쓰지 않는다.</summary>
    public static class InputSetup
    {
        [MenuItem("IBIIIS/Create Default Input Actions")]
        public static InputActionAsset EnsureDefault()
        {
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.InputActions);
            if (existing == null)
            {
                Directory.CreateDirectory(AssetPaths.Settings); AssetDatabase.Refresh();
                var created = BattleInput.CreateDefaultAsset();
                try { File.WriteAllText(AssetPaths.InputActions, created.ToJson()); }
                finally { Object.DestroyImmediate(created); }
                AssetDatabase.ImportAsset(AssetPaths.InputActions, ImportAssetOptions.ForceUpdate);
                existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.InputActions);
            }
            var settings = AssetDatabase.LoadAssetAtPath<PlayerSettings>(AssetPaths.PlayerSettings);
            if (settings != null && existing != null)
            {
                var so = new SerializedObject(settings); var property = so.FindProperty("inputActions");
                if (property.objectReferenceValue == null)
                {
                    property.objectReferenceValue = existing; so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
                }
            }
            Selection.activeObject = existing; EditorGUIUtility.PingObject(existing);
            return existing;
        }
    }
}
