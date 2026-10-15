using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>어드벤처씬에서 대본(ScenarioScript)의 '입장 전'·'클리어 후' 구간을 미연시 대화창으로 보여 준다.
    /// 어드벤처씬에서는 대사·지문과 클리어 기록 단계만 실행하고, 전투에만 쓰는 단계(입력 대기·감정 표시·자세 등)는 경고를 남기고 건너뛴다.
    /// 대화 중에는 플레이어 이동을 막고, 끝나면 풀고 done을 부른다. '한 번만'이 켜져 있으면 구간별로 본 기록을 남긴다.</summary>
    public static class AdventureDialogue
    {
        /// <summary>어드벤처씬에서 실행하는 단계인지(대사·지문, 클리어 기록).</summary>
        public static bool Runs(ScenarioStepType type) => type == ScenarioStepType.Line || type == ScenarioStepType.RecordClear;
        /// <summary>구간을 보여 줄 것이 있는지(대사가 있고, '한 번만'으로 이미 보지 않았음).</summary>
        public static bool HasLines(ScenarioScript script, ScenarioSection section)
        {
            if (script == null || !script.Has(section) || Seen(script, section)) return false;
            script.Range(section, out int from, out int to);
            for (int i = from; i < to; i++) if (script.Steps[i].type == ScenarioStepType.Line) return true;
            return false;
        }
        private static bool Seen(ScenarioScript script, ScenarioSection section) => script.Options.onlyOnce && ProgressStore.IsSeen(script.SeenId(section));
        /// <summary>구간을 시작한다. 보여 줄 대사가 있으면 대화창을 열고 true(끝나면 done), 없으면(빈 구간·이미 봄) 클리어 기록만 실행하고 false(done은 부르지 않음).</summary>
        public static bool Play(ScenarioScript script, ScenarioSection section, OverworldPlayer player, Action done)
        {
            if (script == null || section == ScenarioSection.Battle || !script.Has(section) || Seen(script, section)) return false;
            script.Range(section, out int from, out int to);
            var lines = new List<DialogueLine>(); var table = GameText.Scenario(script.Id);
            for (int i = from; i < to; i++)
            {
                var step = script.Steps[i];
                if (step.type == ScenarioStepType.Line) lines.Add(step.line.Localized(table, step.id));
                else if (step.type == ScenarioStepType.RecordClear)
                {
                    try { ProgressStore.MarkCleared(step.text); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                else Debug.LogWarning($"[IBIIIS] 대본 '{script.name}' {i}번 단계({step.type})는 어드벤처씬 구간에서 실행하지 않습니다(대사·지문·클리어 기록만).", script);
            }
            if (lines.Count == 0) { MarkSeen(script, section); return false; }
            player?.SetInputLocked(true);
            var ui = player != null && player.Settings != null ? player.Settings.GameUi : null;
            DialogueController.Play(ui, lines, () =>
            {
                MarkSeen(script, section);
                if (player != null) player.SetInputLocked(false);
                done?.Invoke();
            });
            return true;
        }
        /// <summary>대화 대본(기획서 7.6)을 보여 준다: 이번 버전(차례대로·무작위)의 대사·지문을 대화창으로 보이고 그동안 이동을 막는다.
        /// 끝까지 보면 대본 ID로 본 기록을 남기고(버전과 관계없이 하나) done을 부른다. 보여 줄 대사가 없으면 클리어 기록 단계만 실행하고 본 것으로 기록한 뒤 null(done은 부르지 않음).</summary>
        public static DialogueController PlayTalk(ScenarioScript script, OverworldPlayer player, Action done)
        {
            if (script == null) return null;
            if (!script.IsTalk) { Debug.LogWarning($"[IBIIIS] 대본 '{script.name}'은(는) 대화 대본이 아니어서 대화로 보여 주지 않습니다.", script); return null; }
            var steps = script.VersionSteps(DialogueProgress.NextVersion(script));
            var lines = new List<DialogueLine>(); var table = GameText.Scenario(script.Id);
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step.type == ScenarioStepType.Line) lines.Add(step.line.Localized(table, step.id));
                else if (step.type == ScenarioStepType.RecordClear)
                {
                    try { ProgressStore.MarkCleared(step.text); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                else Debug.LogWarning($"[IBIIIS] 대화 대본 '{script.name}' {i}번 단계({step.type})는 어드벤처씬에서 실행하지 않습니다(대사·지문·클리어 기록만).", script);
            }
            if (lines.Count == 0) { MarkTalkSeen(script); return null; }
            player?.SetInputLocked(true);
            var ui = player != null && player.Settings != null ? player.Settings.GameUi : null;
            return DialogueController.Play(ui, lines, () =>
            {
                MarkTalkSeen(script);
                if (player != null) player.SetInputLocked(false);
                done?.Invoke();
            });
        }
        private static void MarkTalkSeen(ScenarioScript script)
        {
            try { ProgressStore.MarkSeen(script.TalkSeenId); }
            catch (Exception e) { Debug.LogException(e); }
        }
        private static void MarkSeen(ScenarioScript script, ScenarioSection section)
        {
            if (!script.Options.onlyOnce) return;
            try { ProgressStore.MarkSeen(script.SeenId(section)); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
