using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵 대시·구르기 한 번의 진행. 정한 방향으로 정한 거리를 정한 시간 동안 나아간다.
    /// easeOut 0이면 일정한 속도, 1이면 처음에 가장 빠르고(평균의 2배) 끝에 멈추도록 감속한다. 위치를 진행 비율에서 바로 계산하므로 프레임을 어떻게 나눠도 총 이동 거리는 같다.
    /// 끝나거나 막혀서 멈추면 회복 시간(아무 입력도 받지 않음) 뒤 쿨다운이 시작된다(대시·구르기 공유). 실제 이동·충돌은 호출하는 쪽(CharacterController)이 한다.</summary>
    public sealed class OverworldEvade
    {
        private float elapsed, duration, distance, cooldownLeft, cooldown, easeOut, recovery, recoveryLeft;
        /// <summary>진행 중인 동작(Dash/Roll). 없으면 Wait.</summary>
        public PlayerAction Action { get; private set; } = PlayerAction.Wait;
        public bool Active => Action == PlayerAction.Dash || Action == PlayerAction.Roll;
        /// <summary>바닥 평면의 단위 방향.</summary>
        public Vector3 Direction { get; private set; }
        public float Progress => Active ? Mathf.Clamp01(elapsed / duration) : 0;
        /// <summary>정한 시간을 다 채웠으면 true. 호출하는 쪽이 마무리(Finish)한다.</summary>
        public bool ReachedEnd => Active && elapsed >= duration;
        public float CooldownLeft => cooldownLeft;
        /// <summary>동작이 끝난 뒤 회복 중이면 true. 이 동안 이동·대시·구르기·상호작용 입력을 받지 않는다.</summary>
        public bool Recovering => !Active && recoveryLeft > 0;
        public float RecoveryLeft => recoveryLeft;
        public bool CanStart => !Active && recoveryLeft <= 0 && cooldownLeft <= 0;
        /// <param name="easeOut">0 = 일정한 속도, 1 = 처음 빠르고 끝에 멈춤(그 사이 값은 섞음).</param>
        /// <param name="recovery">끝난 뒤 움직일 수 없는 시간(초). 쿨다운은 회복이 끝난 뒤부터 센다.</param>
        public bool TryStart(PlayerAction action, Vector3 direction, float distance, float duration, float cooldown, float easeOut = 0, float recovery = 0)
        {
            direction.y = 0;
            if (!CanStart || (action != PlayerAction.Dash && action != PlayerAction.Roll) || direction.sqrMagnitude < 1e-8f || distance <= 0 || duration <= 0) return false;
            Action = action; Direction = direction.normalized; this.distance = distance; this.duration = duration; this.cooldown = Mathf.Max(0, cooldown); elapsed = 0;
            this.easeOut = Mathf.Clamp01(easeOut); this.recovery = Mathf.Max(0, recovery);
            return true;
        }
        /// <summary>진행 비율 t(0~1)에서 나아간 거리 비율. 감속 곡선은 1-(1-t)², 시작 속도가 평균의 2배이고 끝 속도는 0.</summary>
        public static float Covered(float t, float easeOut)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(t, 1 - (1 - t) * (1 - t), Mathf.Clamp01(easeOut));
        }
        /// <summary>시간을 진행하고 이번에 나아갈 이동량을 돌려준다.</summary>
        public Vector3 Advance(float seconds)
        {
            if (!Active || seconds <= 0) return Vector3.zero;
            float before = Covered(elapsed / duration, easeOut);
            elapsed = Mathf.Min(duration, elapsed + seconds);
            return Direction * (distance * (Covered(elapsed / duration, easeOut) - before));
        }
        /// <summary>동작을 끝내고(막혀서 일찍 끝날 때 포함) 쿨다운을 시작한다.</summary>
        public void Finish()
        {
            if (!Active) return;
            Action = PlayerAction.Wait; elapsed = 0; recoveryLeft = recovery; cooldownLeft = cooldown;
        }
        /// <summary>회복 시간을 먼저 줄이고, 회복이 끝나면 남은 시간으로 쿨다운을 줄인다(동작하지 않는 동안 매 프레임).</summary>
        public void TickCooldown(float seconds)
        {
            if (Active || seconds <= 0) return;
            float used = Mathf.Min(seconds, recoveryLeft); recoveryLeft -= used; seconds -= used;
            if (seconds > 0 && cooldownLeft > 0) cooldownLeft = Mathf.Max(0, cooldownLeft - seconds);
        }
        /// <summary>진행·회복·쿨다운을 모두 지운다(순간 이동 등).</summary>
        public void Reset() { Action = PlayerAction.Wait; elapsed = 0; cooldownLeft = recoveryLeft = 0; }
    }
}
