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

        [Header("릴리즈 — 놓는 순간 위로 쏘아 올린다")]
        [Tooltip("놓을 때 <b>수평</b> 속력의 몇 %를 상향 속도로 얹을지. 0이면 순수 관성만 남는다 " +
                 "(물리적으로 정직하지만, 최저점에서 놓으면 수평으로만 날아가 곧 떨어진다). " +
                 "수평 성분만 재므로 극점(속력 0)과 수직 상승(줄을 타고 오르는 중)에서는 거의 " +
                 "0이다 — 아치를 그려 벌어둔 속도에만 보상이 붙는다.")]
        [Range(0f, 1f)][SerializeField] private float releaseUpFraction = 0.5f;

        [Tooltip("상향 속도의 절대 상한(m/s). 비율만 두면 천장이 없어 빠를수록 무한정 " +
                 "높이 솟는다 — 잘할수록 오래 아무것도 못 하는 구조가 된다. 체공 시간은 " +
                 "'다음 앵커를 찾는 데 필요한 시간'이라 목표값이 정해진 값이므로, " +
                 "속도에 비례할 이유가 없는 지점에서 잘라준다. 중력 9.81에서 12면 상승 7.3m · 체공 2.4초.")]
        [Min(0f)][SerializeField] private float maxReleaseUpSpeed = 12f;

        [Header("충돌 시 자동 릴리즈")]
        [Tooltip("건물에 정면으로 박으면 줄을 자동으로 놓는다. 끄면 벽에 붙은 채 구속이 계속 " +
                 "당겨 긁히며 끌려간다.")]
        [SerializeField] private bool releaseOnImpact = true;

        [Tooltip("면에 <b>수직으로</b> 파고드는 속도가 이 값을 넘으면 놓는다(m/s).\n\n" +
                 "속력 전체가 아니라 접촉 법선 성분을 재는 것이 요점이다. 벽을 비스듬히 " +
                 "스치는 것과 정면으로 박는 것을 가르는 값이 그것이기 때문이다 — 17m/s로 " +
                 "10° 각도로 스치면 법선 성분은 3m/s뿐이고, 같은 속도로 정면으로 박으면 17이다. " +
                 "스치는 것까지 끊으면 건물 옆을 지나갈 때마다 줄이 풀려 스윙이 이어지지 않는다.")]
        [Min(0f)][SerializeField] private float impactReleaseSpeed = 8f;

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
        [Tooltip("앵커보다 이만큼 위로 올라가면 스윙 자세 대신 닌자 점프 공중 자세를 " +
                 "보여준다. 큰 감기 증폭이나 강한 펌프로 정상 아치를 넘어 튀어 올랐을 때를 " +
                 "잡아내는 값이다.\n\n" +
                 "기준이 월드 Y가 아니라 앵커인 이유는 도시 높이에 딸려가지 않게 하려는 것이다. " +
                 "매달려 있는 동안 몸은 정의상 앵커보다 아래에 있으므로, 그 위로 올라갔다는 " +
                 "사실 자체가 곧 '정상 아치를 벗어났다'는 뜻이다.")]
        [SerializeField] private float highFlightAboveAnchor = 2f;

        [Header("스윙 자세 — 몸을 로프에 맞춰 기울인다")]
        [Tooltip("기울일 대상. 비워두면 Animator가 붙은 트랜스폼을 쓴다 — 그게 곧 캐릭터 " +
                 "메시의 뿌리라 뼈대와 총이 전부 딸려온다. 물리 캡슐이 아니라 이쪽을 돌린다.")]
        [SerializeField] private Transform visual = null;
        [Tooltip("초당 최대 회전 각도. 0이면 즉시 맞춘다 — 붙는 순간 몸이 홱 돌아 부자연스럽다.")]
        [Min(0f)][SerializeField] private float visualTurnSpeedDegPerSec = 360f;
        [Tooltip("로프 정렬 강도. 1이면 머리가 완전히 로프를 향하고, 0이면 수직으로 선 채 " +
                 "진행 방향만 본다. 스윙 클립 자체에 이미 기울어진 자세가 구워져 있으므로, " +
                 "1로 두면 두 번 기울어 과해 보인다.")]
        [Range(0f, 1f)][SerializeField] private float ropeAlignment = 0.7f;

        [Tooltip("이 속도(m/s) 아래에서는 몸의 방향을 갱신하지 않는다. 아치 극점에서는 " +
                 "접선 속도가 0에 수렴하는데, 그때 남는 것은 펌프 입력과 솔버 잔여값 " +
                 "즉 노이즈다. 그걸 방향으로 쓰면 몸이 제자리에서 떤다.")]
        [Min(0f)][SerializeField] private float facingSpeedThreshold = 4f;

        [Tooltip("몸의 방향이 한 번에 이만큼 넘게 꺾이는 갱신은 버린다. 진자는 극점마다 " +
                 "진행 방향이 180° 뒤집히는데, 그걸 그대로 따라가면 반주기마다 몸이 " +
                 "반 바퀴 휘돈다 — 바람 인형처럼 보이던 것의 정체가 이것이다.")]
        [Range(10f, 180f)][SerializeField] private float maxFacingTurn = 80f;

        [Tooltip("몸의 방향이 목표를 따라가는 속도(초당 각도). 낮을수록 부드럽고 굼뜨다.")]
        [Min(1f)][SerializeField] private float facingTurnSpeedDegPerSec = 120f;

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

        /// <summary>
        /// 구속 보정이 벽에 멈출 때 남겨두는 여유. 정확히 표면에 붙여두면 다음 스텝에
        /// 겹침 판정과 되밀기가 번갈아 일어나 떨린다.
        /// </summary>
        private const float ContactSkin = 0.02f;

        private Animator _animator;
        private const string SwingLayerName = "Swing";
        private static readonly int ReelingHash = Animator.StringToHash("Reeling");
        private static readonly int HighFlightHash = Animator.StringToHash("HighFlight");
        private static readonly int SwingStartedHash = Animator.StringToHash("SwingStarted");
        private static readonly int SwingEndedHash = Animator.StringToHash("SwingEnded");
        /// <summary>기본 레이어용. 트리거는 한 전이가 먹으면 소비되므로 레이어마다 따로 둔다.</summary>
        private static readonly int SwingReleasedHash = Animator.StringToHash("SwingReleased");

        /// <summary>Swing 레이어의 인덱스. 레이어는 이름이 아니라 인덱스로 제어하는 API라 미리 찾아둔다.</summary>
        private int _swingLayerIndex = -1;

        /// <summary>
        /// 대기 상태에서 시각 레이어가 캡슐에 대해 갖고 있던 로컬 회전.
        ///
        /// 놓은 뒤 돌아갈 자리다. 항등이라고 가정하면 메시가 캡슐에 대해 조금이라도 틀어져
        /// 붙어 있는 프리팹에서 스윙 한 번에 그 틀어짐이 영구히 사라진다.
        /// </summary>
        private Quaternion _visualLocalRest = Quaternion.identity;

        /// <summary>
        /// 몸이 향하고 있는 방향. <b>매 프레임 새로 뽑지 않고 여기에 담아 이어간다.</b>
        ///
        /// 접선 속도를 그대로 방향으로 쓰던 것이 바람 인형의 원인이었다. 진자는 왕복하므로
        /// 극점을 지날 때마다 진행 방향이 180° 뒤집히고, 목표 회전이 그만큼 튀면 몸이
        /// 반 바퀴 휘돈다. 방향을 상태로 들고 있으면 "지금 값에서 얼마나 움직일 것인가"를
        /// 물을 수 있고, 그래야 튀는 갱신을 버릴 수 있다.
        /// </summary>
        private Vector3 _facing = Vector3.forward;

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
            // 플레이어가 놓은 것이 아니므로 릴리즈 보상은 주지 않는다.
            if (_swinging) EndSwing(boost: false);
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

            // 원래는 월드 Y가 30m를 넘는지로 봤다. 지면이 y=0에 붙어 있으니 그게 곧 지상 높이라는
            // 근거였는데, 그 30이라는 값은 사실 "최고 건물 28m 바로 위"라는 뜻이었다.
            // 건물을 겹으로 쌓아 옥상이 70m가 되는 순간 그 전제가 깨진다 — 정상 스윙이 내내
            // 문턱 위에 있어 공중 자세가 상시로 나온다.
            //
            // 기준을 앵커로 옮기면 도시 높이가 얼마든 성립한다. 로프에 매달린 몸은 정의상
            // 앵커보다 아래에 있으므로, 그 위로 올라갔다는 것 자체가 아치를 벗어났다는 뜻이다.
            bool highFlight = _swinging && gun != null &&
                              _rb.position.y >= gun.AnchorPoint.y + highFlightAboveAnchor;
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

            UpdateFacing(up);

            // 몸의 앞을 접선면에 눕힌다. 로프 방향과 직교해야 LookRotation이 안정적이다.
            Vector3 forward = _facing - up * Vector3.Dot(_facing, up);
            if (forward.sqrMagnitude < 0.0001f) return visual.rotation;
            forward.Normalize();

            Quaternion aligned = Quaternion.LookRotation(forward, up);
            if (ropeAlignment >= 0.999f) return aligned;

            // 덜 기울이고 싶을 때 섞을 반대편 극단 — 진행 방향만 보고 수직으로 선 자세.
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 0.0001f) return aligned;

            return Quaternion.Slerp(Quaternion.LookRotation(flat.normalized, Vector3.up),
                                    aligned, ropeAlignment);
        }

        /// <summary>
        /// 몸이 향할 방향을 <b>조금씩</b> 옮긴다 — 바람 인형을 잡는 함수.
        ///
        /// 세 겹으로 거른다. 어느 하나만 빠져도 증상이 돌아온다.
        ///
        /// <b>하나, 느릴 때는 갱신하지 않는다.</b> 아치 극점에서 접선 속도는 0에 수렴하는데,
        /// 그때 남는 것은 펌프 입력과 솔버 잔여값, 즉 노이즈다. 예전 임계값은 0.1m/s라
        /// 사실상 항상 통과했고, 그래서 극점마다 몸이 제자리에서 떨었다.
        ///
        /// <b>둘, 크게 꺾이는 갱신은 버린다.</b> 진자는 극점마다 진행 방향이 <b>정확히
        /// 180° 뒤집힌다.</b> 그것을 그대로 따라가면 반주기(약 2.7초)마다 몸이 반 바퀴
        /// 휘도는데, 이게 "행사장 바람 인형"의 정체였다. 실제 웹스윙에서는 아치를 되돌아
        /// 오기 전에 놓으므로 몸이 뒤돌 일 자체가 없다 — 그 사실을 규칙으로 옮긴 것이다.
        ///
        /// <b>셋, 남은 변화도 서서히 따라간다.</b> 통과한 갱신마저 즉시 반영하면 펌프로
        /// 방향을 틀 때 몸이 홱 돈다.
        /// </summary>
        private void UpdateFacing(Vector3 up)
        {
            Vector3 velocity = _rb.linearVelocity;
            Vector3 tangent = velocity - up * Vector3.Dot(velocity, up);

            if (tangent.magnitude < facingSpeedThreshold) return;

            Vector3 candidate = tangent.normalized;
            if (Vector3.Angle(_facing, candidate) > maxFacingTurn) return;

            _facing = Vector3.RotateTowards(
                _facing, candidate,
                facingTurnSpeedDegPerSec * Mathf.Deg2Rad * Time.deltaTime, 0f);
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

            // 매 스텝 다시 켠다. 한 번만 켜두면(BeginSwing) 다른 쪽이 자기 볼일을 끝내며
            // 끄는 순간 스윙이 조용히 죽는다 — 실제로 견인(GrappleZip)이 스윙에 취소당하면서
            // 이 스위치를 같이 꺼버려, 그 뒤로 지상 이동이 매 스텝 수평 속도를 덮어써
            // 진자가 성립하지 않았다. 상태를 나눠 갖는 대신 <b>원하는 쪽이 계속 주장한다</b>로
            // 바꾸면, 누가 언제 끄든 다음 스텝에 저절로 회복된다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

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
        /// 건물에 정면으로 박으면 줄을 놓는다.
        ///
        /// 이것이 없으면 벽에 닿은 뒤에도 구속이 계속 앵커 쪽으로 당겨, 벽이 밀어내는 힘과
        /// 번갈아 싸우며 긁혀 끌려간다. 로프가 감당할 수 있는 것은 <b>당기는 힘</b>뿐이고
        /// 앞을 막은 벽은 로프가 풀 수 있는 문제가 아니다 — 그러면 놓는 것이 맞다.
        ///
        /// <b>속력이 아니라 접촉 법선 성분을 잰다.</b> 스윙은 건물 옆을 스치며 지나가는 것이
        /// 정상이라, 닿기만 하면 끊는 규칙은 도시를 지나갈 수 없게 만든다. 법선 성분은
        /// 스칠 때 거의 0이고 정면으로 박을 때만 속력 전체가 실리므로, 그 둘을 값 하나로
        /// 가른다. 부호는 <c>Abs</c>로 지운다 — 재려는 것은 방향이 아니라 파고드는 세기다.
        ///
        /// <c>EndSwing(false)</c>인 것은 의도적이다. <b>박은 것은 놓은 것이 아니므로 상향
        /// 보상을 주지 않는다.</b> 벽에 부딪힐 때마다 위로 쏘아 올려주면 그게 곧 등반 기술이 된다.
        /// 여기서 먼저 스윙을 끄므로 다음 <c>FixedUpdate</c>는 두 번 끄지 않는다.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!_swinging || !releaseOnImpact || gun == null) return;

            float impact = 0f;
            for (int i = 0; i < collision.contactCount; i++)
            {
                float normalSpeed = Mathf.Abs(
                    Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal));
                if (normalSpeed > impact) impact = normalSpeed;
            }

            if (impact < impactReleaseSpeed) return;

            EndSwing(false);
            gun.ForceDetach();
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

            // 몸의 방향을 붙는 순간의 진행 방향으로 초기화한다. 이전 스윙에서 쓰던 값을
            // 그대로 이어받으면, 방향이 크게 달라진 새 스윙에서 UpdateFacing의 "크게
            // 꺾이는 갱신은 버린다" 규칙에 막혀 영영 못 돌아온다.
            Vector3 entry = _rb.linearVelocity;
            entry.y = 0f;
            _facing = entry.sqrMagnitude > 0.01f ? entry.normalized : transform.forward;

            // 지상 이동이 수평 속도를 덮어쓰지 못하게 막는다. 이걸 안 하면 진자가 만든
            // 접선 속도가 다음 스텝에 입력값으로 대체되어 스윙이 그 자리에서 죽는다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            // 붙는 순간을 명시적으로 알린다. Swing 레이어의 상태 기계는 가중치 0일 때도
            // 계속 돌고 있어서, 매번 SwingLoop부터 다시 시작한다는 보장이 없다 — 트리거로
            // "지금 막 붙었다"를 알려야 StartSwing 클립(있다면)이 확실히 재생된다.
            _animator?.SetTrigger(SwingStartedHash);
        }

        /// <summary>
        /// 놓는다. 벌어둔 속도는 그대로 두고, <b>위로 쏘아 올린다.</b>
        ///
        /// 감쇠는 여전히 한 줄도 없다 — 그걸 넣으면 "지킨다" 축이 통째로 사라진다.
        /// 대신 여기서 <b>의도적으로 거짓말을 한다.</b> 없던 에너지를 만들어 위로 얹는다.
        ///
        /// <b>왜 필요한가 — 총은 손이 하나이기 때문이다.</b> 스파이더맨은 양손을 번갈아 써서
        /// 앞 웹을 놓기 전에 다음 웹이 이미 나가 있다. 그래서 끊기지 않는다. 총으로는 그
        /// 교대가 불가능하므로 "놓는다 → <b>날아간다</b> → 다시 겨눈다"라는 박자가 하나 더
        /// 생기고, <b>날아가는 구간이 곧 다음 앵커를 찾는 시간</b>이 된다. 순수 관성만
        /// 남기면 최저점에서 놓았을 때 수평 속도뿐이라 곧장 지면으로 떨어지고, 그 박자가
        /// 아예 생기지 않는다. 2026-08-01 영상에서 스윙 몇 번 만에 지상에 눌러앉아 다시는
        /// 못 올라가던 것이 이것이었다.
        ///
        /// <b>왜 총속력이 아니라 수평 속력에 비례시키는가.</b> 고정값을 얹으면 아무 때나
        /// 놓아도 같은 보상이 나와 릴리즈 타이밍이 실력이 아니게 된다. 그래서 속도에
        /// 비례시켜야 하는데, <b>총속력을 쓰면 이미 위로 가고 있는 속도까지 보상해 준다.</b>
        ///
        /// 그게 실제로 사고를 냈다. 2026-08-01 영상에서 벽을 따라 줄을 감아 수직으로
        /// 솟구치던 중에 놓자, 이미 위로 향하던 40 m/s에 다시 50%가 얹혀 상한(60)까지 찬
        /// 채로 발사됐다 — 중력 9.81에서 정점 <b>183m</b>, 왕복 <b>12초</b>. 도시가 화면에서
        /// 점이 되고 그동안 아무것도 할 수 없다.
        ///
        /// 수평 성분만 재면 세 경우가 전부 옳게 나온다.
        /// <list type="bullet">
        /// <item>아치 최저점 — 속도가 온전히 수평이라 보상이 최대다. 원하던 그것</item>
        /// <item>아치 극점 — 속도가 0이라 보상도 0. 릴리즈 타이밍이 실력으로 남는다</item>
        /// <item>줄을 타고 수직 상승 중 — 수평 성분이 0이라 보상도 0. <b>로켓이 되지 않는다</b></item>
        /// </list>
        /// 즉 "위로 가려면 먼저 앞으로 빨라야 한다"는 규칙 하나로 정리된다. 줄을 감아 벽을
        /// 오르는 것은 애초에 만들려는 물건이 아니므로(설계 결정 4-2), 그 경로에 보상이
        /// 붙지 않는 것이 맞다.
        ///
        /// 상향은 <b>월드 기준 위</b>다. 로프 방향이 아니라. 로프 기준으로 얹으면 로프가
        /// 비스듬할 때 옆으로 밀려 진행 방향이 틀어지는데, 여기서 원하는 것은 방향 전환이
        /// 아니라 체공 시간이다.
        /// </summary>
        /// <param name="boost">
        /// 상향 임펄스를 얹을지. 컴포넌트가 꺼져서 정리되는 경우(<c>OnDisable</c>)에는
        /// 플레이어가 놓은 것이 아니므로 보상을 주지 않는다.
        /// </param>
        private void EndSwing(bool boost = true)
        {
            _swinging = false;
            RopeLength = 0f;
            if (_movement != null) _movement.SuspendHorizontalControl = false;

            // 놓는 순간을 명시적으로 알린다 — boost 여부(컴포넌트가 꺼져서 정리되는 경우
            // 등)와 무관하게, 스윙이 끝났다는 사실 자체는 항상 알려야 SwingLand 클립이
            // (있다면) 놓친 적 없이 재생된다.
            _animator?.SetTrigger(SwingEndedHash);    // 스윙 레이어 — SwingLand
            _animator?.SetTrigger(SwingReleasedHash); // 기본 레이어 — AirIdle

            if (!boost || releaseUpFraction <= 0f) return;

            // 수평 성분만 잰다. y를 빼는 이 한 줄이 "이미 위로 가던 속도까지 보상하는" 것을
            // 막는다 — 줄을 타고 수직으로 솟구치던 중에 놓으면 여기가 0이라 보상이 없다.
            Vector3 velocity = _rb.linearVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            if (horizontalSpeed < 0.01f) return;

            // 비율은 "언제 놓았는가"를 재고, 상한은 "얼마나 높이 갈 수 있는가"를 정한다.
            // 둘을 함께 두어야 타이밍이 실력으로 남으면서도 폭주하지 않는다.
            float up = Mathf.Min(horizontalSpeed * releaseUpFraction, maxReleaseUpSpeed);

            // ForceMode.VelocityChange는 질량을 무시하고 속도를 그대로 더한다. 질량을 1로
            // 두고 튜닝한 값이 나중에 질량을 바꾸는 순간 어긋나는 일을 여기서 미리 막는다.
            _rb.AddForce(Vector3.up * up, ForceMode.VelocityChange);

            // 감기 증폭과 겹쳐 상한을 넘지 않게 한다 — maxSpeed는 설계값이 아니라 안전장치다.
            _rb.linearVelocity = Vector3.ClampMagnitude(_rb.linearVelocity, maxSpeed);
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

            // 1. 위치 — 원 밖으로 나간 만큼 되당겨 로프 길이에 올려놓는다.
            //
            // <b>그냥 position에 대입하면 충돌을 건너뛰는 순간이동이다.</b> 그리고 이 보정은
            // 방향이 언제나 앵커 쪽인데 앵커는 건물 위에 있으므로, 매 물리 스텝마다 캡슐을
            // 건물 안으로 조금씩 밀어넣는 것과 같다. 밀려 들어간 캡슐을 PhysX가 밀어내면
            // 다음 스텝에 로프가 다시 당기고, 그 왕복이 곧 벽에 박혀 긁히며 지나가는 증상이다.
            // Continuous Dynamic으로도 안 잡히는 이유가 이것이다 — 터널링이 아니라 대입이다.
            //
            // 캡슐을 실제로 쓸어보고 막히는 지점에서 멈춘다. 벽에 막히면 로프가 길이를 넘긴
            // 채로 남지만 그게 맞다 — 실제로도 벽이 가로막으면 줄은 팽팽해지고 몸은 안 간다.
            // 아래 속도 보정은 그대로 도므로 접선 속도는 계속 유지된다.
            float pull = distance - RopeLength;
            if (_rb.SweepTest(direction, out RaycastHit blocked, pull, QueryTriggerInteraction.Ignore))
                pull = Mathf.Max(0f, blocked.distance - ContactSkin);

            if (pull > 0f) _rb.position += direction * pull;

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
