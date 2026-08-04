using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 웹 그랩 & 스로우 — 가벼운 프롭을 당겨와 쥐었다가, 조준해서 던진다.
    ///
    /// README <c>### 웹 공격 동작 설계</c>의 ④번 동작. 새 물리 시스템이 아니라 이미 있는
    /// 두 가지 기법을 이어 붙인 것뿐이다.
    /// <list type="number">
    /// <item><b>당겨오기</b> — <see cref="GrappleZip"/>이 플레이어를 앵커로 끌어당길 때 쓰는
    ///       "힘이 아니라 속도를 대입한다"는 기법을 그대로 프롭에 적용한다</item>
    /// <item><b>쥐기</b> — 가까워지면 <c>Rigidbody.isKinematic = true</c>로 바꿔 물리 계산을
    ///       끄고 손 위치만 따라가게 한다</item>
    /// <item><b>던지기</b> — 갈고리가 놓이는 순간(<see cref="GrappleGun.Detached"/>)
    ///       <c>isKinematic = false</c>로 되돌리고, 속도 = <b>내 현재 속도 + 조준 방향 ×
    ///       던지는 힘</b>을 대입한다. 정지 상태로 던지면 약하고 전속력 스윙 중에 던지면
    ///       세다 — 관성이 그대로 타격이 된다</item>
    /// </list>
    ///
    /// <b>당겨오는 동안에도 프롭은 계속 동적(Dynamic) 상태다.</b> 위치를 옮기는 게 아니라
    /// 속도를 대입하는 것이라 물리 엔진의 충돌 처리를 그대로 통과한다 — 당겨오는 경로 위에
    /// 다른 프롭 무더기가 있으면 <see cref="CombatProp"/>이 그 충돌을 그대로 잡아낸다.
    /// README ②번(웹 얀크·관통)이 이 부수 효과만으로 이미 성립하는 이유다.
    ///
    /// 무거운 프롭(<see cref="maxGrabMass"/> 초과)은 건드리지 않는다 — 그쪽은 리버스 얀크
    /// (③, 아직 미구현) 몫이다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(GrappleGun))]
    public class WebGrabThrow : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커 판정을 빌려 쓴다. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        [Tooltip("쥔 프롭이 따라갈 위치. 손이나 가슴 앞에 빈 오브젝트를 만들어 끼운다. " +
                 "비워두면 GrappleGun의 Muzzle을 그대로 쓴다.")]
        [SerializeField] private Transform holdPoint = null;

        [Tooltip("조준 기준 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private Camera aimCamera = null;

        [Header("무게 상한")]
        [Tooltip("이보다 무거운 프롭은 잡지 않는다 — 로프는 붙어도 당겨오지 않는다. " +
                 "무거운 쪽(차량 등)은 리버스 얀크가 따로 담당할 몫이다.")]
        [Min(0f)][SerializeField] private float maxGrabMass = 50f;

        [Header("당겨오기")]
        [Tooltip("프롭이 손을 향해 날아오는 속도(m/s). 플레이어를 당기는 GrappleZip의 " +
                 "zipSpeed와 같은 기법 — 힘이 아니라 속도를 매 스텝 대입한다.")]
        [Min(1f)][SerializeField] private float pullSpeed = 12f;

        [Tooltip("손에서 이만큼 가까워지면 당겨오기를 끝내고 쥔 것으로 본다.")]
        [Min(0.1f)][SerializeField] private float grabDistance = 1.2f;

        [Header("던지기")]
        [Tooltip("조준 방향으로 얹는 속도(m/s). 여기에 내 현재 속도가 그대로 더해진다 — " +
                 "정지 상태로 던지면 이 값이 전부고, 스윙 중에 던지면 그만큼 더 세다.")]
        [Min(0f)][SerializeField] private float throwForce = 22f;

        /// <summary>지금 뭔가를 쥐고 있는가.</summary>
        public bool IsHolding { get; private set; }

        private Rigidbody _rb;
        private Collider _playerCollider;
        private Rigidbody _propBody;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _playerCollider = GetComponent<Collider>();
            if (gun == null) gun = GetComponent<GrappleGun>();
            if (aimCamera == null) aimCamera = Camera.main;
            if (holdPoint == null && gun != null) holdPoint = gun.Muzzle;

            if (holdPoint == null)
                Debug.LogWarning("[WebGrabThrow] HoldPoint가 비어 있어 쥔 프롭을 들 위치가 없다.", this);
            if (_playerCollider == null)
                Debug.LogWarning("[WebGrabThrow] 플레이어 콜라이더를 못 찾아 프롭과의 충돌을 못 끈다 — " +
                                 "당겨오는 프롭이 몸에 부딪혀 튕겨 나갈 수 있다.", this);
        }

        private void OnEnable()
        {
            if (gun != null) gun.Detached += OnGunDetached;
        }

        private void OnDisable()
        {
            if (gun != null) gun.Detached -= OnGunDetached;

            // 컴포넌트가 꺼지는 순간 쥐고 있던 것은 그냥 물리로 되돌린다. 던지는 것과
            // 같은 취급을 하면 안 된다 — 조준 방향에 의미가 없는 상황이다.
            if (IsHolding) ReleaseWithoutThrow();
            SetIgnorePlayerCollision(_propBody, false);
            _propBody = null;
        }

        /// <summary>
        /// 당겨오는 프롭과 내 몸의 충돌 판정을 껐다 켠다.
        ///
        /// <b>이게 없으면 당겨오던 프롭이 몸에 부딪혀 폭발하듯 튕겨 나간다.</b> 매 물리
        /// 스텝 프롭 속도를 손 쪽으로 강제로 대입하는데, 손 위치가 몸 바로 앞이라 도착하기
        /// 전에 콜라이더끼리 먼저 겹친다. 그러면 물리 엔진은 겹친 만큼 서로 밀어내려 하는데,
        /// 프롭 쪽 속도는 다음 스텝에 다시 손 쪽으로 강제 대입되어 버리니 그 반작용이 전부
        /// <b>나</b>에게만 쌓인다 — 순간적으로 아주 빠르게 튕겨 나가는 버그의 정체다.
        /// 쥐고 있는 동안(당겨오는 중 포함)은 아예 부딪히지 않게 막는 것이 맞다 — 들고 있는
        /// 물건이 주인 몸과 충돌 판정을 갖는 게 애초에 이상하다.
        /// </summary>
        private void SetIgnorePlayerCollision(Rigidbody body, bool ignore)
        {
            if (body == null || _playerCollider == null) return;
            foreach (var col in body.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(_playerCollider, col, ignore);
        }

        /// <summary>
        /// 갈고리가 뭔가에서 떨어졌다는 신호 — 버튼을 놓았거나, 충돌로 강제로 끊겼거나.
        ///
        /// 쥐고 있던 중이면 그게 곧 던지는 신호다. 아직 당겨오는 중이었다면 던질 게 없으니
        /// 그냥 추적을 그만둔다 — 프롭은 그 순간 갖고 있던 속도를 그대로 유지한 채 날아간다,
        /// 별도로 멈추거나 되돌리지 않는다.
        /// </summary>
        private void OnGunDetached()
        {
            if (IsHolding) Throw();
            else
            {
                SetIgnorePlayerCollision(_propBody, false);
                _propBody = null;
            }
        }

        private void FixedUpdate()
        {
            // 새로 프롭에 붙은 순간을 잡는다. 매 스텝 다시 확인하는 이유는 붙는 시점이
            // Update(입력)와 FixedUpdate(물리) 어느 쪽이 먼저 도는지 보장이 없어서다.
            if (gun != null && gun.IsAttached && gun.IsPropAnchor && gun.AnchorBody != null
                && _propBody == null && !IsHolding)
            {
                if (gun.AnchorBody.mass > maxGrabMass) return;   // 무거우면 손대지 않는다
                _propBody = gun.AnchorBody;

                // 당겨오기 시작. 도착하기 전에 몸과 부딪혀 튕겨 나가는 것을 막는다 —
                // SetIgnorePlayerCollision 주석 참고.
                SetIgnorePlayerCollision(_propBody, true);
            }

            if (_propBody == null) return;

            if (IsHolding) HoldFollow();
            else PullTowardHold();
        }

        /// <summary>
        /// 프롭을 손 쪽으로 당긴다 — 힘이 아니라 속도를 대입한다.
        ///
        /// <see cref="GrappleZip"/>과 같은 이유다. 힘으로 끌면 중력과 겨루느라 궤적이 처져
        /// 도착 시점이 예측되지 않는다. 속도 대입은 <b>여전히 동적 상태를 유지</b>하므로
        /// 도중에 다른 프롭 무더기를 치면 정상적으로 충돌 처리된다(README ② 웹 얀크가 이
        /// 부수 효과만으로 성립하는 이유).
        /// </summary>
        private void PullTowardHold()
        {
            if (holdPoint == null) return;

            Vector3 toHold = holdPoint.position - _propBody.position;
            float distance = toHold.magnitude;

            if (distance <= grabDistance)
            {
                BeginHold();
                return;
            }

            _propBody.linearVelocity = toHold.normalized * pullSpeed;
        }

        /// <summary>
        /// 쥔다 — 물리 계산을 끄고 위치만 손을 따라가게 한다.
        ///
        /// 코드 몇 줄로 끝나는 이유가 이것이다. 쥐고 있는 동안 물체는 물리 시뮬레이션에서
        /// 완전히 빠지므로, 손과 물체 사이 관계를 구속으로 풀 필요가 없다.
        /// </summary>
        private void BeginHold()
        {
            IsHolding = true;
            _propBody.linearVelocity = Vector3.zero;
            _propBody.angularVelocity = Vector3.zero;
            _propBody.isKinematic = true;
        }

        /// <summary>
        /// 쥔 프롭이 손을 따라가게 한다. 키네마틱 강체는 transform이 아니라
        /// <c>MovePosition</c>으로 옮겨야 다음 스텝의 보간·충돌 판정이 정확하다.
        /// </summary>
        private void HoldFollow()
        {
            if (holdPoint == null) return;
            _propBody.MovePosition(holdPoint.position);
            _propBody.MoveRotation(holdPoint.rotation);
        }

        /// <summary>
        /// 놓는다 — 물리로 되돌리되 속도를 주지 않는다. 던지기가 아니라 취소하는 경로
        /// (컴포넌트 비활성화 등)에서만 쓴다.
        /// </summary>
        private void ReleaseWithoutThrow()
        {
            if (_propBody != null) _propBody.isKinematic = false;
            IsHolding = false;
        }

        /// <summary>
        /// 던진다 — <b>내 현재 속도 + 조준 방향 × 던지는 힘</b>을 그대로 대입한다.
        ///
        /// 정지 상태로 던지면 <see cref="throwForce"/>뿐이라 약하고, 스윙으로 가속한 채
        /// 던지면 그 속도가 고스란히 더해져 세다. 새 원칙이 아니라 <see cref="SwingConstraint"/>의
        /// 릴리즈 보상과 같은 것 — "관성이 그대로 타격이 된다"를 던지기에도 적용한 것뿐이다.
        /// </summary>
        private void Throw()
        {
            SetIgnorePlayerCollision(_propBody, false);
            _propBody.isKinematic = false;

            Vector3 aimDirection = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            _propBody.linearVelocity = _rb.linearVelocity + aimDirection * throwForce;

            IsHolding = false;
            _propBody = null;
        }
    }
}
