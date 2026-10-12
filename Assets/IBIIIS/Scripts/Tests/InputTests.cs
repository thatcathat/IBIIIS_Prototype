using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace IBIIIS.Tests
{
    public sealed class InputTests : InputTestFixture
    {
        private Keyboard keyboard;
        private BattleInput input;
        public override void Setup() { base.Setup(); keyboard = InputSystem.AddDevice<Keyboard>(); input = new BattleInput(); input.Enable(); }
        public override void TearDown() { input.Dispose(); base.TearDown(); }
        private BattleCommand Tap(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update();
            var command = input.Read();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            return command;
        }
        [Test] public void DefaultKeysMapToCommands()
        {
            Assert.AreEqual(Vector2Int.up, Tap(Key.W).Direction); Assert.AreEqual(Vector2Int.up, Tap(Key.UpArrow).Direction);
            Assert.AreEqual(Vector2Int.down, Tap(Key.S).Direction); Assert.AreEqual(Vector2Int.left, Tap(Key.A).Direction); Assert.AreEqual(Vector2Int.right, Tap(Key.RightArrow).Direction);
            Assert.AreEqual(BattleCommandKind.Move, Tap(Key.D).Kind);
            var dash = Tap(Key.LeftShift, Key.A); Assert.AreEqual(BattleCommandKind.Dash, dash.Kind); Assert.AreEqual(Vector2Int.left, dash.Direction);
            Assert.AreEqual(BattleCommandKind.Dash, Tap(Key.RightShift, Key.W).Kind);
            Assert.AreEqual(new Vector2Int(-1, 1), Tap(Key.Q).Direction); Assert.AreEqual(new Vector2Int(1, 1), Tap(Key.E).Direction);
            Assert.AreEqual(new Vector2Int(-1, -1), Tap(Key.Z).Direction); var roll = Tap(Key.C); Assert.AreEqual(BattleCommandKind.Roll, roll.Kind); Assert.AreEqual(new Vector2Int(1, -1), roll.Direction);
            Assert.AreEqual(BattleCommandKind.Wait, Tap(Key.Space).Kind); Assert.AreEqual(BattleCommandKind.Undo, Tap(Key.Backspace).Kind); Assert.AreEqual(BattleCommandKind.Undo, Tap(Key.U).Kind);
            Assert.AreEqual(BattleCommandKind.Restart, Tap(Key.R).Kind); Assert.AreEqual(BattleCommandKind.ToggleRanges, Tap(Key.Tab).Kind);
            Assert.AreEqual(BattleCommandKind.None, Tap().Kind);
        }
        [Test] public void SimultaneousKeysFollowTheDocumentedPriority()
        {
            Assert.AreEqual(Vector2Int.up, Tap(Key.D, Key.S, Key.W).Direction);
            Assert.AreEqual(Vector2Int.down, Tap(Key.D, Key.S).Direction);
            Assert.AreEqual(BattleCommandKind.Roll, Tap(Key.W, Key.E).Kind);
            Assert.AreEqual(BattleCommandKind.Wait, Tap(Key.Q, Key.Space).Kind);
            Assert.AreEqual(BattleCommandKind.Undo, Tap(Key.Space, Key.Backspace).Kind);
            Assert.AreEqual(BattleCommandKind.Restart, Tap(Key.Tab, Key.R).Kind);
        }
        [Test] public void HeldKeyIsOnlyACommandOnTheFrameItIsPressed()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
            Assert.AreEqual(BattleCommandKind.Move, input.Read().Kind);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
            Assert.AreEqual(BattleCommandKind.None, input.Read().Kind);
        }
        [Test] public void RebindingTheAssetChangesTheKeyAndShowsInHints()
        {
            var source = BattleInput.CreateDefaultAsset();
            try
            {
                source.FindAction(BattleInput.MapName + "/" + BattleInput.Wait, true).ChangeBinding(0).WithPath("<Keyboard>/x");
                Assert.Null(BattleInput.FindMissingAction(source));
                input.Dispose(); input = new BattleInput(source); input.Enable();
                Assert.AreEqual(BattleCommandKind.Wait, Tap(Key.X).Kind); Assert.AreEqual(BattleCommandKind.None, Tap(Key.Space).Kind);
                Assert.AreEqual("X", input.Key(BattleInput.Wait)); Assert.AreEqual("W", input.Key(BattleInput.MoveUp));
            }
            finally { Object.DestroyImmediate(source); }
        }
        [Test] public void SavedAssetJsonKeepsAllActionsAndIncompleteAssetFallsBack()
        {
            var created = BattleInput.CreateDefaultAsset(); var loaded = InputActionAsset.FromJson(created.ToJson());
            try
            {
                Assert.Null(BattleInput.FindMissingAction(loaded));
                Assert.AreEqual(created.FindActionMap(BattleInput.MapName).actions.Count, loaded.FindActionMap(BattleInput.MapName).actions.Count);
                loaded.FindActionMap(BattleInput.MapName).FindAction(BattleInput.Undo).RemoveAction();
                Assert.AreEqual("액션 'Undo'", BattleInput.FindMissingAction(loaded));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("기본 키 배치"));
                input.Dispose(); input = new BattleInput(loaded); input.Enable();
                Assert.AreEqual(BattleCommandKind.Undo, Tap(Key.U).Kind);
            }
            finally { Object.DestroyImmediate(created); Object.DestroyImmediate(loaded); }
        }
    }
}
