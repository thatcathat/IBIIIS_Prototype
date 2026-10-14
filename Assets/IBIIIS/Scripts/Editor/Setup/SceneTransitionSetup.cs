using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>씬 전환 설정 에셋(Resources/IBIIIS/SceneTransitionSettings.asset)을 없을 때만 만들고 고른다. 이미 있으면 그대로 연다.</summary>
    public static class SceneTransitionSetup
    {
        public const string AssetPath = AssetPaths.DefaultResources + "/SceneTransitionSettings.asset";
        [MenuItem("IBIIIS/Scene Transition Settings")]
        public static void Open() { Selection.activeObject = EnsureDefault(); }
        public static SceneTransitionSettings EnsureDefault()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SceneTransitionSettings>(AssetPath);
            if (settings != null) return settings;
            AssetPaths.EnsureFolder(AssetPaths.DefaultResources);
            settings = ScriptableObject.CreateInstance<SceneTransitionSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }
    }
}
