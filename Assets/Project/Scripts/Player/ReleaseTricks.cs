using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-21)
    /// <summary>
    /// 공중 트릭 — 스윙을 놓는 순간 공중제비·비틀기 같은 연출 동작을 한 번 재생한다(인섬니악 스파이더맨 느낌).
    ///
    /// <b>언제 나오나.</b> 공중에서 줄을 놓았고, 발밑 땅까지 <see cref="minHeight"/> 이상 떠 있을 때.
    /// 스윙 점프를 좋은 타이밍에 하면 항상, 그 밖의 놓기는 <see cref="plainReleaseChance"/> 확률로 나온다 —
    /// 잘 놓았을 때 보상처럼 보이게. 같은 트릭이 연달아 나오지 않게 직전 것은 빼고 무작위로 고른다.
    ///
    /// <b>어떻게.</b> PlayerAnimatorBuilder가 만든 "Trick" 레이어(Override, 전신)에 TrickIndex를 넣고
    /// ReleaseTrick 트리거를 쏜다. 레이어 가중치는 여기서만 올리고 내린다 — 트릭이 끝나갈 때,
    /// 다시 줄을 붙였을 때, 다이브·견인을 시작했을 때, 땅에 닿았을 때 서서히 꺼져 다른 동작에 자리를 내준다.
    /// <see cref="SwingConstraint"/>가 놓는 순간 <see cref="TryPlay"/>를 부르고, <see cref="GrappleGun"/>이 실행 중에 붙인다.
    /// </summary>
    public class ReleaseTricks : MonoBehaviour
    {
        private const string LayerName = "Trick";
        private static readonly int TrickIndexHash = Animator.StringToHash("TrickIndex");
        private static readonly int ReleaseTrickHash = Animator.StringToHash("ReleaseTrick");

        [Tooltip("끄면 트릭이 안 나온다(A/B 비교용).")]
        [SerializeField] private bool tricksEnabled = true;
        [Tooltip("발밑 땅까지 이 높이(m) 이상일 때만 트릭이 나온다. 낮으면 도는 도중 땅에 닿아 어색하다.")]
        [Min(0f)][SerializeField] private float minHeight = 6f;
        [Tooltip("좋은 타이밍 스윙 점프가 아닌 보통 놓기에서 트릭이 나올 확률. 1이면 매번, 0이면 좋은 타이밍에서만.")]
        [Range(0f, 1f)][SerializeField] private float plainReleaseChance = 0.35f;
        [Tooltip("트릭 하나가 나온 뒤 다음 트릭까지 최소 간격(초). 스윙을 빠르게 이어갈 때 매번 돌면 정신없다.")]
        [Min(0f)][SerializeField] private float cooldown = 1.2f;
        [Tooltip("트릭 클립이 이 비율(0~1)만큼 재생되면 레이어를 꺼 나가기 시작한다. 끝 자세(서 있는 착지 자세 등)가 공중에서 안 보이게.")]
        [Range(0.3f, 1f)][SerializeField] private float fadeOutAt = 0.7f;
        // 수정 (2026-10-21) — 켜고 끄는 속도를 나눴다. 같은 속도로 빨리 끄면 트릭 끝에서 공중 자세로 툭 바뀌었다.
        [Tooltip("트릭을 켤 때 레이어 가중치 변화 속도(초당). 10이면 0→1에 0.1초.")]
        [Min(0.1f)][SerializeField] private float fadeInSpeed = 10f;
        [Tooltip("트릭을 끌 때 속도(초당). 3.5면 1→0에 약 0.3초 — 트릭이 공중 자세로 녹아든다.")]
        [Min(0.1f)][SerializeField] private float fadeOutSpeed = 3.5f;
        [Tooltip("높이를 잴 때 맞출 레이어(땅·건물).")]
        [SerializeField] private LayerMask groundMask = ~0;

        private Animator _animator;
        private Rigidbody _body;
        private PlayerMovement _movement;
        private SwingConstraint _swing;
        private GrappleZip _zip;
        private SwingDive _dive;
        private int _layer = -1;
        private int _trickCount;
        private int _lastTrick = -1;
        private float _readyAt;
        private float _startedAt;
        private bool _playing;

        /// <summary>지금 트릭을 재생 중인가.</summary>
        public bool IsPlaying => _playing;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            _swing = GetComponent<SwingConstraint>();
            _zip = GetComponent<GrappleZip>();
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;

            // 트릭 레이어와 그 안의 Trick_0, Trick_1 … 상태 수를 센다. 레이어가 없으면(클립을 안 채웠으면) 아무것도 안 한다.
            _layer = _animator.GetLayerIndex(LayerName);
            if (_layer < 0) return;
            while (_animator.HasState(_layer, Animator.StringToHash("Trick_" + _trickCount))) _trickCount++;
        }

        /// <summary>
        /// 줄을 놓는 순간 부른다. 조건(공중, 높이, 쿨다운, 확률)이 맞으면 트릭 하나를 골라 재생한다.
        /// </summary>
        /// <param name="goodTiming">좋은 타이밍 스윙 점프였는가. 참이면 확률 없이 항상 나온다.</param>
        public void TryPlay(bool goodTiming)
        {
            if (!tricksEnabled || _layer < 0 || _trickCount == 0 || _body == null) return;
            if (Time.time < _readyAt) return;
            if (_movement != null && _movement.IsGrounded) return;
            if (!goodTiming && Random.value >= plainReleaseChance) return;
            if (Physics.Raycast(_body.position, Vector3.down, minHeight, groundMask, QueryTriggerInteraction.Ignore))
                return; // 땅이 너무 가깝다

            // 직전 트릭은 빼고 고른다(하나뿐이면 그대로).
            int pick = Random.Range(0, _trickCount);
            if (_trickCount > 1 && pick == _lastTrick) pick = (pick + 1 + Random.Range(0, _trickCount - 1)) % _trickCount;
            _lastTrick = pick;

            _animator.SetInteger(TrickIndexHash, pick);
            _animator.SetTrigger(ReleaseTrickHash);
            _playing = true;
            _startedAt = Time.time;
            _readyAt = Time.time + cooldown;
        }

        /// <summary>
        /// 트릭 레이어 가중치를 관리한다. 재생 중이면 1로, 끝나가거나 다른 동작이 시작되면 0으로 서서히 옮긴다.
        /// </summary>
        private void Update()
        {
            if (_layer < 0) return;
            if (_dive == null) _dive = GetComponent<SwingDive>(); // GrappleGun이 실행 중에 붙인다

            float target = 0f;
            if (_playing)
            {
                bool interrupted = (_movement != null && _movement.IsGrounded) ||
                                   (_swing != null && _swing.RopeLength > 0f) ||
                                   (_zip != null && _zip.IsZipping) ||
                                   (_dive != null && _dive.IsDiving);

                var state = _animator.GetCurrentAnimatorStateInfo(_layer);
                bool inTrick = state.IsTag("Trick") || _animator.IsInTransition(_layer);
                // 트리거가 상태를 바꾸기까지 한두 프레임 걸리므로 시작 직후 잠깐은 끝난 것으로 보지 않는다.
                bool finished = Time.time - _startedAt > 0.15f && !inTrick;
                bool nearEnd = state.IsTag("Trick") && state.normalizedTime >= fadeOutAt;

                if (interrupted || finished)
                {
                    _playing = false;
                    _animator.ResetTrigger(ReleaseTrickHash);
                }
                else if (!nearEnd)
                {
                    target = 1f;
                }
            }

            AnimatorLayerFade.To(_animator, _layer, target, target > 0f ? fadeInSpeed : fadeOutSpeed);
        }
    }
}
