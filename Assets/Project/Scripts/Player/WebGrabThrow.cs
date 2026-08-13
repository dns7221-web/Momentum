using UnityEngine;
using UnityEngine.InputSystem;
using Momentum.Level;

namespace Momentum.Player
{
    /// <summary>
    /// 웹 그랩 & 스로우 — E(물병)는 <b>이동용</b>, 좌클릭으로 잡은 소품은 <b>공격용</b>이다.
    /// 둘 다 던진 뒤 좌클릭으로 다시 맞히면 그쪽으로 확 끌려간다(체이스 대시) — 물병
    /// 쪽은 그렇게 "타고 이동"하는 게 원래 목적이고, 소품 쪽은 덤으로 따라오는 부가
    /// 기능일 뿐, 던지는 순간 자동으로 끌려가지는 않는다(적에게 던졌는데 그쪽으로
    /// 확 끌려가면 이상하다).
    ///
    /// <list type="bullet">
    /// <item><b>E</b> — 주변 상황과 무관하게 <see cref="waterBottlePrefab"/>을 손 위치에
    ///       스폰해 던진다. 웹이 아니라 손이 하는 일이라, 스폰하는 순간 총을 잠깐 숨기고
    ///       던지기 애니메이션을 재생한다</item>
    /// <item><b>좌클릭 → 소품</b> — 갈고리가 <see cref="GrappleGun.IsPropAnchor"/>인
    ///       소품에 웹을 붙인다. 붙어 있는 동안 그 소품을 나를 중심으로 끌고 다닌다
    ///       (<see cref="UpdateHeldPropDrag"/>). 그냥 놓으면 그대로 떨어질 뿐이고,
    ///       잡은 채로 E를 누르면 그 순간 던진다(<see cref="ThrowHeldProp"/>) — 공격이다</item>
    /// <item><b>좌클릭 → 날아가는 물건</b> — 방금 던진 그 물건(물병이든 소품이든)이
    ///       아직 날아가는 중이면(<see cref="GrappleGun.ChaseTarget"/>) 맞히는 순간
    ///       체이스가 된다 — 그 물건이 아니라 <b>내가</b> 그쪽으로 끌려간다</item>
    /// <item><b>좌클릭으로 잡은 채 V</b> — 그랩 앤 슬램(<see cref="BeginSlam"/>). 손에
    ///       붙잡아두고 제자리에서 180도 회전한 뒤(<see cref="PlayerRootMotionRelay"/>가
    ///       그 회전을 애니메이션 루트 모션에서 그대로 뽑아 전달), 회전이 끝난 순간
    ///       <b>내가 새로 바라보는 방향</b>으로 던진다 — 카메라 방향이 아니다, 돌려서
    ///       바라본 방향으로 던져야 회전한 의미가 산다</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(GrappleGun))]
    public class WebGrabThrow : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커 판정을 빌려 쓴다. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        [Tooltip("조준 기준 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private Camera aimCamera = null;

        [Tooltip("던지는 순간 Throw 레이어를 켤 Animator. 비워두면 자식에서 찾는다.")]
        [SerializeField] private Animator animator = null;

        [Tooltip("E키 입력 (InputSystem_Actions에 새로 만들 액션).")]
        [SerializeField] private InputActionReference throwAction = null;

        [Tooltip("V키 입력 — 잡은 소품이 있을 때만 그랩 앤 슬램을 시작한다.")]
        [SerializeField] private InputActionReference slamAction = null;

        [Tooltip("회전 중 루트 모션을 실제로 캐릭터에 전달하는 중계자. 비워두면 자식에서 " +
                 "찾는다 — Animator가 있는 그 오브젝트에 붙어 있어야 한다.")]
        [SerializeField] private PlayerRootMotionRelay rootMotionRelay = null;

        [Header("잡은 소품 끌어당기기 (좌클릭으로 웹이 붙은 동안)")]
        [Tooltip("SwingConstraint가 쓰는 것과 같은 Reel 액션을 그대로 꽂아도 된다 — 스크롤 " +
                 "입력을 읽기만 하고 Enable은 직접 하되 Disable은 하지 않는다(스윙 쪽 소유권을 " +
                 "건드리지 않기 위해서).")]
        [SerializeField] private InputActionReference reelAction = null;

        [Tooltip("Reel 입력 1당 초당 줄어드는 거리(m/s) — 감을수록 소품이 가까워진다.")]
        [Min(0.1f)][SerializeField] private float propReelSpeed = 6f;

        [Tooltip("이보다 가까워지면 더 안 당겨진다 — 완전히 몸에 파고드는 것을 막는다.")]
        [Min(0.1f)][SerializeField] private float propMinHoldDistance = 1.5f;

        [Tooltip("잡으면 이 거리까지 <b>저절로</b> 딸려온다(m). 0이면 자동으로 안 오고 " +
                 "예전처럼 잡은 그 자리에 떠 있는다.\n\n" +
                 "예전에는 잡는 순간의 거리를 그대로 줄 길이로 굳혀서, 멀리 있는 걸 잡으면 " +
                 "그 자리에 뜬 채로 있고 휠로 감아야만 왔다 — '잡았는데 안 온다'로 읽힌다. " +
                 "그렇다고 손 앞까지 끌고 오면 큰 소품이 화면을 가리는 원래 문제로 돌아가므로, " +
                 "가슴이 아니라 이 거리에서 멈춘다.")]
        [Min(0f)][SerializeField] private float propAutoPullDistance = 4f;

        [Tooltip("위 자동 당김의 속도(m/s). 순간이동처럼 붙으면 잡은 느낌이 안 나고, " +
                 "너무 느리면 안 오는 것처럼 보인다.")]
        [Min(0.1f)][SerializeField] private float propAutoPullSpeed = 12f;

        [Tooltip("잡은 소품이 목표 지점(내 몸 기준 저장해둔 방향 × 줄 길이)으로 얼마나 " +
                 "적극적으로 따라오는지. 클수록 뻣뻣하게, 작을수록 느슨하게 따라온다.")]
        [Min(0.1f)][SerializeField] private float propDragResponsiveness = 6f;

        [Tooltip("소품을 끌어당기는 최대 속도(m/s) — 멀리서 확 잡아채는 걸 막는 상한선.")]
        [Min(0.1f)][SerializeField] private float propMaxDragSpeed = 20f;

        [Header("잡은 소품 던지기 (잡은 채로 E)")]
        [Tooltip("던진 직후 이 시간(초) 동안 와이어가 날아가는 소품을 쫓아가며 그려지다가 " +
                 "풀린다 — 놓자마자 뚝 끊기지 않게.")]
        [Min(0f)][SerializeField] private float ropeReleaseTrailDuration = 0.25f;

        [Header("물병 스폰 → 던지기 (E, 항상 가능)")]
        [Tooltip("E를 누르면 손에 스폰해 던지는 프리팹. Rigidbody가 있어야 한다 — 없으면 " +
                 "자동으로 붙인다.")]
        [SerializeField] private GameObject waterBottlePrefab = null;

        [Tooltip("물병이 스폰·부착될 손 위치. 비워두면 GrappleGun의 Muzzle(손목 소켓)을 쓴다.")]
        [SerializeField] private Transform handAnchor = null;

        [Tooltip("던진 물병의 최종 월드 크기 배율. 1이면 프리팹 원본 크기 그대로 — " +
                 "손 뼈(handAnchor)가 자체 스케일을 갖고 있어도 그 영향을 상쇄하고 이 " +
                 "배율만 적용한다.")]
        [Min(0.01f)][SerializeField] private float thrownScale = 1f;

        [Tooltip("E를 누른 뒤 실제로 던져지기까지의 시간(초). 던지기 애니메이션이 팔을 " +
                 "휘두르는 시점에 맞춰야 자연스럽다 — 클립 길이를 보고 맞출 것.")]
        [Min(0f)][SerializeField] private float throwWindupDelay = 0.4f;

        [Tooltip("던지는 순간부터 Throw 레이어 가중치를 1로 유지하는 시간(초). " +
                 "GrappleGun의 GrappleFire 레이어와 같은 방식 — 클립 길이보다 조금 짧게 " +
                 "두면 자연스럽게 겹쳐 끊긴다.")]
        [Min(0f)][SerializeField] private float throwLayerHoldTime = 0.5f;

        [Tooltip("Throw·GrabAndSlam 레이어 가중치가 0↔1을 오가는 속도(초당). 8이면 0.125초.\n\n" +
                 "0으로 두면 예전처럼 한 프레임에 대입한다 — 스윙 자세가 뚝 끊기고 " +
                 "던지기 자세로 갈아 끼워지는 그 그림으로 즉시 돌아간다. 너무 느리게 잡으면 " +
                 "(2 이하) 던지는 동작이 다 끝나도록 가중치가 안 차서 팔만 어정쩡하게 든다.\n\n" +
                 "SwingConstraint의 animLayerLerpSpeed와 같은 단위·같은 기본값이다.")]
        [Min(0f)][SerializeField] private float layerFadeSpeed = AnimatorLayerFade.DefaultSpeed;

        [Tooltip("놓는 순간 조준 방향으로 얹는 속도(m/s). 여기에 내 현재 속도가 그대로 " +
                 "더해진다 — 정지 상태로 던지면 이 값이 전부고, 스윙 중에 던지면 그만큼 더 세다.")]
        [Min(0f)][SerializeField] private float throwForce = 22f;

        [Tooltip("던지는 순간 소품에 얹는 무작위 회전 속도(도/초)의 최대치 — 미끄러지듯 " +
                 "날아가지 않고 데굴데굴 구르며 날아가게 한다. 축·부호가 매번 무작위다.")]
        [Min(0f)][SerializeField] private float thrownSpinSpeed = 720f;

        [Header("웹 체이스 대시 — 던진 소품에 좌클릭이 맞으면 그쪽으로 확 끌려간다")]
        [Tooltip("던진 뒤 이 시간(초) 안에 맞혀야 체이스가 성립한다. 넘기면 그냥 평범하게 " +
                 "던진 걸로 끝난다 — 영원히 '맞힐 수 있는' 상태로 남으면 안 쓰는 소품에 " +
                 "조준 보조가 계속 붙어 있는 이상한 상황이 생긴다.")]
        [Min(0.1f)][SerializeField] private float chaseWindowDuration = 2.5f;

        [Tooltip("던진 소품의 속도가 이 아래로 떨어지면(=착지해서 멈췄다) 체이스 기회를 " +
                 "접는다. 시간이 남아 있어도 이미 바닥에 구르는 병을 다시 맞혀 날아가는 건 " +
                 "이상하다.")]
        [Min(0f)][SerializeField] private float chaseSettleSpeed = 1f;

        [Tooltip("맞았을 때 그 소품을 향해 끌려가는 속도(m/s).")]
        [Min(1f)][SerializeField] private float chaseSpeed = 26f;

        [Tooltip("체이스로 낼 수 있는 <b>최대</b> 속력(m/s). 위 chaseSpeed는 이제 하한이고, " +
                 "체이스를 걸 때 이미 그보다 빠르면 <b>그 속력을 그대로 이어받는다</b>.\n\n" +
                 "예전에는 속력을 chaseSpeed로 덮어썼는데, 그러면 35m/s로 날다 체이스를 건 " +
                 "플레이어가 26으로 <b>느려진다</b> — 맨 위 표의 '번다 → 지킨다 → 쓴다'와 " +
                 "정반대로, 스윙을 잘한 쪽이 손해를 본다.\n\n" +
                 "이 값을 chaseSpeed와 같게 두면 예전처럼 항상 고정 속력이 된다.")]
        [Min(1f)][SerializeField] private float chaseMaxSpeed = 40f;

        [Tooltip("소품과 이만큼 가까워지면 도착으로 보고 놓는다.")]
        [Min(0.5f)][SerializeField] private float chaseArrivalDistance = 2f;

        [Tooltip("도착 시 최소 수평 속도(m/s). GrappleZip과 같은 원칙 — 이미 이보다 빠르면 " +
                 "그 속도를 유지한다.")]
        [Min(0f)][SerializeField] private float chaseExitSpeed = 16f;

        [Tooltip("도착 시 위로 얹는 속도(m/s). 순수 수평이면 곧장 떨어지므로, 체공 시간을 " +
                 "벌어 다음 앵커를 찾을 틈을 준다.")]
        [Min(0f)][SerializeField] private float chaseExitUpSpeed = 8f;

        [Tooltip("체이스가 이 시간(초)을 넘기면 도착 판정과 무관하게 강제로 끊는다. 표면 " +
                 "기준 거리로 재도 못 예측한 지형에 끼어 도착 조건을 영영 못 만나는 경우의 " +
                 "안전장치 — 이게 없으면 SuspendHorizontalControl이 계속 켜진 채로 안 풀려 " +
                 "플레이어가 영원히 조작 불능이 될 수 있다.")]
        [Min(0.5f)][SerializeField] private float chaseTimeout = 5f;

        [Header("그랩 앤 슬램 (잡은 채로 V — 회전하며 던지기)")]
        [Tooltip("V를 누른 뒤 실제로 던져지기까지의 시간(초) — 회전 애니메이션 길이에 " +
                 "맞출 것. 이 동안 캐릭터가 제자리에서 180도 돈다.")]
        [Min(0.1f)][SerializeField] private float slamDuration = 1f;

        [Tooltip("슬램 던지기 힘(m/s). 일반 던지기(throwForce)보다 세게 잡아 회전 관성이 " +
                 "실린 느낌을 낸다.")]
        [Min(0f)][SerializeField] private float slamThrowForce = 30f;

        [Tooltip("소품이 손 쪽으로 당겨지는 최대 거리(m) — 잡았던 원래 위치 기준. 손까지 " +
                 "완전히 끌어오는 게 아니라, 당겨졌다는 걸 알아볼 수 있을 만큼만 아주 " +
                 "조금 움직인다.")]
        [Min(0f)][SerializeField] private float slamPullDistance = 0.35f;

        [Tooltip("소품 질량이 이걸 넘으면 그랩 앤 슬램 자체를 안 받는다 — 화물 컨테이너 " +
                 "같은 무거운 소품을 들고 도는 건 말이 안 된다.")]
        [Min(0f)][SerializeField] private float maxSlamMass = 100f;

        [Header("디버그")]
        [Tooltip("소품을 잡고 있는 동안 매 프레임 모든 Animator 레이어의 가중치를 콘솔에 " +
                 "찍는다 — 원인 불명의 '뜨는' 문제처럼 어느 레이어가 새는지 눈으로 봐야 " +
                 "할 때만 켤 것. Inspector를 Debug 모드로 바꾸는 것과 같은 정보를 " +
                 "콘솔로 대신 보는 것뿐이다.")]
        [SerializeField] private bool logLayerWeights = false;

        /// <summary>지금 E로 잡아서 던지는 중간(대기 동작)인가.</summary>
        public bool IsWindingUp { get; private set; }

        /// <summary>지금 방금 던진 소품을 향해 체이스 대시 중인가.</summary>
        public bool IsChasing { get; private set; }

        /// <summary>지금 V로 그랩 앤 슬램 중(제자리 회전 → 던지기)인가.</summary>
        public bool IsSlamming { get; private set; }

        private Rigidbody _rb;

        /// <summary>내 몸의 콜라이더 전부. 잡은 소품과의 충돌을 끌 때 하나라도 빠지면
        /// 그 하나가 소품에 밀려 몸이 떠오른다.</summary>
        private Collider[] _playerColliders;
        private PlayerMovement _movement;
        private Rigidbody _propBody;

        /// <summary>지금 좌클릭으로 끌고 다니는 중인 소품. <see cref="_thrownBody"/>와 달리
        /// "붙잡혀서 나를 따라다니는" 대상이고, 놓으면 바로 null로 돌아간다.</summary>
        private Rigidbody _heldBody;

        /// <summary>지금 좌클릭으로 잡고 있는 소품까지의 줄 길이(m). 0이면 "아직 안 재봤다" —
        /// 새로 잡을 때마다 그 순간의 거리로 다시 잰다. Reel 입력으로 이 값이 줄어든다.</summary>
        private float _heldRopeLength;

        /// <summary>잡는 순간의 소품 방향을 내 몸 기준 로컬 방향으로 저장해둔 것. 매 프레임
        /// <see cref="Transform.TransformDirection(Vector3)"/>로 다시 월드 방향을 구해, 내가
        /// 돌면 그 방향도 같이 돈다 — 소품이 나를 중심으로 따라 도는 이유다.</summary>
        private Vector3 _heldLocalDir;

        /// <summary>지난 프레임의 목표 지점. 이번 프레임 목표 지점과의 차이 ÷ 시간이
        /// "내가 돌면서 실제로 만들어낸 회전 속도"가 된다 — 이게 있어야 놓는 순간 관성이
        /// 진짜로 실린다. 목표 지점을 향한 위치 보정만으로는 가만히 있다 놓을 때 속도가
        /// 0에 가까워 던져도 그냥 툭 떨어진다.</summary>
        private Vector3 _prevHeldTargetPoint;
        private bool _hasPrevHeldTarget;

        /// <summary>와이어 잔상이 쫓아가야 할 대상. <see cref="_thrownBody"/>는 체이스 판정용
        /// "정착 감지"로 일찍 null이 될 수 있어서, 잔상 타이밍은 이걸로 따로 관리한다.</summary>
        private Rigidbody _ropeTrailBody;

        /// <summary><see cref="GrappleGun.OverrideRopeTarget"/>을 지금 내가 켜둔 상태인지.
        /// GrappleZip도 같은 필드를 견인 로프 표시에 쓰므로, 내가 켜지 않았는데 끄면
        /// 견인 중인 로프까지 매 프레임 지워버리게 된다.</summary>
        private bool _ownsRopeOverride;

        /// <summary>지금 체이스 대시 중에 쫓아가는 대상 — 좌클릭으로 다시 맞힌 그
        /// 물건(물병이든 소품이든)을 직접 붙잡아둔다. <see cref="GrappleGun.AnchorPoint"/>를
        /// 매 프레임 다시 읽는 대신 이걸로 쫓아간다.</summary>
        private Rigidbody _chaseTargetBody;

        /// <summary>체이스가 <see cref="chaseTimeout"/>을 넘겼는지 재는 시각(<see cref="Time.time"/> 기준).</summary>
        private float _chaseDeadline;

        /// <summary>이 시간(<see cref="Time.time"/> 기준)까지는 와이어가 <see cref="_ropeTrailBody"/>를
        /// 쫓아가며 그려진다 — 놓는 순간 뚝 끊기지 않고 풀려나가는 느낌을 준다.</summary>
        private float _ropeReleaseUntil;

        /// <summary>던지기까지 남은 대기 시간(초). <see cref="throwWindupDelay"/>에서 줄어든다.</summary>
        private float _windupRemaining;

        /// <summary>방금 던진 소품. 이게 남아있는 동안만 맞히면 체이스가 성립한다.</summary>
        private Rigidbody _thrownBody;

        /// <summary>체이스 기회가 남은 시간(초). 0 이하가 되면 <see cref="_thrownBody"/>를 놓아준다.</summary>
        private float _chaseWindowRemaining;

        /// <summary>지금 슬램 중인 소품. <see cref="_heldBody"/>는 슬램을 시작하는 순간
        /// 비워버린다 — 회전 중엔 드래그 로직(<see cref="UpdateHeldPropDrag"/>)이 안
        /// 끼어들어야 해서 별도로 들고 있는다.</summary>
        private Rigidbody _slamBody;

        /// <summary>슬램을 시작한 순간 소품의 위치. 매 프레임 이 지점 기준으로 최대
        /// <see cref="slamPullDistance"/>만큼만 손 쪽으로 당긴 지점을 계산한다 — 현재
        /// 위치에서 누적으로 계속 다가가게 하면 결국 손까지 다 끌려오므로, 항상 이
        /// 원점에서 다시 재는 것이 핵심이다.</summary>
        private Vector3 _slamStartPosition;

        /// <summary>슬램 회전이 끝나기까지 남은 시간(초). <see cref="slamDuration"/>에서 줄어든다.</summary>
        private float _slamRemaining;

        private static readonly int ThrowTriggerHash = Animator.StringToHash("ThrowTrigger");
        private static readonly int GrabAndSlamHash = Animator.StringToHash("GrabAndSlam");

        /// <summary>Animator에 GrabAndSlam 트리거 파라미터가 실제로 있는지. 없는 채로
        /// <c>SetTrigger</c>를 부르면 조용히 무시되지 않고 매번 에러를 찍는다 — 컨트롤러에
        /// 파라미터를 아직 안 넣어도 콘솔이 안 더러워지도록 미리 있는지 확인해둔다.</summary>
        private bool _hasSlamParam;

        /// <summary>Throw 레이어 인덱스. 클립을 안 채웠으면 -1로 남고 가중치 조작을 건너뛴다.</summary>
        private int _throwLayerIndex = -1;
        private float _throwWeightUntil;

        /// <summary>GrabAndSlam 레이어 인덱스. Throw와 달리 짧은 펄스 타이머가 아니라
        /// <see cref="IsSlamming"/> 값을 그대로 매 프레임 반영한다 — 시작·끝 시점을
        /// 이미 정확히 알고 있으니 별도 타이머로 근사할 이유가 없다.</summary>
        private int _slamLayerIndex = -1;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _playerColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            _movement = GetComponent<PlayerMovement>();
            if (gun == null) gun = GetComponent<GrappleGun>();
            if (aimCamera == null) aimCamera = Camera.main;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) _throwLayerIndex = animator.GetLayerIndex("Throw");
            if (animator != null) _slamLayerIndex = animator.GetLayerIndex("GrabAndSlam");
            if (handAnchor == null && gun != null) handAnchor = gun.Muzzle;
            if (rootMotionRelay == null) rootMotionRelay = GetComponentInChildren<PlayerRootMotionRelay>();

            if (animator != null)
            {
                foreach (var param in animator.parameters)
                {
                    if (param.type == AnimatorControllerParameterType.Trigger && param.nameHash == GrabAndSlamHash)
                    {
                        _hasSlamParam = true;
                        break;
                    }
                }
            }

            if (_playerColliders == null || _playerColliders.Length == 0)
                Debug.LogWarning("[WebGrabThrow] 플레이어 콜라이더를 못 찾아 프롭과의 충돌을 못 끈다.", this);
            if (throwAction == null)
                Debug.LogWarning("[WebGrabThrow] Throw Action이 비어 있어 E로 던지는 동작이 안 된다.", this);
            if (waterBottlePrefab == null)
                Debug.LogWarning("[WebGrabThrow] Water Bottle Prefab이 비어 있어 E로 던질 게 없다.", this);
            if (rootMotionRelay == null)
                Debug.LogWarning("[WebGrabThrow] Root Motion Relay를 못 찾아 슬램 회전이 안 보일 수 있다.", this);
        }

