using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Editor
{
    /// <summary>번역 표에 넣을 원문을 프로젝트 데이터에서 모은다. 표·키 규칙:
    /// 대본(Scenario_대본 ID): 단계 ID(본문), 단계 ID.name(이름표 덮어쓰기), 단계 ID.keys(한글 키 표시), 단계 ID.default(이름 입력 기본값) /
    /// 대화(Dialogue_대화 ID): 줄 ID / 인물(Characters): 인물 ID / 스테이지(Stages): Stage Id /
    /// NPC 말풍선(NPC): NPC ID.name, NPC ID.순번 / 전투 말풍선(BattleDialogue): 말풍선 ID.spotted.순번.player|enemy, 말풍선 ID.lost.순번.
    /// 모으는 동안 비어 있는 단계·줄 ID는 채워 저장한다. 씬 안의 스테이지 입구·NPC는 Scenes 폴더 씬을 잠깐 더해 열어 읽고 닫는다(저장하지 않음).</summary>
    public static class LocalizationSources
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Collect(List<LocalizedEntry> list)
        {
            Warnings.Clear();
            Scenarios(list); Dialogues(list); Characters(list); BattleDialogues(list); SceneObjects(list);
        }
        private static IEnumerable<T> AssetsOf<T>() where T : Object
            => AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { AssetPaths.Root }).Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null);
        // ID는 불러올 때(OnValidate) 메모리에서 먼저 채워질 수 있어 "바뀜"만으로는 알 수 없다.
        // 메모리의 ID가 파일에 하나라도 없으면 저장해, 번역 키가 실행마다 바뀌지 않게 한다.
        private static void Persist(Object asset, bool changed, IEnumerable<string> ids)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return;
            var file = System.IO.File.ReadAllText(path);
            if (!changed && ids.All(id => !string.IsNullOrEmpty(id) && file.Contains(id))) return;
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
        }
        private static string Who(DialogueLine line) => !string.IsNullOrEmpty(line.nameOverride) ? line.nameOverride : line.speaker != null ? line.speaker.DisplayName : "지문";

        private static void Scenarios(List<LocalizedEntry> list)
        {
            foreach (var script in AssetsOf<ScenarioScript>())
            {
                Persist(script, script.EnsureStepIds(), script.Steps.Select(s => s.id));
                string table = GameText.Scenario(script.Id);
                var steps = script.Steps;
                for (int i = 0; i < steps.Length; i++)
                {
                    var s = steps[i]; string where = $"{script.Id} {i}번";
                    bool hasLine = s.type == ScenarioStepType.Line || !string.IsNullOrEmpty(s.line.text);
                    if (hasLine && !string.IsNullOrEmpty(s.line.text)) list.Add(new LocalizedEntry(table, s.id, s.line.text, null, $"{where} · {Who(s.line)}"));
                    if (hasLine && !string.IsNullOrEmpty(s.line.nameOverride)) list.Add(new LocalizedEntry(table, s.id + ".name", s.line.nameOverride, null, $"{where} · 이름표"));
                    if (GameText.NeedsTranslation(s.keys)) list.Add(new LocalizedEntry(table, s.id + ".keys", s.keys, null, $"{where} · 키 표시(공백으로 나눔)"));
                    if (s.type == ScenarioStepType.NameInput && GameText.NeedsTranslation(s.text)) list.Add(new LocalizedEntry(table, s.id + ".default", s.text, null, $"{where} · 이름 입력 기본값"));
                }
            }
        }
        private static void Dialogues(List<LocalizedEntry> list)
        {
            foreach (var dialogue in AssetsOf<DialogueScript>())
            {
                Persist(dialogue, dialogue.EnsureLineIds(), dialogue.Lines.Select(l => l.id));
                var lines = dialogue.Lines;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(lines[i].text)) list.Add(new LocalizedEntry(dialogue.TableName, lines[i].id, lines[i].text, null, $"{dialogue.Id} {i}번 · {Who(lines[i])}"));
                    if (!string.IsNullOrEmpty(lines[i].nameOverride)) list.Add(new LocalizedEntry(dialogue.TableName, lines[i].id + ".name", lines[i].nameOverride, null, $"{dialogue.Id} {i}번 · 이름표"));
                }
            }
        }
        private static void Characters(List<LocalizedEntry> list)
        {
            foreach (var character in AssetsOf<DialogueCharacter>())
                if (!string.IsNullOrEmpty(character.DisplayName)) list.Add(new LocalizedEntry(GameText.CharactersTable, character.Id, character.DisplayName, null, "인물 이름"));
        }
        private static void BattleDialogues(List<LocalizedEntry> list)
        {
            foreach (var dialogue in AssetsOf<BattleDialogue>())
            {
                var spotted = dialogue.SourceSpotted; var lost = dialogue.SourceLost; string t = GameText.BattleDialogueTable, id = dialogue.Id;
                for (int i = 0; i < spotted.Length; i++)
                {
                    if (!string.IsNullOrEmpty(spotted[i].player)) list.Add(new LocalizedEntry(t, $"{id}.spotted.{i}.player", spotted[i].player, null, $"전투 말풍선 {id} · 인식 {i}번 플레이어"));
                    if (!string.IsNullOrEmpty(spotted[i].enemy)) list.Add(new LocalizedEntry(t, $"{id}.spotted.{i}.enemy", spotted[i].enemy, null, $"전투 말풍선 {id} · 인식 {i}번 적 대답"));
                }
                for (int i = 0; i < lost.Length; i++)
                    if (!string.IsNullOrEmpty(lost[i])) list.Add(new LocalizedEntry(t, $"{id}.lost.{i}", lost[i], null, $"전투 말풍선 {id} · 놓침 {i}번"));
            }
        }
        // 씬 안의 스테이지 입구(이름)와 NPC(이름·말풍선 대사). 열린 씬은 그대로 읽고, 닫힌 씬은 잠깐 더해 열었다 닫는다.
        private static void SceneObjects(List<LocalizedEntry> list)
        {
            foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] { AssetPaths.Scenes }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = SceneManager.GetSceneByPath(path); bool opened = false;
                if (!scene.isLoaded)
                {
                    if (!SceneMentions(path)) continue; // 스테이지 입구·NPC가 없는 씬은 열지 않는다
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true;
                }
                try
                {
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var stage in root.GetComponentsInChildren<StageEntrance>(true))
                            if (!string.IsNullOrEmpty(stage.StageId) && !string.IsNullOrEmpty(stage.SourceName)) list.Add(new LocalizedEntry(GameText.StagesTable, stage.StageId, stage.SourceName, null, $"스테이지 이름 · {sceneName}"));
                        foreach (var enemy in root.GetComponentsInChildren<StageEnemy>(true))
                            if (!string.IsNullOrEmpty(enemy.StageId) && !string.IsNullOrEmpty(enemy.SourceName)) list.Add(new LocalizedEntry(GameText.StagesTable, enemy.StageId, enemy.SourceName, null, $"스테이지 이름(어드벤처씬 적) · {sceneName}"));
                        foreach (var npc in root.GetComponentsInChildren<NpcSpeaker>(true))
                        {
                            if (string.IsNullOrEmpty(npc.NpcId))
                            {
                                if ((npc.DefaultDialogue == null && npc.SourceLines.Length > 0) || (npc.ClearedDialogue == null && npc.SourceClearedLines.Length > 0)) Warnings.Add($"{sceneName}/{npc.name}: NPC ID가 없어 말풍선을 번역하지 않음");
                                continue;
                            }
                            if (!string.IsNullOrEmpty(npc.SourceName)) list.Add(new LocalizedEntry(NpcSpeaker.NpcTable, npc.NpcId + ".name", npc.SourceName, null, $"NPC 이름 · {sceneName}/{npc.name}"));
                            var lines = npc.SourceLines;
                            for (int i = 0; i < lines.Length; i++) if (!string.IsNullOrEmpty(lines[i])) list.Add(new LocalizedEntry(NpcSpeaker.NpcTable, $"{npc.NpcId}.{i}", lines[i], null, $"NPC 말풍선 {i}번 · {sceneName}/{npc.name}"));
                            var cleared = npc.SourceClearedLines;
                            for (int i = 0; i < cleared.Length; i++) if (!string.IsNullOrEmpty(cleared[i])) list.Add(new LocalizedEntry(NpcSpeaker.NpcTable, $"{npc.NpcId}.cleared.{i}", cleared[i], null, $"NPC 클리어 후 말풍선 {i}번 · {sceneName}/{npc.name}"));
                        }
                    }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        // 씬 파일에 스테이지 입구·NPC 스크립트가 있는지(열기 전에 거른다).
        private static bool SceneMentions(string path)
        {
            var text = System.IO.File.ReadAllText(path);
            foreach (var type in new[] { typeof(StageEntrance), typeof(StageEnemy), typeof(NpcSpeaker) })
            {
                var script = AssetDatabase.FindAssets(type.Name + " t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == type.Name);
                if (script != null && text.Contains(AssetDatabase.AssetPathToGUID(script))) return true;
            }
            return text.Contains("NPC.prefab") || text.Contains(PrefabGuid("NPC"));
        }
        private static string PrefabGuid(string name)
        {
            var path = AssetDatabase.FindAssets(name + " t:Prefab").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            return path != null ? AssetDatabase.AssetPathToGUID(path) : "\u0000";
        }
    }
}
