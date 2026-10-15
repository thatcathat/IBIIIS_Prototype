using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>대화 대상(씬에 놓인 NPC·근접 대화 구역·적)과 오브젝트 ID. ID는 이미 있는 대상 컴포넌트의 칸이다:
    /// NPC `NpcSpeaker.Npc Id`, 구역 `DialogueZone.Zone Id`, 적 `StageEnemy.Stage Id`(기획서 7.6, 개발 가이드의 대화 진행 재설계).</summary>
    public static class DialogueGraphTargets
    {
        public enum Kind { None, Npc, Zone, Enemy }
        public readonly struct Target
        {
            public readonly GameObject GameObject; public readonly Kind Kind; public readonly string Id;
            public Target(GameObject go, Kind kind, string id) { GameObject = go; Kind = kind; Id = id; }
            public string SceneName => GameObject != null ? GameObject.scene.name : "";
        }
        public static string KindName(Kind kind) => kind == Kind.Npc ? "NPC" : kind == Kind.Zone ? "근접 대화 구역" : kind == Kind.Enemy ? "적" : "대상";

        /// <summary>오브젝트의 대화 대상 종류와 ID(대상 컴포넌트가 없으면 None).</summary>
        public static Target Of(GameObject go)
        {
            if (go == null) return default;
            var npc = go.GetComponent<NpcSpeaker>(); if (npc != null) return new Target(go, Kind.Npc, npc.NpcId);
            var zone = go.GetComponent<DialogueZone>(); if (zone != null) return new Target(go, Kind.Zone, zone.ZoneId);
            var enemy = go.GetComponent<StageEnemy>(); if (enemy != null) return new Target(go, Kind.Enemy, enemy.StageId);
            return new Target(go, Kind.None, null);
        }
        /// <summary>열린 씬의 대화 대상(ID가 있는 것). 같은 ID가 둘 이상이면 duplicates에 넣는다.</summary>
        public static Dictionary<string, Target> InOpenScenes(List<string> duplicates = null)
        {
            var result = new Dictionary<string, Target>();
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!(behaviour is NpcSpeaker || behaviour is DialogueZone || behaviour is StageEnemy)) continue;
                if (EditorUtility.IsPersistent(behaviour)) continue; // 프리팹 에셋 제외
                var target = Of(behaviour.gameObject);
                if (string.IsNullOrEmpty(target.Id)) continue;
                if (result.ContainsKey(target.Id)) { duplicates?.Add(target.Id); continue; }
                result[target.Id] = target;
            }
            return result;
        }
        /// <summary>열린 씬에서 쓰지 않는 ID 제안(prefix + 번호). 스테이지 입구·적·구역·NPC의 ID와 그래프의 대상 ID를 모두 피한다.</summary>
        public static string UniqueId(string prefix, DialogueGraph graph = null)
        {
            var used = new HashSet<string>();
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b is IStageLink stage && !string.IsNullOrEmpty(stage.StageId)) used.Add(stage.StageId);
                if (b is DialogueZone zone && !string.IsNullOrEmpty(zone.ZoneId)) used.Add(zone.ZoneId);
                if (b is NpcSpeaker npc && !string.IsNullOrEmpty(npc.NpcId)) used.Add(npc.NpcId);
            }
            if (graph != null) foreach (var o in graph.Objects) if (o != null && !string.IsNullOrEmpty(o.objectId)) used.Add(o.objectId);
            for (int i = 1; ; i++) if (!used.Contains(prefix + i)) return prefix + i;
        }
        /// <summary>대상 컴포넌트가 없는 오브젝트를 NPC로 만든다(NpcSpeaker를 붙이고 겹치지 않는 Npc Id를 준다, Undo). 이미 대상이면 그대로 돌려준다.
        /// ID가 빈 대상이면 ID를 채운다(적의 Stage Id는 채우지 않음 — 클리어 기록 키라 사용자가 정함).</summary>
        public static Target EnsureTarget(GameObject go, DialogueGraph graph)
        {
            var target = Of(go);
            if (target.Kind == Kind.None)
            {
                var npc = Undo.AddComponent<NpcSpeaker>(go);
                var so = new SerializedObject(npc);
                so.FindProperty("npcId").stringValue = UniqueId("npc-", graph);
                so.FindProperty("displayName").stringValue = go.name;
                so.ApplyModifiedProperties();
                MarkDirty(go);
                return Of(go);
            }
            if (string.IsNullOrEmpty(target.Id) && target.Kind != Kind.Enemy)
            {
                Component component = target.Kind == Kind.Npc ? (Component)go.GetComponent<NpcSpeaker>() : go.GetComponent<DialogueZone>();
                var so = new SerializedObject(component);
                so.FindProperty(target.Kind == Kind.Npc ? "npcId" : "zoneId").stringValue = UniqueId(target.Kind == Kind.Npc ? "npc-" : "zone-", graph);
                so.ApplyModifiedProperties();
                MarkDirty(go);
                return Of(go);
            }
            return target;
        }
        private static void MarkDirty(GameObject go) { if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene); }
    }
}
