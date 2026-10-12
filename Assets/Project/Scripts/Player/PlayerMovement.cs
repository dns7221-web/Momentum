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

        [Header("착지 예측 — 착지 애니메이션을 닿기 전에 미리 튼다")]
        [Tooltip("Grounded는 발이 실제로 닿은 뒤에야 참이 된다. 그 신호로 착지 클립을 걸면 " +
                 "항상 닿은 다음에야 재생을 시작해 순서가 거꾸로 보인다 — 이게 그 문제를 " +
                 "잡는 값이다. 발밑으로 이 시간만큼 미리 레이를 쏴서, 그만큼 앞서 착지를 " +
                 "예고한다. 낙하 속도가 빠를수록 같은 시간이라도 더 먼 거리를 본다.")]
        [Min(0f)][SerializeField] private float landingAnticipationTime = 0.2f;

        [Tooltip("착지 예측 스윕이 이 거리 안에서만 바닥을 찾는다. 시간 문턱 " +
                 "(landingAnticipationTime)이 실제로 걸러주므로 넉넉히 두면 된다 — " +
                 "너무 짧으면 빠른 낙하·스윙에서 그 안에 바닥을 못 찾아 예고가 아예 안 켜진다.")]
        [Min(1f)][SerializeField] private float landingProbeMaxDistance = 40f;

        [Tooltip("접지 상태에서 이보다 작은 상향 속도는 매 스텝 0으로 지운다. " +
                 "인도 턱 같은 단차에서 depenetration이 얹는 잔여 튐을 지우기 위한 값이다.\n\n" +
                 "jumpSpeed보다 확실히 낮게 둘 것 — 점프 직후 한 프레임 접지 신호가 " +
                 "남았을 때 이 값이 점프 속도까지 지워버리면 점프가 거의 안 뛰는 것처럼 된다.")]
        [Min(0f)][SerializeField] private float residualLiftClamp = 2f;

        [Header("턱 오르기 — 인도 턱에 걸려 못 넘어가는 것을 푼다")]
        [Tooltip("이 높이 아래에서만 닿은 장애물은 '넘을 수 있는 턱'으로 본다. " +
                 "이 키트의 인도 턱이 15cm이므로 그보다 넉넉히, 다만 무릎보다는 낮게.")]
        [Min(0f)][SerializeField] private float stepHeight = 0.35f;

        [Tooltip("턱을 만났을 때 위로 주는 속도(m/s).\n\n" +
                 "<b>residualLiftClamp보다 반드시 커야 한다.</b> 접지 상태에서 그 값보다 작은 " +
                 "상승 속도는 매 스텝 0으로 지워지므로, 작게 주면 여기서 준 속도가 바로 " +
                 "그 클램프에 먹혀 영영 못 올라간다 — 실제로 그게 턱에 걸리던 원인이었다.")]
        [Min(0f)][SerializeField] private float stepUpSpeed = 3f;

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

        [Tooltip("LandingSoon이 켜진 순간부터 실제 Grounded가 켜질 때까지 걸린 시간을 " +
                 "콘솔에 찍는다. 히어로 랜딩이 착지 전에 미리 안 보이는 것 같을 때, 신호가 " +
                 "너무 늦게 켜지는 건지 아니면 클립·전이 쪽 문제인지 가릴 때 켤 것.")]
        [SerializeField] private bool logLandingSoon = false;

        [Tooltip("몸을 <b>위로 띄우는 코드가 발동할 때마다</b> 어느 쪽이 띄웠는지 콘솔에 찍는다. " +
                 "이 프로젝트에서 위로 솟는 경로는 넷이다 — 점프, 턱 넘기(stepUp), 스윙 릴리즈, " +
                 "견인/체이스 도착 부스트. '떠오르는데 왜 뜨는지 모르겠다'는 상황에서 넷 중 " +
                 "누가 범인인지 한 번에 가른다. 다른 세 스크립트(SwingConstraint·GrappleZip·" +
                 "WebGrabThrow)도 이 스위치 하나를 같이 본다.")]
        [SerializeField] private bool logLift = false;

        /// <summary>위로 띄우는 경로들이 공용으로 보는 디버그 스위치. 같은 오브젝트에 붙은
        /// 다른 스크립트들이 <c>_movement.LogLift</c>로 읽는다 — 체크박스를 넷으로 늘리지
        /// 않으려는 것뿐이다.</summary>
        public bool LogLift => logLift;
        private float _landingSoonSetTime = -1f;
        private float _landingSoonFirstSetTime = -1f;
        private int _landingSoonFlipCount;
        private bool _prevLandingSoon;

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
        private static readonly int SwingLandingHash = Animator.StringToHash("SwingLanding");
        private static readonly int LandingSoonHash = Animator.StringToHash("LandingSoon");

        /// <summary>
        /// 직전 공중 구간이 스윙에서 시작됐는가. 착지 순간 일반 착지와 스윙 착지를 가른다.
        ///
        /// 착지할 때는 이미 스윙이 끝난 뒤라 <c>SuspendHorizontalControl</c>로는 알 수 없다.
        /// 놓는 순간 켜두고 <b>땅에 닿을 때 끈다</b> — 그 사이가 곧 "스윙에서 내려오는 중"이다.
        /// </summary>
        private bool _swingLanding;
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int FallingHash = Animator.StringToHash("Falling");

        private Rigidbody _rb;
        private Animator _animator;

        /// <summary>이번 물리 스텝에 바닥과 닿아 있는지. 충돌 콜백이 채운다.</summary>
        private bool _isGrounded;

        /// <summary>
        /// <see cref="_isGrounded"/>를 밖에 연다. <see cref="SwingConstraint"/>가 릴리즈
        /// 순간 이미 땅에 서 있는지 판단할 때 쓴다 — 이미 접지 중이면 공중 자세(AirIdle)를
        /// 강제로 씌울 이유가 없다.
        /// </summary>
        public bool IsGrounded => _isGrounded;

        // 추가 생성
        /// <summary>
        /// 점프 입력 액션(스페이스바). <see cref="GrappleZip"/>이 공중 웹 집 칸이 비어 있으면 빌려 간다 —
        /// 공중에서 누른 점프는 어차피 버려지므로(FixedUpdate 참고) 같은 키를 땅/공중으로 나눠 쓴다.
        /// </summary>
        public InputActionReference JumpAction => jumpAction;

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

        /// <summary>
        /// 이번 스텝에 <b>낮은 데만</b> 닿은 장애물이 있었는지. <see cref="_groundContact"/>와
        /// 같은 방식으로 콜백이 채우고 FixedUpdate가 읽은 뒤 끈다.
        /// </summary>
        private bool _lowObstacleContact;

        /// <summary>턱을 넘느라 뜬 구간의 끝. 이때까지는 공중이어도 낙하로 세지 않는다.</summary>
        private float _stepGraceUntil;

        private bool _jumpRequested;
        private float _horizontalSpeed;
        private CapsuleCollider _capsule;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // 턱 높이 판정의 기준선(발밑)을 잡는 데 쓴다.
            _capsule = GetComponent<CapsuleCollider>();
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
            // 소품은 <b>턱이 아니다</b>. 잡으려고 다가가다 살짝 스치기만 해도 "낮은 턱"으로
            // 오인해 턱 넘기 도약(velocity.y = stepUpSpeed)이 발동하고, 그게 매 스텝 다시
            // 걸리면서 계단 오르듯 몸이 떠오른다 — 소품을 잡을 때 캐릭터가 뜨던 원인이 이것.
            //
            // <b>레이어가 아니라 Rigidbody 유무로 가른다.</b> 처음엔 Grabbable 레이어로만
            // 걸렀는데 실제로는 안 걸러졌다(로그로 확인 — stepUp이 그대로 3연속 발동). 건물·
            // 지면은 정적이라 attachedRigidbody가 없고, 굴러다니는 소품은 반드시 있다. 이름이
            // 아니라 성질로 가르므로 레이어 설정이 어긋나도 안 샌다.
            //
            // 여기서 <c>return</c>하지 않는 것이 중요하다 — 소품 <b>위에 올라섰을 때</b>는
            // 그게 정상적인 발판이므로 접지(_groundContact)는 그대로 인정해야 한다. 막으려는
            // 것은 "옆면에 부딪힌 것을 턱으로 오인하는 것" 하나뿐이다.
            bool isLoose = collision.rigidbody != null;

            // 이 콜라이더가 "넘을 수 있는 턱"인지 판정하려면 한 접촉점만 봐서는 안 된다.
            // 낮은 데만 닿았으면 턱이고, 위쪽에도 닿았으면 벽이다 — 그래서 이 콜라이더의
            // 접촉을 전부 훑은 뒤에 결론을 낸다. 벽에도 낮은 접촉점은 있기 때문에
            // "낮은 곳에 닿았다"만으로 판정하면 벽을 기어오르게 된다.
            float stepCeiling = (_capsule != null ? _capsule.bounds.min.y : _rb.position.y) + stepHeight;
            bool touchedLow = false;
            bool touchedHigh = false;

            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);

                // 접촉면의 법선이 충분히 위를 향하면 바닥, 아니면 벽이다.
                // 이 한 줄이 벽에 붙어 무한히 점프하던 문제를 막는다.
                if (Vector3.Angle(contact.normal, Vector3.up) <= maxSlopeAngle)
                {
                    _groundContact = true;
                    continue; // 아래 턱 판정도 마저 해야 하므로 여기서 빠져나가지 않는다
                }

                // 소품 옆면은 턱으로 세지 않는다(위 isLoose 설명 참고).
                if (isLoose) continue;

                if (contact.point.y < stepCeiling) touchedLow = true;
                else touchedHigh = true;
            }

            if (touchedLow && !touchedHigh) _lowObstacleContact = true;
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

            bool wasGrounded = _isGrounded;
            _isGrounded = _groundContact;

            // 발이 땅에서 떨어지는 바로 그 순간을 찍는다. 위 네 경로("[띄움] ...")가 이 줄
            // 직전에 하나도 안 찍혔는데 여기만 뜨면, 띄운 것은 코드가 아니라 물리다 —
            // 겹침 밀어내기(depenetration)나 콜라이더에 올라타진 것. 그 둘은 고치는 곳이
            // 전혀 다르므로 먼저 갈라야 한다.
            if (logLift && wasGrounded && !_isGrounded)
                Debug.Log($"[띄움] ▲ 이륙 — 이 순간 y속도 {_rb.linearVelocity.y:F2}, " +
                          $"높이 {transform.position.y:F2}m. 바로 위에 다른 [띄움] 줄이 " +
                          "없으면 코드가 아니라 물리가 띄운 것이다.", this);

            if (logLandingSoon && !wasGrounded && _isGrounded && _landingSoonSetTime >= 0f)
            {
                // [SweepTest 빌드] 태그를 넣어둔다 — 이 태그 없이 옛 로그 형식이 그대로
                // 찍히면 새 코드가 아니라 옛 컴파일 결과가 도는 것이니 재컴파일부터 의심할 것.
                //
                // "마지막"과 "맨 처음" 켜진 시점을 같이 찍는다 — 깜빡였는지(flip 2회 이상)
                // 가리기 위해서다. 둘이 다르면 착지 직전에 LandingSoon이 껐다 켜졌다 했다는
                // 뜻이고, 애니메이션이 실제로 반응하는 건 "마지막" 쪽이라 그게 원래 재던 값이다.
                Debug.Log($"[PlayerMovement][SweepTest 빌드] LandingSoon → Grounded: " +
                          $"마지막 켜짐 기준 {Time.time - _landingSoonSetTime:F2}초, " +
                          $"맨 처음 켜짐 기준 {Time.time - _landingSoonFirstSetTime:F2}초, " +
                          $"깜빡인 횟수 {_landingSoonFlipCount}회 (예고 시간 {landingAnticipationTime}초)", this);
                _landingSoonSetTime = -1f;
                _landingSoonFirstSetTime = -1f;
                _landingSoonFlipCount = 0;
            }
            bool lowObstacle = _lowObstacleContact;
            // 다음 스텝의 충돌 콜백이 다시 채운다. 발이 떨어지면 아무도 안 채우므로 false로 남는다.
            _groundContact = false;
            _lowObstacleContact = false;

            // 스윙을 막 놓은 프레임인지. 아래 두 곳에서 쓴다.
            bool justReleasedSwing = _wasSuspended && !SuspendHorizontalControl;
            _wasSuspended = SuspendHorizontalControl;

            // 놓는 순간 켜고 땅에 닿을 때 끈다 — 그 사이가 "스윙에서 내려오는 중"이다.
            if (justReleasedSwing) _swingLanding = true;
            else if (_isGrounded) _swingLanding = false;

            if (_isGrounded)
            {
                _airTime = 0f;
                _falling = false;
            }
            else if (Time.time < _stepGraceUntil)
            {
                // 턱을 넘느라 잠깐 뜬 것은 낙하가 아니다. stepUpSpeed로 실제로 몸이
                // 떠오르므로 접지가 끊기는데, 그 체공 시간(3m/s·중력 14.7에서 약 0.41초)이
                // AirIdle 문턱(0.25초)을 넘겨 턱을 넘을 때마다 공중 자세가 나왔다.
                // 스스로 올라타려고 준 속도이므로 여기서는 낙하로 세지 않는다.
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

                    // 낮은 턱에 막혀 있으면 올라탄다. <b>위 클램프보다 뒤에 와야 한다</b> —
                    // 앞에 두면 여기서 준 속도를 그 클램프가 곧바로 지운다.
                    //
                    // 실제로 그게 "인도 턱에 걸려 못 넘어간다"의 원인이었다. 턱(15cm)을
                    // 넘으려면 √(2gh) ≈ 2.1m/s가 필요한데 클램프가 2였고, 솔버가 밀어주는
                    // 상승분은 그보다 훨씬 잘게 쌓이므로 매 스텝 전부 0이 됐다. 그렇게
                    // 붙잡혀 있는 동안 접지가 끊겼다 붙었다 하면서 낙하 판정까지 켜져
                    // 공중 애니메이션이 나오던 것이 같은 원인의 다른 증상이다.
                    // 한 번 도약했으면 그 도약이 끝날 때까지 다시 걸지 않는다. 이게 없으면
                    // 턱에 닿아 있는 동안 매 물리 스텝 3m/s가 <b>새로</b> 대입돼(로그에서 3연속
                    // 확인), 중력이 깎을 틈이 없어 사실상 계단을 오르듯 계속 떠오른다.
                    // _stepGraceUntil은 원래 "이 도약은 낙하가 아니다"를 표시하려고 두었던
                    // 값인데, 도약이 진행 중인 구간을 이미 정확히 가리키고 있으므로 재발동
                    // 금지에도 그대로 쓴다 — 새 값을 만들면 둘이 어긋날 여지만 생긴다.
                    if (lowObstacle && moveDir.sqrMagnitude > 0.0001f && Time.time >= _stepGraceUntil)
                    {
                        velocity.y = stepUpSpeed;

                        if (logLift)
                            Debug.Log($"[띄움] 턱 넘기(stepUp) — y속도 {stepUpSpeed:F2} 대입, " +
                                      $"높이 {transform.position.y:F2}m", this);

                        // 이 도약이 끝날 때까지는 낙하로 세지 않는다. 시간을 상수로 박지 않고
                        // 여기서 계산하는 이유는, 속도나 중력을 바꾸면 체공 시간도 따라
                        // 달라지기 때문이다 — 값이 두 군데로 갈라져 어긋나는 것을 막는다.
                        float hopTime = 2f * stepUpSpeed / Mathf.Max(1f, Physics.gravity.magnitude);
                        _stepGraceUntil = Time.time + hopTime + 0.05f;
                    }
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

                if (logJumps || logLift)
                    Debug.Log($"[띄움] 점프 — y속도 {jumpSpeed:F2} 대입, " +
                              $"높이 {transform.position.y:F2}m", this);
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

            UpdateLandingAnticipation(velocity);
        }

        /// <summary>
        /// 착지가 곧 임박했는지 미리 잰다. "곧 임박"의 기준은 <b>거리가 아니라 시간</b>이다.
        ///
        /// <b>짐작이 아니라 역산이다.</b> 예전엔 "0.2초 뒤 위치를 짐작 → 거기로 레이를
        /// 쏴서 맞았는지 본다" 방식이었는데, 등속·수평 이동 같은 근사가 하나씩 늘어도
        /// 실측(로그)에서 예고 시간이 0.03~0.32초로 계속 흔들렸다. 이 게임의 낙하 자세는
        /// 몸 전체가 앞으로 기울어 바닥을 보는 다이빙 포즈라, 발밑만 보는 근사로는
        /// 애초에 "지금 정말 향하고 있는 곳"과 어긋나기 쉽다.
        ///
        /// 그래서 순서를 뒤집는다 — <b>먼저 실제 거리를 재고, 그 거리에서 도달 시간을
        /// 역산한다.</b> <see cref="Rigidbody.SweepTest"/>(유니티 내장)로 지금 속도 방향
        /// 그대로 캡슐을 밀어봤을 때 처음 걸리는 지점까지의 진짜 거리를 얻는다 — 캡슐
        /// 반지름·바운즈를 손으로 재구성할 필요가 없고, 실제 붙어 있는 콜라이더 모양을
        /// 그대로 쓴다. 그 거리에서 <b>수직 낙하 성분</b>만 뽑아 등가속도 공식
        /// <c>h = v·t + ½·g·t²</c>를 <b>t에 대해 직접 푼다</b>(이차방정식 해) — 짐작해서
        /// 맞았는지 확인하는 게 아니라 정확한 도달 시각을 계산하므로, 낙하 초반이든
        /// 막바지든 근사 오차가 낄 자리가 없다.
        ///
        /// 접지 중이거나 위로 가는 중에는 검사하지 않는다 — 착지를 예고할 이유가 없다.
        /// </summary>
        private void UpdateLandingAnticipation(Vector3 velocity)
        {
            if (_isGrounded || velocity.y >= 0f)
            {
                _animator?.SetBool(LandingSoonHash, false);
                _prevLandingSoon = false;
                return;
            }

            Vector3 direction = velocity.normalized;
            bool landingSoon = false;

            if (_rb.SweepTest(direction, out RaycastHit hit, landingProbeMaxDistance,
                               QueryTriggerInteraction.Ignore)
                && Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle)
            {
                // 벽은 걸러낸다. 스윙 중 건물에 부딪혀 튕겨 나오는 순간처럼 velocity
                // 방향에 벽면이 바짝 붙어 있으면, 그 벽까지의 거리가 아주 짧게 잡혀
                // "착지가 임박했다"로 오판한다 — 바닥에 닿는 게 아니라 벽에 부딪힌
                // 것뿐인데도 공중에서 히어로 랜딩이 튀어나오던 원인이 이것이다. 바닥이냐
                // 벽이냐를 가르는 기준은 이 파일이 이미 쓰는 것(<see cref="maxSlopeAngle"/>,
                // 충돌 콜백에서 접지 판정에 쓰는 것과 같은 값)을 그대로 재사용한다.
                //
                // hit.point는 부딪힌 쪽 표면 위의 점이라 캡슐 반지름만큼 어긋날 수 있다.
                // 대신 "그 지점에 닿을 때까지 실제로 이동한 거리(hit.distance)"에서
                // 수직 성분만 뽑는다 — 스윕 방향(velocity 방향)의 y 비율을 곱하면 된다.
                float verticalDrop = hit.distance * -direction.y;

                if (verticalDrop > 0f)
                {
                    float downSpeed = -velocity.y;
                    float fallAcceleration = Mathf.Abs(Physics.gravity.y) * fallGravityMultiplier;

                    // h = v·t + ½·g·t² 를 t에 대해 푼 근의 공식. fallAcceleration > 0이
                    // 보장되므로(중력이 0일 순 없다) 분모 걱정은 없다.
                    float discriminant = downSpeed * downSpeed + 2f * fallAcceleration * verticalDrop;
                    float timeToImpact = (-downSpeed + Mathf.Sqrt(discriminant)) / fallAcceleration;

                    landingSoon = timeToImpact <= landingAnticipationTime;
                }
            }

            _animator?.SetBool(LandingSoonHash, landingSoon);

            if (logLandingSoon && landingSoon && !_prevLandingSoon)
            {
                // _landingSoonSetTime은 "가장 최근에 켜진 시점"이라 매번 덮어쓴다 — 착지
                // 전이(AddLandingTransitions)가 실제로 반응하는 것도 그 최신 값이므로
                // 애니메이션 타이밍과는 이게 맞다. 깜빡였는지 가리려고
                // _landingSoonFirstSetTime은 이번 낙하의 <b>맨 처음</b> 켜진 시점만 남긴다.
                _landingSoonSetTime = Time.time;
                if (_landingSoonFlipCount == 0) _landingSoonFirstSetTime = Time.time;
                _landingSoonFlipCount++;
            }
            _prevLandingSoon = landingSoon;
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
            _animator.SetBool(SwingLandingHash, _swingLanding);
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
