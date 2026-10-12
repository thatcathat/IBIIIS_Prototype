using System;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>전투 인식 말풍선의 기본 에셋을 만든다: 공용 대사(Battle Dialogue, 임시 문구), 효과음, 설정 에셋. 공용 플레이어 설정에 연결한다.
    /// 이미 있는 파일·연결·대사는 덮어쓰지 않는다. 말풍선 모양은 Game UI(`IBIIIS > Create Default Game UI`)의 Speech Bubble과 GameUi.uss가 정한다.</summary>
    public static class EnemyAlertSetup
    {
        public const string AlertSound = AssetPaths.Audio + "/EnemyAlert.wav";

        [MenuItem("IBIIIS/Create Default Enemy Alert")]
        public static EnemyAlertSettings EnsureDefault()
        {
            AssetPaths.EnsureFolder(AssetPaths.Audio); AssetPaths.EnsureFolder(AssetPaths.Settings);
            // 짧게 올라가는 두 음 "띵-딩!"
            var sound = MotionFeedbackSetup.EnsureSound(AlertSound, .2f, (t, noise) =>
                (float)Math.Sin(2 * Math.PI * (t < .06f ? 880 : 1320) * t) * Mathf.Exp(-(t < .06f ? t : t - .06f) * 18));
            var dialogue = EnsureDialogue();
            var settings = AssetDatabase.LoadAssetAtPath<EnemyAlertSettings>(AssetPaths.EnemyAlert);
            if (settings == null) { settings = ScriptableObject.CreateInstance<EnemyAlertSettings>(); AssetDatabase.CreateAsset(settings, AssetPaths.EnemyAlert); }
            var so = new SerializedObject(settings); bool filled = false;
            void Fill(string name, UnityEngine.Object value) { var slot = so.FindProperty(name); if (slot.objectReferenceValue == null && value != null) { slot.objectReferenceValue = value; filled = true; } }
            Fill("dialogue", dialogue); Fill("alertSound", sound);
            if (filled) so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings); // 예전 그림(!·?) 필드 흔적도 함께 정리된다
            var player = AssetDatabase.LoadAssetAtPath<PlayerSettings>(AssetPaths.PlayerSettings);
            if (player != null)
            {
                var ps = new SerializedObject(player); var slot = ps.FindProperty("enemyAlert");
                if (slot.objectReferenceValue == null) { slot.objectReferenceValue = settings; ps.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player); }
            }
            Selection.activeObject = settings;
            return settings;
        }
        /// <summary>공용 대사 에셋. 없을 때만 임시 문구로 만든다(있으면 사용자가 고친 대사를 그대로 둔다).</summary>
        public static BattleDialogue EnsureDialogue()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BattleDialogue>(AssetPaths.BattleDialogue);
            if (existing != null) return existing;
            var dialogue = ScriptableObject.CreateInstance<BattleDialogue>();
            dialogue.Set(new[]
            {
                new DialogueExchange { player = "들켰다!", enemy = "거기 서!" },
                new DialogueExchange { player = "앗…!", enemy = "찾았다!" },
                new DialogueExchange { player = "이런!", enemy = "꼼짝 마!" },
            }, new[] { "어디 갔지?", "기분 탓인가…", "놓쳤다…" }, true);
            AssetDatabase.CreateAsset(dialogue, AssetPaths.BattleDialogue); AssetDatabase.SaveAssetIfDirty(dialogue);
            return dialogue;
        }
    }
}
