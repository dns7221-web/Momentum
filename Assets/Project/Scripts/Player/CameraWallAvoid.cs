using Unity.Cinemachine;
using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 — Movie_008 "벽에 박으면 카메라도 같이 박힘" 수정 (2026-10-12)
    /// <summary>
    /// 캐릭터가 벽에 붙어 있는데 카메라가 그 벽 너머에 있으면, 카메라를 벽 <b>앞쪽(열린 쪽)</b>으로
    /// 돌려 보내는 컴포넌트. CinemachineCamera 오브젝트에 붙이기만 하면 된다(참조는 실행 중에 찾는다).
    ///
    /// <b>왜 당기지 않고 돌리는가.</b> 캐릭터가 벽에 닿아 있고 카메라가 벽 너머에 있으면, 카메라와
    /// 캐릭터를 잇는 선 위에는 좋은 자리가 없다. Cinemachine Deoccluder는 그 선을 따라 카메라를
    /// 당기기만 하는데, 타깃에서 <c>Minimum Distance From Target + Camera Radius</c>(지금 0.3 + 0.4
    /// = 0.7 m)보다 가까이는 당기지 않는다(CinemachineDeoccluder.PullCameraInFrontOfNearestObstacle).
    /// 캡슐 반지름이 0.25라 닿은 벽은 타깃에서 0.25 m — 0.7 m 지점은 벽 속이다. 그래서 "벽만 보임"과
    /// "얼굴 초근접"이 번갈아 나왔다. 값으로는 이 자리를 만들 수 없으니 선 자체를 바꾼다.
    ///
    /// <b>내장 기능을 쓰지 않은 이유.</b> Cinemachine에는 궤도 각도를 벽 반대편으로 돌려 주는 기능이
    /// 없다. Deoccluder의 Preserve Camera Distance는 장애물 모서리를 찾아 돌아가는 방식이라 건물
    /// 한 면 전체가 벽일 때는 모서리를 못 찾는다. 대신 각도를 바꾸는 통로는 내장 그대로 쓴다 —
    /// Orbital Follow의 <c>HorizontalAxis</c>(카메라가 캐릭터 주위를 도는 각도)를 움직이므로
    /// 마우스 입력, Deoccluder, 리센터링이 평소처럼 함께 동작한다.
    /// </summary>
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    // CameraHeadingTarget(-100)보다 뒤, Cinemachine Brain의 LateUpdate보다 먼저 돌아야
    // 같은 프레임에 카메라 위치가 바뀐다.
    [DefaultExecutionOrder(-90)]
    public class CameraWallAvoid : MonoBehaviour
    {
        [Tooltip("벽으로 볼 레이어. 건물(Anchorable)과 바닥(Ground) — Deoccluder의 Collide Against와 같게.")]
        [SerializeField] private LayerMask wallLayers = 0;

        [Tooltip("캐릭터에서 카메라 쪽으로 이 거리 안에 벽이 있으면 돌린다(m). " +
                 "Deoccluder가 못 다루는 0.7 m보다 조금 넓게 잡는다.")]
        [Min(0.1f)][SerializeField] private float wallCheckDistance = 1.2f;

        [Tooltip("벽 검사 구의 반지름(m). 캡슐 반지름(0.25)보다 작아야 한다 — " +
                 "더 크면 닿아 있는 벽과 처음부터 겹쳐 SphereCast가 그 벽을 못 본다.")]
        [Min(0.01f)][SerializeField] private float probeRadius = 0.15f;

        [Tooltip("발(Follow Target 위치)에서 검사 시작점까지의 높이(m). Orbital Follow의 Target Offset y와 맞춘다.")]
        [SerializeField] private float probeHeight = 0.6f;

        [Tooltip("돌린 뒤 카메라가 벽면에서 바깥으로 벌어지는 각도(°). 0이면 벽을 따라 나란히 보고, " +
                 "클수록 벽에서 떨어져 비스듬히 본다.")]
        [Range(0f, 80f)][SerializeField] private float openAngle = 25f;

        [Tooltip("목표 각도까지 다가가는 시간(초). 작을수록 빨리 돈다. 0.15면 약 0.3초에 거의 다 돈다.")]
        [Min(0.01f)][SerializeField] private float smoothTime = 0.15f;

        [Tooltip("켜면 Scene 뷰에 검사선(초록: 벽 없음, 빨강: 벽 있음)과 목표 방향(노랑)을 그린다.")]
        [SerializeField] private bool drawDebug = false;

        private CinemachineOrbitalFollow _orbital;
        private Transform _target;   // 지금 따라가는 대상(플레이어)
        private Rigidbody _body;     // 벽 정면으로 박혔을 때 어느 쪽으로 돌지 정하는 데 쓰는 속도

        /// <summary>
        /// 컴포넌트를 처음 붙였을 때(에디터) 기본 레이어를 채운다.
        /// LayerMask.GetMask는 필드 초기화에서 부를 수 없어 여기서 한다.
        /// </summary>
        private void Reset()
        {
            wallLayers = LayerMask.GetMask("Anchorable", "Ground");
        }

        private void Awake()
        {
            _orbital = GetComponent<CinemachineOrbitalFollow>();
            if (wallLayers == 0) wallLayers = LayerMask.GetMask("Anchorable", "Ground");
        }

        private void LateUpdate()
        {
            if (_orbital == null || !_orbital.IsValid) return;
            CacheTarget();
            if (_target == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return; // 일시정지 중엔 건드리지 않는다

            // 1. 캐릭터 → 카메라 수평 방향.
            //    카메라 위치는 지난 프레임 값이지만, Deoccluder가 당겨도 같은 선 위라 방향은 같다.
            Vector3 origin = _target.position + Vector3.up * probeHeight;
            Vector3 toCamera = transform.position - origin;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.0001f) return;
            toCamera.Normalize();

            // 2. 그 방향으로 가까운 벽이 있는가. SphereCast: 구를 쓸어 보내 처음 닿는 것을 찾는다(유니티 내장).
            bool hit = Physics.SphereCast(origin, probeRadius, toCamera, out RaycastHit wall,
                                          wallCheckDistance, wallLayers, QueryTriggerInteraction.Ignore);
            if (drawDebug)
                Debug.DrawRay(origin, toCamera * wallCheckDistance, hit ? Color.red : Color.green);

            // 바닥·지붕(위나 아래를 보는 면)은 벽이 아니다.
            if (!hit || Mathf.Abs(wall.normal.y) > 0.5f) return;

            Vector3 outward = wall.normal;
            outward.y = 0f;
            outward.Normalize();

            // 3. 이미 벽 앞쪽에 충분히 나와 있으면 손대지 않는다.
            float openSin = Mathf.Sin(openAngle * Mathf.Deg2Rad);
            if (Vector3.Dot(toCamera, outward) >= openSin) return;

            // 4. 목표 방향 = 벽을 따라가는 방향 + 바깥으로 openAngle만큼.
            Vector3 desired = DesiredCameraDirection(toCamera, outward, openSin);
            if (drawDebug)
                Debug.DrawRay(origin, desired * wallCheckDistance, Color.yellow);

            // 5. 지금 방향에서 목표 방향까지의 수평 각도만큼 HorizontalAxis를 돌린다.
            //    절대 각도가 아니라 "차이"를 더하므로 Orbital Follow의 Binding Mode가 무엇이든 맞다.
            //    1 - e^(-dt/smoothTime): 프레임 수와 상관없이 같은 빠르기로 다가가는 지수 감쇠.
            float delta = Vector3.SignedAngle(toCamera, desired, Vector3.up);
            float step = delta * (1f - Mathf.Exp(-dt / smoothTime));

            // HorizontalAxis는 구조체 필드라 이렇게 직접 값을 바꿀 수 있다.
            // ClampValue: 축의 범위와 Wrap(-180~180 순환) 설정대로 정리해 준다(내장).
            // 값이 바뀌면 Orbital Follow가 이를 입력으로 보고 리센터링을 잠시 멈춘다 —
            // 벽에서 떨어질 때까지 리센터링이 다시 벽 쪽으로 끌고 가지 않는다.
            _orbital.HorizontalAxis.Value = _orbital.HorizontalAxis.ClampValue(_orbital.HorizontalAxis.Value + step);
        }

        /// <summary>
        /// 카메라가 있어야 할 수평 방향(캐릭터 기준)을 구한다.
        ///
        /// 지금 방향을 벽면에 투영해 "벽을 따라가는 성분"만 남기고, 거기에 바깥쪽을 조금 섞는다.
        /// 그러면 카메라는 원래 있던 쪽(왼쪽/오른쪽)을 유지한 채 최소한만 돈다.
        /// 카메라가 벽 정면 뒤에 있으면 따라갈 성분이 없으므로, 진행 방향의 뒤쪽을 고른다.
        /// </summary>
        private Vector3 DesiredCameraDirection(Vector3 toCamera, Vector3 outward, float openSin)
        {
            // ProjectOnPlane: 벽의 법선 성분을 빼 벽면 위의 방향만 남긴다(유니티 내장).
            Vector3 along = Vector3.ProjectOnPlane(toCamera, outward);
            if (along.sqrMagnitude < 0.04f)
            {
                // 카메라가 벽 정면 뒤 — 진행 방향의 반대(캐릭터 뒤)쪽 벽면 방향을 쓴다.
                Vector3 velocity = _body != null ? _body.linearVelocity : Vector3.zero;
                along = Vector3.ProjectOnPlane(-velocity, outward);
                along.y = 0f;
                if (along.sqrMagnitude < 0.04f)
                    along = Vector3.Cross(Vector3.up, outward); // 멈춰 있으면 아무 쪽이나 한쪽
            }
            along.Normalize();

            float openCos = Mathf.Sqrt(1f - openSin * openSin);
            return (along * openCos + outward * openSin).normalized;
        }

        /// <summary>
        /// Follow Target이 바뀌었을 때만 다시 찾는다. 매 프레임 GetComponent를 부르지 않기 위함이다.
        /// </summary>
        private void CacheTarget()
        {
            Transform current = _orbital.FollowTarget;
            if (current == _target) return;
            _target = current;
            _body = current != null ? current.GetComponentInParent<Rigidbody>() : null;
        }
    }
}
