using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-19)
    /// <summary>
    /// 슬링샷을 감는 동안의 팔 동작 — 유니티 내장 Animator IK. 스파이더맨 2에서 피터가 땅에 낮게 앉아
    /// <b>두 팔을 허리 높이에서 옆으로 벌리고</b> 줄을 쥔 채 버티는 자세를, 총을 든 우리 캐릭터에 맞게 옮겼다.
    /// <list type="bullet">
    /// <item><b>오른손(총)</b>: 충전 0%면 앵커 쪽으로 팔을 뻗어 겨누고, 충전할수록 <b>허리 옆까지 끌어당긴다</b> —
    ///       줄 끝인 총을 몸으로 당겨 시위를 당기는 그림. 줄은 총구에서 앵커로 팽팽하게 이어진다.</item>
    /// <item><b>왼손</b>: 허리 높이에서 옆으로 벌려 균형을 잡는다(피터의 반대쪽 팔).</item>
    /// </list>
    /// 두 손을 모으지 않는다 — 가슴 앞에 모으면 기도·고해성사 자세처럼 보였다(2026-10-19 플레이 확인).
    ///
    /// 앵커 방향이 매번 달라 클립 하나로는 못 맞추므로 손 위치를 직접 정하는 IK를 쓴다. 손 회전은 건드리지 않는다
    /// (권총 조준 클립의 손목 그대로) — 뼈 축이 모델마다 달라 회전까지 잡으면 손목이 꺾인다.
    /// <c>OnAnimatorIK</c>는 Animator와 같은 오브젝트에 있어야 불리고, IK Pass가 켜진 레이어(Slingshot)에서 불린다.
    /// <see cref="WebSlingshot"/>이 실행 중에 붙인다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class SlingPullIK : MonoBehaviour
    {
        [Header("오른손 (총)")]
        [Tooltip("충전 0%일 때 총을 든 손이 가슴에서 앵커 쪽으로 나가 있는 거리(m). 팔을 뻗어 겨눈 모양.")]
        [Min(0f)][SerializeField] private float reachOut = 0.55f;
        [Tooltip("충전 100%일 때 총을 든 손이 끌려오는 자리: 엉덩이에서 오른쪽으로(m).")]
        [SerializeField] private float pullSide = 0.3f;
        [Tooltip("그 자리의 높이: 엉덩이에서 위로(m). 허리 높이쯤.")]
        [SerializeField] private float pullHeight = 0.15f;

        [Header("왼손 (균형)")]
        [Tooltip("왼손을 엉덩이에서 왼쪽으로 벌리는 거리(m).")]
        [SerializeField] private float balanceSide = 0.45f;
        [Tooltip("왼손 높이: 엉덩이에서 위로(m).")]
        [SerializeField] private float balanceHeight = 0.2f;
        [Tooltip("왼손을 몸 앞(앵커 쪽)으로 내미는 거리(m). 음수면 뒤로.")]
        [SerializeField] private float balanceForward = 0.1f;

        [Tooltip("IK가 켜지고 꺼지는 속도(초당). 뚝 켜지면 팔이 튄다.")]
        [Min(0.1f)][SerializeField] private float weightSpeed = 8f;

        private Animator _animator;
        private WebSlingshot _sling;
        private float _weight;

        /// <summary>붙이는 쪽(WebSlingshot)이 자기 자신을 넘긴다.</summary>
        public void Init(WebSlingshot sling) => _sling = sling;

        private void Awake() => _animator = GetComponent<Animator>();

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || _sling == null) return;

            float target = _sling.IsCharging ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, weightSpeed * Time.deltaTime);
            if (_weight <= 0.001f) return; // IK 가중치는 매 프레임 0으로 시작하므로 안 정하면 꺼진 것

            Transform chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (chest == null || hips == null) return;

            // 앵커 쪽 수평 방향과 그 오른쪽. 몸은 WebSlingshot이 앵커 쪽으로 돌려 둔다.
            Vector3 toAnchor = _sling.AimAnchor - chest.position;
            if (toAnchor.sqrMagnitude < 0.01f) return;
            Vector3 dir = toAnchor.normalized;
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;

            // 오른손(총): 앵커 쪽으로 뻗은 자리 → 충전할수록 허리 옆으로 끌려온다.
            Vector3 aimed = chest.position + dir * reachOut;
            Vector3 pulled = hips.position + right * pullSide + Vector3.up * pullHeight;
            Vector3 gunHand = Vector3.Lerp(aimed, pulled, _sling.ChargeVisual01);
            SetHand(AvatarIKGoal.RightHand, gunHand, _weight);

            // 왼손: 허리 높이에서 옆으로 벌려 균형. 충전 초반부터 바로 벌린다.
            Vector3 balance = hips.position - right * balanceSide + Vector3.up * balanceHeight + flat * balanceForward;
            SetHand(AvatarIKGoal.LeftHand, balance, _weight);

            // 팔꿈치는 바깥·뒤로 — 당길 때 팔꿈치가 옆으로 벌어지는 모양.
            SetElbow(AvatarIKHint.RightElbow, chest.position + right * 0.5f - flat * 0.2f);
            SetElbow(AvatarIKHint.LeftElbow, chest.position - right * 0.5f - flat * 0.1f);
        }

        private void SetHand(AvatarIKGoal goal, Vector3 position, float weight)
        {
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKPosition(goal, position);
        }

        private void SetElbow(AvatarIKHint hint, Vector3 position)
        {
            _animator.SetIKHintPositionWeight(hint, _weight * 0.6f);
            _animator.SetIKHintPosition(hint, position);
        }
    }
}