        private void OnEnable()
        {
            // Reel은 SwingConstraint도 같은 액션을 쓸 수 있다 — 소유권 다툼을 피하려고
            // 여기선 Enable만 하고(중복 호출은 안전) Disable은 절대 안 한다.
            reelAction?.action.Enable();

            if (slamAction != null)
            {
                slamAction.action.Enable();
                slamAction.action.performed += OnSlamPerformed;
            }

            if (throwAction == null) return;
            throwAction.action.Enable();
            throwAction.action.performed += OnThrowPerformed;
        }

        private void OnDisable()
        {
            if (throwAction != null)
            {
                throwAction.action.performed -= OnThrowPerformed;
                throwAction.action.Disable();
            }

            if (slamAction != null)
            {
                slamAction.action.performed -= OnSlamPerformed;
                slamAction.action.Disable();
            }

            // 대기·슬램·체이스 중에 꺼지면 정리한다.
            if (IsWindingUp) CancelWindup();
            if (IsSlamming) CancelSlam();
            if (IsChasing) EndChase(boost: false);

            if (_heldBody != null)
            {
                SetIgnorePlayerCollision(_heldBody, false);
                SetHitFeedbackSuppressed(false);
            }
            _heldBody = null;
            _heldRopeLength = 0f;

            if (gun != null && gun.ChaseTarget == _thrownBody) gun.ChaseTarget = null;
            _thrownBody = null;
        }

