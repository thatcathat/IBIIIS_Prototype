using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IBIIIS
{
    public enum BattleCommandKind { None, Restart, ToggleRanges, Undo, Wait, Roll, Move, Dash }
    public readonly struct BattleCommand
    {
        public readonly BattleCommandKind Kind;
        public readonly Vector2Int Direction;
        public BattleCommand(BattleCommandKind kind, Vector2Int direction = default) { Kind = kind; Direction = direction; }
    }
    /// <summary>전투 입력을 Input Actions 에셋에서 읽어 한 번의 명령으로 바꾼다. 키 배치는 에셋의 바인딩에서 바꾼다.
    /// 에셋이 없으면 코드의 기본 배치를 쓴다. 에셋은 사본을 만들어 쓰므로 원본의 활성 상태·재바인딩 실행 상태를 건드리지 않는다.</summary>
    public sealed class BattleInput : IDisposable
    {
        public const string MapName = "Battle";
        public const string MoveUp = "MoveUp", MoveDown = "MoveDown", MoveLeft = "MoveLeft", MoveRight = "MoveRight", DashModifier = "DashModifier";
        public const string RollUpLeft = "RollUpLeft", RollUpRight = "RollUpRight", RollDownLeft = "RollDownLeft", RollDownRight = "RollDownRight";
        public const string Wait = "Wait", Undo = "Undo", Restart = "Restart", ToggleRanges = "ToggleRanges";
        private static readonly string[] Required = { MoveUp, MoveDown, MoveLeft, MoveRight, DashModifier, RollUpLeft, RollUpRight, RollDownLeft, RollDownRight, Wait, Undo, Restart, ToggleRanges };
        private readonly InputActionAsset asset;
        private readonly InputActionMap map;
        /// <summary>기본 키 배치. 에디터의 `IBIIIS > Create Default Input Actions`가 같은 내용을 .inputactions 파일로 저장한다.</summary>
        public static InputActionAsset CreateDefaultAsset()
        {
            var created = ScriptableObject.CreateInstance<InputActionAsset>(); created.name = "IBIIISInput";
            var battle = created.AddActionMap(MapName);
            Add(battle, MoveUp, "<Keyboard>/w", "<Keyboard>/upArrow"); Add(battle, MoveDown, "<Keyboard>/s", "<Keyboard>/downArrow");
            Add(battle, MoveLeft, "<Keyboard>/a", "<Keyboard>/leftArrow"); Add(battle, MoveRight, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Add(battle, DashModifier, "<Keyboard>/leftShift", "<Keyboard>/rightShift");
            Add(battle, RollUpLeft, "<Keyboard>/q"); Add(battle, RollUpRight, "<Keyboard>/e"); Add(battle, RollDownLeft, "<Keyboard>/z"); Add(battle, RollDownRight, "<Keyboard>/c");
            Add(battle, Wait, "<Keyboard>/space"); Add(battle, Undo, "<Keyboard>/backspace", "<Keyboard>/u");
            Add(battle, Restart, "<Keyboard>/r"); Add(battle, ToggleRanges, "<Keyboard>/tab");
            return created;
        }
        private static void Add(InputActionMap target, string name, params string[] paths)
        {
            var action = target.AddAction(name, InputActionType.Button);
            foreach (var path in paths) action.AddBinding(path);
        }
        /// <summary>필수 액션이 없는 에셋이면 누락된 이름을 알려 준다. 모두 있으면 null.</summary>
        public static string FindMissingAction(InputActionAsset source)
        {
            var battle = source != null ? source.FindActionMap(MapName) : null;
            if (battle == null) return $"액션 맵 '{MapName}'";
            foreach (var name in Required) if (battle.FindAction(name) == null) return $"액션 '{name}'";
            return null;
        }
        public BattleInput(InputActionAsset source = null)
        {
            if (source != null && FindMissingAction(source) == null) asset = UnityEngine.Object.Instantiate(source);
            else
            {
                if (source != null) Debug.LogWarning($"[IBIIIS] 입력 에셋 '{source.name}'에 {FindMissingAction(source)}이(가) 없어 기본 키 배치를 사용합니다.");
                asset = CreateDefaultAsset();
            }
            map = asset.FindActionMap(MapName, true);
        }
        public void Enable() { map.Enable(); }
        public void Disable() { map.Disable(); }
        public void Dispose() { map.Disable(); if (asset == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(asset); else UnityEngine.Object.DestroyImmediate(asset); }
        private bool Pressed(string name) => map.FindAction(name, true).WasPressedThisFrame();
        /// <summary>이번 프레임에 눌린 입력 하나를 명령으로 반환한다. 우선순위: 재시작 → 범위 표시 → 되돌리기 → 대기 → 구르기 → 이동·대시. 방향은 위·아래·왼쪽·오른쪽 순.</summary>
        public BattleCommand Read()
        {
            if (Pressed(Restart)) return new BattleCommand(BattleCommandKind.Restart);
            if (Pressed(ToggleRanges)) return new BattleCommand(BattleCommandKind.ToggleRanges);
            if (Pressed(Undo)) return new BattleCommand(BattleCommandKind.Undo);
            if (Pressed(Wait)) return new BattleCommand(BattleCommandKind.Wait);
            var roll = Pressed(RollUpLeft) ? new Vector2Int(-1, 1) : Pressed(RollUpRight) ? new Vector2Int(1, 1) : Pressed(RollDownLeft) ? new Vector2Int(-1, -1) : Pressed(RollDownRight) ? new Vector2Int(1, -1) : Vector2Int.zero;
            if (roll != Vector2Int.zero) return new BattleCommand(BattleCommandKind.Roll, roll);
            var direction = Pressed(MoveUp) ? Vector2Int.up : Pressed(MoveDown) ? Vector2Int.down : Pressed(MoveLeft) ? Vector2Int.left : Pressed(MoveRight) ? Vector2Int.right : Vector2Int.zero;
            if (direction == Vector2Int.zero) return default;
            return new BattleCommand(map.FindAction(DashModifier, true).IsPressed() ? BattleCommandKind.Dash : BattleCommandKind.Move, direction);
        }
        /// <summary>안내 문구용 첫 번째 바인딩 표시(예: "W"). 바인딩이 없으면 "-".</summary>
        public string Key(string actionName)
        {
            var action = map.FindAction(actionName, true);
            return action.bindings.Count == 0 ? "-" : action.GetBindingDisplayString(0);
        }
    }
}
