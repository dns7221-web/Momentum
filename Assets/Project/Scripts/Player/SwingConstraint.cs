using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 로프 거리 구속 — 스윙 그 자체.
    ///
    /// 하는 일은 매 물리 스텝에 두 가지뿐이다.
    /// <list type="number">
    /// <item>앵커까지의 거리가 로프 길이를 넘으면 그만큼 되당긴다</item>
    /// <item>속도에서 <b>반지름 방향 성분을 빼 접선 성분만 남긴다</b></item>
    /// </list>
    ///
    /// 진자 공식은 어디에도 없다. 중력이 매 스텝 속도를 아래로 밀고 구속이 반지름 성분을
    /// 잘라내면, 남는 것은 원호를 따라 도는 운동이다 — 진자는 가둬두면 저절로 나온다.
    /// 주기와 최저점 속도를 코드가 계산하지 않는 이유가 이것이고, <c>SwingMetrics</c>가
    /// 순수 함수로 따로 있는 이유이기도 하다. 그쪽은 예측이고 이쪽은 시뮬레이션이다.
    ///
    /// <c>SpringJoint</c>를 쓰지 않은 것은 README 설계 결정 1번이다. 줄이 늘어나면 "탁 걸리는"
    /// 순간이 뭉개지고, 무엇보다 주제가 물리인데 Joint를 쓰면 물리를 남에게 맡긴 것이 된다.
    ///
    /// 이 두 가지 위에 감기(<see cref="ApplyReelVelocity"/>)와 펌프·조종
    /// (<see cref="ApplyPumpInput"/>)이 얹힌다. 둘 다 구속을 대체하지 않고 매 스텝
    /// <c>SolveRope</c>보다 먼저 속도만 건드린 뒤 구속에 넘긴다 — 순수 진자로는 놓기 전까지
    /// 아무 조작이 안 되는데, 스파이더맨 식 스윙은 매달린 채로 계속 힘을 넣어 진폭을 키우고
    /// 방향을 트는 것이 핵심이라 그 "그네 구르기"를 이 두 층이 담당한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SwingConstraint : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커를 물어다 주는 쪽. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        [Header("구속")]
        [Tooltip("한 스텝에 구속을 몇 번 푸는지. 구속이 하나뿐인 지금은 1회로 정확해서 " +
                 "값을 올려도 달라지지 않는다. 전투에서 구속이 둘 이상이 되면 그때부터 값을 한다.")]
        [Min(1)][SerializeField] private int iterations = 1;

        [Tooltip("로프가 이보다 짧아지지 않게 막는다. 앵커에 얼굴을 박는 것을 막을 뿐 아니라 " +
                 "감기 증폭의 상한을 정한다 — 22m에서 3m까지 감으면 접선 속도가 7.3배가 된다.")]
        [Min(0.1f)][SerializeField] private float minRopeLength = 3f;

        [Tooltip("속도 상한. 설계값이 아니라 안전장치다. 짧게 감을수록 증폭이 커지므로 " +
                 "상한이 없으면 한 스텝에 건물을 뚫고 나가는 속도가 나온다.")]
        [Min(1f)][SerializeField] private float maxSpeed = 60f;

        [Header("감기 (InputSystem_Actions ▸ Player ▸ Reel)")]
        [SerializeField] private InputActionReference reelAction = null;
        [Tooltip("휠 한 칸에 목표 길이가 몇 m 바뀌는가. 실제 길이는 아래 속도로 그 목표를 따라간다.")]
        [Min(0.1f)][SerializeField] private float lengthPerNotch = 2f;
        [Tooltip("실제 로프가 목표 길이를 따라가는 속도(m/s). 이 값이 곧 벽을 타고 오르는 속도다.")]
        [Min(0.1f)][SerializeField] private float reelSpeed = 8f;

        [Header("애니메이션")]
        [Tooltip("Swing 레이어 가중치가 오르내리는 속도(초당). 0이면 즉시 전환된다 — " +
                 "붙는 순간 자세가 뚝 끊겨서 대개 부자연스럽다.")]
        [Min(0f)][SerializeField] private float animLayerLerpSpeed = 8f;
        [Tooltip("이 높이(월드 Y, 지면 위 미터와 거의 같다)를 넘으면 스윙 자세 대신 " +
                 "닌자 점프 공중 자세를 보여준다. 큰 감기 증폭이나 강한 펌프로 정상 아치 " +
                 "범위(최고 건물 28m 안팎)를 넘어 튀어 올랐을 때를 잡아내는 값이다.")]
        [SerializeField] private float highFlightHeight = 30f;

        [Header("스윙 자세 — 몸을 로프에 맞춰 기울인다")]
        [Tooltip("기울일 대상. 비워두면 Animator가 붙은 트랜스폼을 쓴다 — 그게 곧 캐릭터 " +
                 "메시의 뿌리라 뼈대와 총이 전부 딸려온다. 물리 캡슐이 아니라 이쪽을 돌린다.")]
        [SerializeField] private Transform visual = null;
        [Tooltip("초당 최대 회전 각도. 0이면 즉시 맞춘다 — 붙는 순간 몸이 홱 돌아 부자연스럽다.")]
        [Min(0f)][SerializeField] private float visualTurnSpeedDegPerSec = 360f;
        [Tooltip("로프 정렬 강도. 1이면 머리가 완전히 로프를 향하고, 0이면 수직으로 선 채 " +
                 "진행 방향만 본다. 스윙 클립 자체에 이미 기울어진 자세가 구워져 있으므로, " +
                 "1로 두면 두 번 기울어 과해 보일 수 있다 — 그때 이 값으로 덜어낸다.")]
        [Range(0f, 1f)][SerializeField] private float ropeAlignment = 1f;

        [Header("펌프 · 조종 (InputSystem_Actions ▸ Player ▸ Move, PlayerMovement와 같은 액션)")]
        [Tooltip("PlayerMovement가 켜고 끄는 액션을 그대로 참조만 한다 — 여기서 별도로 " +
                 "Enable/Disable하지 않는다. 둘 다 같은 Player 오브젝트에 있어 생명주기가 같다.")]
        [SerializeField] private InputActionReference moveAction = null;
        [Tooltip("진행 방향으로 미는 가속도(m/s²). 중력(29.43)보다 작아야 그네를 구르는 느낌이 " +
                 "유지된다 — 이보다 크면 물리보다 입력이 주도권을 쥐어 비행처럼 보인다.")]
        [Min(0f)][SerializeField] private float pumpAcceleration = 18f;
        [Tooltip("진행에 수직으로 트는 가속도(m/s²). 스윙 평면을 회전시켜 방향을 조종한다. " +
                 "펌프보다 약하게 두는 편이 좋다 — 세면 그네가 아니라 방향 전환 게임이 된다.")]
        [Min(0f)][SerializeField] private float steerAcceleration = 9f;

        /// <summary>현재 로프 길이. 구속이 실제로 쓰는 값이다.</summary>
        public float RopeLength { get; private set; }

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private bool _swinging;

        /// <summary>휠이 바꾸는 값. 실제 길이가 이쪽으로 천천히 따라간다.</summary>
        private float _targetLength;

        /// <summary>붙은 순간의 길이. 그보다 길게 풀지는 못한다.</summary>
        private float _maxLength;

        // ─── 애니메이션 ───
        //
        // PlayerMovement가 Speed·Grounded를 갖고 있듯, 여기서도 스윙이 스스로 자신의
        // Animator 파라미터를 갖는다. 물리를 계산하는 쪽이 곧 그 결과를 가장 정확히 아는
        // 쪽이라, 다른 스크립트를 거쳐 전달하면 한 프레임 지연이나 값 어긋남이 생길 자리가
        // 하나 늘어날 뿐이다.

        private Animator _animator;
        private const string SwingLayerName = "Swing";
        private static readonly int ReelingHash = Animator.StringToHash("Reeling");
        private static readonly int HighFlightHash = Animator.StringToHash("HighFlight");

        /// <summary>Swing 레이어의 인덱스. 레이어는 이름이 아니라 인덱스로 제어하는 API라 미리 찾아둔다.</summary>
        private int _swingLayerIndex = -1;

        /// <summary>
        /// 대기 상태에서 시각 레이어가 캡슐에 대해 갖고 있던 로컬 회전.
        ///
        /// 놓은 뒤 돌아갈 자리다. 항등이라고 가정하면 메시가 캡슐에 대해 조금이라도 틀어져
        /// 붙어 있는 프리팹에서 스윙 한 번에 그 틀어짐이 영구히 사라진다.
        /// </summary>
        private Quaternion _visualLocalRest = Quaternion.identity;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            if (gun == null) gun = GetComponent<GrappleGun>();

            if (gun == null)
                Debug.LogWarning("[SwingConstraint] GrappleGun이 없어 스윙할 수 없다.", this);

            // 스킨 메시는 자식 프리팹이다 — PlayerMovement가 하는 것과 같은 방식으로 찾는다.
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _swingLayerIndex = _animator.GetLayerIndex(SwingLayerName);
                if (_swingLayerIndex < 0)
                    Debug.LogWarning("[SwingConstraint] Animator에 'Swing' 레이어가 없다. " +
                                     "PlayerAnimatorBuilder로 컨트롤러를 다시 만들 것.", this);
            }

            // 기울일 대상도 같은 자리에서 찾는다. Animator가 붙은 트랜스폼이 곧 캐릭터
            // 메시의 뿌리이고, 뼈대·리그·손에 쥔 총이 전부 그 밑에 있다.
            if (visual == null && _animator != null) visual = _animator.transform;
            if (visual != null) _visualLocalRest = visual.localRotation;
        }

        private void OnEnable()
        {
            reelAction?.action.Enable();
        }

        private void OnDisable()
        {
            reelAction?.action.Disable();

            // 스윙 중에 컴포넌트가 꺼지면 이동 쪽 스위치가 켜진 채로 남는다.
            // 그러면 갈고리를 놓아도 영영 조작이 안 먹는다.
            if (_swinging) EndSwing();
        }

        /// <summary>
        /// 휠을 읽어 <b>목표</b> 길이만 바꾼다. 실제 길이는 건드리지 않는다.
        ///
        /// 두 단계로 나눈 이유는 휠이 이산 입력이기 때문이다. 한 칸 굴릴 때마다 길이를 즉시
        /// 2m 줄이면 로프가 계단처럼 뛰고 그 순간 몸이 튄다. 목표만 옮겨두고 실제 길이가
        /// 속도 제한을 받으며 따라가면, 톡톡 끊기는 입력이 연속 동작으로 나온다.
        ///
        /// <c>FixedUpdate</c>가 아니라 <c>Update</c>에서 읽는 것도 이유가 있다. 휠 값은
        /// 프레임 단위 델타인데 물리 스텝이 0.005초라 한 프레임에 <c>FixedUpdate</c>가 서너 번
        /// 돈다. 거기서 읽으면 한 번 굴린 것이 서너 칸으로 계산된다.
        /// </summary>
        private void Update()
        {
            UpdateReelInput();
            UpdateAnimator();
            UpdateVisualRotation();
        }

        private void UpdateReelInput()
        {
            if (!_swinging || reelAction == null) return;

            float input = reelAction.action.ReadValue<float>();
            if (Mathf.Abs(input) < 0.01f) return;

            // 값의 크기가 아니라 부호만 쓴다. 휠 델타는 플랫폼마다 단위가 달라
            // (윈도우는 한 칸에 120) 크기를 그대로 믿으면 다른 기계에서 감기 속도가 달라진다.
            _targetLength = Mathf.Clamp(_targetLength - Mathf.Sign(input) * lengthPerNotch,
                                        minRopeLength, _maxLength);
        }

        /// <summary>
        /// Swing 레이어 가중치와 <c>Reeling</c>을 갱신한다.
        ///
        /// <c>_swinging</c>이 꺼진 뒤에도 이 함수는 계속 돌아야 한다. 그래야 놓는 순간
        /// 가중치가 뚝 0으로 끊기지 않고 <c>animLayerLerpSpeed</c>를 따라 서서히 걷기 자세로
        /// 녹아든다. 그래서 <see cref="UpdateReelInput"/>과 달리 <c>_swinging</c> 여부로
        /// 통째로 리턴하지 않는다.
        /// </summary>
        private void UpdateAnimator()
        {
            if (_animator == null || _swingLayerIndex < 0) return;

            float targetWeight = _swinging ? 1f : 0f;
            float current = _animator.GetLayerWeight(_swingLayerIndex);
            float next = animLayerLerpSpeed <= 0f
                ? targetWeight
                : Mathf.MoveTowards(current, targetWeight, animLayerLerpSpeed * Time.deltaTime);
            _animator.SetLayerWeight(_swingLayerIndex, next);

            // 감고 있지 않을 때 목표 길이를 향해 실제 길이가 아직 줄어드는 중이면 그게 곧
            // "당겨서 끌려가는 중"이다. 별도 상태 변수를 안 두고 두 길이의 차이로 판정하면,
            // MoveTowards가 목표에 도착하는 순간 자동으로 꺼진다 — 수동으로 끌 일이 없다.
            bool reeling = _swinging && RopeLength - _targetLength > 0.01f;
            _animator.SetBool(ReelingHash, reeling);

            // 이 도시는 지면이 거의 월드 Y=0에 붙어 있어(도시 생성기가 그렇게 만든다),
            // 레이캐스트로 지면을 다시 찾을 필요 없이 위치의 Y값 자체가 곧 지면 위 높이다.
            bool highFlight = _swinging && _rb.position.y >= highFlightHeight;
            _animator.SetBool(HighFlightHash, highFlight);
        }

        /// <summary>
        /// 몸을 로프에 맞춰 기울인다 — 다림추처럼 매달려 있던 문제의 해결책.
        ///
        /// <b>물리 캡슐이 아니라 시각 레이어를 돌린다.</b> 캡슐을 기울이면 콜라이더가 같이
        /// 누워 접지 판정과 착지가 통째로 흔들리는데, 이 프로젝트는 처음부터 "캡슐이 물리
        /// 몸통이고 스킨 메시는 그 위에 얹히는 시각 레이어"라는 구조라 (<see cref="PlayerMovement"/>
        /// 클래스 주석) 기울임도 시각 레이어에서 끝내는 것이 맞다. 캡슐은 계속 수직이다.
        ///
        /// <c>Rigidbody.freezeRotation</c>을 풀어서 될 일이 아니었다. 거리 구속은 위치와
        /// 선속도만 건드리고 <b>토크를 한 번도 주지 않으므로</b>, 회전을 물리에 넘겨도
        /// 로프가 몸을 돌려주지 않는다. 돌릴 각도를 여기서 직접 계산하는 이유가 이것이다.
        ///
        /// 축 둘은 이미 다 있는 값이다.
        /// <list type="bullet">
        /// <item><b>위</b>는 앵커 방향 — 매달린 것은 머리가 줄을 향한다</item>
        /// <item><b>앞</b>은 접선 속도 방향 — 실제로 가고 있는 쪽을 본다</item>
        /// </list>
        /// 접선 속도는 정의상 로프에 수직이므로 두 축이 직교한다. 따로 직교화할 것이 없다.
        ///
        /// <c>Update</c>에서 도는 것이 중요하다. 애니메이터 평가와 리그 계산이 모두 이 뒤에
        /// 오므로 손 본의 최종 위치가 이 회전을 반영하고, 그래야 <see cref="GrappleGun"/>이
        /// <c>LateUpdate</c>에서 긋는 로프가 같은 프레임의 총구에서 나간다.
        /// </summary>
        private void UpdateVisualRotation()
        {
            if (visual == null) return;

            if (!_swinging)
            {
                // 이미 제자리로 돌아왔으면 손을 뗀다. 계속 대입하면 이쪽 회전 속도가
                // 캡슐의 회전 속도(turnSpeedDegPerSec)를 붙잡아, 걷다가 방향을 홱 틀 때
                // 몸이 캡슐보다 늦게 도는 것이 보인다. 부모-자식 관계가 알아서 해주는
                // 일을 굳이 뺏어올 이유가 없다.
                if (Quaternion.Angle(visual.localRotation, _visualLocalRest) < 0.1f)
                {
                    visual.localRotation = _visualLocalRest;
                    return;
                }
            }

            // 놓은 뒤에는 캡슐이 다시 방향을 쥔다. 여기서 통째로 리턴하지 않는 이유는
            // 레이어 가중치와 같다 — 뚝 끊지 않고 같은 속도로 수직 자세에 녹아들게 한다.
            Quaternion target = _swinging && gun != null
                ? SwingPose(gun.AnchorPoint)
                : _rb.rotation * _visualLocalRest;

            visual.rotation = visualTurnSpeedDegPerSec <= 0f
                ? target
                : Quaternion.RotateTowards(visual.rotation, target,
                                           visualTurnSpeedDegPerSec * Time.deltaTime);
        }

        /// <summary>
        /// 이번 프레임의 스윙 자세. <see cref="UpdateVisualRotation"/>의 계산 부분만 떼어냈다.
        /// </summary>
        private Quaternion SwingPose(Vector3 anchor)
        {
            Vector3 toAnchor = anchor - _rb.position;
            if (toAnchor.sqrMagnitude < 0.0001f) return visual.rotation;

            Vector3 up = toAnchor.normalized;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 tangent = velocity - up * Vector3.Dot(velocity, up);

            Vector3 forward;
            if (tangent.sqrMagnitude > 0.01f)
            {
                forward = tangent.normalized;
            }
            else
            {
                // 막 붙어서 접선 속도가 거의 없을 때. 0벡터를 정규화하면 회전이 튀므로,
                // 지금 보고 있는 쪽을 접선면에 눕혀 그대로 쓴다 — 몸이 제자리를 지킨다.
                forward = visual.forward - up * Vector3.Dot(visual.forward, up);
                if (forward.sqrMagnitude < 0.0001f) return visual.rotation;
                forward.Normalize();
            }

            Quaternion aligned = Quaternion.LookRotation(forward, up);
            if (ropeAlignment >= 0.999f) return aligned;

            // 덜 기울이고 싶을 때 섞을 반대편 극단 — 진행 방향만 보고 수직으로 선 자세.
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 0.0001f) return aligned;

            return Quaternion.Slerp(Quaternion.LookRotation(flat.normalized, Vector3.up),
                                    aligned, ropeAlignment);
        }

        /// <summary>
        /// 물리 스텝마다 구속을 푼다.
        ///
        /// <c>Update</c>가 아닌 이유는 명확하다 — 여기서 손대는 것은 위치와 속도, 즉 물리량이다.
        /// 프레임 레이트에 따라 호출 횟수가 달라지면 같은 조작에서 다른 궤도가 나온다.
        /// </summary>
        private void FixedUpdate()
        {
            bool attached = gun != null && gun.IsAttached;

            if (attached && !_swinging) BeginSwing();
            else if (!attached && _swinging) EndSwing();

            if (!attached) return;

            Vector3 anchor = gun.AnchorPoint;

            // 실제 길이가 목표를 향해 정해진 속도로 따라간다. MoveTowards는 목표를 넘어가지
            // 않으므로 도착하면 저절로 멈춘다 — 도착 판정을 따로 쓸 필요가 없다.
            float previousLength = RopeLength;
            RopeLength = Mathf.MoveTowards(RopeLength, _targetLength, reelSpeed * Time.fixedDeltaTime);

            ApplyReelVelocity(anchor, previousLength);
            ApplyPumpInput(anchor);

            for (int i = 0; i < iterations; i++) SolveRope(anchor);
        }

        /// <summary>
        /// 붙는 순간 로프 길이를 확정한다.
        ///
        /// 고정값을 쓰면 안 된다. 22m로 박아두고 15m 떨어진 곳에 붙으면 줄이 늘어져 아무 일도
        /// 일어나지 않고, 반대로 짧게 잡으면 붙자마자 앵커 쪽으로 확 당겨져 튕긴다.
        /// <b>지금 이만큼 떨어져 있다</b>를 그대로 길이로 삼으면 어디에 붙든 즉시 팽팽하다.
        /// </summary>
        private void BeginSwing()
        {
            _swinging = true;
            RopeLength = Mathf.Max(minRopeLength, Vector3.Distance(_rb.position, gun.AnchorPoint));

            // 붙은 길이가 곧 최대 길이다. 그보다 길게 풀 수 있으면 앵커에 붙은 의미가 없어진다.
            _maxLength = RopeLength;
            _targetLength = RopeLength;

            // 지상 이동이 수평 속도를 덮어쓰지 못하게 막는다. 이걸 안 하면 진자가 만든
            // 접선 속도가 다음 스텝에 입력값으로 대체되어 스윙이 그 자리에서 죽는다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;
        }

        /// <summary>
        /// 놓는다. <b>속도에는 손대지 않는다.</b>
        ///
        /// 릴리즈의 쾌감은 벌어둔 속도가 그대로 남는 데서 온다. 여기서 감쇠를 한 줄이라도
        /// 넣으면 "지킨다" 축이 통째로 사라진다.
        /// </summary>
        private void EndSwing()
        {
            _swinging = false;
            RopeLength = 0f;
            if (_movement != null) _movement.SuspendHorizontalControl = false;
        }

        /// <summary>
        /// 줄을 감을 때 <b>속도</b>에 일어나는 일 — "당겨서 탄력받는" 감각의 정체다.
        ///
        /// 줄이 늘어나서 튕기는 게 아니다. 실제 로프를 감으면 두 가지가 동시에 일어난다.
        ///
        /// <b>하나, 접선 속도가 빨라진다.</b> 감는 힘이 일을 하기 때문이고, 결과는 각운동량
        /// 보존으로 나타난다 — <c>접선속도 × 반지름 = 일정</c>. 반지름이 절반이 되면 속도가
        /// 두 배다. 피겨 스케이터가 팔을 모으면 빨라지는 것과 같은 원리이고,
        /// 스윙 최저점에서 감았을 때 확 튀어나가는 것이 이것이다.
        ///
        /// <b>둘, 앵커 쪽으로 실제로 날아간다.</b> 감는 속도만큼 반지름 속도가 생긴다.
        /// 위치만 당기면 놓는 순간 그 자리에서 떨어지지만, 속도로 당기면 <b>놓아도 계속
        /// 날아간다</b> — 지상에서 벽에 쏘고 당겨 솟구치는 그림이 여기서 나온다.
        ///
        /// 풀 때(<paramref name="previousLength"/>보다 길어질 때)는 아무것도 하지 않는다.
        /// 줄을 늘어뜨리는 것은 일이 아니라서 속도를 줄 이유가 없다.
        /// </summary>
        private void ApplyReelVelocity(Vector3 anchor, float previousLength)
        {
            float shrink = previousLength - RopeLength;
            if (shrink <= 0f || RopeLength < 0.0001f) return;

            Vector3 toAnchor = anchor - _rb.position;
            float distance = toAnchor.magnitude;
            if (distance < 0.0001f) return;

            Vector3 direction = toAnchor / distance;
            Vector3 velocity = _rb.linearVelocity;

            float radial = Vector3.Dot(velocity, direction);
            Vector3 tangent = velocity - direction * radial;

            // 각운동량 보존. 한 스텝의 변화량은 감기 속도로 제한되어 있으므로 비율이 한 번에
            // 폭발하지 않고, 감은 총량만큼만 누적된다.
            tangent *= previousLength / RopeLength;

            // 감는 속도만큼은 앵커 쪽으로 간다. 이미 그보다 빠르면 건드리지 않는다 —
            // 스윙으로 벌어둔 속도를 감기가 깎으면 안 된다.
            float reelRate = shrink / Time.fixedDeltaTime;
            if (radial < reelRate) radial = reelRate;

            _rb.linearVelocity = Vector3.ClampMagnitude(tangent + direction * radial, maxSpeed);
        }

        /// <summary>
        /// 그네를 다리로 구르는 것 — 스윙 중에도 입력이 살아있게 한다.
        ///
        /// 구속만 있으면 초기 속도로만 흔들리는 순수 진자다. 스파이더맨 식 스윙은 매달린
        /// 채로 계속 힘을 넣어 진폭을 키우고 방향을 튼다. 그 "구르기"를 흉내 낸다.
        ///
        /// 새 입력을 만들지 않는다. <see cref="PlayerMovement.CameraRelativeDirection"/>가
        /// 지상에서 쓰던 그 계산을 그대로 가져와, 로프 방향 성분만 제거해 접선면에 투영한다.
        /// 접선면은 2차원이라 투영된 힘은 자연히 둘로 갈린다.
        ///
        /// <b>진행 방향과 같은 쪽</b> — 밀면 가속, 반대로 밀면 감속. 새 규칙을 만들지 않아도
        /// "미는 방향과 반대로 밀면 느려진다"는 사실 하나에서 가속과 브레이크가 함께 나온다.
        ///
        /// <b>진행에 수직인 쪽</b> — 스윙 평면을 돌린다. 그래서 로프 하나로도 어느 건물
        /// 쪽으로 흔들지 실시간으로 고를 수 있다.
        ///
        /// 가속도를 중력(29.43)보다 작게 잡아두는 것이 이 함수 전체의 전제다. 그래야
        /// 플레이어가 미는 힘이 중력·구속보다 덜 지배적이라 여전히 "타는" 느낌으로 남고,
        /// 제트팩처럼 날아다니지 않는다.
        /// </summary>
        private void ApplyPumpInput(Vector3 anchor)
        {
            if (moveAction == null || _movement == null) return;

            Vector2 input = moveAction.action.ReadValue<Vector2>();
            if (input.sqrMagnitude < 0.0001f) return;

            Vector3 worldInput = _movement.CameraRelativeDirection(input);

            Vector3 toAnchor = anchor - _rb.position;
            float distance = toAnchor.magnitude;
            if (distance < 0.0001f) return;
            Vector3 radialDir = toAnchor / distance;

            // 로프를 향해 곧장 밀거나 당기는 성분은 버린다 — 그 방향은 구속이 이미 담당하고,
            // 여기서 더해봐야 진자에 에너지를 주지 못한다.
            Vector3 tangentPush = worldInput - radialDir * Vector3.Dot(worldInput, radialDir);
            if (tangentPush.sqrMagnitude < 0.0001f) return;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 tangentVel = velocity - radialDir * Vector3.Dot(velocity, radialDir);

            // "진행 방향"의 기준. 막 붙어서 접선 속도가 거의 없으면 입력 방향 자체를 기준으로
            // 삼는다 — 그러지 않으면 0벡터를 정규화하게 되어 첫 입력이 씹힌다.
            Vector3 alongDir = tangentVel.sqrMagnitude > 0.01f
                ? tangentVel.normalized
                : tangentPush.normalized;

            Vector3 alongPush = alongDir * Vector3.Dot(tangentPush, alongDir);
            Vector3 steerPush = tangentPush - alongPush;

            Vector3 accel = alongPush * pumpAcceleration + steerPush * steerAcceleration;
            _rb.linearVelocity = Vector3.ClampMagnitude(velocity + accel * Time.fixedDeltaTime, maxSpeed);
        }

        /// <summary>
        /// 거리 구속 한 번.
        ///
        /// 로프는 <b>당기기만 하고 밀지 못한다.</b> 그래서 원 안쪽에 있을 때는 아무것도 하지
        /// 않는다. 막대처럼 안쪽에서도 밀어내면 스윙이 아니라 회전목마가 된다.
        /// </summary>
        private void SolveRope(Vector3 anchor)
        {
            Vector3 toAnchor = anchor - _rb.position;
            float distance = toAnchor.magnitude;

            if (distance <= RopeLength || distance < 0.0001f) return;

            Vector3 direction = toAnchor / distance;   // 플레이어 → 앵커

            // 1. 위치 — 원 밖으로 나간 만큼 되당겨 정확히 로프 길이에 올려놓는다.
            _rb.position += direction * (distance - RopeLength);

            // 2. 속도 — 앵커에서 멀어지는 성분만 지운다.
            //
            // 내적이 음수라는 것은 줄을 잡아당기며 밖으로 나가고 있다는 뜻이고, 그건 실제
            // 로프라면 일어날 수 없는 일이다. 그 성분만 빼면 남는 것이 접선 속도다.
            // 안쪽으로 가는 성분(양수)은 건드리지 않는다 — 줄이 느슨해지는 방향이라 로프가
            // 막을 이유가 없고, 막으면 앵커로 빨려드는 느낌이 난다.
            float radial = Vector3.Dot(_rb.linearVelocity, direction);
            if (radial < 0f) _rb.linearVelocity -= direction * radial;
        }
    }
}
