using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 동적 프롭(드럼통, 상자, 물탱크 등)에 부착하여 플레이어 또는 다른 객체와의 충돌 시
    /// 상대 속도를 감지하고 HitFeedbackManager에 피드백을 전달하는 스크립트.
    ///
    /// 충돌 판정 모드도 여기서 가진다 — <see cref="continuousOnlyWhileThrown"/> 참고.
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

        [Header("충돌 판정")]
        [Tooltip("평소에는 Discrete로 두고, 던져진 동안만 Continuous로 올린다.\n\n" +
                 "터널링을 막아야 하는 것은 <b>던져서 날아가는 그 한 번</b>뿐이다. 그런데 " +
                 "예전에는 도시 생성이 소품 전부에 Continuous를 박아, 옥상에 가만히 놓여 " +
                 "있는 수백 개까지 매 물리 스텝 스윕 판정을 받았다. 물리가 200Hz였으므로 " +
                 "초당 수만 번이다.\n\n" +
                 "되돌리는 시점은 <b>첫 충돌</b>이다 — 무언가에 맞은 뒤로는 굴러다니는 " +
                 "저속 운동이라 Discrete로 충분하고, 그래서 매 프레임 속도를 감시할 필요가 " +
                 "없다(감시 자체가 소품 수만큼 드는 비용이다).\n\n" +
                 "끄면 예전처럼 항상 Continuous다. 던진 소품이 벽을 뚫는 것이 보이면 " +
                 "여기부터 의심할 것.")]
        [SerializeField] private bool continuousOnlyWhileThrown = true;

        [Tooltip("날아가는 동안 쓸 모드. Continuous는 <b>정적 콜라이더</b>(건물·지면)에 " +
                 "대해서만 스윕한다 — 던진 소품이 뚫으면 안 되는 것이 그것이라 " +
                 "ContinuousDynamic(움직이는 것끼리도 스윕, 더 비쌈)까지는 필요 없다.")]
        [SerializeField] private CollisionDetectionMode thrownMode = CollisionDetectionMode.Continuous;

        private Rigidbody _rigidbody;

        /// <summary>던져져 날아가는 중인지. 첫 충돌에서 꺼진다.</summary>
        private bool _inFlight;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.maxDepenetrationVelocity = maxDepenetrationVelocity;

            // 손으로 씬에 놓아 Continuous가 이미 직렬화돼 있는 소품까지 여기서 같이 내린다.
            // 도시 생성 쪽만 고치면 프리팹으로 배치한 것들이 조용히 예전 값으로 남는다.
            if (continuousOnlyWhileThrown)
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        /// <summary>
        /// 지금 던져졌다고 알린다. 던지는 쪽(<c>WebGrabThrow</c>의 던지기·슬램)이 부른다.
        ///
        /// <b>왜 속도를 보고 스스로 판단하지 않는가.</b> 그러려면 소품마다 매 프레임
        /// 속도를 재야 하고, 그 검사가 소품 수만큼 붙는다 — 지금 줄이려는 바로 그 비용이다.
        /// 던지는 순간은 던지는 쪽이 이미 정확히 알고 있으므로, 아는 쪽이 말해주는 것이 싸다.
        /// </summary>
        public void BeginThrownFlight()
        {
            if (!continuousOnlyWhileThrown || _rigidbody == null) return;

            _rigidbody.collisionDetectionMode = thrownMode;
            _inFlight = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 맞은 순간 되돌린다. 이 뒤는 굴러다니는 저속 운동이라 스윕이 필요 없다.
            if (_inFlight)
            {
                _inFlight = false;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            // 상대 속도 측정
            float relativeSpeed = collision.relativeVelocity.magnitude;

            if (relativeSpeed >= minImpactSpeed)
            {
                // 여기서 무조건 로그를 찍지 않는다 — HitFeedbackManager가 쿨다운을 적용해
                // 실제로 발동할 때만 로그를 남긴다. 복합 콜라이더 소품이 바닥에 걸쳐 미세하게
                // 흔들리며 OnCollisionEnter가 초당 수십 번 재발동하는 상황에서, 여기서 매번
                // Debug.Log(에디터의 스택 트레이스 캡처 포함)를 찍으면 그 로깅 자체가 메인
                // 스레드를 갉아먹어 렉으로 보인다 — 실제 이펙트는 쿨다운으로 막혀 있어도.
                //
                // <b>collision.contacts를 쓰면 안 된다.</b> 그 프로퍼티는 호출할 때마다
                // ContactPoint 배열을 <b>새로 할당</b>한다. 위와 같은 상황에서는 초당 수십
                // 번씩 쓰레기가 쌓여 GC를 부르고, 그 GC가 프레임을 통째로 멈춘다. 개수는
                // contactCount로, 값은 GetContact(i)로 읽으면 할당이 없다 —
                // PlayerMovement.EvaluateContacts가 이미 그 방식이다.
                Vector3 contactPoint = collision.contactCount > 0
                    ? collision.GetContact(0).point
                    : transform.position;

                // HitFeedbackManager가 씬에 존재하는 경우 피드백 실행
                if (Player.HitFeedbackManager.Instance != null)
                {
                    Player.HitFeedbackManager.Instance.TriggerHitFeedback(relativeSpeed, contactPoint);
                }
            }
        }
    }
}
