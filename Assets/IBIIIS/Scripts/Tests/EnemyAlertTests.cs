using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class EnemyAlertTests
    {
        private Scene scene;
        private GridMap map;
        private PlayerSettings playerSettings;
        private EnemyAlertSettings settings;
        private BattleDialogue common;
        private Camera camera;
        private GridMapPlayer player;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(7, 7);
            for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(3, 0));
            common = ScriptableObject.CreateInstance<BattleDialogue>();
            common.Set(new[] { new DialogueExchange { player = "들켰다!", enemy = "거기 서!" } }, new[] { "어디 갔지?" }, true);
            settings = ScriptableObject.CreateInstance<EnemyAlertSettings>();
            var so = new SerializedObject(settings); so.FindProperty("dialogue").objectReferenceValue = common; so.ApplyModifiedPropertiesWithoutUndo();
            playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            so = new SerializedObject(playerSettings); so.FindProperty("enemyAlert").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            camera = Add(new GameObject("Camera")).AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(40, 0, 0);
        }
        [TearDown] public void Cleanup()
        {
            if (player != null) Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(map); Object.DestroyImmediate(playerSettings); Object.DestroyImmediate(settings); Object.DestroyImmediate(common);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        private GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, scene); return go; }
        // 아래(-Y)를 보는 적: 바로 앞 두 칸을 인식하고, 조준만 하며 움직이거나 공격하지 않는다.
        private EnemyDefinition Watcher(int x, int y, BattleDialogue own = null)
        {
            var go = Add(new GameObject("Enemy")); var definition = go.AddComponent<EnemyDefinition>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false); visual.AddComponent<SpriteRenderer>();
            var so = new SerializedObject(definition);
            var actions = so.FindProperty("actions"); actions.arraySize = 1; actions.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)EnemyActionType.AimAtPlayer;
            var recognition = so.FindProperty("recognition"); recognition.arraySize = 2;
            recognition.GetArrayElementAtIndex(0).vector2IntValue = new Vector2Int(0, 1); recognition.GetArrayElementAtIndex(1).vector2IntValue = new Vector2Int(0, 2);
            so.FindProperty("attack").arraySize = 0; so.FindProperty("recognizedAttack").arraySize = 0;
            so.FindProperty("visual").objectReferenceValue = visual.transform; so.FindProperty("dialogue").objectReferenceValue = own;
            so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x, y), go, Vector2Int.down);
            return definition;
        }
        private void Build()
        {
            var go = Add(new GameObject("Player")); player = go.AddComponent<GridMapPlayer>(); player.Configure(map, null, camera);
            var so = new SerializedObject(player); so.FindProperty("playerSettings").objectReferenceValue = playerSettings; so.ApplyModifiedPropertiesWithoutUndo();
            player.Build();
        }
        private void Act(PlayerAction action, Vector2Int direction)
        {
            Assert.True(player.TryBeginAction(action, direction));
            player.UpdateMotion(.01f); Assert.AreEqual(0, player.Alert.Bubbles.Count, "행동 중에는 말풍선 없음");
            for (int i = 0; i < 100 && (player.Session.IsBusy || player.IsPresenting); i++) { player.AdvanceMovement(.02f); player.UpdateMotion(.02f); }
            player.UpdateMotion(0);
        }
        private SpeechBubble Of(int speaker) => player.Alert.Bubbles.FirstOrDefault(b => b.Speaker == speaker);

        [Test] public void SpottingShowsThePlayerLineThenTheEnemyReply()
        {
            Watcher(3, 2); Build();
            var mine = Of(SpeechBubble.Player); var reply = Of(0);
            Assert.AreEqual("들켰다!", mine.Text); Assert.IsTrue(mine.Shown, "전투 시작부터 인식 중이면 바로 대화");
            Assert.AreEqual("거기 서!", reply.Text); Assert.IsFalse(reply.Shown, "적은 잠시 뒤 대답");
            Assert.AreEqual(1, player.Alert.ExchangesPlayed, "효과음 한 번");
            player.UpdateMotion(settings.ReplyDelay + .01f); Assert.IsTrue(Of(0).Shown, "대답이 뜸");
            player.UpdateMotion(settings.SpottedTime + settings.ReplyDelay); Assert.AreEqual(0, player.Alert.Bubbles.Count, "시간이 지나면 사라짐");
            Act(PlayerAction.Wait, Vector2Int.zero); Assert.AreEqual(0, player.Alert.Bubbles.Count, "계속 인식 중이면 다시 말하지 않음"); Assert.AreEqual(1, player.Alert.ExchangesPlayed);
        }
        [Test] public void EveryNewlySpottingEnemyRepliesToOnePlayerLine()
        {
            map.SetStart(new Vector2Int(3, 3)); Watcher(3, 4); Watcher(3, 5); Build();
            Assert.AreEqual(1, player.Alert.Bubbles.Count(b => b.Speaker == SpeechBubble.Player), "플레이어 말은 한 번");
            Assert.AreEqual("거기 서!", Of(0).Text); Assert.AreEqual("거기 서!", Of(1).Text, "인식한 적 모두 대답");
        }
        [Test] public void LeavingMakesTheEnemySayALostLine()
        {
            Watcher(3, 2); Build(); player.UpdateMotion(5);
            Act(PlayerAction.Move, Vector2Int.left);
            Assert.AreEqual("어디 갔지?", Of(0).Text); Assert.IsTrue(Of(0).Shown); Assert.IsNull(Of(SpeechBubble.Player), "놓칠 때는 적만 말함");
            Act(PlayerAction.Wait, Vector2Int.zero); Assert.AreEqual(0, player.Alert.Bubbles.Count, "다음 행동 뒤 남은 말풍선을 다시 띄우지 않음");
            Act(PlayerAction.Move, Vector2Int.right); Assert.AreEqual(2, player.Alert.ExchangesPlayed, "다시 들어오면 다시 대화");
        }
        [Test] public void EnemyOwnDialogueOverridesTheCommonLines()
        {
            var own = ScriptableObject.CreateInstance<BattleDialogue>();
            try
            {
                own.Set(new[] { new DialogueExchange { player = "무시됨", enemy = "내 구역이다" } }, new string[0], true);
                map.SetStart(new Vector2Int(3, 3)); Watcher(3, 4); Watcher(3, 5, own); Build();
                Assert.AreEqual("들켰다!", Of(SpeechBubble.Player).Text, "플레이어 말은 첫 적의 목록에서");
                Assert.AreEqual("거기 서!", Of(0).Text); Assert.AreEqual("내 구역이다", Of(1).Text, "다른 목록을 쓰는 적은 자기 대답");
            }
            finally { Object.DestroyImmediate(own); }
        }
        [Test] public void NoImmediateRepeatAvoidsTheLastLine()
        {
            common.Set(new[] { new DialogueExchange { player = "A", enemy = "a" }, new DialogueExchange { player = "B", enemy = "b" } }, new string[0], true);
            Watcher(3, 2); Build();
            for (int round = 0; round < 6; round++)
            {
                var before = Of(SpeechBubble.Player).Text;
                Act(PlayerAction.Move, Vector2Int.left); Act(PlayerAction.Move, Vector2Int.right);
                var now = Of(SpeechBubble.Player).Text;
                Assert.AreNotEqual(before, now, "체크하면 연달아 같은 대사가 나오지 않음");
                Assert.AreEqual(now.ToLower(), Of(0).Text, "대답은 고른 쌍과 짝");
            }
        }
        [Test] public void BubbleRisesWhileWobblingAroundTheTailThenSettles()
        {
            var start = BubbleIntro.At(0, .35f, 20, 10, 2.5f, .8f);
            Assert.AreEqual(20, start.Rise, 1e-4f, "아래에서 시작"); Assert.AreEqual(.8f, start.Scale, 1e-4f); Assert.AreEqual(0, start.Alpha, 1e-4f, "투명하게 나타남");
            float maxAngle = 0; bool left = false, right = false; float lastRise = start.Rise;
            for (int i = 1; i < 35; i++)
            {
                var pose = BubbleIntro.At(i * .01f, .35f, 20, 10, 2.5f, .8f);
                Assert.LessOrEqual(pose.Rise, lastRise + 1e-5f, "계속 위로 떠오름"); lastRise = pose.Rise;
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(pose.Angle)); left |= pose.Angle > 1; right |= pose.Angle < -1;
            }
            Assert.IsTrue(left && right, "좌우로 기울며 흔들림"); Assert.LessOrEqual(maxAngle, 10 + 1e-4f);
            Assert.AreEqual(10, start.Angle, 1e-4f, "처음에 가장 크게 기움");
            // 좌우 한 번씩 꺾일 때마다(흔들림의 끝점) 폭이 줄어든다.
            float lastPeak = float.MaxValue; int peaks = 0; float prev = start.Angle, prevSlope = 0;
            for (int i = 1; i <= 350; i++)
            {
                float angle = BubbleIntro.At(i * .001f, .35f, 20, 10, 2.5f, .8f).Angle, slope = angle - prev;
                if (prevSlope != 0 && Mathf.Sign(slope) != Mathf.Sign(prevSlope)) { Assert.Less(Mathf.Abs(prev), lastPeak, "뒤로 갈수록 작게 흔들림"); lastPeak = Mathf.Abs(prev); peaks++; }
                prev = angle; prevSlope = slope;
            }
            Assert.GreaterOrEqual(peaks, 3, "여러 번 흔들림");
            var end = BubbleIntro.At(.35f, .35f, 20, 10, 2.5f, .8f);
            Assert.AreEqual(0, end.Rise, 1e-4f); Assert.AreEqual(0, end.Angle, 1e-4f, "도착하면 멈춤"); Assert.AreEqual(1, end.Scale, 1e-4f); Assert.AreEqual(1, end.Alpha, 1e-4f);
        }
        [Test] public void UndoMatchesStateWithoutBubblesOrSound()
        {
            Watcher(3, 2); Build(); player.UpdateMotion(5);
            Act(PlayerAction.Move, Vector2Int.left);
            Assert.True(player.TryUndo());
            Assert.AreEqual(0, player.Alert.Bubbles.Count); Assert.AreEqual(1, player.Alert.ExchangesPlayed, "효과음 없음");
            player.UpdateMotion(.1f); Assert.AreEqual(0, player.Alert.Bubbles.Count, "되돌린 뒤 인식 중이어도 다시 말하지 않음");
        }
        [Test] public void NothingWhenOffOutOfRangeOrLinesEmpty()
        {
            Watcher(0, 6); Build(); Assert.AreEqual(0, player.Alert.Bubbles.Count, "범위 밖");
            Object.DestroyImmediate(player.gameObject); player = null; map.RemoveEnemy(new Vector2Int(0, 6));
            var so = new SerializedObject(settings); so.FindProperty("enabledAlert").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            Watcher(3, 2); Build(); Assert.AreEqual(0, player.Alert.Bubbles.Count, "끄면 표시 안 함");
            Object.DestroyImmediate(player.gameObject); player = null;
            so.FindProperty("enabledAlert").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
            common.Set(new DialogueExchange[0], new string[0], true); Build();
            Assert.AreEqual(0, player.Alert.Bubbles.Count, "대사가 없으면 말풍선 없음"); Assert.AreEqual(1, player.Alert.ExchangesPlayed, "효과음은 냄");
        }
    }
}
