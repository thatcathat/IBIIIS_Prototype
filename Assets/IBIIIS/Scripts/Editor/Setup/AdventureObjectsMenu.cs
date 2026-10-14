using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>어드벤처씬에 적(Stage Enemy)·근접 대화 구역(Dialogue Zone)을 놓는 메뉴(GameObject > IBIIIS, Hierarchy 우클릭에도 나옴).
    /// 누를 때마다 새 오브젝트를 Scene 뷰 가운데 바닥(높이 0)에 하나 만들고 고른다(Ctrl+Z로 취소). 씬 저장은 사용자가 한다.</summary>
    public static class AdventureObjectsMenu
    {
        [MenuItem("GameObject/IBIIIS/Stage Enemy (어드벤처씬 적)", false, 10)]
        public static void CreateStageEnemy(MenuCommand command)
        {
            var go = Create("Stage Enemy", command);
            var enemy = go.AddComponent<StageEnemy>();
            var so = new SerializedObject(enemy);
            so.FindProperty("stageId").stringValue = UniqueId("stage-");
            so.ApplyModifiedPropertiesWithoutUndo();
            // 표시: 기본 적 그림(없으면 그림 없이). 카메라를 향하는 스프라이트, 발밑 기준.
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetPaths.EnemySprites + "/Enemy001.png");
            var visual = new GameObject("Visual", typeof(SpriteRenderer), typeof(CameraFacingSprite));
            visual.transform.SetParent(go.transform, false); visual.transform.localScale = Vector3.one * 1.5f;
            visual.GetComponent<SpriteRenderer>().sprite = sprite;
            if (sprite == null) Debug.LogWarning($"[IBIIIS] 적 그림({AssetPaths.EnemySprites}/Enemy001.png)이 없어 그림 없이 만들었습니다. Visual의 Sprite를 지정하세요.", go);
            Selection.activeGameObject = go;
        }
        [MenuItem("GameObject/IBIIIS/Dialogue Zone (근접 대화 구역)", false, 11)]
        public static void CreateDialogueZone(MenuCommand command)
        {
            var go = Create("Dialogue Zone", command);
            var zone = go.AddComponent<DialogueZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("zoneId").stringValue = UniqueId("zone-");
            so.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = go;
        }
        private static GameObject Create(string name, MenuCommand command)
        {
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, name));
            var view = SceneView.lastActiveSceneView;
            var position = view != null ? view.pivot : Vector3.zero; position.y = 0;
            go.transform.position = position;
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            SceneManager_Move(go);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }
        private static void SceneManager_Move(GameObject go)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (go.transform.parent == null && scene.IsValid() && go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }
        // 열린 씬에서 겹치지 않는 ID 제안(prefix + 번호). 정한 뒤에는 바꾸지 않는다.
        private static string UniqueId(string prefix)
        {
            var used = new System.Collections.Generic.HashSet<string>();
            foreach (var link in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (link is IStageLink stage && !string.IsNullOrEmpty(stage.StageId)) used.Add(stage.StageId);
                if (link is DialogueZone zone && !string.IsNullOrEmpty(zone.ZoneId)) used.Add(zone.ZoneId);
            }
            for (int i = 1; ; i++) if (!used.Contains(prefix + i)) return prefix + i;
        }
    }
}
