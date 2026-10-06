using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>FloatRange를 "이름 | 최소 ~ 최대" 한 줄로 보여 준다. 음수는 입력할 수 없고, 최소가 최대보다 크면 경고색으로 표시한다(실행 시 두 값을 바꿔 사용).</summary>
    [CustomPropertyDrawer(typeof(FloatRange))]
    public sealed class FloatRangeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var field = EditorGUI.PrefixLabel(position, label);
            var min = property.FindPropertyRelative("min"); var max = property.FindPropertyRelative("max");
            int indent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
            float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 28;
            float half = (field.width - 14) / 2;
            var minRect = new Rect(field.x, field.y, half, EditorGUIUtility.singleLineHeight);
            var dashRect = new Rect(minRect.xMax, field.y, 14, EditorGUIUtility.singleLineHeight);
            var maxRect = new Rect(dashRect.xMax, field.y, half, EditorGUIUtility.singleLineHeight);
            var previous = GUI.color; if (min.floatValue > max.floatValue) GUI.color = new Color(1f, .75f, .4f);
            min.floatValue = Mathf.Max(0, EditorGUI.FloatField(minRect, new GUIContent("최소"), min.floatValue));
            EditorGUI.LabelField(dashRect, "~");
            max.floatValue = Mathf.Max(0, EditorGUI.FloatField(maxRect, new GUIContent("최대"), max.floatValue));
            GUI.color = previous; EditorGUIUtility.labelWidth = labelWidth; EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;
    }
}