        /// <summary>
        /// Throw 레이어 가중치를 켰다 끈다. <see cref="GrappleGun.UpdateFireLayerWeight"/>와
        /// 같은 방식이다 — 매 프레임 무조건 돈다. 잡은 소품을 막 던졌으면, 와이어가 뚝
        /// 끊기지 않고 <see cref="ropeReleaseTrailDuration"/>초 동안 날아가는 소품을 쫓아가며
        /// 그려지다가 풀리게 한다(<see cref="GrappleGun.OverrideRopeTarget"/>).
        /// </summary>
        private void Update()
        {
            // 던지기는 양쪽으로 서서히 간다. 대입하면 스윙으로 날아가던 자세가 그 프레임에
            // 통째로 갈아 끼워졌다가 0.4초 뒤 다시 갈아 끼워진다 — 공격이 스윙과 안
            // 이어져 보이던 것이 이것이다. 루트 모션을 안 쓰는 레이어라 켜는 쪽도 안전하다.
            AnimatorLayerFade.To(animator, _throwLayerIndex,
                                 Time.time < _throwWeightUntil ? 1f : 0f, layerFadeSpeed);

            // 슬램은 <b>켤 때만 즉시</b>다. 이 레이어의 루트 모션(제자리 180도 회전)을
            // PlayerRootMotionRelay가 뽑아 쓰는데, 가중치가 곧 그 회전의 크기라서 서서히
            // 켜면 도는 각도가 그만큼 모자란다 — 돌아본 방향으로 던진다는 규칙이 깨진다.
            // 끌 때는 회전이 이미 끝난 뒤라 서서히 꺼도 안전하고, 그래야 던지고 나서
            // 원래 자세로 뚝 끊기지 않는다.
            AnimatorLayerFade.To(animator, _slamLayerIndex, IsSlamming ? 1f : 0f,
                                 IsSlamming ? 0f : layerFadeSpeed);

            if (logLayerWeights && animator != null && _heldBody != null)
            {
                var sb = new System.Text.StringBuilder("[WebGrabThrow] 레이어 가중치 — ");
                for (int i = 0; i < animator.layerCount; i++)
                    sb.Append($"{animator.GetLayerName(i)}={animator.GetLayerWeight(i):F2} ");
                sb.Append($"| Grounded={animator.GetBool("Grounded")}");
                Debug.Log(sb.ToString());
            }

            if (gun == null) return;

            if (_ropeTrailBody != null && Time.time < _ropeReleaseUntil)
            {
                gun.OverrideRopeTarget = _ropeTrailBody.position;
                _ownsRopeOverride = true;
            }
            else if (_ownsRopeOverride)
            {
                // OverrideRopeTarget은 GrappleZip도 견인 로프를 그리는 데 같이 쓴다 —
                // 내가 설정한 값이 아닌데도 "지금 내 잔상 조건이 꺼졌으니 지운다"고
                // 무조건 null을 대입하면, 견인 중인 GrappleZip의 목표까지 매 프레임
                // 지워버려 견인 로프가 거의 안 보이는 사고가 났다. 내가 켰을 때만 끈다.
                gun.OverrideRopeTarget = null;
                _ownsRopeOverride = false;
                _ropeTrailBody = null;
            }
        }

