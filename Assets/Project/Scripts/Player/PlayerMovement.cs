using UnityEngine;
using UnityEngine.InputSystem;

namespace Momentum.Player
{
    /// <summary>
    /// 지상 이동 — 걷기·달리기·점프·카메라 기준 회전.
    ///
    /// 이 캡슐이 물리 몸통이고, 스킨 메시 캐릭터는 자식으로 붙는 시각 레이어일 뿐이다
    /// (README 설계: "목적은 외형이 아니라 물리 바디다"). 나중에 로프 스윙이 붙을 때도
    /// 같은 Rigidbody를 그대로 구속하면 되므로, 지금부터 위치는 전부 Rigidbody가 갖고
    /// 애니메이션은 그 위에 얹히는 포즈로만 작동한다 — Animator의 Apply Root Motion은
    /// 반드시 꺼져 있어야 한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 8f;
        [Tooltip("초당 최대 회전 각도. 카메라가 아니라 이동 방향을 향해 도는 속도다.")]
        [SerializeField] private float turnSpeedDegPerSec = 540f;
        [SerializeField] private float jumpSpeed = 6f;

        [Header("지면 판정")]
        [Tooltip("이 각도보다 가파른 면은 바닥이 아니라 벽으로 본다. 벽 점프를 막는 값.")]
        [Range(0f, 89f)][SerializeField] private float maxSlopeAngle = 50f;

        [Header("진단")]
        [Tooltip("점프가 실제로 몇 번 발동하는지 콘솔에 찍는다. " +
                 "한 번 눌러 한 줄만 뜨는데도 캐릭터가 떠오르면 원인은 코드가 아니라 " +
                 "Animator의 Apply Root Motion이다.")]
        [SerializeField] private bool logJumps = false;

        [Header("카메라")]
        [Tooltip("비워두면 Camera.main을 자동으로 쓴다. 입력을 이 방향 기준으로 회전시킨다.")]
        [SerializeField] private Transform cameraTransform;

        [Header("입력 (InputSystem_Actions.inputactions의 Player 맵에서 드래그)")]
        [SerializeField] private InputActionReference moveAction = null;
        [SerializeField] private InputActionReference sprintAction = null;
        [SerializeField] private InputActionReference jumpAction = null;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");

        private Rigidbody _rb;
        private Animator _animator;

        /// <summary>이번 물리 스텝에 바닥과 닿아 있는지. 충돌 콜백이 채운다.</summary>
        private bool _isGrounded;

        /// <summary>
        /// 충돌 콜백이 접촉을 발견하면 켠다.
        ///
        /// 별도 변수를 두는 이유는 물리 콜백과 FixedUpdate의 실행 순서 때문이다.
        /// Unity는 [FixedUpdate → 물리 시뮬레이션 → 충돌 콜백] 순으로 돌므로,
        /// FixedUpdate에서 이 값을 읽어 쓰고 곧바로 꺼두면 다음 스텝의 콜백이 다시 채운다.
        /// 접촉이 사라지면 아무도 켜주지 않으므로 자연히 false로 남는다.
        /// </summary>
        private bool _groundContact;

        private bool _jumpRequested;
        private float _horizontalSpeed;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // 스킨 메시는 자식 프리팹으로 붙는다 — 캡슐 자체엔 Animator를 두지 않는다.
            _animator = GetComponentInChildren<Animator>();

