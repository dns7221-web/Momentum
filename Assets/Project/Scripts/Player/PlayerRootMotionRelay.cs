using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 애니메이션 루트 모션의 <b>회전만</b> 부모 Rigidbody로 중계한다. 위치 이동은 절대
    /// 안 건드린다 — 이 프로젝트는 항상 물리(Rigidbody)가 위치를 담당하고, 루트 모션은
    /// 캐릭터가 제자리에서 도는 동작(예: 그랩 앤 슬램)에 한해서만 예외적으로 쓴다.
    ///
    /// <b>왜 이 컴포넌트가 별도로 필요한가.</b> 이 프로젝트의 <c>Animator</c>는 물리를 갖고
    /// 있는 <c>Player</c> 루트가 아니라 그 자식(모델) 오브젝트에 있다. 유니티의
    /// <c>OnAnimatorMove</c> 콜백은 <b>Animator가 붙은 바로 그 오브젝트</b>에서만 불리기
    /// 때문에, Player 루트에 있는 스크립트들은 이 콜백을 아예 못 받는다 — 그래서 Animator와
    /// 같은 오브젝트에 이 중계자를 따로 둔다.
    ///
    /// <b>기본값은 항상 꺼짐(<see cref="IsActive"/> = false)이다.</b> 걷기·스윙 등 이
    /// 프로젝트의 모든 애니메이션은 물리로만 움직이므로, 이게 켜져 있으면 안 된다 — 켜는
    /// 주체(예: <see cref="WebGrabThrow"/>)가 회전이 필요한 그 순간에만 켰다가 끝나면
    /// 반드시 끈다.
    /// </summary>
    public class PlayerRootMotionRelay : MonoBehaviour
    {
        [Tooltip("회전을 전달할 부모 Rigidbody. 비워두면 부모 쪽에서 찾는다.")]
        [SerializeField] private Rigidbody targetBody = null;

        private Animator _animator;

        /// <summary>켜져 있는 동안만 <see cref="OnAnimatorMove"/>가 회전을 실제로 적용한다.</summary>
        public bool IsActive { get; set; }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (targetBody == null) targetBody = GetComponentInParent<Rigidbody>();

            if (_animator == null)
                Debug.LogWarning("[PlayerRootMotionRelay] 같은 오브젝트에 Animator가 없다.", this);
            if (targetBody == null)
                Debug.LogWarning("[PlayerRootMotionRelay] 회전을 전달할 부모 Rigidbody를 못 찾았다.", this);
        }

        /// <summary>
        /// 애니메이터가 이번 프레임 만들어낸 루트 모션. <see cref="IsActive"/>가 꺼져 있으면
        /// 아무 것도 안 한다 — <c>deltaPosition</c>은 어느 경우에도 절대 쓰지 않는다.
        /// </summary>
        private void OnAnimatorMove()
        {
            if (!IsActive || targetBody == null || _animator == null) return;

            targetBody.MoveRotation(targetBody.rotation * _animator.deltaRotation);
        }
    }
}