        /// <summary>
        /// E를 눌렀다. 지금 좌클릭으로 소품을 잡고 있으면 <b>그 소품을 던진다</b> —
        /// 마우스를 놓는 것과 던지는 걸 분리해뒀다, 그냥 놓으면 잡은 게 풀릴 뿐이고
        /// 던지려면 E를 따로 눌러야 한다. 잡은 게 없으면 평소대로 물병을 스폰해 던진다.
        /// 이미 뭔가 대기·체이스 중이면 무시한다.
        /// </summary>
        private void OnThrowPerformed(InputAction.CallbackContext ctx)
        {
            if (IsChasing) return;

            if (_heldBody != null)
            {
                ThrowHeldProp(_heldBody);
                _heldBody = null;
                _heldRopeLength = 0f;
                return;
            }

            if (IsWindingUp || _propBody != null) return;
            if (waterBottlePrefab == null) return;

            Transform anchor = handAnchor != null ? handAnchor : transform;
            var instance = Instantiate(waterBottlePrefab, anchor.position, anchor.rotation);
            instance.transform.localScale = waterBottlePrefab.transform.localScale * thrownScale;

            var spawnedBody = instance.GetComponent<Rigidbody>();
            if (spawnedBody == null) spawnedBody = instance.AddComponent<Rigidbody>();
            if (instance.GetComponent<CombatProp>() == null) instance.AddComponent<CombatProp>();

            BeginWindup(spawnedBody);
        }

