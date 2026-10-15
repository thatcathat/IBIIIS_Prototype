using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>대본 에셋 Inspector: IBIIIS Dialogue Editor에서 이 대본 노드를 펼쳐 여는 버튼 + 기본 Inspector.</summary>
    [CustomEditor(typeof(ScenarioScript))]
    public sealed class ScenarioScriptEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new Button(() => DialogueGraphWindow.OpenScript((ScenarioScript)target)) { text = "IBIIIS Dialogue Editor에서 열기", style = { height = 26, marginBottom = 4 } });
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
    }
}
