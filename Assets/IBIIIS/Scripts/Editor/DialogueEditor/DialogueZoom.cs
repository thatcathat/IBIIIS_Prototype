using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor 노드 화면의 확대·축소 계산. 화면 요소 없이 값만 다룬다(자동 테스트 대상).
    /// 배율 1 = 원래 크기. 내용 좌표는 배율을 곱하기 전, 스크롤 위치는 배율을 곱한 뒤의 값이다.</summary>
    public static class DialogueZoom
    {
        public const float Min = .3f, Max = 2f, Step = 1.1f;
        public static float Clamp(float zoom) => float.IsNaN(zoom) || zoom <= 0 ? 1 : Mathf.Clamp(zoom, Min, Max);
        /// <summary>휠 한 칸(delta.y 양수 = 아래로 굴림 = 축소)에 따른 새 배율.</summary>
        public static float Wheel(float zoom, float deltaY) => Clamp(deltaY > 0 ? zoom / Step : deltaY < 0 ? zoom * Step : zoom);
        /// <summary>배율을 바꿔도 화면의 point(보이는 영역 기준 위치)가 가리키던 내용이 그대로 그 자리에 있도록 하는 새 스크롤 위치.</summary>
        public static Vector2 ScrollKeeping(Vector2 scroll, float zoom, float newZoom, Vector2 point)
        {
            var content = (scroll + point) / zoom;
            return Vector2.Max(Vector2.zero, content * newZoom - point);
        }
        /// <summary>내용의 영역(rect, 배율 전 좌표)이 보이는 영역(view)에 여백(margin, 화면 픽셀)을 두고 다 들어가는 배율과 스크롤 위치.</summary>
        public static (float zoom, Vector2 scroll) Fit(Vector2 view, Rect rect, float margin = 12)
        {
            if (rect.width <= 0 || rect.height <= 0 || view.x <= margin * 2 || view.y <= margin * 2) return (1, Vector2.zero);
            float zoom = Clamp(Mathf.Min((view.x - margin * 2) / rect.width, (view.y - margin * 2) / rect.height));
            return (zoom, Vector2.Max(Vector2.zero, rect.min * zoom - Vector2.one * margin));
        }
    }
}