        /// <summary>
        /// V를 눌렀다. 잡고 있는 소품이 있을 때만 의미가 있다 — 빈손이거나 이미 뭔가
        /// (대기·슬램·체이스) 진행 중이면 무시한다. <see cref="maxSlamMass"/>를 넘는
        /// 소품(화물 컨테이너 등)은 애초에 받지 않는다 — 무거운 걸 들고 도는 건 말이
        /// 안 된다.
        /// </summary>
        private void OnSlamPerformed(InputAction.CallbackContext ctx)
        {
            if (_heldBody == null) return;
            if (IsWindingUp || IsSlamming || IsChasing) return;
            if (_heldBody.mass > maxSlamMass) return;

            BeginSlam(_heldBody);
        }

        /// <summary>
        /// 매달린 프롭과 내 몸의 충돌 판정을 껐다 켠다. 잡고 있는 물건이 주인 몸과
        /// 충돌 판정을 갖는 게 애초에 이상하다.
        /// </summary>
        private void SetIgnorePlayerCollision(Rigidbody body, bool ignore)
        {
            if (body == null || _playerColliders == null) return;

            // 내 콜라이더를 <b>전부</b> 돈다. 예전엔 GetComponent&lt;Collider&gt;()로 루트의
            // 하나만 잡았는데, 거기서 빠진 콜라이더가 있으면 잡고 있는 소품이 그 콜라이더와
            // 그대로 부딪혀 몸을 밀어 올린다 — 소품을 잡는 순간 캐릭터가 뜨던 증상의
            // 후보 중 하나가 이것이었다.
            foreach (var mine in _playerColliders)
            {
                if (mine == null) continue;
                foreach (var col in body.GetComponentsInChildren<Collider>())
                {
                    if (col == null) continue;
                    Physics.IgnoreCollision(mine, col, ignore);
                }
            }
        }

        /// <summary>
        /// 잡고 드래그하는 동안(<see cref="UpdateHeldPropDrag"/>)은 카메라 방향을 따라
        /// 도는 목표 지점이 다른 소품·바닥·벽을 스치고 지나가는 경우가 있다. 처음엔
        /// 잡은 소품 자신의 CombatProp만 꺼봤지만, 스친 상대 소품이 다시 그 옆 소품을
        /// 치는 식으로 연쇄가 번져 히트스톱이 초당 여러 번 재발동, TimeScale이 거의
        /// 계속 바닥에 눌려있는 문제가 있었다(캐릭터가 "뜨는" 것처럼 보인 원인) — 부딪힌
        /// 상대 하나하나를 가려낼 게 아니라, 손으로 뭔가를 붙잡고 있는 동안은
        /// <see cref="HitFeedbackManager"/>의 피드백 발동 자체를 전역으로 꺼두는 쪽이
        /// 훨씬 단순하고 확실하다. 놓거나·던지거나·슬램으로 넘어가는 순간 즉시 되돌린다.
        /// </summary>
        private void SetHitFeedbackSuppressed(bool suppressed)
        {
            if (HitFeedbackManager.Instance != null)
                HitFeedbackManager.Instance.SuppressedByHold = suppressed;
        }

        /// <summary>
        /// 방금 던진 소품에 "날아가는 중"을 알린다. 소품은 평소 <c>Discrete</c>로 있다가
        /// 이때만 <c>Continuous</c>로 올라가고 첫 충돌에서 되돌아간다 —
        /// <see cref="Level.CombatProp.BeginThrownFlight"/> 참고.
        ///
        /// <b>속도를 대입한 뒤에 부른다.</b> 순서가 바뀌면 PhysX가 아직 0인 속도로 스윕
        /// 준비를 하게 되고, 정작 뚫으면 안 되는 첫 스텝이 예전 모드로 돈다.
        /// </summary>
        private static void MarkThrown(Rigidbody body)
        {
            if (body != null && body.TryGetComponent(out Level.CombatProp prop))
                prop.BeginThrownFlight();
        }

        private void FixedUpdate()
        {
            UpdateThrownTracking();

            if (IsWindingUp)
            {
                // 스폰한 물병이 손 위치를 따라가게 한다.
                if (_propBody != null)
                {
                    Transform anchor = handAnchor != null ? handAnchor : transform;
                    _propBody.MovePosition(anchor.position);
                    _propBody.MoveRotation(anchor.rotation);
                }

                _windupRemaining -= Time.fixedDeltaTime;
                if (_windupRemaining <= 0f) FinishWindup();
                return;
            }

            if (IsSlamming)
            {
                // 소품이 손 쪽으로 아주 살짝만 당겨진다 — "당겼다"는 게 보일 정도로만.
                // 매번 _slamStartPosition(잡았던 원래 자리) 기준으로 다시 계산하는 게
                // 핵심이다 — 만약 매 프레임 "지금 위치"에서 손 쪽으로 조금씩 더 다가가게
                // 하면, 프레임이 쌓이면서 결국 손까지 다 끌려온다. 항상 같은 원점에서
                // 최대 slamPullDistance만큼만 재면 그 이상은 절대 안 당겨진다.
                if (_slamBody != null)
                {
                    Transform anchor = handAnchor != null ? handAnchor : transform;
                    Vector3 next = Vector3.MoveTowards(_slamStartPosition, anchor.position, slamPullDistance);
                    _slamBody.MovePosition(next);
                    _slamBody.MoveRotation(anchor.rotation);
                }

                _slamRemaining -= Time.fixedDeltaTime;
                if (_slamRemaining <= 0f) FinishSlam();
                return;
            }

            // 좌클릭으로 소품에 웹이 붙는 것 자체(IsPropAnchor)는 GrappleGun이 이미 다 한다.
            // 여기서 하는 일은 딱 하나 — 잡고 있는 동안 그 소품을 나를 중심으로 끌고
            // 다니는 것뿐이다. 던지기 동작은 태우지 않는다.
            bool holdingProp = !IsChasing && gun != null && gun.IsAttached
                             && gun.IsPropAnchor && gun.AnchorBody != null;

            if (holdingProp)
            {
                if (gun.AnchorBody == _thrownBody)
                {
                    // 방금 던진 그 물건에 좌클릭이 다시 맞았다 — 체이스 대시.
                    BeginChase(gun.AnchorBody);
                }
                else
                {
                    UpdateHeldPropDrag(gun.AnchorBody);
                }
            }
            else if (_heldBody != null)
            {
                // 좌클릭을 그냥 놓았다 — 던지는 게 아니라 놓는 것뿐이다. 던지려면
                // 잡고 있는 동안 E를 따로 눌러야 한다(OnThrowPerformed 참고).
                SetIgnorePlayerCollision(_heldBody, false);
                SetHitFeedbackSuppressed(false);
                _heldBody = null;
                _heldRopeLength = 0f;
            }

            if (IsChasing) UpdateChase();
        }

