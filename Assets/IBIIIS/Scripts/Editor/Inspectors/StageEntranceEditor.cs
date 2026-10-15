using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>스테이지 입구 Inspector(StageLinkEditor와 같음).</summary>
    [CustomEditor(typeof(StageEntrance)), CanEditMultipleObjects]
    public sealed class StageEntranceEditor : StageLinkEditor { }
    /// <summary>어드벤처씬 적 Inspector. 입구와 같은 검사에, 대화 그래프의 이 적 대본 순서(Stage Id)와 그 전투 진입 대본이 전투 씬 진행기의 대본과 같은지 알려 준다.</summary>
    [CustomEditor(typeof(StageEnemy)), CanEditMultipleObjects]
    public sealed class StageEnemyEditor : StageLinkEditor
    {
        // 편집 중에는 게임이 쓰는 그래프(Overworld Settings의 Dialogue Graph)를 직접 읽는다.
        private static DialogueGraph ProjectGraph() => AssetDatabase.LoadAssetAtPath<OverworldSettings>(AssetPaths.OverworldSettings)?.DialogueGraph;
        private ScenarioScript BattleScript(out List<ScenarioScript> chain)
        {
            var enemy = (StageEnemy)target; var graph = ProjectGraph();
            chain = graph != null ? graph.Chain(enemy.StageId) : new List<ScenarioScript>();
            return chain.Find(s => s != null && !s.IsTalk);
        }
        protected override void AddExtra(VisualElement status)
        {
            var enemy = (StageEnemy)target;
            var battle = BattleScript(out var chain);
            if (string.IsNullOrEmpty(enemy.StageId)) return;
            if (chain.Count == 0) { status.Add(new HelpBox("대화 그래프에 이 적(Stage Id)의 대본 순서가 없어 대화 없이 전투에 들어갑니다. 대본은 IBIIIS Dialogue Editor의 그래프에서 잇습니다.", HelpBoxMessageType.Info)); return; }
            var order = new TextField("대본 순서(그래프)") { value = string.Join(" → ", chain.ConvertAll(s => s.name)), isReadOnly = true };
            order.AddToClassList(TextField.alignedFieldUssClassName); status.Add(order);
            if (battle == null || string.IsNullOrEmpty(enemy.BattleScenePath)) return;
            var directors = DialogueScriptScenes.DirectorsInProject().FindAll(e => e.ScenePath == enemy.BattleScenePath);
            if (directors.Count == 0)
                status.Add(new HelpBox("전투 씬에 대본 진행기가 없어 대본의 '입장 후' 구간은 실행되지 않습니다(입장 전·클리어 후 구간은 어드벤처씬에서 실행).", HelpBoxMessageType.Info));
            else if (!directors.Exists(e => e.Script == battle))
            {
                status.Add(new HelpBox($"전투 씬 진행기의 대본({(directors[0].Script != null ? directors[0].Script.name : "없음")})이 이 적의 전투 진입 대본({battle.name})과 다릅니다. '입장 후' 구간은 전투 씬 진행기의 대본으로 실행됩니다.", HelpBoxMessageType.Warning));
                status.Add(new Button(() => { DialogueScriptScenes.Connect(directors[0], battle, out string problem); if (problem != null) EditorUtility.DisplayDialog("전투 씬에 연결", problem, "확인"); }) { text = "전투 씬 진행기에 이 대본 연결" });
            }
        }
        protected override string ExtraKey { get { var battle = BattleScript(out var chain); return string.Join(",", chain.ConvertAll(s => s.name)) + "|" + (battle != null ? battle.name : ""); } }
    }
    /// <summary>전투 스테이지 연결(입구·어드벤처씬 적) Inspector 공통. 전투 씬을 씬 에셋으로 고르게 하고, ID 누락·중복과 빌드 씬 목록 누락을 알려 준다.</summary>
    public abstract class StageLinkEditor : UnityEditor.Editor
    {
        protected virtual void AddExtra(VisualElement status) { }
        protected virtual string ExtraKey => "";
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var property = serializedObject.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (property.name == "m_Script" || property.name == "battleScenePath") continue;
                if (property.name == "battleSceneAsset")
                {
                    // 바인딩된 필드라 여러 선택·프리팹 오버라이드 표시·우클릭 메뉴가 기본과 같다. 사용자가 바꿨을 때만 경로를 함께 쓴다.
                    var scene = new ObjectField("Battle Scene") { objectType = typeof(SceneAsset), allowSceneObjects = false, tooltip = "필수. 들어갈 전투 씬(맵과 함께 만든 .unity). Grid Map Player가 있어야 합니다." };
                    scene.AddToClassList(ObjectField.alignedFieldUssClassName);
                    scene.BindProperty(property.Copy());
                    scene.RegisterValueChangedCallback(evt =>
                    {
                        serializedObject.FindProperty("battleScenePath").stringValue = evt.newValue != null ? AssetDatabase.GetAssetPath(evt.newValue) : "";
                        serializedObject.ApplyModifiedProperties();
                    });
                    root.Add(scene);
                    continue;
                }
                root.Add(new PropertyField(property.Copy()));
            }
            if (targets.Length != 1) return root;
            // 경고는 다른 입구·빌드 씬 목록·클리어 기록에도 달려 있어 값이 바뀔 때와 주기적으로 다시 확인한다(내용이 같으면 다시 그리지 않음).
            var status = new VisualElement { style = { marginTop = 4 } };
            root.Add(status);
            string shown = null;
            void Refresh()
            {
                var entrance = target as IStageLink;
                if (entrance == null || target == null) return;
                var duplicate = FindDuplicate(entrance);
                var path = entrance.BattleScenePath;
                int index = string.IsNullOrEmpty(path) ? -2 : SceneUtilityIndex(path);
                bool disabled = index >= 0 && !EditorBuildSettings.scenes[index].enabled;
                bool hasId = !string.IsNullOrEmpty(entrance.StageId);
                string key = $"{entrance.Problem}|{(duplicate != null ? duplicate.name : null)}|{entrance.StageId}|{path}|{index}|{disabled}|{hasId && entrance.IsCleared}|{ExtraKey}";
                if (key == shown) return;
                shown = key; status.Clear();
                if (entrance.Problem != null) status.Add(new HelpBox(entrance.Problem + " 전투에 들어가지 않습니다.", HelpBoxMessageType.Error));
                if (duplicate != null) status.Add(new HelpBox($"Stage Id '{entrance.StageId}'를 '{duplicate.name}'도 쓰고 있습니다. 클리어 기록이 섞이므로 ID를 다르게 하세요.", HelpBoxMessageType.Error));
                if (index == -1)
                {
                    status.Add(new HelpBox("이 전투 씬은 빌드 씬 목록(File > Build Profiles)에 없습니다. 에디터 Play에서는 들어갈 수 있지만, 빌드한 게임에서는 들어갈 수 없습니다.", HelpBoxMessageType.Info));
                    status.Add(new Button(() => { AddToBuild(path); Refresh(); }) { text = "빌드 씬 목록에 추가" });
                }
                else if (disabled)
                {
                    status.Add(new HelpBox("이 전투 씬은 빌드 씬 목록에 있지만 꺼져 있습니다. 빌드한 게임에서는 들어갈 수 없습니다.", HelpBoxMessageType.Info));
                    status.Add(new Button(() => { EnableInBuild(index); Refresh(); }) { text = "빌드 씬 목록에서 켜기" });
                }
                AddExtra(status);
                if (hasId)
                {
                    var record = new TextField("클리어 기록") { value = entrance.IsCleared ? "클리어함" : "클리어 안 함", isReadOnly = true };
                    record.AddToClassList(TextField.alignedFieldUssClassName);
                    status.Add(record);
                }
            }
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.schedule.Execute(Refresh).Every(500);
            Refresh();
            return root;
        }
        // 같은 Stage Id를 쓰는 다른 입구·적(열린 씬). 클리어 기록이 섞인다.
        private MonoBehaviour FindDuplicate(IStageLink entrance)
        {
            if (string.IsNullOrEmpty(entrance.StageId)) return null;
            foreach (var other in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other != target && other is IStageLink link && link.StageId == entrance.StageId) return other;
            return null;
        }
        private static int SceneUtilityIndex(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++) if (scenes[i].path == path) return i;
            return -1;
        }
        private static void EnableInBuild(int index)
        {
            var scenes = EditorBuildSettings.scenes; scenes[index].enabled = true; EditorBuildSettings.scenes = scenes;
            Debug.Log($"[IBIIIS] 빌드 씬 목록에서 켬: {scenes[index].path}");
        }
        private static void AddToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes) { new EditorBuildSettingsScene(path, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[IBIIIS] 빌드 씬 목록에 추가: {path}");
        }
    }
}