            // 캡슐이 충돌로 옆으로 넘어지면 안 된다. 회전은 아래에서 스크립트가 직접 관리한다.
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // 지금 속도로는 필요 없지만, 이후 스윙에서 같은 Rigidbody가 24m/s대로 움직이게 되므로
            // 걷기 단계부터 맞춰 둔다 — 나중에 바꿀 일이 없어야 터널링 설정을 두 번 안 건드린다.
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            moveAction?.action.Enable();
            sprintAction?.action.Enable();
            if (jumpAction != null)
            {
                jumpAction.action.Enable();
                jumpAction.action.performed += OnJumpPerformed;
            }
        }

        private void OnDisable()
        {
            moveAction?.action.Disable();
            sprintAction?.action.Disable();
            if (jumpAction != null)
            {
                jumpAction.action.performed -= OnJumpPerformed;
                jumpAction.action.Disable();
            }
        }

        /// <summary>
        /// 입력만 기록한다. 지면 판정은 하지 않는다.
        ///
        /// 입력 이벤트는 물리 스텝과 타이밍이 어긋나므로, 여기서 지면 상태를 보고 판단하면
        /// 이미 낡은 값을 읽게 된다. 판단은 FixedUpdate에서 그 스텝의 접촉 정보로 한 번만 한다.
        /// </summary>
        private void OnJumpPerformed(InputAction.CallbackContext ctx) => _jumpRequested = true;

        // ─── 지면 접촉 ───
        //
        // 구를 쏘아 짐작하지 않고 물리 엔진이 알려주는 실제 접촉점을 쓴다.
        // 반지름·거리 같은 튜닝 값이 없어지고, 닿았는지 아닌지가 추정이 아니라 사실이 된다.

        private void OnCollisionEnter(Collision collision) => EvaluateContacts(collision);
        private void OnCollisionStay(Collision collision) => EvaluateContacts(collision);

        private void EvaluateContacts(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                // 접촉면의 법선이 충분히 위를 향하면 바닥, 아니면 벽이다.
                // 이 한 줄이 벽에 붙어 무한히 점프하던 문제를 막는다.
                if (Vector3.Angle(collision.GetContact(i).normal, Vector3.up) <= maxSlopeAngle)
                {
                    _groundContact = true;
                    return;
                }
            }
        }

        private void FixedUpdate()
        {
            _isGrounded = _groundContact;
            // 다음 스텝의 충돌 콜백이 다시 채운다. 발이 떨어지면 아무도 안 채우므로 false로 남는다.
            _groundContact = false;

            Vector2 input = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            bool sprinting = sprintAction != null && sprintAction.action.IsPressed();

            Vector3 moveDir = CameraRelativeDirection(input);
            float targetSpeed = sprinting ? sprintSpeed : walkSpeed;

            var velocity = _rb.linearVelocity;
            velocity.x = moveDir.x * targetSpeed;
            velocity.z = moveDir.z * targetSpeed;

            if (_jumpRequested && _isGrounded)
            {
                velocity.y = jumpSpeed;
                // 이번 스텝부터 즉시 공중으로 취급한다. 뛰어오른 프레임에도 접촉은 아직
                // 남아 있으므로, 이걸 끄지 않으면 같은 스텝에 두 번째 점프가 끼어든다.
                _isGrounded = false;
                _groundContact = false;
                _animator?.SetTrigger(JumpHash);

                if (logJumps)
                    Debug.Log($"[PlayerMovement] 점프 발동 — 높이 {transform.position.y:F2}m", this);
            }
            // 눌렀는데 공중이었으면 그냥 버린다. 남겨두면 착지하는 순간 저절로 튀어 오른다.
            _jumpRequested = false;

            _rb.linearVelocity = velocity;
            _horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                var targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                var rotated = Quaternion.RotateTowards(_rb.rotation, targetRot,
                    turnSpeedDegPerSec * Time.fixedDeltaTime);
                _rb.MoveRotation(rotated);
            }
        }

        private void Update()
        {
            if (_animator == null) return;
            // 애니메이터는 실제 m/s 값을 그대로 받는다. Blend Tree 문턱값을
            // walkSpeed·sprintSpeed와 같은 숫자로 맞추면 튜닝값이 두 곳에서 어긋나지 않는다.
            _animator.SetFloat(SpeedHash, _horizontalSpeed);
            _animator.SetBool(GroundedHash, _isGrounded);
        }

        /// <summary>입력을 카메라의 수평 방향 기준으로 회전시킨다. 카메라 피치는 무시한다.</summary>
        private Vector3 CameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            if (cameraTransform == null)
                return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = cameraTransform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 dir = forward * input.y + right * input.x;
            return dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }
    }
}