        /// <summary>
        /// 좌클릭으로 잡고 있는 소품을 나를 중심으로 끌고 다닌다. 처음 잡는 순간의
        /// 거리·방향을 기준 삼아, 내가 돌면 그 방향도 같이 돌고(<see cref="_heldLocalDir"/>),
        /// Reel 입력으로 거리(<see cref="_heldRopeLength"/>)를 줄여 당겨온다.
        ///
        /// 방향 기준은 <b>카메라</b>다, 내 몸(<see cref="Transform"/>)이 아니다 — 이동
        /// 방향에 카메라 상대 조작이라 몸은 실제로 움직일 때만 서서히 따라 돈다. 몸을
        /// 기준으로 삼으면 제자리에 서서 시점만 돌릴 때 목표 지점이 거의 안 움직여서
        /// 다음 항목이 늘 0에 가까운 값만 낸다.
        ///
        /// 속도는 목표 지점으로의 위치 보정만 쓰지 않는다 — 그러면 스핀을 멈추고 가만히
        /// 있다 놓는 순간 속도가 0에 가까워, 돌리다 놓아도 그냥 툭 떨어진다. 목표 지점
        /// 자체가 매 프레임 얼마나 움직였는지(<see cref="_prevHeldTargetPoint"/>)를 더해서,
        /// 내가 실제로 돌리며 만든 회전 속도가 그대로 실리게 한다.
        /// </summary>
        private void UpdateHeldPropDrag(Rigidbody body)
        {
            if (_heldBody != body)
            {
                if (_heldBody != null)
                {
                    SetIgnorePlayerCollision(_heldBody, false);
                }
                _heldBody = body;
                SetIgnorePlayerCollision(_heldBody, true);
                SetHitFeedbackSuppressed(true);
                _heldRopeLength = 0f;
                _hasPrevHeldTarget = false;
            }

            // 방향 기준은 카메라다, 내 몸이 아니다 — 이동 중이 아니면 몸(transform)은
            // 마우스로 시점을 돌려도 같이 안 돈다(카메라 상대 이동 방식이라 몸은 움직일
            // 때만 서서히 따라 돈다). 몸 기준으로 계산하면 제자리에서 시점만 돌릴 때
            // 목표 지점이 거의 안 움직여서 놓아도 속도가 0에 가깝다.
            Transform dirRef = aimCamera != null ? aimCamera.transform : transform;

            Vector3 toBody = body.position - transform.position;
            if (_heldRopeLength <= 0f)
            {
                // 방금 잡았다 — 지금 이 거리·방향을 기준 줄로 삼는다.
                _heldRopeLength = Mathf.Max(toBody.magnitude, propMinHoldDistance);
                _heldLocalDir = dirRef.InverseTransformDirection(toBody.normalized);
            }

            // 잡았으면 저절로 딸려온다. 예전에는 잡는 순간의 거리를 그대로 굳혀서 멀리
            // 있는 걸 잡으면 그 자리에 뜬 채 휠로 감아야만 왔고, 그게 "잡았는데 안 온다"로
            // 읽혔다. 손 앞까지 끌고 오면 큰 소품이 화면을 가리므로 목표 거리에서 멈춘다.
            //
            // 이미 그보다 가까이 있으면 밀어내지 않는다 — MoveTowards는 양방향이라
            // 그냥 쓰면 가까이서 잡은 소품이 오히려 뒤로 밀려난다.
            if (propAutoPullDistance > 0f)
            {
                float pullTarget = Mathf.Max(propAutoPullDistance, propMinHoldDistance);
                if (_heldRopeLength > pullTarget)
                {
                    _heldRopeLength = Mathf.MoveTowards(_heldRopeLength, pullTarget,
                                                        propAutoPullSpeed * Time.fixedDeltaTime);
                }
            }

            if (reelAction != null)
            {
                float reelInput = reelAction.action.ReadValue<float>();
                _heldRopeLength = Mathf.Max(propMinHoldDistance,
                    _heldRopeLength - reelInput * propReelSpeed * Time.fixedDeltaTime);
            }

            Vector3 targetPoint = transform.position
                + dirRef.TransformDirection(_heldLocalDir) * _heldRopeLength;

            Vector3 targetVelocity = _hasPrevHeldTarget
                ? (targetPoint - _prevHeldTargetPoint) / Time.fixedDeltaTime
                : Vector3.zero;
            _prevHeldTargetPoint = targetPoint;
            _hasPrevHeldTarget = true;

            Vector3 correction = (targetPoint - body.position) * propDragResponsiveness;
            body.linearVelocity = Vector3.ClampMagnitude(targetVelocity + correction, propMaxDragSpeed);
        }

        /// <summary>
        /// 잡고 있던 소품을 E로 직접 던진다 — 이건 <b>공격</b>이다. 마우스를 놓는 것과는
        /// 분리돼 있다 — 그냥 놓으면 잡은 게 풀릴 뿐이고, 던지려면 이걸 눌러야 한다.
        /// 던지는 힘은 물병(<see cref="FinishWindup"/>)과 같은 공식이다 — <b>내 현재
        /// 속도 + 조준 방향 × throwForce</b>. 아직 마우스를 쥔 채로 E를 누를 수 있어서,
        /// 여기서 직접 갈고리를 떼어준다(<see cref="GrappleGun.ForceDetach"/>) — 안
        /// 그러면 소품은 날아가는데 웹은 붙어있다고 착각한 채로 남는다.
        /// </summary>
        private void ThrowHeldProp(Rigidbody body)
        {
            SetIgnorePlayerCollision(body, false);
            SetHitFeedbackSuppressed(false);
            if (gun != null && gun.AnchorBody == body) gun.ForceDetach();

            if (animator != null)
            {
                animator.SetTrigger(ThrowTriggerHash);
                _throwWeightUntil = Time.time + throwLayerHoldTime;
            }

            Vector3 aimDirection = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            body.linearVelocity = _rb.linearVelocity + aimDirection * throwForce;
            body.angularVelocity = RandomSpin();
            MarkThrown(body);

            // 와이어 잔상 — 던진 직후 잠깐 소품을 쫓아가며 그려지다가 풀린다.
            _ropeTrailBody = body;
            _ropeReleaseUntil = Time.time + ropeReleaseTrailDuration;

            // 체이스 대상으로만 등록한다 — E쪽 FinishWindup과 같은 절차.
            _thrownBody = body;
            _chaseWindowRemaining = chaseWindowDuration;
            if (gun != null) gun.ChaseTarget = body;
        }

