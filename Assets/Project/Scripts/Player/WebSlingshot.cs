using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 웹 슬링샷 — 지상에서 우클릭으로 벽에 줄을 붙이고 권총을 양손으로 겨눈 채,
    /// 마우스 휠을 몸 쪽으로 굴려 줄을 감았다가 놓으면 감은 만큼의 속력으로 옥상 너머까지 튕겨 오른다.
    ///
    /// <b>흐름</b>: 우클릭 누름(부착·조준) → 휠(충전, 허리가 뒤로 젖혀짐) → 우클릭 놓음(발사)
    /// → 꼭짓점 → 착지. 착지 동작은 따로 만들지 않았다 — 비행이 끝날 때 공중 조작 스위치를
    /// 돌려주면 PlayerMovement가 이를 "스윙을 놓은 것"으로 보고 스윙 착지(swingLand)를 그대로 튼다.
    ///
    /// <b>왜 필요한가.</b> 새 도시(건물 34m 이상, 사거리 22m)에서는 지상에 떨어지면
    /// 옥상이 사거리·최대 앙각 밖이라 스윙이 아예 안 걸린다. 견인(<see cref="GrappleZip"/>)은
    /// 벽 중턱까지만 데려다주고 벽 반대쪽으로 튕기므로 옥상에 못 올라선다. 지상은 사실상
    /// 탈출구 없는 바닥이었다.
    ///
    /// <b>물리 — README 표의 "번다" 칸이다.</b>
    /// <list type="bullet">
    /// <item><b>저장</b>: 석궁의 시위를 감는 것과 같다. 휠 한 칸이 줄을 x만큼 더 당기고,
    ///       탄성 에너지는 E = ½kx²로 쌓인다. 휠을 굴리는 것이 곧 플레이어가 에너지를 버는 행동이다</item>
    /// <item><b>방출</b>: ½kx² = ½mv² → v = x·√(k/m). 발사 속력은 감은 양에 <b>정비례</b>한다
    ///       (두 배 감으면 두 배 빠르고 에너지는 4배). 그래서 k를 노출하지 않고
    ///       "끝까지 감았을 때의 속력" 하나만 튜닝값으로 둔다 — 같은 뜻이고 읽기 쉽다</item>
    /// <item><b>방향</b>: 포물선 꼭짓점을 옥상 모서리 너머에 두도록 역산한다.
    ///       v_y = √(2gΔh), t = v_y/g, v_x = d/t</item>
    /// </list>
    ///
    /// <b>방향만은 물리를 그대로 따르지 않는다.</b> 실제 고무줄이라면 몸은 앵커 쪽으로
    /// 날아가는데, 지상에서 잡을 수 있는 앵커는 벽 중턱뿐이라 그대로 두면 벽에 정면으로
    /// 박힌다. 그래서 <b>크기(속력)는 플레이어가 번 에너지, 방향은 게임이 역산</b>으로 나눴다.
    /// 견인이 도착 속도를 설계값으로 재설정하는 것과 같은 판단이다.
    ///
    /// <b>"웹은 스프링이 아니다"(설계 결정 1)와 부딪히지 않는다.</b> 스윙 로프는 에너지를
    /// <b>보존하며 방향만 바꾸는</b> 도구라 늘어나면 안 되고, 슬링샷은 에너지를 <b>저장하는</b>
    /// 도구라 늘어나야 한다. 목적이 달라서 물성이 다르다.
    ///
    /// <b>실행 순서를 PlayerMovement 뒤로 고정한다(<c>DefaultExecutionOrder</c>).</b>
    /// 지상 이동은 매 물리 스텝 수평 속도와 회전을 입력값으로 <b>대입</b>한다. 조준 중
    /// 제자리에 세우고 건물을 보게 하는 것을 그 앞에서 하면 바로 다음에 덮어써지므로,
    /// 반드시 그 뒤에서 덮어야 한다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Rigidbody))]
    public class WebSlingshot : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커 판정과 로프 그리기를 빌려 쓴다. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        [Header("입력 (InputSystem_Actions ▸ Player ▸ Zip — 마우스 우클릭)")]
        [Tooltip("견인과 같은 액션을 쓴다 — 지상이면 슬링샷, 공중이면 견인.\n\n" +
                 "비워두면 같은 오브젝트의 GrappleZip이 쓰는 액션을 빌려 쓴다. 두 칸에 따로 " +
                 "넣어두면 언젠가 하나만 바뀌어 '우클릭이 지상에서만 안 먹는다' 같은 사고가 난다.")]
        [SerializeField] private InputActionReference slingshotAction = null;

        [Header("휠 충전 (InputSystem_Actions ▸ Player ▸ Reel — 마우스 휠)")]
        [Tooltip("충전에 쓸 휠 액션. 비워두면 같은 오브젝트의 SwingConstraint가 쓰는 Reel 액션을 " +
                 "빌려 쓴다. 슬링샷은 지상, 스윙은 공중에서만 켜지므로 같은 휠을 써도 겹치지 않는다.")]
        [SerializeField] private InputActionReference chargeAction = null;

        [Tooltip("휠 한 칸에 오르는 충전량(0~1). 0.1이면 10칸에 최대. 작을수록 '당긴다'는 손맛이 " +
                 "살고, 클수록 빠르다 — 속도감이 우선이면 0.2(5칸) 정도.")]
        [Range(0.01f, 1f)][SerializeField] private float chargePerNotch = 0.1f;

        [Tooltip("보이는 충전량(허리 젖힘)이 실제 충전량을 따라가는 속도(초당). 휠은 칸 단위라 " +
                 "그대로 애니메이터에 넣으면 몸이 툭툭 끊겨 젖혀진다. 0이면 즉시 대입한다.")]
        [Min(0f)][SerializeField] private float chargeVisualSpeed = 4f;

        [Tooltip("끝까지 감았을 때의 발사 속력(m/s). v = x·√(k/m)이라 속력은 감은 양에 " +
                 "정비례하므로, 스프링 상수 k 대신 이 값 하나로 튜닝한다.\n\n" +
                 "기준: 중력 14.7 m/s²에서 36m 옥상까지 약 19m 떨어져 쏘면 약 34 m/s가 " +
                 "필요하다. 38이면 약 47m 높이까지 닿고, 그보다 높은 타워는 한 번에 못 간다 " +
                 "— 그건 견인으로 이어갈 몫이다. 스윙 속도 범위(25~40 m/s) 안에 두는 것이 " +
                 "전제다 — 넘으면 고속 물리 설정(설계 결정 2)을 다시 봐야 한다.")]
        [Min(1f)][SerializeField] private float maxLaunchSpeed = 38f;

        [Tooltip("이 비율보다 덜 감고 놓으면 발사하지 않고 그냥 풀린다. 우클릭을 툭 " +
                 "누른 것만으로 몸이 튀어 오르는 사고를 막는다.")]
        [Range(0f, 1f)][SerializeField] private float minCharge = 0.2f;

        [Tooltip("붙은 건물의 옥상이 내 발보다 이만큼 이상 높을 때만 슬링샷이 우클릭을 가져간다(m).\n\n" +
                 "IsGrounded는 옥상 위에서도 참이라, '지상이면 슬링샷'으로만 가르면 옥상 → 옥상 " +
                 "견인(기존 핵심 동작)이 통째로 슬링샷으로 바뀐다. 높이 차로 가르면 길바닥과 '바로 " +
                 "옆이 훨씬 높은 타워'일 때만 슬링샷이 되고, 비슷한 높이 옥상끼리는 견인 그대로다.")]
        [Min(0f)][SerializeField] private float minRoofRise = 10f;

        [Tooltip("충전 중 이 시간(초)보다 오래 공중에 있으면 줄을 푼다. 접지 신호가 잠깐 " +
                 "끊기는 것으로 충전이 날아가지 않게 하는 유예다. 점프는 이보다 길어서 걸린다.")]
        [Min(0f)][SerializeField] private float airborneGrace = 0.3f;

        // 추가 생성 (2026-10-18) — 공중 F
        [Tooltip("공중에서 F로 충전하는 동안 속도를 초당 이만큼(m/s) 줄여 줄에 걸린 것처럼 멈춘다. 중력도 상쇄한다.")]
        [Min(0f)][SerializeField] private float airHangDecel = 40f;
        // 추가 생성 (2026-10-19) — 땅 F도 마일즈처럼 살짝 떠올라 공중에 걸린 채 당긴다
        [Tooltip("땅에서 F로 줄을 걸면 이만큼(m) 떠올라 공중에 걸린 채 당긴다. 0이면 땅에 선 채(예전).")]
        [Min(0f)][SerializeField] private float groundLift = 0f; // 수정 (2026-10-19) — 피터처럼 땅에 앉아 버티기로(떠오르지 않음)
        [Tooltip("공중에 걸린 높이로 몸을 붙잡는 세기(초당). 클수록 그 높이에 딱 붙는다.")]
        [Min(0.1f)][SerializeField] private float hangSpring = 6f;
        private float _hangY;
        // 추가 생성 (2026-10-19) — 쪼그려 앉기: 다리만 굽히는 레이어(SlingCrouch)라 그만큼 모델을 내려 발을 땅에 붙인다
        [Tooltip("충전 중 모델을 내리는 거리(m). 다리만 굽혀 들린 발을 땅에 붙이고 엉덩이를 낮춘다. 웅크린 클립의 엉덩이 높이 차이쯤.")]
        [Min(0f)][SerializeField] private float crouchDrop = 0.45f;
        private Vector3 _visualRestPos;
        private float _crouch;
        // 추가 생성 (2026-10-18) — 공중 F 충전 연출
        [Tooltip("공중 F 충전이 꽉 찼을 때 몸 전체가 뒤로 젖혀지는 각도(°). 땅 F의 허리 젖힘을 공중에서 몸 전체로 보여준다. 0이면 끈다.")]
        [Range(0f, 80f)][SerializeField] private float airChargeLeanMax = 0f; // 수정 (2026-10-19) — 앞으로 숙이면(12°) 무릎 꿇고 고개 숙인 자세가 됐다. 웅크림은 닌자 점프 클립이 맡는다
        // 추가 생성 (2026-10-19)
        [Tooltip("충전이 꽉 찼을 때 허리 젖힘 블렌드(SlingCharge)를 얼마까지 쓸지(0~1). 1이면 끝까지 젖혀 누운 것처럼 보인다.")]
        [Range(0f, 1f)][SerializeField] private float chargeArchMax = 0.4f;
        [Tooltip("젖힘 각도가 목표를 따라가는 속도(초당 각도). 휠 한 칸마다 오는 반동(_yank)이 보이려면 충분히 빨라야 한다.")]
        [Min(1f)][SerializeField] private float airChargeLeanSpeed = 360f;

        // 추가 생성 (2026-10-19) — 발사 연출
        [Header("발사 연출 — \"팡\" (공통 연출 TraversalFx)")]
        [Tooltip("발사 순간 멈칫(히트스톱) 길이(실제 초). 시야각 펀치·흔들림은 SpeedFieldOfView가 이미 준다.")]
        [Min(0f)][SerializeField] private float launchHitStop = 0.05f;
        [Tooltip("발사 뒤 속도선을 켜 둘 시간(초).")]
        [Min(0f)][SerializeField] private float launchSpeedLines = 0f; // 수정 (2026-10-21) — 바람 연출 교체 전까지 끔
        [Tooltip("앞으로 대시(F)로 날아가는 동안 몸을 앞으로 눕히는 각도(°). 슈퍼히어로 비행 자세. 0이면 끈다.")]
        [Range(0f, 90f)][SerializeField] private float flightLean = 0f; // 수정 (2026-10-19) — 55는 점프 자세와 겹쳐 "개 자세"가 됐다. 비행은 Flying 클립이 맡는다
        [Tooltip("발사 뒤 손·발 꼬리(트레일)를 켜 둘 시간(초). 0이면 끈다.")]
        [Min(0f)][SerializeField] private float launchTrailTime = 0f; // 수정 (2026-10-21) — 바람 연출 교체 전까지 끔
        // 추가 생성 (2026-10-21)
        [Tooltip("발사 순간 한 번에 뿌리는 바람 조각 수(WindStreaks). 0이면 끈다.")]
        [Min(0)][SerializeField] private int launchWindBurst = 18;
        // 추가 생성 (2026-10-20)
        [Tooltip("손·발 꼬리에 쓸 트레일 프리팹. 비우면 흰 단색 꼬리. Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Sound 추천(공기 물결 + 왜곡).")]
        [SerializeField] private GameObject limbTrailPrefab = null;
        private LimbTrails _trails;

        [Tooltip("조준하는 동안 몸을 앵커(건물) 쪽으로 돌리는 속도(초당 각도). 등을 보이고 " +
                 "우클릭해도 금방 건물을 마주 보게 된다.\n\n" +
                 "<b>PlayerMovement의 Turn Speed(기본 540)보다 커야 한다.</b> 조준 중에 방향키를 " +
                 "누르면 PlayerMovement가 매 스텝 그쪽으로 돌리고 이 스크립트가 바로 뒤에서 되돌리는데, " +
                 "이 값이 더 작으면 다 못 되돌려 몸이 입력 쪽으로 서서히 돌아간다.")]
        [Min(0f)][SerializeField] private float aimTurnDegPerSec = 720f;

        // 추가 생성 — 발사 방식 (2026-10-15)
        /// <summary>슬링샷을 놓았을 때 어디로 보낼지.</summary>
        public enum LaunchMode
        {
            /// <summary>마블 스파이더맨 2의 슬링샷 런치처럼 카메라가 보는 앞쪽 공중으로 쭉 튕겨 나간다(기본).</summary>
            Forward,
            /// <summary>예전 방식 — 줄을 건 건물의 옥상 위에 꼭짓점이 오게 올라간다.</summary>
            RoofClimb,
        }

        // 수정 (2026-10-15) — 방식을 설정 하나로 고르지 않고 키로 나눈다: 우클릭 = 옥상 오르기(예전 그대로), F = 앞으로 대시.
        [Header("발사 방식 — 우클릭은 옥상 오르기, F는 앞으로 대시")]
        [Tooltip("앞으로 대시(마블 스파이더맨 2의 슬링샷 런치) 입력. 누르고 있는 동안 충전, 떼면 발사. " +
                 "비워두면 F 키로 실행 중에 만든다(InputSystem_Actions 파일은 건드리지 않는다).")]
        [SerializeField] private InputActionReference forwardAction = null;
        [Tooltip("Forward 발사의 최소 위 각도(°). 카메라가 아래를 봐도 이만큼은 띄워서 땅에 꽂히지 않게 한다.")]
        [Range(0f, 45f)][SerializeField] private float minLaunchPitch = 10f;
        [Tooltip("Forward 발사의 최대 위 각도(°). 카메라가 하늘을 봐도 이 이상은 안 솟는다 — 앞으로 나가는 대시라서.")]
        [Range(10f, 80f)][SerializeField] private float maxLaunchPitch = 35f;
        [Tooltip("발사 방향 기준 카메라. 비워두면 Camera.main.")]
        [SerializeField] private Transform aimCamera = null;

        [Header("발사 — 꼭짓점을 옥상 너머에 둔다 (RoofClimb)")]
        [Tooltip("옥상 모서리에서 건물 안쪽으로 얼마나 들어간 지점에 꼭짓점을 둘지(m). " +
                 "0이면 모서리 난간에 걸리고, 너무 크면 얕은 옥상을 넘어 반대편 길로 떨어진다.")]
        [Min(0f)][SerializeField] private float landingInset = 3f;

        [Tooltip("꼭짓점을 옥상 면보다 얼마나 위에 둘지(m). 캡슐 높이와 난간을 넘을 여유다.")]
        [Min(0f)][SerializeField] private float apexClearance = 2f;

        [Tooltip("날아가는 동안 몸을 발사 방향으로 돌리는 속도(초당 각도). 조준 중에 이미 " +
                 "건물을 보고 있으므로 대개 거의 돌 일이 없고, 비스듬히 쏜 만큼만 보정한다.")]
        [Min(0f)][SerializeField] private float flightTurnDegPerSec = 720f;

        [Header("애니메이션 (PlayerAnimatorBuilder가 만든 Slingshot 레이어)")]
        [Tooltip("Slingshot 레이어 가중치가 오르내리는 속도(초당). 8이면 0↔1에 0.125초. " +
                 "0이면 즉시 바뀐다 — 자세가 한 프레임에 갈아 끼워져 튄다(AnimatorLayerFade 참고).")]
        [Min(0f)][SerializeField] private float animLayerLerpSpeed = AnimatorLayerFade.DefaultSpeed;

        // 추가 생성 — 휠 한 칸 = 한 번 "홱" (2026-10-15)
        [Header("당김 손맛 — 휠 한 칸마다 몸이 툭 젖혀지고 줄이 떨린다")]
        [Tooltip("휠 한 칸에 주는 당김 충격(허리 젖힘 속도). 클수록 한 칸이 크게 '홱' 들어간다.")]
        [Min(0f)][SerializeField] private float yankKick = 3f;
        [Tooltip("스프링 강도. 클수록 충격이 빨리 제자리로 돌아온다(짧고 단단한 당김).")]
        [Min(0f)][SerializeField] private float yankStiffness = 220f;
        [Tooltip("스프링 감쇠. 작으면 젖힘이 몇 번 출렁이고, 크면 한 번에 멈춘다.")]
        [Min(0f)][SerializeField] private float yankDamping = 18f;
        [Tooltip("충격으로 더해지는 젖힘의 상한(0~1, SlingCharge 단위). 너무 크면 몸이 뒤로 꺾여 보인다.")]
        [Range(0f, 0.5f)][SerializeField] private float maxYank = 0.2f;
        [Tooltip("젖힘 충격 1당 줄 가운데가 옆으로 밀리는 거리(m). 충격 최대(maxYank 0.2)일 때 기본 5면 1 m 떨린다. 0이면 줄은 안 떨린다.")]
        [Min(0f)][SerializeField] private float ropeWobblePerYank = 5f;

        /// <summary>줄을 붙이고 조준·충전하는 중인가.</summary>
        public bool IsCharging { get; private set; }

        /// <summary>발사 후 꼭짓점까지 날아가는 중인가. 이 동안은 공중 조작을 막는다.</summary>
        public bool IsLaunching { get; private set; }

        /// <summary>현재 충전량(0~1). 휠로 감은 양. UI나 카메라 연출이 읽을 수 있게 공개한다.</summary>
        public float Charge01 { get; private set; }

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private GrappleZip _zip;

        /// <summary>실제로 구독한 액션. 내 칸이 비면 GrappleZip의 것을 빌려 오므로 따로 들고 있는다.</summary>
        private InputAction _action;

        /// <summary>실제로 읽는 휠 액션. 내 칸이 비면 SwingConstraint의 Reel을 빌려 온다.</summary>
        private InputAction _chargeAction;

        private Animator _animator;
        private int _slingLayerIndex = -1;

        /// <summary>화면에 보이는 충전량. Charge01을 일정 속도로 따라가 허리 젖힘을 부드럽게 만든다.</summary>
        private float _shownCharge;
        // 추가 생성 (2026-10-18) — 공중 F 젖힘: 기울일 모델 뿌리, 원래 자세, 지금 각도
        private Transform _visual;
        private Quaternion _visualRest;
        private float _lean;

        // 추가 생성 — 당김 스프링 상태. _yank는 허리 젖힘에 더하는 값, _yankVelocity는 그 변화 속도.
        private float _yank;
        private float _yankVelocity;

        // 추가 생성 (2026-10-15) — 앞으로 대시 입력과, 지금 충전이 어느 방식으로 시작됐는지.
        private InputAction _forwardAction;
        private bool _ownsForwardAction;   // 실행 중에 직접 만든 액션이면 끌 때 정리한다
        private LaunchMode _mode = LaunchMode.RoofClimb;

        /// <summary>지금 충전·비행이 앞으로 대시(F)인가. 카메라 연출 등이 읽을 수 있게 공개한다.</summary>
        public bool IsForwardDash => _mode == LaunchMode.Forward;

        // 문자열 비교를 매 프레임 하지 않도록 해시를 한 번만 만든다(Animator.StringToHash).
        // 이름은 PlayerAnimatorBuilder.BuildSlingshotLayer가 만드는 파라미터와 같아야 한다.
        private static readonly int SlingBeginHash = Animator.StringToHash("SlingBegin");
        private static readonly int SlingLaunchHash = Animator.StringToHash("SlingLaunch");
        // 추가 생성 (2026-10-18) — 공중 F 매달림 자세. 컨트롤러에 파라미터가 있을 때만 쓴다(없으면 땅 조준 자세).
        // 수정 (2026-10-19) — 공중 F는 상체를 땅과 같은 Charge로 쓰고 다리만 SlingAirLegs 레이어가 덮는다.
        private int _airLegsLayerIndex = -1;
        // 추가 생성 (2026-10-19) — 앞으로 대시 비행 자세(Flying). 컨트롤러에 있을 때만 쓴다.
        private static readonly int SlingLaunchForwardHash = Animator.StringToHash("SlingLaunchForward");
        private bool _hasDashFlight;
        private static readonly int SlingChargeHash = Animator.StringToHash("SlingCharge");

        /// <summary>앵커. 건물이 움직일 일은 없지만 견인과 같은 방식(트랜스폼 + 로컬 좌표)으로 들고 있는다.</summary>
        private Transform _anchorTransform;
        private Vector3 _anchorLocalPoint;
        private Collider _anchorCollider;
        private Vector3 _anchorNormal;

        /// <summary>놓았을 때 다음 물리 스텝에서 발사하도록 예약한다(입력은 Update 타이밍이라서).</summary>
        private bool _launchQueued;

        private float _launchTime;
        private float _airborneTime;
        /// <summary>이번 충전을 공중에서 시작했는가(공중 F). 참이면 "떠 있으면 취소" 규칙을 안 쓰고 공중에 멈춘다.</summary>
        private bool _airStart; // 추가 생성 (2026-10-18)
        private Vector3 _launchDirection;

        /// <summary>내가 켠 로프 그리기인지. WebGrabThrow와 같은 이유로, 남의 값을 지우지 않게 한다.</summary>
        private bool _ownsRopeOverride;

        // 추가 생성 (2026-10-19) — 팔 IK(SlingPullIK)가 읽는 값
        /// <summary>줄이 붙은 점(월드). 손이 이쪽을 향해 뻗었다가 끌려온다.</summary>
        public Vector3 AimAnchor => AnchorPoint;
        /// <summary>보이는 충전량(0~1, 휠 반동 포함). 손이 가슴 쪽으로 끌려오는 정도.</summary>
        public float ChargeVisual01 => Mathf.Clamp01(_shownCharge + _yank);

        private Vector3 AnchorPoint =>
            _anchorTransform != null
                ? _anchorTransform.TransformPoint(_anchorLocalPoint)
                : transform.position;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            _zip = GetComponent<GrappleZip>();
            if (gun == null) gun = GetComponent<GrappleGun>();

            if (gun == null)
                Debug.LogWarning("[WebSlingshot] GrappleGun이 없어 앵커를 찾을 수 없다.", this);

            // GrappleZip과 같은 방식으로 레이어를 이름으로 찾는다. 없으면(-1) 애니메이션 없이 동작만 한다.
            _animator = GetComponentInChildren<Animator>();
            // 추가 생성 (2026-10-18) — Animator가 붙은 트랜스폼이 캐릭터 메시의 뿌리(SwingDive·SwingConstraint와 같은 대상).
            if (_animator != null) { _visual = _animator.transform; _visualRest = _visual.localRotation; _visualRestPos = _visual.localPosition; }
            // 추가 생성 (2026-10-19) — 손·발 꼬리. 없으면 실행 중에 붙인다.
            _trails = GetComponent<LimbTrails>();
            if (_trails == null) { _trails = gameObject.AddComponent<LimbTrails>(); _trails.Init(_animator, limbTrailPrefab); }
            // 추가 생성 (2026-10-19) — 감을 때 총을 가슴 쪽으로 끌어당기는 팔 IK. Animator와 같은 오브젝트에 있어야 한다.
            if (_animator != null && _animator.GetComponent<SlingPullIK>() == null)
                _animator.gameObject.AddComponent<SlingPullIK>().Init(this);
            if (_animator != null) _slingLayerIndex = _animator.GetLayerIndex("Slingshot");
            if (_animator != null) _airLegsLayerIndex = _animator.GetLayerIndex("SlingCrouch"); // 추가 생성 (2026-10-19)
            // 추가 생성 (2026-10-18) — 없는 트리거를 쏘면 매번 경고가 뜨므로 미리 확인해 둔다.
            if (_animator != null)
                foreach (var parameter in _animator.parameters)
                    if (parameter.nameHash == SlingLaunchForwardHash) _hasDashFlight = true;
        }

        private void OnEnable()
        {
            // 휠: 내 칸이 비었으면 스윙의 Reel을 빌린다. WebGrabThrow와 같은 규칙으로 Enable만
            // 하고 Disable은 하지 않는다 — 같이 쓰는 액션이라 여기서 끄면 스윙 감기까지 죽는다.
            InputActionReference chargeRef = chargeAction;
            if (chargeRef == null && TryGetComponent(out SwingConstraint swing)) chargeRef = swing.ReelAction;
            _chargeAction = chargeRef != null ? chargeRef.action : null;
            if (_chargeAction != null) _chargeAction.Enable();
            else Debug.LogWarning("[WebSlingshot] 휠 액션이 없어 충전할 수 없다. Reel 액션을 넣어라.", this);

            // 우클릭: 내 칸이 비었으면 견인이 쓰는 액션을 빌린다 — 같은 우클릭을 지상/공중으로 나눠 쓰므로.
            InputActionReference reference =
                slingshotAction != null ? slingshotAction : (_zip != null ? _zip.ZipAction : null);
            _action = reference != null ? reference.action : null;
            if (_action == null)
            {
                Debug.LogWarning("[WebSlingshot] 입력 액션이 없다. Zip 액션을 넣거나 GrappleZip을 같이 붙여라.", this);
                return;
            }

            _action.Enable();
            // Button 타입 액션은 누를 때 performed, 뗄 때 canceled가 온다. 누르고 있는 동안이
            // 충전 구간이므로 두 이벤트가 다 필요하다.
            _action.performed += OnPressed;
            _action.canceled += OnReleased;

            // 추가 생성 (2026-10-15) — 앞으로 대시 키. 칸이 비면 F 키 액션을 실행 중에 만든다(Unity 내장 InputAction 생성자).
            // 입력 에셋을 고치지 않는 이유: 에셋을 바꾸면 컨트롤 스킴·다른 PC 설정까지 같이 바뀌어 충돌할 수 있다.
            if (forwardAction != null) _forwardAction = forwardAction.action;
            else
            {
                _forwardAction = new InputAction("SlingshotForward", InputActionType.Button, "<Keyboard>/f");
                _ownsForwardAction = true;
            }
            _forwardAction.Enable();
            _forwardAction.performed += OnForwardPressed;
            _forwardAction.canceled += OnForwardReleased;
        }

        private void OnDisable()
        {
            if (_action != null)
            {
                _action.performed -= OnPressed;
                _action.canceled -= OnReleased;
                // Disable()은 부르지 않는다 — 이 액션은 GrappleZip과 같이 쓰므로, 여기서 끄면
                // 슬링샷 하나 껐을 뿐인데 공중 견인까지 안 먹는다.
                _action = null;
            }

            // 추가 생성 — 앞으로 대시 키 정리. 직접 만든 액션이면 끄고 버린다(남이 쓰는 액션이 아니다).
            if (_forwardAction != null)
            {
                _forwardAction.performed -= OnForwardPressed;
                _forwardAction.canceled -= OnForwardReleased;
                if (_ownsForwardAction) { _forwardAction.Disable(); _forwardAction.Dispose(); }
                _forwardAction = null;
                _ownsForwardAction = false;
            }

            // 충전·비행 중에 꺼지면 스위치가 켜진 채로 남아 조작이 영영 안 먹는다(견인과 같은 이유).
            if (IsCharging || IsLaunching) EndAll();
        }

        // 수정 (2026-10-15) — 우클릭 = 옥상 오르기, F = 앞으로 대시. 놓기는 시작한 키로만 받는다
        // (F로 감는 중에 우클릭을 떼도 발사되지 않게).
        private void OnPressed(InputAction.CallbackContext ctx) => TryBeginCharge(LaunchMode.RoofClimb);
        private void OnReleased(InputAction.CallbackContext ctx) => ReleaseCharge(LaunchMode.RoofClimb);
        private void OnForwardPressed(InputAction.CallbackContext ctx) => TryBeginCharge(LaunchMode.Forward);
        private void OnForwardReleased(InputAction.CallbackContext ctx) => ReleaseCharge(LaunchMode.Forward);

        private void ReleaseCharge(LaunchMode mode)
        {
            if (!IsCharging || _mode != mode) return;

            // 덜 감았으면 그냥 푼다. 발사는 다음 물리 스텝으로 미룬다 — 속도를 Update
            // 타이밍에 대입하면 그 사이 PlayerMovement의 FixedUpdate가 수평 속도를 다시 덮어쓸 수 있다.
            if (Charge01 < minCharge) EndCharge();
            else _launchQueued = true;
        }

        /// <summary>
        /// 줄을 붙인다. <b>지상에서만</b> — 공중 웹 집은 스페이스바(<see cref="GrappleZip"/>)로 나간다.
        ///
        /// 앵커 판정은 견인과 같은 옵션을 쓴다. 소품은 빼고(건물에 붙어야 옥상으로 간다),
        /// 생 벽면의 진자 검사도 건너뛴다(매달리지 않으므로 의미가 없고, 지상에서는 벽면이
        /// 유일하게 사거리 안에 있는 목표다).
        /// </summary>
        private void TryBeginCharge(LaunchMode mode)
        {
            if (IsCharging || !CanBegin(mode, out RaycastHit hit)) return;
            _mode = mode; // 추가 생성 — 놓을 때 어느 방식으로 쏠지 기억한다

            _anchorTransform = hit.collider.transform;
            _anchorLocalPoint = _anchorTransform.InverseTransformPoint(hit.point);
            _anchorCollider = hit.collider;
            _anchorNormal = hit.normal;
            _airborneTime = 0f;
            _airStart = _movement != null && !_movement.IsGrounded; // 추가 생성 (2026-10-18)
            // 추가 생성 (2026-10-19) — 땅 F는 살짝 떠올라 공중 F와 같은 모션으로. 옥상 오르기(우클릭)는 땅에 선 채.
            _hangY = _rb.position.y;
            if (mode == LaunchMode.Forward && !_airStart && groundLift > 0f)
            {
                _airStart = true;
                _hangY += groundLift;
            }
            Charge01 = 0f;
            _shownCharge = 0f;
            IsCharging = true;

            // 조준 자세로. Bool이 아니라 Trigger인 이유는 PlayerAnimatorBuilder.BuildSlingshotLayer 주석 참고.
            // 수정 (2026-10-18) — 공중에서 시작했으면 매달림 자세(AirHang), 아니면 땅 조준 자세.
            if (_animator != null)
                _animator.SetTrigger(SlingBeginHash); // 수정 (2026-10-19) — 땅·공중 모두 같은 권총 조준(상체)

            // 견인과 같은 이유로 여기서 바로 넘긴다 — FixedUpdate까지 기다리면 그 사이 한 프레임 줄이 안 보인다.
            SetRope(AnchorPoint);
        }

        /// <summary>
        /// 이번 우클릭을 슬링샷이 가져가는가. <see cref="GrappleZip"/>이 견인을 시작하기 전에 묻는다.
        ///
        /// 두 컴포넌트가 같은 액션의 performed를 각자 구독하므로 <b>누가 먼저 불릴지 정해져 있지
        /// 않다.</b> 그래서 상태(IsCharging)와 판정(CanBegin)을 둘 다 본다 — 슬링샷이 먼저 불렸으면
        /// 이미 충전 중이고, 견인이 먼저 불렸으면 같은 판정을 미리 돌려본다. 어느 순서든 답이 같다.
        /// </summary>
        public bool ClaimsPress() => isActiveAndEnabled && (IsCharging || CanBegin(LaunchMode.RoofClimb, out _));

        /// <summary>
        /// 슬링샷을 시작할 수 있는가 — 발이 땅에 있고, 앵커가 있고, 그 건물 옥상이 충분히 높을 때.
        /// </summary>
        // 수정 (2026-10-15) — 어느 방식으로 시작하려는지(mode)를 받는다.
        private bool CanBegin(LaunchMode mode, out RaycastHit hit)
        {
            hit = default;
            if (IsLaunching || gun == null || gun.IsAttached) return false;
            // 수정 (2026-10-18) — 앞으로 대시(F)는 공중에서도 된다. 옥상 오르기(우클릭)는 지금처럼 땅에서만.
            if (_movement == null) return false;
            if (mode == LaunchMode.RoofClimb && !_movement.IsGrounded) return false;
            // 추가 생성 (2026-10-19) — 우클릭에 포인트 런치 포인트가 보이면 양보한다(땅에서도 공중과 같게).
            if (mode == LaunchMode.RoofClimb && _zip != null && _zip.WillPointLaunch) return false;
            if (_zip != null && _zip.IsZipping) return false;

            if (!gun.TryFindAnchor(out hit, includeProps: false, requireClearHang: false)) return false;

            // 수정 (2026-10-15) — 앞으로 대시(Forward)는 앞에 붙을 벽만 있으면 된다. 옥상 높이 조건은 옥상 오르기에만.
            if (mode == LaunchMode.Forward) return true;

            // 옥상이 별로 안 높으면 견인에게 넘긴다(minRoofRise 설명 참고).
            float roofRise = hit.collider.bounds.max.y - transform.position.y;
            return roofRise >= minRoofRise;
        }

        /// <summary>
        /// 휠로 충전량을 채우고, Slingshot 레이어 가중치와 허리 젖힘(SlingCharge)을 갱신한다.
        ///
        /// <b>휠은 FixedUpdate가 아니라 여기서 읽는다.</b> 휠 한 칸은 한 프레임에만 값이 들어온다.
        /// 물리 스텝은 프레임마다 0번 돌 수도 있어서, 거기서 읽으면 그 칸을 놓친다.
        /// </summary>
        private void Update()
        {
            if (IsCharging && _chargeAction != null)
            {
                float input = _chargeAction.ReadValue<float>();
                if (Mathf.Abs(input) > 0.01f)
                {
                    // 값의 크기가 아니라 부호만 쓴다. 휠 델타는 플랫폼마다 단위가 달라(윈도우는
                    // 한 칸에 120) 크기를 믿으면 기계마다 충전 속도가 달라진다(SwingConstraint와 같은 규칙).
                    // 몸 쪽으로 굴리면(음수) 당긴다 — 시위를 뒤로 당기는 손동작과 방향이 같다.
                    float before = Charge01; // 추가 생성 — 이번 칸에 실제로 감겼는지 보려고
                    Charge01 = Mathf.Clamp01(Charge01 - Mathf.Sign(input) * chargePerNotch);
                    // 추가 생성 — 감긴 칸이면 당김 충격을 준다(꽉 찬 뒤 더 굴리거나 푸는 칸은 제외).
                    if (Charge01 > before) _yankVelocity += yankKick;
                }
            }

            UpdateYank(); // 추가 생성 — 당김 스프링과 줄 떨림

            // 추가 생성 (2026-10-19) — 감을수록 줄이 팽팽(굵게). 충전이 아닐 땐 0으로 돌려놓는다.
            if (gun != null && _ownsRopeOverride) gun.RopeTension01 = IsCharging ? _shownCharge : 0f;

            if (_animator == null || _slingLayerIndex < 0) return;

            // 조준·비행 중에만 레이어를 켠다. 꼭짓점에서 IsLaunching이 꺼지면 서서히 내려가며
            // 기본 레이어의 낙하 자세로 넘어간다 — 끝난 뒤에도 매 프레임 돌아야 하는 이유다.
            AnimatorLayerFade.To(_animator, _slingLayerIndex,
                IsCharging || IsLaunching ? 1f : 0f, animLayerLerpSpeed);

            // 보이는 충전량은 실제 값을 일정 속도로 따라간다 → 칸 단위 입력이 연속 동작이 된다.
            _shownCharge = chargeVisualSpeed <= 0f
                ? Charge01
                : Mathf.MoveTowards(_shownCharge, Charge01, chargeVisualSpeed * Time.deltaTime);
            // 수정 (2026-10-15) — 부드럽게 따라가는 젖힘 위에 당김 충격(_yank)을 얹는다 → 칸마다 툭, 툭 끊어진다.
            _animator.SetFloat(SlingChargeHash, Mathf.Clamp01(_shownCharge + _yank) * chargeArchMax); // 수정 (2026-10-19) — 젖힘 상한
        }

        // 추가 생성 (2026-10-15)
        /// <summary>
        /// 당김 스프링 한 프레임. 휠 한 칸이 속도(<c>_yankVelocity</c>)를 튕기면, 용수철처럼
        /// 잠깐 더 젖혀졌다가 제자리로 돌아온다(감쇠 진동: a = −k·x − c·v).
        ///
        /// 애니메이션 클립을 새로 만들지 않고 기존 허리 젖힘 값(SlingCharge)에 얹는 이유: 무료 클립이 없고,
        /// "칸마다 끊어지는 당김"은 동작의 모양이 아니라 <b>타이밍</b>이라 값 하나로 충분하다.
        /// 같은 값으로 줄 가운데를 옆으로 밀어 당길 때마다 줄이 떨리게 한다.
        /// </summary>
        private void UpdateYank()
        {
            if (!IsCharging)
            {
                _yank = 0f;
                _yankVelocity = 0f;
                if (gun != null && _ownsRopeOverride) gun.RopeWobble = 0f;
                return;
            }

            float dt = Time.deltaTime;
            _yankVelocity += (-yankStiffness * _yank - yankDamping * _yankVelocity) * dt;
            _yank = Mathf.Clamp(_yank + _yankVelocity * dt, -maxYank, maxYank);

            if (gun != null) gun.RopeWobble = _yank * ropeWobblePerYank;
        }

        // 추가 생성 (2026-10-18)
        /// <summary>
        /// 공중 F 충전 연출 — 충전할수록 몸 전체를 뒤로 젖힌다. 땅 F에서 허리가 젖혀지는 것(SlingCharge 블렌드)을
        /// 공중 매달림 자세(Hanging) 위에서 보여주는 방법이다. 새 클립 없이 모델 뿌리만 돌리므로 어떤 자세와도 겹쳐 쓴다.
        /// 휠 한 칸마다 튀는 반동(_yank)도 각도에 그대로 더해 "툭, 툭" 당겨지는 느낌을 준다.
        /// 발사하거나 취소하면 0으로 돌아오고, 0이 되면 더는 모델 회전을 건드리지 않는다(다른 연출에 양보).
        /// </summary>
        private void LateUpdate()
        {
            // 추가 생성 (2026-10-19) — 충전 중엔(땅·공중) 몸통·다리를 웅크린 자세로(SlingCrouch 레이어).
            AnimatorLayerFade.To(_animator, _airLegsLayerIndex, IsCharging ? 1f : 0f, animLayerLerpSpeed);

            if (_visual == null) return;

            // 추가 생성 (2026-10-19) — 충전 중 쪼그려 앉기(모델을 내림). 레이어 가중치와 같은 빠르기로 오르내린다.
            float crouchTarget = IsCharging ? 1f : 0f;
            bool crouchWas = _crouch > 0.001f;
            _crouch = Mathf.MoveTowards(_crouch, crouchTarget, animLayerLerpSpeed * Time.deltaTime);
            if (crouchWas || _crouch > 0.001f)
                _visual.localPosition = _visualRestPos + Vector3.down * (crouchDrop * _crouch);

            // 수정 (2026-10-19) — 부호 있는 각도로 바꿨다: 음수 = 뒤로 젖힘(공중 충전), 양수 = 앞으로 눕힘(F 비행).
            float target = 0f;
            // 수정 (2026-10-19) — 스파이더맨 2 슬링샷처럼 뒤로 눕지 않고 앞으로 살짝 웅크린다(땅·공중 같게).
            // 충전할수록, 휠 한 칸마다 툭 하고 조금 더 말려 들어간다.
            if (IsCharging)
                target = airChargeLeanMax * Mathf.Clamp01(_shownCharge + _yank);
            else if (IsLaunching && _mode == LaunchMode.Forward)
                target = flightLean; // 추가 생성 (2026-10-19) — 날아가는 동안 슈퍼히어로 비행 자세

            bool wasActive = Mathf.Abs(_lean) > 0.01f;
            _lean = Mathf.MoveTowards(_lean, target, airChargeLeanSpeed * Time.deltaTime);
            if (!wasActive && Mathf.Abs(_lean) <= 0.01f) return;

            // 양수 X 회전 = 앞으로 숙임(SwingDive와 같은 방향), 음수 = 뒤로 젖힘.
            _visual.localRotation = _visualRest * Quaternion.Euler(_lean, 0f, 0f);
        }

        private void FixedUpdate()
        {
            if (IsCharging) TickCharge();
            else if (IsLaunching) TickFlight();
        }

        /// <summary>
        /// 충전 중 매 물리 스텝. 줄을 그리고, 제자리에 세워 건물을 보게 한다.
        /// 충전량 자체는 휠 입력이라 <see cref="Update"/>에서 채운다.
        /// </summary>
        private void TickCharge()
        {
            // 공중에 있던 시간을 센다. 턱 넘기처럼 짧게 뜨는 것은 봐주고, 점프·낙하만 걸러낸다.
            _airborneTime = _movement.IsGrounded ? 0f : _airborneTime + Time.fixedDeltaTime;

            // 줄을 들고 있는 동안 다른 동작이 끼어들면 접는다. 스윙(R)이 붙었거나, 점프·낙하로
            // 발이 떨어졌거나, 앵커가 사라졌을 때 — 지상 전용 동작이라 공중으로 끌고 가지 않는다.
            if (gun.IsAttached || _anchorTransform == null ||
                (!_launchQueued && !_airStart && _airborneTime > airborneGrace)) // 수정 (2026-10-18) — 공중 F는 제외
            {
                EndCharge();
                return;
            }

            Vector3 anchor = AnchorPoint;
            SetRope(anchor);

            if (_launchQueued)
            {
                Launch(anchor);
                return;
            }

            HoldAim(anchor);
        }

        /// <summary>
        /// 조준 자세 유지 — 그 자리에 세우고, 몸을 앵커(건물) 쪽으로 돌린다.
        ///
        /// <b>SuspendHorizontalControl을 쓰지 않는 이유.</b> 그 스위치는 놓는 순간을
        /// PlayerMovement가 "스윙을 놓았다"로 읽어 스윙 착지 플래그를 켠다. 땅에 선 채 충전을
        /// 취소해도 그 플래그가 한 스텝 켜지므로 엉뚱한 착지 판정이 섞일 수 있다. 대신 이
        /// 스크립트가 실행 순서상 뒤(100)라는 점을 이용해, PlayerMovement가 대입한 수평 속도와
        /// 회전을 <b>바로 뒤에서 덮는다</b>. MoveRotation은 마지막 호출이 이긴다.
        /// </summary>
        private void HoldAim(Vector3 anchor)
        {
            Vector3 velocity = _rb.linearVelocity;
            if (_airStart)
            {
                // 추가 생성 (2026-10-18) — 공중 F: 줄에 걸린 것처럼 속도를 빠르게 0으로 줄이고,
                // 이번 스텝에 더해질 중력만큼 미리 빼서 그 자리에 뜬 채로 충전한다.
                // 수정 (2026-10-19) — 수평은 빠르게 0으로, 높이는 걸린 높이(_hangY)로 스프링처럼 붙잡는다.
                // 이번 스텝에 더해질 중력만큼 미리 빼서 그 높이에 뜬 채로 충전한다.
                Vector3 flat = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), Vector3.zero,
                                                   airHangDecel * Time.fixedDeltaTime);
                float vy = Mathf.Clamp((_hangY - _rb.position.y) * hangSpring, -6f, 6f);
                _rb.linearVelocity = flat + Vector3.up * vy - Physics.gravity * Time.fixedDeltaTime;
            }
            else
            {
                // 수평 속도만 지운다. y는 그대로 둬야 중력·접지 처리가 정상으로 돈다.
                _rb.linearVelocity = new Vector3(0f, velocity.y, 0f);
            }

            Vector3 toAnchor = anchor - _rb.position;
            toAnchor.y = 0f;
            if (toAnchor.sqrMagnitude < 0.0001f) return;

            Quaternion face = Quaternion.LookRotation(toAnchor.normalized, Vector3.up);
            _rb.MoveRotation(aimTurnDegPerSec <= 0f
                ? face
                : Quaternion.RotateTowards(_rb.rotation, face, aimTurnDegPerSec * Time.fixedDeltaTime));
        }

        /// <summary>
        /// 발사. 꼭짓점이 옥상 너머에 오는 속도를 역산하고, 그 크기를 번 에너지로 제한한다.
        /// </summary>
        private void Launch(Vector3 anchor)
        {
            // 수정 (2026-10-15) — 발사 속도 계산을 LaunchVelocity로 뺐다(방식별로 갈리고, 기즈모도 같은 식을 쓴다).
            Vector3 velocity = LaunchVelocity(_rb.position, anchor, out float neededSpeed);

            _rb.linearVelocity = velocity;
            _launchDirection = new Vector3(velocity.x, 0f, velocity.z);
            _launchTime = Time.time;

            // 꼭짓점까지 공중 조작을 막는다. 조준하는 동안 아무 방향키나 누르고 있었을 수 있어,
            // 그대로 두면 공중 가속(airAcceleration)이 발사 속도를 엉뚱한 쪽으로 깎아 궤적이 무너진다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            if (_movement != null && _movement.LogLift)
                Debug.Log($"[띄움] 슬링샷 발사 — 충전 {Charge01:P0}, 속도 {velocity.magnitude:F1} m/s " +
                          $"(필요 {neededSpeed:F1}), y속도 {velocity.y:F2} 대입, " +
                          $"높이 {transform.position.y:F2}m", this);

            // 발사 자세로. 레이어 가중치는 IsLaunching이 켜져 있는 동안 Update가 1로 유지한다.
            // 수정 (2026-10-19) — 앞으로 대시(F)는 비행 자세(Flying), 옥상 오르기는 지금 발사 자세.
            if (_animator != null)
                _animator.SetTrigger(_mode == LaunchMode.Forward && _hasDashFlight ? SlingLaunchForwardHash : SlingLaunchHash);

            // 추가 생성 (2026-10-19) — "팡": 멈칫 + 속도선(공통 연출). 시야각 펀치·흔들림은 SpeedFieldOfView가 준다.
            TraversalFx.Burst(_rb.position, hitStop: launchHitStop, speedLines: launchSpeedLines);
            if (_trails != null && launchTrailTime > 0f) _trails.Play(launchTrailTime);
            // 추가 생성 (2026-10-21) — 발사 순간 스윙 바람(WindStreaks)을 한 번에 확 뿌린다. 그 뒤로는 속도에 따라 저절로 나온다.
            var wind = GetComponent<WindStreaks>();
            if (wind != null) wind.Burst(launchWindBurst);

            EndCharge();
            IsLaunching = true;
        }

        // 추가 생성 (2026-10-15)
        /// <summary>
        /// 놓는 순간의 발사 속도. 번 속력은 두 방식 모두 v = x·√(k/m) → <c>maxLaunchSpeed × 충전량</c>이다.
        ///
        /// <b>Forward</b>: 카메라 방향을 위 각도 [minLaunchPitch, maxLaunchPitch]로 눌러 그 방향으로 번 속력을 <b>다</b> 쓴다.
        /// 목적이 "앞으로 멀리"라 아낄 이유가 없다. 줄은 당기는 대상일 뿐이라 방향을 정하지 않는다.
        ///
        /// <b>RoofClimb</b>: 예전 그대로 — 꼭짓점이 옥상 위에 오는 속도를 역산하고, 모자라면 크기만 줄이고
        /// 남으면 필요한 만큼만 쓴다(남는 에너지까지 쓰면 얕은 건물에서 반대편 길로 떨어진다).
        /// </summary>
        /// <param name="neededSpeed">RoofClimb에서 옥상까지 필요한 속력(로그용). Forward는 번 속력과 같다.</param>
        private Vector3 LaunchVelocity(Vector3 from, Vector3 anchor, out float neededSpeed)
        {
            float earned = maxLaunchSpeed * Charge01;

            if (_mode == LaunchMode.Forward)
            {
                Transform cam = aimCamera != null ? aimCamera : (Camera.main != null ? Camera.main.transform : transform);
                Vector3 aim = cam.forward;
                Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up);
                if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                flat.Normalize();

                // 카메라의 위 각도(앙각)를 범위 안으로 누른다. 90 − (위 방향과의 사잇각) = 앙각.
                float pitch = Mathf.Clamp(90f - Vector3.Angle(aim, Vector3.up), minLaunchPitch, maxLaunchPitch);
                // 수평 앞 방향을 오른쪽 축 기준으로 pitch만큼 들어 올린다. (왼손 좌표계라 위로 들려면 음수 각도)
                Vector3 right = Vector3.Cross(Vector3.up, flat);
                Vector3 dir = Quaternion.AngleAxis(-pitch, right) * flat;

                neededSpeed = earned;
                return dir * earned;
            }

            Vector3 needed = SolveApexVelocity(from, anchor);
            neededSpeed = needed.magnitude;
            return neededSpeed > earned && neededSpeed > 0.001f
                ? needed * (earned / neededSpeed)
                : needed;
        }

        /// <summary>
        /// 꼭짓점이 목표점에 오는 발사 속도를 구한다.
        ///
        /// <b>왜 '목표를 지나가는' 포물선이 아니라 '꼭짓점'을 겨누나.</b> PlayerMovement는
        /// 내려올 때만 중력을 2배로 건다(<c>fallGravityMultiplier</c>). 꼭짓점을 목표에 두면
        /// 계산할 구간이 <b>올라가는 동안</b>(중력 1배)뿐이라 식이 정확하고, 2배 중력은 이미
        /// 옥상 위에 있을 때부터 작동해 오히려 착지를 단단하게 해준다.
        ///
        /// 유니티에는 포물선 역산 함수가 없어 직접 쓴다 — 등가속도 운동 식 세 줄이다.
        /// </summary>
        private Vector3 SolveApexVelocity(Vector3 from, Vector3 anchor)
        {
            Vector3 target = LandingTarget(from, anchor);
            float g = Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));

            // 목표가 발보다 낮으면(낮은 건물 등) 최소 높이만큼은 띄운다 — 0이면 수평으로 쏘게 된다.
            float rise = Mathf.Max(apexClearance, target.y - from.y);

            float vy = Mathf.Sqrt(2f * g * rise);   // v² = 2gh  → 꼭짓점 높이가 rise
            float t = vy / g;                       // 꼭짓점까지 걸리는 시간

            Vector3 horizontal = target - from;
            horizontal.y = 0f;
            Vector3 vxz = horizontal / t;           // 그 시간 동안 수평 거리를 등속으로

            return vxz + Vector3.up * vy;
        }

        /// <summary>
        /// 꼭짓점을 둘 지점 — 앵커가 붙은 건물의 옥상 면, 모서리에서 안쪽으로 들어간 곳.
        ///
        /// 옥상 높이는 <c>collider.bounds.max.y</c>로 잡는다. <see cref="GrappleGun"/>이 앵커를
        /// 옥상으로 올릴 때 쓰는 것과 같은 기준이라, 두 판정이 서로 다른 옥상을 보는 일이 없다.
        /// </summary>
        private Vector3 LandingTarget(Vector3 from, Vector3 anchor)
        {
            // 안쪽 = 붙은 벽면 법선의 반대. 옥상 면에 바로 붙었으면(법선이 위) 수평 성분이
            // 없으므로, 그때는 나에게서 앵커로 가는 방향을 안쪽으로 본다.
            Vector3 inward = -new Vector3(_anchorNormal.x, 0f, _anchorNormal.z);
            if (inward.sqrMagnitude < 0.01f)
            {
                inward = anchor - from;
                inward.y = 0f;
            }
            inward = inward.sqrMagnitude > 0.0001f ? inward.normalized : transform.forward;

            float roofY = _anchorCollider != null ? _anchorCollider.bounds.max.y : anchor.y;
            Vector3 target = anchor + inward * landingInset;
            target.y = roofY + apexClearance;
            return target;
        }

        /// <summary>
        /// 발사 후 꼭짓점까지. 몸을 발사 방향으로 돌리고, 공중 조작 차단을 유지한다.
        /// </summary>
        private void TickFlight()
        {
            // 꼭짓점(y속도가 0 이하)에 닿으면 끝낸다. 그 뒤로는 평범한 낙하라 공중 조작을
            // 돌려줘야 옥상 위에서 착지 위치를 다듬을 수 있다. 스윙·견인이 시작돼도 넘긴다.
            // 발사 직후 잠깐은 접지 신호가 남아 있을 수 있어(PlayerMovement의 점프 주석 참고)
            // 0.2초가 지난 뒤에만 착지로 본다.
            bool landed = Time.time - _launchTime > 0.2f && _movement != null && _movement.IsGrounded;
            if (_rb.linearVelocity.y <= 0f || landed || gun.IsAttached || (_zip != null && _zip.IsZipping))
            {
                EndFlight();
                return;
            }

            // 스윙·견인과 같은 이유로 매 스텝 다시 켠다 — 스위치를 나눠 쓰는 쪽이 여럿이라
            // 한 번만 켜두면 남이 끄고 간 뒤에 회복할 방법이 없다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            // 스위치가 켜져 있으면 PlayerMovement가 회전도 넘기므로, 여기서 돌린다.
            if (_launchDirection.sqrMagnitude > 0.0001f && flightTurnDegPerSec > 0f)
            {
                Quaternion target = Quaternion.LookRotation(_launchDirection.normalized, Vector3.up);
                _rb.MoveRotation(Quaternion.RotateTowards(
                    _rb.rotation, target, flightTurnDegPerSec * Time.fixedDeltaTime));
            }
        }

        /// <summary>충전을 끝낸다(발사했든 그냥 풀었든). 줄과 상태를 정리한다.</summary>
        private void EndCharge()
        {
            IsCharging = false;
            _launchQueued = false;
            _anchorTransform = null;
            _anchorCollider = null;
            Charge01 = 0f;
            if (gun != null) gun.RopeTension01 = 0f; // 추가 생성 (2026-10-19)
            ClearRope();
        }

        /// <summary>비행을 끝낸다. 공중 조작을 돌려준다.</summary>
        private void EndFlight()
        {
            IsLaunching = false;
            // 스윙·견인이 이어받았으면 그쪽이 스위치를 쥐고 있으므로 건드리지 않는다.
            bool someoneElse = (gun != null && gun.IsAttached) || (_zip != null && _zip.IsZipping);
            if (_movement != null && !someoneElse) _movement.SuspendHorizontalControl = false;
        }

        private void EndAll()
        {
            if (IsCharging) EndCharge();
            if (IsLaunching) EndFlight();
        }

        /// <summary>로프를 앵커까지 그린다. 그리기는 GrappleGun의 LineRenderer를 그대로 쓴다.</summary>
        private void SetRope(Vector3 target)
        {
            if (gun == null) return;
            gun.OverrideRopeTarget = target;
            _ownsRopeOverride = true;
        }

        /// <summary>
        /// 내가 켠 로프만 끈다. OverrideRopeTarget은 견인·던지기도 같이 쓰므로, 내 것이
        /// 아닌데 null을 넣으면 남의 줄을 지운다(WebGrabThrow에서 실제로 났던 사고).
        /// </summary>
        private void ClearRope()
        {
            if (!_ownsRopeOverride || gun == null) return;
            gun.OverrideRopeTarget = null;
            gun.RopeWobble = 0f; // 추가 생성 — 줄 떨림도 같이 끈다
            _ownsRopeOverride = false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 충전 중에 예상 궤적을 씬 뷰에 그린다(Gizmos — 유니티 내장 디버그 그리기).
        /// 노란 선 = 지금 놓으면 날아갈 길, 초록 구 = 꼭짓점 목표.
        /// 튜닝할 때 "왜 못 올라갔지"를 영상 없이 바로 보기 위한 것이다.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !IsCharging || _rb == null) return;

            Vector3 from = _rb.position;
            Vector3 anchor = AnchorPoint;
            // 수정 (2026-10-15) — 발사 방식과 같은 식(LaunchVelocity)으로 그린다.
            Vector3 v = LaunchVelocity(from, anchor, out _);

            if (_mode == LaunchMode.RoofClimb)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(LandingTarget(from, anchor), 0.5f);
            }

            // 꼭짓점까지만 그린다 — 그 뒤는 2배 중력이라 이 식이 아니다.
            float g = Mathf.Abs(Physics.gravity.y);
            float tApex = g > 0.01f ? Mathf.Max(0f, v.y / g) : 0f;
            const int Segments = 24;
            Gizmos.color = Color.yellow;
            Vector3 prev = from;
            for (int i = 1; i <= Segments; i++)
            {
                float t = tApex * i / Segments;
                Vector3 p = from + v * t + 0.5f * Physics.gravity * t * t;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
#endif
    }
}
