using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Tests
{
    public sealed class PlayerVisualTests
    {
        private GameObject root;
        private PlayerVisual visual;
        private Sprite Make(string name) { var s = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero); s.name = name; return s; }
        private static void Set(SerializedProperty p, string f, Sprite s) => p.FindPropertyRelative(f).objectReferenceValue = s;
        [SetUp] public void SetUp()
        {
            root = new GameObject("visual test"); var child = new GameObject("Sprite"); child.transform.SetParent(root.transform);
            visual = root.AddComponent<PlayerVisual>(); var so = new SerializedObject(visual);
            so.FindProperty("spriteRenderer").objectReferenceValue = child.AddComponent<SpriteRenderer>();
            foreach (var group in new[] { "idle", "move", "dash" })
                foreach (var face in new[] { "back", "right", "front", "left" }) Set(so.FindProperty(group), face, Make(group + "-" + face));
            foreach (var roll in new[] { "rollBackLeft", "rollBackRight", "rollFrontLeft", "rollFrontRight" })
            { Set(so.FindProperty(roll), "first", Make(roll + "-1")); Set(so.FindProperty(roll), "second", Make(roll + "-2")); }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(root); }
        private string Shown => visual.Renderer.sprite.name;
        [Test] public void MoveAndIdleFollowGridDirectionAsScreenFacing()
        {
            visual.Show(PlayerAction.Move, Vector2Int.up, .3f, true, null); Assert.AreEqual("move-back", Shown);
            visual.Show(PlayerAction.Move, Vector2Int.up, 1, false, null); Assert.AreEqual("idle-back", Shown);
            visual.Show(PlayerAction.Move, Vector2Int.left, .3f, true, null); Assert.AreEqual("move-left", Shown);
            visual.Show(PlayerAction.Move, Vector2Int.down, .3f, true, null); Assert.AreEqual("move-front", Shown);
            visual.Show(PlayerAction.Wait, Vector2Int.zero, 0, false, null); Assert.AreEqual("idle-front", Shown, "대기는 마지막 방향을 유지");
        }
        [Test] public void DashUsesDashSprites()
        {
            visual.Show(PlayerAction.Dash, Vector2Int.right, .3f, true, null); Assert.AreEqual("dash-right", Shown);
        }
        [Test] public void RollShowsFirstThenSecondFrameAndFacesHorizontally()
        {
            visual.Show(PlayerAction.Roll, new Vector2Int(-1, -1), .49f, true, null); Assert.AreEqual("rollFrontLeft-1", Shown);
            visual.Show(PlayerAction.Roll, new Vector2Int(-1, -1), .5f, true, null); Assert.AreEqual("rollFrontLeft-2", Shown);
            visual.Show(PlayerAction.Roll, new Vector2Int(1, 1), .1f, true, null); Assert.AreEqual("rollBackRight-1", Shown);
            visual.Show(PlayerAction.Roll, new Vector2Int(1, 1), 1, false, null); Assert.AreEqual("idle-right", Shown);
        }
        [Test] public void EmptySlotKeepsPreviousSpriteWithoutThrowing()
        {
            visual.Show(PlayerAction.Move, Vector2Int.up, .3f, true, null);
            var so = new SerializedObject(visual); so.FindProperty("move").FindPropertyRelative("right").objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Move Right"));
            visual.Show(PlayerAction.Move, Vector2Int.right, .3f, true, null); Assert.AreEqual("move-back", Shown);
        }
        [Test] public void SpriteFacesCamera()
        {
            var cam = new GameObject("cam"); cam.transform.rotation = Quaternion.Euler(50, 0, 0);
            try { visual.Show(PlayerAction.Wait, Vector2Int.zero, 0, false, cam.AddComponent<Camera>()); Assert.AreEqual(cam.transform.rotation, visual.Renderer.transform.rotation); }
            finally { Object.DestroyImmediate(cam); }
        }
    }
}
