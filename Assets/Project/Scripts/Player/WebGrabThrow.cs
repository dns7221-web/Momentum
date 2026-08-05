using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 웹 그랩 & 스로우 — E키로 근처 소품을 잡아 던지고, 좌클릭으로 그 날아가는 소품을
    /// 다시 맞히면 그쪽으로 확 끌려간다(체이스 대시).
    ///
    /// 두 입력이 완전히 다른 일을 한다.
    /// <list type="bullet">
    /// <item><b>E</b> — 손으로 직접 잡아 던진다. 웹이 아니라 손이 하는 일이라, 잡는 순간
    ///       총을 잠깐 숨기고 던지기 애니메이션을 재생한다</item>
    /// <item><b>좌클릭</b> — 평소엔 그대로 갈고리 조준이지만, 방금 던진 그 소품이 아직
    ///       날아가는 중이면(<see cref="GrappleGun.ChaseTarget"/>) 맞히는 순간 체이스가
    ///       된다 — 소품이 아니라 <b>내가</b> 그쪽으로 끌려간다</item>
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

        [Header("잡기 → 던지기 (E)")]
        [Tooltip("이보다 무거운 프롭은 잡지 않는다 — 무거운 쪽(차량 등)은 리버스 얀크가 " +
                 "따로 담당할 몫이다.")]
        [Min(0f)][SerializeField] private float maxGrabMass = 50f;

        [Tooltip("E를 누른 뒤 실제로 던져지기까지의 시간(초). 던지기 애니메이션이 팔을 " +
                 "휘두르는 시점에 맞춰야 자연스럽다 — 클립 길이를 보고 맞출 것.")]
        [Min(0f)][SerializeField] private float throwWindupDelay = 0.4f;

        [Tooltip("던지는 순간부터 Throw 레이어 가중치를 1로 유지하는 시간(초). " +
                 "GrappleGun의 GrappleFire 레이어와 같은 방식 — 클립 길이보다 조금 짧게 " +
                 "두면 자연스럽게 겹쳐 끊긴다.")]
        [Min(0f)][SerializeField] private float throwLayerHoldTime = 0.5f;

        [Tooltip("놓는 순간 조준 방향으로 얹는 속도(m/s). 여기에 내 현재 속도가 그대로 " +
                 "더해진다 — 정지 상태로 던지면 이 값이 전부고, 스윙 중에 던지면 그만큼 더 세다.")]
        [Min(0f)][SerializeField] private float throwForce = 22f;

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

        [Tooltip("소품과 이만큼 가까워지면 도착으로 보고 놓는다.")]
        [Min(0.5f)][SerializeField] private float chaseArrivalDistance = 2f;

        [Tooltip("도착 시 최소 수평 속도(m/s). GrappleZip과 같은 원칙 — 이미 이보다 빠르면 " +
                 "그 속도를 유지한다.")]
        [Min(0f)][SerializeField] private float chaseExitSpeed = 16f;

        [Tooltip("도착 시 위로 얹는 속도(m/s). 순수 수평이면 곧장 떨어지므로, 체공 시간을 " +
                 "벌어 다음 앵커를 찾을 틈을 준다.")]
        [Min(0f)][SerializeField] private float chaseExitUpSpeed = 8f;

        /// <summary>지금 E로 잡아서 던지는 중간(대기 동작)인가.</summary>
        public bool IsWindingUp { get; private set; }

        /// <summary>지금 방금 던진 소품을 향해 체이스 대시 중인가.</summary>
        public bool IsChasing { get; private set; }

        private Rigidbody _rb;
        private Collider _playerCollider;
        private PlayerMovement _movement;
        private Rigidbody _propBody;

        /// <summary>던지기까지 남은 대기 시간(초). <see cref="throwWindupDelay"/>에서 줄어든다.</summary>
        private float _windupRemaining;

        /// <summary>방금 던진 소품. 이게 남아있는 동안만 맞히면 체이스가 성립한다.</summary>
        private Rigidbody _thrownBody;

        /// <summary>체이스 기회가 남은 시간(초). 0 이하가 되면 <see cref="_thrownBody"/>를 놓아준다.</summary>
        private float _chaseWindowRemaining;

        private static readonly int ThrowTriggerHash = Animator.StringToHash("ThrowTrigger");

        /// <summary>Throw 레이어 인덱스. 클립을 안 채웠으면 -1로 남고 가중치 조작을 건너뛴다.</summary>
        private int _throwLayerIndex = -1;
        private float _throwWeightUntil;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _playerCollider = GetComponent<Collider>();
            _movement = GetComponent<PlayerMovement>();
            if (gun == null) gun = GetComponent<GrappleGun>();
            if (aimCamera == null) aimCamera = Camera.main;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) _throwLayerIndex = animator.GetLayerIndex("Throw");

            if (_playerCollider == null)
                Debug.LogWarning("[WebGrabThrow] 플레이어 콜라이더를 못 찾아 프롭과의 충돌을 못 끈다.", this);
            if (throwAction == null)
                Debug.LogWarning("[WebGrabThrow] Throw Action이 비어 있어 E로 잡아 던지는 동작이 안 된다.", this);
        }

        private void OnEnable()
        {
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

            // 대기 중이거나 체이스 중에 꺼지면 프롭을 정상 물리로 되돌린다.
            if (IsWindingUp) CancelWindup();
            if (IsChasing) EndChase(boost: false);

            if (gun != null && gun.ChaseTarget == _thrownBody) gun.ChaseTarget = null;
            _thrownBody = null;
        }

        /// <summary>
        /// Throw 레이어 가중치를 켰다 끈다. <see cref="GrappleGun.UpdateFireLayerWeight"/>와
        /// 같은 방식이다 — 매 프레임 무조건 돈다.
        /// </summary>
        private void Update()
        {
            if (animator == null || _throwLayerIndex < 0) return;
            animator.SetLayerWeight(_throwLayerIndex, Time.time < _throwWeightUntil ? 1f : 0f);
        }

        /// <summary>
        /// E를 눌렀다 — 크로스헤어 주변 소품을 잡고 대기 동작에 들어간다. 이미 뭔가
        /// 대기·체이스 중이면 무시한다.
        /// </summary>
        private void OnThrowPerformed(InputAction.CallbackContext ctx)
        {
            if (IsWindingUp || IsChasing || _propBody != null) return;
            if (gun == null || !gun.TryFindProp(out RaycastHit hit)) return;

            var body = hit.collider.attachedRigidbody;
            if (body == null || body.mass > maxGrabMass) return;   // 무거우면 손대지 않는다

            BeginWindup(body);
        }

        /// <summary>
        /// 매달린 프롭과 내 몸의 충돌 판정을 껐다 켠다. 잡고 있는 물건이 주인 몸과
        /// 충돌 판정을 갖는 게 애초에 이상하다.
        /// </summary>
        private void SetIgnorePlayerCollision(Rigidbody body, bool ignore)
        {
            if (body == null || _playerCollider == null) return;
            foreach (var col in body.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(_playerCollider, col, ignore);
        }

        private void FixedUpdate()
        {
            UpdateThrownTracking();

            if (IsWindingUp)
            {
                _windupRemaining -= Time.fixedDeltaTime;
                if (_windupRemaining <= 0f) FinishWindup();
                return;
            }

            // 방금 던진 그 소품에 좌클릭이 맞았는지를 본다 — 스윙/견인의 평소 조준과
            // 같은 입력이지만, 결과(gun.AnchorBody)가 그 소품과 정확히 같을 때만 체이스로
            // 받아들인다.
            if (!IsChasing && gun != null && gun.IsAttached && gun.AnchorBody != null
                && gun.AnchorBody == _thrownBody)
            {
                BeginChase();
            }

            if (IsChasing) UpdateChase();
        }

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
        /// 대기 동작을 시작한다 — 프롭을 잠깐 그 자리에 고정해두고, 총을 숨기고, 던지기
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
        /// 취소한다 — 컴포넌트가 꺼지는 등, 던지기를 끝까지 못 갈 때만 쓴다. 물리로
        /// 되돌릴 뿐 속도는 주지 않는다.
        /// </summary>
        private void CancelWindup()
        {
            IsWindingUp = false;
            if (gun != null) gun.SetVisible(true);

            if (_propBody != null)
            {
                SetIgnorePlayerCollision(_propBody, false);
                _propBody.isKinematic = false;
            }
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

            // 던진 이 소품을 체이스 대상으로 등록한다 — 시한 안에 좌클릭이 맞으면
            // 위 FixedUpdate의 체이스 감지 분기가 잡아챈다. GrappleGun에도 같이 알려서,
            // 이 소품에 한해서는 조준이 후해지게 한다(GrappleGun.ChaseTarget 참고).
            _thrownBody = _propBody;
            _chaseWindowRemaining = chaseWindowDuration;
            if (gun != null) gun.ChaseTarget = _thrownBody;

            _propBody = null;
        }

        /// <summary>
        /// 던진 소품에 좌클릭이 맞았다 — 체이스 대시를 시작한다. 이 순간부터는 그
        /// 소품이 아니라 <b>내가</b> 끌려간다.
        /// </summary>
        private void BeginChase()
        {
            IsChasing = true;
            _thrownBody = null;
            if (gun != null) gun.ChaseTarget = null;
            if (_movement != null) _movement.SuspendHorizontalControl = true;
        }

        /// <summary>
        /// <see cref="GrappleZip"/>과 같은 기법 — 힘이 아니라 속도를 목표 방향으로
        /// 직접 대입한다. 대상이 건물이 아니라 날아가는 소품이라는 것만 다르다.
        /// </summary>
        private void UpdateChase()
        {
            if (gun == null || !gun.IsAttached)
            {
                EndChase(boost: false);
                return;
            }

            Vector3 toTarget = gun.AnchorPoint - _rb.position;
            if (toTarget.magnitude <= chaseArrivalDistance)
            {
                EndChase(boost: true);
                return;
            }

            _rb.linearVelocity = toTarget.normalized * chaseSpeed;
            if (_movement != null) _movement.SuspendHorizontalControl = true;
        }

        /// <summary>
        /// 체이스를 끝낸다 — <see cref="GrappleZip.Stop"/>과 같은 릴리즈 원칙이다.
        /// 도착했을 때만(<paramref name="boost"/>) 속도를 새로 쓴다.
        /// </summary>
        private void EndChase(bool boost)
        {
            IsChasing = false;
            if (_movement != null) _movement.SuspendHorizontalControl = false;
            if (gun != null) gun.ForceDetach();

            if (!boost) return;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 direction = horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized : transform.forward;
            float speed = Mathf.Max(horizontal.magnitude, chaseExitSpeed);

            _rb.linearVelocity = direction * speed + Vector3.up * chaseExitUpSpeed;
        }
    }
}
