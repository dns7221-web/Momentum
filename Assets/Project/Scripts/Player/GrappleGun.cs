using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 갈고리 발사와 앵커 판정 — 스윙 물리의 앞단.
    ///
    /// 여기까지는 <b>물리가 없다.</b> 쏘고, 맞은 지점을 기억하고, 선을 긋는 것이 전부다.
    /// 구속을 같이 넣지 않는 이유는 나중을 위해서다 — 줄이 엉뚱한 데 붙는 것과 구속이 틀린
    /// 것은 화면에서 똑같이 "이상하다"로 보인다. 앞단이 먼저 맞아야 그 구분이 생긴다.
    ///
    /// 갈고리는 날아가지 않고 <b>즉시 붙는다.</b> 발사체를 날리면 20m를 0.33초에 가는데
    /// 그 사이 플레이어는 8m를 이동하므로 조준을 앞질러야 하는 게임이 된다. 그게 스킬이
    /// 될지 짜증이 될지는 손맛을 판정한 뒤의 문제라, 지금 단계에 지연을 넣지 않는다.
    /// </summary>
    public class GrappleGun : MonoBehaviour
    {
        [Header("사거리")]
        [Tooltip("로프 길이. 이보다 먼 곳에는 붙지 않는다. 프리셋의 ropeLength와 같은 값을 쓴다.")]
        [Min(1f)][SerializeField] private float maxRange = 22f;
        [Tooltip("갈고리가 걸리는 레이어. Ground는 반드시 빠져 있어야 한다 — " +
                 "아스팔트에 붙으면 스윙이 아니라 지면 견인이 된다.")]
        [SerializeField] private LayerMask anchorMask;

        [Tooltip("소품(드럼통·상자 등)이 걸리는 레이어. 건물과 분리해두는 이유는 아래 " +
                 "'옥상 올려붙이기'·벽면 판정이 전부 건물 지오메트리를 전제하기 때문이다 — " +
                 "소품을 맞추면 그 로직을 건너뛰고 맞은 지점에 그대로 붙는다.")]
        [SerializeField] private LayerMask propMask;

        [Header("조준 보조")]
        [Tooltip("정중앙이 빗나갔을 때 크로스헤어에서 이 각도 안까지 후보를 찾아본다. " +
                 "0이면 보조 없음 — 정확히 겨눈 것만 붙는다. 24m 앞에서 12°면 약 5m 반경이다.")]
        [Range(0f, 30f)][SerializeField] private float aimAssistAngle = 12f;

        [Tooltip("보조에 쓰는 레이 수. 많을수록 촘촘하지만 한 번 클릭에 이만큼 쏜다. " +
                 "12개면 5m 반경을 충분히 덮는다.")]
        [Range(0, 32)][SerializeField] private int aimAssistRays = 12;

        [Tooltip("이보다 가까운 앵커에는 붙지 않는다. <b>발밑 바닥을 쏘는 것을 막는 값이 " +
                 "이쪽이다.</b> 서 있는 옥상 바닥은 2~5m 거리에 잡히고, 쓸 만한 스윙 앵커는 " +
                 "15m 이상이라 거리로 깨끗하게 갈린다.")]
        [Min(0f)][SerializeField] private float minAnchorDistance = 6f;

        [Tooltip("소품 앵커에는 위 minAnchorDistance 대신 이 값을 쓴다. 건물용 값(6m)을 " +
                 "소품에도 그대로 물리면, 옥상에 흩어둔 소품 대부분이 2~6m 안에 있어 " +
                 "조준해도 안 붙는 사고가 난다 — '발밑 바닥'과 헷갈릴 소품은 없으므로 " +
                 "짧게 둬도 된다.")]
        [Min(0f)][SerializeField] private float minPropAnchorDistance = 1f;

        [Tooltip("소품을 찾는 레이는 점이 아니라 이 반지름의 구(SphereCast)로 쏜다. " +
                 "건물은 커서 점 레이로도 잘 맞지만, 드럼통 같은 소품은 화면에서 차지하는 " +
                 "면적이 작아 점으로 쏘면 스치듯 지나가기 쉽다 — 판정이 어렵다는 느낌의 " +
                 "정체가 이것이다. 건물·벽면 판정(옥상 올려붙이기 포함)은 그대로 점 레이를 " +
                 "쓴다 — 그쪽은 모서리 근처에서 구 캐스팅 결과가 애매해질 수 있어 손대지 않는다.")]
        [Min(0f)][SerializeField] private float propAssistRadius = 0.6f;

        [Header("체이스 타겟 — 방금 던진 소품을 다시 맞히기 쉽게")]
        [Tooltip("WebGrabThrow가 소품을 던지면 여기 등록해둔다. 등록된 동안은 화면 어디를 " +
                 "겨눠도 조준선이 이 반경 안에만 들어오면 맞은 것으로 쳐준다 — 실제 충돌 " +
                 "크기(콜라이더)와는 무관하게 판정만 후하게 준다. 작고 빠르게 날아가는 " +
                 "물체를 평소 소품 판정(Prop Assist Radius) 수준으로 다시 맞히라는 건 " +
                 "너무 가혹하다는 판단 — 겉모습은 그대로 작은 병이지만 '맞았다고 쳐주는 " +
                 "범위'만 훨씬 크게 잡는다.")]
        [Min(0f)][SerializeField] private float chaseTargetAssistRadius = 2.5f;

        /// <summary>
        /// 지금 쫓는 대상. <c>null</c>이면 평소대로 조준한다. WebGrabThrow가 소품을 던질 때
        /// 켜고, 붙잡히거나 시간이 지나면 끈다 — 이 값이 켜져 있는 동안만 위 반경 보조가 붙는다.
        /// </summary>
        public Rigidbody ChaseTarget { get; set; }

        [Tooltip("나보다 이 각도만큼 아래인 앵커는 버린다. <b>느슨하게 두는 것이 맞다.</b> " +
                 "이 게임에서 가장 자주 쓰는 샷은 옥상 높이에서 앞 건물 꼭대기를 쏘는 것이라 " +
                 "앙각이 0° 근처이고, 크게 튀어 오른 뒤에는 주변이 전부 나보다 아래다. " +
                 "값을 올리면 그 상황에서 웹이 통째로 안 나간다. -90이면 제한 없음.")]
        [Range(-90f, 60f)][SerializeField] private float minAnchorElevation = -25f;

        [Tooltip("바닥에 서 있을 때 옥상 윗면(roofNormalMinY 이상) 앵커에 한해 대신 쓰는 " +
                 "더 엄격한 최소 앙각. 위 minAnchorElevation을 느슨하게(-25°) 둔 것은 공중에 " +
                 "높이 뜬 채로 멀리 있는 건물 옥상을 스치듯 맞히는 상황 때문인데, 그 느슨한 " +
                 "값이 '내가 지금 서 있는 옥상의 먼 지점'에도 그대로 통과돼버려 — 거리 " +
                 "필터(minAnchorDistance)는 가까운 발밑만 거르지 6m 넘게 떨어진 같은 옥상 " +
                 "지점은 못 거른다 — 좌클릭하자마자 그 지점으로 낮고 빠르게 미끄러지듯 끌려가는 " +
                 "사고가 났다(녹화 영상으로 확인). 공중에 떠 있을 때는 원래 느슨한 값을 그대로 " +
                 "쓰고, 발이 땅에 붙어 있을 때만 옥상 앵커에 이 엄격한 값을 쓴다.")]
        [Range(-90f, 60f)][SerializeField] private float minGroundedRoofAnchorElevation = 20f;

        [Tooltip("나보다 이 각도보다 <b>더 위</b>인 앵커는 버린다 — 위 두 값과 반대 방향의 " +
                 "상한이다.\n\n" +
                 "머리 바로 위에 붙으면 로프가 수직이 되고, 수직 로프에는 접선 방향이 없다. " +
                 "붙는 순간부터 흔들리지 못하고 다림추처럼 매달린 채 팔다리만 움직이는 " +
                 "'바람 인형' 상태가 되는데, 이건 자세 코드가 고장 난 게 아니라 " +
                 "up = (앵커 - 나)가 (0,1,0)이 된 정직한 결과라 SwingConstraint 쪽에서는 " +
                 "고칠 수가 없다. 애초에 그런 앵커를 안 주는 것이 유일한 수정이다.\n\n" +
                 "값을 <b>내리면</b> 골목에서 바로 위 옥상을 못 잡아 웹이 안 나가고, " +
                 "<b>올리면</b> 수직 매달림이 돌아온다. 90이면 제한 없음(예전 동작).")]
        [Range(30f, 90f)][SerializeField] private float maxAnchorElevation = 70f;

        /// <summary>앙각 상한을 적용하지 않을 때 쓰는 값. 소품은 머리 위에 있어도 잡는 것이
        /// 맞아서(스윙이 아니라 끌어오기라 접선 방향이 필요 없다) 이 값을 넘긴다.</summary>
        private const float NoElevationLimit = 90f;

        [Header("옥상 올려붙이기 — 벽면 앵커는 반드시 벽에 처박힌다")]
        [Tooltip("벽면을 겨눴을 때 부착점을 그 위 옥상 모서리로 올린다.\n\n" +
                 "끄면 겨눈 벽면에 그대로 붙는데, 그러면 진자 최저점이 정확히 그 벽면이 되어 " +
                 "아래로 흔들릴 때마다 건물을 긁는다. 조준 실수가 아니라 궤도 자체가 그렇다.")]
        [SerializeField] private bool preferRoofAnchors = true;

        [Tooltip("법선의 y가 이 값 이상이면 윗면(옥상·선반)으로 본다. 벽은 0, 옥상은 1이라 " +
                 "가운데 아무 값이나 들어가면 된다.")]
        [Range(0f, 1f)][SerializeField] private float roofNormalMinY = 0.5f;

        [Tooltip("옥상으로 올린 부착점을 모서리에서 <b>바깥으로</b> 얼마나 뺄지(m).\n\n" +
                 "안쪽이 아니라 바깥인 것이 요점이다. 진자 최저점은 앵커 바로 아래인데, " +
                 "부착점이 옥상 안쪽에 있으면 그 아래가 곧 건물이라 매달리는 순간 벽을 향한다. " +
                 "모서리 바깥으로 빼야 줄이 파사드를 따라 허공에 드리운다.")]
        [Min(0f)][SerializeField] private float roofEdgeOffset = 1.5f;

        [Tooltip("진자 최저점이 비어 있는지 검사할 반경(m). 이보다 가까이 건물이 있으면 " +
                 "부착점을 더 바깥으로 밀어낸다. 플레이어 캡슐 반지름(0.25)보다 넉넉해야 " +
                 "스치지 않는다.")]
        [Min(0.1f)][SerializeField] private float hangClearRadius = 1f;

        [Tooltip("옥상으로 못 올렸을 때(사거리 밖 등) 벽면에라도 붙을지. " +
                 "끄면 그 상황에서 웹이 아예 안 나간다 — 붙는 곳이 나빠도 안 나가는 것보단 낫다.")]
        [SerializeField] private bool allowWallAnchors = true;

        [Tooltip("위 벽면 붙기를 허용하더라도, 매달렸을 때 진자 최저점이 건물 속인 벽면은 " +
                 "그래도 거부한다.\n\n" +
                 "옥상으로 올린 부착점은 IsHangBlocked를 통과해야 채택되는데, 올리기에 " +
                 "실패해 생 벽면으로 떨어지는 경로만 그 검사를 건너뛰고 있었다 — 벽을 " +
                 "위해 만든 검사가 정작 벽에 그대로 붙는 경우에만 안 걸렸던 셈이다.\n\n" +
                 "끄면 예전처럼 '나쁜 곳이라도 붙는다'가 된다. 켜 두면 그 상황에서 웹이 " +
                 "안 나가는 대신, 벽에 납작하게 붙어 끌려가는 그림이 사라진다.")]
        [SerializeField] private bool requireClearHangOnWallAnchors = true;

        [Header("참조")]
        [Tooltip("로프가 나가는 지점. 권총 프리팹 안의 Muzzle. " +
                 "물리에는 쓰지 않고 선을 그릴 때만 쓴다.")]
        [SerializeField] private Transform muzzle = null;
        [Tooltip("총 모델의 보이는 부분(메시). 비워두면 Muzzle의 부모를 그대로 쓴다.\n\n" +
                 "웹으로 태그하는 게 아니라 손으로 직접 잡아 던지는 동작(WebGrabThrow의 " +
                 "E키 던지기)에서는 총이 화면에 있는 게 어색해서, 그 순간만 잠깐 꺼둔다.")]
        [SerializeField] private GameObject visualRoot = null;
        [Tooltip("로프를 그릴 LineRenderer. 붙어 있는 동안에만 켜진다.")]
        [SerializeField] private LineRenderer rope = null;
        [Tooltip("조준 기준 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private Camera aimCamera = null;
        [Tooltip("발사 애니메이션(GrappleFire 레이어)을 켤 Animator. 비워두면 자식에서 찾는다.")]
        [SerializeField] private Animator animator = null;

        [Header("발사 애니메이션")]
        [Tooltip("발사 순간부터 GrappleFire 레이어 가중치를 1로 유지하는 시간(초). " +
                 "클립 길이보다 조금 짧게 두면 자연스럽게 겹쳐 끊긴다 — 정확히 맞출 필요는 없다.")]
        [Min(0f)][SerializeField] private float fireLayerHoldTime = 0.4f;

        [Header("소품 잡기 로프 처짐 — WebGrabThrow가 소품을 물고 있는 동안(IsPropAnchor)만")]
        [Tooltip("건물 스윙 로프는 팽팽한 직선 그대로 두고, 소품을 잡고 있을 때만 이 " +
                 "세그먼트 수로 나눠 곡선을 그린다. 스윙 로프는 실제로 팽팽하니 직선이 " +
                 "맞고, 잡고 있는 소품은 그냥 매달려 있을 뿐이라 처져야 자연스럽다.")]
        [Min(2)][SerializeField] private int propRopeSegments = 12;

        [Tooltip("소품 로프 중앙이 아래로 처지는 정도 — 로프 길이에 대한 비율.")]
        [Range(0f, 0.5f)][SerializeField] private float propRopeSagFraction = 0.12f;

        [Tooltip("내 이동 속도 중 로프 방향에 수직인 성분이 로프를 옆으로 얼마나 미는지. " +
                 "0이면 처짐만 있고 움직임에 반응하지 않는다.")]
        [Min(0f)][SerializeField] private float propRopeSwayPerSpeed = 0.05f;

        [Tooltip("로프 쏠림이 속도 변화를 얼마나 빨리 따라가는지(초당 보간 계수). " +
                 "낮추면 관성이 남아 더 물렁하게, 올리면 즉각적으로 반응한다.")]
        [Min(0.1f)][SerializeField] private float propRopeSwaySmoothing = 6f;

        [Header("입력 (InputSystem_Actions.inputactions의 Player 맵에서 드래그)")]
        [SerializeField] private InputActionReference grappleAction = null;

        [Tooltip("버튼을 누르고 있는 동안 계속 다시 쏴본다. 빗나갔거나 벽에 박아 끊겼을 때 " +
                 "손을 뗐다 다시 누르지 않아도 된다.\n\n" +
                 "끄면 한 번 놓친 순간 조작이 멈춘다 — 떨어지는 중에 그걸 알아채고 다시 " +
                 "누르는 시간이 곧 추락이다.")]
        [SerializeField] private bool retryWhileHeld = true;

        [Tooltip("다시 쏘는 간격(초). 0에 가까우면 매 프레임 레이를 13발씩 쏘고, " +
                 "방금 박은 자리에 그대로 다시 붙는다.")]
        [Min(0.02f)][SerializeField] private float retryInterval = 0.12f;

        /// <summary>갈고리가 붙어 있는가. 구속이 붙을 때 이 값이 그대로 스위치가 된다.</summary>
        public bool IsAttached { get; private set; }

        /// <summary>
        /// 총구. 로프의 시작점이자 <see cref="GrappleArmAim"/>이 조준 회전을 계산하는 기준이다.
        ///
        /// 참조를 두 곳에 따로 꽂게 하지 않으려고 열어둔다 — 총을 바꾸면 여기 한 곳만 바꾼다.
        /// </summary>
        public Transform Muzzle => muzzle;

        /// <summary>
        /// 앵커의 현재 월드 좌표.
        ///
        /// 좌표를 저장해두지 않고 매번 다시 계산하는 이유는 앵커가 움직일 수 있기 때문이다.
        /// 지금은 고정된 건물뿐이라 차이가 없지만, 전투 단계에서 드럼통에 붙는 순간
        /// 월드 좌표로 굳혀둔 앵커는 물체를 두고 허공에 남는다. 그때 고치면 이미 맞춰둔
        /// 손맛 값들이 전부 흔들리므로 처음부터 이 모양으로 들고 있는다.
        /// </summary>
        public Vector3 AnchorPoint =>
            _anchorTransform != null
                ? _anchorTransform.TransformPoint(_anchorLocalPoint)
                : Vector3.zero;

        /// <summary>
        /// 앵커가 얹힌 강체. 고정된 건물이면 <c>null</c>이다.
        ///
        /// 구속이 붙을 때 이 자리가 "두 강체 중 반대쪽"이 된다. null을 역질량 0으로 읽으면
        /// 건물은 스윙, 가벼운 물체는 끌어오기, 무거운 물체는 내가 끌려가기가 분기 없이 나온다.
        /// </summary>
        public Rigidbody AnchorBody { get; private set; }

        /// <summary>
        /// 지금 붙은 앵커가 소품(<see cref="propMask"/>)인가.
        ///
        /// <see cref="SwingConstraint"/>가 이 값으로 스윙 시작을 가른다. 소품을 잡는 것은
        /// 스윙을 도우려는 게 아니라 전투(끌어오기·던지기)를 보여주려는 것이라, 붙는
        /// 순간 펜듈럼 물리가 같이 시작되면 안 된다 — 건물이든 드럼통이든 구분 없이
        /// 스윙부터 걸리는 것이 애초에 이 플래그가 생긴 이유다.
        /// </summary>
        public bool IsPropAnchor { get; private set; }

        /// <summary>
        /// 부착이 막 풀렸다는 신호. <see cref="Detach"/> 끝에서 한 번 울린다.
        ///
        /// 버튼을 놓았든, 충돌로 <see cref="ForceDetach"/>가 불렸든, 컴포넌트가 꺼졌든
        /// 전부 이 하나로 온다. <see cref="WebGrabThrow"/>가 "쥐고 있던 걸 지금 던져야
        /// 하는가"를 판단하는 신호로 쓴다 — 입력 액션을 따로 구독하지 않고 여기 하나에
        /// 걸어두면, 부착이 끊기는 경로가 나중에 하나 더 늘어도 자동으로 같이 반영된다.
        /// </summary>
        public event Action Detached;

        /// <summary>
        /// 법선의 y가 이 값 이상이면 "평평한(옥상 같은) 면"으로 본다.
        ///
        /// <see cref="GrappleZip"/>이 "같은 옥상 바닥을 다시 잡는" 것을 걸러낼 때 이
        /// 기준을 그대로 빌려 쓴다 — 벽이냐 바닥이냐를 가르는 기준이 두 군데서 서로
        /// 다르면 언젠가 어긋난다.
        /// </summary>
        public float RoofNormalMinY => roofNormalMinY;

        /// <summary>
        /// 로프를 잠시 다른 목표로 그린다. <c>null</c>이면 평소대로 앵커까지 그린다.
        ///
        /// <see cref="GrappleZip"/>이 견인하는 동안 쓴다. 줄을 그리는 책임은 여기 하나로
        /// 두는 편이 낫다 — LineRenderer를 두 곳에서 만지면 어느 쪽이 껐는지 못 가리는
        /// 순간이 반드시 온다.
        /// </summary>
        public Vector3? OverrideRopeTarget { get; set; }

        /// <summary>최저점이 막혔을 때 부착점을 바깥으로 더 밀어보는 횟수와 한 걸음의 크기(m).</summary>
        private const int ClearanceSteps = 3;
        private const float ClearanceStep = 1f;

        /// <summary>발사 버튼을 쥐고 있는가. 강제로 끊긴 뒤 다시 쏠지를 이 값이 정한다.</summary>
        private bool _holding;
        private float _nextRetryTime;

        private static readonly int FireHash = Animator.StringToHash("Fire");
        private int _fireLayerIndex = -1;
        private float _fireWeightUntil;

        /// <summary>맞은 콜라이더의 트랜스폼. 부착점을 이것의 로컬 좌표로 들고 있는다.</summary>
        private Transform _anchorTransform;
        private Vector3 _anchorLocalPoint;

        /// <summary>소품 로프 처짐 계산에 쓰는 내 몸의 Rigidbody. 속도의 로프 수직 성분을 읽는다.</summary>
        private Rigidbody _rb;

        /// <summary>바닥에 서 있는지 본다 — 옥상 앵커에 <see cref="minGroundedRoofAnchorElevation"/>을
        /// 쓸지 판단하는 데 쓴다.</summary>
        private PlayerMovement _movement;

        /// <summary>소품 로프의 옆 쏠림 — 매 프레임 목표값으로 서서히 보간해 관성을 흉내낸다.</summary>
        private Vector3 _propRopeSway;

        /// <summary>컴포넌트를 처음 붙일 때 레이어 마스크를 채워둔다. 빈 마스크는 아무것도 안 맞는다.</summary>
        private void Reset()
        {
            anchorMask = LayerMask.GetMask(Level.CityGenerator.AnchorLayerName);
            propMask = LayerMask.GetMask(Level.CityGenerator.GrabbableLayerName);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            if (aimCamera == null) aimCamera = Camera.main;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (visualRoot == null && muzzle != null) visualRoot = muzzle.parent != null ? muzzle.parent.gameObject : null;

            // 레이어가 없으면(GrappleFire 클립을 아직 안 채웠으면) -1로 남고,
            // 아래 Update의 SetLayerWeight는 -1일 때 아무 것도 안 하게 건너뛴다.
            if (animator != null) _fireLayerIndex = animator.GetLayerIndex("GrappleFire");

            if (muzzle == null || rope == null)
            {
                Debug.LogWarning("[GrappleGun] Muzzle 또는 LineRenderer가 비어 있다. " +
                                 "판정은 되지만 로프가 그려지지 않는다.", this);
            }

            if (anchorMask == 0)
            {
                Debug.LogWarning("[GrappleGun] 앵커 레이어 마스크가 비어 있어 아무 데도 붙지 않는다. " +
                                 $"'{Level.CityGenerator.AnchorLayerName}'을 선택할 것.", this);
            }

            if (propMask == 0)
            {
                Debug.LogWarning("[GrappleGun] 소품 레이어 마스크가 비어 있어 소품에는 안 붙는다. " +
                                 $"'{Level.CityGenerator.GrabbableLayerName}'을 선택할 것.", this);
            }

            SetRopeVisible(false);
        }

        // ─── 입력 ───
        //
        // 액션 하나로 발사와 릴리즈를 모두 처리한다. Button 액션에 Interaction을 걸지 않으면
        // 누를 때 performed, 뗄 때 canceled가 오기 때문이다. 릴리즈 타이밍이 이 게임의 핵심
        // 조작이므로 토글이 아니라 "누르고 있는 동안 붙어 있다"가 맞다.

        private void OnEnable()
        {
            if (grappleAction == null) return;
            grappleAction.action.Enable();
            grappleAction.action.performed += OnGrapplePerformed;
            grappleAction.action.canceled += OnGrappleCanceled;
        }

        private void OnDisable()
        {
            if (grappleAction != null)
            {
                // 구독 해제를 빼먹으면 에디터에서 플레이를 반복할 때마다 콜백이 쌓여
                // 한 번 클릭에 갈고리가 여러 번 발사된다.
                grappleAction.action.performed -= OnGrapplePerformed;
                grappleAction.action.canceled -= OnGrappleCanceled;
                grappleAction.action.Disable();
            }

            // 꺼진 채로 쥐고 있던 것으로 남으면, 다시 켜지는 순간 누르지도 않았는데 발사된다.
            _holding = false;
            Detach();
        }

        private void OnGrapplePerformed(InputAction.CallbackContext ctx)
        {
            _holding = true;
            FireAnimation();
            TryAttach();
        }

        /// <summary>
        /// 쏘는 순간 팔 애니메이션을 튕긴다. <see cref="TryAttach"/>와 분리한 이유는,
        /// 겨눈 게 안 맞아도(하늘·사거리 밖) <b>총은 들어 올려 쏜다</b>는 사실은 그대로라서다.
        ///
        /// <see cref="Update"/>의 재시도 루프가 아니라 여기(버튼을 처음 누른 순간)에서만
        /// 부르는 것도 의도적이다. 재시도는 0.12초마다 도는데, 여기서도 같이 부르면
        /// 버튼을 쥐고 허공을 겨눈 채로 있는 동안 애니메이션이 연달아 재생돼 팔이
        /// 파르르 떠는 것처럼 보인다 — 발사 제스처는 누른 순간 한 번이면 충분하다.
        /// </summary>
        private void FireAnimation()
        {
            if (animator == null) return;
            animator.SetTrigger(FireHash);
            _fireWeightUntil = Time.time + fireLayerHoldTime;
        }

        private void OnGrappleCanceled(InputAction.CallbackContext ctx)
        {
            _holding = false;
            Detach();
        }

        /// <summary>
        /// 누르고 있는 동안 계속 다시 쏴본다.
        ///
        /// <b>이것이 없으면 한 번 놓친 순간 조작이 통째로 멈춘다.</b> 눌러도 안 붙었거나
        /// (사거리 밖·하늘) 벽에 박아 강제로 끊겼을 때, 버튼을 쥐고 있어도 아무 일이 안
        /// 일어나므로 플레이어는 손을 뗐다 다시 눌러야 한다. 초당 20m로 떨어지는 중에
        /// 그걸 알아채고 다시 누르는 데 드는 시간이 곧 추락이다.
        ///
        /// 실력을 깎는 것도 아니다. 어디를 겨눌지는 여전히 플레이어가 정하고, 이 코드는
        /// <b>같은 판정을 다시 한 번 돌릴 뿐</b>이다. 조준이 나쁘면 여전히 안 붙는다.
        /// 바뀌는 것은 "쏠 수 있는 순간"이 한 프레임에서 <b>누르고 있는 동안 전체</b>로
        /// 늘어나는 것뿐이고, 스파이더맨 계열이 전부 이 모양이다.
        ///
        /// 간격을 두는 이유는 두 가지다 — 매 프레임 레이를 13발씩 쏘지 않기 위해서,
        /// 그리고 방금 박은 그 자리에 같은 프레임에 다시 붙는 것을 막기 위해서.
        /// </summary>
        private void Update()
        {
            UpdateFireLayerWeight();

            if (!retryWhileHeld || !_holding || IsAttached) return;
            if (Time.time < _nextRetryTime) return;

            _nextRetryTime = Time.time + retryInterval;
            TryAttach();
        }

        /// <summary>
        /// GrappleFire 레이어 가중치를 켰다 끈다. 위 재시도 분기와 달리 매 프레임
        /// 무조건 돌아야 한다 — 안 그러면 <c>IsAttached</c>가 되는 순간(스윙 시작)
        /// 이 검사가 통째로 건너뛰어져 발사 애니메이션이 켜진 채로 굳는다.
        /// </summary>
        private void UpdateFireLayerWeight()
        {
            if (animator == null || _fireLayerIndex < 0) return;
            animator.SetLayerWeight(_fireLayerIndex, Time.time < _fireWeightUntil ? 1f : 0f);
        }

        // ─── 판정 ───

        /// <summary>
        /// 화면 중앙에서 레이를 쏘아 앵커를 찾는다.
        ///
        /// 총구가 아니라 <b>카메라</b>에서 쏜다. 조준은 크로스헤어로 하는 것이고 총은 손에
        /// 들려 흔들리는 물건이라, 총구를 기준으로 삼으면 크로스헤어가 가리키는 곳과 실제로
        /// 맞는 곳이 어긋난다. 총구는 선을 그릴 때만 쓴다.
        /// </summary>
        /// <summary>
        /// 크로스헤어가 가리키는 앵커를 찾는다. 붙이지는 않는다.
        ///
        /// <c>public</c>인 이유는 <see cref="GrappleZip"/>이 <b>같은</b> 판정을 써야 하기
        /// 때문이다. 레이캐스트를 두 벌 두면 사거리·레이어 마스크·카메라 보정이 언젠가
        /// 반드시 어긋나고, 그때 "왜 스윙은 붙는데 견인은 안 붙지"를 못 가린다.
        /// </summary>
        public bool TryFindAnchor(out RaycastHit hit) => TryFindAnchor(out hit, includeProps: true);

        /// <summary>
        /// <paramref name="includeProps"/>가 <c>false</c>면 소품(<see cref="propMask"/>)은
        /// 아예 안 보고 건물만 찾는다.
        ///
        /// <see cref="GrappleZip"/>이 이걸 필요로 한다. 견인은 "옥상에서 다음 옥상으로
        /// 점프"하는 동작인데, 프롭까지 목표로 잡히면 옥상 위에 서서 우클릭만 해도 바로
        /// 옆 드럼통·에어컨 실외기로 순간 견인되며 Zip 애니메이션이 튀어나온다 — 붙기만
        /// 하고 안 당기는 <see cref="WebGrabThrow"/>와 다르게, 견인은 <b>진짜로 그 자리까지
        /// 이동</b>하므로 이 사고가 훨씬 크게 보인다.
        /// </summary>
        public bool TryFindAnchor(out RaycastHit hit, bool includeProps)
        {
            hit = default;
            if (aimCamera == null) return false;

            // 체이스 타겟이 켜져 있으면 다른 무엇보다 먼저 본다 — 날아가는 병을 다시
            // 맞히는 게 이번 샷의 목적이라면, 화면 어디를 겨눴든 그 병이 근처에 있으면
            // 그걸 우선한다.
            if (ChaseTarget != null && TryFindChaseTarget(out hit)) return true;

            // 사거리는 플레이어 기준인데 레이는 뒤에 있는 카메라에서 나간다. 그 차이만큼
            // 더 쏘고, 맞은 뒤에 플레이어와의 실제 거리로 다시 거른다. 카메라 거리를 빼먹으면
            // 카메라를 당길수록 사거리가 줄어드는 이상한 규칙이 생긴다.
            Vector3 origin = aimCamera.transform.position;
            float distance = maxRange + Vector3.Distance(origin, transform.position);
            Vector3 center = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)).direction;

            // 정중앙을 먼저 본다. 겨눈 그대로 붙는 경험이 기본이어야 조준이 조작으로 남는다.
            if (CastAnchor(origin, center, distance, includeProps, out hit)) return true;

            if (aimAssistAngle <= 0f || aimAssistRays <= 0) return false;

            // 빗나갔을 때만 부채꼴로 흩어 쏜다. 황금각 나선이라 겹치지 않고 고르게 퍼지고,
            // 반지름이 i에 따라 <b>단조 증가</b>하므로 <b>처음 맞는 것이 곧 크로스헤어에
            // 가장 가까운 것</b>이다 — 점수를 매겨 고를 필요가 없다.
            Vector3 up = aimCamera.transform.up;
            for (int i = 0; i < aimAssistRays; i++)
            {
                float t = (i + 0.5f) / aimAssistRays;
                float radius = Mathf.Sqrt(t) * aimAssistAngle;   // √ 를 씌워야 면적이 고르다
                float spin = i * 137.50776f;                     // 황금각(도)

                Vector3 direction = Quaternion.AngleAxis(spin, center)
                                  * (Quaternion.AngleAxis(radius, up) * center);

                if (CastAnchor(origin, direction, distance, includeProps, out hit)) return true;
            }

            return false;
        }

        /// <summary>
        /// 크로스헤어 주변에서 <b>소품만</b> 찾는다. 건물은 아예 안 본다.
        ///
        /// <see cref="WebGrabThrow"/>의 E키 던지기가 이걸 쓴다. 그 동작은 애초에 건물과
        /// 무관하므로(웹을 걸어 스윙하는 게 아니라 손으로 소품을 잡아 던지는 것), 건물까지
        /// 같이 찾는 <see cref="TryFindAnchor(out RaycastHit)"/>를 쓰고 나중에 걸러낼
        /// 이유가 없다 — 앞단부터 소품만 보게 좁혀둔다. 정중앙 → 빗나가면 부채꼴, 판정은
        /// <see cref="CastPropAnchor"/>(구 캐스팅)로 나머지 소품 탐색과 완전히 같다.
        /// </summary>
        public bool TryFindProp(out RaycastHit hit)
        {
            hit = default;
            if (aimCamera == null) return false;

            Vector3 origin = aimCamera.transform.position;
            float distance = maxRange + Vector3.Distance(origin, transform.position);
            Vector3 center = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)).direction;

            if (CastPropAnchor(origin, center, distance, out hit)) return true;

            if (aimAssistAngle <= 0f || aimAssistRays <= 0) return false;

            Vector3 up = aimCamera.transform.up;
            for (int i = 0; i < aimAssistRays; i++)
            {
                float t = (i + 0.5f) / aimAssistRays;
                float radius = Mathf.Sqrt(t) * aimAssistAngle;
                float spin = i * 137.50776f;

                Vector3 direction = Quaternion.AngleAxis(spin, center)
                                  * (Quaternion.AngleAxis(radius, up) * center);

                if (CastPropAnchor(origin, direction, distance, out hit)) return true;
            }

            return false;
        }

        /// <summary>
        /// 체이스 타겟이 조준선 근처에 있는지 본다.
        ///
        /// <see cref="CastPropAnchor"/>와 같은 도구(<c>Physics.SphereCast</c>)를 그대로
        /// 쓴다 — 손으로 벡터 계산을 짜지 않는다. 판정을 후하게 주는 문제는 이미
        /// 유니티 물리 쿼리(구 캐스팅)로 풀어놨으니, 여기서 또 다른 방식을 새로 짤
        /// 이유가 없다. 반지름만 <see cref="chaseTargetAssistRadius"/>로 훨씬 크게 줄 뿐,
        /// 나머지(레이어 마스크, 막히면 실패하는 것)는 전부 같은 규칙이다.
        /// </summary>
        private bool TryFindChaseTarget(out RaycastHit hit)
        {
            hit = default;

            Vector3 origin = aimCamera.transform.position;
            Vector3 direction = aimCamera.transform.forward;
            float range = maxRange + Vector3.Distance(origin, transform.position);

            if (!Physics.SphereCast(origin, chaseTargetAssistRadius, direction, out hit, range,
                                    propMask, QueryTriggerInteraction.Ignore))
                return false;

            return hit.rigidbody == ChaseTarget;
        }

        /// <summary>
        /// 레이 하나를 쏘고 사거리·고도까지 전부 통과했는지 본다.
        ///
        /// 소품과 건물을 <b>따로</b> 쏜다. 소품을 먼저 <see cref="CastPropAnchor"/>(구 캐스팅)로
        /// 찾아보고, 없으면 건물을 예전처럼 점 레이로 찾는다. 하나의 레이로 한 번에 처리하지
        /// 않는 이유는 두 판정의 성격이 달라서다 — 소품은 두께를 줘서 후하게, 건물은 옥상
        /// 올려붙이기·벽면 판정이 정확한 법선·지점을 요구해서 점 그대로 둬야 한다.
        /// </summary>
        private bool CastAnchor(Vector3 origin, Vector3 direction, float distance, bool includeProps, out RaycastHit hit)
        {
            if (includeProps && CastPropAnchor(origin, direction, distance, out hit))
                return true;

            if (!Physics.Raycast(origin, direction, out hit, distance, anchorMask,
                                 QueryTriggerInteraction.Ignore))
                return false;

            // 윗면을 맞았으면 그대로 쓴다. 발이 땅에 붙어 있으면 더 엄격한 앙각을 쓴다 —
            // 공중 값(minAnchorElevation)은 일부러 느슨해서 "내가 서 있는 옥상의 먼 지점"도
            // 통과시켜버린다(위 minGroundedRoofAnchorElevation 설명 참고).
            if (hit.normal.y >= roofNormalMinY)
            {
                float minElevation = (_movement != null && _movement.IsGrounded)
                    ? minGroundedRoofAnchorElevation : minAnchorElevation;
                return IsUsableAnchor(hit.point, minAnchorDistance, minElevation);
            }
            if (TryLiftToRoof(ref hit)) return true;

            // 옥상으로 못 올린 생 벽면. TryLiftToRoof는 후보마다 IsHangBlocked를 보는데
            // 여기로 떨어지면 그 검사를 통째로 건너뛰고 있었다 — 벽 앵커는 반드시 벽에
            // 처박힌다고 바로 위에 적어두고, 정작 벽에 그대로 붙는 경로만 안 보던 것이다.
            if (!allowWallAnchors) return false;
            if (requireClearHangOnWallAnchors && IsHangBlocked(hit.point)) return false;

            return IsUsableAnchor(hit.point, minAnchorDistance);
        }

        /// <summary>
        /// 소품을 찾는다 — 점 레이가 아니라 <see cref="propAssistRadius"/> 반지름의
        /// SphereCast다. 소품은 옥상 바닥과 달리 헷갈릴 상대가 없어 판정을 후하게 줘도
        /// 안전하다 — "옥상 위에서 발밑을 잘못 잡는" 사고는 건물 쪽(점 레이) 얘기다.
        ///
        /// <b>시작점을 반지름만큼 뒤로 물려서 쏜다.</b> 유니티 SphereCast는 시작점에서 이미
        /// 콜라이더와 겹쳐 있으면 그 콜라이더를 감지하지 못하는 알려진 한계가 있다 — 화물
        /// 컨테이너처럼 큰 평면에 바싹 붙어 정면을 똑바로 겨누면 카메라가 이 반경 안에서
        /// 이미 표면과 겹쳐 있어 캐스트가 통째로 실패한다(모서리처럼 실제로 더 떨어진
        /// 지점을 겨눌 땐 안 겪는 문제라 "가까이서 정면은 안 먹히고 구석은 되는" 것처럼
        /// 보였다). 뒤로 물려서 쏘면 겹친 채로 시작하는 상황 자체가 없어지고, 캐스트가
        /// 진행하며 카메라 실제 위치를 지나 표면에 닿는 순간 정상 히트로 잡힌다.
        /// </summary>
        private bool CastPropAnchor(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            float backOff = propAssistRadius + 0.05f;
            Vector3 pulledOrigin = origin - direction * backOff;

            if (!Physics.SphereCast(pulledOrigin, propAssistRadius, direction, out hit, distance + backOff,
                                    propMask, QueryTriggerInteraction.Ignore))
                return false;

            // 소품에는 앙각 상한을 걸지 않는다. 상한은 "수직 로프에는 접선 방향이 없다"는
            // 스윙 사정이고, 소품은 매달리는 게 아니라 끌어오는 것이라 머리 위에 있어도 된다.
            return IsUsableAnchor(hit.point, minPropAnchorDistance, minAnchorElevation,
                                  NoElevationLimit);
        }

        private bool IsPropLayer(int layer) => (propMask.value & (1 << layer)) != 0;

        /// <summary>
        /// 벽면 앵커를 그 위 옥상 모서리로 올린다.
        ///
        /// <b>수직 벽면에 매달리면 진자 최저점이 정확히 그 벽면이다.</b> 앵커에서 로프 길이만큼
        /// 곧장 아래가 최저점인데, 앵커가 벽에 있으면 그 아래도 벽이기 때문이다. 즉 벽 앵커는
        /// 조준 실수가 아니라 <b>반드시 건물에 처박히는 궤도</b>이고, 값을 조절해 피할 수 있는
        /// 종류가 아니다. 건물이 70m가 되면서 사거리 안이 온통 벽면이 되어 이게 기본값이 됐다.
        ///
        /// 그렇다고 벽면을 거부하면 README에 적힌 그 실패로 돌아간다 — 고도 제한을 10°로
        /// 조였다가 "웹이 통째로 안 나가는" 상태를 만들었던 것. <b>거부하는 대신 옮긴다.</b>
        /// 겨눈 건물은 그대로 두고 부착점만 그 수직선 위 옥상으로 올리면, 플레이어가 고른
        /// 대상은 바뀌지 않으면서 궤도만 건물 바깥으로 나온다.
        ///
        /// 옥상 높이는 맞은 콜라이더의 바운즈 윗면에서 그냥 읽는다. 겹마다 BoxCollider가
        /// 하나씩이라 그 값이 곧 그 겹의 옥상이고, 아래 겹을 겨누면 셋백 선반에 걸린다 —
        /// 레이를 한 번 더 쏠 필요가 없다.
        /// </summary>
        private bool TryLiftToRoof(ref RaycastHit hit)
        {
            if (!preferRoofAnchors || hit.collider == null) return false;

            Vector3 outward = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (outward.sqrMagnitude < 0.0001f) return false;
            outward.Normalize();

            Vector3 top = new Vector3(hit.point.x, hit.collider.bounds.max.y, hit.point.z);

            // 한 번에 못 비우면 조금씩 더 바깥으로 밀어본다. 셋백 때문에 한 겹 바깥으로
            // 빼도 그 아래 넓은 겹이 여전히 걸리는 경우가 있어서, 될 때까지 물러난다.
            for (int step = 0; step <= ClearanceSteps; step++)
            {
                Vector3 candidate = top + outward * (roofEdgeOffset + step * ClearanceStep);

                // 멀어질수록 사거리 밖으로 나간다. 거기서 끊는 것이 맞다 — 더 밀어봐야 못 붙는다.
                if (!IsUsableAnchor(candidate, minAnchorDistance)) return false;
                if (IsHangBlocked(candidate)) continue;

                hit.point = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 이 앵커에 매달리면 진자 최저점이 건물 속인가.
        ///
        /// 스윙이 벽에 처박히느냐를 결정하는 것은 조준 각도가 아니라 이 한 점이다.
        /// 최저점은 앵커에서 로프 길이만큼 곧장 아래이고, 로프 길이는 붙는 순간의 거리다
        /// (<c>SwingConstraint.BeginSwing</c>). 그러니 붙기 <b>전에</b> 그 지점이 비어 있는지
        /// 보면 "이 웹을 쏘면 처박힌다"를 미리 알 수 있다.
        ///
        /// 아치 전체가 아니라 최저점 하나만 본다. 가장 깊이 파고드는 지점이라 여기가 비면
        /// 나머지도 대체로 비고, 검사 하나로 끝나 조준할 때마다 돌려도 부담이 없다.
        /// </summary>
        private bool IsHangBlocked(Vector3 anchor)
        {
            float rope = Vector3.Distance(anchor, transform.position);
            Vector3 bottom = anchor + Vector3.down * rope;
            return Physics.CheckSphere(bottom, hangClearRadius, anchorMask,
                                       QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 붙을 만한 앵커인가 — 거리와 고도 둘로 거른다.
        ///
        /// <b>거리가 주력이다.</b> 막으려는 것은 "서 있는 옥상 바닥을 쏘는 것"인데, 그건
        /// 2~5m 앞에 잡히고 쓸 만한 스윙 앵커는 15m 이상이라 거리로 깨끗하게 갈린다.
        ///
        /// <b>고도는 보조이고, 느슨해야 한다.</b> 처음에 이걸 10°로 잡았다가 웹이 통째로
        /// 안 나가는 상태를 만들었다. 이 게임에서 가장 자주 쓰는 샷은 옥상 높이에서 앞
        /// 건물 꼭대기를 쏘는 것이라 앙각이 0° 근처이고, 릴리즈로 크게 솟은 뒤에는 주변이
        /// <b>전부 나보다 아래</b>다. 각도로는 "발밑 바닥"과 "앞 건물 꼭대기"를 가를 수 없다.
        /// </summary>
        private bool IsUsableAnchor(Vector3 point, float minDistance) =>
            IsUsableAnchor(point, minDistance, minAnchorElevation);

        private bool IsUsableAnchor(Vector3 point, float minDistance, float minElevation) =>
            IsUsableAnchor(point, minDistance, minElevation, maxAnchorElevation);

        private bool IsUsableAnchor(Vector3 point, float minDistance, float minElevation,
                                    float maxElevation)
        {
            Vector3 toAnchor = point - transform.position;

            if (toAnchor.magnitude > maxRange) return false;
            if (toAnchor.magnitude < minDistance) return false;

            // Vector3.Angle은 위 방향과의 사잇각(0~180)이라 90에서 빼면 그대로 앙각이 된다.
            // 수평 거리가 0인 "바로 머리 위"에서도 정의되므로, 각도가 정의되지 않는 경우를
            // 손으로 갈라주던 분기가 필요 없다 — 그 분기가 하던 일(머리 위는 통과)이
            // 정확히 지금 막으려는 것이기도 했다.
            float elevation = 90f - Vector3.Angle(toAnchor, Vector3.up);

            return elevation >= minElevation && elevation <= maxElevation;
        }

        private void TryAttach()
        {
            if (!TryFindAnchor(out RaycastHit hit)) return;

            _anchorTransform = hit.collider.transform;
            _anchorLocalPoint = _anchorTransform.InverseTransformPoint(hit.point);
            AnchorBody = hit.collider.attachedRigidbody;
            IsPropAnchor = IsPropLayer(hit.collider.gameObject.layer);
            IsAttached = true;

            SetRopeVisible(true);
        }

        /// <summary>
        /// 바깥에서 강제로 떼어낸다. 지금은 <see cref="SwingConstraint"/>가 건물에 정면으로
        /// 박았을 때 쓴다.
        ///
        /// 부착 여부의 주인이 여기 하나여야 하기 때문에 이 경로가 필요하다. 구속 쪽에서
        /// 스윙만 끄면 <c>FixedUpdate</c>가 다음 스텝에 <see cref="IsAttached"/>를 다시 읽어
        /// 그대로 재부착하고, 그때 로프 길이가 현재 거리로 새로 잡혀 벽에 붙은 채 줄만
        /// 다시 팽팽해진다.
        ///
        /// 버튼을 누르고 있는 중이라도 뗀다. 다시 쏘려면 손을 놓았다 눌러야 하는데,
        /// 그게 맞다 — 박은 자리에 그대로 다시 붙는 것을 막아준다.
        /// </summary>
        public void ForceDetach()
        {
            if (!IsAttached) return;
            Detach();

            // 버튼을 쥐고 있으면 곧 다시 쏜다(<see cref="Update"/>). 박은 그 자리에 같은
            // 프레임에 도로 붙지 않게 한 박자만 쉰다 — 그 사이 몸이 튕겨 나가 조준이 달라진다.
            _nextRetryTime = Time.time + retryInterval;
        }

        /// <summary>붙은 것을 놓는다. 속도에는 손대지 않는다 — 관성이 곧 보상이다.</summary>
        private void Detach()
        {
            IsAttached = false;
            _anchorTransform = null;
            AnchorBody = null;
            IsPropAnchor = false;

            SetRopeVisible(false);
            Detached?.Invoke();
        }

        // ─── 그리기 ───

        /// <summary>
        /// 로프를 그린다.
        ///
        /// <c>Update</c>가 아니라 <c>LateUpdate</c>인 이유는 애니메이터가 손 본을 옮기는
        /// 시점이 Update 이후이기 때문이다. Update에서 그리면 총구가 한 프레임 전 위치라
        /// 선이 손에서 떨어져 따라다닌다. 로프가 떨려 보이는 원인의 대부분이 이것이다.
        /// </summary>
        private void LateUpdate()
        {
            // 견인 중에는 줄이 그쪽으로 간다. 붙어 있지 않아도 그려야 하므로 먼저 본다.
            if (OverrideRopeTarget.HasValue)
            {
                SetRopeVisible(true);
                DrawRope(OverrideRopeTarget.Value);
                return;
            }

            if (!IsAttached)
            {
                // 견인이 끝난 프레임에 줄이 남아 있지 않게 한다.
                SetRopeVisible(false);
                return;
            }

            // 앵커가 사라지는 경우 — 프롭이 파괴되거나 도시를 재생성했을 때.
            if (_anchorTransform == null)
            {
                Detach();
                return;
            }

            DrawRope(AnchorPoint);
        }

        private void DrawRope(Vector3 target)
        {
            if (rope == null || muzzle == null) return;

            // 건물 스윙 로프는 실제로 팽팽하니 직선이 맞다. 소품을 잡고 있을 때만
            // 곡선으로 그려 "매달려서 처져 있다"는 무게감을 보여준다.
            if (IsPropAnchor)
            {
                DrawSaggingRope(muzzle.position, target);
                return;
            }

            rope.positionCount = 2;
            rope.SetPosition(0, muzzle.position);
            rope.SetPosition(1, target);
        }

        /// <summary>
        /// 소품을 붙잡고 있는 동안의 로프를 그린다 — 팽팽한 직선이 아니라, 중력으로
        /// 아래로 처지고 내 이동 방향(로프에 수직인 성분)으로 옆으로 쏠리는 완만한
        /// 곡선이다. 실제 로프 시뮬레이션은 아니고, 중간점들을 사인 곡선 가중치로
        /// 밀어내는 정도의 눈속임이다 — 소품 하나 붙잡자고 버라이어티 로프 물리를
        /// 새로 들일 이유는 없다.
        /// </summary>
        private void DrawSaggingRope(Vector3 start, Vector3 end)
        {
            int segments = Mathf.Max(2, propRopeSegments);
            rope.positionCount = segments + 1;

            Vector3 straight = end - start;
            float length = straight.magnitude;
            Vector3 along = length > 0.001f ? straight / length : transform.forward;

            // 내 속도 중 로프 방향 성분을 빼면 "로프에 수직으로 미는 힘"만 남는다.
            Vector3 velocity = _rb != null ? _rb.linearVelocity : Vector3.zero;
            Vector3 lateralTarget = velocity - Vector3.Dot(velocity, along) * along;
            _propRopeSway = Vector3.Lerp(_propRopeSway, lateralTarget * propRopeSwayPerSpeed,
                                         1f - Mathf.Exp(-propRopeSwaySmoothing * Time.deltaTime));

            float sagAmount = length * propRopeSagFraction;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float curve = Mathf.Sin(t * Mathf.PI); // 양 끝 0, 가운데서 최대.

                Vector3 point = Vector3.Lerp(start, end, t);
                point += Vector3.down * (sagAmount * curve);
                point += _propRopeSway * curve;

                rope.SetPosition(i, point);
            }
        }

        private void SetRopeVisible(bool visible)
        {
            if (rope != null) rope.gameObject.SetActive(visible);
        }

        /// <summary>
        /// 총 모델을 껐다 켠다. <see cref="WebGrabThrow"/>가 손으로 직접 잡아 던지는
        /// 동작(E키) 동안만 쓴다 — 그 동작은 웹이 아니라 손이 하는 일이라, 화면에
        /// 총이 떠 있으면 어색하다.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visualRoot != null) visualRoot.SetActive(visible);
        }
    }
}
