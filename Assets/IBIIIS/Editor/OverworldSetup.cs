using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IBIIIS.Editor
{
    /// <summary>미니맵(Overworld) 테스트용 설정·입력·플레이어 프리팹·테스트 씬을 만든다. 이미 있는 에셋과 연결은 덮어쓰지 않는다.</summary>
    public static class OverworldSetup
    {
        [MenuItem("IBIIIS/Overworld/Create Overworld Test Scene")]
        public static void CreateTestSceneMenu()
        {
            try
            {
                var path = CreateTestScene(NextScenePath());
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path); EditorGUIUtility.PingObject(Selection.activeObject);
                Debug.Log($"[IBIIIS] 미니맵 테스트 씬 저장: {path}. 이 씬을 열고 Play하세요.");
            }
            catch (Exception e) { EditorUtility.DisplayDialog("미니맵 테스트 씬", e.Message, "확인"); }
        }
        /// <summary>겹치지 않는 테스트 씬 경로. 폴더가 없으면 GenerateUniqueAssetPath가 빈 문자열을 돌려주므로 폴더를 먼저 만든다.</summary>
        public static string NextScenePath()
        {
            AssetPaths.EnsureFolder(AssetPaths.Overworld); // AssetPaths의 폴더 구조가 바뀌어도 중간 폴더까지 만든다
            var path = AssetDatabase.GenerateUniqueAssetPath(AssetPaths.OverworldScene);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException($"테스트 씬 경로를 만들지 못했습니다: {AssetPaths.OverworldScene}");
            return path;
        }
        [MenuItem("IBIIIS/Overworld/Overworld Settings")]
        public static void OpenSettings() { Selection.activeObject = EnsureSettings(); EditorGUIUtility.PingObject(Selection.activeObject); }
        [MenuItem("IBIIIS/Overworld/Reset Clear Records")]
        public static void ResetClearRecords()
        {
            if (!EditorUtility.DisplayDialog("클리어 기록 초기화", $"모든 스테이지 클리어 기록을 지웁니다.\n{ProgressStore.FilePath}", "지우기", "취소")) return;
            ProgressStore.ResetAll();
            foreach (var entrance in Object.FindObjectsByType<StageEntrance>(FindObjectsInactive.Include, FindObjectsSortMode.None)) entrance.RefreshIndicator();
            Debug.Log("[IBIIIS] 클리어 기록을 초기화했습니다.");
        }
        [MenuItem("IBIIIS/Overworld/Show Save File")]
        public static void ShowSaveFile()
        {
            var path = ProgressStore.FilePath;
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)); EditorUtility.RevealInFinder(Path.GetDirectoryName(path)); Debug.Log($"[IBIIIS] 아직 클리어 기록 파일이 없습니다: {path}"); }
        }
        /// <summary>미니맵 설정 에셋. 새로 만들 때와 fillEmptySlots(테스트 씬 생성)일 때만 빈 슬롯에 기본 카메라·입력·손맛 설정을 연결한다.
        /// 설정 메뉴로 열기만 할 때는 사용자가 일부러 비운 슬롯을 다시 채우지 않는다. 입력 파일에 빠진 액션 보충은 항상 한다.</summary>
        public static OverworldSettings EnsureSettings(bool fillEmptySlots = false)
        {
            var settings = AssetDatabase.LoadAssetAtPath<OverworldSettings>(AssetPaths.OverworldSettings);
            bool created = settings == null;
            if (created)
            {
                AssetPaths.EnsureFolder(AssetPaths.Settings);
                settings = ScriptableObject.CreateInstance<OverworldSettings>();
                AssetDatabase.CreateAsset(settings, AssetPaths.OverworldSettings);
            }
            if (created || fillEmptySlots)
            {
                var so = new SerializedObject(settings);
                Fill(so, "cameraSettings", EnsureCameraSettings);
                Fill(so, "inputActions", EnsureInput);
                Fill(so, "motionFeedback", () => AssetDatabase.LoadAssetAtPath<MotionFeedbackSettings>(AssetPaths.MotionFeedback));
                Fill(so, "gameUi", GameUiSetup.EnsureDefault);
            }
            if (AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.OverworldInput) != null) AddMissingInputActions();
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }
        /// <summary>미니맵 전용 카메라 설정. 처음 만들 때 전투 공용 설정의 각도를 따르고 화각만 30으로 좁혀 가장자리 왜곡을 줄인다. 이미 있으면 그대로 쓴다.</summary>
        public static MapCameraSettings EnsureCameraSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MapCameraSettings>(AssetPaths.OverworldCameraSettings);
            if (existing != null) return existing;
            var created = Object.Instantiate(MapEditorSetup.EnsureCameraSettings());
            var so = new SerializedObject(created); so.FindProperty("fieldOfView").floatValue = 30; so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(created, AssetPaths.OverworldCameraSettings); AssetDatabase.SaveAssetIfDirty(created);
            return created;
        }
        public static InputActionAsset EnsureInput()
        {
            var existing = InputSetup.LoadOrCreate(AssetPaths.OverworldInput, OverworldInput.CreateDefaultAsset);
            if (existing == null) return null;
            AddMissingInputActions(); return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.OverworldInput);
        }
        /// <summary>이전에 만든 입력 파일에 대시·구르기처럼 나중에 추가된 액션이 없으면 기본 키로 추가한다. 다른 액션·바인딩은 그대로 둔다.</summary>
        public static bool AddMissingInputActions()
        {
            if (!File.Exists(AssetPaths.OverworldInput)) return false;
            var asset = InputActionAsset.FromJson(File.ReadAllText(AssetPaths.OverworldInput));
            try
            {
                var map = asset.FindActionMap(OverworldInput.MapName);
                if (map == null || !OverworldInput.AddMissingOptional(map)) return false;
                File.WriteAllText(AssetPaths.OverworldInput, asset.ToJson());
                AssetDatabase.ImportAsset(AssetPaths.OverworldInput, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[IBIIIS] {AssetPaths.OverworldInput}에 빠진 미니맵 액션(대시·구르기)을 기본 키로 추가했습니다.");
                return true;
            }
            finally { Object.DestroyImmediate(asset); }
        }
        /// <summary>미니맵 플레이어 프리팹: CharacterController + OverworldPlayer, 자식에 공용 플레이어 외형(PlayerVisual) 중첩 프리팹.</summary>
        public static GameObject EnsurePlayerPrefab(OverworldSettings settings)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPaths.OverworldPlayer);
            if (existing != null) return existing;
            var playerSettings = AssetDatabase.LoadAssetAtPath<IBIIIS.PlayerSettings>(AssetPaths.PlayerSettings);
            var visualPrefab = playerSettings != null ? playerSettings.VisualPrefab : null;
            if (visualPrefab == null) throw new InvalidOperationException("GlobalPlayerSettings의 Visual Prefab이 비어 있습니다. IBIIIS > Create Default Player Visual로 먼저 외형을 만드세요.");
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("OverworldPlayer", typeof(CharacterController), typeof(OverworldPlayer));
                SceneManager.MoveGameObjectToScene(root, preview);
                var controller = root.GetComponent<CharacterController>();
                controller.center = new Vector3(0, .5f, 0); controller.height = 1; controller.radius = .3f;
                var so = new SerializedObject(root.GetComponent<OverworldPlayer>());
                so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.transform);
                visual.transform.localPosition = Vector3.zero;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetPaths.OverworldPlayer, out bool saved);
                if (!saved) throw new IOException("미니맵 플레이어 프리팹을 저장하지 못했습니다.");
                return prefab;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        [MenuItem("IBIIIS/Overworld/Create NPC Prefab")]
        public static void CreateNpcPrefabMenu() { Selection.activeObject = EnsureNpcPrefab(); EditorGUIUtility.PingObject(Selection.activeObject); }
        /// <summary>바닥·경계·플레이어·따라가는 카메라·NPC 하나·스테이지 입구 하나를 담은 테스트 씬을 만든다. 현재 열린 씬은 바꾸지 않는다.</summary>
        public static string CreateTestScene(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play 모드를 종료한 뒤 만드세요.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("이름 없는 씬이 열려 있습니다. 현재 씬을 먼저 저장한 뒤 다시 실행하세요. 기존 작업은 변경하지 않았습니다.");
            AssetPaths.EnsureFolder(Path.GetDirectoryName(path));
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                // 새 오브젝트가 사용자가 열어 둔 씬에 잠시라도 생기지 않도록 새 씬을 활성 씬으로 둔다.
                SceneManager.SetActiveScene(scene);
                var settings = EnsureSettings(true);
                var playerPrefab = EnsurePlayerPrefab(settings);
                var ground = MapEditorSetup.EnsureGroundMaterial();
                var npcPrefab = EnsureNpcPrefab();
                var stageMaterial = EnsureMaterial("Placeholder_Stage", new Color(1f, .55f, .25f));
                var flagPrefab = EnsureClearedFlagPrefab();
                const float half = 12;
                var floor = Primitive(PrimitiveType.Quad, "Ground", scene, null, ground, false);
                floor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90, 0, 0)); floor.transform.localScale = new Vector3(half * 2, half * 2, 1);
                var bounds = new GameObject("Bounds"); SceneManager.MoveGameObjectToScene(bounds, scene);
                for (int side = 0; side < 4; side++)
                {
                    var wall = new GameObject("Wall " + side, typeof(BoxCollider)); wall.transform.SetParent(bounds.transform, false);
                    bool vertical = side < 2; float sign = side % 2 == 0 ? -1 : 1;
                    wall.transform.localPosition = vertical ? new Vector3(sign * (half + .5f), 1, 0) : new Vector3(0, 1, sign * (half + .5f));
                    wall.GetComponent<BoxCollider>().size = vertical ? new Vector3(1, 2, half * 2 + 2) : new Vector3(half * 2 + 2, 2, 1);
                }
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.transform.position = new Vector3(0, 0, -3);
                var cameraObject = new GameObject("Overworld Camera", typeof(Camera), typeof(AudioListener), typeof(OverworldCamera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f); camera.farClipPlane = 500;
                if (settings.CameraSettings != null) settings.CameraSettings.Apply(camera);
                var follow = cameraObject.GetComponent<OverworldCamera>(); follow.Configure(player.transform, settings); follow.Snap();

                var npc = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, scene); npc.name = "NPC_Guide";
                npc.transform.position = new Vector3(-3, 0, 0);
                var speaker = new SerializedObject(npc.GetComponent<NpcSpeaker>());
                speaker.FindProperty("displayName").stringValue = "안내인";
                var lines = speaker.FindProperty("lines"); lines.arraySize = 3;
                lines.GetArrayElementAtIndex(0).stringValue = "어서 와. 여기는 미니맵 테스트 장소야.";
                lines.GetArrayElementAtIndex(1).stringValue = "주황색 발판 위에서 F를 누르면 전투에 들어갈 수 있어.";
                lines.GetArrayElementAtIndex(2).stringValue = "이기든 지든 다시 여기로 돌아오게 될 거야.";
                speaker.ApplyModifiedPropertiesWithoutUndo();

                var stage = new GameObject("Stage_ProtoTypeMap"); SceneManager.MoveGameObjectToScene(stage, scene);
                stage.transform.position = new Vector3(3, 0, 3);
                var pad = Primitive(PrimitiveType.Cylinder, "Pad (임시)", scene, stage.transform, stageMaterial, false);
                pad.transform.localPosition = new Vector3(0, .01f, 0); pad.transform.localScale = new Vector3(1.4f, .01f, 1.4f);
                var flag = (GameObject)PrefabUtility.InstantiatePrefab(flagPrefab, stage.transform); flag.name = "Cleared Flag";
                flag.transform.localPosition = new Vector3(.5f, 0, .5f); flag.SetActive(false);
                var entrance = new SerializedObject(stage.AddComponent<StageEntrance>());
                entrance.FindProperty("stageId").stringValue = "stage-prototype-01";
                entrance.FindProperty("displayName").stringValue = "프로토타입 스테이지";
                entrance.FindProperty("clearedIndicator").objectReferenceValue = flag;
                var battleScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetPaths.OverworldTestStage);
                if (battleScene != null) { entrance.FindProperty("battleSceneAsset").objectReferenceValue = battleScene; entrance.FindProperty("battleScenePath").stringValue = AssetPaths.OverworldTestStage; }
                else Debug.LogWarning($"[IBIIIS] {AssetPaths.OverworldTestStage}이(가) 없어 스테이지 입구의 Battle Scene을 비워 두었습니다. Inspector에서 지정하세요.");
                entrance.ApplyModifiedPropertiesWithoutUndo();
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("미니맵 테스트 씬 저장에 실패했습니다.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            return path;
        }
        /// <summary>임시 NPC 실루엣(256×256, PPU 256 = 캔버스 한 칸 너비, 기준점 아래 중앙). 정식 그림이 생기면 이 파일을 교체하거나 프리팹의 Visual 스프라이트를 바꾼다.</summary>
        public static Sprite EnsureNpcSprite()
        {
            var fill = new Color(.66f, .76f, .9f);
            return GeneratedSprites.EnsureGeneratedSprite(AssetPaths.NpcPlaceholderSprite, (u, v) =>
                GeneratedSprites.Shade(Mathf.Min(GeneratedSprites.RoundedBox(u, v - .33f, .22f, .29f, .12f), new Vector2(u, v - .76f).magnitude - .14f), fill));
        }
        /// <summary>임시 클리어 깃발(256×256, PPU 256, 기준점 아래 중앙 = 깃대 밑동 근처): 갈색 깃대, 초록 삼각 깃발, 노란 꼭지.</summary>
        public static Sprite EnsureClearedFlagSprite()
        {
            Color pole = new Color(.5f, .38f, .26f), flag = new Color(.35f, .88f, .45f), knob = new Color(.98f, .82f, .3f);
            return GeneratedSprites.EnsureGeneratedSprite(AssetPaths.ClearedFlagSprite, (u, v) =>
            {
                float dKnob = new Vector2(u + .12f, v - .95f).magnitude - .04f;
                float dPole = GeneratedSprites.RoundedBox(u + .12f, v - .47f, .025f, .47f, .02f);
                float dFlag = GeneratedSprites.Triangle(new Vector2(u, v), new Vector2(-.1f, .92f), new Vector2(-.1f, .56f), new Vector2(.32f, .74f));
                float d = Mathf.Min(dKnob, Mathf.Min(dPole, dFlag));
                return GeneratedSprites.Shade(d, d == dKnob ? knob : d == dPole ? pole : flag);
            });
        }
        /// <summary>이전에 만든 NPC 프리팹의 Visual에 숨쉬기가 없으면 붙인다(공용 손맛 설정 연결). 이미 있으면 그대로 둔다.</summary>
        public static void EnsureNpcBreathing()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(AssetPaths.NpcPrefab) == null) return;
            var root = PrefabUtility.LoadPrefabContents(AssetPaths.NpcPrefab);
            try
            {
                var visual = root.transform.Find("Visual");
                if (visual == null || visual.GetComponent<BreathingSprite>() != null) return;
                visual.gameObject.AddComponent<BreathingSprite>().Configure(AssetDatabase.LoadAssetAtPath<MotionFeedbackSettings>(AssetPaths.MotionFeedback));
                PrefabUtility.SaveAsPrefabAsset(root, AssetPaths.NpcPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        /// <summary>클리어 깃발 프리팹: 카메라를 바라보는 스프라이트 하나. 스테이지 입구의 Cleared Indicator로 연결하며 클리어한 입구에서만 켜진다. 이미 있으면 덮어쓰지 않는다.</summary>
        public static GameObject EnsureClearedFlagPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPaths.ClearedFlagPrefab);
            if (existing != null) return existing;
            var sprite = EnsureClearedFlagSprite();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("ClearedFlag", typeof(SpriteRenderer), typeof(CameraFacingSprite));
                SceneManager.MoveGameObjectToScene(root, preview);
                root.GetComponent<SpriteRenderer>().sprite = sprite;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetPaths.ClearedFlagPrefab, out bool saved);
                if (!saved) throw new IOException("클리어 깃발 프리팹을 저장하지 못했습니다.");
                return prefab;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        /// <summary>미니맵 NPC 기본 프리팹: 루트에 NpcSpeaker와 보이지 않는 CapsuleCollider(막힘), 자식 Visual에 카메라를 바라보는 스프라이트, 발밑 그림자.
        /// 그림·크기는 Visual에서만 바꾸므로 막힘·말 걸기 범위와 무관하다. 이미 있으면 덮어쓰지 않는다.</summary>
        public static GameObject EnsureNpcPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPaths.NpcPrefab);
            if (existing != null) { EnsureNpcBreathing(); return existing; }
            var sprite = EnsureNpcSprite();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("NPC", typeof(CapsuleCollider), typeof(NpcSpeaker));
                SceneManager.MoveGameObjectToScene(root, preview);
                var collider = root.GetComponent<CapsuleCollider>(); collider.center = new Vector3(0, .5f, 0); collider.height = 1; collider.radius = .3f;
                var visual = new GameObject("Visual", typeof(SpriteRenderer), typeof(CameraFacingSprite));
                visual.transform.SetParent(root.transform, false); visual.transform.localScale = Vector3.one * 1.5f; // 플레이어·적과 같은 임시 표시 배율
                visual.GetComponent<SpriteRenderer>().sprite = sprite;
                visual.AddComponent<BreathingSprite>().Configure(AssetDatabase.LoadAssetAtPath<MotionFeedbackSettings>(AssetPaths.MotionFeedback));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetPaths.NpcPrefab, out bool saved);
                if (!saved) throw new IOException("NPC 프리팹을 저장하지 못했습니다.");
                if (!GroundMarkerSetup.AttachShadow(AssetPaths.NpcPrefab))
                    Debug.LogWarning($"[IBIIIS] {GroundMarkerSetup.ShadowPrefab}이(가) 없어 NPC 프리팹에 그림자를 붙이지 않았습니다. IBIIIS > Create Ground Markers를 실행하면 빠진 그림자를 붙입니다.");
                return AssetDatabase.LoadAssetAtPath<GameObject>(AssetPaths.NpcPrefab);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        private static GameObject Primitive(PrimitiveType type, string name, Scene scene, Transform parent, Material material, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            if (parent != null) go.transform.SetParent(parent, false); else SceneManager.MoveGameObjectToScene(go, scene);
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        private static Material EnsureMaterial(string name, Color color)
        {
            var path = $"{AssetPaths.Overworld}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP/Unlit 셰이더가 없습니다.");
            AssetPaths.EnsureFolder(AssetPaths.Overworld);
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        /// <summary>슬롯이 비어 있을 때만 값을 구해(에셋 생성 포함) 연결한다.</summary>
        private static void Fill(SerializedObject so, string property, Func<Object> getValue)
        {
            var field = so.FindProperty(property);
            if (field.objectReferenceValue != null) return;
            var value = getValue();
            if (value == null) return;
            field.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(so.targetObject);
        }
    }
}
