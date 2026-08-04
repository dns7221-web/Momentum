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
            if (relativeSpeed < minHitStopSpeed)
            {
                return;
            }

            float speedRatio = Mathf.Clamp01((relativeSpeed - minHitStopSpeed) / (maxHitStopSpeed - minHitStopSpeed));

            // 1. 히트스톱 실행
            float targetTimeScale = Mathf.Lerp(0.2f, minTimeScale, speedRatio);
            float duration = hitStopBaseDuration * (1f + speedRatio * 0.5f);
            ExecuteHitStop(targetTimeScale, duration);

            // 2. 카메라 셰이크 — Cinemachine Impulse. 방향까지 실리므로 정면충돌과
            // 스침이 다르게 흔들린다(velocity 인자가 임펄스의 방향·크기를 겸한다).
            impulseSource.GenerateImpulseAtPositionWithVelocity(
                hitPoint, Vector3.down * (relativeSpeed * impulseIntensityPerSpeed));

            // 3. 디버그 로그
            Debug.Log($"[HitFeedback] 💥 고속 충돌! 상대속도: {relativeSpeed:F1} m/s | TimeScale: {targetTimeScale:F2}");
        }

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
    }
}
