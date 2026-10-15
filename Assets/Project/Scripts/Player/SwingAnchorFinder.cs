using UnityEngine;

namespace Momentum.Player
{
    /// <summary>
    /// 스파이더맨식 자동 앵커 탐색기. 공중에서 R을 누르면 <see cref="GrappleGun"/>이
    /// 크로스헤어 조준보다 이것을 먼저 부른다.
    ///
    /// <b>왜 조준이 아니라 자동인가.</b> 크로스헤어 레이 방식은 옥상에서 떨어지는 순간
    /// 카메라가 하늘이나 도로 통로를 보고 있어 맞을 곳이 없고, 길 건너편 벽을 맞히면
    /// 진자가 그 벽 쪽으로 흔들려 정면으로 박는다(영상 Movie_003에서 확인). 웹스윙의
    /// 손맛은 "어디를 맞히느냐"가 아니라 "앵커가 몸에 대해 어디 있느냐"가 정한다.
    ///
    /// <b>탐색 순서</b>
    /// <list type="number">
    /// <item>진행 방향(수평 속도, 없으면 카메라 정면)으로 앞·위 <see cref="idealElevation"/>
    ///       각도, 거리 <see cref="ropeLength"/>인 "이상점"을 잡는다.</item>
    /// <item>그 높이에서 좌·우로 레이를 쏴 <b>옆 건물 벽</b>을 찾는다. 앵커가 옆 벽에 생기면
    ///       진자면이 도로와 나란해져 벽에 박지 않는다.</item>
    /// <item>못 찾으면 각도를 <see cref="elevationStep"/>씩 낮춰 다시 본다. 옥상 높이에서
    ///       뛰어내린 직전에는 위쪽에 벽이 없으므로, 조금 떨어진 뒤(재시도 루프)에 붙는다.</item>
    /// </list>
    ///
    /// 레이어 마스크와 사거리는 <see cref="GrappleGun"/>이 넘겨준다 — 같은 값을 두 곳에
    /// 들고 있으면 한쪽만 고쳤을 때 어긋난다.
    /// </summary>
    public class SwingAnchorFinder : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("플레이어 Rigidbody. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private Rigidbody body = null;

        [Tooltip("진자 최저점이 닿으면 안 되는 바닥 레이어. 비우면 Ground 레이어를 쓴다. " +
                 "건물(앵커 레이어)은 GrappleGun이 넘겨주는 마스크로 함께 검사한다.")]
        [SerializeField] private LayerMask groundMask;

        [Header("이상점 — 앵커가 몸에 대해 있어야 할 자리")]
        [Tooltip("설계 줄 길이(m). 이상점까지의 거리다. 웹 사거리(22)보다 짧게 둬야 " +
                 "옆 벽까지 비켜 간 실제 앵커가 사거리 안에 남는다.")]
        // 수정 (2026-10-15) — V5 넓은 길(애비뉴 30 m, 웹 사거리 32 m)에 맞춰 18 → 26
        [Min(3f)][SerializeField] private float ropeLength = 26f;

        [Tooltip("먼저 시도할 앙각(°). 높을수록 붙자마자 크게 휘어 나간다. " +
                 "70을 넘기면 GrappleGun의 수직 매달림 문제가 돌아온다.")]
        [Range(20f, 70f)][SerializeField] private float idealElevation = 55f;

        [Tooltip("여기까지 낮춰가며 찾는다(°). 너무 낮으면 앵커가 눈높이라 붙어도 그냥 떨어진다.")]
        [Range(0f, 60f)][SerializeField] private float minElevation = 15f;

        [Tooltip("각도를 낮추는 간격(°). 작을수록 촘촘하지만 레이를 더 쏜다.")]
        [Range(2f, 30f)][SerializeField] private float elevationStep = 10f;

        [Header("벽 찾기")]
        [Tooltip("이상점에서 좌우로 벽을 찾는 거리(m). 24 m 애비뉴 한가운데서도 닿도록 " +
                 "반폭(12) + 여유.")]
        // 수정 (2026-10-15) — V5 30 m 애비뉴 한가운데서도 옆 벽에 닿게 14 → 20
        [Min(1f)][SerializeField] private float sideSearch = 20f;

        [Tooltip("앵커를 벽에서 바깥으로 띄우는 최소 거리(m). 줄이 벽을 긁지 않고 허공에 드리우게 한다.")]
        [Min(0f)][SerializeField] private float wallStandOff = 2f;

        [Tooltip("벽까지 옆 거리 중 이 비율만큼 앵커를 벽에서 띄운다(위 최소 거리보다 크면).\n\n" +
                 "진자는 앵커를 사이에 두고 좌우로도 대칭으로 흔들린다. 몸이 벽에서 12 m 떨어져 있는데 " +
                 "앵커를 벽 2 m 앞에 걸면, 아치 끝에서 반대쪽으로 10 m를 넘어가 벽에 박는다(24 m " +
                 "애비뉴 한가운데서 계산 확인). 0.5면 앵커가 몸과 벽의 중간쯤이라 아치 끝에서도 벽에 " +
                 "안 닿는다. 0이면 항상 벽 바로 앞(넓은 도로에서 박기 쉬움).")]
        [Range(0f, 0.9f)][SerializeField] private float wallStandOffFraction = 0.5f;

        [Tooltip("이보다 가까운 앵커는 버린다(m). 붙자마자 줄이 너무 짧아 휘지 못한다.")]
        [Min(0f)][SerializeField] private float minDistance = 6f;

        [Tooltip("진자 최저점 아래로 남겨야 하는 여유(m). 이만큼 안에 바닥·건물이 있으면 버린다.")]
        [Min(0f)][SerializeField] private float minGroundClearance = 3f;

        [Tooltip("이상점이 건물 속인지 검사하는 반지름(m). 속이면 그 높이는 건너뛴다.")]
        [Min(0.1f)][SerializeField] private float probeRadius = 0.5f;

        [Tooltip("수평 속도가 이보다 느리면 진행 방향 대신 카메라 정면을 쓴다(m/s).")]
        [Min(0f)][SerializeField] private float minForwardSpeed = 2f;

        // 추가 생성 (2026-10-12, Movie_006 분석)
        [Tooltip("앞으로 이 거리 안에 벽이 있으면 '앞'을 벽면을 따라 눕힌다(m). 0이면 끈다.\n\n" +
                 "벽을 긁으며 떨어질 때는 수평 속도가 거의 0이라 카메라 정면을 앞으로 쓰는데, 카메라가 " +
                 "벽을 보고 있으면 높이마다 이상점이 전부 건물 속이 되어 앵커를 하나도 못 찾는다.")]
        [Min(0f)][SerializeField] private float frontWallCheck = 6f;

        [Tooltip("벽을 따라 눕힌 방향에 벽 바깥쪽을 이만큼 섞는다. 0이면 벽과 완전히 나란하다. " +
                 "조금 섞어야 아치가 벽을 긁지 않는다.")]
        [Range(0f, 1f)][SerializeField] private float frontWallPushOut = 0.35f;

        [Header("디버그")]
        [Tooltip("켜면 마지막 탐색의 이상점과 앵커를 씬 뷰에 그린다(Gizmos).")]
        [SerializeField] private bool drawGizmos = true;

        // 마지막 탐색 결과 — Gizmos로 눈으로 확인하는 데만 쓴다.
        private Vector3 _lastIdeal;
        private Vector3 _lastAnchor;
        private bool _lastFound;

        // 좌·우 두 방향. 매번 배열을 만들지 않으려고 static으로 둔다(가비지 방지).
        private static readonly float[] Sides = { 1f, -1f };

        /// <summary>컴포넌트를 처음 붙일 때 바닥 마스크를 채워둔다. 빈 마스크는 아무것도 안 맞는다.</summary>
        private void Reset()
        {
            groundMask = LayerMask.GetMask(Level.CityGenerator.GroundLayerName);
        }

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            // GrappleGun이 실행 중에 AddComponent로 붙인 경우 Reset이 안 불리므로 여기서도 채운다.
            if (groundMask == 0) groundMask = LayerMask.GetMask(Level.CityGenerator.GroundLayerName);
        }

