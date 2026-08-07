using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 동적 프롭(드럼통, 상자, 물탱크 등)에 부착하여 플레이어 또는 다른 객체와의 충돌 시
    /// 상대 속도를 감지하고 HitFeedbackManager에 피드백을 전달하는 스크립트.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CombatProp : MonoBehaviour
    {
        [Header("프롭 정보")]
        [Tooltip("최소 타격 반응 감지 속도 (m/s)")]
        [SerializeField] private float minImpactSpeed = 5f;

        [Tooltip("PhysX가 겹침을 밀어낼 때 낼 수 있는 최대 속도(m/s) 상한. 기본값(무제한)을 " +
                 "그대로 두면, 배치할 때 지면·건물과 살짝 겹친 무거운 소품이 첫 물리 스텝에서 " +
                 "그 겹침을 한 번에 밀어내며 실제 충돌과 무관한 거대한 상대속도를 만들고, 그게 " +
                 "그대로 OnCollisionEnter로 들어가 강한 히트스톱을 걸어버린다 — 화물 컨테이너처럼 " +
                 "무거운 소품을 씬에 새로 놓았을 때 순간적으로 렉처럼 느껴지던 원인이 이것이다. " +
                 "minImpactSpeed보다 낮게 잡아 겹침 보정만으로는 히트 피드백이 절대 안 터지게 한다.")]
        [Min(0f)][SerializeField] private float maxDepenetrationVelocity = 3f;

        private Rigidbody _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.maxDepenetrationVelocity = maxDepenetrationVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 상대 속도 측정
            float relativeSpeed = collision.relativeVelocity.magnitude;

            if (relativeSpeed >= minImpactSpeed)
            {
                // 여기서 무조건 로그를 찍지 않는다 — HitFeedbackManager가 쿨다운을 적용해
                // 실제로 발동할 때만 로그를 남긴다. 복합 콜라이더 소품이 바닥에 걸쳐 미세하게
                // 흔들리며 OnCollisionEnter가 초당 수십 번 재발동하는 상황에서, 여기서 매번
                // Debug.Log(에디터의 스택 트레이스 캡처 포함)를 찍으면 그 로깅 자체가 메인
                // 스레드를 갉아먹어 렉으로 보인다 — 실제 이펙트는 쿨다운으로 막혀 있어도.
                Vector3 contactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;

                // HitFeedbackManager가 씬에 존재하는 경우 피드백 실행
                if (Player.HitFeedbackManager.Instance != null)
                {
                    Player.HitFeedbackManager.Instance.TriggerHitFeedback(relativeSpeed, contactPoint);
                }
            }
        }
    }
}
