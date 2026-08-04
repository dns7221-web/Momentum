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

        private Rigidbody _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 상대 속도 측정
            float relativeSpeed = collision.relativeVelocity.magnitude;

            if (relativeSpeed >= minImpactSpeed)
            {
                Debug.Log($"[CombatProp] '{gameObject.name}'에 '{collision.gameObject.name}' 💥 고속 충돌! 상대속도: {relativeSpeed:F1} m/s");

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
