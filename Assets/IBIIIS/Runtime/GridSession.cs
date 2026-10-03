using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    public sealed class GridSession
    {
        private readonly bool[,] walkable;
        private float elapsed, duration;
        public Vector2Int Position { get; private set; }
        public Vector2Int Destination { get; private set; }
        public bool IsMoving { get; private set; }
        public float Progress => IsMoving ? Mathf.Clamp01(elapsed / duration) : 0;
        public float ActiveDuration => duration;
        public GridSession(GridMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var errors = map.ValidateMap();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            walkable = new bool[map.Width, map.Height];
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++) walkable[x, y] = map.IsWalkable(new Vector2Int(x, y));
            Position = Destination = map.Start;
        }
        public static bool CanStep(Vector2Int position, Vector2Int direction, Func<Vector2Int, bool> isWalkable)
            => Math.Abs((long)direction.x) + Math.Abs((long)direction.y) == 1 && isWalkable(position + direction);
        private bool IsWalkable(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < walkable.GetLength(0) && p.y < walkable.GetLength(1) && walkable[p.x, p.y];
        public bool CanMove(Vector2Int direction) => !IsMoving && CanStep(Position, direction, IsWalkable);
        public bool TryMove(Vector2Int direction, float moveDuration = .25f)
        {
            if (!CanMove(direction)) return false;
            if (float.IsNaN(moveDuration) || float.IsInfinity(moveDuration) || moveDuration <= 0) throw new ArgumentOutOfRangeException(nameof(moveDuration));
            Destination = Position + direction; duration = moveDuration; elapsed = 0; IsMoving = true; return true;
        }
        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!IsMoving) return;
            elapsed = Mathf.Min(duration, elapsed + seconds);
            if (elapsed < duration) return;
            Position = Destination; IsMoving = false;
        }
    }

    public static class MovementWorldTime
    {
        private static readonly HashSet<object> owners = new HashSet<object>();
        private static readonly HashSet<object> moving = new HashSet<object>();
        private static float previousScale = 1;
        public static void Register(object owner)
        {
            if (owners.Contains(owner)) return;
            if (owners.Count == 0) previousScale = Time.timeScale;
            owners.Add(owner); Apply();
        }
        public static void SetMoving(object owner, bool value)
        {
            if (!owners.Contains(owner)) return;
            if (value) moving.Add(owner); else moving.Remove(owner);
            Apply();
        }
        public static void Unregister(object owner)
        {
            if (!owners.Remove(owner)) return;
            moving.Remove(owner);
            if (owners.Count == 0) Time.timeScale = previousScale; else Apply();
        }
        private static void Apply() { Time.timeScale = moving.Count > 0 ? 1 : 0; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (owners.Count > 0) Time.timeScale = previousScale;
            owners.Clear(); moving.Clear();
        }
    }
}