using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 지상 이동 — 걷기·달리기·점프·카메라 기준 회전.
    ///
    /// 이 캡슐이 물리 몸통이고, 스킨 메시 캐릭터는 자식으로 붙는 시각 레이어일 뿐이다
    /// (README 설계: "목적은 외형이 아니라 물리 바디다"). 나중에 로프 스윙이 붙을 때도
    /// 같은 Rigidbody를 그대로 구속하면 되므로, 지금부터 위치는 전부 Rigidbody가 갖고
    /// 애니메이션은 그 위에 얹히는 포즈로만 작동한다 — Animator의 Apply Root Motion은
    /// 반드시 꺼져 있어야 한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 8f;
        [Tooltip("초당 최대 회전 각도. 카메라가 아니라 이동 방향을 향해 도는 속도다.")]
        [SerializeField] private float turnSpeedDegPerSec = 540f;
        [SerializeField] private float jumpSpeed = 6f;

        [Header("에어 컨트롤 — 공중에서는 대입이 아니라 가속이다")]
        [Tooltip("공중에서 입력이 수평 속도를 바꾸는 최대 가속도(m/s²).\n\n" +
                 "값 하나가 두 상황을 다 감당한다. 정지 → 걷기 속도(4)는 0.3초라 점프 중 " +
                 "조작이 답답하지 않고, 릴리즈 직후 30m/s를 90° 트는 데는 3초가 넘어 " +
                 "사실상 못 튼다. 올릴수록 공중이 조종에 가까워지고, 내릴수록 관성에 " +
                 "맡기게 된다.\n\n" +
                 "스윙의 펌프(SwingConstraint)보다 낮게 두는 것이 전제다 — 매달리지 않고도 " +
                 "더 잘 날아가면 로프를 걸 이유가 없어진다.")]
        [Min(0f)][SerializeField] private float airAcceleration = 12f;

        [Tooltip("접지 상태에서 이보다 작은 상향 속도는 매 스텝 0으로 지운다. " +
                 "인도 턱 같은 단차에서 depenetration이 얹는 잔여 튐을 지우기 위한 값이다.\n\n" +
                 "jumpSpeed보다 확실히 낮게 둘 것 — 점프 직후 한 프레임 접지 신호가 " +
                 "남았을 때 이 값이 점프 속도까지 지워버리면 점프가 거의 안 뛰는 것처럼 된다.")]
        [Min(0f)][SerializeField] private float residualLiftClamp = 2f;

        [Header("안전망 — 도시 밖으로 날아가는 것에 대한 최후 방어선")]
        [Tooltip("원점에서 이 거리(XZ)를 넘으면 마지막 접지 위치로 되돌린다.")]
        [Min(1f)][SerializeField] private float safeRadius = 500f;
        [Tooltip("이 높이(Y, 위아래 모두)를 넘으면 마지막 접지 위치로 되돌린다.")]
        [Min(1f)][SerializeField] private float safeHeight = 300f;
        private Vector3 _lastSafePosition;

        /// <summary>지난 스텝의 SuspendHorizontalControl 값. 스윙을 막 놓은 프레임을 잡아낸다.</summary>
        private bool _wasSuspended;

        [Header("지면 판정")]
        [Tooltip("이 각도보다 가파른 면은 바닥이 아니라 벽으로 본다. 벽 점프를 막는 값.")]
        [Range(0f, 89f)][SerializeField] private float maxSlopeAngle = 50f;

        [Header("진단")]
        [Tooltip("점프가 실제로 몇 번 발동하는지 콘솔에 찍는다. " +
                 "한 번 눌러 한 줄만 뜨는데도 캐릭터가 떠오르면 원인은 코드가 아니라 " +
                 "Animator의 Apply Root Motion이다.")]
        [SerializeField] private bool logJumps = false;

        [Header("카메라")]
        [Tooltip("비워두면 Camera.main을 자동으로 쓴다. 입력을 이 방향 기준으로 회전시킨다.")]
        [SerializeField] private Transform cameraTransform;

        [Header("입력 (InputSystem_Actions.inputactions의 Player 맵에서 드래그)")]
        [SerializeField] private InputActionReference moveAction = null;
        [SerializeField] private InputActionReference sprintAction = null;
        [SerializeField] private InputActionReference jumpAction = null;

        /// <summary>
        /// 켜면 수평 속도를 입력으로 덮어쓰지 않는다. <see cref="SwingConstraint"/>가 스윙 중에 켠다.
        ///
        /// 이 스위치가 필요한 이유는 아래 <c>FixedUpdate</c>가 매 스텝 x·z를 입력값으로
        /// <b>대입</b>하기 때문이다. 그대로 두면 진자가 만들어낸 접선 속도가 다음 스텝에
        /// 지워져 스윙이 성립하지 않는다.
        ///
        /// 이동 쪽이 갈고리를 알 필요는 없으므로 "누가 끄는지"가 아니라 "꺼졌는지"만 둔다.
        /// </summary>
        public bool SuspendHorizontalControl { get; set; }

        [Header("비대칭 중력 — 올라갈 땐 그대로, 내려올 땐 무겁게")]
        [Tooltip("하강 중에 중력을 몇 배로 걸지. 1이면 정직한 포물선이고, 올릴수록 " +
                 "정점에 머무는 느낌은 그대로인 채 떨어지는 구간만 짧아진다. " +
                 "마리오 때부터 거의 모든 점프 게임이 쓰는 방법이다 — 발사의 쾌감은 " +
                 "올라가는 구간에서 나오고, 지루한 것은 내려오는 구간이기 때문이다.")]
        [Min(1f)][SerializeField] private float fallGravityMultiplier = 2f;

        [Header("낙하 판정")]
        [Tooltip("이만큼 연속으로 공중이어야 '진짜 낙하'로 본다. 타일 이음매 · 인도 턱을 " +
                 "지나갈 때 물리 스텝 한 번만큼 접지가 순간적으로 끊길 수 있는데, 그걸 그대로 " +
                 "믿으면 걷다가 다이빙 애니메이션이 튀어나온다.")]
        [Min(0f)][SerializeField] private float fallDebounceSeconds = 0.15f;
        [Tooltip("이보다 빠르게 아래로 떨어지고 있어야 '진짜 낙하'로 본다.\n\n" +
                 "3(옛값)은 이 중력(gravityScale 1.5 → 14.7 m/s²)에서 시간 문턱(0.15초)과 " +
                 "사실상 같은 시점에 도달한다 — 자유낙하로 0.15초면 이미 2.2m/s, 0.2초면 3m/s라 " +
                 "속도 조건이 시간 조건에 얹혀 따라올 뿐 독립된 안전장치가 못 됐다. 그래서 " +
                 "전력질주로 완만한 언덕 정상을 넘을 때(짧게 뜨지만 실제 낙하는 아님)도 " +
                 "걸러지지 않고 낙하로 오판됐다.\n\n" +
                 "6은 자유낙하 0.4초(≈1.2m 낙하)에 해당한다 — 시간 문턱의 두 배가 넘는 " +
                 "여유를 둬서, 언덕 정상에서 잠깐 뜨는 것과 실제로 뭔가에서 떨어지는 것을 " +
                 "속도로 확실히 가른다. 중력 배율을 바꾸면 이 값도 같이 다시 볼 것.")]
        [Min(0f)][SerializeField] private float fallVelocityThreshold = 6f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AirTimeHash = Animator.StringToHash("AirTime");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int FallingHash = Animator.StringToHash("Falling");

        private Rigidbody _rb;
        private Animator _animator;

        /// <summary>이번 물리 스텝에 바닥과 닿아 있는지. 충돌 콜백이 채운다.</summary>
        private bool _isGrounded;

        /// <summary>연속으로 공중에 있던 시간. 착지하는 순간 즉시 0으로 되돌아간다.</summary>
        private float _airTime;

        /// <summary>
        /// "진짜 낙하"인지. <see cref="_airTime"/>이 <see cref="fallDebounceSeconds"/>를
        /// 넘어야 참이 된다.
        ///
        /// <see cref="_isGrounded"/>를 애니메이터에 그대로 흘려보내지 않는 이유가 이거다.
        /// 접지 판정은 물리 스텝 단위(0.005초)라 순간적인 끊김에도 즉시 반응하는데, 그건
        /// "바닥이 없어졌는가"에는 정확해도 "낙하 애니메이션을 틀어도 되는가"에는 너무 예민하다.
        /// 착지는 반대로 지연 없이 즉시 반영한다 — 비대칭이다. 떨어지기 시작했다는 판단은
        /// 신중해야 하지만, 발이 닿았다는 판단은 늦출 이유가 없다.
        /// </summary>
        private bool _falling;

        /// <summary>
        /// 충돌 콜백이 접촉을 발견하면 켠다.
        ///
        /// 별도 변수를 두는 이유는 물리 콜백과 FixedUpdate의 실행 순서 때문이다.
        /// Unity는 [FixedUpdate → 물리 시뮬레이션 → 충돌 콜백] 순으로 돌므로,
        /// FixedUpdate에서 이 값을 읽어 쓰고 곧바로 꺼두면 다음 스텝의 콜백이 다시 채운다.
        /// 접촉이 사라지면 아무도 켜주지 않으므로 자연히 false로 남는다.
        /// </summary>
        private bool _groundContact;

        private bool _jumpRequested;
        private float _horizontalSpeed;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // 스킨 메시는 자식 프리팹으로 붙는다 — 캡슐 자체엔 Animator를 두지 않는다.
            _animator = GetComponentInChildren<Animator>();

            // 캡슐이 충돌로 옆으로 넘어지면 안 된다. 회전은 아래에서 스크립트가 직접 관리한다.
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // 지금 속도로는 필요 없지만, 이후 스윙에서 같은 Rigidbody가 24m/s대로 움직이게 되므로
            // 걷기 단계부터 맞춰 둔다 — 나중에 바꿀 일이 없어야 터널링 설정을 두 번 안 건드린다.
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            moveAction?.action.Enable();
            sprintAction?.action.Enable();
            if (jumpAction != null)
            {
                jumpAction.action.Enable();
                jumpAction.action.performed += OnJumpPerformed;
            }
        }

        private void OnDisable()
        {
            moveAction?.action.Disable();
            sprintAction?.action.Disable();
            if (jumpAction != null)
            {
                jumpAction.action.performed -= OnJumpPerformed;
                jumpAction.action.Disable();
            }
        }

        /// <summary>
        /// 입력만 기록한다. 지면 판정은 하지 않는다.
        ///
        /// 입력 이벤트는 물리 스텝과 타이밍이 어긋나므로, 여기서 지면 상태를 보고 판단하면
        /// 이미 낡은 값을 읽게 된다. 판단은 FixedUpdate에서 그 스텝의 접촉 정보로 한 번만 한다.
        /// </summary>
        private void OnJumpPerformed(InputAction.CallbackContext ctx) => _jumpRequested = true;

        // ─── 지면 접촉 ───
        //
        // 구를 쏘아 짐작하지 않고 물리 엔진이 알려주는 실제 접촉점을 쓴다.
        // 반지름·거리 같은 튜닝 값이 없어지고, 닿았는지 아닌지가 추정이 아니라 사실이 된다.

        private void OnCollisionEnter(Collision collision) => EvaluateContacts(collision);
        private void OnCollisionStay(Collision collision) => EvaluateContacts(collision);

        private void EvaluateContacts(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                // 접촉면의 법선이 충분히 위를 향하면 바닥, 아니면 벽이다.
                // 이 한 줄이 벽에 붙어 무한히 점프하던 문제를 막는다.
                if (Vector3.Angle(collision.GetContact(i).normal, Vector3.up) <= maxSlopeAngle)
                {
                    _groundContact = true;
                    return;
                }
            }
        }

        private void FixedUpdate()
        {
            // 최후의 방어선. 먼 릴리즈 거리와 큰 상승 보정이 겹치면 언젠가 도시 경계를
            // 넘어갈 수 있다 — 아직은 안 났지만, 나면 그 아래는 무한 낙하뿐이다.
            // 값을 정교하게 맞추는 대신, 접지된 마지막 위치로 그냥 되돌린다.
            if (_isGrounded) _lastSafePosition = _rb.position;
            else if (Mathf.Abs(_rb.position.x) > safeRadius ||
                     Mathf.Abs(_rb.position.z) > safeRadius ||
                     _rb.position.y > safeHeight || _rb.position.y < -safeHeight)
            {
                _rb.position = _lastSafePosition;
                _rb.linearVelocity = Vector3.zero;
            }

            _isGrounded = _groundContact;
            // 다음 스텝의 충돌 콜백이 다시 채운다. 발이 떨어지면 아무도 안 채우므로 false로 남는다.
            _groundContact = false;

            // 스윙을 막 놓은 프레임인지. 아래 두 곳에서 쓴다.
            bool justReleasedSwing = _wasSuspended && !SuspendHorizontalControl;
            _wasSuspended = SuspendHorizontalControl;

            if (_isGrounded)
            {
                _airTime = 0f;
                _falling = false;
            }
            else if (justReleasedSwing)
            {
                // <b>놓는 순간 낙하 판정을 리셋한다.</b> _falling은 한 번 참이 되면 착지할
                // 때까지 latch되는데, 스윙 중에는 아치 하강 구간마다 하강 속도가 문턱을
                // 넘으므로 사실상 항상 참인 채로 릴리즈를 맞는다. 그 상태로 놓으면 공중
                // 자세(AirIdle)가 뜨자마자 Falling 조건에 걸려 곧장 낙하 사슬로 넘어가버려
                // 한 프레임도 안 보인다 — "가끔만 나온다"의 정체가 이것이었다.
                // 릴리즈는 새로운 공중 구간의 시작이므로 여기서 처음부터 다시 센다.
                _airTime = 0f;
                _falling = false;
            }
            else
            {
                _airTime += Time.fixedDeltaTime;
                bool longEnough = _airTime >= fallDebounceSeconds;
                bool fastEnough = _rb.linearVelocity.y <= -fallVelocityThreshold;
                if (longEnough && fastEnough) _falling = true;
            }

            Vector2 input = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            bool sprinting = sprintAction != null && sprintAction.action.IsPressed();

            Vector3 moveDir = CameraRelativeDirection(input);
            float targetSpeed = sprinting ? sprintSpeed : walkSpeed;

            // 놓은 바로 그 한 스텝은 접지 여부와 무관하게 공중 취급한다. 앵커 근처에서
            // 놓으면(건물에 스치듯 붙어 있던 경우 등) 접지 신호가 한 스텝 잔류할 수 있는데,
            // 그때 아래 grounded 분기가 걸리면 입력이 없을 때 속도를 0으로 대입해버려
            // 날아가던 몸이 그 자리에 뚝 멈춘다. 점프 직후 잔류 접지가 점프 속도를 지우던
            // 것과 같은 종류의 사고다.
            var velocity = _rb.linearVelocity;
            if (!SuspendHorizontalControl)
            {
                // 지상에서만 대입한다. 공중까지 대입하면 <b>릴리즈한 다음 스텝에 스윙으로
                // 벌어둔 수평 속도가 걷기 속도로 지워진다</b> — 30m/s로 날아가야 할 몸이 4m/s가
                // 되고, y는 안 건드리므로 상향 보상만 남아 거의 수직으로 솟는다.
                // "놓는 순간 위로 솟는다"의 실체가 이것이었다. 관성이 곧 릴리즈의 보상인데
                // 그 보상을 지상 이동 로직이 한 스텝 만에 회수하고 있었다.
                if (_isGrounded && !justReleasedSwing)
                {
                    velocity.x = moveDir.x * targetSpeed;
                    velocity.z = moveDir.z * targetSpeed;

                    // 인도 턱 같은 단차를 넘을 때 depenetration이 얹은 잔여 상승 속도를
                    // 여기서 지운다. 안 지우면 중력이 서서히 깎아줄 때까지 몇 스텝 남아있고,
                    // 턱을 빠르게 연속으로 넘으면 이전 턱의 잔여값에 다음 턱의 튕김이 또
                    // 얹혀 넘을 때마다 점점 높이 뜬다.
                    //
                    // <b>작은 값만</b> 지운다. 점프한 바로 다음 스텝에도 접촉이 아직 남아
                    // _isGrounded가 잠깐 true로 다시 읽힐 수 있는데(위 점프 코드의 주석 참고),
                    // 크기를 안 가리고 지우면 그 프레임에 막 얹은 점프 속도(jumpSpeed)까지
                    // 여기서 죽어 점프가 거의 안 뛰는 것처럼 보인다. 턱의 잔여값(최대 약
                    // Project Settings의 Max Depenetration Velocity)은 점프 속도보다 훨씬
                    // 작으므로, 그 사이 어딘가에 문턱을 두면 점프는 안 건드리고 턱 튐만 잡는다.
                    if (velocity.y > 0f && velocity.y < residualLiftClamp) velocity.y = 0f;
                }
                else
                {
                    ApplyAirControl(ref velocity, moveDir, targetSpeed);
                }
            }

            if (_jumpRequested && _isGrounded)
            {
                velocity.y = jumpSpeed;
                // 이번 스텝부터 즉시 공중으로 취급한다. 뛰어오른 프레임에도 접촉은 아직
                // 남아 있으므로, 이걸 끄지 않으면 같은 스텝에 두 번째 점프가 끼어든다.
                _isGrounded = false;
                _groundContact = false;
                _animator?.SetTrigger(JumpHash);

                if (logJumps)
                    Debug.Log($"[PlayerMovement] 점프 발동 — 높이 {transform.position.y:F2}m", this);
            }
            // 눌렀는데 공중이었으면 그냥 버린다. 남겨두면 착지하는 순간 저절로 튀어 오른다.
            _jumpRequested = false;

            _rb.linearVelocity = velocity;
            _horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            ApplyFallGravity(velocity);

            // 속도와 마찬가지로 회전도 스윙 중에는 넘긴다.
            //
            // 이 가드가 없으면 스윙 중에도 몸이 입력 방향으로 돌아가버린다. 그러면 진행
            // 방향과 무관한 쪽을 보고 날아가고, 입력을 놓는 순간 마지막 각도로 굳는다.
            // 무엇보다 아래 <c>Vector3.up</c>이 매 스텝 pitch·roll을 0으로 되돌려서,
            // 로프가 아무리 비스듬해도 몸은 다림추처럼 수직으로 남는다 — 스윙 자세가
            // 안 나오던 원인이 이 한 줄이었다. 스윙 중 회전은 SwingConstraint가 맡는다.
            if (!SuspendHorizontalControl)
            {
                // 공중에서 빠를 때는 입력이 아니라 <b>실제 진행 방향</b>을 본다.
                //
                // 에어 컨트롤이 대입에서 가속으로 바뀌면서 둘이 갈라졌다. 전에는 공중에서도
                // 속도가 곧 입력이라 입력을 보는 것이 곧 진행 방향을 보는 것이었는데, 이제
                // 30m/s로 날아가는 중에 방향키를 톡 누르면 몸만 그쪽으로 돈다. 그건 README가
                // 치명적 결함으로 분류한 "진행 방향을 안 보는 것"이다.
                //
                // 기준을 전력질주 속도로 삼으면 새 값이 필요 없다. 평범한 점프는 그 이하라
                // 지금까지처럼 입력을 보고, 스윙에서 놓고 날아가는 속도는 언제나 그 위다.
                Vector3 facing = moveDir;
                if (!_isGrounded)
                {
                    var flight = new Vector3(velocity.x, 0f, velocity.z);
                    if (flight.sqrMagnitude > sprintSpeed * sprintSpeed)
                        facing = flight.normalized;
                }

                if (facing.sqrMagnitude > 0.0001f)
                {
                    var targetRot = Quaternion.LookRotation(facing, Vector3.up);
                    var rotated = Quaternion.RotateTowards(_rb.rotation, targetRot,
                        turnSpeedDegPerSec * Time.fixedDeltaTime);
                    _rb.MoveRotation(rotated);
                }
            }
        }

        /// <summary>
        /// 공중에서의 이동 입력 — 대입이 아니라 <b>가속</b>이다.
        ///
        /// 지상은 대입이 맞다. 발이 땅에 있으면 마찰로 원하는 속도를 즉시 낼 수 있고,
        /// 조작이 즉각적이어야 걷는 맛이 산다. 공중은 반대다. 붙잡을 것이 없는데 속도를
        /// 갈아치울 수 있으면 그건 이동이 아니라 순간이동이고, 무엇보다 <b>벌어둔 관성이
        /// 입력 한 번에 사라진다.</b>
        ///
        /// 두 줄이 이 함수의 전부다.
        ///
        /// <b>하나, 목표 속력은 현재 속력과 지상 속도 중 큰 쪽이다.</b> 이 한 줄이
        /// "입력이 관성을 깎지 못한다"를 만든다 — 30m/s로 날다가 진행 방향을 눌러도
        /// 목표가 4가 아니라 30이라 느려지지 않고, 그냥 걸어 다닐 때는 목표가 4로
        /// 남아 평소와 같다.
        ///
        /// <b>둘, 거기까지 가는 속도를 <see cref="airAcceleration"/>으로 제한한다.</b>
        /// 가속도가 하나뿐인데도 필요한 비대칭이 저절로 나온다 — 정지에서 걷기 속도까지는
        /// 0.3초면 닿아 점프 중 조작이 답답하지 않지만, 30m/s를 90° 틀려면 Δv가 42m/s라
        /// 3초가 넘어 사실상 불가능하다. <b>느릴 때는 조작이고 빠를 때는 관성이다.</b>
        ///
        /// 입력이 없으면 아무것도 하지 않는다. 목표를 0으로 두면 손을 떼는 순간 공중에서
        /// 제동이 걸려, 릴리즈하고 손을 놓는 가장 흔한 조작이 그대로 감속이 된다.
        /// 반대로 <b>진행 반대쪽을 누르면 목표가 뒤를 향하므로 자연히 브레이크가 된다</b> —
        /// 스윙의 펌프가 "미는 방향과 반대면 느려진다" 하나로 가속과 제동을 함께 얻는 것과
        /// 같은 구조이고, 규칙을 새로 만들지 않았다.
        /// </summary>
        private void ApplyAirControl(ref Vector3 velocity, Vector3 moveDir, float targetSpeed)
        {
            if (moveDir.sqrMagnitude < 0.0001f) return;

            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 desired = moveDir * Mathf.Max(horizontal.magnitude, targetSpeed);

            horizontal = Vector3.MoveTowards(
                horizontal, desired, airAcceleration * Time.fixedDeltaTime);

            velocity.x = horizontal.x;
            velocity.z = horizontal.z;
        }

        /// <summary>
        /// 하강 중에만 중력을 더 건다 — 죽은 체공 시간을 절반으로 자르는 장치.
        ///
        /// 스윙에서 크게 튀어 오른 뒤 <b>내려오기만 하는 몇 초</b>가 이 게임의 가장 지루한
        /// 구간이다. 올라가는 것은 릴리즈의 보상이라 건드리면 안 되지만, 내려오는 것은
        /// 아무 정보도 조작도 없는 시간이라 짧을수록 좋다. 그 둘을 갈라 다르게 대우한다.
        ///
        /// <b>물리적 거짓말인가.</b> 그렇다. 그리고 마리오 이래 거의 모든 점프 게임이 하는
        /// 거짓말이다 — 사람은 정점에 머무는 시간을 "체공감"으로 읽고 떨어지는 시간을
        /// "기다림"으로 읽기 때문에, 둘에 같은 중력을 주면 한쪽이 반드시 틀린다.
        ///
        /// <b>추가 가속도로 넣는 이유.</b> <c>Physics.gravity</c> 자체를 바꾸면 도시의
        /// 모든 강체가 같이 무거워지고, 무엇보다 도시 생성기가 심어둔 중력 가정(설계 결정에
        /// 따라 레벨 메트릭의 입력이다)이 흔들린다. 이 캡슐 하나에만 얹으면 그 계산은 그대로다.
        ///
        /// 배수에서 1을 빼는 것은 유니티가 이미 표준 중력 1인분을 걸어줬기 때문이다.
        /// 여기서 거는 것은 그 위에 얹는 <b>차액</b>이다.
        /// </summary>
        private void ApplyFallGravity(Vector3 velocity)
        {
            // 접지 상태에서 얹으면 경사면에서 발이 바닥에 박히듯 눌린다. 올라가는 중에는
            // 애초에 대상이 아니다 — 그 구간을 그대로 두는 것이 이 함수의 목적이다.
            if (_isGrounded || velocity.y >= 0f) return;

            float extra = Physics.gravity.y * (fallGravityMultiplier - 1f);
            _rb.AddForce(new Vector3(0f, extra, 0f), ForceMode.Acceleration);
        }

        private void Update()
        {
            if (_animator == null) return;
            // 애니메이터는 실제 m/s 값을 그대로 받는다. Blend Tree 문턱값을
            // walkSpeed·sprintSpeed와 같은 숫자로 맞추면 튜닝값이 두 곳에서 어긋나지 않는다.
            _animator.SetFloat(SpeedHash, _horizontalSpeed);
            _animator.SetBool(GroundedHash, _isGrounded);
            _animator.SetBool(FallingHash, _falling);
            // AirIdle 진입에 쓴다 — Grounded 하나만 보면 인도 턱 같은 한 스텝짜리 순간
            // 접지 끊김까지 잡아버린다. Falling이 시간+속도로 이 문제를 피하는 것과
            // 같은 이유로, AirIdle 쪽도 raw !Grounded 대신 누적 공중 시간에 문턱을 둔다.
            _animator.SetFloat(AirTimeHash, _airTime);
        }

        /// <summary>
        /// 입력을 카메라의 수평 방향 기준으로 회전시킨다. 카메라 피치는 무시한다.
        ///
        /// <c>internal</c>인 이유는 <see cref="SwingConstraint"/>가 같은 계산을 그대로 쓰기
        /// 때문이다. "입력이 카메라 기준으로 어느 방향인가"는 지상이든 공중이든 같은 질문이고,
        /// 답이 두 곳에 따로 있으면 카메라 설정을 바꿀 때 한쪽만 고치는 사고가 난다.
        /// </summary>
        internal Vector3 CameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            if (cameraTransform == null)
                return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = cameraTransform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 dir = forward * input.y + right * input.x;
            return dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }
    }
}
