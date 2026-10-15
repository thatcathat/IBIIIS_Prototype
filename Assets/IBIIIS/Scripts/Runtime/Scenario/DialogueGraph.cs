using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대화 조건 종류(기획서 7.6). 값은 에셋에 정수로 저장되므로 끝에만 추가한다.</summary>
    public enum DialogueConditionType
    {
        /// <summary>그 대화 대본을 끝까지 봤다.</summary>
        Seen = 0,
        /// <summary>그 전투 진입 대본을 가진 적(대본 순서의 주인)의 스테이지를 클리어했다.</summary>
        Clear = 1,
    }
    [Serializable]
    public struct DialogueCondition
    {
        public DialogueConditionType type;
        [Tooltip("봄: 대화 대본. 클리어: 전투 진입 대본(그 대본을 순서에 둔 적의 Stage Id로 클리어 기록을 확인).")] public ScenarioScript script;
        public static DialogueCondition Seen(ScenarioScript script) => new DialogueCondition { type = DialogueConditionType.Seen, script = script };
        public static DialogueCondition Clear(ScenarioScript script) => new DialogueCondition { type = DialogueConditionType.Clear, script = script };
    }
    /// <summary>대화 대상(NPC·근접 대화 구역·적) 하나: 오브젝트 ID와 대본 순서의 첫 대본.</summary>
    [Serializable]
    public sealed class DialogueGraphObject
    {
        [Tooltip("오브젝트 ID: NPC는 Npc Id, 근접 대화 구역은 Zone Id, 적은 Stage Id. 정한 뒤 바꾸지 않습니다.")] public string objectId;
        [Tooltip("이 대상의 대본 순서 첫 대본.")] public ScenarioScript first;
        [Tooltip("그래프 화면의 노드 위치(편집기 전용).")] public Vector2 position;
    }
    /// <summary>대본 하나의 연결: 순서의 다음 대본과 조건.</summary>
    [Serializable]
    public sealed class DialogueGraphScript
    {
        public ScenarioScript script;
        [Tooltip("같은 대상 순서의 다음 대본(하나만). 비우면 순서 끝.")] public ScenarioScript next;
        [Tooltip("이 대본이 열리는 조건(모두 맞아야 함). 비우면 바로 앞 대본을 보면 열린다.")] public DialogueCondition[] conditions = Array.Empty<DialogueCondition>();
        [Tooltip("그래프 화면의 노드 위치(편집기 전용).")] public Vector2 position;
        public IReadOnlyList<DialogueCondition> Conditions => conditions ?? Array.Empty<DialogueCondition>();
    }
    /// <summary>어드벤처씬 대화 진행의 연결 데이터(기획서 7.6): 대상(오브젝트 ID) → 첫 대본, 대본마다 다음 대본과 조건, 그래프 노드 위치.
    /// 대본 사이 관계는 여기에만 저장한다(대본 에셋에는 대사만). 고르는 규칙은 DialogueProgress가 맡는다. Dialogue Editor의 그래프가 편집한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Dialogue Graph")]
    public sealed class DialogueGraph : ScriptableObject
    {
        [SerializeField] private List<DialogueGraphObject> objects = new List<DialogueGraphObject>();
        [SerializeField] private List<DialogueGraphScript> scripts = new List<DialogueGraphScript>();
        public IReadOnlyList<DialogueGraphObject> Objects => objects;
        public IReadOnlyList<DialogueGraphScript> Scripts => scripts;

        public DialogueGraphObject ObjectOf(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return null;
            foreach (var o in objects) if (o != null && o.objectId == objectId) return o;
            return null;
        }
        /// <summary>이 오브젝트 ID가 그래프에 있으면(첫 대본이 비어 있어도) true. 있으면 그 대상은 그래프 규칙을 따른다.</summary>
        public bool HasObject(string objectId) => ObjectOf(objectId) != null;
        public DialogueGraphScript EntryOf(ScenarioScript script)
        {
            if (script == null) return null;
            foreach (var s in scripts) if (s != null && s.script == script) return s;
            return null;
        }
        public ScenarioScript NextOf(ScenarioScript script) => EntryOf(script)?.next;
        public IReadOnlyList<DialogueCondition> ConditionsOf(ScenarioScript script) => EntryOf(script)?.Conditions ?? Array.Empty<DialogueCondition>();

        /// <summary>대상의 대본 순서(첫 대본부터 다음을 따라감). 고리로 돌아오면 그 앞에서 멈춘다.</summary>
        public List<ScenarioScript> Chain(string objectId)
        {
            var list = new List<ScenarioScript>();
            for (var s = ObjectOf(objectId)?.first; s != null && !list.Contains(s); s = NextOf(s)) list.Add(s);
            return list;
        }
        /// <summary>대본을 순서에 둔 대상의 오브젝트 ID(없으면 null, 여러 대상이면 처음 것 — 검사가 알림).</summary>
        public string OwnerOf(ScenarioScript script)
        {
            if (script == null) return null;
            foreach (var o in objects) if (o != null && Chain(o.objectId).Contains(script)) return o.objectId;
            return null;
        }

        // ── 코드·테스트용 편집(편집기는 직렬화 속성으로 고치고 Undo를 남긴다) ──
        public DialogueGraphObject SetFirst(string objectId, ScenarioScript first)
        {
            var o = ObjectOf(objectId);
            if (o == null) objects.Add(o = new DialogueGraphObject { objectId = objectId });
            o.first = first;
            return o;
        }
        public DialogueGraphScript Link(ScenarioScript script, ScenarioScript next = null, params DialogueCondition[] conditions)
        {
            var s = Entry(script);
            s.next = next; s.conditions = conditions ?? Array.Empty<DialogueCondition>();
            return s;
        }
        /// <summary>대본의 연결 항목(없으면 만든다 — 다음·조건 없음).</summary>
        public DialogueGraphScript Entry(ScenarioScript script)
        {
            var s = EntryOf(script);
            if (s == null) scripts.Add(s = new DialogueGraphScript { script = script });
            return s;
        }
        public void SetNext(ScenarioScript script, ScenarioScript next) => Entry(script).next = next;
        public void SetConditions(ScenarioScript script, DialogueCondition[] conditions) => Entry(script).conditions = conditions ?? Array.Empty<DialogueCondition>();
        /// <summary>대상을 그래프에서 뺀다(대본과 대본 사이 연결은 그대로).</summary>
        public bool RemoveObject(string objectId) => objects.RemoveAll(o => o != null && o.objectId == objectId) > 0;
        /// <summary>지운 대본(빈 참조)의 연결 항목과 그 대본을 가리키던 다음·조건·첫 대본을 정리한다. 바뀌었으면 true.
        /// 편집기가 저절로 하지 않는다(대본이 잠깐 없을 때 — 브랜치 바꾸기·다시 가져오기 — 연결을 잃지 않게). 그래프 화면의 빈 곳 우클릭으로 한다.</summary>
        public bool RemoveMissing()
        {
            if (!HasMissing) return false;
            scripts.RemoveAll(s => s == null || s.script == null);
            foreach (var s in scripts)
            {
                if (Missing(s.next)) s.next = null;
                if (s.Conditions.Any(c => c.script == null)) s.conditions = s.Conditions.Where(c => c.script != null).ToArray();
            }
            foreach (var o in objects) if (o != null && Missing(o.first)) o.first = null;
            return true;
        }
        /// <summary>지운 대본을 가리키는 연결이 있으면 true(빈 조건 칸 포함).</summary>
        public bool HasMissing => scripts.Any(s => s == null || s.script == null || Missing(s.next) || s.Conditions.Any(c => c.script == null))
            || objects.Any(o => o != null && Missing(o.first));
        // 유니티의 '없어진 오브젝트'는 == null이지만 참조 값은 남아 있다(비워 둔 칸과 구별).
        private static bool Missing(ScenarioScript s) => s == null && !ReferenceEquals(s, null);

        /// <summary>설정 문제 목록(편집기 검사·테스트용). 비어 있으면 정상.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>(); var owners = new Dictionary<ScenarioScript, string>();
            foreach (var o in objects)
            {
                if (o == null) continue;
                if (string.IsNullOrWhiteSpace(o.objectId)) { problems.Add("오브젝트 ID가 빈 대상이 있습니다."); continue; }
                if (Missing(o.first)) problems.Add($"'{o.objectId}'의 첫 대본이 지워졌습니다(그래프 빈 곳 우클릭 '지운 대본 연결 정리').");
                if (!ids.Add(o.objectId)) problems.Add($"오브젝트 ID '{o.objectId}'가 두 번 있습니다.");
                // 고리 검사: 다음을 따라가다 이미 지난 대본으로 돌아오면 고리
                var path = new List<ScenarioScript>();
                for (var s = o.first; s != null; s = NextOf(s))
                {
                    if (path.Contains(s)) { problems.Add($"'{o.objectId}'의 대본 순서가 '{s.name}'에서 고리로 돌아옵니다."); break; }
                    if (path.Count > 0 && !path[path.Count - 1].IsTalk && ConditionsOf(s).Count == 0)
                        problems.Add($"'{o.objectId}'의 '{s.name}'은(는) 전투 진입 대본 뒤에 조건 없이 이어져 열리지 않을 수 있습니다('클리어' 조건을 거세요).");
                    path.Add(s);
                    if (owners.TryGetValue(s, out var other) && other != o.objectId) problems.Add($"대본 '{s.name}'이(가) '{other}'와 '{o.objectId}' 두 대상의 순서에 들어 있습니다(대본 하나는 한 대상에만).");
                    else owners[s] = o.objectId;
                }
            }
            foreach (var e in scripts)
            {
                if (e == null) continue;
                if (e.script == null) { problems.Add("대본이 비어 있는 연결이 있습니다(지운 대본 — 그래프 빈 곳 우클릭 '지운 대본 연결 정리')."); continue; }
                if (Missing(e.next)) problems.Add($"'{e.script.name}'의 다음 대본이 지워져 순서가 여기서 끊깁니다.");
                foreach (var c in e.Conditions)
                {
                    if (c.script == null) { problems.Add($"'{e.script.name}'의 조건 대상 대본이 비어 있습니다."); continue; }
                    if (c.type == DialogueConditionType.Seen && !c.script.IsTalk) problems.Add($"'{e.script.name}'의 '봄' 조건 대상 '{c.script.name}'은(는) 대화 대본이 아닙니다.");
                    if (c.type == DialogueConditionType.Clear)
                    {
                        if (c.script.IsTalk) problems.Add($"'{e.script.name}'의 '클리어' 조건 대상 '{c.script.name}'은(는) 전투 진입 대본이 아닙니다.");
                        else if (OwnerOf(c.script) == null) problems.Add($"'{e.script.name}'의 '클리어' 조건 대상 '{c.script.name}'을(를) 순서에 둔 적이 없습니다.");
                    }
                }
            }
            // 본 기록·구역 기록은 대본 ID로 남으므로, 순서에 든 대본의 ID가 비었거나 겹치면 진행이 멈추거나 섞인다(Project 창 복제는 ID를 그대로 가져감).
            var byId = new Dictionary<string, ScenarioScript>();
            foreach (var s in owners.Keys)
            {
                if (string.IsNullOrWhiteSpace(s.Id)) { problems.Add($"대본 '{s.name}'의 대본 ID가 비어 있어 본 기록을 남기지 못합니다."); continue; }
                if (byId.TryGetValue(s.Id, out var same)) problems.Add($"대본 '{same.name}'과(와) '{s.name}'의 대본 ID '{s.Id}'가 같아 본 기록이 섞입니다.");
                else byId[s.Id] = s;
            }
            return problems;
        }
    }
}
