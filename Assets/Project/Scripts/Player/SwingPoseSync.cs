using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-21)
    /// <summary>
    /// 스윙 호 위치별 자세 — 스윙 클립(Swinging)의 재생 위치를 <b>실제 진자 위치</b>에 맞춘다.
    ///
    /// <b>왜.</b> 예전엔 Swinging 클립이 제 박자대로 반복돼서, 몸은 호의 앞 꼭짓점에 있는데 자세는 뒤로 젖히는 중인
    /// 식으로 어긋났다. Mixamo Swinging 클립은 원래 "뒤 꼭짓점 → 바닥 → 앞 꼭짓점 → 바닥 → 뒤"를 한 번 도는 진자 동작이라,
    /// 그 재생 위치를 줄 각도에 묶으면 뒤에선 젖히고, 바닥에선 다리를 모으고, 앞에선 다리를 뻗는 자세가 호와 딱 맞는다.
    ///
    /// <b>어떻게.</b> 유니티 Animator 상태의 <c>Motion Time</c>(timeParameter) 기능을 쓴다. PlayerAnimatorBuilder가 SwingLoop 상태의
    /// 재생 위치를 "SwingCycle" 파라미터로 받게 만들고, 여기서 매 프레임 0~1 값을 넣는다.
    /// 값: 줄이 수직에서 진행 방향 앞뒤로 얼마나 벌어졌는지(s = -1 뒤 꼭짓점 ~ +1 앞 꼭짓점)와 몸이 앞으로 가는 중인지 뒤로 가는 중인지로
    /// 진자 한 바퀴 위치를 만든다 — 앞으로 가는 반 바퀴가 0~0.5, 돌아오는 반 바퀴가 0.5~1.
    /// 클립마다 꼭짓점 위치가 달라서 <see cref="cycleOffset"/>·<see cref="reverse"/>로 맞춘다.
    /// <see cref="GrappleGun"/>이 실행 중에 붙인다.
    /// </summary>
    public class SwingPoseSync : MonoBehaviour
    {
        private static readonly int SwingCycleHash = Animator.StringToHash("SwingCycle");

        [Tooltip("클립에서 '뒤 꼭짓점'이 있는 재생 위치(0~1). 자세가 호와 반 박자 어긋나 보이면 0.25씩 바꿔 본다.")]
        [Range(0f, 1f)][SerializeField] private float cycleOffset = 0f;
        [Tooltip("켜면 클립을 거꾸로 돌린다. 앞에서 젖히고 뒤에서 뻗으면 켠다.")]
        [SerializeField] private bool reverse = false;
        [Tooltip("진폭(꼭짓점 각도)의 최소값(°). 작은 스윙에서도 자세가 너무 크게 바뀌지 않게.")]
        [Min(5f)][SerializeField] private float minAmplitude = 30f;
        [Tooltip("재생 위치를 따라가는 빠르기. 클수록 즉각적, 작을수록 부드럽다.")]
        [Min(1f)][SerializeField] private float follow = 12f;

        private Animator _animator;
        private Rigidbody _body;
        private SwingConstraint _swing;
        private Vector3 _forward;
        private Vector3 _lastPivot;
        private float _amplitude;
        private float _cycle;
        private bool _wasSwinging;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _swing = GetComponent<SwingConstraint>();
            _animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (_animator == null || _body == null || _swing == null) return;

            bool swinging = _swing.RopeLength > 0f;
            if (!swinging) { _wasSwinging = false; return; }

            Vector3 pivot = _swing.SwingPivot;
            Vector3 fromPivot = _body.position - pivot;

            // 붙는 순간(또는 코너 스윙으로 앵커가 바뀐 순간) 진행 방향을 정한다 — 앵커 쪽 수평 방향.
            if (!_wasSwinging || (pivot - _lastPivot).sqrMagnitude > 4f)
            {
                Vector3 toPivot = Vector3.ProjectOnPlane(-fromPivot, Vector3.up);
                if (toPivot.sqrMagnitude < 0.01f) toPivot = Vector3.ProjectOnPlane(_body.linearVelocity, Vector3.up);
                _forward = toPivot.sqrMagnitude > 0.01f ? toPivot.normalized : transform.forward;
                _amplitude = Mathf.Max(minAmplitude, Vector3.Angle(fromPivot, Vector3.down));
                _cycle = Cycle(-1f, true); // 붙는 순간은 뒤쪽에서 앞으로 출발
                _wasSwinging = true;
            }
            _lastPivot = pivot;

            // 진행 방향으로 잰 줄 각도(+ = 몸이 앵커보다 앞). 진폭은 천천히 줄어드는 최대값으로 추적한다.
            float along = Vector3.Dot(fromPivot, _forward);
            float angle = Mathf.Atan2(along, -fromPivot.y) * Mathf.Rad2Deg;
            _amplitude = Mathf.Max(Mathf.Max(minAmplitude, Mathf.Abs(angle)), _amplitude - 10f * Time.deltaTime);
            float s = Mathf.Clamp(angle / _amplitude, -1f, 1f);
            bool movingForward = Vector3.Dot(_body.linearVelocity, _forward) >= 0f;

            // 고리 값이라 가까운 쪽으로 따라간다(0.95 → 0.05가 거꾸로 한 바퀴 돌지 않게).
            float target = Cycle(s, movingForward);
            float delta = Mathf.Repeat(target - _cycle + 0.5f, 1f) - 0.5f;
            _cycle = Mathf.Repeat(_cycle + delta * Mathf.Clamp01(follow * Time.deltaTime), 1f);

            float value = Mathf.Repeat((reverse ? 1f - _cycle : _cycle) + cycleOffset, 1f);
            _animator.SetFloat(SwingCycleHash, value);
        }

        /// <summary>
        /// 진자 위치(s: -1 뒤 꼭짓점 ~ +1 앞 꼭짓점)와 방향으로 한 바퀴 위치(0~1)를 만든다.
        /// 뒤 꼭짓점 = 0, 앞으로 가며 바닥 = 0.25, 앞 꼭짓점 = 0.5, 돌아오며 바닥 = 0.75.
        /// </summary>
        private static float Cycle(float s, bool movingForward)
        {
            float half = Mathf.Acos(-s) / (2f * Mathf.PI); // s=-1 → 0, s=0 → 0.25, s=1 → 0.5
            return movingForward ? half : 1f - half;
        }
    }
}
