using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>대본 단계 한 칸을 종류에 맞는 항목만 보여 준다(쓰지 않는 칸은 숨김).</summary>
    [CustomPropertyDrawer(typeof(ScenarioStep))]
    public sealed class ScenarioStepDrawer : PropertyDrawer
    {
        private static readonly Dictionary<ScenarioStepType, (string field, string label)[]> Fields = new Dictionary<ScenarioStepType, (string, string)[]>
        {
            { ScenarioStepType.Line, new[] { ("line", "대사") } },
            { ScenarioStepType.Emote, new[] { ("target", "대상 ID"), ("text", "표시 글자"), ("sprite", "아이콘(있으면 글자 대신)"), ("untilEnd", "표시 끝 단계까지 유지"), ("seconds", "보이는 시간(초, 0=다음 대사까지, 표시 끝 단계까지 유지면 무시)") } },
            { ScenarioStepType.Pose, new[] { ("sprite", "자세 그림"), ("motion", "움직임"), ("endMotion", "돌아올 때 움직임"), ("untilEnd", "표시 끝 단계까지 유지"), ("untilAction", "다음 행동까지 유지"), ("seconds", "보이는 시간(초, 0=다음 대사까지, 유지 옵션이 켜져 있으면 무시)") } },
            { ScenarioStepType.EndDisplay, new[] { ("display", "끝낼 표시"), ("target", "대상 ID(감정 표시만)") } },
            { ScenarioStepType.WaitAllDirections, new[] { ("line", "안내 줄"), ("keys", "키 표시(위 왼쪽 아래 오른쪽 순)") } },
            { ScenarioStepType.WaitAction, new[] { ("line", "안내 줄"), ("actions", "허용 행동"), ("direction", "허용 방향(0,0=모두)"), ("keys", "키 표시") } },
            { ScenarioStepType.WaitFreeMove, new[] { ("line", "안내 줄"), ("actions", "허용 행동"), ("seconds", "첫 이동 뒤 시간(초)"), ("keys", "키 표시") } },
            { ScenarioStepType.WaitReachCell, new[] { ("line", "안내 줄"), ("actions", "허용 행동"), ("cell", "도착 칸"), ("keys", "키 표시") } },
            { ScenarioStepType.WaitClickCell, new[] { ("line", "안내 줄"), ("cell", "누를 칸"), ("seconds", "누른 뒤 연출 시간(초)"), ("sprite", "날아가는 그림"), ("keys", "키 표시") } },
            { ScenarioStepType.NameInput, new[] { ("line", "안내 줄"), ("text", "기본값") } },
            { ScenarioStepType.SetActive, new[] { ("target", "대상 ID"), ("active", "켜기") } },
            { ScenarioStepType.Wait, new[] { ("seconds", "기다릴 시간(초)") } },
            { ScenarioStepType.LoadScene, new[] { ("scenePath", "씬 경로") } },
            { ScenarioStepType.SpawnEnemy, new[] { ("prefab", "적 프리팹"), ("cell", "칸"), ("direction", "방향") } },
            { ScenarioStepType.RecordClear, new[] { ("text", "기록 ID") } },
            { ScenarioStepType.FreePlay, new[] { ("actions", "허용 행동"), ("line", "위험 경고 대사(넘겨야 닫힘)") } },
        };
        private static (string field, string label)[] For(SerializedProperty property)
        {
            var type = (ScenarioStepType)property.FindPropertyRelative("type").intValue;
            return Fields.TryGetValue(type, out var list) ? list : new (string, string)[0];
        }
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var type = property.FindPropertyRelative("type");
            var typeField = new PropertyField(type, preferredLabel + " 종류");
            typeField.BindProperty(type);
            var fields = new VisualElement { style = { marginLeft = 15 } };
            root.Add(typeField); root.Add(fields);
            void Rebuild()
            {
                fields.Clear();
                foreach (var (field, text) in For(property))
                {
                    var child = property.FindPropertyRelative(field);
                    var element = new PropertyField(child, text) { tooltip = child.tooltip };
                    element.BindProperty(child);
                    fields.Add(element);
                }
            }
            root.TrackPropertyValue(type, _ => Rebuild());
            Rebuild();
            return root;
        }
    }
}
