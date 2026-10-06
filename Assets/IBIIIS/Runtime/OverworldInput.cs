using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IBIIIS
{
    /// <summary>미니맵(Overworld) 입력. 전투 입력(BattleInput)과 별도의 액션 맵을 쓴다. 에셋이 없거나 필수 액션이 빠지면 코드의 기본 배치를 쓴다.
    /// 에셋은 사본을 만들어 쓰므로 원본의 활성 상태를 건드리지 않는다.</summary>
    public sealed class OverworldInput : IDisposable
    {
        public const string MapName = "Overworld";
        public const string Move = "Move", Interact = "Interact";
        private static readonly string[] Required = { Move, Interact };
        private readonly InputActionAsset asset;
        private readonly InputActionMap map;
        /// <summary>기본 키 배치: WASD·방향키 자유 이동, F 상호작용. 에디터의 `IBIIIS > Overworld > Create Overworld Test Scene`이 같은 내용을 파일로 저장한다.</summary>
        public static InputActionAsset CreateDefaultAsset()
        {
            var created = ScriptableObject.CreateInstance<InputActionAsset>(); created.name = "OverworldInput";
            var overworld = created.AddActionMap(MapName);
            var move = overworld.AddAction(Move, InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            overworld.AddAction(Interact, InputActionType.Button, "<Keyboard>/f");
            return created;
        }
        /// <summary>필수 액션이 없는 에셋이면 누락된 이름을 알려 준다. 모두 있으면 null.</summary>
        public static string FindMissingAction(InputActionAsset source)
        {
            var overworld = source != null ? source.FindActionMap(MapName) : null;
            if (overworld == null) return $"액션 맵 '{MapName}'";
            foreach (var name in Required) if (overworld.FindAction(name) == null) return $"액션 '{name}'";
            return null;
        }
        public OverworldInput(InputActionAsset source = null)
        {
            if (source != null && FindMissingAction(source) == null) asset = UnityEngine.Object.Instantiate(source);
            else
            {
                if (source != null) Debug.LogWarning($"[IBIIIS] 미니맵 입력 에셋 '{source.name}'에 {FindMissingAction(source)}이(가) 없어 기본 키 배치를 사용합니다.");
                asset = CreateDefaultAsset();
            }
            map = asset.FindActionMap(MapName, true);
        }
        public void Enable() { map.Enable(); }
        public void Disable() { map.Disable(); }
        public void Dispose() { map.Disable(); if (asset == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(asset); else UnityEngine.Object.DestroyImmediate(asset); }
        /// <summary>이동 입력. 화면 기준 x=오른쪽, y=위(맵 +Z). 대각선은 길이 1로 정규화된다.</summary>
        public Vector2 ReadMove() => Vector2.ClampMagnitude(map.FindAction(Move, true).ReadValue<Vector2>(), 1);
        public bool InteractPressed() => map.FindAction(Interact, true).WasPressedThisFrame();
        /// <summary>안내 문구용 첫 번째 바인딩 표시(예: "F").</summary>
        public string Key(string actionName)
        {
            var action = map.FindAction(actionName, true);
            return action.bindings.Count == 0 ? "-" : action.GetBindingDisplayString(0);
        }
    }
}
