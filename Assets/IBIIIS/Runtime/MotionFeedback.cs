using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>플레이어 이동 손맛.
    /// 1칸 이동: 웅크림·포물선 뜀·공중 늘어남 → 착지 납작함·먼지·발소리. 대시: 낮게 웅크려 진행 방향으로 기울고 잔상·출발 먼지 → 미끄러지며 멈춤.
    /// 구르기: 낮은 뜀 → 착지. 대기: 제자리 끄덕임. 입력 대기 중: 숨쉬기. 막힌 입력: 그쪽으로 부딪힘.
    /// 적: 한 칸마다 낮고 무거운 뜀 → 착지 납작함·먼지·발소리, 방향을 틀 때 짧은 눌림, 벽 앞에서 돌아설 때 벽 쪽으로 부딪힘.
    /// 행동 중 자세는 이번 행동의 진행도로만 계산해 프레임·되돌리기와 무관하게 같은 모습이 된다. 표시 전용이며 판정에 관여하지 않는다.</summary>
    public sealed class MotionFeedback : IDisposable
    {
        private sealed class EnemyMotion
        {
            public Transform Visual; public Vector3 BasePosition, BaseScale;
            public Vector2Int LastDirection; public bool MovingThisStep;
            public float LandTime = -1, TurnTime = -1, BumpTime = -1; public Vector2Int BumpDirection;
            public void Reset() { LandTime = TurnTime = BumpTime = -1; MovingThisStep = false; }
        }
        private readonly MotionFeedbackSettings settings;
        private readonly bool ownsSettings;
        private readonly Transform parent;
        private readonly Func<Vector2Int, Vector3> cellPosition;
        private readonly float cellSize;
        private readonly Camera camera;
        // 먼지·잔상·효과음은 미니맵과 공유하는 연출 부품이 맡는다.
        private readonly MotionEffects effects;
        // 이번 행동의 착지를 아직 처리하지 않았으면 true. 중간 프레임을 보지 못해도(긴 프레임) 착지를 놓치지 않는다.
        private bool landingPending;
        private PlayerAction pendingAction;
        private float landingTime = -1, landingSquash, bumpTime = -1, breathTime;
        private Vector2Int bumpDirection;
        private int ghostsSpawned;
        private readonly Dictionary<int, EnemyMotion> enemyMotions = new Dictionary<int, EnemyMotion>();
        private int enemyStepsSeen;
        // 되돌리기 등으로 적 방향이 순간 바뀐 직후에는 방향 전환 반응을 내지 않고 현재 방향만 기억한다.
        private bool resyncEnemies = true;

        public MotionFeedback(MotionFeedbackSettings settings, Transform parent, Func<Vector2Int, Vector3> cellPosition, float cellSize, Camera camera)
        {
            ownsSettings = settings == null;
            if (settings == null) { settings = ScriptableObject.CreateInstance<MotionFeedbackSettings>(); settings.hideFlags = HideFlags.HideAndDontSave; }
            this.settings = settings; this.parent = parent; this.cellPosition = cellPosition; this.cellSize = cellSize; this.camera = camera;
            effects = new MotionEffects(parent, camera);
        }
        public MotionFeedbackSettings Settings => settings;
        /// <summary>착지·부딪힘 여운이나 먼지·잔상이 남아 있으면 true. 숨쉬기는 포함하지 않는다. 입력은 막지 않는다.</summary>
        public bool IsAnimating => landingTime >= 0 || bumpTime >= 0 || effects.IsAnimating;
        public int AfterimageCount => effects.AfterimageCount;

        /// <summary>새 행동을 시작할 때 부른다. origin은 출발 칸, direction은 행동 방향. 이전 여운을 정리하고 대시·구르기 시작 소리·먼지를 낸다.</summary>
        public void BeginAction(PlayerAction action, Vector2Int origin, Vector2Int direction)
        {
            landingTime = -1; bumpTime = -1; breathTime = 0; ghostsSpawned = 0; enemyStepsSeen = 0;
            landingPending = action == PlayerAction.Move || action == PlayerAction.Dash || action == PlayerAction.Roll; pendingAction = action;
            if (!settings.Enabled) return;
            if (action == PlayerAction.Dash) { Play(settings.DashSound, settings.DashVolume); SpawnDust(origin, -direction); }
            if (action == PlayerAction.Roll) Play(settings.RollSound, settings.RollVolume);
        }
        /// <summary>갈 수 없는 방향을 눌렀을 때 그쪽으로 부딪혔다 돌아온다.</summary>
        public void Bump(Vector2Int direction)
        {
            if (!settings.Enabled || direction == Vector2Int.zero) return;
            bumpDirection = direction; bumpTime = 0; landingTime = -1; breathTime = 0;
            Play(settings.BumpSound, settings.BumpVolume);
        }
        /// <summary>매 프레임 호출한다. 현재 행동과 진행도로 플레이어 자세를 계산해 돌려준다. sprite는 잔상을 복사할 플레이어 스프라이트(없으면 잔상 생략).</summary>
        /// <summary>먼지·잔상을 흐리게 하고 다 사라지면 지운다(매 프레임, 플레이어 외형이 없어도 호출).</summary>
        public void TickEffects(float seconds) => effects.Tick(seconds);
        public MotionPose Tick(float seconds, GridSession session, PlayerAction action, Vector2Int direction, SpriteRenderer sprite = null)
        {
            if (!settings.Enabled || session == null) { landingTime = bumpTime = -1; return MotionPose.Identity; }
            if (session.IsMoving)
            {
                float p = session.Progress;
                switch (action)
                {
                    case PlayerAction.Move: return HopPose(p);
                    case PlayerAction.Roll: return HopPose(p, settings.RollHopHeight);
                    case PlayerAction.Dash: SpawnGhosts(p, sprite); return DashPose(p, direction);
                    case PlayerAction.Wait: return Squashed(settings.WaitSquash * Mathf.Sin(Mathf.PI * p));
                }
            }
            if (landingPending)
            {
                landingPending = false;
                if (pendingAction == action) Land(session.Position, action == PlayerAction.Dash ? settings.DashStopSquash : action == PlayerAction.Roll ? settings.RollLandingSquash : settings.LandingSquash, action == PlayerAction.Dash ? direction : Vector2Int.zero);
            }
            if (landingTime >= 0)
            {
                landingTime += seconds;
                if (landingTime >= settings.LandingTime) landingTime = -1;
                else return Squashed(landingSquash * Mathf.Sin(Mathf.PI * landingTime / settings.LandingTime));
            }
            if (bumpTime >= 0)
            {
                bumpTime += seconds;
                if (bumpTime >= settings.BumpTime) bumpTime = -1;
                else
                {
                    float k = Mathf.Sin(Mathf.PI * bumpTime / settings.BumpTime);
                    var pose = Squashed(settings.BumpSquash * k);
                    pose.Shift = new Vector3(bumpDirection.x, 0, bumpDirection.y).normalized * settings.BumpDistance * k;
                    return pose;
                }
            }
            if (session.Phase == BattlePhase.Waiting && !session.IsBusy) return BreathPose(breathTime += seconds);
            breathTime = 0;
            return MotionPose.Identity;
        }
        /// <summary>뜀 자세: 시작 구간에 웅크렸다가 포물선으로 뜨고, 공중에서 위아래로 늘어난다. 진행도 0과 1에서는 원래 자세.</summary>
        public MotionPose HopPose(float progress) => HopPose(progress, settings.HopHeight);
        public MotionPose HopPose(float progress, float height) => HopPose(progress, height, settings.TakeoffSquash, settings.AirStretch);
        public MotionPose HopPose(float progress, float height, float takeoffSquash, float airStretch)
            => MotionPoses.Hop(progress, height, takeoffSquash, airStretch, settings.TakeoffPortion);

        /// <summary>적 외형 손맛을 갱신한다(매 프레임). skip이 true인 적(충돌 연출 중 등)과 죽은 적은 건드리지 않는다. walkable은 벽 반사 판단에 쓴다.</summary>
        public void TickEnemies(float seconds, GridSession session, IReadOnlyList<EnemyDefinition> views, Func<int, bool> skip, Func<Vector2Int, bool> walkable)
        {
            if (session == null || views == null) return;
            bool enabled = settings.Enabled;
            int completed = session.EnemyStepsCompleted;
            bool stepEnded = completed > enemyStepsSeen; enemyStepsSeen = completed;
            bool anyLanded = false;
            for (int i = 0; i < views.Count && i < session.Enemies.Count; i++)
            {
                var view = views[i]; var state = session.Enemies[i];
                if (view == null || view.Visual == null) continue;
                if (!enemyMotions.TryGetValue(i, out var m) || m.Visual != view.Visual)
                    enemyMotions[i] = m = new EnemyMotion { Visual = view.Visual, BasePosition = view.Visual.localPosition, BaseScale = view.Visual.localScale, LastDirection = state.Direction };
                if ((skip != null && skip(i)) || !state.Alive) { m.Reset(); m.LastDirection = state.Direction; continue; }
                if (!enabled) { m.Reset(); m.LastDirection = state.Direction; Apply(m, view.transform, MotionPose.Identity); continue; }
                // 진행 중인 반응 시간을 먼저 흘린 뒤 새 반응을 감지한다(새 반응은 이번 프레임에 0에서 시작).
                if (m.TurnTime >= 0) { m.TurnTime += seconds; if (m.TurnTime >= settings.TurnTime) m.TurnTime = -1; }
                if (m.BumpTime >= 0) { m.BumpTime += seconds; if (m.BumpTime >= settings.WallBumpTime) m.BumpTime = -1; }
                // 방금 끝난 단계에서 움직였던 적은 착지한다. 긴 프레임으로 여러 단계를 건너뛰어도 한 번은 착지한다.
                if (stepEnded && m.MovingThisStep) { m.LandTime = 0; anyLanded = true; if (settings.EnemyDustScale > 0) SpawnDust(state.Position, Vector2Int.zero, settings.EnemyDustScale * cellSize); }
                if (!resyncEnemies && state.Direction != m.LastDirection)
                {
                    m.TurnTime = 0;
                    var before = session.IsEnemiesMoving ? state.StepFrom : state.Position;
                    if (state.Direction == -m.LastDirection && walkable != null && !walkable(before + m.LastDirection)) { m.BumpTime = 0; m.BumpDirection = m.LastDirection; }
                }
                m.LastDirection = state.Direction;
                m.MovingThisStep = session.IsEnemiesMoving && state.StepFrom != state.StepTo;

                MotionPose pose;
                if (m.MovingThisStep) { pose = HopPose(session.EnemyProgress, settings.EnemyHopHeight, settings.EnemyTakeoffSquash, settings.EnemyAirStretch); m.LandTime = -1; }
                else if (m.LandTime >= 0)
                {
                    m.LandTime += seconds;
                    if (m.LandTime >= settings.EnemyLandingTime) { m.LandTime = -1; pose = MotionPose.Identity; }
                    else pose = Squashed(settings.EnemyLandingSquash * Mathf.Sin(Mathf.PI * m.LandTime / settings.EnemyLandingTime));
                }
                else pose = MotionPose.Identity;
                if (m.TurnTime >= 0)
                {
                    float k = Mathf.Sin(Mathf.PI * m.TurnTime / settings.TurnTime);
                    pose.Squash = Vector2.Scale(pose.Squash, new Vector2(1 + settings.TurnSquash * k, 1 - settings.TurnSquash * k));
                }
                if (m.BumpTime >= 0)
                {
                    float k = Mathf.Sin(Mathf.PI * m.BumpTime / settings.WallBumpTime);
                    pose.Shift += new Vector3(m.BumpDirection.x, 0, m.BumpDirection.y) * settings.WallBumpDistance * k;
                }
                Apply(m, view.transform, pose);
            }
            resyncEnemies = false;
            if (anyLanded) Play(settings.EnemyFootstepSound, settings.EnemyFootstepVolume);
        }
        // 적 루트는 진행 방향으로 돌아가 있으므로 맵 기준 밀림을 루트 기준으로 바꿔 더한다. 뜸은 위쪽, 납작함은 발밑 기준 크기 배율.
        private static void Apply(EnemyMotion m, Transform root, MotionPose pose)
        {
            var shift = root != null ? Quaternion.Inverse(root.localRotation) * pose.Shift : pose.Shift;
            m.Visual.localPosition = m.BasePosition + Vector3.up * pose.Lift + shift;
            m.Visual.localScale = new Vector3(m.BaseScale.x * pose.Squash.x, m.BaseScale.y * pose.Squash.y, m.BaseScale.z);
        }
        /// <summary>대시 자세: 시작·끝 15% 구간에서 부드럽게 들어갔다 빠지며, 그 사이 낮게 웅크리고 좌우 대시면 진행 방향으로 기운다.</summary>
        public MotionPose DashPose(float progress, Vector2Int direction) => MotionPoses.Dash(progress, direction.x, settings.DashSquash, settings.DashLean);
        /// <summary>숨쉬기: 주기마다 위아래로 살짝 늘었다 줄어든다. 시간 0에서 원래 자세.</summary>
        public MotionPose BreathPose(float time) => MotionPoses.Breath(time, settings.BreathAmount, settings.BreathPeriod);
        private static MotionPose Squashed(float amount) => MotionPoses.Squashed(amount);

        private void Land(Vector2Int cell, float squash, Vector2Int skid)
        {
            landingTime = 0; landingSquash = squash; bumpTime = -1;
            Play(settings.FootstepSound, settings.FootstepVolume);
            if (skid != Vector2Int.zero) SpawnDust(cell, skid); else SpawnDust(cell, Vector2Int.zero);
        }
        // 먼지 두 덩이. away가 0이면 화면 좌우로, 아니면 그 방향(맵 기준) 양옆으로 퍼진다.
        private void SpawnDust(Vector2Int cell, Vector2Int away) => SpawnDust(cell, away, settings.DustScale * cellSize);
        private void SpawnDust(Vector2Int cell, Vector2Int away, float scale)
            => effects.SpawnDust(settings.LandingDust, cellPosition(cell), new Vector3(away.x, 0, away.y), scale, settings.DustTime, cellSize);
        // 이동 구간을 (잔상 수 + 1)등분한 지점을 지날 때마다 현재 스프라이트를 복사해 남긴다. 긴 프레임이면 지난 지점만큼 한꺼번에 남긴다.
        private void SpawnGhosts(float progress, SpriteRenderer sprite)
        {
            int count = settings.AfterimageCount;
            if (sprite == null || parent == null || count <= 0) return;
            while (ghostsSpawned < count && progress >= (ghostsSpawned + 1f) / (count + 1))
            {
                ghostsSpawned++;
                effects.SpawnAfterimage(sprite, settings.AfterimageColor, settings.AfterimageTime);
            }
        }
        private void Play(AudioClip clip, float volume) => effects.Play(clip, volume);
        /// <summary>여운·먼지·잔상을 모두 즉시 정리한다(되돌리기·재시작).</summary>
        public void Clear()
        {
            effects.Clear(); landingTime = bumpTime = -1; landingPending = false; breathTime = 0; ghostsSpawned = 0;
            foreach (var m in enemyMotions.Values) { m.Reset(); if (m.Visual != null) { m.Visual.localPosition = m.BasePosition; m.Visual.localScale = m.BaseScale; } }
            resyncEnemies = true; enemyStepsSeen = 0;
        }
        public void Dispose()
        {
            Clear();
            effects.Dispose();
            if (ownsSettings && settings != null) MotionEffects.Release(settings);
        }
    }
}
