using System;
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
            var existing = LoadOrCreate(AssetPaths.InputActions, BattleInput.CreateDefaultAsset);
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
            if (existing != null) { Selection.activeObject = existing; EditorGUIUtility.PingObject(existing); }
            return existing;
        }
        /// <summary>입력 파일을 불러오고, 파일이 아예 없을 때만 기본 배치로 만든다.
        /// 파일은 있는데 가져오기에 실패했으면(JSON 오류·이름 중복 등) 사용자의 키 배치를 지키려고 덮어쓰지 않고, 오류를 남긴 뒤 null을 돌려준다.</summary>
        public static InputActionAsset LoadOrCreate(string path, Func<InputActionAsset> createDefault)
        {
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (existing != null) return existing;
            if (File.Exists(path))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (existing == null) Debug.LogError($"[IBIIIS] 입력 파일을 불러오지 못했습니다: {path}. Console의 Import 오류(JSON 형식·이름 중복)를 확인해 고치세요. 키 배치를 보존하려고 기본 배치로 덮어쓰지 않았습니다.");
                return existing;
            }
            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) { Directory.CreateDirectory(folder); AssetDatabase.Refresh(); }
            var created = createDefault();
            try { File.WriteAllText(path, created.ToJson()); }
            finally { UnityEngine.Object.DestroyImmediate(created); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }
    }
}
