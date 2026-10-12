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

        [Tooltip("구속의 위치 보정을 <b>보간을 살리는 방식</b>으로 넣을지.\n\n" +
                 "끄면(예전 동작) <c>Rigidbody.position</c>에 직접 대입한다. 유니티는 이 대입을 " +
                 "순간이동으로 보고 <b>그 스텝의 보간을 버리는데</b>, 로프가 팽팽한 동안은 매 " +
                 "물리 스텝마다 대입하므로 스윙 내내 보간된 포즈와 순간이동한 포즈가 섞여 " +
                 "캐릭터만 떠는 것처럼 보인다(게임 프레임은 멀쩡한데 몸만 버벅이는 증상).\n\n" +
                 "켜면 <c>MovePosition</c>으로 넘겨 보간이 유지된다. 다만 동적 Rigidbody에서 " +
                 "<c>MovePosition</c>은 속도와 상호작용하므로 <b>진자 감각이 미세하게 달라질 수 " +
                 "있다</b> — 둘을 번갈아 켜보고 떨림과 손맛을 같이 보고 정할 것. 이 스위치가 " +
                 "있는 이유가 그 비교다.")]
        [SerializeField] private bool interpolatedConstraintMove = true;

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

        // 추가 생성 (2026-10-12, Movie_007) — 놓은 뒤 높이 상한
        [Tooltip("놓은 뒤 날아오를 수 있는 가장 높은 곳 = 앵커 높이 + 이 값(m). 음수면 끈다.\n\n" +
                 "앵커는 항상 건물 벽에 붙으므로(SwingAnchorFinder) 정점이 옥상 근처에서 멈춘다. " +
                 "진입 속도·펌프·릴리즈 보상·스윙 중력이 쌓여도 도시 위로 솟지 않는다. " +
                 "1.5면 붙었던 건물 옥상에 겨우 올라설 수 있는 정도.")]
        [SerializeField] private float releaseApexAboveAnchor = 1.5f;

        [Tooltip("높이 상한 때문에 깎인 위 속도 중 몇 %를 진행 방향(수평)으로 돌릴지.\n" +
                 "0이면 그냥 버린다(놓는 순간 확 느려진 느낌). 1이면 전부 앞으로(너무 빨라질 수 있다).")]
        [Range(0f, 1f)][SerializeField] private float clippedLiftToForward = 0.5f;

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

        [Tooltip("매달려 있는 동안 히트스톱·카메라 셰이크를 끈다.\n\n" +
                 "옥상마다 소품이 1~4개씩 놓여 있어서, 아치가 옥상을 스칠 때마다 그것들을 " +
                 "30m/s로 들이받고 그때마다 TimeScale이 0.05까지 떨어진다. 2026-08-13 " +
                 "에디터 로그 실측으로 한 세션에 18회, 그중 두 번은 3~4회 연타였다 — " +
                 "슬로우모션이 풀리기도 전에 다음 것이 걸리는 구간이다.\n\n" +
                 "날아가다 스친 것은 타격이 아니므로 끄는 것이 맞다. 다만 \"스윙으로 " +
                 "들이받는 것\"을 나중에 공격으로 쓸 생각이 있으면 이 스위치를 꺼서 " +
                 "예전 동작으로 즉시 돌아간다. 소품을 손에 쥐고 있는 동안의 억제는 " +
                 "WebGrabThrow가 따로 가지므로 이걸 꺼도 그쪽은 그대로 산다.")]
        [SerializeField] private bool suppressHitFeedbackWhileSwinging = true;

        [Header("감기 (InputSystem_Actions ▸ Player ▸ Reel)")]
        [SerializeField] private InputActionReference reelAction = null;
        [Tooltip("휠 한 칸에 목표 길이가 몇 m 바뀌는가. 실제 길이는 아래 속도로 그 목표를 따라간다.")]
        [Min(0.1f)][SerializeField] private float lengthPerNotch = 2f;
        [Tooltip("실제 로프가 목표 길이를 따라가는 속도(m/s). 이 값이 곧 벽을 타고 오르는 속도다.")]
        [Min(0.1f)][SerializeField] private float reelSpeed = 8f;

        /// <summary>
        /// 감기 입력 액션(마우스 휠). <see cref="WebSlingshot"/>이 지상에서 같은 휠로 충전하므로,
        /// 슬링샷 쪽 칸이 비어 있으면 이걸 빌려 간다 — GrappleZip.ZipAction과 같은 이유다.
        /// </summary>
        public InputActionReference ReelAction => reelAction;

        [Header("애니메이션")]
        [Tooltip("Swing 레이어 가중치가 오르내리는 속도(초당). 0이면 즉시 전환된다 — " +
                 "붙는 순간 자세가 뚝 끊겨서 대개 부자연스럽다.")]
        [Min(0f)][SerializeField] private float animLayerLerpSpeed = 8f;
        [Tooltip("이번 스윙을 붙잡은 순간의 높이보다 이만큼 위로 올라가면 스윙 자세 대신 " +
                 "닌자 점프 공중 자세를 보여준다. 큰 감기 증폭이나 강한 펌프로 정상 아치를 " +
                 "넘어 튀어 올랐을 때를 잡아내는 값이다.\n\n" +
                 "기준을 앵커가 아니라 <b>붙는 순간의 내 높이</b>로 잡는다. 앵커 기준(고정 " +
                 "절대값)이었을 때는 로프가 짧은 옥상↔옥상 스윙에서 문제가 났다 — 같은 2m가 " +
                 "짧은 로프의 호(arc) 전체에서 차지하는 비중은 크고, 긴 로프에서는 작아서 " +
                 "옥상 스윙은 평범하게만 움직여도 걸리고 고층 스윙은 진짜 비정상일 때만 " +
                 "걸리는 식으로 로프 길이에 따라 엄격도가 달라졌다. 붙는 순간의 내 높이를 " +
                 "기준으로 삼으면 로프 길이·앵커 위치와 무관하게 항상 같은 뜻이 된다 — " +
                 "\"펌프·감기로 에너지를 실제로 더 넣어 시작 높이보다 위로 올라갔다\".")]
        [SerializeField] private float highFlightAboveStart = 2f;

        [Tooltip("HighFlight가 켜지고 꺼질 때마다 그 순간의 높이차·RopeLength를 콘솔에 " +
                 "찍는다. 지금 보이는 공중 자세가 정말 이 값 때문인지, 아니면 다른 자리에서 " +
                 "나오는 건지 가릴 때 켤 것. 확인 끝나면 꺼둘 것.")]
        [SerializeField] private bool logHighFlight = false;
        private bool _prevHighFlight;

        [Tooltip("접지 중이라 스윙 레이어 가중치를 즉시 0으로 스냅시킬 때마다 그 직전 " +
                 "가중치를 콘솔에 찍는다. 0에 가까웠으면 이미 거의 안 보이던 상태였다는 " +
                 "뜻이고, 1에 가까웠으면 이 스냅이 실제로 자세를 확 바꿔준 것이다.")]
        [SerializeField] private bool logGroundSnap = false;

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

        [Tooltip("몸의 '위'로 쓸 로프 방향의 최소 y 성분. 펌프로 크게 솟구쳐 앵커보다 높이 " +
                 "올라가면 앵커 방향(toAnchor)이 아래를 가리키게 되어 몸이 통째로 뒤집혀 보인다 " +
                 "— 영상으로 확인한 또 다른 바람 인형 증상이다(위 maxFacingTurn이 잡는 것은 " +
                 "진행 방향이 반 바퀴 도는 것이고, 이건 축 자체가 뒤집히는 것이라 별개다). " +
                 "0보다 확실히 큰 값으로 눌러두면 그 구간에서도 위가 아래를 향하는 일이 없다.")]
        [Range(0.05f, 0.9f)][SerializeField] private float minSwingUpY = 0.15f;

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

        // 추가 생성
        [Header("진입 속도 — 붙는 순간 스윙이 바로 살아나게")]
        [Tooltip("공중에서 붙는 순간 진행 방향(접선)으로 보장하는 최소 속도(m/s). 0이면 끈다(순수 물리).\n\n" +
                 "순수 진자는 출발 높이보다 높이 못 올라가고, 일찍 놓을 때마다 조금씩 낮아져 결국 " +
                 "길바닥으로 내려온다. 스파이더맨식 스윙은 붙는 순간 속도를 보장해 그 손실을 메운다. " +
                 "이미 이보다 빠르면 건드리지 않는다.")]
        [Min(0f)][SerializeField] private float minEntrySpeed = 14f;

        // 추가 생성 (2026-10-12, Movie_006 분석)
        [Header("스윙 중력 — 아치를 빠르게")]
        [Tooltip("스윙 중에만 중력을 이 배수로 키운다. 1이면 기본 중력.\n\n" +
                 "진자 반 바퀴 시간 ≈ π·√(줄 길이 / 중력). 줄 18 m에 기본 중력이면 약 4.3초라, " +
                 "최저점 근처에 오래 머물러 그냥 매달린 것처럼 보인다. 2배면 약 3.0초(√2배 빠름). " +
                 "로프 구속이 떨어지려는 속도를 접선 속도로 바꾸므로 최저점 속도도 함께 빨라진다.")]
        [Range(1f, 4f)][SerializeField] private float swingGravityMultiplier = 2f;

        /// <summary>현재 로프 길이. 구속이 실제로 쓰는 값이다.</summary>
        public float RopeLength { get; private set; }

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private bool _swinging;

        /// <summary>휠이 바꾸는 값. 실제 길이가 이쪽으로 천천히 따라간다.</summary>
        private float _targetLength;

        /// <summary>붙은 순간의 길이. 그보다 길게 풀지는 못한다.</summary>
        private float _maxLength;

        /// <summary>붙은 순간의 월드 Y. <c>HighFlight</c> 판정의 기준선이다.</summary>
        private float _swingStartHeight;

        // 추가 생성 — 마지막으로 붙어 있던 앵커 위치. 놓는 순간엔 GrappleGun이 이미 앵커를
        // 지운 뒤라(AnchorPoint가 Vector3.zero) 매 스텝 여기에 기억해 둔다.
        private Vector3 _lastAnchor;

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
        ///
        /// <b>단, 이미 땅에 서 있으면 예외다.</b> 이 레이어는 Override라 가중치가 남아 있는
        /// 동안은 기본 레이어가 무엇을 하든(착지 애니메이션으로 이미 전환됐어도) 전부
        /// 가린다. 빠르게 스윙하다 땅에 부딪혀 <see cref="OnCollisionEnter"/>로 강제
        /// 릴리즈되면, 부딪히는 그 순간 이 레이어가 <c>NinjaAirLoop</c>를 보여주고 있었을
        /// 수 있는데 — 페이드가 <c>animLayerLerpSpeed</c>를 그대로 따르면 발이 이미 땅에
        /// 닿은 뒤로도 최대 0.125초(1/8초) 동안 그 공중 자세가 남아 보인다. 착지 애니메이션
        /// 타이밍을 아무리 정확히 맞춰도 이 레이어가 안 비켜주면 소용없으므로, 접지 중에는
        /// 서서히가 아니라 즉시 0으로 내린다.
        /// </summary>
        private void UpdateAnimator()
        {
            if (_animator == null || _swingLayerIndex < 0) return;

            bool grounded = _movement != null && _movement.IsGrounded;
            float targetWeight = _swinging && !grounded ? 1f : 0f;
            float current = _animator.GetLayerWeight(_swingLayerIndex);
            float next = grounded || animLayerLerpSpeed <= 0f
                ? targetWeight
                : Mathf.MoveTowards(current, targetWeight, animLayerLerpSpeed * Time.deltaTime);

            if (logGroundSnap && grounded && current > 0.01f && next <= 0.01f)
                Debug.Log($"[SwingConstraint] 접지 스냅 — 직전 가중치 {current:F2} → 0", this);

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
            // 그다음엔 기준을 앵커로 옮겼는데(앵커 + 2m), 이번엔 로프 길이에 따라 엄격도가
            // 달라졌다 — 옥상↔옥상처럼 로프가 짧으면 같은 2m이 호 전체에서 차지하는 비중이
            // 커서 평범하게 스윙만 해도 걸렸다. 기준을 <b>붙는 순간의 내 높이</b>로 옮기면
            // 로프 길이·앵커 위치와 무관해진다 — 펌프·감기로 진짜 에너지를 더 넣어야만
            // 시작 높이를 넘어서므로.
            bool highFlight = _swinging &&
                              _rb.position.y >= _swingStartHeight + highFlightAboveStart;
            _animator.SetBool(HighFlightHash, highFlight);

            if (logHighFlight && highFlight != _prevHighFlight)
            {
                Debug.Log($"[SwingConstraint] HighFlight {(highFlight ? "ON" : "OFF")} — " +
                          $"높이차={_rb.position.y - _swingStartHeight:F2}m " +
                          $"(문턱 {highFlightAboveStart}m) · RopeLength={RopeLength:F1}m", this);
            }
            _prevHighFlight = highFlight;
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

            // 앵커보다 높이 솟구치면(하이 플라이트 등) toAnchor가 아래를 가리켜 up이 뒤집히고
            // 몸이 순간적으로 거꾸로 보인다 — y 성분을 최솟값 아래로 못 내려가게 눌러서
            // 막는다. 평소 스윙(y가 이미 그 이상)에는 전혀 영향이 없다.
            if (up.y < minSwingUpY)
            {
                up.y = minSwingUpY;
                up.Normalize();
            }

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
            // 소품 앵커는 스윙에서 뺀다 — 소품을 잡는 것은 전투(끌어오기·던지기)를 보여주려는
            // 것이지 스윙을 돕는 것이 아니다. gun.IsPropAnchor를 확인.
            bool attached = gun != null && gun.IsAttached && !gun.IsPropAnchor;

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
            _lastAnchor = anchor; // 추가 생성 — EndSwing의 높이 상한이 쓴다

            // 실제 길이가 목표를 향해 정해진 속도로 따라간다. MoveTowards는 목표를 넘어가지
            // 않으므로 도착하면 저절로 멈춘다 — 도착 판정을 따로 쓸 필요가 없다.
            float previousLength = RopeLength;
            RopeLength = Mathf.MoveTowards(RopeLength, _targetLength, reelSpeed * Time.fixedDeltaTime);

            ApplyReelVelocity(anchor, previousLength);
            ApplyPumpInput(anchor);
            ApplySwingGravity(); // 추가 생성 — SolveRope보다 먼저: 구속이 이 낙하를 접선 속도로 바꾼다

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
            _swingStartHeight = _rb.position.y;
            _lastAnchor = gun.AnchorPoint; // 추가 생성 — 붙자마자 놓아도 상한이 맞게
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

            SetHitFeedbackSuppressed(true);

            // 추가 생성 — 붙는 순간 접선 속도를 최소값까지 채운다(아래 함수 주석 참고).
            ApplyEntrySpeed();

            // 붙는 순간을 명시적으로 알린다. Swing 레이어의 상태 기계는 가중치 0일 때도
            // 계속 돌고 있어서, 매번 SwingLoop부터 다시 시작한다는 보장이 없다 — 트리거로
            // "지금 막 붙었다"를 알려야 StartSwing 클립(있다면)이 확실히 재생된다.
            _animator?.SetTrigger(SwingStartedHash);
        }

        // 추가 생성
        /// <summary>
        /// 붙는 순간 <b>접선 방향</b> 속도가 <see cref="minEntrySpeed"/>보다 느리면 그만큼 채운다.
        ///
        /// <b>왜 접선인가.</b> 줄 방향(앵커 쪽) 성분은 거리 구속이 다음 스텝에 지워버리므로
        /// 거기에 속도를 넣어봐야 사라진다. 줄에 수직인 접선 성분만이 진자를 실제로 돌린다 —
        /// <see cref="ApplyPumpInput"/>이 로프 방향 성분을 버리는 것과 같은 이유다.
        ///
        /// <b>방향은 지금 움직이는 쪽을 그대로 쓴다.</b> 떨어지는 중이면 아래·앞을 섞은 방향인데,
        /// 그게 곧 진자가 어차피 가려던 방향이라 궤도를 비틀지 않고 빠르기만 올린다.
        /// 거의 멈춰 있을 때만 몸이 향한 방향(<see cref="_facing"/>)을 접선면에 눕혀 쓴다.
        ///
        /// 땅에서 붙인 경우는 건드리지 않는다 — 그건 스윙이 아니라 견인·슬링샷의 영역이다.
        /// </summary>
        private void ApplyEntrySpeed()
        {
            if (minEntrySpeed <= 0f || gun == null) return;
            if (_movement != null && _movement.IsGrounded) return;

            Vector3 toAnchor = gun.AnchorPoint - _rb.position;
            if (toAnchor.sqrMagnitude < 0.0001f) return;
            Vector3 radial = toAnchor.normalized;

            // Vector3.ProjectOnPlane(v, n): v에서 n 방향 성분을 뺀 것 = 줄에 수직인 성분.
            Vector3 velocity = _rb.linearVelocity;
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, radial);
            Vector3 direction = tangent.sqrMagnitude > 1f
                ? tangent
                : Vector3.ProjectOnPlane(_facing, radial);
            if (direction.sqrMagnitude < 0.0001f) return;   // 줄과 나란한 방향뿐이면 줄 게 없다
            direction.Normalize();

            float along = Vector3.Dot(velocity, direction);
            if (along >= minEntrySpeed) return;

            // AddForce가 아니라 속도를 바로 쓴다. AddForce는 다음 물리 스텝에야 반영돼서
            // 같은 줄에서 상한(maxSpeed)으로 자를 수가 없기 때문이다.
            _rb.linearVelocity = Vector3.ClampMagnitude(
                velocity + direction * (minEntrySpeed - along), maxSpeed);
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

            // 아래에 이른 return이 있으므로(보상 없음) 그보다 위에서 푼다.
            // 억제를 켜둔 채로 빠져나가면 그 뒤로 타격 피드백이 영영 안 나온다.
            SetHitFeedbackSuppressed(false);

            // 놓는 순간을 명시적으로 알린다 — boost 여부(컴포넌트가 꺼져서 정리되는 경우
            // 등)와 무관하게, 스윙이 끝났다는 사실 자체는 항상 알려야 SwingLand 클립이
            // (있다면) 놓친 적 없이 재생된다.
            _animator?.SetTrigger(SwingEndedHash);    // 스윙 레이어 — SwingLand

            // 기본 레이어 — AirIdle. <b>이미 땅에 서 있으면 쏘지 않는다.</b> 옥상 위처럼
            // 애초에 접지 상태에서 붙였다 바로 놓는 경우(붙자마자 취소·같은 자리 재포착
            // 등)까지 이 트리거가 무조건 나가면, Any State가 그 즉시 AirIdle로 끌고 가
            // 착지 전이가 되돌리기 전까지 한두 프레임 그 자세가 스친다 — 발이 땅에 붙어
            // 있는데 공중 자세가 보이는 원인이 이것이었다. 진짜로 공중에서 놓았을 때만
            // 필요한 신호이므로, 접지 중이면 애초에 쏘지 않는 것이 맞다.
            if (_movement == null || !_movement.IsGrounded)
                _animator?.SetTrigger(SwingReleasedHash);

            // 수정 (2026-10-12) — 보상과 높이 상한은 플레이어가 놓았을 때만 건다. 벽에 박아
            // 끊긴 경우(OnCollisionEnter)나 컴포넌트가 꺼진 경우는 놓은 것이 아니다.
            if (!boost) return;

            // 수정 — 보상과 높이 상한을 한 번에 계산하려고 속도를 지역 변수에 모아
            // 마지막에 한 번만 쓴다.
            Vector3 velocity = _rb.linearVelocity;

            if (releaseUpFraction > 0f)
            {
                // 수평 성분만 잰다. y를 빼는 이 한 줄이 "이미 위로 가던 속도까지 보상하는" 것을
                // 막는다 — 줄을 타고 수직으로 솟구치던 중에 놓으면 여기가 0이라 보상이 없다.
                float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
                if (horizontalSpeed >= 0.01f)
                {
                    // 비율은 "언제 놓았는가"를 재고, 상한은 "얼마나 높이 갈 수 있는가"를 정한다.
                    // 둘을 함께 두어야 타이밍이 실력으로 남으면서도 폭주하지 않는다.
                    float up = Mathf.Min(horizontalSpeed * releaseUpFraction, maxReleaseUpSpeed);

                    // ForceMode.VelocityChange는 질량을 무시하고 속도를 그대로 더한다. 질량을 1로
                    // 두고 튜닝한 값이 나중에 질량을 바꾸는 순간 어긋나는 일을 여기서 미리 막는다.
                    //
                    // 수정 (2026-10-12) — 그 AddForce 대신 속도에 바로 더한다. 질량을 무시하는 점은
                    // 같다. AddForce는 FixedUpdate 다음 물리 스텝에서야 적용돼서, 아래 높이 상한과
                    // 마지막 maxSpeed 상한이 이 보상을 못 보고 지나친다.
                    velocity.y += up;

                    if (_movement != null && _movement.LogLift)
                        Debug.Log($"[띄움] 스윙 릴리즈 보상 — y속도 +{up:F2}, " +
                                  $"높이 {transform.position.y:F2}m", this);
                }
            }

            // 추가 생성 — 정점이 앵커 높이를 넘지 않게 자른다(LimitReleaseApex 주석 참고).
            velocity = LimitReleaseApex(velocity);

            // 감기 증폭과 겹쳐 상한을 넘지 않게 한다 — maxSpeed는 설계값이 아니라 안전장치다.
            _rb.linearVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
        }

        // 추가 생성 (2026-10-12, Movie_007)
        /// <summary>
        /// 놓은 뒤 정점이 <c>앵커 높이 + releaseApexAboveAnchor</c>를 넘지 않게 위 속도를 자른다.
        ///
        /// <b>공식.</b> 위로 던진 물체가 더 올라가는 높이는 h = vy² / (2g). 거꾸로 "h까지만
        /// 올라가라"면 vy 상한 = √(2gh). 놓은 뒤에는 스윙 중력 배수가 안 걸리고 기본 중력만
        /// 받으므로(올라가는 동안은 PlayerMovement의 낙하 중력도 안 걸린다) g = 9.81 그대로 쓴다.
        ///
        /// <b>왜 앵커 높이인가.</b> 앵커는 항상 건물 벽에 붙는다. 그 높이를 천장으로 삼으면
        /// 건물마다 따로 높이를 재지 않아도 정점이 옥상 근처에서 멈춘다. 인섬니악 스파이더맨도
        /// 스윙만으로는 웹을 붙인 곳보다 높이 못 가고, 높이는 웹 집·포인트 런치로 얻는다.
        ///
        /// <b>깎인 속도를 앞으로.</b> 그냥 버리면 놓는 순간 확 느려진 것처럼 느껴진다.
        /// 일부를 진행 방향으로 돌려서 "위로 못 가는 대신 앞으로 빨라진다"가 되게 한다.
        ///
        /// 유니티 내장 <c>Rigidbody.maxLinearVelocity</c>는 방향 구분 없이 전체 속력만 자르므로
        /// "위로만" 자르는 이 일에는 쓸 수 없어 직접 계산한다.
        /// </summary>
        private Vector3 LimitReleaseApex(Vector3 velocity)
        {
            if (releaseApexAboveAnchor < 0f || velocity.y <= 0f) return velocity;

            float g = -Physics.gravity.y;
            if (g <= 0f) return velocity;

            // 아직 더 올라가도 되는 높이. 이미 앵커보다 위면 0 → 위 속도를 전부 자른다.
            float room = _lastAnchor.y + releaseApexAboveAnchor - _rb.position.y;
            float maxUp = room > 0f ? Mathf.Sqrt(2f * g * room) : 0f;
            if (velocity.y <= maxUp) return velocity;

            float clipped = velocity.y - maxUp;
            velocity.y = maxUp;

            // 수평 방향이 있으면 깎인 만큼의 일부를 그쪽으로 돌린다.
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude > 0.0001f)
                velocity += flat.normalized * (clipped * clippedLiftToForward);

            if (_movement != null && _movement.LogLift)
                Debug.Log($"[띄움] 릴리즈 높이 상한 — y속도 {clipped:F2} 깎음, " +
                          $"정점 ≤ 앵커+{releaseApexAboveAnchor:F1}m", this);

            return velocity;
        }

        /// <summary>
        /// 매달려 있는 동안 타격 피드백을 재운다. <see cref="suppressHitFeedbackWhileSwinging"/>
        /// 참고.
        ///
        /// <c>Instance</c>가 없을 수 있으므로(피드백 매니저가 없는 씬에서도 스윙은 돌아야
        /// 한다) 널 검사를 여기 한 곳에 모은다 — <c>WebGrabThrow</c>가 자기 쪽에
        /// <c>SetHitFeedbackSuppressed</c>를 둔 것과 같은 이유이자 같은 모양이다.
        /// </summary>
        private void SetHitFeedbackSuppressed(bool suppressed)
        {
            // <b>끄는 쪽은 스위치와 무관하게 항상 통과시킨다.</b> 매달린 채로 인스펙터에서
            // 스위치를 내리면 이미 켜둔 억제를 풀어줄 사람이 없어져, 그 뒤로 타격 피드백이
            // 영영 안 나온다 — A/B 스위치를 런타임에 만지는 것이 이 프로젝트의 검증 방식이라
            // 실제로 밟게 되는 경로다.
            if (suppressed && !suppressHitFeedbackWhileSwinging) return;
            if (HitFeedbackManager.Instance != null)
                HitFeedbackManager.Instance.SuppressedBySwing = suppressed;
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

        // 추가 생성
        /// <summary>
        /// 스윙 중에만 중력을 더 건다(<see cref="swingGravityMultiplier"/>).
        ///
        /// Rigidbody가 이미 기본 중력(1배)을 주므로 나머지(배수 - 1)만 더한다.
        /// Unity의 <c>AddForce(..., ForceMode.Acceleration)</c>도 같은 일을 하지만, 힘은 FixedUpdate가
        /// 끝난 뒤 물리 스텝에서 적용돼 바로 다음 줄의 SolveRope가 보지 못한다. 그래서 이 파일의
        /// 다른 곳(ApplyEntrySpeed, ApplyPumpInput)처럼 속도를 직접 바꾼다.
        /// </summary>
        private void ApplySwingGravity()
        {
            if (swingGravityMultiplier <= 1f) return;
            Vector3 extra = Physics.gravity * ((swingGravityMultiplier - 1f) * Time.fixedDeltaTime);
            _rb.linearVelocity = Vector3.ClampMagnitude(_rb.linearVelocity + extra, maxSpeed);
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

            // 대입이냐 MovePosition이냐는 물리적으로 같은 보정이고 차이는 보간뿐이다.
            // position 대입은 유니티가 순간이동으로 취급해 그 스텝의 보간을 버리는데,
            // 팽팽한 동안 매 스텝 일어나므로 그게 곧 "캐릭터만 버벅인다"가 된다.
            //
            // 단 MovePosition은 이번 물리 스텝이 돌아야 반영되므로 _rb.position이 즉시
            // 안 바뀐다 — iterations를 2 이상으로 올리면 뒤 회차가 옛 위치를 읽어 사실상
            // 1회만 먹는다. 지금은 구속이 하나라 1회로 정확해서 문제가 없지만, 전투에서
            // 구속이 둘 이상이 되어 iterations를 올릴 때는 이 스위치를 꺼야 한다.
            if (pull > 0f)
            {
                Vector3 corrected = _rb.position + direction * pull;
                if (interpolatedConstraintMove) _rb.MovePosition(corrected);
                else _rb.position = corrected;
            }

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
