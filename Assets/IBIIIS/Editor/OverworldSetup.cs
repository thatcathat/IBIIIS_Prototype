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
            if (!AssetDatabase.IsValidFolder(AssetPaths.Overworld)) AssetDatabase.CreateFolder(AssetPaths.Root, Path.GetFileName(AssetPaths.Overworld));
            return AssetDatabase.GenerateUniqueAssetPath(AssetPaths.OverworldScene);
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
        public static OverworldSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<OverworldSettings>(AssetPaths.OverworldSettings);
            if (settings == null)
            {
                Directory.CreateDirectory(AssetPaths.Settings); AssetDatabase.Refresh();
                settings = ScriptableObject.CreateInstance<OverworldSettings>();
                AssetDatabase.CreateAsset(settings, AssetPaths.OverworldSettings);
            }
            var so = new SerializedObject(settings);
            Fill(so, "cameraSettings", EnsureCameraSettings());
            Fill(so, "inputActions", EnsureInput());
            Fill(so, "motionFeedback", AssetDatabase.LoadAssetAtPath<MotionFeedbackSettings>(AssetPaths.MotionFeedback));
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
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.OverworldInput);
            if (existing != null) { AddMissingInputActions(); return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.OverworldInput); }
            Directory.CreateDirectory(AssetPaths.Settings);
            var created = OverworldInput.CreateDefaultAsset();
            try { File.WriteAllText(AssetPaths.OverworldInput, created.ToJson()); }
            finally { Object.DestroyImmediate(created); }
            AssetDatabase.ImportAsset(AssetPaths.OverworldInput, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPaths.OverworldInput);
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                // 새 오브젝트가 사용자가 열어 둔 씬에 잠시라도 생기지 않도록 새 씬을 활성 씬으로 둔다.
                SceneManager.SetActiveScene(scene);
                var settings = EnsureSettings();
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
            return EnsureGeneratedSprite(AssetPaths.NpcPlaceholderSprite, (u, v) =>
                Shade(Mathf.Min(RoundedBox(u, v - .33f, .22f, .29f, .12f), new Vector2(u, v - .76f).magnitude - .14f), fill));
        }
        /// <summary>임시 클리어 깃발(256×256, PPU 256, 기준점 아래 중앙 = 깃대 밑동 근처): 갈색 깃대, 초록 삼각 깃발, 노란 꼭지.</summary>
        public static Sprite EnsureClearedFlagSprite()
        {
            Color pole = new Color(.5f, .38f, .26f), flag = new Color(.35f, .88f, .45f), knob = new Color(.98f, .82f, .3f);
            return EnsureGeneratedSprite(AssetPaths.ClearedFlagSprite, (u, v) =>
            {
                float dKnob = new Vector2(u + .12f, v - .95f).magnitude - .04f;
                float dPole = RoundedBox(u + .12f, v - .47f, .025f, .47f, .02f);
                float dFlag = Triangle(new Vector2(u, v), new Vector2(-.1f, .92f), new Vector2(-.1f, .56f), new Vector2(.32f, .74f));
                float d = Mathf.Min(dKnob, Mathf.Min(dPole, dFlag));
                return Shade(d, d == dKnob ? knob : d == dPole ? pole : flag);
            });
        }
        private static readonly Color Outline = new Color(.18f, .22f, .32f);
        // 거리 d(안쪽 음수)에 따라 채움/테두리/투명을 고른다. 테두리 두께는 캔버스 너비의 2%.
        private static Color Shade(float d, Color fill) => d < -.02f ? new Color(fill.r, fill.g, fill.b, 1) : d < 0 ? new Color(Outline.r, Outline.g, Outline.b, 1) : Color.clear;
        /// <summary>코드로 그린 256×256 임시 스프라이트를 PNG로 저장하고 스프라이트(PPU 256, 기준점 아래 중앙)로 가져온다. 파일이 있으면 그대로 쓴다.
        /// sample(u, v): u는 -0.5~0.5(가로), v는 0~1(아래→위). 4×4 표본 평균으로 가장자리를 부드럽게 한다.</summary>
        private static Sprite EnsureGeneratedSprite(string path, Func<float, float, Color> sample)
        {
            const int size = 256;
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.Refresh();
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                    {
                        var c = sample((x + (sx + .5f) / 4) / size - .5f, (y + (sy + .5f) / 4) / size);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                    var avg = sum / 16; pixels[y * size + x] = avg.a > 0 ? new Color(avg.r / avg.a, avg.g / avg.a, avg.b / avg.a, avg.a) : Color.clear;
                }
                texture.SetPixels(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter; settings.spritePivot = new Vector2(.5f, 0);
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static float RoundedBox(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            var q = new Vector2(Mathf.Abs(x) - halfWidth + radius, Mathf.Abs(y) - halfHeight + radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
        }
        // 삼각형까지의 부호 있는 거리(안쪽 음수).
        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 e0 = b - a, e1 = c - b, e2 = a - c, v0 = p - a, v1 = p - b, v2 = p - c;
            Vector2 q0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 q1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 q2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            var d = Vector2.Min(Vector2.Min(new Vector2(Vector2.Dot(q0, q0), s * (v0.x * e0.y - v0.y * e0.x)),
                new Vector2(Vector2.Dot(q1, q1), s * (v1.x * e1.y - v1.y * e1.x))), new Vector2(Vector2.Dot(q2, q2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
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
                var shadow = AssetDatabase.LoadAssetAtPath<GameObject>(GroundMarkerSetup.ShadowPrefab);
                if (shadow != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(shadow, root.transform);
                    instance.name = GroundMarkerSetup.ShadowName; instance.transform.localPosition = new Vector3(0, .03f, 0);
                }
                else Debug.LogWarning($"[IBIIIS] {GroundMarkerSetup.ShadowPrefab}이(가) 없어 NPC 프리팹에 그림자를 붙이지 않았습니다. IBIIIS > Create Ground Markers 후 직접 붙이세요.");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetPaths.NpcPrefab, out bool saved);
                if (!saved) throw new IOException("NPC 프리팹을 저장하지 못했습니다.");
                return prefab;
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
            Directory.CreateDirectory(AssetPaths.Overworld); AssetDatabase.Refresh();
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static void Fill(SerializedObject so, string property, Object value)
        {
            var field = so.FindProperty(property);
            if (field.objectReferenceValue != null || value == null) return;
            field.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(so.targetObject);
        }
    }
}
