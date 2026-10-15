using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대화 진행 규칙(기획서 7.6). 대상은 자기 대본 순서에서 '열린' 대본 가운데 가장 뒤의 것을 보여 준다.
    /// 첫 대본은 처음부터 열려 있고, 조건이 있는 대본은 조건이 모두 맞으면(앞 대본을 봤는지와 관계없이), 조건이 없는 대본은 바로 앞 대본을 보면 열린다.
    /// 판정은 기록 조회 함수(seen·cleared)를 받아 화면·저장과 떨어져 있다. 게임 중에는 ProgressStore 기록을 쓴다.</summary>
    public static class DialogueProgress
    {
        /// <summary>게임이 쓰는 대화 그래프. 어드벤처씬 플레이어가 시작할 때 Overworld Settings에서 넣는다(테스트는 직접 넣음).
        /// 없거나 대상의 오브젝트 ID가 그래프에 없으면 그 대상은 대본이 없다(NPC는 반응하지 않고, 구역은 오류를 남기고, 적은 대화 없이 전투).</summary>
        public static DialogueGraph Graph { get; set; }

        /// <summary>조건이 맞는지. 봄 = 그 대본을 봤음, 클리어 = 그 전투 진입 대본을 순서에 둔 적(오브젝트 ID = Stage Id)을 클리어했음.</summary>
        public static bool Met(DialogueGraph graph, DialogueCondition condition, Func<ScenarioScript, bool> seen, Func<string, bool> cleared)
        {
            if (condition.script == null) return false;
            if (condition.type == DialogueConditionType.Seen) return seen(condition.script);
            var owner = graph.OwnerOf(condition.script);
            return owner != null && cleared(owner);
        }
        /// <summary>순서의 index번째 대본이 열렸는지.</summary>
        public static bool Unlocked(DialogueGraph graph, IReadOnlyList<ScenarioScript> chain, int index, Func<ScenarioScript, bool> seen, Func<string, bool> cleared)
        {
            if (index <= 0) return index == 0;
            var conditions = graph.ConditionsOf(chain[index]);
            if (conditions.Count == 0) return seen(chain[index - 1]);
            foreach (var c in conditions) if (!Met(graph, c, seen, cleared)) return false;
            return true;
        }
        /// <summary>대상이 지금 보여 줄 대본(열린 대본 중 가장 뒤). 순서가 비었으면 null.</summary>
        public static ScenarioScript Current(DialogueGraph graph, string objectId, Func<ScenarioScript, bool> seen, Func<string, bool> cleared)
        {
            if (graph == null) return null;
            var chain = graph.Chain(objectId);
            ScenarioScript current = null;
            for (int i = 0; i < chain.Count; i++) if (Unlocked(graph, chain, i, seen, cleared)) current = chain[i];
            return current;
        }
        // 게임 기록: 대화 대본은 끝까지 보면 대본 ID로, 전투 진입 대본은 입장 후 구간 기록(대본 ID)으로 본다.
        public static bool SeenInSave(ScenarioScript script) => script != null && ProgressStore.IsSeen(script.TalkSeenId);
        public static bool ClearedInSave(string stageId) => ProgressStore.IsCleared(stageId);
        /// <summary>게임 기록으로 본 지금 대본.</summary>
        public static ScenarioScript Current(DialogueGraph graph, string objectId) => Current(graph, objectId, SeenInSave, ClearedInSave);
        /// <summary>근접 대화 구역: 지금 대본이 그 구역에서 아직 나오지 않았을 때만 돌려준다(대본마다 처음 들어갈 때 한 번).</summary>
        public static ScenarioScript ZoneCurrent(DialogueGraph graph, string zoneId)
        {
            var script = Current(graph, zoneId);
            return script != null && !ProgressStore.IsZoneShown(zoneId, script.TalkSeenId) ? script : null;
        }

        // 버전 차례(저장하지 않음, 다시 실행하면 버전 1부터).
        private static readonly Dictionary<ScenarioScript, int> turns = new Dictionary<ScenarioScript, int>();
        /// <summary>이번에 보여 줄 버전(0 = 버전 1). 차례대로면 1, 2, …, 다시 1. 무작위면 random(개수)로 고른다(기본 UnityEngine.Random).</summary>
        public static int NextVersion(ScenarioScript script, Func<int, int> random = null)
        {
            if (script == null) return 0;
            int count = script.VersionCount;
            if (count <= 1) return 0;
            if (script.Options.versionOrder == ScenarioVersionOrder.Random) return Mathf.Clamp((random ?? (n => UnityEngine.Random.Range(0, n)))(count), 0, count - 1);
            turns.TryGetValue(script, out int turn);
            turns[script] = turn + 1;
            return turn % count;
        }
        public static void ResetVersions() => turns.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Graph = null; turns.Clear(); }
    }
}
