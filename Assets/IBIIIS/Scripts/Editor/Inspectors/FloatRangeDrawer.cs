using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>FloatRange를 "이름 | 최소 ~ 최대" 한 줄로 보여 준다. 음수는 입력할 수 없고, 최소가 최대보다 크면 경고색으로 표시한다(실행 시 두 값을 바꿔 사용).</summary>
    [CustomPropertyDrawer(typeof(FloatRange))]
    public sealed class FloatRangeDrawer : PropertyDrawer
    {
        private static readonly Color WarnColor = new Color(1f, .75f, .4f);

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var min = property.FindPropertyRelative("min"); var max = property.FindPropertyRelative("max");
            var minField = Field("최소", min); var maxField = Field("최대", max);
            var input = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            input.Add(minField);
            input.Add(new Label("~") { style = { unityTextAlign = TextAnchor.MiddleCenter, width = 14 } });
            input.Add(maxField);
            var row = new RangeField(preferredLabel, input);
            void Refresh()
            {
                bool swapped = min.floatValue > max.floatValue;
                foreach (var field in new[] { minField, maxField })
                    field.Q(className: FloatField.inputUssClassName).style.color = swapped ? WarnColor : StyleKeyword.Null;
            }
            row.TrackPropertyValue(min, _ => Refresh()); row.TrackPropertyValue(max, _ => Refresh());
            row.RegisterCallback<AttachToPanelEvent>(_ => Refresh());
            return row;
        }
        // 바인딩된 칸은 값을 실제로 바꿨을 때만 쓴다. 여러 에셋을 함께 선택해도 건드리지 않은 칸은 각 에셋 값을 유지한다.
        private static FloatField Field(string label, SerializedProperty value)
        {
            var field = new FloatField(label) { style = { flexGrow = 1, flexBasis = 0, marginLeft = 0, marginRight = 0 } };
            field.labelElement.style.minWidth = 28; field.labelElement.style.width = 28;
            field.BindProperty(value);
            field.RegisterValueChangedCallback(evt => { if (evt.newValue < 0) field.value = 0; });
            return field;
        }
        // Inspector의 이름 칸 너비 맞춤은 BaseField에만 적용되므로 두 칸을 BaseField 하나로 감싼다. 값은 안쪽 칸이 각각 바인딩한다.
        private sealed class RangeField : BaseField<Vector2>
        {
            public RangeField(string label, VisualElement input) : base(label, input) { AddToClassList(alignedFieldUssClassName); }
        }
    }
}
