using Unity.Cinemachine;
using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 — 인섬니악 방향 스윙 카메라 2단계 (2026-10-13)
    /// <summary>
    /// 플레이어가 지금 어떤 상황(땅/스윙/공중/잽)인지, 얼마나 빠른지, 스윙의 어느 구간인지,
    /// 얼마나 높은지를 읽어서 카메라의 <b>거리·세로 각도·세로 댐핑</b>을 그때그때 바꾸는 "카메라 감독".
    /// CinemachineCamera 오브젝트에 붙이고 <see cref="CameraFeelProfile"/> 에셋을 넣으면 된다.
    ///
    /// <b>왜 필요한가.</b> 지금까지는 카메라 값이 전부 고정이라 한 상황에만 맞출 수 있었다.
    /// 레퍼런스(인섬니악 스파이더맨)는 스윙 최저점에서는 낮고 가깝게, 떠오를 때는 뒤로 빠져 내려다보고,
    /// 빠를수록 멀어지고, 다이브하면 아래를 본다. 그 "상황에 따라 변하는 부분"을 이 컴포넌트가 맡는다.
    ///
    /// <b>값을 넣는 통로는 Cinemachine 내장 그대로 쓴다.</b>
    /// - 세로 각도: <c>VerticalAxis.Center</c>만 바꾼다. 실제로 그 각도까지 부드럽게 도는 건
    ///   이미 켜 둔 Vertical Axis <b>Recentering</b>이 한다. 마우스를 움직이면 Recentering이 스스로 멈추므로
    ///   플레이어 조작과 싸우지 않는다(Center는 "입력"으로 치지 않는다. InputAxis.TrackValueChange는 Value만 본다).
    /// - 거리: Orbital Follow의 <c>Radius</c>를 <c>Mathf.SmoothDamp</c>로 바꾼다.
    /// - 세로 댐핑: <c>TrackerSettings.PositionDamping.y</c>.
    ///
    /// <b>내장 기능을 안 쓴 곳과 이유.</b>
    /// - 거리에 Orbital Follow의 Radial Axis(궤도 크기 배율)를 쓰지 않는다. 이 축은 지금 마우스 휠(Orbit Scale)에
    ///   묶여 있고, 휠은 슬링샷 충전에 쓴다. 또 Orbital Follow는 축 값이 바뀌면 "입력"으로 보고 Recentering Time이
    ///   같은 다른 축의 Recentering까지 멈춘다(CinemachineOrbitalFollow의 "Sync recentering"). 그래서 입력과 상관없는
    ///   Radius를 직접 움직인다.
    /// - 상황마다 카메라를 따로 두는 State-Driven Camera를 쓰지 않는다. 스윙은 1~2초마다 붙었다 놓기를 반복해서,
    ///   카메라를 바꿀 때마다 블렌드가 보이게 된다. 카메라 한 대의 값을 부드럽게 바꾸는 쪽이 끊김이 없다.
    /// </summary>
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    // CameraHeadingTarget(-100) 다음, CameraWallAvoid(-90) 전, Cinemachine Brain의 LateUpdate보다 먼저.
    // Brain이 카메라를 계산하기 전에 값을 바꿔야 같은 프레임에 반영된다.
    [DefaultExecutionOrder(-95)]
    public class TraversalCameraDirector : MonoBehaviour
    {
        /// <summary>카메라가 구분하는 플레이어 상황.</summary>
        public enum CameraState { Ground, Swing, Air, Zip }

        [Tooltip("카메라 튜닝 값 에셋. Project 창 우클릭 › Create › Momentum › Camera Feel Profile로 만든다. " +
                 "비어 있으면 이 컴포넌트는 아무것도 하지 않는다.")]
        [SerializeField] private CameraFeelProfile profile = null;

        [Tooltip("발밑 높이를 잴 때 지면으로 볼 레이어. 건물 지붕(Anchorable)과 바닥(Ground).")]
        [SerializeField] private LayerMask groundLayers = 0;

        [Tooltip("켜면 Game 뷰 왼쪽 위에 지금 상황과 카메라 값을 띄운다. 튜닝할 때만 켠다.")]
        [SerializeField] private bool showDebugHud = false;

        /// <summary>지금 카메라가 판단한 상황. 3단계(순간 펀치)나 디버그 표시에서 읽는다.</summary>
        public CameraState State { get; private set; }

        private CinemachineOrbitalFollow _orbital;

        // 플레이어 쪽 참조. Follow Target(플레이어 루트)이 바뀔 때만 다시 찾는다.
        private Transform _target;
        private Rigidbody _body;
        private PlayerMovement _movement;
        private GrappleGun _gun;
        private GrappleZip _zip;
        private WebSlingshot _slingshot;

        // 컴포넌트를 켤 때의 인스펙터 값. 기준으로 쓰고, 끌 때 되돌린다.
        private float _baseRadius;
        private float _baseVerticalDamping;
        private float _baseVerticalCenter;

        // SmoothDamp가 프레임 사이에 기억하는 속도
        private float _radiusVelocity;
        private float _dampingVelocity;

        // 디버그 표시용 마지막 계산 값
        private float _debugHeight;
        private float _debugPitch;

        /// <summary>
        /// 컴포넌트를 처음 붙였을 때(에디터) 기본 레이어를 채운다.
        /// LayerMask.GetMask는 필드 초기화에서 부를 수 없어 여기서 한다.
        /// </summary>
        private void Reset()
        {
            groundLayers = LayerMask.GetMask("Anchorable", "Ground");
        }

        private void Awake()
        {
            _orbital = GetComponent<CinemachineOrbitalFollow>();
            if (groundLayers == 0) groundLayers = LayerMask.GetMask("Anchorable", "Ground");
        }

        /// <summary>
        /// 켤 때 인스펙터 값을 기억한다. Radius는 "기준 거리"로 계속 쓰이고,
        /// 세로 댐핑과 세로 Center는 이 컴포넌트가 상황마다 덮어쓴다.
        /// </summary>
        private void OnEnable()
        {
            if (_orbital == null) _orbital = GetComponent<CinemachineOrbitalFollow>();
            _baseRadius = _orbital.Radius;
            _baseVerticalDamping = _orbital.TrackerSettings.PositionDamping.y;
            _baseVerticalCenter = _orbital.VerticalAxis.Center;
            _radiusVelocity = 0f;
            _dampingVelocity = 0f;

            if (profile == null)
                Debug.LogWarning("TraversalCameraDirector: Profile이 비어 있어 카메라 연출을 하지 않습니다. " +
                                 "Create › Momentum › Camera Feel Profile로 만든 에셋을 넣어 주세요.", this);
        }

        /// <summary>
        /// 끌 때 인스펙터 값으로 되돌린다. 컴포넌트를 껐다 켜며 before/after를 비교할 수 있다.
        /// </summary>
        private void OnDisable()
        {
            if (_orbital == null) return;
            _orbital.Radius = _baseRadius;
            Vector3 damping = _orbital.TrackerSettings.PositionDamping;
            damping.y = _baseVerticalDamping;
            _orbital.TrackerSettings.PositionDamping = damping;
            _orbital.VerticalAxis.Center = _baseVerticalCenter;
        }

        private void LateUpdate()
        {
            if (profile == null || _orbital == null || !_orbital.IsValid) return;
            CacheTarget();
            if (_body == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return; // 일시정지 중엔 건드리지 않는다

            // 1. 지금 상황과 그 상황의 기본값
            State = ReadState();
            CameraFeelProfile.StateSettings settings = profile.Get(State);

            Vector3 velocity = _body.linearVelocity;
            float flatSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            // 2. 스윙 구간 신호 (0~1). 최저점 근처일수록 bottom01이, 위로 빠를수록 rising01이 커진다.
            float bottom01 = State == CameraState.Swing ? SwingBottom01() : 0f;
            bool airborne = State == CameraState.Swing || State == CameraState.Air;
            float rising01 = airborne ? Mathf.Clamp01(velocity.y / profile.risingSpeedForMax) : 0f;

            // 3. 거리 = 기준 거리 × 상황 배율 × 속도 배율 × 최저점 배율 × 떠오름 배율
            float distanceScale = settings.distanceScale
                                  * profile.speedToDistance.Evaluate(flatSpeed)
                                  * Mathf.Lerp(1f, profile.swingBottomDistance, bottom01)
                                  * Mathf.Lerp(1f, profile.risingDistance, rising01);
            // SmoothDamp: 목표가 갑자기 바뀌어도 속도를 이어 가며 부드럽게 다가간다(유니티 내장).
            _orbital.Radius = Mathf.SmoothDamp(_orbital.Radius, _baseRadius * distanceScale,
                                               ref _radiusVelocity, profile.distanceSmoothTime, Mathf.Infinity, dt);

            // 4. 세로 각도 = 상황 각도 + 최저점 + 떠오름 + 높이 + 낙하
            float pitch = settings.pitch
                          + profile.swingBottomPitch * bottom01
                          + profile.risingPitch * rising01;
            _debugHeight = 0f;
            if (State != CameraState.Ground)
            {
                _debugHeight = HeightAboveGround();
                pitch += profile.heightToPitch.Evaluate(_debugHeight);
            }
            if (State == CameraState.Air)
                pitch += profile.fallSpeedToPitch.Evaluate(-velocity.y); // 아래로 갈수록 -velocity.y가 크다

            // Center만 바꾸면 Recentering이 Value를 그쪽으로 SmoothDamp한다(Time × 0.5초).
            // ClampValue: Vertical Axis의 Range(-25~40) 밖으로 나가지 않게 자른다(내장).
            _debugPitch = _orbital.VerticalAxis.ClampValue(pitch);
            _orbital.VerticalAxis.Center = _debugPitch;

            // 5. 세로 댐핑을 상황 값으로 천천히 넘긴다. 툭 바뀌면 카메라가 한 번 덜컥인다.
            Vector3 damping = _orbital.TrackerSettings.PositionDamping;
            damping.y = Mathf.SmoothDamp(damping.y, settings.verticalDamping,
                                         ref _dampingVelocity, profile.dampingBlendTime, Mathf.Infinity, dt);
            _orbital.TrackerSettings.PositionDamping = damping;
        }

        /// <summary>
        /// 플레이어 컴포넌트들의 공개 상태로 지금 상황을 정한다. 위에 있을수록 우선한다.
        /// 잽·슬링샷 발사 → 스윙(매달림) → 땅 → 그 밖은 공중.
        /// </summary>
        private CameraState ReadState()
        {
            if ((_zip != null && _zip.IsZipping) || (_slingshot != null && _slingshot.IsLaunching))
                return CameraState.Zip;

            // 물건(프롭)에 붙은 웹은 잡아당기기용이라 스윙이 아니다 — SwingConstraint와 같은 기준.
            if (_gun != null && _gun.IsAttached && !_gun.IsPropAnchor)
                return CameraState.Swing;

            if (_movement != null && _movement.IsGrounded)
                return CameraState.Ground;

            return CameraState.Air;
        }

        /// <summary>
        /// 스윙 최저점에 얼마나 가까운지(0~1). 앵커에서 플레이어로 가는 방향이 바로 아래(수직)면 1,
        /// 수직에서 swingBottomAngle만큼 벗어나면 0이다. 내적(Dot)이 곧 두 방향 사이 각도의 cos이다.
        /// </summary>
        private float SwingBottom01()
        {
            Vector3 fromAnchor = _body.position - _gun.AnchorPoint;
            if (fromAnchor.sqrMagnitude < 0.01f) return 0f;

            float cos = Vector3.Dot(fromAnchor.normalized, Vector3.down);
            float edgeCos = Mathf.Cos(profile.swingBottomAngle * Mathf.Deg2Rad);
            return Mathf.InverseLerp(edgeCos, 1f, cos);
        }

        /// <summary>
        /// 발밑 지면(지붕 포함)까지의 높이(m). 못 찾으면 heightProbeDistance로 친다.
        /// 발보다 0.5 m 위에서 쏘는 건 발이 지면에 살짝 묻혀 있을 때도 그 지면을 찾게 하기 위함이다.
        /// </summary>
        private float HeightAboveGround()
        {
            const float startLift = 0.5f;
            Vector3 origin = _body.position + Vector3.up * startLift;
            // Raycast: 광선 한 줄로 처음 닿는 것을 찾는다(유니티 내장). 트리거는 무시한다.
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                profile.heightProbeDistance + startLift, groundLayers, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0f, hit.distance - startLift);
            return profile.heightProbeDistance;
        }

        /// <summary>
        /// Follow Target이 바뀌었을 때만 플레이어 컴포넌트를 다시 찾는다. 매 프레임 GetComponent를 부르지 않기 위함이다.
        /// 플레이어 컴포넌트들은 루트나 그 자식에 있으므로 Rigidbody(루트)에서 아래로 찾는다.
        /// </summary>
        private void CacheTarget()
        {
            Transform current = _orbital.FollowTarget;
            if (current == _target) return;
            _target = current;

            _body = current != null ? current.GetComponentInParent<Rigidbody>() : null;
            _movement = _body != null ? _body.GetComponentInChildren<PlayerMovement>() : null;
            _gun = _body != null ? _body.GetComponentInChildren<GrappleGun>() : null;
            _zip = _body != null ? _body.GetComponentInChildren<GrappleZip>() : null;
            _slingshot = _body != null ? _body.GetComponentInChildren<WebSlingshot>() : null;
        }

        /// <summary>
        /// 튜닝용 화면 표시. IMGUI(OnGUI)는 유니티 내장의 가장 간단한 디버그 UI라
        /// 캔버스나 프리팹 없이 바로 띄울 수 있다. 빌드용 UI가 아니라 개발 중에만 켠다.
        /// </summary>
        private void OnGUI()
        {
            if (!showDebugHud || profile == null || _orbital == null) return;
            GUI.Label(new Rect(10, 10, 900, 22),
                $"Camera {State} | Radius {_orbital.Radius:0.00} | Pitch {_debugPitch:0.0}° " +
                $"(now {_orbital.VerticalAxis.Value:0.0}°) | DampY {_orbital.TrackerSettings.PositionDamping.y:0.00} | " +
                $"Height {_debugHeight:0.0} m");
        }
    }
}
