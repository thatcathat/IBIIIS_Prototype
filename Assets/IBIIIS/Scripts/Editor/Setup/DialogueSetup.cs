using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>미연시 대화의 기본 에셋을 만든다: 임시 초상화(코드로 그린 실루엣), 기본 인물(플레이어·어시 파리·마을 사람), 어드벤처씬 대화 그래프.
    /// 이미 있는 파일은 덮어쓰지 않는다. 대화 대본은 만들지 않는다(2026-10-16 확인용 대화 VillagerGreeting은 사용자가 정리 — 시험 예시는 Add Dialogue Example).
    /// 대본을 NPC에 잇는 것은 대화 그래프에서 한다(NPC 프리팹에는 대본 칸이 없다).</summary>
    public static class DialogueSetup
    {
        // 초상화(그림)는 Art/Portraits, 대화 인물은 ScriptableObjects/Dialogue/Characters, NPC 대화는 ScriptableObjects/Scenarios/NPC.
        public const string PortraitFolder = AssetPaths.Portraits, CharacterFolder = AssetPaths.Data + "/Dialogue/Characters", ScriptFolder = AssetPaths.Scenarios + "/NPC";
        public const string Player = CharacterFolder + "/Player.asset", AssiFly = CharacterFolder + "/AssiFly.asset", Villager = CharacterFolder + "/Villager.asset";

        /// <summary>어드벤처씬 대화 그래프(대상 → 대본 순서·조건, 기획서 7.6). 없을 때만 빈 그래프를 만든다(이미 있으면 그대로).</summary>
        public static DialogueGraph EnsureGraph()
        {
            var existing = AssetDatabase.LoadAssetAtPath<DialogueGraph>(AssetPaths.DialogueGraph);
            if (existing != null) return existing;
            AssetPaths.EnsureFolder(AssetPaths.Scenarios);
            var graph = ScriptableObject.CreateInstance<DialogueGraph>();
            AssetDatabase.CreateAsset(graph, AssetPaths.DialogueGraph); AssetDatabase.SaveAssetIfDirty(graph);
            return graph;
        }
        [MenuItem("IBIIIS/Create Default Dialogue")]
        public static void EnsureDefault()
        {
            AssetPaths.EnsureFolder(PortraitFolder); AssetPaths.EnsureFolder(CharacterFolder); AssetPaths.EnsureFolder(ScriptFolder);
            EnsureCharacter(Player, "player", "플레이어", DialogueSide.Left, new Color(.55f, .78f, 1f), Silhouette("Player", new Color(.95f, .78f, .35f)));
            EnsureCharacter(AssiFly, "assi_fly", "어시 파리", DialogueSide.Right, new Color(.55f, .9f, .45f), Silhouette("AssiFly", new Color(.45f, .8f, .4f)));
            EnsureCharacter(Villager, "villager", "마을 사람", DialogueSide.Right, new Color(1f, .85f, .55f), Silhouette("Villager", new Color(.66f, .76f, .9f)));
            EnsureGraph();
        }
        /// <summary>대화 대본(용도: 대화)을 없을 때만 만든다(줄마다 대사 단계 하나). 이미 있으면 그대로 돌려준다. 그 경로에 다른 종류 파일이 있으면 만들지 않고 null.</summary>
        public static ScenarioScript EnsureTalk(string path, string id, params DialogueLine[] lines)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScenarioScript>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { Debug.LogError($"[IBIIIS] {path}에 대화 대본이 아닌 파일이 있어 대본을 만들지 않았습니다."); return null; }
            AssetPaths.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            var script = ScriptableObject.CreateInstance<ScenarioScript>();
            var options = ScenarioScript.Settings.Default; options.use = ScenarioUse.Talk; script.SetOptions(options);
            script.Set(id, System.Array.ConvertAll(lines, l => new ScenarioStep { type = ScenarioStepType.Line, line = l }));
            AssetDatabase.CreateAsset(script, path); AssetDatabase.SaveAssetIfDirty(script);
            return script;
        }
        private static DialogueCharacter EnsureCharacter(string path, string id, string name, DialogueSide side, Color nameColor, Sprite portrait)
        {
            var existing = AssetDatabase.LoadAssetAtPath<DialogueCharacter>(path);
            if (existing != null) return existing;
            var character = ScriptableObject.CreateInstance<DialogueCharacter>();
            character.Set(id, name, side, portrait, nameColor);
            AssetDatabase.CreateAsset(character, path); AssetDatabase.SaveAssetIfDirty(character);
            return character;
        }
        // 임시 상반신 실루엣: 둥근 어깨·몸통과 머리(256×256, 기준점 아래 가운데).
        private static Sprite Silhouette(string name, Color fill) => GeneratedSprites.EnsureGeneratedSprite($"{PortraitFolder}/{name}_Placeholder.png", (u, v) =>
            GeneratedSprites.Shade(Mathf.Min(GeneratedSprites.RoundedBox(u, v - .2f, .36f, .22f, .16f), new Vector2(u, v - .64f).magnitude - .2f), fill));
    }
}
