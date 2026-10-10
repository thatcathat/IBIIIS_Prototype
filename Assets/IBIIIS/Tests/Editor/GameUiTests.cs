using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using IBIIIS.Editor;

namespace IBIIIS.Tests
{
    public sealed class GameUiTests
    {
        private Scene scene;
        private GridMap map;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(7, 3);
            for (int y = 0; y < 3; y++) for (int x = 0; x < 7; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(3, 0));
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(map); EditorSceneManager.ClosePreviewScene(scene); }
        private static VisualElement Clone(string path)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path); Assert.IsNotNull(tree, path);
            return tree.CloneTree();
        }
        private static BattleHudState Ready(int cooldown = 0, bool canUndo = true) => new BattleHudState
        { StageName = "1-1", AliveEnemies = 2, TotalEnemies = 3, Actions = 4, EvasionCooldown = cooldown, Ready = true, CanUndo = canUndo, RangesOn = true };

        [Test] public void HudShowsCountsAndGraysEvasionWithTurnsLeft()
        {
            var root = Clone(GameUiSetup.BattleHudPath);
            var hud = new BattleHud(root, "이동 WASD", "Backspace", "R", "Tab", null, false, null, null, null);
            hud.Refresh(Ready());
            Assert.AreEqual("1-1", root.Q<Label>("stage-name").text);
            Assert.AreEqual("남은 적 2/3", root.Q<Label>("enemies").text); Assert.AreEqual("행동 4", root.Q<Label>("actions").text);
            Assert.AreEqual("이동 WASD", root.Q<Label>("controls").text);
            Assert.IsFalse(root.Q("evasion-icon").ClassListContains("locked")); Assert.AreEqual(DisplayStyle.None, root.Q<Label>("evasion-cooldown").style.display.value);
            hud.Refresh(Ready(cooldown: 1));
            Assert.IsTrue(root.Q("evasion-icon").ClassListContains("locked"), "쿨타임 중 회색");
            Assert.AreEqual("1", root.Q<Label>("evasion-cooldown").text, "남은 턴 수");
            Assert.AreEqual("되돌리기 [Backspace]", root.Q<Button>("undo").text); Assert.AreEqual("범위 표시 켬 [Tab]", root.Q<Button>("ranges").text);
        }
        [Test] public void ButtonsRunTheSameCommandsOnlyWhenReady()
        {
            int undo = 0, restart = 0, ranges = 0;
            var root = Clone(GameUiSetup.BattleHudPath);
            var hud = new BattleHud(root, "", "", "", "", null, false, () => undo++, () => restart++, () => ranges++);
            hud.Refresh(Ready(canUndo: false));
            hud.Press("undo"); hud.Press("restart"); hud.Press("ranges");
            Assert.AreEqual(0, undo, "되돌릴 행동이 없으면 무시"); Assert.AreEqual(1, restart); Assert.AreEqual(1, ranges);
            Assert.IsFalse(root.Q<Button>("undo").enabledSelf);
            var busy = Ready(); busy.Ready = false; hud.Refresh(busy);
            hud.Press("undo"); hud.Press("restart"); hud.Press("ranges");
            Assert.AreEqual(0, undo); Assert.AreEqual(1, restart); Assert.AreEqual(1, ranges); Assert.IsFalse(root.Q<Button>("restart").enabledSelf, "행동 중에는 누를 수 없음");
            hud.Refresh(Ready()); hud.Press("undo"); Assert.AreEqual(1, undo);
        }
        [Test] public void MissingElementsAreSkippedWithAWarning()
        {
            var root = new VisualElement(); root.Add(new Label { name = "enemies" });
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("stage-name"));
            LogAssert.ignoreFailingMessages = true; // 빠진 요소마다 경고가 나온다
            var hud = new BattleHud(root, "", "", "", "", null, false, null, null, null);
            hud.Refresh(Ready()); Assert.AreEqual("남은 적 2/3", root.Q<Label>("enemies").text);
        }
        [Test] public void PlayerHudStateFollowsActionsUndoAndEvasionCooldown()
        {
            var go = new GameObject("Player"); SceneManager.MoveGameObjectToScene(go, scene);
            var player = go.AddComponent<GridMapPlayer>(); player.Configure(map, null, null); player.Build();
            var s = player.HudState();
            Assert.AreEqual(map.name, s.StageName, "미니맵 없이 들어오면 맵 이름"); Assert.AreEqual(0, s.Actions); Assert.IsTrue(s.Ready); Assert.IsFalse(s.CanUndo); Assert.AreEqual(0, s.EvasionCooldown);
            Assert.True(player.TryBeginAction(PlayerAction.Dash, Vector2Int.up)); Assert.IsFalse(player.HudState().Ready, "행동 중");
            player.AdvanceMovement(2);
            s = player.HudState(); Assert.AreEqual(1, s.Actions); Assert.AreEqual(1, s.EvasionCooldown, "회피 뒤 일반 행동 1회 남음"); Assert.IsTrue(s.CanUndo);
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(2);
            Assert.AreEqual(0, player.HudState().EvasionCooldown); Assert.AreEqual(2, player.HudState().Actions);
            Assert.True(player.TryUndo()); Assert.AreEqual(1, player.HudState().EvasionCooldown); Assert.AreEqual(1, player.HudState().Actions);
            Assert.IsNull(player.Hud, "Play가 아니면 HUD 화면을 만들지 않음");
        }
        [Test] public void PopupAndOverworldScreensHaveTheNamedElements()
        {
            var popup = Clone(GameUiSetup.ResultPopupPath);
            Assert.IsNotNull(popup.Q<Label>("title")); Assert.IsNotNull(popup.Q<Label>("message")); Assert.IsNotNull(popup.Q<Button>("confirm"));
            var overworld = Clone(GameUiSetup.OverworldHudPath);
            Assert.IsNotNull(overworld.Q<Label>("hint")); Assert.IsNotNull(overworld.Q<Label>("prompt"));
            var settings = AssetDatabase.LoadAssetAtPath<GameUiSettings>(AssetPaths.GameUiSettings);
            Assert.IsNotNull(settings, "기본 Game UI 설정"); Assert.IsNotNull(settings.PanelSettings); Assert.IsNotNull(settings.PanelSettings.themeStyleSheet);
        }
    }
}
