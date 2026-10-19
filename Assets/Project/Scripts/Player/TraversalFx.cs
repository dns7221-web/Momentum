using System;
using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-19)
    /// <summary>
    /// 이동 연출 공통 부품 — "시야각 펀치 + 히트스톱(멈칫) + 화면 흔들림 + 속도선"을 한 번에 부른다.
    ///
    /// <b>왜 한 곳에 모으나.</b> 다이브에서 줄을 잡는 순간, 슬링샷 발사 순간, 포인트 런치 도착처럼
    /// "팡" 하는 순간이 여러 기능에 흩어져 있다. 각자 시야각·타임스케일·흔들림을 따로 만지면
    /// 서로 덮어쓰거나 세기가 제각각이 된다. 여기서는 <b>요청만 받고</b>, 실제로 화면을 바꾸는 쪽은
    /// 이미 있는 담당자에게 넘긴다.
    /// <list type="bullet">
    /// <item>시야각 → <see cref="SpeedFieldOfView"/>가 <see cref="FovPunch"/>를 듣고 펀치로 더한다.</item>
    /// <item>히트스톱·흔들림 → <see cref="HitFeedbackManager"/>(Time.timeScale·Cinemachine Impulse 담당).</item>
    /// <item>속도선 → <see cref="SpeedLines"/>가 <see cref="SpeedLineBurst"/>를 듣고 잠깐 터뜨린다.</item>
    /// </list>
    /// static 이벤트라 씬에 오브젝트를 하나 더 둘 필요가 없다. 듣는 쪽이 없으면 그냥 아무 일도 안 일어난다.
    /// </summary>
    public static class TraversalFx
    {
        /// <summary>시야각 펀치 요청(°). 양수는 확 넓어졌다가, 음수는 확 좁아졌다가 원래대로 돌아온다.</summary>
        public static event Action<float> FovPunch;

        /// <summary>속도선 터뜨리기 요청(초).</summary>
        public static event Action<float> SpeedLineBurst;

        /// <summary>
        /// 한 번에 부른다. 0인 항목은 건너뛴다.
        /// </summary>
        /// <param name="at">흔들림이 시작되는 위치(보통 플레이어).</param>
        /// <param name="fovPunch">시야각 펀치(°).</param>
        /// <param name="hitStop">멈칫 길이(실제 초). 0이면 안 멈춘다.</param>
        /// <param name="hitStopScale">멈칫하는 동안의 타임스케일(0.05 = 거의 정지).</param>
        /// <param name="shake">화면 흔들림 세기.</param>
        /// <param name="speedLines">속도선을 켜 둘 시간(초).</param>
        public static void Burst(Vector3 at, float fovPunch = 0f, float hitStop = 0f, float hitStopScale = 0.05f,
                                 float shake = 0f, float speedLines = 0f)
        {
            if (fovPunch != 0f) FovPunch?.Invoke(fovPunch);

            HitFeedbackManager feedback = HitFeedbackManager.Instance;
            if (feedback != null)
            {
                if (hitStop > 0f) feedback.PlaySlowMo(hitStopScale, hitStop);
                if (shake > 0f) feedback.PlayShake(at, shake);
            }

            if (speedLines > 0f) SpeedLineBurst?.Invoke(speedLines);
        }
    }
}
