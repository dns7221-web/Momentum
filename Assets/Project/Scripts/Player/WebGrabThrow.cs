using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 웹 그랩 & 스로우 — 원거리 소품을 붙잡아 휘둘러 던진다.
    ///
    /// <b>몸까지 끌고 오지 않는다.</b> 원래는 손 앞까지 당겨와 쥐는 방식이었는데, 드럼통처럼
    /// 큰 소품은 끌려오는 동안 캐릭터를 그대로 가려버렸다. 대신 붙는 순간의 거리를 로프
    /// 길이로 고정하고, <b>그 길이만큼 떨어진 채로 조준 방향을 계속 따라다니게</b> 한다 —
    /// 몸에 안 붙으니 가릴 것도 없다.
    ///
    /// <b>"세게 휘둘렀는지"를 따로 계산하지 않는다.</b> 조준(카메라)이 도는 속도가 곧
    /// 소품의 접선 속도다 — 원운동 공식 그대로(v = r·ω), 로프가 길수록 같은 회전에도
    /// 더 빠르게 움직인다. 매 스텝 "따라가야 할 자리"를 계산해 그 이동량을 속도로 기록해
    /// 두면, 마우스를 크게 휘두를수록 저절로 세게 던져진다 — 입력을 따로 해석할 필요가
    /// 없다, 위치를 따라간 궤적 자체가 곧 속도다.
    ///
    /// <see cref="GrappleZip"/>·<see cref="SwingConstraint"/>과 같은 원칙 — 물리를 힘이
    /// 아니라 속도 대입으로 다룬다. 붙잡는 동안은 <c>isKinematic</c>이라 물리 시뮬레이션
    /// 밖에 있고, 놓는 순간에만 그동안 기록해둔 속도를 그대로 넘겨준다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(GrappleGun))]
    public class WebGrabThrow : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("앵커 판정을 빌려 쓴다. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        [Tooltip("소품이 따라다닐 기준점. 비워두면 GrappleGun의 Muzzle을 그대로 쓴다 — " +
                 "로프가 그려지는 자리와 같아야 부자연스럽지 않다.")]
        [SerializeField] private Transform holdPoint = null;

        [Tooltip("조준 기준 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private Camera aimCamera = null;

        [Header("무게 상한")]
        [Tooltip("이보다 무거운 프롭은 잡지 않는다 — 로프는 붙어도 붙잡지 않는다. " +
                 "무거운 쪽(차량 등)은 리버스 얀크가 따로 담당할 몫이다.")]
        [Min(0f)][SerializeField] private float maxGrabMass = 50f;

        [Header("휘두르기 — 몸까지 끌고 오지 않고 로프 길이만큼 떨어진 채 조준을 따라다닌다")]
        [Tooltip("붙는 순간의 실제 거리를 로프 길이로 쓰되, 이 범위로 자른다. 하한이 없으면 " +
                 "가까이서 붙잡은 소품이 여전히 몸 근처에 남아 가릴 수 있고, 상한이 없으면 " +
                 "멀리서 붙잡았을 때 팔이 안 닿을 듯 어색하게 멀리 떠 있게 된다.")]
        [Min(0.5f)][SerializeField] private float minHoldDistance = 2f;
        [Min(0.5f)][SerializeField] private float maxHoldDistance = 6f;

        [Tooltip("따라다니는 자리가 내 위치보다 이만큼 아래로는 못 내려간다.\n\n" +
                 "목표 자리는 '조준 방향으로 로프 길이만큼'인데, 카메라가 땅을 보면 그 " +
                 "방향의 로프 길이 끝은 바닥 밑이다. 키네마틱이라 충돌 없이 그 자리로 그냥 " +
                 "이동해버려 소품이 바닥에 파묻혀 사라진다 — 높이만 이 값으로 잘라서 막는다.")]
        [Min(0f)][SerializeField] private float minHoldHeightBelowPlayer = 1f;

        [Header("던지기")]
        [Tooltip("놓는 순간 조준 방향으로 더 얹는 속도(m/s). 0이어도 휘두른 속도만으로 " +
                 "던져지지만, 가만히 서서 놓아도 최소한의 힘은 실리도록 기본값을 둔다.")]
        [Min(0f)][SerializeField] private float throwForce = 8f;

        /// <summary>지금 뭔가를 붙잡아 휘두르고 있는가.</summary>
        public bool IsHolding { get; private set; }

        private Rigidbody _rb;
        private Collider _playerCollider;
        private Rigidbody _propBody;

        /// <summary>붙는 순간 정해지는 로프 길이. 놓을 때까지 고정이다.</summary>
        private float _holdDistance;

        /// <summary>지난 스텝에 소품을 옮겨뒀던 자리. 이번 자리와의 차이가 곧 속도다.</summary>
        private Vector3 _lastHoldTarget;

        /// <summary>매 스텝 갱신되는 "휘두른 속도". 놓는 순간 이 값을 그대로 던지는 속도로 쓴다.</summary>
        private Vector3 _swingVelocity;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _playerCollider = GetComponent<Collider>();
            if (gun == null) gun = GetComponent<GrappleGun>();
            if (aimCamera == null) aimCamera = Camera.main;
            if (holdPoint == null && gun != null) holdPoint = gun.Muzzle;

            if (holdPoint == null)
                Debug.LogWarning("[WebGrabThrow] HoldPoint가 비어 있어 붙잡은 프롭이 따라다닐 자리가 없다.", this);
            if (_playerCollider == null)
                Debug.LogWarning("[WebGrabThrow] 플레이어 콜라이더를 못 찾아 프롭과의 충돌을 못 끈다.", this);
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
        /// 붙잡은 프롭과 내 몸의 충돌 판정을 껐다 켠다.
        ///
        /// 로프 길이만큼 떨어뜨려 두는 지금 방식에서도 필요하다 — 스윙하며 몸이 크게
        /// 움직이는 동안 붙잡은 소품과 스쳐 부딪힐 수 있는데, 들고 있는 물건이 주인 몸과
        /// 충돌 판정을 갖는 게 애초에 이상하다.
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
        /// 붙잡고 있던 중이면 그게 곧 던지는 신호다.
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
                BeginHold(gun.AnchorBody);
            }

            if (_propBody == null || !IsHolding) return;
            HoldFollow();
        }

        /// <summary>
        /// 붙잡기 시작한다 — 당겨오지 않고, 지금 거리를 로프 길이로 고정한 채 바로 든다.
        /// </summary>
        private void BeginHold(Rigidbody body)
        {
            _propBody = body;
            SetIgnorePlayerCollision(_propBody, true);

            float distance = holdPoint != null
                ? Vector3.Distance(_propBody.position, holdPoint.position)
                : minHoldDistance;
            _holdDistance = Mathf.Clamp(distance, minHoldDistance, maxHoldDistance);

            _propBody.linearVelocity = Vector3.zero;
            _propBody.angularVelocity = Vector3.zero;
            _propBody.isKinematic = true;

            _lastHoldTarget = holdPoint != null ? ComputeHoldTarget() : _propBody.position;
            _swingVelocity = Vector3.zero;

            IsHolding = true;
        }

        /// <summary>
        /// 조준 카메라가 보는 방향. <b>손(<see cref="holdPoint"/>)의 방향이 아니라 이쪽을
        /// 써야 한다</b> — 손의 방향은 팔 IK뿐 아니라 캐릭터 몸통 회전도 따라가는데,
        /// 몸통은 마우스가 아니라 <b>키보드로 이동할 때(WASD로 이동 방향을 향해 도는 것)도
        /// 돈다.</b> 손 방향으로 휘두른 속도를 재면 방향키만 눌러도 "휘두른 것"으로 잡혀
        /// 던지는 힘이 실린다. 카메라는 이 게임에서 마우스로만 도므로, 여기 기준으로
        /// 재야 진짜 "마우스로 휘둘렀는지"만 반영된다.
        /// </summary>
        private Vector3 AimDirection => aimCamera != null ? aimCamera.transform.forward : transform.forward;

        /// <summary>
        /// 지금 조준 방향으로 로프 길이만큼 떨어진 목표 자리. 높이만 <see cref="minHoldHeightBelowPlayer"/>
        /// 아래로는 못 내려가게 자른다 — 안 그러면 땅을 내려다볼 때 그 방향의 로프 길이
        /// 끝이 바닥 밑이라, 키네마틱이라 충돌 없이 그대로 파묻혀버린다.
        /// </summary>
        private Vector3 ComputeHoldTarget()
        {
            Vector3 target = holdPoint.position + AimDirection * _holdDistance;
            float floor = _rb.position.y - minHoldHeightBelowPlayer;
            target.y = Mathf.Max(target.y, floor);
            return target;
        }

        /// <summary>
        /// 조준 방향으로 로프 길이만큼 떨어진 자리를 계속 따라간다.
        ///
        /// 목표 자리를 직접 계산해서 그 이동량으로 속도를 기록하는 이유는, 키네마틱
        /// Rigidbody의 <c>position</c>을 <c>MovePosition</c> 직후 바로 읽으면 아직 반영되기
        /// 전이라 최신 값을 못 믿기 때문이다 — 우리가 계산한 목표값을 기준으로 직접
        /// 재는 것이 정확하다.
        /// </summary>
        private void HoldFollow()
        {
            if (holdPoint == null) return;

            Vector3 target = ComputeHoldTarget();
            _swingVelocity = (target - _lastHoldTarget) / Time.fixedDeltaTime;
            _lastHoldTarget = target;

            _propBody.MovePosition(target);
            _propBody.MoveRotation(Quaternion.LookRotation(AimDirection, Vector3.up));
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
        /// 던진다 — <b>휘두르며 쌓인 속도 + 조준 방향 × 던지는 힘</b>을 그대로 대입한다.
        ///
        /// 정지 상태로 놓으면(휘두른 속도가 거의 0) <see cref="throwForce"/>뿐이라 약하고,
        /// 카메라를 세게 돌리며 놓으면 그 접선 속도가 고스란히 더해져 세다. 그동안
        /// <c>isKinematic</c>이었으므로 물리 시뮬레이션이 임의로 값을 건드릴 일이 없어,
        /// <see cref="HoldFollow"/>가 매 스텝 기록해둔 <see cref="_swingVelocity"/>를
        /// 그대로 믿을 수 있다.
        /// </summary>
        private void Throw()
        {
            SetIgnorePlayerCollision(_propBody, false);
            _propBody.isKinematic = false;

            _propBody.linearVelocity = _swingVelocity + AimDirection * throwForce;

            IsHolding = false;
            _propBody = null;
        }
    }
}
