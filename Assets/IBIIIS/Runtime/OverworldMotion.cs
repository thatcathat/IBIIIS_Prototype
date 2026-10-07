using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵 플레이어의 걷기 손맛. 연속 이동이라 전투의 행동 진행도 대신 "걸은 거리"로 걸음 주기를 센다.
    /// 걸음마다 낮게 뛰고 발소리, 몇 걸음마다 먼지. 멈추면 착지처럼 납작해지고, 멈춰 있으면 숨쉬기, 막히면 그쪽으로 부딪힘.
    /// 같은 경로를 어떤 프레임으로 나눠 걸어도 같은 걸음 수·자세가 된다. 표시 전용이며 이동·충돌·상호작용에 관여하지 않는다.</summary>
    public sealed class OverworldMotion : IDisposable
    {
        // 의도한 이동량에 비해 실제 이동이 이 비율보다 작으면 막힌 것으로 본다.
        private const float BlockedRatio = .2f;
        private readonly MotionFeedbackSettings settings;
        private readonly bool ownsSettings;
        private readonly OverworldSettings overworld;
        private readonly MotionEffects effects;
        private float strideTravel, landingTime = -1, landingSquash, bumpTime = -1, breathTime;
        private Vector3 bumpDirection;
        private bool walking, blockedLatched;
        /// <summary>지금까지 걸은 걸음 수(Reset 전까지).</summary>
        public int Steps { get; private set; }
        public int DustCount => effects.DustCount;
        public bool IsWalking => walking;
        /// <summary>이번 걸음에서 걸은 비율(0~1).</summary>
        public float StrideProgress => strideTravel / Stride;
        private float Stride => overworld != null ? overworld.StrideLength : .9f;
        private float HopScale => overworld != null ? overworld.WalkHopScale : .5f;
        private bool Enabled => settings.Enabled;
        /// <param name="effectsParent">먼지·효과음 오브젝트를 둘 부모(월드 기준). null이면 먼지·소리 없이 자세만 계산한다.</param>
        public OverworldMotion(MotionFeedbackSettings settings, OverworldSettings overworld, Transform effectsParent, Camera camera)
        {
            ownsSettings = settings == null;
            if (settings == null) { settings = ScriptableObject.CreateInstance<MotionFeedbackSettings>(); settings.hideFlags = HideFlags.HideAndDontSave; }
            this.settings = settings; this.overworld = overworld;
            effects = new MotionEffects(effectsParent, camera);
        }
        /// <summary>매 프레임 호출한다. position은 이동 후 발밑(효과 부모 기준), intended는 입력으로 의도한 이번 프레임 이동량, actual은 실제 이동량(수평).
        /// 돌려준 자세를 PlayerVisual.SetPose에 넘긴다.</summary>
        public MotionPose Tick(float seconds, Vector3 position, Vector3 intended, Vector3 actual)
        {
            effects.Tick(seconds);
            if (!Enabled) { walking = false; landingTime = bumpTime = -1; strideTravel = 0; return MotionPose.Identity; }
            intended.y = 0; actual.y = 0;
            bool pressing = intended.sqrMagnitude > 1e-10f;
            float moved = actual.magnitude;
            bool blocked = pressing && moved < intended.magnitude * BlockedRatio;
            if (!pressing) blockedLatched = false;
            if (pressing && !blocked)
            {
                blockedLatched = false;
                if (!walking) { walking = true; strideTravel = 0; landingTime = bumpTime = -1; breathTime = 0; }
                strideTravel += moved;
                while (strideTravel >= Stride) { strideTravel -= Stride; Step(position, actual); }
                float k = HopScale;
                return MotionPoses.Hop(StrideProgress, settings.HopHeight * k, settings.TakeoffSquash * k, settings.AirStretch * k, settings.TakeoffPortion);
            }
            if (walking) Stop(position);
            if (blocked && !blockedLatched && (overworld == null || overworld.WallBump))
            {
                blockedLatched = true; bumpTime = 0; bumpDirection = intended.normalized; landingTime = -1; breathTime = 0;
                effects.Play(settings.BumpSound, settings.BumpVolume);
            }
            if (landingTime >= 0)
            {
                landingTime += seconds;
                if (landingTime >= settings.LandingTime) landingTime = -1;
                else return MotionPoses.Squashed(landingSquash * Mathf.Sin(Mathf.PI * landingTime / settings.LandingTime));
            }
            if (bumpTime >= 0)
            {
                bumpTime += seconds;
                if (bumpTime >= settings.BumpTime) bumpTime = -1;
                else
                {
                    float k = Mathf.Sin(Mathf.PI * bumpTime / settings.BumpTime);
                    var pose = MotionPoses.Squashed(settings.BumpSquash * k);
                    pose.Shift = bumpDirection * settings.BumpDistance * k;
                    return pose;
                }
            }
            if (overworld != null && !overworld.Breathing) { breathTime = 0; return MotionPose.Identity; }
            return MotionPoses.Breath(breathTime += seconds, settings.BreathAmount, settings.BreathPeriod);
        }
        private void Step(Vector3 position, Vector3 heading)
        {
            Steps++;
            if (overworld == null || overworld.Footsteps) effects.Play(settings.FootstepSound, settings.FootstepVolume);
            int every = overworld != null ? overworld.DustEverySteps : 3;
            if (every > 0 && Steps % every == 0) effects.SpawnDust(settings.LandingDust, position, -heading, settings.DustScale * .7f, settings.DustTime, 1);
        }
        private void Stop(Vector3 position)
        {
            walking = false; strideTravel = 0; breathTime = 0;
            float scale = overworld != null ? overworld.StopSquashScale : .6f;
            if (scale <= 0) return;
            landingTime = 0; landingSquash = settings.LandingSquash * scale;
            effects.Play(settings.FootstepSound, settings.FootstepVolume);
            effects.SpawnDust(settings.LandingDust, position, Vector3.zero, settings.DustScale * scale, settings.DustTime, 1);
        }
        /// <summary>위치를 순간 이동했을 때(전투에서 돌아옴 등) 진행 중인 반응을 지운다.</summary>
        public void Reset()
        {
            effects.Clear(); walking = blockedLatched = false; strideTravel = 0; landingTime = bumpTime = -1; breathTime = 0; Steps = 0;
        }
        public void Dispose()
        {
            effects.Dispose();
            if (ownsSettings && settings != null) MotionEffects.Release(settings);
        }
    }
}
