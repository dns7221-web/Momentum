using System.Collections.Generic;
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

        [Tooltip("공중 자세의 <b>추가 변형</b>. 비워두면 위 airLoop 하나만 쓴다.\n\n" +
                 "채우면 공중에 뜰 때마다 이 중 하나를 무작위로 골라 재생한다 — 같은 상황에서도 " +
                 "자세가 달라져 반복적으로 안 보인다. 낙하 <b>도중에</b> 바뀌지는 않는다. " +
                 "한 번 고르면 착지할 때까지 그 자세를 유지한다.")]
        public List<AnimationClip> airLoopVariants = new List<AnimationClip>();

        /// <summary>실제로 쓸 공중 자세 목록. <see cref="airLoop"/>이 항상 첫 번째다.</summary>
        public List<AnimationClip> AirLoopClips()
        {
            var clips = new List<AnimationClip>();
            if (airLoop != null) clips.Add(airLoop);
            foreach (var clip in airLoopVariants)
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
            return clips;
        }
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

        [Header("견인 (우클릭) — 비우면 견인 레이어를 만들지 않는다")]
        [Tooltip("갈고리로 끌려가는 동안 반복. 없으면 견인 중에 <b>전용 자세가 없어</b> " +
                 "기본 레이어의 공중 자세와 스윙 레이어의 잔여 가중치가 그대로 겹쳐 보인다 — " +
                 "우클릭인데 스윙 자세가 같이 나오던 원인이 이것이다.")]
        public AnimationClip zipLoop;

        // 추가 생성 (2026-10-17)
        [Header("다이브 (공중에서 X) — 비우면 다이브 레이어를 만들지 않는다")]
        [Tooltip("다이브하는 동안 반복할 자세. Mix/Flying(슈퍼히어로 비행 자세)을 추천한다 — " +
                 "SwingDive가 몸을 앞으로 숙이므로 팔을 앞으로 뻗은 수평 자세가 머리부터 떨어지는 그림이 된다.")]
        public AnimationClip diveLoop;

        [Header("웹 슬링샷 (지상 우클릭 + 휠) — slingAim을 비우면 슬링샷 레이어를 만들지 않는다")]
        [Tooltip("충전 0%: 권총을 양손으로 겨눈 자세. 권총 팩의 W1_Stand_Aim_Idle_IPC.\n\n" +
                 "In-Place(IPC) 버전을 쓴다 — 조준 중엔 WebSlingshot이 몸을 제자리에 세우므로 " +
                 "클립이 루트를 움직이면 발이 미끄러진다.")]
        public AnimationClip slingAim;
        [Tooltip("충전 100%: 위로 겨눠 허리가 뒤로 젖혀진 자세. 권총 팩의 W1_Stand_Aim_Point_U90.\n\n" +
                 "휠을 굴릴수록 slingAim과 이 자세가 1D 블렌드 트리로 섞인다 — 충전량이 곧 젖혀진 정도다. " +
                 "비우면 slingAim 하나로 고정(젖혀지지 않음).")]
        public AnimationClip slingAimMax;
        [Tooltip("우클릭을 놓아 튀어 오르는 순간부터 꼭짓점까지. 비우면 기본 레이어의 공중 자세가 보인다.\n\n" +
                 "착지 칸은 없다 — 비행이 끝나면 PlayerMovement가 스윙 착지(swingLand) 경로를 그대로 탄다.")]
        public AnimationClip slingLaunch;

        [Header("갈고리 발사 (좌클릭 순간, 팔만) — 비우면 발사 레이어를 만들지 않는다")]
        [Tooltip("총을 들어 쏘는 순간 한 번 재생. 걷기·점프·스윙 위에 팔만 얹히므로, " +
                 "몸 전체가 아니라 오른팔·상체 정도만 움직이는 클립이어야 한다.")]
        public AnimationClip grappleFire;
        [Tooltip("위 클립에서 실제로 움직일 부위만 켠 Avatar Mask. 다리까지 포함하면 " +
                 "걷는 중에 쏠 때 다리가 그 순간 멈추거나 어긋난다. 보통 오른팔(+상체) 정도만.")]
        public AvatarMask grappleFireMask;

        [Header("웹 그랩 & 스로우 (소품 던지는 순간) — 비우면 던지기 레이어를 만들지 않는다")]
        [Tooltip("WebGrabThrow가 소품을 던지는 순간 한 번 재생.")]
        public AnimationClip throwProp;

        [Tooltip("비워두면 전신을 덮는다. 원래 판단은 \"던지는 그 순간만큼은 이동 " +
                 "애니메이션과 안 섞여도 된다\"였는데, 그 판단은 <b>지상에서 던지는 것만</b> " +
                 "염두에 둔 것이었다.\n\n" +
                 "스윙 중에 던지면 얘기가 달라진다 — 전신 Override라 매달린 자세가 통째로 " +
                 "사라졌다가 돌아온다. 상체만 켠 마스크를 넣으면 <b>다리는 스윙 자세를 " +
                 "유지한 채 팔로만 던지므로</b> 두 동작이 겹쳐서 이어진다.\n\n" +
                 "켤 것: Body · Head · 양팔 · 손가락 · Hand IK. 끌 것: <b>Root · 양다리 · " +
                 "Foot IK</b>. 다리를 끄는 것이 요점이다 — 그래야 하체가 스윙 자세로 남는다.\n\n" +
                 "<b>기존 Throw.mask를 그대로 물리면 안 된다.</b> 그 에셋은 오른팔·오른손만 " +
                 "꺼둔 것(= 나머지 전신을 켠 것)이라 다리까지 덮어, 마스크를 안 건 것과 " +
                 "사실상 같다. 새로 만들거나 그 에셋의 다리를 꺼서 쓸 것.")]
        public AvatarMask throwMask;

        [Header("그랩 앤 슬램 (잡은 채로 V, 전신) — 비우면 슬램 레이어를 만들지 않는다")]
        [Tooltip("WebGrabThrow가 V로 회전하며 던지는 동안 재생. Throw와 같은 이유로 마스크 " +
                 "없이 전신을 덮는다. 제자리 회전(위치 이동 없이 회전만)인 클립이어야 " +
                 "PlayerRootMotionRelay가 그 회전을 그대로 캐릭터에 옮길 수 있다 — 이 클립의 " +
                 "Root Transform Rotation → Bake Into Pose는 꺼둘 것(루트 모션으로 뽑아써야 함).")]
        public AnimationClip grabAndSlam;
    }
}
