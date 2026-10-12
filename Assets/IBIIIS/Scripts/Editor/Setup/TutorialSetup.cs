using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>타이틀과 튜토리얼 1·2단계 콘텐츠를 만든다(기획 원본: 튜토리얼 1·2단계 규칙 문서와 HTML 프로토타입).
    /// 임시 그림(어시파리·허수아비·칼), 대화 인물, 대본 2개, 맵·씬 2개, 타이틀 씬을 없을 때만 만들고, 빌드 씬 목록에 없는 씬만 추가한다(타이틀은 맨 앞).
    /// 이미 있는 에셋·씬은 덮어쓰지 않는다. 기획서 좌표(행 0이 맨 위)는 맵 좌표(행 0이 맨 아래)로 옮긴다.</summary>
    public static class TutorialSetup
    {
        public const string Folder = AssetPaths.Root + "/Tutorial";
        // 튜토리얼 전용 에셋은 종류별 하위 폴더에 둔다(Docs 개발 및 사용 가이드의 폴더 규칙).
        public const string SpriteFolder = Folder + "/Sprites", ScenarioFolder = AssetPaths.Scenarios + "/Tutorial";
        public const string FlySprite = SpriteFolder + "/AssiFly.png", ScarecrowSprite = SpriteFolder + "/Scarecrow.png", SwordSprite = SpriteFolder + "/Sword.png";
        // 임시: 땀 아이콘 에셋이 없어 코드로 그린 물방울. 정식 그림이 생기면 대본의 감정 표시 아이콘만 바꾼다.
        public const string SweatSprite = SpriteFolder + "/Sweat.png";
        // 대본 자세 연출에 쓰는 플레이어 그림(Characters/Player/Sprites).
        public const string SurprisedPose = PlayerPrefabSetup.SpriteFolder + "/c_motion_surprized_R.png", DangerGonePose = PlayerPrefabSetup.SpriteFolder + "/c_danger_gone.png", ProudPose = PlayerPrefabSetup.SpriteFolder + "/c_danger_cleared.png", DangerPose = PlayerPrefabSetup.SpriteFolder + "/c_danger.png";
        public const string Stage1Folder = AssetPaths.Maps + "/Tutorial1", Stage2Folder = AssetPaths.Maps + "/Tutorial2";
        public const string Stage1Map = Stage1Folder + "/Tutorial1.asset", Stage1Scene = Stage1Folder + "/Tutorial1.unity";
        public const string Stage2Map = Stage2Folder + "/Tutorial2.asset", Stage2Scene = Stage2Folder + "/Tutorial2.unity";
        public const string Stage1Script = ScenarioFolder + "/Tutorial1Script.asset", Stage2Script = ScenarioFolder + "/Tutorial2Script.asset";
        public const string TitleFolder = AssetPaths.Scenes, TitleScene = TitleFolder + "/Title.unity", TitleLayout = AssetPaths.UI + "/Title.uxml";
        public const string ClearId = "tutorial";
        private const string Enemy003 = AssetPaths.Enemies + "/Enemy003/Enemy003.prefab";

        [MenuItem("IBIIIS/Create Tutorial Content")]
        public static void EnsureAll()
        {
            AssetPaths.EnsureFolder(Folder); AssetPaths.EnsureFolder(ScenarioFolder);
            var ui = GameUiSetup.EnsureDefault();
            DialogueSetup.EnsureDefault();
            var cast = EnsureCast();
            var fly = EnsureSprite(FlySprite, FlyShape);
            var scarecrow = EnsureSprite(ScarecrowSprite, ScarecrowShape);
            var art = EnsureArt();
            var script1 = EnsureScript(Stage1Script, "tutorial_1", Stage1Steps(cast, art));
            var script2 = EnsureScript(Stage2Script, "tutorial_2", Stage2Steps(cast, art));
            EnsureStage(Stage1Folder, Stage1Map, Stage1Scene, 9, 5, new Vector2Int(1, 2), new (Vector2Int, Vector2Int)[0], script1, fly, false, scarecrow);
            EnsureStage(Stage2Folder, Stage2Map, Stage2Scene, 7, 7, new Vector2Int(3, 0),
                new[] { (new Vector2Int(1, 5), Vector2Int.down), (new Vector2Int(4, 1), Vector2Int.down) }, script2, fly, true, null);
            EnsureTitle(ui);
            EnsureBuildList(TitleScene, Stage1Scene, Stage2Scene, AssetPaths.OverworldScene);
            Debug.Log($"[IBIIIS] 튜토리얼 콘텐츠 준비: {TitleScene} → {Stage1Scene} → {Stage2Scene} → {AssetPaths.OverworldScene}");
        }

        /// <summary>두 대본을 이 코드의 기본 내용으로 다시 만든다. Inspector에서 고친 대본 내용은 사라지므로 확인을 받는다.</summary>
        [MenuItem("IBIIIS/Reset Scenario Scripts")]
        private static void ResetScriptsMenu()
        {
            if (EditorUtility.DisplayDialog("튜토리얼 대본 초기화", "Tutorial1Script·Tutorial2Script를 기본 대본으로 덮어씁니다. Inspector에서 고친 내용은 사라집니다.", "덮어쓰기", "취소")) ResetScripts();
        }
        public static void ResetScripts()
        {
            var cast = EnsureCast(); var art = EnsureArt();
            Overwrite(Stage1Script, "tutorial_1", Stage1Steps(cast, art));
            Overwrite(Stage2Script, "tutorial_2", Stage2Steps(cast, art));
        }
        private static void Overwrite(string path, string id, ScenarioStep[] steps)
        {
            var script = AssetDatabase.LoadAssetAtPath<ScenarioScript>(path);
            if (script == null) { EnsureScript(path, id, steps); return; }
            script.Set(id, steps); EditorUtility.SetDirty(script); AssetDatabase.SaveAssetIfDirty(script);
        }
        // ── 대본에 쓰는 그림 ──
        public sealed class Art { public Sprite Sword, Sweat, Surprised, DangerGone, Proud, Danger; }
        private static Art EnsureArt()
        {
            PlayerPrefabSetup.ImportSprites();
            return new Art
            {
                Sword = EnsureSprite(SwordSprite, SwordShape), Sweat = EnsureSprite(SweatSprite, SweatShape),
                Surprised = LoadPose(SurprisedPose), DangerGone = LoadPose(DangerGonePose), Proud = LoadPose(ProudPose), Danger = LoadPose(DangerPose),
            };
        }
        // 자세 그림이 없으면 경고만 하고 그 자세 단계는 건너뛴다(진행은 막지 않음).
        private static Sprite LoadPose(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[IBIIIS] 튜토리얼 자세 그림이 없습니다: {path}");
            return sprite;
        }
        // 물방울: 아래는 둥글고 위로 뾰족한 하늘색 방울.
        private static Color SweatShape(float u, float v)
        {
            var p = new Vector2(u, v - .36f);
            float round = p.magnitude - .26f;
            float tip = GeneratedSprites.Triangle(new Vector2(u, v), new Vector2(-.22f, .48f), new Vector2(.22f, .48f), new Vector2(0, .92f));
            float d = Mathf.Min(round, tip);
            if (new Vector2(u + .09f, v - .42f).magnitude < .06f && d < -.02f) return Color.white; // 반짝임
            return GeneratedSprites.Shade(d, new Color(.55f, .82f, 1f));
        }

        // ── 대화 인물 ──
        public sealed class Cast { public DialogueCharacter Hero, Fly, Scarecrow, Enemy00, Enemy01; }
        private static Cast EnsureCast()
        {
            var hero = AssetDatabase.LoadAssetAtPath<DialogueCharacter>(DialogueSetup.Player);
            // 기획서 대본의 화자 이름은 '주인공'(플레이어 이름 기능은 나중). 처음 만든 임시 이름이면 맞춘다.
            if (hero != null && hero.DisplayName == "플레이어") Rename(hero, "주인공");
            var fly = AssetDatabase.LoadAssetAtPath<DialogueCharacter>(DialogueSetup.AssiFly);
            if (fly != null && fly.DisplayName == "어시 파리") Rename(fly, "어시파리"); // 기획서 검토 의견: '어시파리'로 표기 통일
            return new Cast
            {
                Hero = hero, Fly = fly,
                Scarecrow = EnsureCharacter("Scarecrow", "scarecrow", "허수아비", new Color(.95f, .8f, .45f), new Color(.75f, .6f, .35f)),
                Enemy00 = EnsureCharacter("Enemy00", "enemy_00", "Enemy_00", new Color(1f, .6f, .55f), new Color(.88f, .93f, 1f)),
                Enemy01 = EnsureCharacter("Enemy01", "enemy_01", "Enemy_01", new Color(1f, .6f, .55f), new Color(.8f, .88f, 1f)),
            };
        }
        private static void Rename(DialogueCharacter character, string name)
        {
            var so = new SerializedObject(character); so.FindProperty("displayName").stringValue = name; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(character); AssetDatabase.SaveAssetIfDirty(character);
        }
        private static DialogueCharacter EnsureCharacter(string file, string id, string name, Color nameColor, Color fill)
        {
            var path = $"{DialogueSetup.CharacterFolder}/{file}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<DialogueCharacter>(path);
            if (existing != null) return existing;
            var portrait = GeneratedSprites.EnsureGeneratedSprite($"{DialogueSetup.PortraitFolder}/{file}_Placeholder.png", (u, v) =>
                GeneratedSprites.Shade(Mathf.Min(GeneratedSprites.RoundedBox(u, v - .2f, .36f, .22f, .16f), new Vector2(u, v - .64f).magnitude - .2f), fill));
            var character = ScriptableObject.CreateInstance<DialogueCharacter>();
            character.Set(id, name, DialogueSide.Right, portrait, nameColor);
            AssetDatabase.CreateAsset(character, path); AssetDatabase.SaveAssetIfDirty(character);
            return character;
        }

        // ── 임시 그림(256×256, 기준점 아래 가운데) ──
        private static Sprite EnsureSprite(string path, System.Func<float, float, Color> shape) => GeneratedSprites.EnsureGeneratedSprite(path, shape);
        // 어시파리: 초록 둥근 몸통, 하늘색 날개 둘, 흰 눈.
        private static Color FlyShape(float u, float v)
        {
            var wing = new Color(.75f, .92f, 1f, .9f); var body = new Color(.4f, .78f, .35f);
            float eye = Mathf.Min(new Vector2(u + .08f, v - .5f).magnitude, new Vector2(u - .08f, v - .5f).magnitude) - .045f;
            if (eye < 0) return Color.white;
            float b = new Vector2(u, v - .42f).magnitude - .22f;
            if (b < 0) return GeneratedSprites.Shade(b, body);
            float w = Mathf.Min(new Vector2((u + .24f) / 1.3f, v - .7f).magnitude, new Vector2((u - .24f) / 1.3f, v - .7f).magnitude) - .14f;
            return GeneratedSprites.Shade(Mathf.Min(b, w), wing);
        }
        // 허수아비: 갈색 막대·가로대, 짚 색 둥근 머리.
        private static Color ScarecrowShape(float u, float v)
        {
            float pole = GeneratedSprites.RoundedBox(u, v - .38f, .03f, .38f, .02f), bar = GeneratedSprites.RoundedBox(u, v - .52f, .32f, .03f, .02f);
            float head = new Vector2(u, v - .8f).magnitude - .13f;
            if (head < 0) return GeneratedSprites.Shade(head, new Color(.95f, .82f, .45f));
            return GeneratedSprites.Shade(Mathf.Min(Mathf.Min(pole, bar), head), new Color(.55f, .38f, .2f));
        }
        // 칼: 회색 칼날, 갈색 손잡이(세로).
        private static Color SwordShape(float u, float v)
        {
            float blade = GeneratedSprites.RoundedBox(u, v - .6f, .05f, .32f, .03f), guard = GeneratedSprites.RoundedBox(u, v - .26f, .14f, .025f, .02f), grip = GeneratedSprites.RoundedBox(u, v - .14f, .03f, .1f, .02f);
            if (blade < 0) return GeneratedSprites.Shade(blade, new Color(.85f, .88f, .92f));
            return GeneratedSprites.Shade(Mathf.Min(Mathf.Min(blade, guard), grip), new Color(.5f, .33f, .18f));
        }

        // ── 대본(기획서 대사·지문 순서 그대로) ──
        private static ScenarioStep Say(DialogueCharacter who, string text, string nameOverride = null) => new ScenarioStep { type = ScenarioStepType.Line, line = new DialogueLine { speaker = who, text = text, nameOverride = nameOverride } };
        private static ScenarioStep Narrate(string text) => new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate(text) };
        private static ScenarioStep Emote(string target, string mark, Sprite icon = null) => new ScenarioStep { type = ScenarioStepType.Emote, target = target, text = mark, sprite = icon };
        // 자세: 다음 대사가 끝날 때까지 그림을 바꾸고 처음에 움직임을 준다.
        private static ScenarioStep Pose(Sprite sprite, PoseMotion motion, PoseMotion endMotion = PoseMotion.None) => new ScenarioStep { type = ScenarioStepType.Pose, sprite = sprite, motion = motion, endMotion = endMotion };
        private static ScenarioStep Show(string target) => new ScenarioStep { type = ScenarioStepType.SetActive, target = target, active = true };
        private static ScenarioStep[] Stage1Steps(Cast c, Art art)
        {
            const string unknown = "???(어시파리)";
            var wasd = "W A S D";
            return new[]
            {
                Narrate("주인공은 곧바로 튜토리얼 던전으로 접속된다"),
                Emote("player", "?"), Narrate("좌우를 살피며, 어리둥절한 모습이다"),
                Show("fly"), Narrate("어씨파리가 재빠르게 나타나 말을 건다"),
                Say(c.Fly, "안녕 플레이어! 만나서 반가워!", unknown),
                Emote("player", "!"), Pose(art.Surprised, PoseMotion.Hop, PoseMotion.Hop), Narrate("주인공 화들짝 놀람"),
                Say(c.Fly, "(어우씨 깜짝아 뭘 사람이 이렇게 놀라..;;) 나는 플레이어의 모험을 도울 어시스트 AI야!", unknown),
                Say(c.Fly, "앞으로 자주 보게 될테니, 편하게 불러줘!", unknown),
                Narrate("화면 앞 팝업창으로 ‘어시스트 AI의 이름을 붙여주세요 — 어씨 [  ]’ 가 뜬다"),
                new ScenarioStep { type = ScenarioStepType.NameInput, line = DialogueLine.Narrate("어시스트 AI의 이름을 붙여주세요 — 어씨 [  ]"), text = "파리" },
                Say(c.Hero, ".....파리?"),
                Narrate("어시스트 AI 이름이 ‘어시 파리’로 고정됩니다."),
                Say(c.Fly, "(화난 표정으로) 잠깐만!! 야!! 파리가 뭐야!!!!!! 이거 바꾸지도 못한다고!!!"),
                Emote("player", ";;", art.Sweat), Narrate("주인공은 놀라서 ;; 땀을 흘린다"),
                Say(c.Fly, "하... 일단 정해진 이름은 어쩔 수 없으니.. (후... 참자....) 이 게임의 기본적인 튜토리얼을 알려줄게!(이꽉문 표정)"),
                Narrate("주인공 끄덕거림"),
                Say(c.Fly, "자 맵이 보이지? 이게 플레이어, 너야!"),
                Emote("player", "▼"), Narrate("주인공 머리 위에 화살표가 뜬다"),
                Say(c.Fly, "WASD로 이동할 수 있어! 한번 움직여볼래?"),
                // 네 방향을 모두 한 번씩 눌러야 넘어간다(누른 키는 표시에서 빠짐).
                new ScenarioStep { type = ScenarioStepType.WaitAllDirections, keys = wasd },
                Show("scarecrow"),
                Say(c.Fly, "좋았어! 다음은 [공격]이야! 앞에 너를 위해 허수아비를 준비해뒀어! 허수아비 앞까지 이동해볼래?"),
                Say(c.Fly, "허수아비는 너를 공격하지 않으니 안심하라고! (찡긋)"),
                Narrate("허수아비 앞 칸이 빛나서 플레이어 이동을 유도한다"),
                new ScenarioStep { type = ScenarioStepType.WaitReachCell, actions = ScenarioActions.Move, cell = new Vector2Int(6, 2), keys = wasd },
                Say(c.Fly, "자! 이제 허수아비를 좌클릭으로 [공격]해봐!"),
                new ScenarioStep { type = ScenarioStepType.WaitClickCell, cell = new Vector2Int(7, 2), seconds = 1.1f, sprite = art.Sword, keys = "좌클릭" },
                Narrate("주인공이 공격 모션을 취하지만, 칼이 옆으로 날라간다."),
                Say(c.Fly, "..."),
                Say(c.Hero, "..."),
                Say(c.Scarecrow, "...(풋)"),
                Say(c.Hero, "(쟤 분명 나 비웃은 것 같은ㄷ....)"),
                Say(c.Fly, "너... 끔찍하게 못.. 아니... 그.. 공격은 조금 연습이 필요하겠네..."),
                Say(c.Hero, "(끔찍하게 못...??)"),
                Emote("player", ";;", art.Sweat), Narrate("주인공 땀 표시"),
                Say(c.Fly, "괘,괜찮아! 실전만큼 도움되는 연습은 없지! 이, 일단 앞으로 나아가볼까? 하하.."),
                Say(c.Hero, "(뭔가 얼렁뚱땅인데.... 괜찮으려나..)"),
                new ScenarioStep { type = ScenarioStepType.LoadScene, scenePath = Stage2Scene },
            };
        }
        private static ScenarioStep[] Stage2Steps(Cast c, Art art)
        {
            var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(Enemy003);
            return new[]
            {
                Narrate("튜토리얼 던전 2단계 맵으로 진입한다"),
                Say(c.Fly, "이제 몬스터를 만나러 가보자!"),
                Say(c.Fly, "저기 앞에 몬스터가 있어! 다가가볼래?"),
                Say(c.Enemy00, "음? 넌 뭐지?"),
                Say(c.Hero, "(눈사람? 여기는 한 여름 아닌가? 어떻게 안 녹고 살아있지?)"),
                Say(c.Hero, "너 왜 살아있어? (아니 어떻게 더운ㄷㅔ..)"),
                Say(c.Enemy00, "? 시비냐! 이몸은 무려 티타늄 스노우로 만들어진 눈사람이라고!! 감히 내가 튜토리얼 몬스터라고 나를 무시하는 거냐???"),
                Say(c.Hero, "아니 그런 적 없어...! 오해야!! 애초에 티타늄 스노우 그런게 왜 있는데?!?!?"),
                Narrate("Enemy_00이 돌진 모션을 취함"),
                Say(c.Hero, "살려줘 어씨파리!!!"),
                // 대본 기준 위험 자세: 이 대사 뒤부터 첫 회피 행동(A)을 시작할 때까지. 튜토리얼 중에는 자동 위험 그림을 끈다.
                new ScenarioStep { type = ScenarioStepType.Pose, sprite = art.Danger, motion = PoseMotion.Shake, endMotion = PoseMotion.Shake, untilAction = true },
                Say(c.Fly, "일단 무기를 들어!"),
                Say(c.Hero, "어? 아까 던져버렸는데????? 그런 거 말고ㅠㅠㅠ!!"),
                Say(c.Fly, "아잇 쓸모없어!"),
                Say(c.Hero, "빨리..! 이러다 맞겠어ㅠㅠ 뭐라도 알려줘..!"),
                Say(c.Fly, "그럼 피해야지! 왼쪽으로 움직여!"),
                new ScenarioStep { type = ScenarioStepType.WaitAction, actions = ScenarioActions.Move, direction = Vector2Int.left, keys = "A" },
                Pose(art.DangerGone, PoseMotion.Sigh, PoseMotion.Sigh),
                // 1턴 직후 합류(기획서 (6,4)← · (0,5)→ → 맵 좌표)
                new ScenarioStep { type = ScenarioStepType.SpawnEnemy, prefab = enemy, cell = new Vector2Int(6, 2), direction = Vector2Int.left },
                new ScenarioStep { type = ScenarioStepType.SpawnEnemy, prefab = enemy, cell = new Vector2Int(0, 1), direction = Vector2Int.right },
                Say(c.Hero, "휴... 피했다!!"),
                Say(c.Enemy00, "감히 나의 죽음을 바라고 나를 피했겠다????"),
                Say(c.Enemy01, "뭣?? 네 녀석은 누구냐!"),
                Say(c.Hero, "으악 적이 늘어났어!! 어씨파리..ㅠㅠ 더 멀리 이동할 수 있는 방법이 없어? 이러다가 잡히겠어!!"),
                Say(c.Fly, "내가 도라X몽이냐!(내가 니 엄마냐!!)"),
                Say(c.Hero, "어시스턴트자나...!!ㅠㅠㅠ( ;( )"),
                Say(c.Fly, "에휴.. 그럼 [대시]를 써봐! shift키를 누른채로 가고싶은 방향키를 눌러!"),
                new ScenarioStep { type = ScenarioStepType.WaitAction, actions = ScenarioActions.Dash, direction = Vector2Int.up, keys = "Shift W" },
                // 기획서 검토 의견: 자동 연출 대신 직접 움직이게 하고, 맞을 위치로 가려 하면 막고 경고한다.
                new ScenarioStep { type = ScenarioStepType.FreePlay, actions = ScenarioActions.Move | ScenarioActions.Wait | ScenarioActions.Dash, line = new DialogueLine { speaker = c.Fly, text = "거기로 가면 위험해!" } },
                Narrate("주인공이 회피 후 좌우를 두리번 거리고 신나한다, 몬스터끼리 부딪혀서 꽈당 사라진다"),
                Say(c.Hero, "내, 내가 피했어!!"),
                Say(c.Fly, "굼벵이도 구르는 재주가 있다더니... 이건 제법 잘하는데?"),
                Narrate("어씨파리가 주변을 확인한다"),
                Say(c.Fly, "오? 심지어 몬스터도 서로 부딪혀서 쓰러졌어! 아까 도발도 그렇고, 너.. 의도한거였구나!"),
                Say(c.Hero, "어? 어..어... 그,그런가?"),
                Pose(art.Proud, PoseMotion.Proud, PoseMotion.Proud), Say(c.Hero, "(으쓱대며) 나.. 재능 있을지도?"),
                Say(c.Fly, "차라리 앞으로 이렇게 전투해보는게 어때? 너 공격도 끔찍하게 못하니까.."),
                Say(c.Fly, "피해서 서로 부딪히게 만드는거야!"),
                Say(c.Hero, "어.... 내, 내가 할 수 있을까...?"),
                Say(c.Fly, "할 수 있든 아니든, 튜토리얼 던전을 빠져나가려면 몬스터를 해치워야 해."),
                Say(c.Fly, "캬 내가 다 알려줬다~ 나머진 알아서 처리할 수 있지?"),
                Say(c.Hero, "... 벌써 의지가 바닥났어... 스킵 없나.."),
                Narrate("플레이어가 나머지 몬스터를 처리한다"),
                new ScenarioStep { type = ScenarioStepType.RecordClear, text = ClearId },
                new ScenarioStep { type = ScenarioStepType.LoadScene, scenePath = AssetPaths.OverworldScene },
            };
        }
        private static ScenarioScript EnsureScript(string path, string id, ScenarioStep[] steps)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScenarioScript>(path);
            if (existing != null) return existing; // 사용자가 고친 대본은 덮어쓰지 않는다
            var script = ScriptableObject.CreateInstance<ScenarioScript>(); script.Set(id, steps);
            AssetDatabase.CreateAsset(script, path); AssetDatabase.SaveAssetIfDirty(script);
            return script;
        }

        // ── 맵·씬 ──
        private static void EnsureStage(string folder, string mapPath, string scenePath, int width, int height, Vector2Int start,
            (Vector2Int cell, Vector2Int facing)[] enemies, ScenarioScript script, Sprite flySprite, bool flyVisibleAtStart, Sprite scarecrowSprite)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null) return;
            AssetPaths.EnsureFolder(folder);
            var map = AssetDatabase.LoadAssetAtPath<GridMap>(mapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(width, height);
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) map.SetWalkable(new Vector2Int(x, y), true);
                map.SetStart(start);
                var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Enemy003);
                foreach (var (cell, facing) in enemies) map.PlaceEnemy(cell, enemyPrefab, facing);
                AssetDatabase.CreateAsset(map, mapPath); AssetDatabase.SaveAssetIfDirty(map);
            }
            var created = MapEditorSetup.CreateTestScene(map, scenePath);
            if (string.IsNullOrEmpty(created)) throw new System.IO.IOException($"튜토리얼 씬을 만들지 못했습니다: {scenePath}");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(created, OpenSceneMode.Additive);
            try
            {
                GridMapPlayer battle = null;
                foreach (var root in scene.GetRootGameObjects()) if (battle == null) battle = root.GetComponentInChildren<GridMapPlayer>(true);
                var actors = new List<ScenarioDirector.Actor>();
                var fly = new GameObject("AssiFly"); SceneManager.MoveGameObjectToScene(fly, scene);
                var flyRenderer = fly.AddComponent<SpriteRenderer>(); flyRenderer.sprite = flySprite; flyRenderer.sortingOrder = 4; fly.transform.localScale = Vector3.one * .55f;
                fly.AddComponent<CompanionFollower>().Configure(battle);
                fly.SetActive(flyVisibleAtStart);
                actors.Add(new ScenarioDirector.Actor { id = "fly", target = fly, height = .75f });
                if (scarecrowSprite != null)
                {
                    var scarecrow = new GameObject("Scarecrow"); SceneManager.MoveGameObjectToScene(scarecrow, scene);
                    var visual = new GameObject("Visual", typeof(SpriteRenderer), typeof(CameraFacingSprite)); visual.transform.SetParent(scarecrow.transform, false);
                    visual.transform.localScale = Vector3.one * 1.5f; visual.GetComponent<SpriteRenderer>().sprite = scarecrowSprite;
                    scarecrow.SetActive(false);
                    actors.Add(new ScenarioDirector.Actor { id = "scarecrow", target = scarecrow, height = 1.6f, blocksCell = true, cell = new Vector2Int(7, 2) });
                }
                var director = battle.gameObject.AddComponent<ScenarioDirector>();
                director.Configure(battle, script, actors.ToArray());
                EditorUtility.SetDirty(director);
                if (!EditorSceneManager.SaveScene(scene)) throw new System.IO.IOException($"튜토리얼 씬 저장 실패: {created}");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        private static void EnsureTitle(GameUiSettings ui)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScene) != null) return;
            AssetPaths.EnsureFolder(TitleFolder);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var camera = new GameObject("Title Camera", typeof(Camera), typeof(AudioListener)); SceneManager.MoveGameObjectToScene(camera, scene);
                camera.tag = "MainCamera"; var cam = camera.GetComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.055f, .07f, .11f);
                var title = new GameObject("Title"); SceneManager.MoveGameObjectToScene(title, scene);
                var document = title.AddComponent<UIDocument>(); document.panelSettings = ui.PanelSettings; document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TitleLayout);
                var screen = title.AddComponent<TitleScreen>();
                var so = new SerializedObject(screen); so.FindProperty("startScenePath").stringValue = Stage1Scene; so.ApplyModifiedPropertiesWithoutUndo();
                if (!EditorSceneManager.SaveScene(scene, TitleScene)) throw new System.IO.IOException($"타이틀 씬 저장 실패: {TitleScene}");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        // 빌드 씬 목록: 타이틀이 없으면 맨 앞(게임 시작 씬)에, 나머지는 없을 때만 뒤에 추가한다. 기존 항목·순서·켜짐은 바꾸지 않는다.
        private static void EnsureBuildList(string title, params string[] others)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool Has(string path) => scenes.Exists(s => s.path == path);
            bool changed = false;
            if (!Has(title)) { scenes.Insert(0, new EditorBuildSettingsScene(title, true)); changed = true; }
            foreach (var path in others) if (!Has(path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) { scenes.Add(new EditorBuildSettingsScene(path, true)); changed = true; }
            if (changed) EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
