using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>대본 진행기 Inspector. 씬 연결(전투·대본·등장 오브젝트)은 여기서 편집하고, 대본 설정(시작 시점·한 번만·건너뛰기·연출)은
    /// 대본 에셋에 있으므로 읽기 전용으로 보여 주고 IBIIIS Dialogue Editor의 시작 노드로 보낸다.</summary>
    [CustomEditor(typeof(ScenarioDirector))]
    public sealed class ScenarioDirectorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var director = (ScenarioDirector)target;
            var script = director.Script;
            EditorGUILayout.Space(6);
            if (script == null) { EditorGUILayout.HelpBox("대본(Script)을 연결하세요. 대본 설정은 대본 에셋에 있습니다.", MessageType.Warning); return; }
            var o = script.Options;
            string timing = o.startTiming == ScenarioStartTiming.BattleWon ? "클리어 시(Battle Won)" : "전투 준비 시(Battle Ready)";
            EditorGUILayout.HelpBox($"대본 설정 ({script.name}) — Dialogue Editor의 시작 노드에서 편집\n" +
                $"시작: {(director.PlayOnStart ? timing : "자동 시작 꺼짐(코드에서 Begin)")}\n" +
                $"한 번만: {Mark(o.onlyOnce)} · 건너뛰기: {(o.allowSkip ? $"켬({o.SkipHold:0.##}초)" : "끔")}\n" +
                $"적 말풍선 숨김: {Mark(o.hideEnemyAlerts)} · 위험 그림을 대본이 정함: {Mark(o.scriptControlsDanger)}", MessageType.None);
            if (GUILayout.Button("IBIIIS Dialogue Editor에서 대본 설정 열기")) DialogueEditorWindow.Open(script).SelectStart();
        }
        private static string Mark(bool value) => value ? "켬" : "끔";
    }
}
