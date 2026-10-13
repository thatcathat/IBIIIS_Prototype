using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>행동 한 줄을 종류에 맞는 항목만 보여 준다. 전진은 칸 수, 회전은 방향만 표시한다.</summary>
    [CustomPropertyDrawer(typeof(EnemyActionStep))]
    public sealed class EnemyActionStepDrawer : PropertyDrawer
    {
        /// <summary>맵 에디터 요약에 쓰는 행동 이름. 새 행동 단위를 추가하면 여기와 아래 Detail에 함께 추가한다.</summary>
        public static string Describe(EnemyActionStep step)
        {
            switch (step.Type)
            {
                case EnemyActionType.AimAtPlayer: return "조준";
                case EnemyActionType.MoveForward: return $"전진 {step.Cells}칸";
                case EnemyActionType.Turn: return $"회전({step.Turn})";
                default: return $"알 수 없는 행동({(int)step.Type})";
            }
        }
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var type = property.FindPropertyRelative("type");
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var typeField = new PropertyField(type, string.Empty) { style = { width = Length.Percent(45), flexShrink = 0 } };
            typeField.BindProperty(type);
            var right = new VisualElement { style = { flexGrow = 1, marginLeft = 4 } };
            row.Add(typeField); row.Add(right);
            void Rebuild() { right.Clear(); right.Add(Detail(property, (EnemyActionType)type.enumValueIndex)); }
            row.TrackPropertyValue(type, _ => Rebuild());
            Rebuild();
            return row;
        }
        private static VisualElement Detail(SerializedProperty property, EnemyActionType type)
        {
            switch (type)
            {
                case EnemyActionType.MoveForward:
                    var cells = new SliderInt(1, EnemyActionStep.MaxMoveCells) { showInputField = true };
                    cells.BindProperty(property.FindPropertyRelative("cells"));
                    return cells;
                case EnemyActionType.Turn:
                    var turn = new EnumField(default(EnemyTurn));
                    turn.BindProperty(property.FindPropertyRelative("turn"));
                    return turn;
                case EnemyActionType.AimAtPlayer: return Note("행동 직전 위치 인식 시 향함");
                default: return Note("알 수 없는 행동");
            }
        }
        private static Label Note(string text) => new Label(text) { style = { fontSize = 10, color = new Color(.6f, .6f, .6f), unityTextAlign = TextAnchor.MiddleLeft } };
    }

    /// <summary>행동 목록이 비어 이전 설정으로 동작하는 프리팹에 안내와 변환 버튼을 보여 준다.</summary>
    [CustomEditor(typeof(EnemyDefinition))]
    public sealed class EnemyDefinitionEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var status = new VisualElement();
            root.Add(status);
            string shown = null;
            void Refresh()
            {
                var definition = (EnemyDefinition)target;
                if (definition == null) return;
                string error = definition.IsValid ? null : "이 설정으로는 맵에 배치·플레이할 수 없습니다: " + string.Join(", ", definition.DescribeProblems()) + ".";
                string legacy = definition.UsesLegacyActions ? $"행동 목록이 비어 있어 이전 설정으로 동작합니다: 인식 시 조준 → {definition.LegacyMoveCells}칸 전진. 변환하면 이 목록을 직접 편집할 수 있습니다." : null;
                string key = error + "|" + legacy;
                if (key == shown) return;
                shown = key; status.Clear();
                if (error != null) status.Add(new HelpBox(error, HelpBoxMessageType.Error));
                if (legacy != null)
                {
                    status.Add(new HelpBox(legacy, HelpBoxMessageType.Info));
                    status.Add(new Button(() => ConvertLegacy(serializedObject)) { text = "이전 설정을 행동 목록으로 변환" });
                }
            }
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            Refresh();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
        /// <summary>행동 목록이 비어 있을 때만 [조준, 전진]으로 채운다. 이미 목록이 있으면 아무것도 바꾸지 않는다.</summary>
        public static bool ConvertLegacy(SerializedObject serialized)
        {
            var definition = (EnemyDefinition)serialized.targetObject;
            if (!definition.UsesLegacyActions) return false;
            var actions = definition.Actions;
            serialized.Update();
            var property = serialized.FindProperty("actions"); property.arraySize = actions.Length;
            for (int i = 0; i < actions.Length; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("type").enumValueIndex = (int)actions[i].Type;
                element.FindPropertyRelative("cells").intValue = actions[i].Cells;
                element.FindPropertyRelative("turn").enumValueIndex = (int)actions[i].Turn;
            }
            serialized.ApplyModifiedProperties();
            return true;
        }
    }
}
