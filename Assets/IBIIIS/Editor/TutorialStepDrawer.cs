using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>대본 단계 한 칸을 종류에 맞는 항목만 보여 준다(쓰지 않는 칸은 숨김).</summary>
    [CustomPropertyDrawer(typeof(TutorialStep))]
    public sealed class TutorialStepDrawer : PropertyDrawer
    {
        private static readonly Dictionary<TutorialStepType, (string field, string label)[]> Fields = new Dictionary<TutorialStepType, (string, string)[]>
        {
            { TutorialStepType.Line, new[] { ("line", "대사") } },
            { TutorialStepType.Emote, new[] { ("target", "대상 ID"), ("text", "표시 글자"), ("sprite", "아이콘(있으면 글자 대신)"), ("seconds", "보이는 시간(초, 0=다음 대사까지)") } },
            { TutorialStepType.Pose, new[] { ("sprite", "자세 그림"), ("motion", "움직임"), ("endMotion", "돌아올 때 움직임"), ("untilAction", "다음 행동까지 유지"), ("seconds", "보이는 시간(초, 0=다음 대사까지, 다음 행동까지 유지면 무시)") } },
            { TutorialStepType.WaitAllDirections, new[] { ("line", "안내 줄"), ("keys", "키 표시(위 왼쪽 아래 오른쪽 순)") } },
            { TutorialStepType.WaitAction, new[] { ("line", "안내 줄"), ("actions", "허용 행동"), ("direction", "허용 방향(0,0=모두)"), ("keys", "키 표시") } },
            { TutorialStepType.WaitFreeMove, new[] { ("line", "안내 줄"), ("actions", "허용 행동"), ("seconds", "첫 이동 뒤 시간(초)"), ("keys", "키 표시") } },
            { TutorialStepType.WaitReachCell, new[] { ("line", "안내 줄"), ("actions", "허용 행동"), ("cell", "도착 칸"), ("keys", "키 표시") } },
            { TutorialStepType.WaitClickCell, new[] { ("line", "안내 줄"), ("cell", "누를 칸"), ("seconds", "누른 뒤 연출 시간(초)"), ("sprite", "날아가는 그림"), ("keys", "키 표시") } },
            { TutorialStepType.NameInput, new[] { ("line", "안내 줄"), ("text", "기본값") } },
            { TutorialStepType.SetActive, new[] { ("target", "대상 ID"), ("active", "켜기") } },
            { TutorialStepType.Wait, new[] { ("seconds", "기다릴 시간(초)") } },
            { TutorialStepType.LoadScene, new[] { ("scenePath", "씬 경로") } },
            { TutorialStepType.SpawnEnemy, new[] { ("prefab", "적 프리팹"), ("cell", "칸"), ("direction", "방향") } },
            { TutorialStepType.RecordClear, new[] { ("text", "기록 ID") } },
            { TutorialStepType.FreePlay, new[] { ("actions", "허용 행동"), ("line", "위험 경고 대사(넘겨야 닫힘)") } },
        };
        private static (string field, string label)[] For(SerializedProperty property)
        {
            var type = (TutorialStepType)property.FindPropertyRelative("type").intValue;
            return Fields.TryGetValue(type, out var list) ? list : new (string, string)[0];
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight, gap = EditorGUIUtility.standardVerticalSpacing;
            var rect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.PropertyField(rect, property.FindPropertyRelative("type"), new GUIContent(label.text + " 종류"));
            rect.y += line + gap;
            EditorGUI.indentLevel++;
            foreach (var (field, text) in For(property))
            {
                var child = property.FindPropertyRelative(field);
                rect.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(rect, child, new GUIContent(text, child.tooltip), true);
                rect.y += rect.height + gap;
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float gap = EditorGUIUtility.standardVerticalSpacing, height = EditorGUIUtility.singleLineHeight + gap;
            foreach (var (field, _) in For(property)) height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + gap;
            return height;
        }
    }
}
