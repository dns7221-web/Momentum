using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 다이브 — 공중에서 X를 한 번 누르면 머리부터 떨어지며 속도를 번다. (2026-10-17 추가)
    ///
    /// 인섬니악 스파이더맨에서 가져온 동작이다. 높은 곳에서 다이브로 속도를 벌고, 그 속도 그대로 스윙(R)에
    /// 붙으면 평소보다 확 빠른 스윙이 된다. 스윙 쪽은 붙는 순간의 속력까지 상한을 열어 주므로
    /// (<see cref="SwingConstraint"/>의 "들어온 속도 지키기") 여기서는 <b>속도를 버는 것</b>만 한다.
    ///
    /// <b>누르고 있기가 아니라 한 번 누르기(토글)인 이유</b>: 다이브 중에도 방향키·R·스페이스바를 써야 해서
    /// 손가락 하나를 계속 묶어 두면 이어가기가 어렵다. 착지·스윙·웹 집·슬링샷이 시작되면 저절로 꺼지고,
    /// X를 다시 눌러도 꺼진다.
    ///
    /// 물리는 낙하 가속을 더 거는 것과 속도 상한뿐이다. 나머지(공중 조작, 카메라 내려다보기)는
    /// 기존 시스템이 그대로 처리한다 — PlayerMovement의 공중 조작, TraversalCameraDirector의 낙하 속도 → 내려다보기.
    /// </summary>
    public class SwingDive : MonoBehaviour
    {
        [Header("입력")]
        [Tooltip("다이브 키. 비워두면 X 키 액션을 실행 중에 만든다(InputSystem_Actions 파일은 건드리지 않는다).")]
        [SerializeField] private InputActionReference diveAction = null;

        [Header("물리")]
        [Tooltip("다이브 동안 더 거는 중력 배수. 1.5면 기본 중력의 1.5배를 추가로 건다(PlayerMovement의 낙하 중력과 별도).")]
        [Min(0f)][SerializeField] private float extraGravityMultiplier = 1.5f;
        [Tooltip("다이브 속도 상한(m/s). 스윙에 붙으면 이 속도까지 상한이 열렸다가 천천히 돌아온다.")]
        [Min(1f)][SerializeField] private float maxDiveSpeed = 45f;
        [Tooltip("다이브를 켤 때 아래로 바로 주는 속도(m/s). 누르자마자 '꽂힌다'는 느낌. 0이면 끈다.")]
        [Min(0f)][SerializeField] private float startDownSpeed = 6f;

        [Header("자세")]
        [Tooltip("몸을 앞으로 숙일 대상. 비우면 자식 Animator가 붙은 트랜스폼(캐릭터 모델).")]
        [SerializeField] private Transform visual = null;
        [Tooltip("다이브 중 몸을 앞으로 숙이는 최대 각도(°). 아래로 빠를수록 이 각도에 가까워진다.")]
        [Range(0f, 90f)][SerializeField] private float maxPitch = 60f;
        [Tooltip("이 아래 낙하 속도(m/s)에서 최대 각도가 된다.")]
        [Min(1f)][SerializeField] private float fallSpeedForMaxPitch = 25f;
        [Tooltip("숙임 각도가 따라가는 속도(초당 각도).")]
        [Min(1f)][SerializeField] private float pitchSpeed = 240f;
        [Tooltip("Dive 레이어 가중치가 오르내리는 속도(초당). PlayerAnimatorBuilder가 diveLoop로 만든 레이어.")]
        [Min(0f)][SerializeField] private float animLayerLerpSpeed = AnimatorLayerFade.DefaultSpeed;

        /// <summary>지금 다이브 중인가. 카메라·애니메이션이 읽을 수 있게 공개한다.</summary>
        public bool IsDiving { get; private set; }

        // 추가 생성 (2026-10-17)
        /// <summary>
        /// 방금(0.3초 안)까지 다이브 중이었는가. SwingConstraint가 "다이브하다 붙은 스윙"에만 속도 상한을 열 때 쓴다.
        /// 여유를 두는 이유: 줄이 붙는 그 물리 스텝에 이 컴포넌트가 먼저 돌면 IsDiving이 이미 꺼져 있을 수 있다.
        /// </summary>
        public bool RecentlyDived => Time.time - _lastDiveTime < 0.3f;
        private float _lastDiveTime = -999f;

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private GrappleGun _gun;
        private GrappleZip _zip;
        private WebSlingshot _slingshot;
        private Animator _animator;
        private int _diveLayerIndex = -1;

        private InputAction _action;
        private bool _ownsAction;
        private bool _toggleRequested;

        private Quaternion _visualRest = Quaternion.identity;
        private float _pitch;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            _gun = GetComponent<GrappleGun>();
            _zip = GetComponent<GrappleZip>();
            _slingshot = GetComponent<WebSlingshot>();

            _animator = GetComponentInChildren<Animator>();
            if (_animator != null) _diveLayerIndex = _animator.GetLayerIndex("Dive"); // 없으면 -1 → 자세는 기울기만
            if (visual == null && _animator != null) visual = _animator.transform;
            if (visual != null) _visualRest = visual.localRotation;
        }

        private void OnEnable()
        {
            // 칸이 비면 X 키 액션을 직접 만든다(WebSlingshot의 F 키와 같은 방식).
            if (diveAction != null) _action = diveAction.action;
            else
            {
                _action = new InputAction("Dive", InputActionType.Button, "<Keyboard>/x");
                _ownsAction = true;
            }
            _action.Enable();
            _action.performed += OnDivePressed;
        }

        private void OnDisable()
        {
            if (_action != null)
            {
                _action.performed -= OnDivePressed;
                if (_ownsAction) { _action.Disable(); _action.Dispose(); }
                _action = null;
                _ownsAction = false;
            }
            IsDiving = false;
        }

        // 입력은 Update 타이밍이라 기억만 해 두고, 속도를 바꾸는 일은 FixedUpdate에서 한다.
        private void OnDivePressed(InputAction.CallbackContext ctx) => _toggleRequested = true;

        /// <summary>다이브를 할 수 있는 상황인가 — 공중이고, 줄·웹 집·슬링샷 중이 아닐 때.</summary>
        private bool CanDive()
        {
            if (_movement == null || _movement.IsGrounded) return false;
            if (_gun != null && _gun.IsAttached) return false;
            if (_zip != null && _zip.IsZipping) return false;
            if (_slingshot != null && (_slingshot.IsCharging || _slingshot.IsLaunching)) return false;
            return true;
        }

        private void FixedUpdate()
        {
            if (_toggleRequested)
            {
                _toggleRequested = false;
                if (IsDiving) IsDiving = false;
                else if (CanDive())
                {
                    IsDiving = true;
                    // 누르는 순간 아래로 꽂히게 — 올라가던 중이면 그 위 속도도 지운다.
                    Vector3 v = _rb.linearVelocity;
                    v.y = Mathf.Min(v.y, 0f) - startDownSpeed;
                    _rb.linearVelocity = v;
                }
            }

            // 상황이 바뀌면(착지·스윙·웹 집·슬링샷) 저절로 끈다.
            if (IsDiving && !CanDive()) IsDiving = false;
            if (!IsDiving) return;
            _lastDiveTime = Time.time; // 추가 생성 — RecentlyDived 판정용

            // 낙하 가속을 더 건다. AddForce(Acceleration)는 질량과 무관하게 가속도를 더하는 유니티 내장 방식.
            _rb.AddForce(Physics.gravity * extraGravityMultiplier, ForceMode.Acceleration);

            // 상한. 넘으면 방향은 두고 크기만 줄인다.
            if (_rb.linearVelocity.sqrMagnitude > maxDiveSpeed * maxDiveSpeed)
                _rb.linearVelocity = Vector3.ClampMagnitude(_rb.linearVelocity, maxDiveSpeed);
        }

        private void Update()
        {
            // Dive 레이어 가중치 — 다이브 중이면 1, 아니면 0으로 서서히.
            if (_animator != null && _diveLayerIndex >= 0)
                AnimatorLayerFade.To(_animator, _diveLayerIndex, IsDiving ? 1f : 0f, animLayerLerpSpeed);
        }

        /// <summary>
        /// 몸을 앞으로 숙인다. 아래로 빠를수록 많이(최대 maxPitch). 애니메이터가 뼈를 움직인 뒤인
        /// LateUpdate에서 모델 루트만 돌린다 — 뼈를 건드리지 않아 어떤 클립과도 겹쳐 쓸 수 있다.
        /// 다이브가 끝나 각도가 0으로 돌아오면 더 쓰지 않는다(스윙 자세는 SwingConstraint가 맡는다).
        /// </summary>
        private void LateUpdate()
        {
            if (visual == null) return;

            float target = 0f;
            if (IsDiving)
            {
                float fall = Mathf.Max(0f, -_rb.linearVelocity.y);
                target = maxPitch * Mathf.Clamp01(fall / fallSpeedForMaxPitch);
            }

            bool wasActive = _pitch > 0.01f;
            _pitch = Mathf.MoveTowards(_pitch, target, pitchSpeed * Time.deltaTime);
            if (!wasActive && _pitch <= 0.01f) return; // 다이브와 무관할 때는 모델 회전을 건드리지 않는다

            // 모델의 오른쪽 축 기준으로 앞으로 숙인다(양수 X 회전 = 앞으로 숙임).
            visual.localRotation = _visualRest * Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
