using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 웹 슬링샷 — 지상에서 벽에 줄을 붙이고, 반대로 달려 줄을 늘렸다가 놓으면
    /// 늘린 만큼의 속력으로 옥상 너머까지 튕겨 오른다.
    ///
    /// <b>왜 필요한가.</b> 새 도시(건물 34m 이상, 사거리 22m)에서는 지상에 떨어지면
    /// 옥상이 사거리·최대 앙각 밖이라 스윙이 아예 안 걸린다. 견인(<see cref="GrappleZip"/>)은
    /// 벽 중턱까지만 데려다주고 벽 반대쪽으로 튕기므로 옥상에 못 올라선다. 지상은 사실상
    /// 탈출구 없는 바닥이었다.
    ///
    /// <b>물리 — README 표의 "번다" 칸이다.</b>
    /// <list type="bullet">
    /// <item><b>저장</b>: 훅의 법칙 F = −kx, 탄성 에너지 E = ½kx². 앵커 반대쪽으로 달려
    ///       줄을 x만큼 늘리는 것이 곧 플레이어가 에너지를 버는 행동이다</item>
    /// <item><b>방출</b>: ½kx² = ½mv² → v = x·√(k/m). 발사 속력은 늘린 길이에 <b>정비례</b>한다
    ///       (두 배 늘리면 두 배 빠르고 에너지는 4배). 그래서 k를 노출하지 않고
    ///       "끝까지 늘렸을 때의 속력" 하나만 튜닝값으로 둔다 — 같은 뜻이고 읽기 쉽다</item>
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
    /// 지상 이동은 매 물리 스텝 수평 속도를 입력값으로 <b>대입</b>한다. 줄의 저항을 그
    /// 앞에서 걸면 바로 다음에 덮어써져 아무 효과가 없으므로, 반드시 그 뒤에서 깎아야 한다.
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

        [Header("줄 늘리기 — 에너지를 버는 구간")]
        [Tooltip("최대로 늘릴 수 있는 길이(m). 붙인 순간의 거리에서 이만큼 더 멀어지면 줄이 " +
                 "더는 안 늘어나고 그 방향으로는 못 달린다. 짧으면 충전이 너무 빨라 '번다'는 " +
                 "느낌이 없고, 길면 건물에서 너무 멀어져 필요한 발사 속력이 커진다.")]
        [Min(0.5f)][SerializeField] private float maxStretch = 8f;

        [Tooltip("끝까지 늘렸을 때의 발사 속력(m/s). v = x·√(k/m)이라 속력은 늘린 길이에 " +
                 "정비례하므로, 스프링 상수 k 대신 이 값 하나로 튜닝한다.\n\n" +
                 "기준: 중력 14.7 m/s²에서 36m 옥상까지 약 19m 떨어져 쏘면 약 34 m/s가 " +
                 "필요하다. 38이면 약 47m 높이까지 닿고, 그보다 높은 타워는 한 번에 못 간다 " +
                 "— 그건 견인으로 이어갈 몫이다. 스윙 속도 범위(25~40 m/s) 안에 두는 것이 " +
                 "전제다 — 넘으면 고속 물리 설정(설계 결정 2)을 다시 봐야 한다.")]
        [Min(1f)][SerializeField] private float maxLaunchSpeed = 38f;

        [Tooltip("이 비율보다 덜 늘리고 놓으면 발사하지 않고 그냥 풀린다. 우클릭을 툭 " +
                 "누른 것만으로 몸이 튀어 오르는 사고를 막는다.")]
        [Range(0f, 1f)][SerializeField] private float minCharge = 0.2f;

        [Tooltip("붙은 건물의 옥상이 내 발보다 이만큼 이상 높을 때만 슬링샷이 우클릭을 가져간다(m).\n\n" +
                 "IsGrounded는 옥상 위에서도 참이라, '지상이면 슬링샷'으로만 가르면 옥상 → 옥상 " +
                 "견인(기존 핵심 동작)이 통째로 슬링샷으로 바뀐다. 높이 차로 가르면 길바닥과 '바로 " +
                 "옆이 훨씬 높은 타워'일 때만 슬링샷이 되고, 비슷한 높이 옥상끼리는 견인 그대로다.")]
        [Min(0f)][SerializeField] private float minRoofRise = 10f;

        [Tooltip("충전 중 이 시간(초)보다 오래 공중에 있으면 줄을 푼다. 인도 턱을 넘을 때 " +
                 "잠깐 끊기는 접지 신호로 충전이 날아가지 않게 하는 유예다. 점프는 이보다 길어서 걸린다.")]
        [Min(0f)][SerializeField] private float airborneGrace = 0.3f;

        [Tooltip("줄이 늘어날수록 앵커 반대 방향 달리기 속도를 얼마나 깎을지(0~1).\n\n" +
                 "1이면 끝까지 늘렸을 때 바깥쪽 속도가 0이 된다(= 그때부터는 강체 구속). " +
                 "0이면 저항 없이 달리다가 최대 길이에서 갑자기 막힌다. 실제 고무줄은 늘어날수록 " +
                 "세게 당기므로(F = −kx) 비례해서 깎는 것이 '당겨지는 느낌'에 맞다.")]
        [Range(0f, 1f)][SerializeField] private float pullResistance = 0.6f;

        [Header("발사 — 꼭짓점을 옥상 너머에 둔다")]
        [Tooltip("옥상 모서리에서 건물 안쪽으로 얼마나 들어간 지점에 꼭짓점을 둘지(m). " +
                 "0이면 모서리 난간에 걸리고, 너무 크면 얕은 옥상을 넘어 반대편 길로 떨어진다.")]
        [Min(0f)][SerializeField] private float landingInset = 3f;

        [Tooltip("꼭짓점을 옥상 면보다 얼마나 위에 둘지(m). 캡슐 높이와 난간을 넘을 여유다.")]
        [Min(0f)][SerializeField] private float apexClearance = 2f;

        [Tooltip("날아가는 동안 몸을 발사 방향으로 돌리는 속도(초당 각도). 충전 중에는 건물 " +
                 "반대쪽을 보고 달렸으므로, 안 돌리면 뒤로 날아가는 그림이 된다.")]
        [Min(0f)][SerializeField] private float flightTurnDegPerSec = 720f;

        /// <summary>줄을 붙이고 늘리는 중인가.</summary>
        public bool IsCharging { get; private set; }

        /// <summary>발사 후 꼭짓점까지 날아가는 중인가. 이 동안은 공중 조작을 막는다.</summary>
        public bool IsLaunching { get; private set; }

        /// <summary>현재 충전량(0~1). 늘린 길이 / 최대 길이. UI나 카메라 연출이 읽을 수 있게 공개한다.</summary>
        public float Charge01 { get; private set; }

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private GrappleZip _zip;

        /// <summary>실제로 구독한 액션. 내 칸이 비면 GrappleZip의 것을 빌려 오므로 따로 들고 있는다.</summary>
        private InputAction _action;

        /// <summary>앵커. 건물이 움직일 일은 없지만 견인과 같은 방식(트랜스폼 + 로컬 좌표)으로 들고 있는다.</summary>
        private Transform _anchorTransform;
        private Vector3 _anchorLocalPoint;
        private Collider _anchorCollider;
        private Vector3 _anchorNormal;

        /// <summary>붙인 순간의 거리. 늘어난 길이 = 지금 거리 − 이 값.</summary>
        private float _restLength;

        /// <summary>놓았을 때 다음 물리 스텝에서 발사하도록 예약한다(입력은 Update 타이밍이라서).</summary>
        private bool _launchQueued;

        private float _launchTime;
        private float _airborneTime;
        private Vector3 _launchDirection;

        /// <summary>내가 켠 로프 그리기인지. WebGrabThrow와 같은 이유로, 남의 값을 지우지 않게 한다.</summary>
        private bool _ownsRopeOverride;

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
        }

        private void OnEnable()
        {
            // 내 칸이 비었으면 견인이 쓰는 액션을 빌린다 — 같은 우클릭을 지상/공중으로 나눠 쓰므로.
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

            // 충전·비행 중에 꺼지면 스위치가 켜진 채로 남아 조작이 영영 안 먹는다(견인과 같은 이유).
            if (IsCharging || IsLaunching) EndAll();
        }

        private void OnPressed(InputAction.CallbackContext ctx) => TryBeginCharge();

        private void OnReleased(InputAction.CallbackContext ctx)
        {
            if (!IsCharging) return;

            // 덜 늘렸으면 그냥 푼다. 발사는 다음 물리 스텝으로 미룬다 — 속도를 Update
            // 타이밍에 대입하면 그 사이 PlayerMovement의 FixedUpdate가 수평 속도를 다시 덮어쓸 수 있다.
            if (Charge01 < minCharge) EndCharge();
            else _launchQueued = true;
        }

        /// <summary>
        /// 줄을 붙인다. <b>지상에서만</b> — 공중 우클릭은 견인(<see cref="GrappleZip"/>)의 몫이다.
        ///
        /// 앵커 판정은 견인과 같은 옵션을 쓴다. 소품은 빼고(건물에 붙어야 옥상으로 간다),
        /// 생 벽면의 진자 검사도 건너뛴다(매달리지 않으므로 의미가 없고, 지상에서는 벽면이
        /// 유일하게 사거리 안에 있는 목표다).
        /// </summary>
        private void TryBeginCharge()
        {
            if (IsCharging || !CanBegin(out RaycastHit hit)) return;

            _anchorTransform = hit.collider.transform;
            _anchorLocalPoint = _anchorTransform.InverseTransformPoint(hit.point);
            _anchorCollider = hit.collider;
            _anchorNormal = hit.normal;
            _restLength = Vector3.Distance(_rb.position, hit.point);
            _airborneTime = 0f;
            Charge01 = 0f;
            IsCharging = true;

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
        public bool ClaimsPress() => isActiveAndEnabled && (IsCharging || CanBegin(out _));

        /// <summary>
        /// 슬링샷을 시작할 수 있는가 — 발이 땅에 있고, 앵커가 있고, 그 건물 옥상이 충분히 높을 때.
        /// </summary>
        private bool CanBegin(out RaycastHit hit)
        {
            hit = default;
            if (IsLaunching || gun == null || gun.IsAttached) return false;
            if (_movement == null || !_movement.IsGrounded) return false;
            if (_zip != null && _zip.IsZipping) return false;

            if (!gun.TryFindAnchor(out hit, includeProps: false, requireClearHang: false)) return false;

            // 옥상이 별로 안 높으면 견인에게 넘긴다(minRoofRise 설명 참고).
            float roofRise = hit.collider.bounds.max.y - transform.position.y;
            return roofRise >= minRoofRise;
        }

        private void FixedUpdate()
        {
            if (IsCharging) TickCharge();
            else if (IsLaunching) TickFlight();
        }

        /// <summary>
        /// 충전 중 매 물리 스텝. 늘어난 길이로 충전량을 갱신하고, 바깥쪽 달리기 속도를 깎는다.
        /// </summary>
        private void TickCharge()
        {
            // 공중에 있던 시간을 센다. 턱 넘기처럼 짧게 뜨는 것은 봐주고, 점프·낙하만 걸러낸다.
            _airborneTime = _movement.IsGrounded ? 0f : _airborneTime + Time.fixedDeltaTime;

            // 줄을 들고 있는 동안 다른 동작이 끼어들면 접는다. 스윙(R)이 붙었거나, 점프·낙하로
            // 발이 떨어졌거나, 앵커가 사라졌을 때 — 지상 전용 동작이라 공중으로 끌고 가지 않는다.
            if (gun.IsAttached || _anchorTransform == null ||
                (!_launchQueued && _airborneTime > airborneGrace))
            {
                EndCharge();
                return;
            }

            Vector3 anchor = AnchorPoint;
            float stretch = Mathf.Clamp(Vector3.Distance(_rb.position, anchor) - _restLength, 0f, maxStretch);
            Charge01 = stretch / maxStretch;

            SetRope(anchor);

            if (_launchQueued)
            {
                Launch(anchor);
                return;
            }

            ApplyPullResistance(anchor);
        }

        /// <summary>
        /// 줄의 저항. 앵커 반대쪽(바깥쪽) 수평 속도만 충전량에 비례해 깎는다.
        ///
        /// <b>힘(AddForce)이 아니라 속도를 직접 깎는 이유.</b> 지상 이동은 매 스텝 수평 속도를
        /// 입력값으로 대입하므로, 힘으로 당겨봐야 다음 스텝에 지워진다. 대입된 값을 대입 직후에
        /// 줄이는 것이 이 구조에서 F = −kx를 흉내 내는 유일한 방법이다.
        ///
        /// 끝까지 늘어나면 바깥쪽 성분을 <b>전부</b> 지운다 — 스윙의 강체 구속과 같은 투영
        /// (속도에서 반지름 방향 성분 제거)이다. 옆이나 안쪽으로는 자유롭게 움직일 수 있다.
        /// </summary>
        private void ApplyPullResistance(Vector3 anchor)
        {
            Vector3 away = _rb.position - anchor;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return;
            away.Normalize();

            Vector3 velocity = _rb.linearVelocity;
            float outward = Vector3.Dot(new Vector3(velocity.x, 0f, velocity.z), away);
            if (outward <= 0f) return;   // 앵커 쪽으로 가는 건 막지 않는다

            float cut = Charge01 >= 1f ? 1f : Charge01 * pullResistance;
            _rb.linearVelocity = velocity - away * (outward * cut);
        }

        /// <summary>
        /// 발사. 꼭짓점이 옥상 너머에 오는 속도를 역산하고, 그 크기를 번 에너지로 제한한다.
        /// </summary>
        private void Launch(Vector3 anchor)
        {
            Vector3 needed = SolveApexVelocity(_rb.position, anchor);

            // 번 속력. v = x·√(k/m)이므로 충전량에 정비례한다.
            float earned = maxLaunchSpeed * Charge01;
            float neededSpeed = needed.magnitude;

            // 모자라면 방향은 유지한 채 크기만 줄인다 — 덜 당기면 못 올라가는 것이 정직한 결과다.
            // 남으면 필요한 만큼만 쓴다. 남는 에너지까지 다 쓰면 꼭짓점이 옥상을 지나쳐
            // 얕은 건물에서는 반대편 길로 떨어진다 — 이 동작의 목적은 '옥상 복귀'다.
            Vector3 velocity = neededSpeed > earned && neededSpeed > 0.001f
                ? needed * (earned / neededSpeed)
                : needed;

            _rb.linearVelocity = velocity;
            _launchDirection = new Vector3(velocity.x, 0f, velocity.z);
            _launchTime = Time.time;

            // 꼭짓점까지 공중 조작을 막는다. 충전하느라 '건물 반대쪽' 키를 누르고 있었으므로,
            // 그대로 두면 공중 가속(airAcceleration)이 발사 속도를 거꾸로 깎아 궤적이 무너진다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            if (_movement != null && _movement.LogLift)
                Debug.Log($"[띄움] 슬링샷 발사 — 충전 {Charge01:P0}, 속도 {velocity.magnitude:F1} m/s " +
                          $"(필요 {neededSpeed:F1}), y속도 {velocity.y:F2} 대입, " +
                          $"높이 {transform.position.y:F2}m", this);

            EndCharge();
            IsLaunching = true;
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
            Vector3 needed = SolveApexVelocity(from, anchor);
            float earned = maxLaunchSpeed * Charge01;
            Vector3 v = needed.magnitude > earned && needed.magnitude > 0.001f
                ? needed * (earned / needed.magnitude)
                : needed;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(LandingTarget(from, anchor), 0.5f);

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
