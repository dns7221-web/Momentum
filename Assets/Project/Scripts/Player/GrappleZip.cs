using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 갈고리 견인 — 조준한 지점으로 몸을 끌어당겨 그 자리에서 튀어나간다.
    ///
    /// 스파이더맨 게임의 Point Launch에 해당하고, <b>갈고리 총에는 오히려 거미줄보다
    /// 자연스러운 동작</b>이다. 세키로·배트맨·타이탄폴이 전부 이 방식으로 이동한다.
    ///
    /// 이것이 필요한 이유는 2026-08-01 영상들이 전부 같은 말을 했기 때문이다 —
    /// <b>한 번 지상으로 떨어지면 다시 못 올라온다.</b> 스윙은 앵커가 로프 길이보다 높아야
    /// 성립하는데(그래야 진자 최저점이 지면 위에 남는다) 지상에서 크로스헤어에 닿는 것은
    /// 눈앞의 벽면뿐이고, 점프는 1.8m다. 즉 <b>고도를 버는 수단이 하나도 없었다.</b>
    ///
    /// <b>이것은 "로프 타고 벽 오르기"가 아니다</b>(설계 결정 4-2에서 안 만들기로 한 그것).
    /// 셋이 다르다.
    /// <list type="bullet">
    /// <item><b>지속 조작이 없다</b> — 한 번 누르면 도착까지 자동이다. 줄을 잡고 오르내리는
    ///       구간이 존재하지 않는다</item>
    /// <item><b>도착하면 자동으로 놓는다</b> — 매달려 있을 수 있는 상태가 아예 없다</item>
    /// <item><b>속도가 쌓이지 않는다</b> — 도착 속도를 설계값으로 <b>재설정</b>한다.
    ///       감기가 사고를 냈던 것은 속도가 곱해져 누적됐기 때문인데, 여기는 그 경로가 없다</item>
    /// </list>
    /// 덤으로 천장도 저절로 생긴다. 견인은 앵커보다 높이 갈 수 없으므로
    /// <b>도시에서 가장 높은 건물이 곧 고도 상한</b>이다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrappleZip : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커 판정과 로프 그리기를 빌려 쓴다. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        [Header("입력 (InputSystem_Actions ▸ Player ▸ Zip — 마우스 우클릭)")]
        [SerializeField] private InputActionReference zipAction = null;

        // 추가 생성 — 공중 웹 집 키를 스페이스바로 옮김 (2026-10-12)
        [Tooltip("공중 웹 집 입력. 비워두면 PlayerMovement의 Jump 액션(스페이스바)을 빌려 쓴다.\n" +
                 "점프는 공중에서 누르면 그냥 버려지므로(PlayerMovement) 같은 키를 나눠 써도 겹치지 않는다: " +
                 "땅에서는 점프, 공중에서는 웹 집.")]
        [SerializeField] private InputActionReference airZipAction = null;

        [Header("견인")]
        [Tooltip("끌려가는 속도(m/s). 스윙 최저점 속도보다 빠르게 두면 견인이 " +
                 "'가속 수단'이 되어 스윙보다 견인만 쓰게 된다. 조금 느린 편이 좋다.")]
        [Min(1f)][SerializeField] private float zipSpeed = 22f;

        [Tooltip("앵커에 이만큼 다가가면 도착으로 보고 놓는다. 0에 가까우면 벽에 얼굴을 " +
                 "박고, 너무 크면 건물 근처에도 못 가서 견인한 보람이 없다.")]
        [Min(0.5f)][SerializeField] private float arrivalDistance = 3f;

        [Tooltip("안전장치. 이 시간을 넘으면 도착하지 못했어도 놓는다 — 앵커가 사라지거나 " +
                 "중간에 지형에 걸렸을 때 영원히 끌려가는 상태로 남지 않게 한다.")]
        [Min(0.1f)][SerializeField] private float maxDuration = 2.5f;

        [Tooltip("평평한(옥상 같은) 목표는 내 발보다 이만큼 위에 있어야 견인을 시작한다.\n\n" +
                 "이게 없으면 넓은 옥상 위에서 우클릭할 때마다 6m 넘게 떨어진 같은 바닥을 " +
                 "다시 잡아, 그 자리로 짧게 견인 → 도착 → 릴리즈가 한 번에 일어나며 릴리즈 " +
                 "직후 에어루프 애니메이션이 튀어나온다. 벽면(수직) 목표는 이 제한을 안 받는다 " +
                 "— 다른 건물 벽을 타고 오르는 것은 정상 동작이다.")]
        [SerializeField] private float minClimbHeight = 1f;

        // 추가 생성 (2026-10-17) — 포인트 런치(인섬니악식 우클릭)
        [Header("포인트 런치 — 옥상 모서리로 휙 (PerchPointFinder가 목표를 고른다)")]
        [Tooltip("켜면 우클릭 견인이 크로스헤어가 맞힌 점 대신 PerchPointFinder가 고른 옥상 모서리로 가고, " +
                 "공중에서도 쓸 수 있다. 끄면 예전 견인(맞힌 점, 지상에서만).")]
        [SerializeField] private bool pointLaunch = true;
        [Tooltip("거리와 상관없이 대략 이 시간(초) 안에 도착하게 속도를 정한다. 인섬니악의 '휙' 느낌.")]
        [Min(0.05f)][SerializeField] private float pointZipTime = 0.7f; // 수정 (2026-10-19) — 0.45 → 0.7, 대시가 아니라 "도착"하게
        [Tooltip("포인트 런치 속도 하한/상한(m/s). 가까우면 하한, 멀면 상한에서 잘린다.")]
        [SerializeField] private Vector2 pointZipSpeedRange = new Vector2(18f, 35f); // 수정 (2026-10-19) — 80은 F 대시보다 빨라 대시처럼 보였다
        [Tooltip("포인트 런치 도착 판정 거리(m). 목표가 모서리 바로 위라 일반 견인보다 짧게 둔다.")]
        [Min(0.3f)][SerializeField] private float pointArrivalDistance = 1.5f;
        [Tooltip("도착하면 카메라가 보는 수평 방향으로 나가는 최소 속도(m/s). 이미 더 빠르면 그 속도를 지킨다.")]
        [Min(0f)][SerializeField] private float pointExitSpeed = 20f;
        [Tooltip("도착하면 위로 솟구치는 속도(m/s). 14면 약 7 m 솟아 꼭대기에서 바로 다음 스윙을 걸 수 있다.")]
        [Min(0f)][SerializeField] private float pointExitUpSpeed = 14f;

        // 추가 생성 (2026-10-19) — 스파이더맨처럼 "도착해서 올라서기"
        [Tooltip("켜면 도착하자마자 보는 방향으로 크게 솟구친다(닌자 점프, 10-17 방식). 끄면(기본) 모서리에 올라서서 웅크려 앉는다.")]
        [SerializeField] private bool arrivalLaunch = false;
        [Tooltip("목표까지 이 거리(m) 안으로 들어오면 남은 거리에 비례해 감속한다. 휙 날아가다 사뿐히 도착하는 느낌.")]
        [Min(0f)][SerializeField] private float pointBrakeDistance = 4f;
        [Tooltip("감속해도 이보다 느려지지는 않는다(m/s). 너무 느리면 도착 직전에 멈칫한다.")]
        [Min(0.5f)][SerializeField] private float pointBrakeMinSpeed = 6f;
        [Tooltip("도착해서 모서리 위로 넘어가는 작은 홉 (x = 옥상 쪽, y = 위, m/s).")]
        [SerializeField] private Vector2 perchHop = new Vector2(3f, 3f);

        [Header("도착 — 여기서 속도를 새로 쓴다")]
        [Tooltip("도착 시 최소 수평 속도(m/s). 이미 이보다 빠르면 그 속도를 유지한다 — " +
                 "스윙으로 벌어둔 속도를 견인이 깎으면 '지킨다' 축이 무너진다.")]
        [Min(0f)][SerializeField] private float exitSpeed = 14f;

        [Tooltip("도착 시 위로 얹는 속도(m/s). 고정값인 것이 중요하다 — 비례시키면 " +
                 "견인을 연타할수록 높아져 감기와 같은 폭주가 다시 생긴다.")]
        [Min(0f)][SerializeField] private float exitUpSpeed = 9f;

        // 추가 생성 — 공중 웹 집
        [Header("공중 웹 집 (Web Zip) — 공중에서 스페이스바")]
        [Tooltip("켜면 공중 스페이스바가 스파이더맨식 웹 집이 된다(공중 우클릭은 아무것도 안 한다).\n" +
                 "줄을 쏴서 붙으면 정해진 거리를 직선으로 빠르게 끌려가고, 끝에서 살짝 떠오르며 놓는다.\n" +
                 "끄면 예전처럼 공중 우클릭이 '앵커까지 끌려가기'가 된다. 지상 우클릭(벽 견인·슬링샷)은 그대로다.")]
        [SerializeField] private bool airWebZip = true;

        [Tooltip("줄이 총구에서 앵커까지 날아가는 시간(초). 이 동안 줄 끝이 눈에 보이게 뻗어 나간다. " +
                 "0이면 바로 당긴다 — 손맛(쏘고 → 붙고 → 당긴다)이 사라진다.")]
        [Min(0f)][SerializeField] private float webTravelTime = 0.1f;

        [Tooltip("줄이 날아가는 동안 낙하 속도 상한(m/s). 잠깐 공중에 '걸리는' 느낌(히트스톱)을 준다. " +
                 "크게 두면 그냥 떨어지면서 당긴다.")]
        [Min(0f)][SerializeField] private float fireMaxFallSpeed = 2f;

        // 수정 — 인섬니악 스파이더맨식: 조준한 건물이 없어도 카메라가 보는 쪽으로
        // '정해진 거리를 직선으로 빠르게' 끌려간다. 한 번 튕기고 떨어지는 방식이 아니다.
        [Tooltip("웹 집 한 번에 직선으로 끌려가는 거리(m). 가는 길에 벽이 있으면 그 앞에서 멈춘다.")]
        [Min(1f)][SerializeField] private float airZipDistance = 14f;

        [Tooltip("끌려가는 최소 속도(m/s). 이미 그 방향으로 이보다 빠르면 그 속도를 유지한다 — " +
                 "더하지 않고 하한만 둔다(연타해도 속도가 쌓이지 않는다).")]
        [Min(1f)][SerializeField] private float airZipSpeed = 30f;

        [Tooltip("카메라가 위를 볼 때 웹 집이 올라가는 각도 상한(도). 0이면 항상 수평으로만 간다.")]
        [Range(0f, 45f)][SerializeField] private float maxZipPitchUp = 15f;

        [Tooltip("카메라가 아래를 볼 때 웹 집이 내려가는 각도 상한(도). 크면 땅으로 꽂힌다.")]
        [Range(0f, 45f)][SerializeField] private float maxZipPitchDown = 10f;

        [Tooltip("끌려가기가 끝나는 순간 위로 얹는 속도(m/s). 끝에서 살짝 떠오르며 놓아 " +
                 "바로 다음 스윙·웹 집으로 이어 붙일 시간을 번다.")]
        [Min(0f)][SerializeField] private float zipEndUpSpeed = 4f;

        [Tooltip("줄이 붙을 건물을 찾는 거리(m). 이 안에 건물이 없으면 줄은 허공의 점에 그린다 — " +
                 "웹 집 자체는 건물 없이도 나간다.")]
        [Min(1f)][SerializeField] private float webVisualRange = 25f;

        [Tooltip("한 번 뜬 동안 쓸 수 있는 웹 집 횟수. 땅에 닿거나 스윙에 붙으면 다시 채워진다. " +
                 "0이면 무제한 — 스윙 없이 웹 집만으로 날아다닐 수 있게 된다.")]
        [Min(0)][SerializeField] private int maxAirZips = 2;

        // 추가 생성 — 웹 집 방식 (2026-10-17)
        /// <summary>공중 웹 집이 줄을 어떻게 쓰는지.</summary>
        public enum AirZipStyle
        {
            /// <summary>줄이 붙는 순간 한 번에 튕겨 나가고 줄은 바로 감겨 사라진다(인섬니악식 반동 대시, 기본).</summary>
            Yank,
            /// <summary>예전 방식 — 줄에 매달려 airZipDistance만큼 직선으로 끌려간다(중력 끔).</summary>
            Tow,
        }

        [Header("반동 대시 (Yank) — 줄이 번쩍 나갔다 사라지며 그 반동으로 앞으로")]
        [Tooltip("Yank: 붙는 순간 튕겨 나가고 줄은 사라진다. Tow: 예전처럼 줄을 타고 끌려간다.")]
        [SerializeField] private AirZipStyle airZipStyle = AirZipStyle.Yank;
        [Tooltip("튕겨 나가는 최소 속도(m/s). 그 방향으로 이미 더 빠르면 그 속도를 지킨다.")]
        [Min(0f)][SerializeField] private float yankSpeed = 26f;
        [Tooltip("튕길 때 위로 얹는 속도(m/s). 대시 뒤 바로 떨어지지 않고 살짝 떠오르게.")]
        [Min(0f)][SerializeField] private float yankUpSpeed = 4f;
        [Tooltip("줄이 붙은 점에서 총으로 감겨 들어오는 시간(초). 이 동안만 줄이 보인다.")]
        [Min(0f)][SerializeField] private float yankRetractTime = 0.1f;

        [Header("애니메이션")]
        [Tooltip("Zip 레이어 가중치가 오르내리는 속도(초당). 0이면 즉시 전환된다 — " +
                 "그러면 견인이 끝나는 순간 자세가 뚝 끊겨 튄다.")]
        [Min(0f)][SerializeField] private float animLayerLerpSpeed = 8f;

        /// <summary>견인 중인가. 스윙 쪽이 이 값을 보고 서로 끼어들지 않게 한다.</summary>
        public bool IsZipping { get; private set; }

        // 추가 생성 (2026-10-19)
        /// <summary>지금 우클릭하면 포인트 런치로 가는가(보이는 포인트가 있음). 슬링샷이 우클릭을 양보할지 묻는다.</summary>
        public bool WillPointLaunch => pointLaunch && _perch != null && _perch.HasPoint;

        // 추가 생성 (2026-10-17)
        /// <summary>포인트 런치가 도착해 솟구치는 순간 한 번 울린다. 애니메이션 쪽이 듣는다.</summary>
        public event System.Action PointLaunched;

        // 추가 생성
        /// <summary>
        /// 견인 입력 액션. <see cref="WebSlingshot"/>이 같은 우클릭을 지상/공중으로 나눠 쓰므로,
        /// 슬링샷 쪽 칸이 비어 있으면 이걸 빌려 간다 — 두 칸에 따로 넣어 어긋나는 일을 막는다.
        /// </summary>
        public InputActionReference ZipAction => zipAction;

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private InputActionReference _airZipInput; // 실제로 구독한 공중 웹 집 액션 (빌려 온 Jump일 수 있음)
        private Animator _animator;

        // 추가 생성 — 우클릭을 같이 쓰는 슬링샷. 없으면 예전처럼 견인만 한다.
        private WebSlingshot _slingshot;

        // 추가 생성 (2026-10-17) — 포인트 런치 목표 찾기와 이번 견인 상태
        private PerchPointFinder _perch;
        /// <summary>이번 견인이 포인트 런치인가(옥상 모서리 목표).</summary>
        private bool _pointMode;
        /// <summary>이번 견인의 속도(m/s). 포인트 런치는 거리에 맞춰 시작할 때 정한다.</summary>
        private float _pullSpeed;
        /// <summary>PointLaunch 레이어 인덱스. 컨트롤러에 레이어가 없으면 -1이라 건너뛴다.</summary>
        private int _pointLaunchLayerIndex = -1;
        /// <summary>포인트 런치로 솟구친 뒤 아직 공중인가. 참인 동안 닌자 점프 자세를 보여준다.</summary>
        private bool _pointAirborne;

        // 추가 생성 (2026-10-19) — 퍼치(모서리에 올라서 웅크려 앉기) 상태
        private int _perchLayerIndex = -1;
        private static readonly int PerchStartHash = Animator.StringToHash("PerchStart");
        /// <summary>퍼치 중인가. 참인 동안 Perch 레이어를 보여준다.</summary>
        private bool _perching;
        /// <summary>퍼치 홉 뒤 옥상에 한 번이라도 닿았는가. 닿기 전에는 "공중이라 끝"으로 보지 않는다.</summary>
        private bool _perchLanded;
        private float _perchStartTime;
        private float _perchLandTime; // 처음 옥상에 닿은 시각 — 홉의 남은 속도를 "걷기 시작"으로 오해하지 않게
        private static readonly int PointLaunchHash = Animator.StringToHash("PointLaunch");

        /// <summary>Zip 레이어 인덱스. 클립을 안 채웠으면 -1로 남고 가중치 조작을 건너뛴다.</summary>
        private int _zipLayerIndex = -1;

        /// <summary>끌려갈 목표. 앵커가 움직일 수 있으므로 트랜스폼과 로컬 좌표로 들고 있는다.</summary>
        private Transform _targetTransform;
        private Vector3 _targetLocalPoint;
        private float _elapsed;

        /// <summary>
        /// 붙잡은 면의 법선. 도착했을 때 <b>어느 쪽으로 튀어나갈지</b>를 정한다.
        ///
        /// 이게 없으면 "가던 방향"을 그대로 쓰게 되는데, 견인은 정의상 앵커를 향해 가므로
        /// 그 방향은 곧 <b>벽 안쪽</b>이다. 도착하자마자 벽에 처박힌다.
        /// </summary>
        private Vector3 _surfaceNormal = Vector3.forward;

        // 추가 생성 — 공중 웹 집 상태
        /// <summary>이번 견인이 공중 웹 집인가. 거짓이면 예전 견인(앵커까지 끌려가기)이다.</summary>
        private bool _airMode;
        /// <summary>줄이 붙어 이미 튀어나갔는가. 그 뒤로는 줄만 잠깐 보여주고 끝낸다.</summary>
        private bool _launched;
        /// <summary>웹 집 방향(단위 벡터). 시작할 때 카메라 방향에서 한 번 정하고 끝까지 바꾸지 않는다.</summary>
        private Vector3 _zipDir;
        /// <summary>이번 웹 집이 갈 거리(m). 앞에 벽이 있으면 airZipDistance보다 짧아진다.</summary>
        private float _zipTravel;
        /// <summary>지금까지 간 거리(m).</summary>
        private float _zipTraveled;
        /// <summary>끌려가는 속도(m/s). 줄이 붙는 순간 한 번 정한다.</summary>
        private float _zipSpeed;
        /// <summary>줄 끝이 그려질 월드 좌표. 건물에 붙거나, 없으면 허공의 한 점이다.</summary>
        private Vector3 _airAnchor;
        /// <summary>끝나는 곳에 벽이 있으면 그 법선, 허공이면 zero. 끝날 때 벽을 따라 미끄러질지 정한다.</summary>
        private Vector3 _zipWallNormal;
        /// <summary>이번 공중 구간에서 쓴 웹 집 횟수.</summary>
        private int _airZipsUsed;
        // 추가 생성 — 반동 대시 줄 회수 단계 (2026-10-17)
        private bool _retracting;
        private float _retractElapsed;

        private Vector3 TargetPoint =>
            _targetTransform != null
                ? _targetTransform.TransformPoint(_targetLocalPoint)
                : transform.position;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            // 스킨 메시는 자식으로 붙는다 — 캡슐 자체엔 Animator가 없다.
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null) _zipLayerIndex = _animator.GetLayerIndex("Zip");
            if (_animator != null) _pointLaunchLayerIndex = _animator.GetLayerIndex("PointLaunch"); // 추가 생성 (2026-10-17)
            if (_animator != null) _perchLayerIndex = _animator.GetLayerIndex("Perch"); // 추가 생성 (2026-10-19)
            if (gun == null) gun = GetComponent<GrappleGun>();
            _slingshot = GetComponent<WebSlingshot>();   // 추가 생성
            // 추가 생성 (2026-10-17) — 다른 자동 부품처럼 없으면 실행 중에 붙인다.
            if (pointLaunch)
            {
                // ?? 대신 == null로 검사한다 — 유니티 오브젝트는 에디터에서 "가짜 null"을 돌려줄 수 있어 ??가 속는다.
                _perch = GetComponent<PerchPointFinder>();
                if (_perch == null) _perch = gameObject.AddComponent<PerchPointFinder>();
            }

            if (gun == null)
                Debug.LogWarning("[GrappleZip] GrappleGun이 없어 앵커를 찾을 수 없다.", this);
        }

        private void OnEnable()
        {
            if (zipAction != null)
            {
                zipAction.action.Enable();
                zipAction.action.performed += OnZipPerformed;
            }

            // 추가 생성 — 공중 웹 집(스페이스바). 칸이 비어 있으면 점프 액션을 빌린다.
            // 같은 오브젝트의 Awake가 먼저 끝난 뒤 OnEnable이 불리므로 _movement는 이미 채워져 있다.
            _airZipInput = airZipAction != null ? airZipAction
                         : _movement != null ? _movement.JumpAction : null;
            if (_airZipInput != null)
            {
                _airZipInput.action.Enable(); // 이미 켜져 있으면 아무 일도 안 한다
                _airZipInput.action.performed += OnAirZipPerformed;
            }
        }

        private void OnDisable()
        {
            if (zipAction != null)
            {
                zipAction.action.performed -= OnZipPerformed;
                zipAction.action.Disable();
            }

            // 구독만 푼다. Disable은 하지 않는다 — 빌려 온 Jump 액션이면 점프까지 꺼져 버린다.
            // 액션을 켜고 끄는 것은 그 액션의 주인(PlayerMovement)이 한다.
            if (_airZipInput != null)
            {
                _airZipInput.action.performed -= OnAirZipPerformed;
                _airZipInput = null;
            }

            // 견인 중에 꺼지면 이동 쪽 스위치가 켜진 채로 남아 조작이 영영 안 먹는다.
            if (IsZipping) Stop(arrived: false);
        }

        private void OnZipPerformed(InputAction.CallbackContext ctx) => TryStart();

        // 추가 생성 — 스페이스바. 땅에서 누르면 점프(PlayerMovement)이므로 여기선 공중일 때만 반응한다.
        private void OnAirZipPerformed(InputAction.CallbackContext ctx)
        {
            if (!airWebZip || _movement == null || _movement.IsGrounded) return;
            // 견인 중이거나 스윙 중이면 무시 — TryStart와 같은 규칙(로프와 견인이 속도를 서로 덮어쓰지 않게).
            if (IsZipping || gun == null || gun.IsAttached) return;
            TryStartAirZip();
        }

        /// <summary>
        /// 견인을 시작한다. 스윙 중이면 무시한다.
        ///
        /// 둘을 겹치게 두면 로프 구속과 견인이 같은 속도를 서로 덮어써서, 화면에서는
        /// 그냥 "가끔 이상하게 튄다"로 보인다. 한쪽만 쥐게 하면 그 애매함이 생기지 않는다.
        /// </summary>
        private void TryStart()
        {
            if (IsZipping || gun == null) return;

            // 추가 생성 (2026-10-18) — 스윙 중 우클릭: 포인트가 보이면 줄을 놓고 바로 포인트 런치(스파이더맨처럼).
            // ReleaseByJump는 R을 쥐고 있어도 재시도 루프가 다시 붙이지 않게 "쥐고 있음"까지 지운다.
            if (gun.IsAttached)
            {
                if (gun.IsPropAnchor || _perch == null || !pointLaunch || !_perch.HasPoint) return;
                gun.ReleaseByJump();
                TryStartPointLaunch();
                return;
            }

            // 수정 (2026-10-19) — 포인트가 보이면 땅에서도 공중과 똑같이 포인트 런치가 먼저다
            // (슬링샷 옥상 오르기는 WebSlingshot.CanBegin에서 이 경우 스스로 양보한다).
            // 추가 생성 — 슬링샷이 이번 우클릭을 가져가면 양보한다(발이 땅에 있고 붙은 건물
            // 옥상이 훨씬 높을 때). 둘 다 같은 액션을 구독하므로 누가 먼저 불릴지 모르는데,
            // ClaimsPress()는 어느 순서로 불려도 같은 답을 준다(WebSlingshot 주석 참고).
            if (_slingshot != null && _slingshot.ClaimsPress()) return;

            // 수정 (2026-10-12) — 공중 웹 집은 스페이스바(OnAirZipPerformed)로 옮겼다.
            // 공중 우클릭은 아무것도 안 한다. 여기서 막지 않으면 아래 지상용 견인이 공중에서 나간다.
            // 수정 (2026-10-17) — 포인트 런치는 인섬니악처럼 공중에서도 된다.
            if (_perch != null && pointLaunch)
            {
                TryStartPointLaunch();
                return;
            }
            if (airWebZip && _movement != null && !_movement.IsGrounded) return;
            // 소품은 견인 목표에서 뺀다 — 옥상 위에서 우클릭했을 때 바로 옆 드럼통으로
            // 순간 이동해버리는 사고를 막는다. 견인은 건물끼리 옮겨다니는 동작이다.
            //
            // 수정: requireClearHang: false — 생 벽면의 진자 최저점 검사를 끈다. 견인은
            // 매달리지 않으므로 그 검사가 의미가 없는데, 켜두면 벽면 목표가 전부 걸러져
            // "지상에 떨어지면 못 올라온다"가 다시 생긴다(이 컴포넌트가 존재하는 이유 그 자체).
            // 지상 → 벽 견인 → 벽에서 튕겨 나오며 솟음 → 옥상이 사거리에 들어옴 → 스윙/견인.
            if (!gun.TryFindAnchor(out RaycastHit hit, includeProps: false, requireClearHang: false)) return;

            // 평평한 면인데 내 발보다 안 높으면 거부한다 — "같은 옥상 바닥을 다시 잡는" 것을
            // 걸러낸다. 벽면(normal.y가 낮음)은 그대로 통과시킨다 — 다른 건물 벽을 타고
            // 오르는 것은 막을 이유가 없다.
            // 추가 생성 — 공중이면 웹 집. 높이 제한(minClimbHeight)은 보지 않는다:
            // 웹 집은 "앵커로 올라가기"가 아니라 "앞으로 나가기"라 앵커 높이가 상관없다.
            bool flatSurface = hit.normal.y >= gun.RoofNormalMinY;
            if (flatSurface && hit.point.y < transform.position.y + minClimbHeight) return;
            _airMode = false;
            _launched = false;
            _pointMode = false;     // 추가 생성 (2026-10-17)
            _pullSpeed = zipSpeed;  // 추가 생성 (2026-10-17)

            _targetTransform = hit.collider.transform;
            _targetLocalPoint = _targetTransform.InverseTransformPoint(hit.point);
            _surfaceNormal = hit.normal;
            _elapsed = 0f;
            IsZipping = true;

            // 지상 이동이 매 스텝 수평 속도를 대입하므로, 막지 않으면 견인 속도가
            // 곧바로 지워진다. 스윙이 쓰는 것과 같은 스위치다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            // FixedUpdate를 기다리지 않고 여기서 바로 넘긴다. 우클릭 입력은 Update
            // 타이밍에 처리되는데 그리기 목표는 지금까지 FixedUpdate에서만 넘겼다 —
            // 그 사이 공백 프레임 동안은 IsAttached도 OverrideRopeTarget도 없어 로프가
            // 안 그려진다. 건물벽처럼 가까운 목표는 견인 자체가 아주 짧게 끝나버려서,
            // 그 공백 때문에 로프가 뜰 새도 없이 견인이 끝나 "거미줄이 아예 안 보인다"로
            // 보였다.
            if (gun != null) gun.OverrideRopeTarget = TargetPoint;
        }

        // 추가 생성 (2026-10-17)
        /// <summary>
        /// 포인트 런치 시작. <see cref="PerchPointFinder"/>가 지금 보여주는 옥상 모서리로 끌려간다.
        /// 속도는 거리 / <see cref="pointZipTime"/> — 멀든 가깝든 "휙" 하는 시간이 비슷하게 느껴지게.
        /// </summary>
        private void TryStartPointLaunch()
        {
            if (!_perch.HasPoint || _perch.PointCollider == null) return;

            _airMode = false;
            _launched = false;
            _pointMode = true;

            _targetTransform = _perch.PointCollider.transform;
            _targetLocalPoint = _targetTransform.InverseTransformPoint(_perch.ApproachPoint);
            // 도착해서 튀는 방향의 기준. 모서리면 벽 법선, 옥상이면 위.
            _surfaceNormal = _perch.EdgeNormal.sqrMagnitude > 0f ? _perch.EdgeNormal : Vector3.up;
            _elapsed = 0f;

            float distance = Vector3.Distance(_rb.position, _perch.ApproachPoint);
            _pullSpeed = Mathf.Clamp(distance / pointZipTime, pointZipSpeedRange.x, pointZipSpeedRange.y);

            IsZipping = true;
            if (_movement != null) _movement.SuspendHorizontalControl = true;
            gun.OverrideRopeTarget = TargetPoint;
        }

        /// <summary>줄이 나가는 위치. 총구가 비어 있으면 몸 중심을 쓴다.</summary>
        private Vector3 MuzzlePosition => gun.Muzzle != null ? gun.Muzzle.position : _rb.position;

        /// <summary>
        /// 견인 자세를 켜고 끈다.
        ///
        /// <c>IsZipping</c>으로 통째로 리턴하지 않고 매 프레임 도는 이유는 스윙 레이어와
        /// 같다 — 끝나는 순간 가중치가 뚝 0으로 끊기면 자세가 튀므로, 끝난 뒤에도 계속
        /// 돌면서 서서히 내려야 한다.
        /// </summary>
        private void Update()
        {
            UpdatePointLaunchLayer(); // 추가 생성 (2026-10-17)
            if (_animator == null || _zipLayerIndex < 0) return;

            float target = IsZipping ? 1f : 0f;
            float current = _animator.GetLayerWeight(_zipLayerIndex);
            float next = animLayerLerpSpeed <= 0f
                ? target
                : Mathf.MoveTowards(current, target, animLayerLerpSpeed * Time.deltaTime);
            _animator.SetLayerWeight(_zipLayerIndex, next);
        }

        // 추가 생성 (2026-10-17)
        /// <summary>
        /// 닌자 점프 자세(PointLaunch 레이어)를 켜고 끈다. 솟구친 뒤 착지하거나, R로 스윙에 붙거나,
        /// 다시 견인을 시작하면 끝난 것으로 보고 서서히 끈다(AnimatorLayerFade — 뚝 끊기면 자세가 튄다).
        /// </summary>
        private void UpdatePointLaunchLayer()
        {
            if (_pointAirborne &&
                ((_movement != null && _movement.IsGrounded) || (gun != null && gun.IsAttached) || IsZipping))
                _pointAirborne = false;

            AnimatorLayerFade.To(_animator, _pointLaunchLayerIndex, _pointAirborne ? 1f : 0f, animLayerLerpSpeed);
            UpdatePerch(); // 추가 생성 (2026-10-19)
        }

        // 추가 생성 (2026-10-19)
        /// <summary>
        /// 퍼치를 언제 끝낼지 정하고 Perch 레이어 가중치를 옮긴다.
        /// 끝나는 조건: 옥상에 내려앉은 뒤 걷기 시작(수평 속도 1 m/s 초과)하거나, 다시 떠오르거나(점프·낙하),
        /// 스윙에 붙거나, 다시 견인을 시작할 때. 이동 키를 직접 읽지 않고 결과(속도·접지)로 판단해서
        /// 입력 장치가 무엇이든 같은 규칙이 된다.
        /// </summary>
        private void UpdatePerch()
        {
            if (_perching)
            {
                bool grounded = _movement != null && _movement.IsGrounded;
                if (grounded && !_perchLanded) { _perchLanded = true; _perchLandTime = Time.time; }

                Vector3 v = _rb.linearVelocity;
                float flatSpeed = new Vector2(v.x, v.z).magnitude;
                // 내려앉은 직후 0.3초는 홉의 남은 수평 속도가 남아 있으므로 걷기로 보지 않는다.
                bool moved = _perchLanded && grounded && flatSpeed > 1f && Time.time - _perchLandTime > 0.3f;
                bool leftLedge = _perchLanded && !grounded;
                bool timedOut = !_perchLanded && Time.time - _perchStartTime > 1.5f; // 옥상에 못 닿고 떨어진 경우
                if (moved || leftLedge || timedOut || IsZipping || (gun != null && gun.IsAttached))
                    _perching = false;
            }
            AnimatorLayerFade.To(_animator, _perchLayerIndex, _perching ? 1f : 0f, animLayerLerpSpeed);
        }

        private void FixedUpdate()
        {
            // 추가 생성 — 땅에 닿거나 스윙에 붙으면 웹 집 횟수를 다시 채운다.
            if ((_movement != null && _movement.IsGrounded) || (gun != null && gun.IsAttached))
                _airZipsUsed = 0;

            if (!IsZipping) return;

            _elapsed += Time.fixedDeltaTime;

            // 추가 생성 — 공중 웹 집은 흐름이 달라 따로 돈다.
            if (_airMode)
            {
                AirZipStep();
                return;
            }

            // 스윙이 시작되면 견인을 접는다. 둘 다 같은 속도를 매 스텝 덮어쓰므로 겹치면
            // 화면에서는 "가끔 이상하게 튄다"로만 보여 원인을 못 가린다. 취소당하는 쪽을
            // 견인으로 정한 것은, 줄을 걸어 타는 것이 이 게임의 본체이기 때문이다.
            if (gun.IsAttached || _targetTransform == null || _elapsed >= maxDuration)
            {
                Stop(arrived: false);
                return;
            }

            Vector3 toTarget = TargetPoint - _rb.position;
            if (toTarget.magnitude <= (_pointMode ? pointArrivalDistance : arrivalDistance)) // 수정 (2026-10-17)
            {
                Stop(arrived: true);
                return;
            }

            // 힘이 아니라 속도를 <b>대입</b>한다. 힘으로 끌면 중력과 겨루느라 궤적이
            // 처져서 어디에 도착할지 예측이 안 되고, 긴 견인일수록 그 오차가 커진다.
            // 속도 대입은 물리 엔진의 충돌 처리를 그대로 통과하므로(위치를 직접 옮기는
            // 것과 다르다) 도중에 벽을 만나면 정상적으로 막힌다.
            // 수정 (2026-10-19) — 포인트 런치는 목표 가까이서 감속한다(대시가 아니라 "도착"하게).
            float pull = _pullSpeed;
            if (_pointMode && pointBrakeDistance > 0f && toTarget.magnitude < pointBrakeDistance)
                pull = Mathf.Max(pointBrakeMinSpeed, _pullSpeed * toTarget.magnitude / pointBrakeDistance);
            _rb.linearVelocity = toTarget.normalized * pull;

            // 스윙과 같은 이유로 매 스텝 다시 켠다 — 스위치를 나눠 쓰는 쪽이 둘이므로,
            // 한 번만 켜두면 상대가 끄고 간 뒤에 회복할 방법이 없다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            // 줄을 그릴 목표도 여기서 넘긴다. LateUpdate에서 넘기면 GrappleGun의
            // LateUpdate와 실행 순서가 정해져 있지 않아, 어느 쪽이 먼저 도느냐에 따라
            // 줄이 한 프레임씩 늦거나 첫 프레임에 안 보인다.
            gun.OverrideRopeTarget = TargetPoint;
        }

        // 수정 — 인섬니악 스파이더맨식 웹 집
        /// <summary>
        /// 공중 웹 집을 시작한다. 크로스헤어에 건물이 걸리면 그 앵커 쪽으로(지금 기능 유지),
        /// 안 걸리면 카메라가 보는 쪽으로 간다 — 조준을 놓쳐도 우클릭이 헛돌지 않는다.
        ///
        /// <b>방향</b>: 앵커 쪽 또는 카메라가 보는 쪽. 위아래 각만 <see cref="maxZipPitchUp"/>/<see cref="maxZipPitchDown"/>로
        /// 자른다 — 하늘을 보면 로켓처럼 솟고 땅을 보면 처박히므로 "앞으로" 나가는 범위로 접는다.
        /// <b>거리</b>: <see cref="airZipDistance"/>. 가는 길에 벽이 있으면 <c>Rigidbody.SweepTest</c>로
        /// 미리 재서 그 앞에서 멈춘다(캡슐 모양 그대로 쓸어보는 유니티 내장 기능).
        /// <b>줄</b>: 앞쪽 위로 레이를 쏴서 건물이 맞으면 거기에, 없으면 허공의 한 점에 그린다.
        /// 줄은 보여주기용이고 이동은 줄과 상관없이 정해진 직선을 간다.
        /// </summary>
        private void TryStartAirZip()
        {
            if (maxAirZips > 0 && _airZipsUsed >= maxAirZips) return;

            // 지금 기능 유지 — 크로스헤어에 건물이 걸리면 예전처럼 그 앵커 쪽으로 간다.
            // 걸린 게 없을 때만 카메라가 보는 쪽으로 간다(인섬니악 방식에서 가져온 부분).
            bool aimed = gun.TryFindAnchor(out RaycastHit aimHit, includeProps: false, requireClearHang: false);
            Camera cam = gun.AimCamera;
            Vector3 look = aimed ? (aimHit.point - _rb.position).normalized
                         : cam != null ? cam.transform.forward : transform.forward;

            // 수평 방향 + 제한된 위아래 각으로 단위 벡터를 다시 만든다.
            Vector3 flat = new Vector3(look.x, 0f, look.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;   // 바로 위/아래를 볼 때 대비
            flat.y = 0f;
            flat.Normalize();
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg,
                                      -maxZipPitchDown, maxZipPitchUp) * Mathf.Deg2Rad;
            Vector3 dir = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);

            // 가는 길에 벽이 있으면 그 앞(0.5m)에서 멈춘다. 너무 가까우면 쓰지 않는다(횟수도 안 깎음).
            float travel = aimed ? Mathf.Min(airZipDistance, Vector3.Distance(_rb.position, aimHit.point))
                                 : airZipDistance;
            _zipWallNormal = Vector3.zero;
            if (_rb.SweepTest(dir, out RaycastHit block, travel, QueryTriggerInteraction.Ignore))
            {
                travel = Mathf.Max(0f, block.distance - 0.5f);
                _zipWallNormal = block.normal;
            }
            if (travel < 1.5f) return;

            _airZipsUsed++;
            _zipDir = dir;
            _zipTravel = travel;
            _zipTraveled = 0f;
            _airAnchor = aimed ? aimHit.point : FindWebVisualPoint(dir);

            _airMode = true;
            _launched = false;
            _elapsed = 0f;
            IsZipping = true;

            // 직선으로 끌려가는 동안은 중력을 끈다. 켜두면 끝에 갈수록 처져서 "끌려간다"가
            // 아니라 "던져졌다"로 보인다. Stop에서 반드시 다시 켠다.
            _rb.useGravity = false;
            if (_movement != null) _movement.SuspendHorizontalControl = true;
            gun.OverrideRopeTarget = MuzzlePosition;   // 줄이 총구에서 출발해 뻗어 나가는 것부터 보여준다
        }

        /// <summary>
        /// 줄을 그릴 점을 찾는다. 웹 집 방향에서 조금 위(약 20°)로 레이를 쏴 건물에 맞으면 그 점,
        /// 없으면 같은 방향 <see cref="webVisualRange"/>의 70% 지점이다.
        /// </summary>
        private Vector3 FindWebVisualPoint(Vector3 dir)
        {
            Vector3 from = MuzzlePosition;
            Vector3 ray = (dir + Vector3.up * 0.35f).normalized;
            if (Physics.Raycast(from, ray, out RaycastHit hit, webVisualRange, gun.AnchorMask,
                                QueryTriggerInteraction.Ignore))
                return hit.point;
            return from + ray * (webVisualRange * 0.7f);
        }

        /// <summary>
        /// 공중 웹 집 한 스텝. 두 단계로 진행된다.
        /// <list type="number">
        /// <item><b>발사</b> — 줄 끝이 총구에서 붙을 점까지 <see cref="webTravelTime"/> 동안 뻗어 나간다.
        ///       이 동안 낙하 속도를 묶어 공중에 잠깐 걸리는 느낌을 준다.</item>
        /// <item><b>끌려가기</b> — 정한 방향으로 <b>매 스텝 같은 속도를 대입</b>해 직선으로 간다.
        ///       정해진 거리를 다 가거나 벽에 막히면 <see cref="EndAirZip"/>.</item>
        /// </list>
        /// 예전 방식(속도를 한 번 주고 줄을 끊음)은 바로 중력에 처져서 "대시"가 아니라
        /// "튕김"으로 보였다. 매 스텝 대입하면 끝날 때까지 직선이 유지된다.
        /// 스윙이 붙으면 즉시 양보하는 것은 견인과 같다.
        /// </summary>
        private void AirZipStep()
        {
            if (gun.IsAttached)
            {
                Stop(arrived: false);
                return;
            }

            if (!_launched)
            {
                // 1) 발사 — 줄 끝을 총구 → 붙을 점으로 보간해 날아가는 것처럼 그린다.
                float t = webTravelTime <= 0f ? 1f : Mathf.Clamp01(_elapsed / webTravelTime);
                gun.OverrideRopeTarget = Vector3.Lerp(MuzzlePosition, _airAnchor, t);

                // 떨어지는 속도만 묶는다. 올라가는 중이면 건드리지 않는다.
                Vector3 v = _rb.linearVelocity;
                if (v.y < -fireMaxFallSpeed)
                {
                    v.y = -fireMaxFallSpeed;
                    _rb.linearVelocity = v;
                }

                if (t < 1f) return;

                // 줄이 붙는 순간 속도를 정한다. 이미 그 방향으로 더 빠르면 그 속도를 지킨다.
                _launched = true;
                float carried = Mathf.Max(0f, Vector3.Dot(_rb.linearVelocity, _zipDir));
                _zipSpeed = Mathf.Max(carried, airZipSpeed);

                if (_movement != null && _movement.LogLift)
                    Debug.Log($"[띄움] 웹 집 — 속도 {_zipSpeed:F1}m/s, 거리 {_zipTravel:F1}m, " +
                              $"남은 횟수 {(maxAirZips > 0 ? maxAirZips - _airZipsUsed : -1)}", this);

                // 추가 생성 (2026-10-17) — 반동 대시(Yank): 끌려가지 않고 줄이 붙는 순간 한 번에 튕겨 나간다.
                // 그 방향으로 이미 더 빠르면 그 속도를 지키고(carried), 위로 살짝 얹는다. 중력은 바로 켠다 —
                // 대시 뒤 자연스러운 포물선이 "줄을 당긴 반동"으로 읽힌다. 줄은 아래 회수 단계에서 총으로 감겨 사라진다.
                if (airZipStyle == AirZipStyle.Yank)
                {
                    float speed = Mathf.Max(carried, yankSpeed);
                    _rb.linearVelocity = _zipDir * speed + Vector3.up * yankUpSpeed;
                    _rb.useGravity = true;
                    _retracting = true;
                    _retractElapsed = 0f;
                }
            }

            // 추가 생성 (2026-10-17) — 회수: 줄 끝이 붙은 점에서 총구로 감겨 들어오고, 다 들어오면 끝낸다.
            if (_retracting)
            {
                _retractElapsed += Time.fixedDeltaTime;
                float r = yankRetractTime <= 0f ? 1f : Mathf.Clamp01(_retractElapsed / yankRetractTime);
                gun.OverrideRopeTarget = Vector3.Lerp(_airAnchor, MuzzlePosition, r);
                if (r >= 1f)
                {
                    _retracting = false;
                    Stop(arrived: false);
                }
                return;
            }

            // 2) 끌려가기 — 줄은 붙은 점에 고정된 채 몸만 직선으로 간다.
            gun.OverrideRopeTarget = _airAnchor;

            float step = _zipSpeed * Time.fixedDeltaTime;
            float remaining = _zipTravel - _zipTraveled;
            // 이번 스텝에 갈 길이 막혔는지도 매번 본다 — 출발할 때 없던 것(움직이는 소품 등) 대비.
            if (remaining <= 0f)
            {
                EndAirZip(_zipWallNormal);
                return;
            }
            if (_rb.SweepTest(_zipDir, out RaycastHit stepHit, Mathf.Min(step, remaining) + 0.1f,
                              QueryTriggerInteraction.Ignore))
            {
                EndAirZip(stepHit.normal);
                return;
            }

            _rb.linearVelocity = _zipDir * _zipSpeed;
            _zipTraveled += step;
            if (_movement != null) _movement.SuspendHorizontalControl = true;
        }

        /// <summary>
        /// 웹 집을 끝낸다. 끌려가던 속도는 그대로 두고 위로 <see cref="zipEndUpSpeed"/>만 얹는다 —
        /// 속도를 지켜야 웹 집 → 스윙 → 웹 집으로 이어 붙일 때 속도감이 끊기지 않는다.
        ///
        /// <paramref name="wallNormal"/>이 있으면(벽 앞에서 끝남) 벽 안쪽 성분을 지워 <b>벽을 따라
        /// 미끄러지게</b> 한다(<c>Vector3.ProjectOnPlane</c>). 정면으로 박아서 남는 속도가 적으면
        /// 지상 견인 도착과 같은 규칙으로 벽을 차고 나온다(<see cref="exitSpeed"/>).
        /// </summary>
        private void EndAirZip(Vector3 wallNormal)
        {
            Vector3 v = _zipDir * _zipSpeed;
            if (wallNormal != Vector3.zero)
            {
                Vector3 away = new Vector3(wallNormal.x, 0f, wallNormal.z);
                if (away.sqrMagnitude > 0.01f)
                {
                    away.Normalize();
                    v = Vector3.ProjectOnPlane(v, away);
                    if (new Vector3(v.x, 0f, v.z).magnitude < exitSpeed) v += away * exitSpeed;
                }
                v.y = Mathf.Max(v.y, 0f);   // 벽에서 끝날 때 아래로 꽂히지 않게
            }
            _rb.linearVelocity = v + Vector3.up * zipEndUpSpeed;
            Stop(arrived: false);
        }

        /// <summary>
        /// 견인을 끝낸다. <paramref name="arrived"/>가 참일 때만 도착 속도를 준다.
        ///
        /// <b>속도를 더하지 않고 새로 쓴다.</b> 이 한 줄이 견인을 안전하게 만든다 —
        /// 더하는 방식이면 연타할 때마다 쌓여 감기가 냈던 사고를 그대로 재현한다.
        /// 수평만 하한으로 두는 것은 스윙으로 벌어둔 속도를 깎지 않기 위해서다.
        /// </summary>
        private void Stop(bool arrived)
        {
            IsZipping = false;
            _retracting = false; // 추가 생성 — 반동 대시 회수 단계도 끝낸다
            if (_airMode) _rb.useGravity = true;   // 수정 — 웹 집 동안 꺼둔 중력을 되살린다
            _airMode = false;       // 추가 생성
            _launched = false;      // 추가 생성
            bool wasPoint = _pointMode; // 추가 생성 (2026-10-17)
            _pointMode = false;
            _targetTransform = null;
            if (gun != null) gun.OverrideRopeTarget = null;
            if (_movement != null) _movement.SuspendHorizontalControl = false;

            if (!arrived) return;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            // 방향은 <b>벽에서 멀어지는 쪽</b>이다. 가던 방향을 그대로 쓰면 그건 앵커 쪽,
            // 즉 벽 안쪽이라 도착하자마자 건물에 처박힌다. 벽을 차고 나가는 그림이 되어야
            // 그다음에 줄을 걸 자리가 보인다.
            //
            // 옥상처럼 위를 보는 면이면 수평 성분이 0이므로, 그때는 가던 방향을 쓴다 —
            // 아래에서 올라온 경우라 벽을 뚫을 걱정이 없다.
            // 추가 생성 (2026-10-17) — 포인트 런치 도착: 스파이더맨 2처럼 <b>카메라가 보는 수평 방향으로
            // 크게 솟구친다</b>. 점프 입력은 받지 않는다(거미 능력이 없는 캐릭터라 자동으로 튄다).
            // 벽 바깥으로 튀는 아래 일반 견인 규칙을 쓰면 왔던 길로 도로 튕기므로 따로 처리한다.
            // 추가 생성 (2026-10-19) — 기본은 스파이더맨처럼 모서리에 올라서서 웅크려 앉는다.
            if (wasPoint && !arrivalLaunch)
            {
                // 모서리를 넘어 옥상 쪽(벽 법선 반대)으로 작게 홉. 옥상 자체를 목표로 했으면(법선이 위) 제자리.
                Vector3 onto = new Vector3(-_surfaceNormal.x, 0f, -_surfaceNormal.z);
                if (onto.sqrMagnitude > 0.01f) onto.Normalize(); else onto = Vector3.zero;
                _rb.linearVelocity = onto * perchHop.x + Vector3.up * perchHop.y;

                _perching = true;
                _perchLanded = false;
                _perchStartTime = Time.time;
                if (_animator != null && _perchLayerIndex >= 0)
                {
                    _animator.SetTrigger(PerchStartHash);
                    _animator.SetLayerWeight(_perchLayerIndex, 1f); // 내려앉는 첫 순간부터 보여야 한다
                }
                return;
            }

            if (wasPoint)
            {
                Vector3 look = gun != null && gun.AimCamera != null
                    ? Vector3.ProjectOnPlane(gun.AimCamera.transform.forward, Vector3.up)
                    : Vector3.zero;
                if (look.sqrMagnitude < 0.0001f) look = transform.forward;
                float launchSpeed = Mathf.Max(horizontal.magnitude, pointExitSpeed); // 벌어둔 속도는 안 깎는다
                _rb.linearVelocity = look.normalized * launchSpeed + Vector3.up * pointExitUpSpeed;
                // 닌자 점프 자세. 레이어는 바로 1로 켠다 — 솟구치는 첫 순간부터 보여야 한다.
                if (_animator != null && _pointLaunchLayerIndex >= 0)
                {
                    _animator.SetTrigger(PointLaunchHash);
                    _animator.SetLayerWeight(_pointLaunchLayerIndex, 1f);
                }
                _pointAirborne = true;
                PointLaunched?.Invoke(); // 다른 연출(카메라 등)이 들을 수 있게 남겨둔다
                return;
            }

            Vector3 away = new Vector3(_surfaceNormal.x, 0f, _surfaceNormal.z);
            Vector3 direction =
                away.sqrMagnitude > 0.01f ? away.normalized
                : horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized
                : transform.forward;

            float speed = Mathf.Max(horizontal.magnitude, exitSpeed);

            _rb.linearVelocity = direction * speed + Vector3.up * exitUpSpeed;

            if (_movement != null && _movement.LogLift)
                Debug.Log($"[띄움] 견인(zip) 도착 부스트 — y속도 {exitUpSpeed:F2} 대입, " +
                          $"높이 {transform.position.y:F2}m", this);
        }
    }
}
