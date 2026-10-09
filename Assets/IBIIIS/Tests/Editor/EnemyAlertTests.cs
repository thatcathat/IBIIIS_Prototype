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
        private Texture2D texture;
        private Camera camera;
        private GridMapPlayer player;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(7, 7);
            for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(3, 0));
            texture = new Texture2D(4, 4);
            settings = ScriptableObject.CreateInstance<EnemyAlertSettings>();
            var so = new SerializedObject(settings);
            so.FindProperty("alertSprite").objectReferenceValue = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, 0));
            so.FindProperty("lostSprite").objectReferenceValue = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, 0));
            so.ApplyModifiedPropertiesWithoutUndo();
            playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            so = new SerializedObject(playerSettings); so.FindProperty("enemyAlert").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            camera = Add(new GameObject("Camera")).AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(40, 0, 0);
        }
        [TearDown] public void Cleanup()
        {
            if (player != null) Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(map); Object.DestroyImmediate(playerSettings); Object.DestroyImmediate(settings); Object.DestroyImmediate(texture);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        private GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, scene); return go; }
        // 아래(-Y)를 보는 적: 바로 앞 두 칸을 인식하고, 조준만 하며 움직이거나 공격하지 않는다.
        private void Watcher(int x, int y)
        {
            var go = Add(new GameObject("Enemy")); var definition = go.AddComponent<EnemyDefinition>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false); visual.AddComponent<SpriteRenderer>();
            var so = new SerializedObject(definition);
            var actions = so.FindProperty("actions"); actions.arraySize = 1; actions.GetArrayElementAtIndex(0).FindPropertyRelative("type").enumValueIndex = (int)EnemyActionType.AimAtPlayer;
            var recognition = so.FindProperty("recognition"); recognition.arraySize = 2;
            recognition.GetArrayElementAtIndex(0).vector2IntValue = new Vector2Int(0, 1); recognition.GetArrayElementAtIndex(1).vector2IntValue = new Vector2Int(0, 2);
            so.FindProperty("attack").arraySize = 0; so.FindProperty("recognizedAttack").arraySize = 0;
            so.FindProperty("visual").objectReferenceValue = visual.transform;
            so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x, y), go, Vector2Int.down);
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
            player.UpdateMotion(.01f); Assert.AreEqual(0, player.Alert.ShownAlerts + player.Alert.ShownLost, "행동 중에는 숨김");
            for (int i = 0; i < 100 && (player.Session.IsBusy || player.IsPresenting); i++) { player.AdvanceMovement(.02f); player.UpdateMotion(.02f); }
            player.UpdateMotion(0);
        }
        [Test] public void AlertPopsAboveEnemyWhenPlayerStandsInRecognitionRange()
        {
            Watcher(3, 2); Build();
            Assert.True(player.Session.IsRecognizing(player.Session.Enemies[0]), "(3,0)은 적 앞 두 칸 안");
            Assert.AreEqual(1, player.Alert.ShownAlerts); Assert.AreEqual(1, player.Alert.AlertsPlayed, "전투 시작부터 인식 중이면 바로 알림");
            var mark = player.Alert.AlertMark(0);
            var expected = new Vector3(3, 0, 2) + camera.transform.up * settings.Height;
            Assert.That(Vector3.Distance(expected, mark.position), Is.LessThan(1e-4f), "적 발밑에서 화면 위쪽으로 그림 머리 위");
            Assert.AreEqual(0, mark.localScale.x, 1e-4f, "튀어나오기 시작");
            player.UpdateMotion(settings.PopTime / 2); Assert.AreEqual(settings.Size * settings.PopScale, mark.localScale.x, 1e-3f, "가장 크게");
            player.UpdateMotion(settings.PopTime); Assert.AreEqual(settings.Size, mark.localScale.x, 1e-4f, "원래 크기로 유지");
            Assert.That(Quaternion.Angle(camera.transform.rotation, mark.rotation), Is.LessThan(.01f), "카메라를 바라봄");
        }
        [Test] public void LeavingShowsQuestionThenFadesAndReenteringAlertsAgain()
        {
            Watcher(3, 2); Build(); player.UpdateMotion(1);
            Act(PlayerAction.Move, Vector2Int.left);
            Assert.AreEqual(0, player.Alert.ShownAlerts); Assert.AreEqual(1, player.Alert.ShownLost, "벗어나는 순간 ?");
            player.UpdateMotion(settings.LostTime + .01f);
            Assert.AreEqual(0, player.Alert.ShownLost, "?는 잠깐만");
            Act(PlayerAction.Move, Vector2Int.right);
            Assert.AreEqual(1, player.Alert.ShownAlerts); Assert.AreEqual(2, player.Alert.AlertsPlayed, "다시 들어오면 다시 알림·효과음");
            Act(PlayerAction.Wait, Vector2Int.zero);
            Assert.AreEqual(1, player.Alert.ShownAlerts); Assert.AreEqual(2, player.Alert.AlertsPlayed, "계속 인식 중이면 다시 튀어나오지 않음");
        }
        [Test] public void QuickNextActionDoesNotReplayQuestionMark()
        {
            Watcher(3, 2); Build(); player.UpdateMotion(1);
            Act(PlayerAction.Move, Vector2Int.left); Assert.AreEqual(1, player.Alert.ShownLost);
            Act(PlayerAction.Wait, Vector2Int.zero); // ? 시간이 끝나기 전에 다음 행동
            Assert.AreEqual(0, player.Alert.ShownLost, "행동이 끝난 뒤 남은 ?를 다시 보이지 않음");
        }
        [Test] public void QuickNextActionDoesNotResumeThePop()
        {
            Watcher(3, 2); map.SetStart(new Vector2Int(2, 0)); Build();
            Act(PlayerAction.Move, Vector2Int.right); Assert.AreEqual(1, player.Alert.ShownAlerts);
            Act(PlayerAction.Wait, Vector2Int.zero); // 튀어나옴이 끝나기 전에 다음 행동
            Assert.AreEqual(settings.Size, player.Alert.AlertMark(0).localScale.x, 1e-4f, "튀어나옴을 이어 재생하지 않고 원래 크기");
            Assert.AreEqual(1, player.Alert.AlertsPlayed);
        }
        [Test] public void UndoMatchesStateWithoutPopOrSound()
        {
            Watcher(3, 2); Build(); player.UpdateMotion(1);
            Act(PlayerAction.Move, Vector2Int.left);
            Assert.True(player.TryUndo());
            Assert.AreEqual(1, player.Alert.ShownAlerts); Assert.AreEqual(0, player.Alert.ShownLost);
            Assert.AreEqual(settings.Size, player.Alert.AlertMark(0).localScale.x, 1e-4f, "튀어나오지 않고 바로 원래 크기");
            Assert.AreEqual(1, player.Alert.AlertsPlayed, "효과음 없음");
        }
        [Test] public void NothingShowsWhenOffOrOutOfRange()
        {
            Watcher(0, 6); Build();
            Assert.AreEqual(0, player.Alert.ShownAlerts, "범위 밖");
            Object.DestroyImmediate(player.gameObject); player = null;
            var so = new SerializedObject(settings); so.FindProperty("enabledAlert").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            map.RemoveEnemy(new Vector2Int(0, 6)); Watcher(3, 2); Build();
            Assert.AreEqual(0, player.Alert.ShownAlerts, "끄면 표시 안 함");
        }
    }
}
