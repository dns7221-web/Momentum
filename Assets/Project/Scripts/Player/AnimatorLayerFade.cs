using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 애니메이터 레이어 가중치를 <b>서서히</b> 옮긴다.
    ///
    /// <b>왜 필요한가.</b> 이 프로젝트의 동작 레이어는 전부 <c>Override</c>다 — 가중치가
    /// 1이면 아래 레이어의 포즈를 통째로 덮는다. 그래서 <c>SetLayerWeight(i, 1f)</c>를
    /// 한 프레임에 대입하면 <b>그 프레임에 몸이 다른 자세로 갈아 끼워진다.</b> 스윙으로
    /// 날아가던 중에 던지기를 누르면 스윙 자세가 사라졌다가 0.4초 뒤에 다시 나타나던
    /// 것이 이것이고, "웹스윙과 공격이 안 이어진다"의 실체다.
    ///
    /// <see cref="SwingConstraint"/>와 <see cref="GrappleZip"/>은 이미 같은 모양의
    /// <c>MoveTowards</c>를 각자 들고 있었는데, 나중에 붙은 발사·던지기·슬램 세 레이어만
    /// 대입으로 남아 있었다. 같은 네 줄이 다섯 군데로 갈라지는 대신 여기 한 곳에 둔다.
    ///
    /// <b><c>Lerp</c>가 아니라 <c>MoveTowards</c>인 이유.</b> <c>Lerp</c>는 남은 거리에
    /// 비례해 줄어들어 목표에 영원히 도달하지 않는다(0.001 같은 값이 계속 남는다).
    /// <c>MoveTowards</c>는 정해진 속도로 가서 <b>정확히 도착하고 멈추므로</b>, "다 껐다"를
    /// 판정할 필요 자체가 없다. 초당 각도·초당 미터를 다루는 다른 코드(로프 길이, 몸 회전)와
    /// 단위도 같아진다 — 8이면 "0에서 1까지 0.125초".
    /// </summary>
    public static class AnimatorLayerFade
    {
        /// <summary>초당 가중치 변화량. 8 = 0↔1에 0.125초.</summary>
        public const float DefaultSpeed = 8f;

        /// <summary>
        /// <paramref name="target"/>을 향해 한 프레임만큼 옮긴다. 매 프레임 부르는 것을
        /// 전제로 한다.
        /// </summary>
        /// <param name="speed">
        /// 0 이하면 즉시 대입한다. <b>페이드가 곤란한 경우가 실제로 있다</b> — 루트 모션을
        /// 뽑아 쓰는 레이어(그랩 앤 슬램)는 가중치가 곧 루트 모션의 크기라서, 서서히 켜면
        /// 회전량이 그만큼 모자라 180도를 다 못 돈다. 그런 레이어는 켤 때만 0을 넘겨
        /// 즉시 켜고, 끌 때는 회전이 이미 끝난 뒤라 서서히 꺼도 안전하다.
        /// </param>
        public static void To(Animator animator, int layerIndex, float target,
                              float speed = DefaultSpeed)
        {
            if (animator == null || layerIndex < 0) return;

            float current = animator.GetLayerWeight(layerIndex);
            float next = speed <= 0f
                ? target
                : Mathf.MoveTowards(current, target, speed * Time.deltaTime);

            animator.SetLayerWeight(layerIndex, next);
        }
    }
}
