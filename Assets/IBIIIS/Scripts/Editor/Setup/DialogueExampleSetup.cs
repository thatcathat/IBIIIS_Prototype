using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>(시험) 대화 진행 예시(기획서 7.6, 사용자 그림 2026-10-15·HTML 시안)를 열린 어드벤처씬과 대화 그래프에 넣는다. 플레이 확인 목록 D-01~D-04용 임시 데이터.
    /// NPC_A(npc_a)·NPC_B(npc_b)·구역 DialogueZone_Gate(zone_gate)를 놓고, 적 stage-prototype-enemy-01의 순서 앞에 진입 불가 대화를 넣는다.
    /// 다시 실행해도 결과가 같다: 이미 있는 대본·씬 오브젝트·그래프 대상은 그대로 두고(사용자가 고친 내용 유지) 빠진 것만 만든다. 씬 저장은 사용자가 한다.</summary>
    public static class DialogueExampleSetup
    {
        public const string Folder = AssetPaths.Scenarios + "/DialogueExampleTest";
        public const string NpcA = "npc_a", NpcB = "npc_b", Gate = "zone_gate", Enemy = "stage-prototype-enemy-01";
        private const string EnemyBattleScript = AssetPaths.Scenarios + "/PrototypeMap/PrototypeMap.asset";

        [MenuItem("IBIIIS/Overworld/Add Dialogue Example (시험)")]
        public static void AddToOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[IBIIIS] Play 모드를 끝낸 뒤 실행하세요."); return; }
            if (Object.FindFirstObjectByType<OverworldPlayer>() == null) { Debug.LogError("[IBIIIS] 열린 씬에 어드벤처씬 플레이어(OverworldPlayer)가 없습니다. OverworldTest 같은 어드벤처씬을 열고 실행하세요."); return; }
            var graph = DialogueSetup.EnsureGraph();
            if (graph == null) { Debug.LogError("[IBIIIS] 대화 그래프를 찾지 못했습니다(IBIIIS > Create Default Dialogue)."); return; }
            EnsureScripts(out var s);
            EnsureGraph(graph, s);
            EnsureSceneObjects();
            Debug.Log($"[IBIIIS] (시험) 대화 예시를 넣었습니다: 대본 {Folder}, 그래프 대상 {NpcA}·{NpcB}·{Gate}·{Enemy}. 씬을 저장하세요.");
        }

        public sealed class Scripts { public ScenarioScript AFirst, ARepeat, AClear, ARepeat2, BFirst, BNext, EnemyBlocked, GateBlocked, Battle; }

        // 대본은 없을 때만 만든다(버전·순서도 만들 때만 넣음).
        public static void EnsureScripts(out Scripts s)
        {
            var player = AssetDatabase.LoadAssetAtPath<DialogueCharacter>(DialogueSetup.Player);
            var villager = AssetDatabase.LoadAssetAtPath<DialogueCharacter>(DialogueSetup.Villager);
            DialogueLine P(string text) => DialogueLine.Say(player, text);
            DialogueLine A(string text) => new DialogueLine { speaker = villager, nameOverride = "마을 사람 A", text = text };
            DialogueLine B(string text) => new DialogueLine { speaker = villager, nameOverride = "마을 사람 B", text = text };
            ScenarioScript Talk(string file, string id, params DialogueLine[] lines) => DialogueSetup.EnsureTalk($"{Folder}/{file}.asset", id, lines);
            s = new Scripts
            {
                AFirst = Talk("NpcAFirst", "example_a_first", DialogueLine.Narrate("마을 사람 A가 손을 흔든다."), A("처음 보는 얼굴이네!"), A("위쪽에 이상한 녀석이 길을 막고 있어. 좀 처리해 줄래?"), P("해 볼게요.")),
                AClear = Talk("NpcAClear", "example_a_clear", A("정말 해치웠구나! 고마워."), A("B에게도 알려 줘. 아래쪽에 있어.")),
                BFirst = Talk("NpcBFirst", "example_b_first", B("지금은 바빠. 나중에 와.")),
                BNext = Talk("NpcBNext", "example_b_next", B("A가 보냈구나. 길이 열렸다니 다행이야."), P("이제 어디로 가면 돼요?"), B("던전 입구는 아래쪽이야.")),
                EnemyBlocked = Talk("EnemyBlocked", "example_enemy_blocked", DialogueLine.Narrate("길이 막혀 있다."), P("(마을 사람에게 먼저 이야기를 들어 보자.)")),
                GateBlocked = Talk("GateBlocked", "example_gate_blocked", DialogueLine.Narrate("앞에 몬스터가 길을 막고 있다."), P("(NPC에게 먼저 말을 걸어 보자.)")),
                Battle = AssetDatabase.LoadAssetAtPath<ScenarioScript>(EnemyBattleScript),
            };
            s.ARepeat = Versions("NpcARepeat", "example_a_repeat", A, "적은 위쪽에 있어.", "준비되면 가 봐. 조심하고.");
            s.ARepeat2 = Versions("NpcARepeat2", "example_a_repeat_2", A, "또 왔구나.", "B에게는 가 봤어?", "오늘 날씨 좋다.");
        }
        // 버전마다 대사 한 줄인 반복 대본(차례대로). 처음 만들 때만 버전을 넣는다.
        private static ScenarioScript Versions(string file, string id, System.Func<string, DialogueLine> say, params string[] lines)
        {
            string path = $"{Folder}/{file}.asset";
            bool created = AssetDatabase.LoadMainAssetAtPath(path) == null;
            var script = DialogueSetup.EnsureTalk(path, id, say(lines[0]));
            if (script == null || !created) return script;
            script.SetMoreVersions(lines.Skip(1).Select(t => new[] { new ScenarioStep { type = ScenarioStepType.Line, line = say(t) } }).ToArray());
            EditorUtility.SetDirty(script); AssetDatabase.SaveAssetIfDirty(script);
            return script;
        }

        // 그래프: 대상이 없을 때만 순서를 만든다. 적은 순서가 아직 전투 진입 대본 하나(옮긴 데이터 그대로)일 때만 앞에 진입 불가 대화를 넣는다.
        public static void EnsureGraph(DialogueGraph graph, Scripts s)
        {
            Undo.RecordObject(graph, "(시험) 대화 예시 연결");
            bool changed = false;
            if (!graph.HasObject(NpcA) && s.Battle != null)
                changed |= Chain(graph, NpcA, (s.AFirst, null), (s.ARepeat, null), (s.AClear, DialogueCondition.Clear(s.Battle)), (s.ARepeat2, null));
            if (!graph.HasObject(NpcB) && s.AClear != null)
                changed |= Chain(graph, NpcB, (s.BFirst, null), (s.BNext, DialogueCondition.Seen(s.AClear)));
            if (!graph.HasObject(Gate)) changed |= Chain(graph, Gate, (s.GateBlocked, null));
            var enemy = graph.ObjectOf(Enemy);
            if (enemy != null && s.Battle != null && s.EnemyBlocked != null && s.AFirst != null && enemy.first == s.Battle && graph.OwnerOf(s.EnemyBlocked) == null
                && DialogueGraphEdits.IncomingOf(graph, s.EnemyBlocked) == (null, null) && graph.NextOf(s.EnemyBlocked) == null)
            {
                graph.SetFirst(Enemy, s.EnemyBlocked);
                graph.SetNext(s.EnemyBlocked, s.Battle);
                if (!graph.ConditionsOf(s.Battle).Any()) graph.SetConditions(s.Battle, new[] { DialogueCondition.Seen(s.AFirst) });
                changed = true;
            }
            else if (enemy == null) Debug.LogWarning($"[IBIIIS] 대화 그래프에 적 '{Enemy}'이(가) 없어 진입 불가 대화를 잇지 않았습니다.");
            if (changed) { EditorUtility.SetDirty(graph); AssetDatabase.SaveAssetIfDirty(graph); }
        }
        // 대상의 순서를 잇는다. 그래프 규칙(대본 하나는 한 대상에만, 고리 금지)을 검사하고, 이미 있는 다음·조건은 덮어쓰지 않는다(사용자가 고친 연결 유지).
        private static bool Chain(DialogueGraph graph, string objectId, params (ScenarioScript script, DialogueCondition? condition)[] order)
        {
            if (order.Any(o => o.script == null)) return false;
            string why = DialogueGraphEdits.FirstProblem(graph, objectId, order[0].script);
            if (why != null) { Debug.LogWarning($"[IBIIIS] (시험) 대화 예시: {why} '{objectId}'을(를) 잇지 않았습니다."); return false; }
            graph.SetFirst(objectId, order[0].script);
            for (int i = 0; i < order.Length; i++)
            {
                var s = order[i].script;
                if (order[i].condition is DialogueCondition c && graph.ConditionsOf(s).Count == 0) graph.SetConditions(s, new[] { c });
                if (i + 1 >= order.Length) { graph.Entry(s); break; }
                if (graph.NextOf(s) != null) break; // 사용자가 이미 다른 다음을 이었으면 그 뒤는 두지 않음
                if (DialogueGraphEdits.NextProblem(graph, s, order[i + 1].script) != null) break;
                graph.SetNext(s, order[i + 1].script);
            }
            return true;
        }

        // 씬: 같은 ID의 오브젝트가 없을 때만 만든다(Ctrl+Z로 취소 가능).
        public static void EnsureSceneObjects()
        {
            var npcs = Object.FindObjectsByType<NpcSpeaker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool created = false;
            void Npc(string id, string name, string displayName, Vector3 position)
            {
                if (npcs.Any(n => n.NpcId == id)) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(OverworldSetup.EnsureNpcPrefab(), EditorSceneManager.GetActiveScene());
                Undo.RegisterCreatedObjectUndo(go, "(시험) 대화 예시 NPC");
                go.name = name; go.transform.position = position;
                var so = new SerializedObject(go.GetComponent<NpcSpeaker>());
                so.FindProperty("npcId").stringValue = id; so.FindProperty("displayName").stringValue = displayName;
                so.ApplyModifiedPropertiesWithoutUndo();
                created = true;
            }
            Npc(NpcA, "NPC_A", "마을 사람 A", new Vector3(-6, 0, 3));
            Npc(NpcB, "NPC_B", "마을 사람 B", new Vector3(6, 0, -6));
            if (!Object.FindObjectsByType<DialogueZone>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(z => z.ZoneId == Gate))
            {
                // 플레이어 시작 자리와 적 사이 길목(앞뒤 2칸, 좌우 4칸).
                var go = new GameObject("DialogueZone_Gate");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, EditorSceneManager.GetActiveScene());
                go.transform.position = new Vector3(0, 0, 3);
                var so = new SerializedObject(go.AddComponent<DialogueZone>());
                so.FindProperty("zoneId").stringValue = Gate; so.FindProperty("size").vector2Value = new Vector2(4, 2);
                so.ApplyModifiedPropertiesWithoutUndo();
                Undo.RegisterCreatedObjectUndo(go, "(시험) 대화 예시 구역");
                created = true;
            }
            if (created) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
