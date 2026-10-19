using Unity.Cinemachine;
using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 속도에 따라 시야각을 넓힌다 — 화면이 속도를 말하게 만드는 장치.
    ///
    /// README가 두 번 못박아둔 것이 이것이다. <b>"24.7m/s로 날고 있어도 카메라가 가만히
    /// 붙어 있으면 초속 5m처럼 보인다."</b> 물리는 이미 빠른데 화면이 그것을 보여주지 않으면
    /// 플레이어에게 그 속도는 존재하지 않는 것과 같다. 시야각이 넓어지면 화면 가장자리가
    /// 뒤로 흐르는 속도가 커지고, 그 흐름이 곧 속도감이다.
    ///
    /// 이 스크립트가 있는 이유는 <b>Cinemachine에 이 기능이 없기 때문</b>이다. 벽 회피는
    /// <c>CinemachineDeoccluder</c>, 따라오는 지연은 <c>PositionDamping</c>으로 전부 내장
    /// 기능이 처리하지만, "속도에 비례하는 시야각"만은 없어서 여기서 직접 쓴다.
    /// (<c>CinemachineFollowZoom</c>은 대상의 <b>화면 크기</b>를 유지하는 물건이라 목적이 다르다.)
    ///
    /// 스윙 전용으로 만들지 않았다. 낙하도 질주도 같은 규칙으로 넓어져야 화면이 일관되게
    /// "지금 얼마나 빠른가"만 말한다 — 상태별로 다른 규칙을 두면 전환할 때마다 시야각이
    /// 튀고, 그 튐이 속도가 아니라 상태 변화를 알리는 신호가 되어버린다.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class SpeedFieldOfView : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("속도를 읽어올 강체. 비워두면 카메라의 Tracking Target에서 찾는다.")]
        [SerializeField] private Rigidbody body = null;

        [Header("시야각")]
        [Tooltip("이 속도(m/s)에서 시야각이 최대가 된다. 스윙 최저점 속도쯤에 두면 " +
                 "아치의 바닥에서 화면이 가장 넓어진다 — 실제로 가장 빠른 순간이다.")]
        [Min(1f)][SerializeField] private float speedForMaxFov = 25f;
        [Tooltip("최대 시야각. 기준값은 인스펙터에 적지 않고 카메라에 설정된 값을 그대로 " +
                 "쓴다 — 렌즈 설정이 두 곳에 있으면 하나만 고치는 사고가 난다.")]
        [Range(40f, 120f)][SerializeField] private float maxFieldOfView = 82f;

        [Tooltip("시야각이 목표를 따라가는 속도. 클수록 즉각적이다. 너무 크면 속도가 " +
                 "출렁일 때마다 화면이 같이 펄떡여서 멀미가 난다.")]
        [Min(0.1f)][SerializeField] private float responsiveness = 3f;

        [Tooltip("수평 속도만 잴지. 켜면 수직 낙하로는 시야각이 넓어지지 않는다 — " +
                 "'앞으로 나아가는 속도'만 화면에 반영하고 싶을 때 쓴다.")]
        [SerializeField] private bool horizontalOnly = false;

        // 추가 생성 — 슬링샷 연출 (2026-10-15)
        [Header("슬링샷 연출 — 감을수록 좁게, 놓는 순간 확 넓게")]
        [Tooltip("다 감았을 때 시야각을 이만큼(°) 좁힌다. 힘이 모이는 느낌. 0이면 끈다.")]
        [Min(0f)][SerializeField] private float chargeFovNarrow = 8f;
        [Tooltip("발사 순간 시야각을 이만큼(°) 확 넓혔다가 서서히 돌아온다. 0이면 끈다.")]
        [Min(0f)][SerializeField] private float launchFovPunch = 10f;
        [Tooltip("발사 펀치가 빠지는 속도(초당 감쇠 계수). 클수록 짧게 끝난다.")]
        [Min(0.1f)][SerializeField] private float punchDecay = 4f;
        [Tooltip("처음 100%가 되는 순간의 슬로모션 배율(타임스케일). 1이면 끈다.")]
        [Range(0.05f, 1f)][SerializeField] private float fullChargeTimeScale = 0.3f;
        [Tooltip("그 슬로모션 길이(실제 초).")]
        [Min(0f)][SerializeField] private float fullChargeSlowMoTime = 0.12f;
        [Tooltip("발사 순간 화면 흔들림 세기. 0이면 끈다.")]
        [Min(0f)][SerializeField] private float launchShake = 0.6f;

        // 추가 생성 (2026-10-19) — 다이브 시야각
        [Header("다이브 — 떨어지는 속도만큼 더 넓게")]
        [Tooltip("다이브 중 낙하 속도가 diveSpeedForMax일 때 시야각에 더할 각도(°). 0이면 끈다.")]
        [Min(0f)][SerializeField] private float diveFovBonus = 10f;
        [Tooltip("이 낙하 속도(m/s)에서 다이브 보너스가 최대가 된다.")]
        [Min(1f)][SerializeField] private float diveSpeedForMax = 40f;

        private WebSlingshot _slingshot;
        private SwingDive _dive; // 추가 생성 (2026-10-19)
        private float _punch;
        private bool _wasLaunching;
        private bool _wasFull;
        // 추가 생성 — 펀치를 뺀, 속도·충전을 부드럽게 따라가는 시야각. 0이면 아직 초기화 전.
        private float _smoothedFov;

        private CinemachineCamera _camera;

        /// <summary>카메라에 설정돼 있던 원래 시야각. 정지 상태의 기준이자 돌아갈 자리다.</summary>
        private float _baseFieldOfView;

        private void Awake()
        {
            _camera = GetComponent<CinemachineCamera>();
            _baseFieldOfView = _camera.Lens.FieldOfView;

            if (body == null && _camera.Target.TrackingTarget != null)
                body = _camera.Target.TrackingTarget.GetComponentInParent<Rigidbody>();

            if (body == null)
            {
                Debug.LogWarning("[SpeedFieldOfView] 속도를 읽을 Rigidbody를 못 찾았다. " +
                                 "시야각이 고정된다.", this);
                enabled = false;
                return;
            }

            // 추가 생성 — 슬링샷 상태를 읽기만 한다(쓰지 않는다). 같은 플레이어에 붙어 있다.
            _slingshot = body.GetComponentInChildren<WebSlingshot>();
            _dive = body.GetComponentInChildren<SwingDive>(); // 추가 생성 (2026-10-19) — 실행 중에 붙으므로 Update에서도 다시 찾는다

            // 추가 생성 (2026-10-19) — 속도선은 실제 카메라에 붙어야 화면에 맞춰 흐른다. 없으면 실행 중에 붙인다.
            Camera main = Camera.main;
            if (main != null && main.GetComponent<SpeedLines>() == null)
                main.gameObject.AddComponent<SpeedLines>().Init(body);
        }

        // 추가 생성 (2026-10-19) — 공통 연출(TraversalFx)의 시야각 펀치를 듣는다.
        private void OnEnable() => TraversalFx.FovPunch += OnFovPunch;
        private void OnDisable() => TraversalFx.FovPunch -= OnFovPunch;

        /// <summary>펀치를 더한다. 이미 들어 있는 펀치보다 센 쪽(절댓값)이 이긴다 — 겹쳐도 과하게 커지지 않게.</summary>
        private void OnFovPunch(float degrees)
        {
            if (Mathf.Abs(degrees) > Mathf.Abs(_punch)) _punch = degrees;
        }

        // 추가 생성 (2026-10-15)
        /// <summary>
        /// 슬링샷 연출로 시야각에 더할 값(°)을 구하고, 순간 이벤트(100% 도달, 발사)를 처리한다.
        /// 슬링샷 쪽 코드를 건드리지 않고 공개 상태(IsCharging, Charge01, IsLaunching)만 읽어,
        /// "막 바뀐 순간"은 지난 프레임 값과 비교해서 찾는다.
        /// </summary>
        private float SlingshotFovOffset()
        {
            if (_slingshot == null) return 0f;

            bool launching = _slingshot.IsLaunching;
            bool full = _slingshot.IsCharging && _slingshot.Charge01 >= 0.999f;

            // 처음 꽉 찬 순간 — 아주 짧게 멈칫한다(히트스톱과 같은 코루틴).
            if (full && !_wasFull && fullChargeTimeScale < 1f && HitFeedbackManager.Instance != null)
                HitFeedbackManager.Instance.PlaySlowMo(fullChargeTimeScale, fullChargeSlowMoTime);

            // 발사 순간 — 시야각 펀치 + 작은 흔들림.
            if (launching && !_wasLaunching)
            {
                _punch = launchFovPunch;
                if (launchShake > 0f && HitFeedbackManager.Instance != null)
                    HitFeedbackManager.Instance.PlayShake(body.position, launchShake);
            }

            _wasFull = full;
            _wasLaunching = launching;

            // 펀치는 지수 감쇠로 빠진다(아래 시야각 따라가기와 같은 이유 — 프레임률과 무관하게).
            _punch *= Mathf.Exp(-punchDecay * Time.unscaledDeltaTime);

            float charge = _slingshot.IsCharging ? _slingshot.Charge01 : 0f;
            return _punch - chargeFovNarrow * charge;
        }

        /// <summary>
        /// <c>LateUpdate</c>가 아니라 <c>Update</c>인 이유는 <see cref="CinemachineBrain"/>이
        /// <c>LateUpdate</c>에서 렌즈를 읽어 실제 카메라에 옮기기 때문이다. 여기서 미리
        /// 정해두면 같은 프레임에 반영되고, <c>LateUpdate</c>에서 쓰면 브레인과 실행 순서를
        /// 다투게 되어 한 프레임씩 늦거나 들쭉날쭉해진다.
        /// </summary>
        private void Update()
        {
            Vector3 velocity = body.linearVelocity;
            float speed = horizontalOnly
                ? new Vector2(velocity.x, velocity.z).magnitude
                : velocity.magnitude;

            float target = Mathf.Lerp(_baseFieldOfView, maxFieldOfView,
                                      Mathf.Clamp01(speed / speedForMaxFov));

            // 추가 생성 (2026-10-19) — 다이브 중이면 낙하 속도만큼 더 넓힌다(머리부터 꽂히는 속도감).
            if (_dive == null) _dive = body.GetComponentInChildren<SwingDive>();
            if (_dive != null && _dive.IsDiving && diveFovBonus > 0f)
                target += diveFovBonus * Mathf.Clamp01(-velocity.y / diveSpeedForMax);
            // 추가 생성 — 슬링샷 충전 좁힘은 목표에 넣어 부드럽게 따라가게 한다.
            float slingOffset = SlingshotFovOffset();
            target += slingOffset - _punch;

            // 지수 감쇠로 따라간다. 단순히 Lerp(현재, 목표, 속도 × deltaTime)를 쓰면
            // 프레임률이 흔들릴 때 따라가는 속도가 같이 흔들려, 사양이 다른 기계에서
            // 손맛이 달라진다. 이 형태는 deltaTime이 어떻든 같은 시간 상수를 갖는다.
            float t = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
            // 수정 (2026-10-15) — 따라가는 값(_smoothedFov)과 화면 값을 나눈다. 발사 펀치는 따라가기를 거치지 않고
            // 바로 더해야 "확" 넓어진다(responsiveness 3으로 따라가면 펀치가 다 빠질 때까지 거의 안 보인다).
            if (_smoothedFov <= 0f) _smoothedFov = _camera.Lens.FieldOfView;
            _smoothedFov = Mathf.Lerp(_smoothedFov, target, t);
            _camera.Lens.FieldOfView = _smoothedFov + _punch;
        }
    }
}
