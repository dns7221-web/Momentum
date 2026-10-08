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

        [Header("도착 — 여기서 속도를 새로 쓴다")]
        [Tooltip("도착 시 최소 수평 속도(m/s). 이미 이보다 빠르면 그 속도를 유지한다 — " +
                 "스윙으로 벌어둔 속도를 견인이 깎으면 '지킨다' 축이 무너진다.")]
        [Min(0f)][SerializeField] private float exitSpeed = 14f;

        [Tooltip("도착 시 위로 얹는 속도(m/s). 고정값인 것이 중요하다 — 비례시키면 " +
                 "견인을 연타할수록 높아져 감기와 같은 폭주가 다시 생긴다.")]
        [Min(0f)][SerializeField] private float exitUpSpeed = 9f;

        [Header("애니메이션")]
        [Tooltip("Zip 레이어 가중치가 오르내리는 속도(초당). 0이면 즉시 전환된다 — " +
                 "그러면 견인이 끝나는 순간 자세가 뚝 끊겨 튄다.")]
        [Min(0f)][SerializeField] private float animLayerLerpSpeed = 8f;

        /// <summary>견인 중인가. 스윙 쪽이 이 값을 보고 서로 끼어들지 않게 한다.</summary>
        public bool IsZipping { get; private set; }

        // 추가 생성
        /// <summary>
        /// 견인 입력 액션. <see cref="WebSlingshot"/>이 같은 우클릭을 지상/공중으로 나눠 쓰므로,
        /// 슬링샷 쪽 칸이 비어 있으면 이걸 빌려 간다 — 두 칸에 따로 넣어 어긋나는 일을 막는다.
        /// </summary>
        public InputActionReference ZipAction => zipAction;

        private Rigidbody _rb;
        private PlayerMovement _movement;
        private Animator _animator;

        // 추가 생성 — 우클릭을 같이 쓰는 슬링샷. 없으면 예전처럼 견인만 한다.
        private WebSlingshot _slingshot;

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
            if (gun == null) gun = GetComponent<GrappleGun>();
            _slingshot = GetComponent<WebSlingshot>();   // 추가 생성

            if (gun == null)
                Debug.LogWarning("[GrappleZip] GrappleGun이 없어 앵커를 찾을 수 없다.", this);
        }

        private void OnEnable()
        {
            if (zipAction == null) return;
            zipAction.action.Enable();
            zipAction.action.performed += OnZipPerformed;
        }

        private void OnDisable()
        {
            if (zipAction != null)
            {
                zipAction.action.performed -= OnZipPerformed;
                zipAction.action.Disable();
            }

            // 견인 중에 꺼지면 이동 쪽 스위치가 켜진 채로 남아 조작이 영영 안 먹는다.
            if (IsZipping) Stop(arrived: false);
        }

        private void OnZipPerformed(InputAction.CallbackContext ctx) => TryStart();

        /// <summary>
        /// 견인을 시작한다. 스윙 중이면 무시한다.
        ///
        /// 둘을 겹치게 두면 로프 구속과 견인이 같은 속도를 서로 덮어써서, 화면에서는
        /// 그냥 "가끔 이상하게 튄다"로 보인다. 한쪽만 쥐게 하면 그 애매함이 생기지 않는다.
        /// </summary>
        private void TryStart()
        {
            if (IsZipping || gun == null || gun.IsAttached) return;

            // 추가 생성 — 슬링샷이 이번 우클릭을 가져가면 양보한다(발이 땅에 있고 붙은 건물
            // 옥상이 훨씬 높을 때). 둘 다 같은 액션을 구독하므로 누가 먼저 불릴지 모르는데,
            // ClaimsPress()는 어느 순서로 불려도 같은 답을 준다(WebSlingshot 주석 참고).
            if (_slingshot != null && _slingshot.ClaimsPress()) return;
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
            bool flatSurface = hit.normal.y >= gun.RoofNormalMinY;
            if (flatSurface && hit.point.y < transform.position.y + minClimbHeight) return;

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

        /// <summary>
        /// 견인 자세를 켜고 끈다.
        ///
        /// <c>IsZipping</c>으로 통째로 리턴하지 않고 매 프레임 도는 이유는 스윙 레이어와
        /// 같다 — 끝나는 순간 가중치가 뚝 0으로 끊기면 자세가 튀므로, 끝난 뒤에도 계속
        /// 돌면서 서서히 내려야 한다.
        /// </summary>
        private void Update()
        {
            if (_animator == null || _zipLayerIndex < 0) return;

            float target = IsZipping ? 1f : 0f;
            float current = _animator.GetLayerWeight(_zipLayerIndex);
            float next = animLayerLerpSpeed <= 0f
                ? target
                : Mathf.MoveTowards(current, target, animLayerLerpSpeed * Time.deltaTime);
            _animator.SetLayerWeight(_zipLayerIndex, next);
        }

        private void FixedUpdate()
        {
            if (!IsZipping) return;

            _elapsed += Time.fixedDeltaTime;

            // 스윙이 시작되면 견인을 접는다. 둘 다 같은 속도를 매 스텝 덮어쓰므로 겹치면
            // 화면에서는 "가끔 이상하게 튄다"로만 보여 원인을 못 가린다. 취소당하는 쪽을
            // 견인으로 정한 것은, 줄을 걸어 타는 것이 이 게임의 본체이기 때문이다.
            if (gun.IsAttached || _targetTransform == null || _elapsed >= maxDuration)
            {
                Stop(arrived: false);
                return;
            }

            Vector3 toTarget = TargetPoint - _rb.position;
            if (toTarget.magnitude <= arrivalDistance)
            {
                Stop(arrived: true);
                return;
            }

            // 힘이 아니라 속도를 <b>대입</b>한다. 힘으로 끌면 중력과 겨루느라 궤적이
            // 처져서 어디에 도착할지 예측이 안 되고, 긴 견인일수록 그 오차가 커진다.
            // 속도 대입은 물리 엔진의 충돌 처리를 그대로 통과하므로(위치를 직접 옮기는
            // 것과 다르다) 도중에 벽을 만나면 정상적으로 막힌다.
            _rb.linearVelocity = toTarget.normalized * zipSpeed;

            // 스윙과 같은 이유로 매 스텝 다시 켠다 — 스위치를 나눠 쓰는 쪽이 둘이므로,
            // 한 번만 켜두면 상대가 끄고 간 뒤에 회복할 방법이 없다.
            if (_movement != null) _movement.SuspendHorizontalControl = true;

            // 줄을 그릴 목표도 여기서 넘긴다. LateUpdate에서 넘기면 GrappleGun의
            // LateUpdate와 실행 순서가 정해져 있지 않아, 어느 쪽이 먼저 도느냐에 따라
            // 줄이 한 프레임씩 늦거나 첫 프레임에 안 보인다.
            gun.OverrideRopeTarget = TargetPoint;
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
