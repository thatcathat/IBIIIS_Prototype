using System;
using UnityEngine;

namespace IBIIIS
{
    public sealed class GridSession
    {
        private readonly bool[,] walkable;
        private readonly int moveCost;
        private readonly int blockedCost;
        public Vector2Int Position { get; private set; }
        public int Turn { get; private set; }
        public GridSession(GridMap map, int moveTurnCost = 1, int blockedTurnCost = 0)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var errors = map.ValidateMap();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            if (moveTurnCost < 1 || blockedTurnCost < 0) throw new ArgumentOutOfRangeException(nameof(moveTurnCost));
            walkable = new bool[map.Width, map.Height];
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++) walkable[x, y] = map.IsWalkable(new Vector2Int(x, y));
            Position = map.Start; moveCost = moveTurnCost; blockedCost = blockedTurnCost;
        }
        public bool TryMove(Vector2Int direction)
        {
            if (Math.Abs(direction.x) + Math.Abs(direction.y) != 1) return false;
            var next = Position + direction;
            bool valid = next.x >= 0 && next.y >= 0 && next.x < walkable.GetLength(0) && next.y < walkable.GetLength(1) && walkable[next.x, next.y];
            Turn += valid ? moveCost : blockedCost;
            if (valid) Position = next;
            return valid;
        }
    }
}
