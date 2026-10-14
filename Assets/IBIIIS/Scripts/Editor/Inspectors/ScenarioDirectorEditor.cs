using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>대본 진행기 Inspector. 씬 연결(전투·대본·등장 오브젝트)은 여기서 편집하고, 대본 설정(시작 시점·한 번만·건너뛰기·연출)은
    /// 대본 에셋에 있으므로 읽기 전용으로 보여 주고 IBIIIS Dialogue Editor의 시작 노드로 보낸다.</summary>
    [CustomEditor(typeof(ScenarioDirector))]
    public sealed class ScenarioDirectorEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            var summary = new VisualElement { style = { marginTop = 6 } };
            root.Add(summary);
            // 대본 설정은 다른 에셋에 있으므로 연결된 대본도 따로 지켜본다(Dialogue Editor에서 바꿔도 바로 반영).
            var watch = new VisualElement();
            root.Add(watch);
            ScenarioScript watched = null; string shown = null;
            void Refresh()
            {
                var director = (ScenarioDirector)target;
                if (director == null) return;
                var script = director.Script;
                if (script != watched)
                {
                    watched = script;
                    watch.Unbind(); watch.RemoveFromHierarchy(); watch = new VisualElement(); root.Add(watch);
                    if (script != null) watch.TrackSerializedObjectValue(new SerializedObject(script), _ => Refresh());
                }
                string text = script == null ? null : Summary(director, script);
                if (text == shown && summary.childCount > 0) return;
                shown = text; summary.Clear();
                // 대본이 없는 진행기는 정상 상태(새 맵에 기본으로 붙음): 시작하지 않고 전투는 평소대로 진행된다.
                if (script == null) { summary.Add(new HelpBox("대본 없음 — 이 씬에서는 대본이 실행되지 않고 전투가 평소대로 진행됩니다. 대화를 넣으려면 Script에 대본을 연결하거나 IBIIIS Dialogue Editor의 '씬에 연결'을 쓰세요.", HelpBoxMessageType.Info)); return; }
                summary.Add(new HelpBox(text, HelpBoxMessageType.None));
                summary.Add(new Button(() => DialogueEditorWindow.Open(script).SelectStart()) { text = "IBIIIS Dialogue Editor에서 대본 설정 열기" });
            }
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            Refresh();
            return root;
        }
        private static string Summary(ScenarioDirector director, ScenarioScript script)
        {
            var o = script.Options;
            string Part(ScenarioSection section) { script.Range(section, out int from, out int to); return script.Has(section) ? $"{to - from}단계" : "비어 있음"; }
            string parts = $"입장 후(이 씬에서 실행) {Part(ScenarioSection.Battle)} · 입장 전 {Part(ScenarioSection.Before)} · 클리어 후 {Part(ScenarioSection.Clear)}(어드벤처씬에서 실행)";
            return $"대본 설정 ({script.name}) — Dialogue Editor의 시작 노드에서 편집\n" +
                $"구간: {parts}{(director.PlayOnStart ? "" : " (자동 시작 꺼짐: 코드에서 Begin)")}\n" +
                $"한 번만: {Mark(o.onlyOnce)} · 건너뛰기: {(o.allowSkip ? $"켬({o.SkipHold:0.##}초)" : "끔")}\n" +
                $"적 말풍선 숨김: {Mark(o.hideEnemyAlerts)} · 위험 그림을 대본이 정함: {Mark(o.scriptControlsDanger)}";
        }
        private static string Mark(bool value) => value ? "켬" : "끔";
    }
}
