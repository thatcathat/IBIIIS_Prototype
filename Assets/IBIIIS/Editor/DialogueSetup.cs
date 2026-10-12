using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>미연시 대화의 기본 에셋을 만든다: 임시 초상화(코드로 그린 실루엣), 기본 인물(플레이어·어시 파리·마을 사람), 확인용 대화,
    /// 그리고 미니맵 NPC 프리팹의 Dialogue 슬롯이 비어 있으면 확인용 대화를 연결한다. 이미 있는 파일·연결은 덮어쓰지 않는다.</summary>
    public static class DialogueSetup
    {
        public const string Folder = AssetPaths.Dialogue, PortraitFolder = Folder + "/Portraits", CharacterFolder = Folder + "/Characters", ScriptFolder = Folder + "/Scripts";
        public const string Player = CharacterFolder + "/Player.asset", AssiFly = CharacterFolder + "/AssiFly.asset", Villager = CharacterFolder + "/Villager.asset";
        public const string SampleScript = ScriptFolder + "/VillagerGreeting.asset";

        [MenuItem("IBIIIS/Create Default Dialogue")]
        public static DialogueScript EnsureDefault()
        {
            AssetPaths.EnsureFolder(PortraitFolder); AssetPaths.EnsureFolder(CharacterFolder); AssetPaths.EnsureFolder(ScriptFolder);
            var player = EnsureCharacter(Player, "player", "플레이어", DialogueSide.Left, new Color(.55f, .78f, 1f), Silhouette("Player", new Color(.95f, .78f, .35f)));
            var fly = EnsureCharacter(AssiFly, "assi_fly", "어시 파리", DialogueSide.Right, new Color(.55f, .9f, .45f), Silhouette("AssiFly", new Color(.45f, .8f, .4f)));
            var villager = EnsureCharacter(Villager, "villager", "마을 사람", DialogueSide.Right, new Color(1f, .85f, .55f), Silhouette("Villager", new Color(.66f, .76f, .9f)));
            var script = AssetDatabase.LoadAssetAtPath<DialogueScript>(SampleScript);
            if (script == null)
            {
                script = ScriptableObject.CreateInstance<DialogueScript>();
                script.Set("villager_greeting", new[]
                {
                    DialogueLine.Narrate("마을 사람이 손을 흔든다."),
                    DialogueLine.Say(villager, "어서 와! 처음 보는 얼굴이네."),
                    DialogueLine.Say(player, "안녕하세요. 여기는 어디예요?"),
                    DialogueLine.Say(villager, "던전 입구 마을이야. 저기 주황 발판으로 가면 던전에 들어갈 수 있어."),
                    DialogueLine.Say(fly, "(대화창 확인용 임시 대사예요!)"),
                });
                AssetDatabase.CreateAsset(script, SampleScript); AssetDatabase.SaveAssetIfDirty(script);
            }
            ConnectToNpcPrefab(script);
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
        private static void ConnectToNpcPrefab(DialogueScript script)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPaths.NpcPrefab);
            var speaker = prefab != null ? prefab.GetComponent<NpcSpeaker>() : null;
            if (speaker == null || speaker.Dialogue != null) return;
            var so = new SerializedObject(speaker); so.FindProperty("dialogue").objectReferenceValue = script; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab); AssetDatabase.SaveAssetIfDirty(prefab);
        }
    }
}
