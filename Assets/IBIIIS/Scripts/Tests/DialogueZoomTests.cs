using IBIIIS.Editor;
using NUnit.Framework;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>IBIIIS Dialogue Editor 확대·축소 계산(DialogueZoom).</summary>
    public sealed class DialogueZoomTests
    {
        [Test] public void ZoomStaysInRangeAndWheelStepsBothWays()
        {
            Assert.AreEqual(DialogueZoom.Min, DialogueZoom.Clamp(.01f)); Assert.AreEqual(DialogueZoom.Max, DialogueZoom.Clamp(9));
            Assert.AreEqual(1, DialogueZoom.Clamp(float.NaN), "잘못된 값은 100%");
            Assert.That(DialogueZoom.Wheel(1, 1), Is.LessThan(1), "휠 아래 = 축소");
            Assert.That(DialogueZoom.Wheel(1, -1), Is.GreaterThan(1), "휠 위 = 확대");
            Assert.That(DialogueZoom.Wheel(DialogueZoom.Wheel(1, -1), 1), Is.EqualTo(1).Within(1e-5f), "한 칸 확대 뒤 한 칸 축소하면 원래대로");
            Assert.AreEqual(DialogueZoom.Max, DialogueZoom.Wheel(DialogueZoom.Max, -1), "최대에서 더 커지지 않음");
        }
        [Test] public void ZoomingKeepsThePointUnderTheMouse()
        {
            var scroll = new Vector2(300, 120); var point = new Vector2(200, 100);
            var content = (scroll + point) / 1f; // 배율 1에서 마우스가 가리키는 내용 위치
            var after = DialogueZoom.ScrollKeeping(scroll, 1, 2, point);
            Assert.That(Vector2.Distance((after + point) / 2f, content), Is.LessThan(1e-3f), "배율 2에서도 같은 내용이 마우스 아래");
            Assert.AreEqual(Vector2.zero, DialogueZoom.ScrollKeeping(Vector2.zero, 1, .5f, new Vector2(10, 10)), "스크롤은 0보다 작아지지 않음");
        }
        [Test] public void FitPutsTheWholeRectInsideTheView()
        {
            var view = new Vector2(800, 400); var rect = new Rect(100, 50, 1600, 300);
            var (zoom, scroll) = DialogueZoom.Fit(view, rect);
            Assert.That(rect.width * zoom, Is.LessThanOrEqualTo(view.x)); Assert.That(rect.height * zoom, Is.LessThanOrEqualTo(view.y));
            Assert.That(zoom, Is.EqualTo((800 - 24) / 1600f).Within(1e-4f), "넓은 쪽(가로)에 맞춤");
            Assert.That(scroll.x, Is.EqualTo(100 * zoom - 12).Within(1e-3f), "영역 왼쪽 위가 여백만큼 안쪽에 보임");
            Assert.AreEqual(DialogueZoom.Max, DialogueZoom.Fit(view, new Rect(0, 0, 10, 10)).zoom, "작은 영역도 최대 배율까지만");
            Assert.AreEqual(1, DialogueZoom.Fit(view, new Rect(0, 0, 0, 0)).zoom, "빈 영역은 100%");
        }
    }
}
