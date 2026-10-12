using System.Collections.Generic;

namespace IBIIIS
{
    /// <summary>표시가 끝나는 방식.</summary>
    public enum ScenarioSpanEnd
    {
        /// <summary>끝 단계(대사)를 넘길 때.</summary>
        LineDone,
        /// <summary>끝 단계(입력 대기)에서 플레이어가 행동을 시작할 때.</summary>
        NextAction,
        /// <summary>정한 시간(초)이 지날 때. 대본 단계와 무관하게 실제 시간으로 끝난다.</summary>
        Seconds,
        /// <summary>같은 대상의 다음 표시로 바뀔 때(끝 단계 다음 단계에서 바뀜).</summary>
        Replaced,
        /// <summary>대본이 끝나거나 씬이 바뀔 때까지.</summary>
        ScriptEnd,
        /// <summary>표시 끝 단계(Marker)에서. 끝 단계 바로 앞의 기다리는 단계가 끝날 때 사라진다.</summary>
        EndStep,
    }
    /// <summary>자세·감정 표시 하나가 보이는 범위(단계 번호, 양끝 포함).</summary>
    public readonly struct ScenarioSpan
    {
        public readonly int Start, End;
        public readonly ScenarioSpanEnd EndKind;
        /// <summary>이 표시를 끝내는 표시 끝 단계 번호(없으면 -1).</summary>
        public readonly int Marker;
        public ScenarioSpan(int start, int end, ScenarioSpanEnd endKind, int marker = -1) { Start = start; End = end; EndKind = endKind; Marker = marker; }
        public bool Covers(int index) => index >= Start && index <= End;
    }
    /// <summary>대본을 장면(노드)으로 나누고 자세·감정 표시가 어느 단계부터 어느 단계까지 보이는지 계산한다(데이터만 읽음).
    /// 끝나는 규칙은 ScenarioDirector의 진행과 같다: "다음 대사까지"는 시작 뒤 처음 나오는 대사를 넘길 때, "다음 행동까지"는 시작 뒤 처음 나오는
    /// 행동 입력 대기에서 행동을 시작할 때 끝나고, 같은 대상의 새 표시가 오면 바뀐다. 실제 진행과 같은지는 자동 테스트로 확인한다.</summary>
    public static class ScenarioTimeline
    {
        /// <summary>플레이어 행동을 받는 입력 대기 단계인지.</summary>
        public static bool AcceptsAction(ScenarioStepType type)
            => type == ScenarioStepType.WaitAction || type == ScenarioStepType.WaitFreeMove || type == ScenarioStepType.WaitReachCell
            || type == ScenarioStepType.WaitAllDirections || type == ScenarioStepType.FreePlay;
        /// <summary>진행기가 기다리지 않고 바로 다음 단계로 넘기는 단계인지. 바로 뒤의 기다리는 단계(대사·입력 등)와 같은 순간에 일어난다.</summary>
        public static bool IsInstant(ScenarioStepType type)
            => type == ScenarioStepType.Emote || type == ScenarioStepType.Pose || type == ScenarioStepType.SetActive
            || type == ScenarioStepType.SpawnEnemy || type == ScenarioStepType.RecordClear || type == ScenarioStepType.EndDisplay;
        /// <summary>같은 순간에 일어나는 단계 묶음(세로줄) 목록(첫 단계, 마지막 단계). 바로 넘어가는 단계는 다음 기다리는 단계와 묶이고,
        /// 뒤에 기다리는 단계가 없으면(대본 끝·장면 끝) 그 단계들끼리 묶인다. 묶음은 장면을 넘지 않는다.</summary>
        public static List<(int start, int end)> Moments(IReadOnlyList<ScenarioStep> steps, int from, int to)
        {
            var moments = new List<(int, int)>(); int start = from;
            for (int i = from; i <= to; i++)
                if (!IsInstant(steps[i].type)) { moments.Add((start, i)); start = i + 1; }
            if (start <= to) moments.Add((start, to));
            return moments;
        }
        /// <summary>장면을 끊는 단계인지(입력을 기다리거나 씬을 바꿈). 이 단계까지가 한 장면이다.</summary>
        public static bool EndsScene(ScenarioStepType type)
            => AcceptsAction(type) || type == ScenarioStepType.WaitClickCell || type == ScenarioStepType.NameInput || type == ScenarioStepType.LoadScene;
        /// <summary>장면 목록(첫 단계, 마지막 단계). 장면을 끊는 단계가 그 장면의 마지막이다.</summary>
        public static List<(int start, int end)> Scenes(IReadOnlyList<ScenarioStep> steps)
        {
            var scenes = new List<(int, int)>(); int start = 0;
            for (int i = 0; i < steps.Count; i++)
                if (EndsScene(steps[i].type)) { scenes.Add((start, i)); start = i + 1; }
            if (start < steps.Count) scenes.Add((start, steps.Count - 1));
            return scenes;
        }
        /// <summary>자세·감정 표시 단계의 보이는 범위. 표시 단계가 아니면 null.</summary>
        public static ScenarioSpan? SpanOf(IReadOnlyList<ScenarioStep> steps, int index)
        {
            var step = steps[index];
            if (step.type != ScenarioStepType.Emote && step.type != ScenarioStepType.Pose) return null;
            if (step.type == ScenarioStepType.Pose && step.sprite == null) return null; // 그림이 없으면 진행기가 건너뛴다
            if (step.untilEnd)
            {
                for (int i = index + 1; i < steps.Count; i++)
                {
                    var next = steps[i];
                    if (Replaces(step, next)) return new ScenarioSpan(index, LastWaitBefore(steps, index, i), ScenarioSpanEnd.Replaced);
                    if (Ends(step, next)) return new ScenarioSpan(index, LastWaitBefore(steps, index, i), ScenarioSpanEnd.EndStep, i);
                    if (next.type == ScenarioStepType.LoadScene) return new ScenarioSpan(index, i, ScenarioSpanEnd.ScriptEnd);
                }
                return new ScenarioSpan(index, steps.Count - 1, ScenarioSpanEnd.ScriptEnd);
            }
            bool untilAction = step.type == ScenarioStepType.Pose && step.untilAction;
            if (!untilAction && step.seconds > 0) return new ScenarioSpan(index, index, ScenarioSpanEnd.Seconds);
            for (int i = index + 1; i < steps.Count; i++)
            {
                var next = steps[i];
                if (Replaces(step, next)) return new ScenarioSpan(index, LastWaitBefore(steps, index, i), ScenarioSpanEnd.Replaced);
                if (Ends(step, next)) return new ScenarioSpan(index, LastWaitBefore(steps, index, i), ScenarioSpanEnd.EndStep, i); // 진행기는 표시 끝 단계에서 어떤 표시든 끝낸다
                if (untilAction ? AcceptsAction(next.type) : next.type == ScenarioStepType.Line)
                    return new ScenarioSpan(index, i, untilAction ? ScenarioSpanEnd.NextAction : ScenarioSpanEnd.LineDone);
                if (next.type == ScenarioStepType.LoadScene) return new ScenarioSpan(index, i, ScenarioSpanEnd.ScriptEnd);
            }
            return new ScenarioSpan(index, steps.Count - 1, ScenarioSpanEnd.ScriptEnd);
        }
        /// <summary>표시 끝 단계 marker가 start의 표시를 끝내는지(종류와, 감정 표시면 대상이 같음).</summary>
        public static bool Ends(ScenarioStep start, ScenarioStep marker)
        {
            if (marker.type != ScenarioStepType.EndDisplay) return false;
            if (start.type == ScenarioStepType.Pose) return marker.display == ScenarioDisplay.Pose;
            return start.type == ScenarioStepType.Emote && marker.display == ScenarioDisplay.Emote && marker.target == start.target;
        }
        // 끝 단계 바로 앞에서 마지막으로 기다린 단계(그 단계가 끝날 때 표시가 사라짐). 시작 뒤에 기다린 단계가 없으면 시작 단계.
        private static int LastWaitBefore(IReadOnlyList<ScenarioStep> steps, int start, int marker)
        {
            for (int i = marker - 1; i > start; i--) if (!IsInstant(steps[i].type)) return i;
            return start;
        }
        // 감정 표시는 같은 대상의 새 감정 표시가, 자세는 새 자세(그림이 있는)가 이전 것을 바꾼다.
        private static bool Replaces(ScenarioStep current, ScenarioStep next)
        {
            if (next.type != current.type) return false;
            return current.type == ScenarioStepType.Emote ? next.target == current.target : next.sprite != null;
        }
    }
}