        /// <summary>
        /// 자동 앵커를 찾는다. 찾으면 <paramref name="hit"/>의 <c>point</c>를 벽에서 띄운
        /// 앵커 위치로 바꿔서 돌려준다(<c>collider</c>는 맞은 건물 그대로 — GrappleGun이
        /// 그 Transform 기준 로컬 좌표로 앵커를 저장한다).
        /// </summary>
        /// <param name="viewForward">수평 속도가 거의 없을 때 쓸 방향. 보통 카메라 정면.</param>
        /// <param name="anchorMask">앵커가 걸리는 레이어(건물). GrappleGun의 값.</param>
        /// <param name="maxRange">웹 사거리. GrappleGun의 maxRange(=CityPlan webRange).</param>
        /// <param name="skipGroundClearance">
        /// 수정 (2026-10-14) — true면 "진자 최저점이 땅에 닿는 앵커는 버린다" 검사를 건너뛴다.
        /// 땅에서 막 뛰어 스윙을 시작할 때만 쓴다 — 그때는 모든 앵커가 이 검사에 걸리기 때문이다.
        /// 땅에 끌리는 것은 SwingConstraint의 줄 길이 자동이 대신 막는다.
        /// </param>
        public bool TryFind(Vector3 viewForward, LayerMask anchorMask, float maxRange, out RaycastHit hit,
                            bool skipGroundClearance = false)
        {
            hit = default;
            _lastFound = false;
            if (body == null) return false;

            Vector3 pos = body.position;

            // 1) 진행 방향. Vector3.ProjectOnPlane은 벡터에서 법선(여기선 위) 성분을 빼서
            //    수평 성분만 남긴다. 떨어지는 속도(y)를 빼야 "앞"이 땅을 향하지 않는다.
            Vector3 forward = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (forward.magnitude < minForwardSpeed)
                forward = Vector3.ProjectOnPlane(viewForward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return false;
            forward.Normalize();

            // 추가 생성 — 앞이 바로 벽이면 진행 방향을 벽면을 따라 눕힌다(frontWallCheck 툴팁 참고).
            if (frontWallCheck > 0f &&
                Physics.Raycast(pos, forward, out RaycastHit front, frontWallCheck,
                                anchorMask, QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(front.normal.y) < 0.5f) // 위/아래를 보는 면(지붕·처마 밑)은 벽이 아니다
            {
                // 벽 법선의 수평 성분 = 벽에서 바깥으로 나가는 방향.
                Vector3 outward = new Vector3(front.normal.x, 0f, front.normal.z).normalized;
                // ProjectOnPlane: 벽을 파고드는 성분을 빼서 벽과 나란한 성분만 남긴다.
                Vector3 along = Vector3.ProjectOnPlane(forward, outward);
                // 벽을 정면으로 보면 나란한 성분이 거의 0 → 벽의 한쪽 접선을 쓴다.
                if (along.sqrMagnitude < 0.04f) along = Vector3.Cross(Vector3.up, outward);
                forward = (along.normalized + outward * frontWallPushOut).normalized;
            }

            // 수정 — 2)~6) 단계를 SearchAlong으로 뺐다. 코너 스윙(TryFindToward)이 같은 탐색을 쓴다.
            return SearchAlong(pos, forward, anchorMask, maxRange, out hit, skipGroundClearance);
        }

        // 추가 생성 (2026-10-14)
        /// <summary>
        /// 코너 스윙용 탐색. 진행 방향이 아니라 <paramref name="direction"/>(플레이어가 꺾고 싶은 쪽)
        /// 앞·위에서 옆 벽을 찾는다. 교차로에서 옆 길로 꺾을 때 그 길의 벽에 줄을 걸기 위해서다.
        /// 앞 벽 눕히기(frontWallCheck)는 하지 않는다 — 방향을 플레이어가 직접 정했기 때문이다.
        /// </summary>
        public bool TryFindToward(Vector3 direction, LayerMask anchorMask, float maxRange, out RaycastHit hit)
        {
            hit = default;
            _lastFound = false;
            if (body == null) return false;

            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return false;
            return SearchAlong(body.position, forward.normalized, anchorMask, maxRange, out hit);
        }

        // 추가 생성 — TryFind의 2)~6) 단계를 그대로 옮긴 탐색 본체.
        /// <summary>
        /// <paramref name="forward"/>(수평, 정규화됨) 앞·위 이상점에서 좌우 벽을 찾아 앵커를 고른다.
        /// </summary>
        private bool SearchAlong(Vector3 pos, Vector3 forward, LayerMask anchorMask, float maxRange, out RaycastHit hit,
                                 bool skipGroundClearance = false)
        {
            hit = default;

            // 진행 방향의 오른쪽. Cross(위, 앞) = 오른쪽 (유니티는 왼손 좌표계).
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // 진자 최저점 검사에 쓰는 마스크 — 바닥과 건물(아래층 셋백 지붕 포함) 둘 다 본다.
            int clearanceMask = groundMask | anchorMask;

            // 2) 높은 각도부터 낮춰가며 시도한다. 높은 앵커일수록 스윙이 크고 시원하다.
            for (float elevation = idealElevation; elevation >= minElevation - 0.01f; elevation -= elevationStep)
            {
                float rad = elevation * Mathf.Deg2Rad;
                Vector3 ideal = pos + forward * (ropeLength * Mathf.Cos(rad))
                                    + Vector3.up * (ropeLength * Mathf.Sin(rad));
                _lastIdeal = ideal;

                // 이상점이 건물 속이면 레이가 그 건물 안에서 출발해 벽을 못 맞힌다(유니티 레이는
                // 출발점을 감싼 콜라이더를 무시한다). 그 높이는 건너뛴다.
                if (Physics.CheckSphere(ideal, probeRadius, anchorMask, QueryTriggerInteraction.Ignore))
                    continue;

                // 3) 좌·우 중 가까운 벽. 가까운 쪽이 진자면을 덜 비틀어서 벽 쪽으로 덜 쏠린다.
                bool found = false;
                float bestDistance = float.MaxValue;
                RaycastHit best = default;
                foreach (float side in Sides)
                {
                    if (!Physics.Raycast(ideal, right * side, out RaycastHit wall, sideSearch,
                                         anchorMask, QueryTriggerInteraction.Ignore))
                        continue;

                    // 벽만 쓴다. 법선이 위/아래를 많이 보면 처마 밑면이나 지붕 모서리다.
                    if (Mathf.Abs(wall.normal.y) > 0.5f) continue;

                    if (wall.distance < bestDistance)
                    {
                        bestDistance = wall.distance;
                        best = wall;
                        found = true;
                    }
                }
                if (!found) continue;

                // 4) 벽에서 바깥으로 띄운다. 법선의 수평 성분만 써서 높이는 그대로 둔다.
                //    best.distance = 이상점(=내 진행선)에서 벽까지 옆 거리. 그 비율만큼 띄워서
                //    좌우 흔들림이 아치 끝에서 벽에 닿지 않게 한다(Tooltip 참고).
                Vector3 outward = new Vector3(best.normal.x, 0f, best.normal.z).normalized;
                float standOff = Mathf.Max(wallStandOff, best.distance * wallStandOffFraction);
                Vector3 anchor = best.point + outward * standOff;

                // 5) 실제 줄 길이 = 지금 거리(SwingConstraint.BeginSwing이 그렇게 정한다).
                float rope = Vector3.Distance(pos, anchor);
                if (rope > maxRange || rope < minDistance) continue;

                // 6) 진자 최저점(앵커 바로 아래 rope 지점)과 그 아래 여유에 뭐가 있으면 버린다.
                //    길이를 rope + 여유로 잡은 레이 한 방이면 "최저점이 비어 있는가"를 알 수 있다.
                // 수정 (2026-10-14) — 지상 출발이면 건너뛴다 (skipGroundClearance 설명 참고).
                if (!skipGroundClearance &&
                    Physics.Raycast(anchor, Vector3.down, rope + minGroundClearance,
                                    clearanceMask, QueryTriggerInteraction.Ignore))
                    continue;

                best.point = anchor;
                hit = best;
                _lastAnchor = anchor;
                _lastFound = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 선택했을 때 씬 뷰에 마지막 탐색 결과를 그린다. 유니티 Gizmos는 에디터에서만
        /// 그려지고 빌드에는 안 들어가므로 성능 걱정 없이 켜둬도 된다.
        /// 노란 선 = 이상점, 초록 선 = 실제로 붙은 앵커.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || body == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(body.position, _lastIdeal);
            Gizmos.DrawWireSphere(_lastIdeal, probeRadius);

            if (!_lastFound) return;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(body.position, _lastAnchor);
            Gizmos.DrawWireSphere(_lastAnchor, 0.4f);
        }
    }
}
