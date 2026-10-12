using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 카메라가 "뒤로 돌아올" 방향을 알려 주는 빈 오브젝트.
    ///
    /// 플레이어의 자식으로 두고 CinemachineCamera의 Look At Target으로 지정한 뒤,
    /// Orbital Follow의 Recentering Target을 Look At Target으로 바꾼다.
    /// 그러면 마우스를 놓고 있을 때 카메라가 <b>실제로 날아가는 방향</b>의 뒤로 돌아온다.
    ///
    /// <b>왜 몸통(Player)의 방향을 그대로 쓰지 않는가.</b> 스윙 중에는 PlayerMovement가
    /// 몸통 회전을 멈추고 SwingConstraint가 캐릭터 모델만 돌린다(로프 구속과 회전이 서로
    /// 싸우지 않게 일부러 나눈 구조). 그래서 몸통의 앞은 스윙 시작 전 방향에 멈춰 있다.
    /// 이 오브젝트는 그 구조를 건드리지 않고 카메라에게만 진행 방향을 알려 준다.
    ///
    /// 위치는 부모(플레이어)를 그대로 따라가므로 카메라가 보는 점(RotationComposer)과
    /// 벽 검사 기준(Deoccluder)은 지금과 똑같다. 바뀌는 것은 회전뿐이다.
    /// </summary>
    // Cinemachine Brain도 LateUpdate에서 카메라를 움직인다. 그보다 먼저 방향을 정해야
    // 같은 프레임에 반영된다(늦으면 한 프레임씩 밀린다).
    [DefaultExecutionOrder(-100)]
    public class CameraHeadingTarget : MonoBehaviour
    {
        [Tooltip("속도를 읽을 플레이어 Rigidbody. 비우면 부모에서 찾는다.")]
        [SerializeField] private Rigidbody body = null;

        [Tooltip("수평 속도가 이보다 느리면 방향을 바꾸지 않는다(m/s). " +
                 "제자리에서 조금씩 움직일 때 카메라가 이리저리 돌지 않게 한다.")]
        [Min(0f)][SerializeField] private float minSpeed = 3f;

        [Tooltip("방향이 바뀔 때 초당 최대 회전 각도(°). 스윙 아치 끝에서 속도 방향이 " +
                 "갑자기 꺾여도 카메라 목표가 튀지 않게 한다.")]
        [Min(1f)][SerializeField] private float turnSpeedDegPerSec = 240f;

        // 지금 가리키는 방향(월드 기준). 부모가 돌아도 이 값은 유지해야 해서 따로 들고 있다.
        private Quaternion _heading;

        private void Awake()
        {
            if (body == null) body = GetComponentInParent<Rigidbody>();
            _heading = Quaternion.LookRotation(FlatForward(transform.forward), Vector3.up);
        }

        private void LateUpdate()
        {
            if (body != null)
            {
                // 수평 성분만 쓴다. 떨어지는 속도(y)까지 넣으면 카메라가 아래를 보려 한다.
                Vector3 flat = body.linearVelocity;
                flat.y = 0f;

                if (flat.sqrMagnitude >= minSpeed * minSpeed)
                {
                    Quaternion target = Quaternion.LookRotation(flat, Vector3.up);
                    // RotateTowards: 한 번에 최대 각도만큼만 돌린다(유니티 내장).
                    _heading = Quaternion.RotateTowards(_heading, target,
                                                        turnSpeedDegPerSec * Time.deltaTime);
                }
            }

            // 자식이라 부모 회전을 물려받으므로, 매 프레임 월드 회전을 다시 덮어쓴다.
            transform.rotation = _heading;
        }

        // 위·아래를 보는 벡터가 들어와도 LookRotation이 깨지지 않게 수평으로 눕힌다.
        private static Vector3 FlatForward(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }
    }
}
