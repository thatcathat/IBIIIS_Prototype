using UnityEngine;

namespace IBIIIS
{
    /// <summary>임시 화면 표시(IMGUI). NPC 말풍선과, Game UI 설정이 없을 때 미니맵 안내·결과 팝업의 대체 표시에 쓴다.</summary>
    internal static class OverworldGui
    {
        private static GUIStyle bubble, prompt, title, body, button;
        public static GUIStyle Bubble => bubble ??= new GUIStyle(GUI.skin.box) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(14, 14, 10, 10), normal = { textColor = Color.white } };
        public static GUIStyle Prompt => prompt ??= new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 6, 6), normal = { textColor = new Color(1f, .9f, .5f) } };
        public static GUIStyle Title => title ??= new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        public static GUIStyle Body => body ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        public static GUIStyle Button => button ??= new GUIStyle(GUI.skin.button) { fontSize = 18 };
        /// <summary>월드 위치를 IMGUI 화면 좌표로 바꾼다. 카메라 뒤면 false.</summary>
        public static bool ToGui(Camera camera, Vector3 world, out Vector2 point)
        {
            point = default;
            if (camera == null) return false;
            var screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0) return false;
            point = new Vector2(screen.x, Screen.height - screen.y); return true;
        }
        /// <summary>점 위쪽 가운데에 내용 크기에 맞춘 상자를 그린다.</summary>
        public static void BoxAbove(Vector2 anchor, string text, GUIStyle style, float maxWidth)
        {
            var content = new GUIContent(text);
            float width = Mathf.Min(maxWidth, style.CalcSize(content).x + 4);
            float height = style.CalcHeight(content, width);
            GUI.Box(new Rect(anchor.x - width / 2, anchor.y - height, width, height), content, style);
        }
    }
}
