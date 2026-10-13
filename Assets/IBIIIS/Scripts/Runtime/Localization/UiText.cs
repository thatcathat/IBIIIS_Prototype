namespace IBIIIS
{
    /// <summary>화면 문구 하나: 번역 표 키, 한국어 원문, 영어 처음 값. 한국어 원문은 이 코드가 기준이고, 영어는 처음 표를 만들 때만
    /// 넣으며 그 뒤에는 번역 표(CSV 가져오기)가 기준이다. 자리 표시 {이름}은 Format으로 채운다.</summary>
    public readonly struct GameString
    {
        public readonly string Key, Korean, English;
        public GameString(string key, string korean, string english) { Key = key; Korean = korean; English = english; }
        public string Text => GameText.Get(GameText.UiTable, Key, Korean);
        public string Format(params (string name, object value)[] values) => GameText.Fill(Text, values);
        public override string ToString() => Text;
    }

    /// <summary>게임 화면 문구 목록(UI 표). 문구를 추가하면 메뉴 IBIIIS > Localization > Sync Tables로 표에 반영한다.
    /// 조사(을/를 등)가 이름에 따라 바뀌는 문장은 만들지 않는다(언어마다 어순이 다르므로 이름이 들어가는 문장은 통째로 표에 둔다).</summary>
    public static class UiText
    {
        public static readonly GameString Confirm = new GameString("common.confirm", "확인", "OK");
        public static readonly GameString TitleStart = new GameString("title.start", "게임 시작", "Start Game");
        public static readonly GameString TitleLanguage = new GameString("title.language", "언어: {language}", "Language: {language}");

        public static readonly GameString HudEvasion = new GameString("hud.evasion", "회피", "Evade");
        public static readonly GameString HudUndo = new GameString("hud.undo", "되돌리기", "Undo");
        public static readonly GameString HudRestart = new GameString("hud.restart", "다시 시작", "Restart");
        public static readonly GameString HudEnemies = new GameString("hud.enemies", "남은 적 {alive}/{total}", "Enemies {alive}/{total}");
        public static readonly GameString HudActions = new GameString("hud.actions", "행동 {count}", "Moves {count}");
        public static readonly GameString HudRangesOn = new GameString("hud.ranges.on", "범위 표시 켬", "Ranges: On");
        public static readonly GameString HudRangesOff = new GameString("hud.ranges.off", "범위 표시 끔", "Ranges: Off");
        public static readonly GameString BattleControls = new GameString("battle.controls",
            "이동 {move} · 대시 {dash}+이동 · 구르기 {roll} · 대기 {wait} · 되돌리기 {undo} · 범위 {ranges} · 다시 시작 {restart}",
            "Move {move} · Dash {dash}+Move · Roll {roll} · Wait {wait} · Undo {undo} · Ranges {ranges} · Restart {restart}");

        public static readonly GameString ResultCleared = new GameString("result.cleared", "클리어!", "Clear!");
        public static readonly GameString ResultDefeated = new GameString("result.defeated", "패배", "Defeat");
        public static readonly GameString ResultCheck = new GameString("result.check", "확인 필요", "Check needed");
        public static readonly GameString ResultNoBattle = new GameString("result.no_battle", "전투 씬에 전투(Grid Map Player)가 없습니다.", "The battle scene has no battle (Grid Map Player).");
        public static readonly GameString ResultBattleFailed = new GameString("result.battle_failed", "전투를 시작하지 못했습니다. Console의 [IBIIIS] 오류(맵 설정)를 확인하세요.", "Couldn't start the battle. Check the [IBIIIS] errors in the Console.");
        public static readonly GameString ResultSaveFailed = new GameString("result.save_failed", "클리어 기록을 저장하지 못했습니다.", "Couldn't save the clear record.");
        public static readonly GameString ResultSaveRetryFailed = new GameString("result.save_retry_failed", "다시 시도했지만 실패했습니다. 확인을 누르면 저장하지 않고 미니맵으로 돌아갑니다.", "Retry failed. Press OK to return to the map without saving.");
        public static readonly GameString ResultReturnFailed = new GameString("result.return_failed", "미니맵 씬을 불러오지 못했습니다.", "Couldn't load the map scene.");

        public static readonly GameString OverworldControls = new GameString("overworld.controls",
            "미니맵 | 이동 {move} | 달리기 {dash}(누르고 있기) | 구르기 {roll} | 상호작용 {interact}",
            "Map | Move {move} | Run {dash} (hold) | Roll {roll} | Interact {interact}");
        public static readonly GameString OverworldCleared = new GameString("overworld.cleared", "클리어 {count}", "Cleared {count}");
        public static readonly GameString PromptEnter = new GameString("prompt.enter", "{stage} 입장", "Enter {stage}");
        public static readonly GameString PromptEnterCleared = new GameString("prompt.enter_cleared", "{stage} 입장 (클리어)", "Enter {stage} (cleared)");
        public static readonly GameString PromptTalk = new GameString("prompt.talk", "말 걸기", "Talk");
        public static readonly GameString PromptNext = new GameString("prompt.next", "다음", "Next");
        public static readonly GameString PromptClose = new GameString("prompt.close", "닫기", "Close");
    }
}