        /// <summary>
        /// 그랩 앤 슬램을 시작한다 — 소품을 손을 향해 미끄러져 들어오게 얼려두고(순간이동은
        /// 아니다, FixedUpdate의 <c>MoveTowards</c> 참고), 웹의 실제 부착(<c>gun.IsAttached</c>)은
        /// 뗀다(더는 웹으로 붙잡는 게 아니라 손으로 들고 있는 거라 <see cref="BeginWindup"/>과
        /// 같은 이유) — 대신 와이어 <b>잔상</b>은 던지는 순간까지 계속 이어 그려서(아래
        /// <c>_ropeTrailBody</c>) 웹이 뚝 끊기지 않고 자연스럽게 소품을 쫓아가며 보이게 한다.
        /// 회전 애니메이션은 여기서 재생하고, 실제로 던지는 건 <see cref="FinishSlam"/>에서다.
        /// </summary>
        private void BeginSlam(Rigidbody body)
        {
            SetIgnorePlayerCollision(body, true);
            SetHitFeedbackSuppressed(false);
            if (gun != null && gun.AnchorBody == body) gun.ForceDetach();

            _slamBody = body;
            _slamStartPosition = body.position;
            IsSlamming = true;
            _slamRemaining = slamDuration;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            _heldBody = null;
            _heldRopeLength = 0f;

            // 회전·던지기 내내(그리고 던진 뒤 잔상까지) 와이어가 소품을 쫓아가며 그려지게
            // 한다 — ThrowHeldProp의 잔상과 같은 메커니즘, 그냥 지속 시간을 슬램 전체를
            // 덮도록 늘린 것뿐이다.
            _ropeTrailBody = body;
            _ropeReleaseUntil = Time.time + slamDuration + ropeReleaseTrailDuration;

            if (_movement != null) _movement.SuspendHorizontalControl = true;
            if (rootMotionRelay != null) rootMotionRelay.IsActive = true;
            if (_hasSlamParam) animator.SetTrigger(GrabAndSlamHash);
        }

        /// <summary>
        /// 회전이 끝났다 — <b>캐릭터가 새로 바라보는 방향</b>(<see cref="Transform.forward"/>)으로
        /// 던진다. <see cref="ThrowHeldProp"/>은 조준 카메라 방향으로 던지지만, 여기서
        /// 그러면 애써 돈 의미가 없다 — 회전이 만든 그 방향으로 던져야 "돌려서 던졌다"가
        /// 성립한다.
        /// </summary>
        private void FinishSlam()
        {
            IsSlamming = false;
            if (rootMotionRelay != null) rootMotionRelay.IsActive = false;
            if (_movement != null) _movement.SuspendHorizontalControl = false;

            Rigidbody body = _slamBody;
            _slamBody = null;
            if (body == null) return;

            SetIgnorePlayerCollision(body, false);
            body.isKinematic = false;

            body.linearVelocity = _rb.linearVelocity + transform.forward * slamThrowForce;
            body.angularVelocity = RandomSpin();
            MarkThrown(body);

            _ropeTrailBody = body;
            _ropeReleaseUntil = Time.time + ropeReleaseTrailDuration;

            _thrownBody = body;
            _chaseWindowRemaining = chaseWindowDuration;
            if (gun != null) gun.ChaseTarget = body;
        }

        /// <summary>
        /// 슬램 도중 컴포넌트가 꺼지는 등, 끝까지 못 가고 취소될 때만 쓴다. 던지지 않고
        /// 그냥 물리를 되돌려 소품을 놓아준다 — 잡고 있던 진짜 소품이라(스폰한 물병과
        /// 달리) 파괴하면 안 된다.
        /// </summary>
        private void CancelSlam()
        {
            IsSlamming = false;
            if (rootMotionRelay != null) rootMotionRelay.IsActive = false;
            if (_movement != null) _movement.SuspendHorizontalControl = false;

            if (_slamBody != null)
            {
                SetIgnorePlayerCollision(_slamBody, false);
                _slamBody.isKinematic = false;
            }
            _slamBody = null;
        }

        /// <summary>
        /// 던질 때 얹을 무작위 회전 속도. 축과 방향을 매번 다르게 뽑아서, 던질 때마다
        /// 다르게 굴러가며 날아간다 — 축·속도가 항상 같으면 팽이처럼 똑같이 돌아 오히려
        /// 인공적으로 보인다. <see cref="Rigidbody.angularVelocity"/>는 라디안/초 단위라
        /// <see cref="thrownSpinSpeed"/>(도/초)를 여기서 변환한다.
        /// </summary>
        private Vector3 RandomSpin() =>
            Random.insideUnitSphere.normalized * (Random.Range(-thrownSpinSpeed, thrownSpinSpeed) * Mathf.Deg2Rad);

        /// <summary>
        /// 던진 소품의 "맞으면 체이스가 성립하는" 시한을 관리한다. 시간이 다 됐거나
        /// 이미 착지해서 멈췄으면 놓아준다 — 안 그러면 안 쓰는 소품에 조준 보조
        /// (<see cref="GrappleGun.ChaseTarget"/>)가 영원히 붙어 있는 이상한 상태가 된다.
        /// </summary>
        private void UpdateThrownTracking()
        {
            if (_thrownBody == null) return;

            _chaseWindowRemaining -= Time.fixedDeltaTime;
            bool settled = _thrownBody.linearVelocity.magnitude < chaseSettleSpeed;

            if (_chaseWindowRemaining > 0f && !settled) return;

            if (gun != null && gun.ChaseTarget == _thrownBody) gun.ChaseTarget = null;
            _thrownBody = null;
        }

        /// <summary>
        /// 대기 동작을 시작한다 — 스폰한 물병을 잠깐 고정해두고, 총을 숨기고, 던지기
        /// 애니메이션을 재생한다. 실제로 속도가 실리는 건 <see cref="FinishWindup"/>에서다.
        /// </summary>
        private void BeginWindup(Rigidbody body)
        {
            _propBody = body;
            IsWindingUp = true;
            _windupRemaining = throwWindupDelay;

            SetIgnorePlayerCollision(_propBody, true);
            _propBody.linearVelocity = Vector3.zero;
            _propBody.angularVelocity = Vector3.zero;
            _propBody.isKinematic = true;

            if (gun != null) gun.SetVisible(false);

            if (animator != null)
            {
                animator.SetTrigger(ThrowTriggerHash);
                _throwWeightUntil = Time.time + throwLayerHoldTime;
            }
        }

