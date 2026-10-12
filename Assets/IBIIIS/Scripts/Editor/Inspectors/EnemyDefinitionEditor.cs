using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>행동 한 줄을 종류에 맞는 항목만 보여 준다. 전진은 칸 수, 회전은 방향만 표시한다.</summary>
    [CustomPropertyDrawer(typeof(EnemyActionStep))]
    public sealed class EnemyActionStepDrawer : PropertyDrawer
    {
        /// <summary>맵 에디터 요약에 쓰는 행동 이름. 새 행동 단위를 추가하면 여기와 아래 OnGUI에 함께 추가한다.</summary>
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
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var type = property.FindPropertyRelative("type");
            var left = new Rect(position.x, position.y, position.width * .45f, EditorGUIUtility.singleLineHeight);
            var right = new Rect(left.xMax + 4, position.y, position.width - left.width - 4, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(left, type, GUIContent.none);
            switch ((EnemyActionType)type.enumValueIndex)
            {
                case EnemyActionType.MoveForward:
                    var cells = property.FindPropertyRelative("cells");
                    cells.intValue = EditorGUI.IntSlider(right, cells.intValue, 1, EnemyActionStep.MaxMoveCells); break;
                case EnemyActionType.Turn:
                    EditorGUI.PropertyField(right, property.FindPropertyRelative("turn"), GUIContent.none); break;
                case EnemyActionType.AimAtPlayer:
                    EditorGUI.LabelField(right, "행동 직전 위치 인식 시 향함", EditorStyles.miniLabel); break;
                default:
                    EditorGUI.LabelField(right, "알 수 없는 행동", EditorStyles.miniLabel); break;
            }
            EditorGUI.EndProperty();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;
    }

    /// <summary>행동 목록이 비어 이전 설정으로 동작하는 프리팹에 안내와 변환 버튼을 보여 준다.</summary>
    [CustomEditor(typeof(EnemyDefinition))]
    public sealed class EnemyDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var definition = (EnemyDefinition)target;
            if (!definition.IsValid) EditorGUILayout.HelpBox("이 설정으로는 맵에 배치·플레이할 수 없습니다: " + string.Join(", ", definition.DescribeProblems()) + ".", MessageType.Error);
            if (definition.UsesLegacyActions)
            {
                EditorGUILayout.HelpBox($"행동 목록이 비어 있어 이전 설정으로 동작합니다: 인식 시 조준 → {definition.LegacyMoveCells}칸 전진. 변환하면 이 목록을 직접 편집할 수 있습니다.", MessageType.Info);
                if (GUILayout.Button("이전 설정을 행동 목록으로 변환")) ConvertLegacy(serializedObject);
            }
            DrawDefaultInspector();
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
