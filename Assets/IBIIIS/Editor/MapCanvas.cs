using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>맵 격자를 그리고 마우스 입력을 편집 명령으로 바꾼다. 편집 규칙과 상태는 MapEditorWindow가 가진다.</summary>
    internal sealed class MapCanvas : VisualElement
    {
        private static readonly Color EmptyCell = new Color(.16f, .19f, .23f);
        private static readonly Color RecognitionColor = EnemyRangeColors.Recognition;
        private static readonly Color AttackColor = EnemyRangeColors.Attack;
        // 경고(놓을 수 없는 칸·패배)는 범위 색과 따로 둔다.
        private static readonly Color WarningColor = new Color(.95f, .2f, .2f);
        private static readonly Color SelectionColor = new Color(.3f, .85f, 1f);
        private readonly MapEditorWindow owner;
        private float size = 32;
        private Vector2 offset = new Vector2(28, 28);
        private Vector2 last;
        private bool drawing, panning;
        private int pointer = -1;
        private Vector2Int? hover;
        private GridMap Map => owner.Map;
        public MapCanvas(MapEditorWindow window)
        {
            owner = window; focusable = true;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(Down); RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(e => Stop()); RegisterCallback<PointerCaptureOutEvent>(e => Stop());
            RegisterCallback<PointerLeaveEvent>(e => SetHover(null));
            RegisterCallback<DetachFromPanelEvent>(e => Stop());
            RegisterCallback<WheelEvent>(e =>
            {
                if (drawing || panning) return;
                float next = Mathf.Clamp(size * (e.delta.y > 0 ? .9f : 1.1f), 8, 96);
                var mouse = e.localMousePosition; offset = mouse - (mouse - offset) * next / size; size = next;
                MarkDirtyRepaint(); e.StopPropagation();
            });
        }
        /// <summary>맵 전체가 보이도록 확대 배율과 위치를 맞춘다.</summary>
        public void ResetView()
        {
            if (Map == null || contentRect.width < 1 || contentRect.height < 1) { size = 32; offset = new Vector2(28, 28); MarkDirtyRepaint(); return; }
            size = Mathf.Clamp(Mathf.Min((contentRect.width - 40) / Map.Width, (contentRect.height - 60) / Map.Height), 8, 96);
            offset = new Vector2((contentRect.width - size * Map.Width) / 2, Mathf.Max(10, (contentRect.height - 30 - size * Map.Height) / 2));
            MarkDirtyRepaint();
        }
        private Vector2Int Cell(Vector2 pos) => new Vector2Int(Mathf.FloorToInt((pos.x - offset.x) / size), Map.Height - 1 - Mathf.FloorToInt((pos.y - offset.y) / size));
        private Vector2 Origin(Vector2Int cell) => offset + new Vector2(cell.x, Map.Height - 1 - cell.y) * size;
        private void SetHover(Vector2Int? cell)
        {
            if (cell.HasValue && (Map == null || !Map.Contains(cell.Value))) cell = null;
            if (hover == cell) return;
            hover = cell; owner.SetHover(cell); MarkDirtyRepaint();
        }
        private void Down(PointerDownEvent e)
        {
            if (Map == null || pointer >= 0) return;
            Focus(); last = e.localPosition;
            // 오른쪽 클릭과 선택 도구는 칸만 선택하고 맵을 바꾸지 않는다.
            if (e.button == 1 || (e.button == 0 && owner.Tool == MapTool.Select)) { owner.SelectCell(Cell(last)); e.StopPropagation(); return; }
            if (e.button != 0 && e.button != 2) return;
            pointer = e.pointerId; drawing = e.button == 0; panning = e.button == 2;
            this.CapturePointer(pointer);
            if (drawing) { owner.BeginStroke(); owner.Paint(Cell(last)); }
            e.StopPropagation();
        }
        private void Move(PointerMoveEvent e)
        {
            if (Map == null) return;
            Vector2 next = e.localPosition;
            SetHover(Cell(next));
            if (pointer != e.pointerId) return;
            if (panning) { offset += next - last; MarkDirtyRepaint(); }
            if (drawing)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(last, next) / (size * .3f)));
                Vector2Int previous = new Vector2Int(int.MinValue, int.MinValue);
                for (int i = 1; i <= steps; i++) { var p = Cell(Vector2.Lerp(last, next, i / (float)steps)); if (p != previous) owner.Paint(p); previous = p; }
            }
            last = next;
        }
        private void Stop()
        {
            int old = pointer; pointer = -1; drawing = panning = false; owner.EndStroke();
            if (old >= 0 && this.HasPointerCapture(old)) this.ReleasePointer(old);
        }

        // ---------- 그리기 ----------
        private static void Rect(Painter2D p, Vector2 o, float w, float h)
        {
            p.BeginPath(); p.MoveTo(o); p.LineTo(o + new Vector2(w, 0)); p.LineTo(o + new Vector2(w, h)); p.LineTo(o + new Vector2(0, h)); p.ClosePath();
        }
        private void FillCell(Painter2D p, Vector2Int cell, Color color, float inset = 1)
        { p.fillColor = color; Rect(p, Origin(cell) + Vector2.one * inset, size - inset * 2, size - inset * 2); p.Fill(); }
        private void OutlineCell(Painter2D p, Vector2Int cell, Color color, float width, float inset)
        { p.strokeColor = color; p.lineWidth = width; Rect(p, Origin(cell) + Vector2.one * inset, size - inset * 2, size - inset * 2); p.Stroke(); }
        private void Triangle(Painter2D p, Vector2Int cell, Vector2Int direction, Color fill, Color stroke)
        {
            var center = Origin(cell) + Vector2.one * size * .5f;
            var forward = new Vector2(direction.x, -direction.y); var side = new Vector2(-forward.y, forward.x);
            p.BeginPath(); p.MoveTo(center + forward * size * .35f);
            p.LineTo(center - forward * size * .25f + side * size * .25f); p.LineTo(center - forward * size * .25f - side * size * .25f); p.ClosePath();
            p.fillColor = fill; p.Fill(); p.strokeColor = stroke; p.lineWidth = 1; p.Stroke();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (Map == null || contentRect.width < 1 || contentRect.height < 1) return;
            var painter = context.painter2D;
            for (int y = 0; y < Map.Height; y++) for (int x = 0; x < Map.Width; x++)
            {
                var cell = new Vector2Int(x, y); var o = Origin(cell);
                if (o.x + size < 0 || o.y + size < 0 || o.x > contentRect.width || o.y > contentRect.height) continue;
                var tile = Map.GetTile(cell);
                FillCell(painter, cell, Map.IsWalkable(cell) ? Map.GetFloorColor(cell) : (tile != null || string.IsNullOrEmpty(Map.GetId(cell))) ? EmptyCell : Color.magenta);
            }
            if (Map.HasStart && Map.Contains(Map.Start)) OutlineCell(painter, Map.Start, Color.yellow, 3, 3);
            if (owner.Replay != null) DrawReplay(painter, owner.Replay.Current);
            else
            {
                DrawRanges(painter);
                foreach (var enemy in Map.Enemies)
                {
                    if (enemy == null) continue;
                    var definition = enemy.Prefab != null ? enemy.Prefab.GetComponent<EnemyDefinition>() : null;
                    Triangle(painter, enemy.Position, enemy.Direction, definition != null ? definition.EditorColor : Color.magenta, Color.black);
                }
            }
            if (owner.Selection is Vector2Int selected && Map.Contains(selected)) OutlineCell(painter, selected, SelectionColor, 3, 0);
            DrawHover(painter);
        }
        // 검증 경로 재생: 이번 행동 뒤 공격 칸, 플레이어 이동 선, 적(제거된 적은 흐린 ×), 플레이어 원을 그린다. 맵의 정적 적 배치 대신 그린다.
        private void DrawReplay(Painter2D painter, MapReplay.Frame frame)
        {
            // 전투 화면(Tab 범위 표시)과 같게 이동 가능한 칸의 공격 범위만 칠한다.
            foreach (var cell in frame.Attack) if (Map.IsWalkable(cell)) FillCell(painter, cell, new Color(AttackColor.r, AttackColor.g, AttackColor.b, .45f), 4);
            for (int i = 0; i < frame.EnemyPositions.Length && i < Map.Enemies.Count; i++)
            {
                var placement = Map.Enemies[i];
                var definition = placement != null && placement.Prefab != null ? placement.Prefab.GetComponent<EnemyDefinition>() : null;
                var color = definition != null ? definition.EditorColor : Color.magenta;
                if (frame.EnemyAlive[i]) Triangle(painter, frame.EnemyPositions[i], frame.EnemyDirections[i], color, Color.black);
                else Cross(painter, frame.EnemyPositions[i], new Color(color.r, color.g, color.b, .45f), 3);
            }
            Vector2 Center(Vector2Int c) => Origin(c) + Vector2.one * size * .5f;
            if (frame.From != frame.Player)
            {
                painter.strokeColor = Color.white; painter.lineWidth = 3; painter.lineCap = LineCap.Round;
                painter.BeginPath(); painter.MoveTo(Center(frame.From)); painter.LineTo(Center(frame.Player)); painter.Stroke();
            }
            var playerColor = frame.Phase == BattlePhase.Lost ? WarningColor : frame.Phase == BattlePhase.Won ? new Color(.35f, .9f, .45f) : SelectionColor;
            painter.BeginPath(); painter.Arc(Center(frame.Player), size * .28f, new Angle(0), new Angle(360)); painter.ClosePath();
            painter.fillColor = playerColor; painter.Fill(); painter.strokeColor = Color.white; painter.lineWidth = 2; painter.Stroke();
        }
        private void Cross(Painter2D painter, Vector2Int cell, Color color, float width)
        {
            var o = Origin(cell); painter.strokeColor = color; painter.lineWidth = width;
            painter.BeginPath(); painter.MoveTo(o + Vector2.one * size * .25f); painter.LineTo(o + Vector2.one * size * .75f);
            painter.MoveTo(o + new Vector2(size * .75f, size * .25f)); painter.LineTo(o + new Vector2(size * .25f, size * .75f)); painter.Stroke();
        }
        // 게임의 범위 표시와 같은 색: 노란 테두리=인식, 빨간 칸=공격, 빨간 테두리=인식 후에만 추가되는 공격 칸. 이동 가능한 칸만 그린다.
        private void DrawRanges(Painter2D painter)
        {
            foreach (var enemy in Map.Enemies)
            {
                if (enemy == null) continue;
                bool focused = owner.ShowAllRanges || enemy.Position == hover || enemy.Position == owner.Selection;
                var definition = enemy.Prefab != null ? enemy.Prefab.GetComponent<EnemyDefinition>() : null;
                if (!focused || definition == null || !definition.IsValid) continue;
                var attack = new HashSet<Vector2Int>();
                foreach (var cell in Cells(enemy, definition.Attack)) { attack.Add(cell); FillCell(painter, cell, new Color(AttackColor.r, AttackColor.g, AttackColor.b, .45f), 4); }
                foreach (var cell in Cells(enemy, definition.RecognizedAttack)) if (!attack.Contains(cell)) OutlineCell(painter, cell, AttackColor, 2, 5);
                foreach (var cell in Cells(enemy, definition.Recognition)) OutlineCell(painter, cell, RecognitionColor, 2, 2);
            }
        }
        private IEnumerable<Vector2Int> Cells(EnemyPlacement enemy, Vector2Int[] offsets)
        {
            foreach (var offset in offsets)
            {
                var cell = enemy.Position + GridSession.LocalToGrid(offset, enemy.Direction);
                if (Map.Contains(cell) && Map.IsWalkable(cell)) yield return cell;
            }
        }
        // 마우스 아래 칸에 현재 도구의 결과를 미리 보여 준다. 적용할 수 없는 칸은 빨간 테두리로 표시한다.
        private void DrawHover(Painter2D painter)
        {
            if (!(hover is Vector2Int cell) || panning) return;
            bool valid = owner.CanApply(cell);
            switch (owner.Tool)
            {
                case MapTool.Paint:
                    var color = owner.SelectedFloor != null ? owner.SelectedFloor.Color : Map.MovementColor;
                    FillCell(painter, cell, new Color(color.r, color.g, color.b, .6f), 4); break;
                case MapTool.Erase:
                case MapTool.EraseEnemy:
                    if (valid && (owner.Tool == MapTool.EraseEnemy || Map.IsWalkable(cell)))
                    {
                        var o = Origin(cell); painter.strokeColor = WarningColor; painter.lineWidth = 2;
                        painter.BeginPath(); painter.MoveTo(o + Vector2.one * 6); painter.LineTo(o + Vector2.one * (size - 6));
                        painter.MoveTo(o + new Vector2(size - 6, 6)); painter.LineTo(o + new Vector2(6, size - 6)); painter.Stroke();
                    }
                    break;
                case MapTool.Start: if (valid) OutlineCell(painter, cell, new Color(1, 1, 0, .7f), 2, 5); break;
                case MapTool.PlaceEnemy:
                    var definition = owner.SelectedEnemy != null ? owner.SelectedEnemy.GetComponent<EnemyDefinition>() : null;
                    if (valid && definition != null) { var c = definition.EditorColor; Triangle(painter, cell, owner.PlaceFacing, new Color(c.r, c.g, c.b, .55f), Color.white); }
                    break;
            }
            OutlineCell(painter, cell, valid ? Color.white : WarningColor, valid ? 1.5f : 2.5f, 0);
        }
    }
}