        /// <summary>
        /// 취소한다 — 컴포넌트가 꺼지는 등, 던지기를 끝까지 못 갈 때만 쓴다. 스폰한
        /// 물병이라 되돌려줄 "원래 상태"가 없으니 그대로 없앤다.
        /// </summary>
        private void CancelWindup()
        {
            IsWindingUp = false;
            if (gun != null) gun.SetVisible(true);

            if (_propBody != null) Destroy(_propBody.gameObject);
            _propBody = null;
        }

        /// <summary>
        /// 대기가 끝났다 — 총을 다시 보이게 하고, <b>내 현재 속도 + 조준 방향 × 던지는
        /// 힘</b>을 그대로 대입해 던진다. 정지 상태로 던지면 <see cref="throwForce"/>뿐이라
        /// 약하고, 스윙으로 가속한 채 던지면 그 속도가 고스란히 더해져 세다 —
        /// <see cref="SwingConstraint"/>의 릴리즈 보상과 같은 원칙("관성이 그대로
        /// 타격이 된다")을 던지기에도 그대로 적용한 것뿐이다.
        /// </summary>
        private void FinishWindup()
        {
            IsWindingUp = false;
            if (gun != null) gun.SetVisible(true);

            SetIgnorePlayerCollision(_propBody, false);
            _propBody.isKinematic = false;

            Vector3 aimDirection = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            _propBody.linearVelocity = _rb.linearVelocity + aimDirection * throwForce;
            _propBody.angularVelocity = RandomSpin();

            // 던진 이 소품을 체이스 대상으로 등록한다 — 시한 안에 좌클릭이 맞으면
            // 위 FixedUpdate의 체이스 감지 분기가 잡아챈다. GrappleGun에도 같이 알려서,
            // 이 소품에 한해서는 조준이 후해지게 한다(GrappleGun.ChaseTarget 참고).
            _thrownBody = _propBody;
            _chaseWindowRemaining = chaseWindowDuration;
            if (gun != null) gun.ChaseTarget = _thrownBody;

            _propBody = null;
        }

        /// <summary>
        /// 체이스 대시를 시작한다 — 이 순간부터는 <paramref name="target"/>이 아니라
        /// <b>내가</b> 그쪽으로 끌려간다. 던진 물병에 좌클릭이 다시 맞았을 때도, 잡은
        /// 소품을 놓아 던지는 순간에도 같은 절차를 탄다.
        /// </summary>
        private void BeginChase(Rigidbody target)
        {
            IsChasing = true;
            _chaseTargetBody = target;
            _chaseDeadline = Time.time + chaseTimeout;
            _thrownBody = null;
            if (gun != null) gun.ChaseTarget = null;
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            if (_movement != null && _movement.LogLift)
                Debug.Log($"[띄움] 체이스 대시 <b>시작</b> — 대상 '{target.name}'. " +
                          "그냥 소품을 잡으려 했는데 이게 뜨면, 방금 던진 소품으로 " +
                          "오인한 것이다(_thrownBody).", this);
        }

        /// <summary>
        /// <see cref="GrappleZip"/>과 같은 기법 — 힘이 아니라 속도를 목표 방향으로
        /// 직접 대입한다. 대상이 건물이 아니라 날아가는 소품이라는 것만 다르다.
        /// <see cref="GrappleGun.AnchorPoint"/> 대신 <see cref="_chaseTargetBody"/>를
        /// 직접 쫓는다 — 웹이 이미 풀린 뒤(<see cref="ThrowHeldProp"/>)에도 체이스가
        /// 이어져야 하기 때문이다.
        ///
        /// 목표는 <b>피벗이 아니라 콜라이더 표면(<see cref="Rigidbody.ClosestPointOnBounds"/>)</b>이다.
        /// 물병처럼 작은 소품은 피벗과 표면이 거의 같아 차이가 안 보이지만, 대상이 크면
        /// 피벗까지의 거리로 도착을 재는 순간 <c>chaseArrivalDistance</c>(2m) 안쪽이 이미
        /// 콜라이더 내부인 경우가 생긴다 — 도착 판정이 뜨기도 전에 콜라이더에 물리적으로
        /// 부딪혀 속도 강제 대입과 충돌 반발력이 매 프레임 싸우면서 그 자리에 끼어버리고,
        /// <see cref="PlayerMovement.SuspendHorizontalControl"/>이 계속 켜진 채로 안 풀려
        /// 조작 불능이 된다. 표면 기준으로 재면 대상 크기와 무관하게 일관되게 동작한다.
        /// </summary>
        private void UpdateChase()
        {
            if (_chaseTargetBody == null)
            {
                EndChase(boost: false);
                return;
            }

            // 못 예측한 지형에 끼어 도착 조건을 영영 못 만나는 경우의 안전장치 — 끊을 때도
            // boost는 준다, 그래야 끼인 자리에서 그냥 멈추지 않고 현재 속도 방향으로 빠져나간다.
            if (Time.time >= _chaseDeadline)
            {
                EndChase(boost: true);
                return;
            }

            Vector3 surfacePoint = _chaseTargetBody.ClosestPointOnBounds(_rb.position);
            Vector3 toTarget = surfacePoint - _rb.position;
            if (toTarget.magnitude <= chaseArrivalDistance)
            {
                EndChase(boost: true);
                return;
            }

            // 방향은 목표 쪽으로 꺾되 <b>속력은 벌어온 것을 잇는다.</b> 예전에는 chaseSpeed로
            // 덮어써서, 35m/s로 날다 체이스를 걸면 26으로 느려졌다 — 스윙으로 속도를 번
            // 플레이어가 오히려 손해를 보는 셈이라 이 프로젝트 주제("번다 → 지킨다 → 쓴다")와
            // 정반대였다. chaseSpeed는 이제 하한이고, 상한만 chaseMaxSpeed로 막는다.
            float carried = Mathf.Clamp(_rb.linearVelocity.magnitude,
                                        chaseSpeed, Mathf.Max(chaseSpeed, chaseMaxSpeed));
            _rb.linearVelocity = toTarget.normalized * carried;
            if (_movement != null) _movement.SuspendHorizontalControl = true;
        }

        /// <summary>
        /// 체이스를 끝낸다 — <see cref="GrappleZip.Stop"/>과 같은 릴리즈 원칙이다.
        /// 도착했을 때만(<paramref name="boost"/>) 속도를 새로 쓴다.
        /// </summary>
        private void EndChase(bool boost)
        {
            IsChasing = false;
            _chaseTargetBody = null;
            if (_movement != null) _movement.SuspendHorizontalControl = false;
            if (gun != null) gun.ForceDetach();

            if (!boost) return;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 direction = horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized : transform.forward;
            float speed = Mathf.Max(horizontal.magnitude, chaseExitSpeed);

            _rb.linearVelocity = direction * speed + Vector3.up * chaseExitUpSpeed;

            if (_movement != null && _movement.LogLift)
                Debug.Log($"[띄움] 체이스 대시 도착 부스트 — y속도 {chaseExitUpSpeed:F2} 대입, " +
                          $"높이 {transform.position.y:F2}m", this);
        }
    }
}
