using System;
using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 — 인섬니악 방향 스윙 카메라 2단계 (2026-10-13)
    /// <summary>
    /// <see cref="TraversalCameraDirector"/>가 쓰는 카메라 튜닝 값 묶음(ScriptableObject 에셋).
    ///
    /// <b>왜 에셋으로 뺐는가.</b> 카메라 느낌은 숫자를 수십 번 바꿔 가며 맞추는 일이다. 값을 코드 대신
    /// 에셋에 두면 다시 컴파일하지 않고 <b>플레이 중에도</b> 바로 바꿔 볼 수 있다(플레이를 멈춰도
    /// 에셋 값은 남는다). 또 "땅 카메라", "느린 스윙 카메라" 같은 프로필을 여러 개 만들어 갈아 끼울 수 있다.
    ///
    /// 만드는 법: Project 창 우클릭 › Create › Momentum › Camera Feel Profile.
    /// 필드의 기본값이 그대로 들어간 에셋이 생긴다.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraFeelProfile", menuName = "Momentum/Camera Feel Profile")]
    public class CameraFeelProfile : ScriptableObject
    {
        /// <summary>
        /// 상황(지상/스윙/공중/잽) 하나의 기본 카메라 값.
        /// 아래의 스윙 구간·속도·높이 보정은 이 기본값 위에 더하거나 곱한다.
        /// </summary>
        [Serializable]
        public struct StateSettings
        {
            [Tooltip("거리 배율. Orbital Follow의 Radius(기준 거리)에 곱한다. 1이면 기준 거리 그대로.")]
            public float distanceScale;

            [Tooltip("세로 각도(°). 클수록 위에서 내려다본다. Orbital Follow의 Vertical Axis Center로 들어가고, " +
                     "실제로 그 각도까지 도는 건 Vertical Axis의 Recentering이 한다.")]
            public float pitch;

            [Tooltip("세로 위치 댐핑(초). 클수록 카메라가 캐릭터의 오르내림을 늦게 따라가서 화면(수평선)이 덜 출렁인다.")]
            [Min(0f)] public float verticalDamping;

            public StateSettings(float distanceScale, float pitch, float verticalDamping)
            {
                this.distanceScale = distanceScale;
                this.pitch = pitch;
                this.verticalDamping = verticalDamping;
            }
        }

        [Header("상황별 기본값")]
        [Tooltip("땅 위. 지금 씬 값(세로 15°, 세로 댐핑 0.15)과 같게 시작한다.")]
        public StateSettings ground = new StateSettings(1f, 15f, 0.15f);

        [Tooltip("웹에 매달려 스윙 중. 세로 댐핑을 크게 둬서 진자의 오르내림을 카메라가 절반쯤만 따라가게 한다(R2).")]
        public StateSettings swing = new StateSettings(1f, 12f, 0.6f);

        [Tooltip("놓은 뒤 떠오르거나 떨어지는 중. 조금 뒤로 빠져 다음 건물이 보이게 한다.")]
        public StateSettings air = new StateSettings(1.1f, 15f, 0.3f);

        [Tooltip("웹 잽(Space) 또는 슬링샷 발사 중. 빠른 직선 비행이라 조금 멀리, 낮게 본다.")]
        public StateSettings zip = new StateSettings(1.15f, 10f, 0.2f);

        [Header("스윙 구간 (R3: 최저점은 낮고 가깝게, 떠오를 땐 뒤로 빠지며 내려다봄)")]
        [Tooltip("로프가 수직에서 이 각도(°) 안쪽이면 '최저점 근처'로 본다. 수직일 때 최저점 효과가 가장 크다.")]
        [Range(10f, 80f)] public float swingBottomAngle = 40f;

        [Tooltip("최저점에서의 거리 배율(곱). 1보다 작으면 가까워져 속도감이 커진다.")]
        public float swingBottomDistance = 0.9f;

        [Tooltip("최저점에서 더할 세로 각도(°). 음수면 낮아져 땅이 스쳐 지나가는 게 보인다.")]
        public float swingBottomPitch = -4f;

        [Tooltip("위로 이 속도(m/s)일 때 '떠오름' 효과가 최대가 된다.")]
        [Min(0.1f)] public float risingSpeedForMax = 10f;

        [Tooltip("떠오를 때의 거리 배율(곱). 1보다 크면 뒤로 빠진다.")]
        public float risingDistance = 1.15f;

        [Tooltip("떠오를 때 더할 세로 각도(°). 양수면 내려다봐서 다음 착지 지점이 보인다.")]
        public float risingPitch = 8f;

        [Header("속도 → 거리 (R4)")]
        [Tooltip("가로: 수평 속도(m/s), 세로: 거리 배율. 빠를수록 멀어진다. FOV는 SpeedFieldOfView가 따로 넓힌다.")]
        public AnimationCurve speedToDistance = AnimationCurve.EaseInOut(0f, 1f, 25f, 1.25f);

        [Header("높이·낙하 → 내려다보기 (R5)")]
        [Tooltip("발밑 지면(지붕 포함)을 찾는 최대 거리(m). 이보다 높으면 이 높이로 친다.")]
        [Min(1f)] public float heightProbeDistance = 60f;

        [Tooltip("가로: 발밑 지면까지 높이(m), 세로: 더할 세로 각도(°). 높을수록 길이 보이게 숙인다. 땅 위에서는 쓰지 않는다.")]
        public AnimationCurve heightToPitch = AnimationCurve.EaseInOut(0f, 0f, 30f, 8f);

        [Tooltip("가로: 아래로 떨어지는 속도(m/s), 세로: 더할 세로 각도(°). 다이브하면 크게 내려다본다. 공중일 때만 쓴다.")]
        public AnimationCurve fallSpeedToPitch = AnimationCurve.EaseInOut(5f, 0f, 25f, 20f);

        [Header("부드럽게")]
        [Tooltip("거리가 목표에 다가가는 시간(초). Mathf.SmoothDamp의 smoothTime.")]
        [Min(0.01f)] public float distanceSmoothTime = 0.6f;

        [Tooltip("상황이 바뀔 때 세로 댐핑이 새 값으로 넘어가는 시간(초). 0에 가까우면 툭 바뀐다.")]
        [Min(0.01f)] public float dampingBlendTime = 0.3f;

        // 추가 생성 — 벽 근처 쿠션 (2026-10-13 실험: 벽에 박을 때 카메라 급정거가 어지러움의 원인)
        [Header("벽 근처 쿠션 (벽에 박을 때 카메라가 같이 급정거하지 않게)")]
        [Tooltip("벽 근처·충돌 직후의 가로(X·Z) 위치 댐핑(초). 평소 값은 Orbital Follow 인스펙터 값(0.1)을 그대로 쓴다. " +
                 "실험에서 0.6이 괜찮았다.")]
        [Min(0f)] public float wallHorizontalDamping = 0.6f;

        [Tooltip("플레이어 몸 중심에서 이 반지름(m) 안에 건물이 있으면 '벽 근처'로 본다. " +
                 "캡슐 반지름(0.25)보다 넉넉해야 부딪히기 직전에 미리 켜진다. 20 m/s면 1 m는 약 0.05초 전이다.")]
        [Min(0.3f)] public float wallCheckRadius = 1f;

        [Tooltip("벽에서 떨어진 뒤 평소 댐핑으로 돌아가는 시간(초). 너무 짧으면 떨어지는 순간 카메라가 휙 따라붙는다.")]
        [Min(0.01f)] public float wallReleaseTime = 0.8f;

        /// <summary>
        /// 상황에 맞는 기본값을 돌려준다. 모르는 상황은 땅 값으로 둔다(상황을 새로 만들면 여기에 한 줄 추가).
        /// </summary>
        public StateSettings Get(TraversalCameraDirector.CameraState state) => state switch
        {
            TraversalCameraDirector.CameraState.Swing => swing,
            TraversalCameraDirector.CameraState.Air => air,
            TraversalCameraDirector.CameraState.Zip => zip,
            _ => ground,
        };
    }
}
