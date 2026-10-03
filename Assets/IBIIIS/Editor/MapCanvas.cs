using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    internal sealed class MapCanvas : VisualElement
    {
        public GridMap Map;
        private readonly MapEditorWindow owner;
        private float size = 32;
        private Vector2 offset = new Vector2(28, 28);
        private Vector2 last;
        private bool drawing, panning;
        private int pointer = -1;
        public MapCanvas(MapEditorWindow window)
        {
            owner = window; focusable = true;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(Down); RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(e => Stop()); RegisterCallback<PointerCaptureOutEvent>(e => Stop());
            RegisterCallback<DetachFromPanelEvent>(e => Stop());
            RegisterCallback<WheelEvent>(e =>
            {
                if (drawing || panning) return;
                float next = Mathf.Clamp(size * (e.delta.y > 0 ? .9f : 1.1f), 8, 96);
                var mouse = e.localMousePosition; offset = mouse - (mouse - offset) * next / size; size = next;
                MarkDirtyRepaint(); e.StopPropagation();
            });
        }
        public void ResetView() { size = 32; offset = new Vector2(28, 28); MarkDirtyRepaint(); }
        private Vector2Int Cell(Vector2 pos) => new Vector2Int(Mathf.FloorToInt((pos.x - offset.x) / size), Map.Height - 1 - Mathf.FloorToInt((pos.y - offset.y) / size));
        private void Down(PointerDownEvent e)
        {
            if (Map == null || pointer >= 0) return;
            Focus(); last = e.localPosition;
            if (e.button == 1) { owner.Paint(Cell(last), true); e.StopPropagation(); return; }
            if (e.button != 0 && e.button != 2) return;
            pointer = e.pointerId; drawing = e.button == 0; panning = e.button == 2;
            this.CapturePointer(pointer);
            if (drawing) { owner.BeginStroke(); owner.Paint(Cell(last), false); }
            e.StopPropagation();
        }
        private void Move(PointerMoveEvent e)
        {
            if (Map == null || pointer != e.pointerId) return;
            Vector2 next = e.localPosition;
            if (panning) { offset += next - last; MarkDirtyRepaint(); }
            if (drawing)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(last, next) / (size * .3f)));
                Vector2Int previous = new Vector2Int(int.MinValue, int.MinValue);
                for (int i = 1; i <= steps; i++) { var p = Cell(Vector2.Lerp(last, next, i / (float)steps)); if (p != previous) owner.Paint(p, false); previous = p; }
            }
            last = next;
        }
        private void Stop()
        {
            int old = pointer; pointer = -1; drawing = panning = false; owner.EndStroke();
            if (old >= 0 && this.HasPointerCapture(old)) this.ReleasePointer(old);
        }
        private static void Rect(Painter2D p, Vector2 o, float w, float h)
        {
            p.BeginPath(); p.MoveTo(o); p.LineTo(o + new Vector2(w, 0)); p.LineTo(o + new Vector2(w, h)); p.LineTo(o + new Vector2(0, h)); p.ClosePath();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (Map == null || contentRect.width < 1 || contentRect.height < 1) return;
            var painter = context.painter2D;
            for (int y = 0; y < Map.Height; y++) for (int x = 0; x < Map.Width; x++)
            {
                var cell = new Vector2Int(x, y); var o = offset + new Vector2(x, Map.Height - 1 - y) * size;
                if (o.x + size < 0 || o.y + size < 0 || o.x > contentRect.width || o.y > contentRect.height) continue;
                var tile = Map.GetTile(cell);
                painter.fillColor = Map.IsWalkable(cell) ? Map.GetFloorColor(cell) : (tile != null || string.IsNullOrEmpty(Map.GetId(cell))) ? new Color(.16f, .19f, .23f) : Color.magenta;
                Rect(painter, o + Vector2.one, size - 2, size - 2); painter.Fill();
                if (Map.HasStart && Map.Start == cell)
                { painter.strokeColor = Color.yellow; painter.lineWidth = 3; Rect(painter, o + Vector2.one * 3, size - 6, size - 6); painter.Stroke(); }
            }
        }
    }
}
