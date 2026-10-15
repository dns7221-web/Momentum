using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 전투 증거 1단계: 충돌 시 상대 속도에 비례하여 히트스톱·카메라 셰이크·속도 표시
    /// 피드백을 총괄하는 매니저. 유니티 내장 <c>Time.timeScale</c>과 Cinemachine의
    /// 임펄스(Impulse) 기능을 활용한다.
    ///
    /// <b>셰이크를 카메라 Transform 직접 조작이 아니라 Cinemachine Impulse로 하는 이유.</b>
    /// 이 프로젝트 카메라는 Cinemachine이 매 프레임 위치를 다시 쓴다(README:
    /// "Orbital Follow가 매 프레임 위치를 덮어쓰고 Main Camera는 Brain이 그걸 복사할
    /// 뿐이다"). <see cref="SpeedFieldOfView"/>도 같은 이유로 <c>Camera.main</c>이 아니라
    /// <c>CinemachineCamera</c>를 직접 만진다. Impulse는 Cinemachine 파이프라인 안에서
    /// 도는 확장(Extension)이라 이 문제를 겪지 않는다 — Brain이 덮어쓰기 전에 이미
    /// 반영돼 있다.
    /// </summary>
    public class HitFeedbackManager : MonoBehaviour
    {
        public static HitFeedbackManager Instance { get; private set; }

        [Header("히트스톱 (HitStop)")]
        [Tooltip("히트스톱이 발동할 최소 상대 속도 (m/s)")]
        [SerializeField] private float minHitStopSpeed = 5f;
        [Tooltip("최대 효과를 받을 상대 속도 (m/s)")]
        [SerializeField] private float maxHitStopSpeed = 20f;
        [Tooltip("충돌 시 줄어들 타임스케일 최소 비율 (0.02f ~ 0.2f)")]
        [SerializeField] private float minTimeScale = 0.05f;
        [Tooltip("히트스톱 유지 프레임/시간 (리얼 타임 초)")]
        [SerializeField] private float hitStopBaseDuration = 0.08f;
        [Tooltip("히트스톱이 끝난 뒤 다음 히트스톱까지 최소 대기 시간(리얼 타임 초). " +
                 "이게 없으면 소품 하나가 데굴데굴 구르며 계속 부딪힐 때마다 히트스톱이 " +
                 "매번 다시 걸려서, Time.timeScale이 정상 속도로 돌아올 틈도 없이 슬로우모션이 " +
                 "영원히 이어지는 문제가 생긴다. 그랩 앤 슬램(V)으로 소품을 세게 던졌을 때 " +
                 "주변 소품들에 연쇄로 부딪히며 이 문제가 재현된 적이 있다 — 0.15초로는 " +
                 "연쇄 충돌 간격보다 짧아서 다 못 막았다. 인스펙터에 이미 저장된 값이라 " +
                 "이 기본값을 바꿔도 씬의 HitFeedbackManager 오브젝트에는 반영 안 된다, " +
                 "직접 올려서 테스트해볼 것.")]
        [Min(0f)][SerializeField] private float hitStopCooldown = 0.4f;

        [Header("카메라 셰이크 (Cinemachine Impulse)")]
        [Tooltip("충돌 지점의 임펄스 소스. 비워두면 이 오브젝트에 자동으로 하나 붙인다.\n\n" +
                 "이것만으로는 화면이 안 흔들린다 — 씬의 CinemachineCamera 오브젝트에 " +
                 "Add Extension으로 'Cinemachine Impulse Listener'를 반드시 추가할 것. " +
                 "리스너가 없으면 임펄스를 아무도 안 받는다.")]
        [SerializeField] private CinemachineImpulseSource impulseSource;
        [Tooltip("상대속도 1당 임펄스 강도. 값이 작으면 안 흔들리는 것처럼 느껴진다.")]
        [Min(0f)][SerializeField] private float impulseIntensityPerSpeed = 0.05f;

        private Coroutine _hitStopCoroutine;
        private float _preHitStopTimeScale = 1f;
        private bool _isHitStopActive = false;
        private float _nextHitStopAllowedTime = 0f;

        /// <summary>
        /// 손으로 소품을 붙잡아 끌고 다니는 동안(<c>WebGrabThrow</c>) 켠다. 그 소품이 주변
        /// 다른 소품을 스치면 그게 다시 옆의 소품을 치는 식으로 연쇄가 번져 히트스톱이 초당
        /// 여러 번 재발동하고 TimeScale이 거의 계속 바닥에 눌려 있는 문제가 있었다 — 부딪힌
        /// 상대 하나하나를 가려내는 대신, "지금 뭔가를 손으로 붙잡고 있다" 동안은 이 피드백
        /// 자체를 아예 꺼버리는 쪽이 훨씬 단순하고 확실하다. 던지거나 놓는 순간 바로 꺼서
        /// 정상적인 타격 피드백은 그대로 산다.
        /// </summary>
        public bool SuppressedByHold { get; set; }

        /// <summary>
        /// 스윙으로 매달려 날아가는 동안(<see cref="SwingConstraint"/>) 켠다. 옥상마다
        /// 소품이 1~4개씩 놓여 있어 아치가 옥상을 스칠 때마다 그것들을 30m/s로 들이받고,
        /// 그때마다 <c>TimeScale</c>이 0.05까지 떨어진다 — 실측(2026-08-13 에디터 로그)에서
        /// 한 세션에 18회, 그중 두 번은 3~4회 연타였다. <b>날아가다 스친 것은 타격이 아니다.</b>
        /// </summary>
        public bool SuppressedBySwing { get; set; }

        /// <summary>
        /// 둘 중 하나라도 켜져 있으면 <see cref="TriggerHitFeedback"/>이 통째로 무시된다.
        ///
        /// <b>왜 스위치를 하나로 공유하지 않는가.</b> 원래는 <c>SuppressFeedback</c> 하나를
        /// 두 쪽이 같이 썼는데, 그러면 <b>먼저 끄는 쪽이 다른 쪽의 억제까지 꺼버린다</b> —
        /// 소품을 던지는 순간(<c>SetHitFeedbackSuppressed(false)</c>) 아직 매달려 있는
        /// 스윙의 억제도 같이 풀린다. 이 프로젝트에서 이미 두 번 겪은 종류의 사고다
        /// (<c>OverrideRopeTarget</c>의 소유권 문제, <c>SuspendHorizontalControl</c>을
        /// 스윙과 견인이 나눠 갖던 문제). <b>상태를 나눠 갖게 하지 말고 각자 자기 것만
        /// 주장하게 하면</b>, 누가 언제 끄든 남의 것을 건드릴 수 없다.
        /// </summary>
        private bool Suppressed => SuppressedByHold || SuppressedBySwing;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (impulseSource == null) impulseSource = GetComponent<CinemachineImpulseSource>();
            if (impulseSource == null) impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
        }

        /// <summary>
        /// 충돌 발생 시 상대 속도와 충돌 위치를 받아 타격 피드백을 실행합니다.
        /// </summary>
        /// <param name="relativeSpeed">충돌 상대 속도 (m/s)</param>
        /// <param name="hitPoint">충돌 발생 위치</param>
        public void TriggerHitFeedback(float relativeSpeed, Vector3 hitPoint)
        {
            if (Suppressed) return;

            if (relativeSpeed < minHitStopSpeed)
            {
                return;
            }

            float speedRatio = Mathf.Clamp01((relativeSpeed - minHitStopSpeed) / (maxHitStopSpeed - minHitStopSpeed));

            // 1. 히트스톱 실행 — 쿨다운 중이면 건너뛴다. 소품이 데굴데굴 구르며 계속
            // 부딪히면 매번 다시 걸려서 슬로우모션이 영원히 안 풀리는 문제가 있었다.
            if (Time.unscaledTime >= _nextHitStopAllowedTime)
            {
                float targetTimeScale = Mathf.Lerp(0.2f, minTimeScale, speedRatio);
                float duration = hitStopBaseDuration * (1f + speedRatio * 0.5f);
                ExecuteHitStop(targetTimeScale, duration);
                _nextHitStopAllowedTime = Time.unscaledTime + duration + hitStopCooldown;

                Debug.Log($"[HitFeedback] 💥 고속 충돌! 상대속도: {relativeSpeed:F1} m/s | TimeScale: {targetTimeScale:F2}");
            }

            // 2. 카메라 셰이크 — Cinemachine Impulse. 방향까지 실리므로 정면충돌과
            // 스침이 다르게 흔들린다(velocity 인자가 임펄스의 방향·크기를 겸한다). 쿨다운과
            // 무관하게 매번 울린다 — 흔들림은 겹쳐도 자연스럽지만 슬로우모션은 안 그렇다.
            impulseSource.GenerateImpulseAtPositionWithVelocity(
                hitPoint, Vector3.down * (relativeSpeed * impulseIntensityPerSpeed));
        }

        // 추가 생성 (2026-10-15)
        /// <summary>
        /// 충돌이 아닌 연출용 슬로모션. 슬링샷을 끝까지 감은 순간처럼 "멈칫"을 주고 싶을 때 쓴다.
        /// 소품 충돌 억제(SuppressedBy…)와 쿨다운은 충돌 연쇄를 막으려는 것이라 여기엔 걸지 않는다.
        /// 내부는 히트스톱과 같은 코루틴을 써서 TimeScale 복원 규칙이 한 곳에 모인다.
        /// </summary>
        public void PlaySlowMo(float timeScale, float realDuration) => ExecuteHitStop(timeScale, realDuration);

        // 추가 생성 (2026-10-15)
        /// <summary>충돌이 아닌 연출용 화면 흔들림. 세기는 임펄스 속도 크기(작게 0.3~1).</summary>
        public void PlayShake(Vector3 at, float intensity) =>
            impulseSource.GenerateImpulseAtPositionWithVelocity(at, Vector3.down * intensity);

        private void ExecuteHitStop(float targetTimeScale, float realTimeDuration)
        {
            if (_hitStopCoroutine != null)
            {
                StopCoroutine(_hitStopCoroutine);
                Time.timeScale = _preHitStopTimeScale;
            }

            _hitStopCoroutine = StartCoroutine(HitStopRoutine(targetTimeScale, realTimeDuration));
        }

        private IEnumerator HitStopRoutine(float targetTimeScale, float realTimeDuration)
        {
            if (!_isHitStopActive)
            {
                _preHitStopTimeScale = Time.timeScale;
                _isHitStopActive = true;
            }

            Time.timeScale = targetTimeScale;
            
            // Time.timeScale 영향을 받지 않는 Realtime 대기
            yield return new WaitForSecondsRealtime(realTimeDuration);

            Time.timeScale = _preHitStopTimeScale;
            _isHitStopActive = false;
            _hitStopCoroutine = null;
        }

        private void OnDestroy()
        {
            if (_isHitStopActive)
            {
                Time.timeScale = _preHitStopTimeScale;
            }
        }

        // 꺼지기만 하고 파괴되지는 않는 경우(예: 씬 전환 중 임시 비활성화)에도 코루틴이
        // 조용히 멈추면서 Time.timeScale이 낮은 값에 갇힐 수 있어 OnDestroy와 똑같이 되돌린다.
        private void OnDisable()
        {
            if (_isHitStopActive)
            {
                Time.timeScale = _preHitStopTimeScale;
                _isHitStopActive = false;
            }
        }
    }
}
