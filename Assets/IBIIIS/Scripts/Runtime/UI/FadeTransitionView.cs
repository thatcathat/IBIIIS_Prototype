using UnityEngine;

namespace IBIIIS
{
    /// <summary>기본 전환 화면: 화면 전체를 한 색(기본 검정)으로 덮는다. IMGUI로 맨 위에 그린다.</summary>
    public sealed class FadeTransitionView : MonoBehaviour, ISceneTransitionView
    {
        private static Texture2D white;
        public Color Color { get; set; } = Color.black;
        public float Cover { get; private set; }
        public void Show(SceneTransitionPhase phase, float cover, float loadProgress) => Cover = Mathf.Clamp01(cover);
        private void OnGUI()
        {
            if (Cover <= 0 || Event.current.type != EventType.Repaint) return;
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; }
            GUI.depth = -1000;
            var previous = GUI.color; GUI.color = new Color(Color.r, Color.g, Color.b, Color.a * Cover);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            GUI.color = previous;
        }
    }
}
