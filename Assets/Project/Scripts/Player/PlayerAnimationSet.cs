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
        [Tooltip("Jump 버튼으로 뛰어오르는 순간. 의도적인 짧은 점프라 공중 루프 없이 " +
                 "Grounded가 참이 되는 대로 곧장 이동 상태로 돌아간다 — 아래 낙하 사슬과는 별개다.")]
        public AnimationClip jump;

        [Header("낙하 (버튼 없이 바닥이 사라졌을 때 — 옥상에서 발을 헛디디거나 뛰어내릴 때)")]
        [Tooltip("공중이지만 <b>아직 낙하로 확정되지 않은</b> 구간. Falling은 일정 시간·속도 " +
                 "문턱을 넘어야 켜지는데, 그 전까지는 기본 이동 블렌드 트리(Idle/Walk/Run)가 " +
                 "그대로 남아 있어 공중에서 멀뚱히 서 있는 것처럼 보인다. 스윙을 놓은 직후가 " +
                 "대표적인 예 — 비우면 그 구간이 다시 빈 채로 남는다.")]
        public AnimationClip airIdle;
        [Tooltip("낙하가 시작되는 순간. 비우면 곧장 airLoop으로 들어간다.")]
        public AnimationClip diveStart;
        [Tooltip("공중에 떠 있는 동안 반복. 짧은 점프에는 안 쓰이고 이 낙하 사슬에서만 쓰인다.")]
        public AnimationClip airLoop;
        [Tooltip("착지. 비우면 착지하는 순간 바로 이동 상태로 돌아간다.")]
        public AnimationClip land;

        [Header("스윙 (둘 다 비우면 스윙 레이어를 만들지 않는다)")]
        [Tooltip("로프에 매달려 도는 동안. 감고 있지 않을 때 반복 재생된다.")]
        public AnimationClip swingLoop;
        [Tooltip("로프를 당겨 몸이 앵커 쪽으로 끌려갈 때. 없으면 감는 동안에도 swingLoop이 유지된다.")]
        public AnimationClip reelLoop;
        [Tooltip("앵커에 붙는 순간 한 번 재생. 없으면 곧장 swingLoop으로 들어간다 — " +
                 "붙는 순간이 뚝 끊겨 보이던 문제(README 기록)를 이 클립이 메운다.")]
        public AnimationClip startSwing;
        [Tooltip("로프를 놓는 순간 한 번 재생. 없으면 스윙 레이어가 가중치만 서서히 " +
                 "내려가며 기본 레이어(낙하/이동)로 바로 넘어간다.")]
        public AnimationClip swingLand;

        [Header("갈고리 발사 (좌클릭 순간, 팔만) — 비우면 발사 레이어를 만들지 않는다")]
        [Tooltip("총을 들어 쏘는 순간 한 번 재생. 걷기·점프·스윙 위에 팔만 얹히므로, " +
                 "몸 전체가 아니라 오른팔·상체 정도만 움직이는 클립이어야 한다.")]
        public AnimationClip grappleFire;
        [Tooltip("위 클립에서 실제로 움직일 부위만 켠 Avatar Mask. 다리까지 포함하면 " +
                 "걷는 중에 쏠 때 다리가 그 순간 멈추거나 어긋난다. 보통 오른팔(+상체) 정도만.")]
        public AvatarMask grappleFireMask;
    }
}
