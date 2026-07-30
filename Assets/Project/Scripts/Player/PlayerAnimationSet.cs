using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 캐릭터에 물릴 클립 모음. PlayerAnimatorBuilder가 이 값을 읽어 Animator Controller를 만든다.
    ///
    /// 클립을 직접 Animator Controller에 넣지 않고 이 에셋을 한 단계 거치는 이유는
    /// 클립을 나중에 더 나은 것으로 바꿀 때 컨트롤러를 손으로 다시 그리지 않고
    /// 이 값만 바꿔서 재생성하면 되게 하려는 것이다.
    /// </summary>
    [CreateAssetMenu(menuName = "Momentum/Player Animation Set", fileName = "PlayerAnimationSet")]
    public class PlayerAnimationSet : ScriptableObject
    {
        [Header("지상 이동 — Speed 파라미터(m/s)로 블렌드")]
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip run;

        [Tooltip("PlayerMovement의 walkSpeed와 같은 값이어야 블렌드 문턱이 어긋나지 않는다.")]
        public float walkSpeedThreshold = 4f;
        [Tooltip("PlayerMovement의 sprintSpeed와 같은 값이어야 한다.")]
        public float runSpeedThreshold = 8f;

        [Header("점프 (jump를 비우면 점프 상태 없이 이동만 만든다)")]
        [Tooltip("뛰어오르는 순간. 1회 재생 후 airLoop으로 넘어간다.")]
        public AnimationClip jump;
        [Tooltip("공중에 떠 있는 동안 반복. 비우면 jump가 끝난 뒤 그대로 멈춰 있는다.")]
        public AnimationClip airLoop;
        [Tooltip("착지. 비우면 착지하는 순간 바로 이동 상태로 돌아간다.")]
        public AnimationClip land;
    }
}
