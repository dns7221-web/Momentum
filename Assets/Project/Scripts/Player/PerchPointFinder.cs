using System.Collections.Generic;
using Unity.Profiling; // 추가 생성 (2026-10-22) — 프로파일러에서 훑기 비용을 따로 보기 위해
using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-17)
    /// <summary>
    /// 포인트 런치 목표 찾기 — 인섬니악 스파이더맨의 "퍼치 포인트"처럼 <b>옥상 모서리</b>를
    /// 자동으로 찾아 아이콘으로 보여준다. 우클릭 견인(<see cref="GrappleZip"/>)이 고른 점으로 끌려간다.
    ///
    /// <b>왜 맞힌 점이 아니라 모서리인가.</b> 예전 견인은 크로스헤어가 맞힌 벽 중간으로 끌려가서,
    /// 도착해도 벽에 붙은 채 허공이었다. 인섬니악은 언제나 "올라설 수 있는 가장자리"로 데려간다.
    ///
    /// <b>여러 개 보여주기 (2026-10-19)</b> — 스파이더맨처럼 화면 안의 포인트를 <b>여러 개</b> 띄우고,
    /// 그중 <b>화면 중앙(보는 방향)에 가장 가까운 것</b>을 우클릭 목표로 고른다.
    /// <list type="number">
    /// <item>화면 가운데 영역에 격자로 레이를 쏜다(<see cref="scanGrid"/>). 정중앙은 굵은 SphereCast로 한 번 더(에임 어시스트).</item>
    /// <item>맞은 곳마다 <see cref="TryMakePoint"/>로 옥상 모서리를 계산한다 — 벽이면 그 벽 위 옥상 높이를 재고,
    ///       옥상이면 그 점을 쓴다. 사거리·높이·처마·가림 검사를 통과한 것만 남긴다.</item>
    /// <item>서로 <see cref="mergeDistance"/> 안에 붙은 점은 하나로 합치고, 화면 중앙에 가까운 순으로 <see cref="maxPoints"/>개만 둔다.</item>
    /// </list>
    /// 레이 수십 발을 매 프레임 쏘지 않도록 <see cref="scanInterval"/>마다 다시 찾고, 그 사이에는
    /// 카메라가 돌아간 만큼 "어느 게 중앙에 가장 가까운가"만 다시 고른다.
    ///
    /// <b>렉 잡기 (2026-10-22)</b> — 한 바퀴의 레이를 한 프레임에 몰아 쏘지 않고, <see cref="scanInterval"/> 동안
    /// 프레임마다 나눠 쏜다(<see cref="ScanStep"/>). 중앙부터 쏘고, 이미 찾은 모서리와 같은 자리를 맞힌 레이는
    /// 비싼 모서리 계산을 건너뛴다(<see cref="IsNearFoundPoint"/>).
    /// </summary>
    public class PerchPointFinder : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("조준 카메라·앵커 레이어를 빌려 쓴다. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GrappleGun gun = null;

        // 추가 생성 (2026-10-18) — 스윙과 따로 노는 포인트 런치 조준
        [Header("조준 — 스윙 사거리와 따로")]
        [Tooltip("포인트 런치 사거리(m). 스윙 웹 사거리(32)보다 길게 둬서 멀리서 쏴도 닿게 한다.")]
        [Min(1f)][SerializeField] private float pointRange = 60f;
        [Tooltip("정중앙 레이가 빗나가면 이 반지름(m)의 SphereCast로 한 번 더 찾는다. 0이면 끈다.")]
        [Min(0f)][SerializeField] private float aimAssistRadius = 1.5f;
        [Tooltip("공중에서는 내 발보다 이만큼(m) 아래 모서리까지 목표로 쓴다. 공중에선 보통 주변 옥상보다 높이 있기 때문.")]
        [Min(0f)][SerializeField] private float maxDropInAir = 20f;

        // 추가 생성 (2026-10-19) — 여러 포인트 찾기
        [Header("여러 포인트 — 화면을 격자로 훑기")]
        [Tooltip("화면에 쏘는 레이 격자(가로 × 세로). 많을수록 촘촘히 찾지만 레이를 더 쏜다.")]
        [SerializeField] private Vector2Int scanGrid = new Vector2Int(7, 5);
        [Tooltip("격자가 덮는 화면 범위(화면 가로·세로에 대한 비율, 중앙 기준). 1이면 화면 끝까지.")]
        [SerializeField] private Vector2 scanArea = new Vector2(0.6f, 0.5f);
        [Tooltip("아이콘 최대 개수.")]
        [Min(1)][SerializeField] private int maxPoints = 6;
        [Tooltip("이 거리(m)보다 가까운 포인트는 하나로 합친다(같은 모서리를 여러 레이가 맞히므로).")]
        [Min(0.5f)][SerializeField] private float mergeDistance = 4f;
        // 수정 (2026-10-22) — 뜻이 "몰아 쏘는 간격"에서 "나눠 쏘는 한 바퀴의 길이"로 바뀌었다(값은 그대로).
        [Tooltip("화면을 한 바퀴 훑는 데 거는 시간(초). 레이를 이 시간 동안 프레임마다 나눠 쏜다. " +
                 "그 사이엔 지난 바퀴의 포인트 중에서 카메라 방향만 보고 고른다.")]
        [Min(0.02f)][SerializeField] private float scanInterval = 0.1f;

        [Header("모서리 찾기")]
        [Tooltip("벽에서 건물 안쪽으로 이만큼 들어간 곳에서 옥상 높이를 잰다(m). " +
                 "너무 작으면 벽 모서리를 스쳐 옥상을 못 맞히고, 너무 크면 한 칸 뒤 높은 층을 잰다.")]
        [Min(0.1f)][SerializeField] private float roofProbeInset = 0.6f;

        // 수정 (2026-10-19) — 땅 전용 높이 규칙(minClimbHeight)을 없애고 땅·공중 같은 규칙으로. 대신 최소 거리.
        [Tooltip("이보다 가까운 포인트는 쓰지 않는다(m). 서 있는 옥상의 바로 옆 모서리로 끌려가는 걸 막는다.")]
        [Min(0f)][SerializeField] private float minPointDistance = 4f;

        [Tooltip("끌려갈 목표를 모서리에서 바깥(벽 법선)·위로 띄우는 거리(m). " +
                 "직선으로 끌려갈 때 모서리를 긁지 않고 넘어가게 한다.")]
        [SerializeField] private Vector2 approachOffset = new Vector2(0.3f, 1.2f);

        // 추가 생성 (2026-10-18) — 처마(코니스) 걸림 막기
        [Tooltip("처마가 벽보다 얼마나 튀어나왔는지 잴 때 벽 바깥 어디서부터 쏠지(m). 이보다 깊은 처마는 못 잰다.")]
        [Min(0.5f)][SerializeField] private float overhangProbe = 4f;
        [Tooltip("가는 길을 검사할 몸 굵기(반지름, m). 점 레이로 보면 처마 밑을 통과한다고 착각한다.")]
        [Min(0.05f)][SerializeField] private float bodyRadius = 0.35f;

        [Header("아이콘")]
        [Tooltip("포인트에 마름모 아이콘을 띄울지.")]
        [SerializeField] private bool showMarker = true;
        [Tooltip("고른 아이콘의 화면 크기(카메라와의 거리에 비례해 키워 화면에서 같은 크기로 보이게 한다).")]
        [Min(0.001f)][SerializeField] private float markerScreenSize = 0.025f;
        [Tooltip("고르지 않은 아이콘의 크기 비율(고른 것 대비).")]
        [Range(0.1f, 1f)][SerializeField] private float otherMarkerScale = 0.6f;
        [SerializeField] private Color markerColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color otherMarkerColor = new Color(0.55f, 0.55f, 0.6f, 1f);

        /// <summary>지금 우클릭하면 갈 목표가 있는가.</summary>
        public bool HasPoint { get; private set; }
        /// <summary>고른 목표 — 옥상 모서리 위의 점(올라설 자리).</summary>
        public Vector3 PerchPoint { get; private set; }
        /// <summary>고른 목표 — 끌려갈 때 실제로 향하는 점(모서리에서 바깥·위로 살짝 띄운 곳).</summary>
        public Vector3 ApproachPoint { get; private set; }
        /// <summary>고른 목표 — 모서리의 벽 법선(수평). 옥상을 맞혔으면 zero.</summary>
        public Vector3 EdgeNormal { get; private set; }
        /// <summary>고른 목표가 붙은 건물.</summary>
        public Collider PointCollider { get; private set; }

        /// <summary>찾아 둔 포인트 하나.</summary>
        private struct Point
        {
            public Vector3 Perch;
            public Vector3 Approach;
            public Vector3 EdgeNormal;
            public Collider Collider;
            public float Angle; // 추가 생성 (2026-10-22) — 화면 중앙과의 각도. 한 바퀴를 다 훑을 때 채워 정렬에 쓴다
        }

        // 추가 생성 (2026-10-22)
        /// <summary>
        /// 화면 중앙과의 각도로 포인트를 정렬하는 비교기. 한 번만 만들어 두고 계속 쓴다 —
        /// 람다로 정렬하면 카메라 값을 붙잡느라(클로저) 정렬할 때마다 새 객체가 생겨 GC가 돈다.
        /// </summary>
        private sealed class AngleComparer : IComparer<Point>
        {
            public int Compare(Point a, Point b) => a.Angle.CompareTo(b.Angle);
        }

        // 추가 생성 (2026-10-22) — 렉 잡기
        /// <summary>프로파일러(Window ▸ Analysis ▸ Profiler)의 CPU 항목에 이 이름으로 훑기 비용이 따로 찍힌다.</summary>
        private static readonly ProfilerMarker s_scanMarker = new ProfilerMarker("PerchPointFinder.Scan");
        private static readonly AngleComparer s_angleComparer = new AngleComparer();

        // 수정 (2026-10-22) — readonly 해제: 다 훑은 목록(_scanPoints)과 통째로 바꾼다.
        private List<Point> _points = new List<Point>();          // 보여주고 고르는 목록(지난 바퀴 결과)
        private List<Point> _scanPoints = new List<Point>();      // 추가 생성 (2026-10-22) — 지금 훑는 중인 목록
        private readonly List<Transform> _markers = new List<Transform>();
        private readonly List<MeshRenderer> _markerRenderers = new List<MeshRenderer>(); // 추가 생성 (2026-10-22) — GetComponent 안 부르게 챙겨 둔다
        private Material _selectedMaterial;
        private Material _otherMaterial;
        private int _selected = -1;
        private PlayerMovement _movement; // 추가 생성 (2026-10-18) — 땅/공중 판단

        // 추가 생성 (2026-10-22) — 나눠 쏘기 상태
        private Vector2[] _scanPattern;   // 쏠 화면 좌표. [0]은 정중앙, 나머지는 중앙에서 가까운 순
        private Vector2Int _patternGrid;  // 패턴을 만들 때의 scanGrid — 인스펙터에서 바꾸면 다시 만든다
        private Vector2 _patternArea;     // 패턴을 만들 때의 scanArea
        private int _scanIndex;           // 이번 바퀴에서 다음에 쏠 레이 번호

        private void Awake()
        {
            if (gun == null) gun = GetComponent<GrappleGun>();
            _movement = GetComponent<PlayerMovement>();
        }

        private void OnDisable()
        {
            foreach (var marker in _markers)
                if (marker != null) marker.gameObject.SetActive(false);
        }

        /// <summary>
        /// 일정 간격으로 화면을 다시 훑고, 매 프레임 "화면 중앙에 가장 가까운 포인트"를 고른다.
        /// 카메라는 매 프레임 돌아가므로 고르기는 매 프레임, 무거운 훑기는 가끔 한다.
        /// </summary>
        private void LateUpdate()
        {
            if (gun == null || gun.AimCamera == null)
            {
                HasPoint = false;
                return;
            }

            // 수정 (2026-10-22) — 렉 잡기. 예전엔 scanInterval마다 한 프레임에 레이 36발 + 맞은 곳마다 확인 검사
            // 최대 4번(합쳐 최대 약 180번의 물리 검사)을 몰아 해서, 0.1초마다 그 프레임만 튀었다.
            // 이제 한 바퀴(scanInterval) 동안 프레임마다 조금씩 나눠 쏜다 — 튀는 프레임이 없다.
            ScanStep();

            SelectNearestToCenter();
            UpdateMarkers();
        }

        // 추가 생성 (2026-10-22) — 예전 Scan()을 프레임마다 나눠 하도록 바꾼 것
        /// <summary>
        /// 이번 프레임 몫의 레이만 쏜다. 한 바퀴를 scanInterval 안에 끝내도록 프레임 시간에 맞춰 몫을 정한다
        /// (60fps·레이 35발·0.1초면 프레임당 6발). 다 쏘면 <see cref="FinishScan"/>으로 보여줄 목록을 바꾼다.
        /// ViewportPointToRay: 화면 좌표(0~1)를 카메라에서 나가는 레이로 바꿔 주는 유니티 내장 함수.
        ///
        /// 유니티 내장 일괄 레이(RaycastCommand)를 안 쓴 이유: 맞은 곳마다 이어지는 검사(옥상·처마·길)가 앞 결과에
        /// 달려 있어 여러 단계로 쪼개야 한다. 먼저 나눠 쏘기로 튀는 프레임을 없애고, 프로파일러로 보고 그래도 무거우면 그때 바꾼다.
        /// </summary>
        private void ScanStep()
        {
            // 일시정지(timeScale 0)면 훑지 않는다 — 예전에도 Time.time이 멈추면 안 훑었다.
            if (Time.deltaTime <= 0f) return;

            using (s_scanMarker.Auto())
            {
                EnsureScanPattern();
                int total = _scanPattern.Length;
                int budget = Mathf.Clamp(Mathf.CeilToInt(total * Time.deltaTime / scanInterval), 1, total);

                Camera cam = gun.AimCamera;
                LayerMask mask = gun.AnchorMask;
                // 사거리는 플레이어 기준이라 카메라 거리만큼 더 쏜다.
                float distance = pointRange + Vector3.Distance(cam.transform.position, transform.position);

                int end = Mathf.Min(_scanIndex + budget, total);
                for (; _scanIndex < end; _scanIndex++)
                {
                    Vector2 uv = _scanPattern[_scanIndex];
                    Ray ray = cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
                    bool found = Physics.Raycast(ray, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore);
                    // 정중앙([0])만 — 빗나가면 굵은 SphereCast(에임 어시스트). 멀리 있는 모서리는 화면에서 작기 때문.
                    if (!found && _scanIndex == 0 && aimAssistRadius > 0f)
                        found = Physics.SphereCast(ray, aimAssistRadius, out hit, distance, mask, QueryTriggerInteraction.Ignore);
                    if (found) AddPoint(hit);
                }

                if (_scanIndex >= total) FinishScan();
            }
        }

        // 추가 생성 (2026-10-22)
        /// <summary>
        /// 쏠 화면 좌표 목록을 만든다 — 정중앙을 맨 앞에, 나머지 격자는 화면 중앙에서 가까운 순.
        /// 중앙부터 쏘면 같은 모서리를 여러 레이가 맞힐 때 더 중앙에 가까운 레이가 먼저 들어가므로,
        /// 뒤에 온 레이는 비싼 모서리 계산 없이 건너뛸 수 있다(<see cref="IsNearFoundPoint"/>).
        /// 인스펙터에서 격자를 바꾸면 다음 프레임에 다시 만든다.
        /// </summary>
        private void EnsureScanPattern()
        {
            if (_scanPattern != null && _patternGrid == scanGrid && _patternArea == scanArea) return;
            _patternGrid = scanGrid;
            _patternArea = scanArea;

            Vector2 center = new Vector2(0.5f, 0.5f);
            int nx = Mathf.Max(1, scanGrid.x), ny = Mathf.Max(1, scanGrid.y);
            var uvs = new List<Vector2>(nx * ny + 1) { center };
            // 격자 — 화면 가운데 scanArea 범위를 고르게 나눠 쏜다.
            for (int ix = 0; ix < nx; ix++)
            {
                float u = nx == 1 ? 0.5f : 0.5f + (ix / (float)(nx - 1) - 0.5f) * scanArea.x;
                for (int iy = 0; iy < ny; iy++)
                {
                    float v = ny == 1 ? 0.5f : 0.5f + (iy / (float)(ny - 1) - 0.5f) * scanArea.y;
                    // 홀수 격자의 한가운데 칸은 정중앙과 같은 레이다 — 예전엔 같은 레이를 두 번 쐈다.
                    if (Mathf.Approximately(u, 0.5f) && Mathf.Approximately(v, 0.5f)) continue;
                    uvs.Add(new Vector2(u, v));
                }
            }
            // [0](정중앙)은 그대로 두고 나머지만 중앙에서 가까운 순으로. 패턴을 만들 때만 돌아서 GC 걱정이 없다.
            uvs.Sort(1, uvs.Count - 1,
                     Comparer<Vector2>.Create((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude)));
            _scanPattern = uvs.ToArray();

            // 패턴이 바뀌면 진행 중이던 바퀴는 처음부터 다시.
            _scanIndex = 0;
            _scanPoints.Clear();
        }

        // 추가 생성 (2026-10-22)
        /// <summary>
        /// 한 바퀴를 다 쏘면 화면 중앙에 가까운 순으로 maxPoints개만 남겨, 보여줄 목록과 통째로 바꾼다.
        /// 훑는 동안엔 지난 바퀴의 목록을 그대로 보여주므로 아이콘이 깜빡이지 않는다.
        /// </summary>
        private void FinishScan()
        {
            Transform cam = gun.AimCamera.transform;
            for (int i = 0; i < _scanPoints.Count; i++)
            {
                Point p = _scanPoints[i];
                p.Angle = Vector3.Angle(cam.forward, p.Perch - cam.position);
                _scanPoints[i] = p;
            }
            // 수정 (2026-10-22) — 예전 정렬 람다는 카메라 값을 붙잡아 훑을 때마다 쓰레기(GC)를 만들었다.
            // 각도를 미리 넣어 두고, 미리 만들어 둔 비교기 하나로 정렬한다.
            _scanPoints.Sort(s_angleComparer);
            if (_scanPoints.Count > maxPoints) _scanPoints.RemoveRange(maxPoints, _scanPoints.Count - maxPoints);

            // 두 목록을 맞바꾸고 새로 훑을 쪽을 비운다 — 리스트를 새로 만들지 않아 쓰레기가 안 생긴다.
            (_points, _scanPoints) = (_scanPoints, _points);
            _scanPoints.Clear();
            _scanIndex = 0;
        }

        /// <summary>맞은 곳을 포인트로 만들어, 이미 있는 포인트와 가까우면 합치고(더 중앙에 가까운 쪽을 남김) 아니면 더한다.</summary>
        private void AddPoint(RaycastHit hit)
        {
            // 추가 생성 (2026-10-22) — 같은 건물의 같은 모서리를 이미 찾았으면 비싼 모서리 계산(검사 최대 4번)을 건너뛴다.
            if (IsNearFoundPoint(hit.collider, hit.point)) return;
            if (!TryMakePoint(hit, out Point point)) return;

            // 수정 (2026-10-22) — 훑는 중인 목록(_scanPoints)에 넣는다.
            Transform cam = gun.AimCamera.transform;
            for (int i = 0; i < _scanPoints.Count; i++)
            {
                if (Vector3.Distance(_scanPoints[i].Perch, point.Perch) > mergeDistance) continue;
                if (Vector3.Angle(cam.forward, point.Perch - cam.position) <
                    Vector3.Angle(cam.forward, _scanPoints[i].Perch - cam.position))
                    _scanPoints[i] = point;
                return;
            }
            _scanPoints.Add(point);
        }

        // 추가 생성 (2026-10-22)
        /// <summary>
        /// 맞은 곳이 이번 바퀴에 이미 찾은 포인트와 같은 건물이고 수평으로 mergeDistance 안이면 true.
        /// 모서리의 수평 위치는 맞은 점의 수평 위치 그대로라(<see cref="TryMakePoint"/>), 계산해 봐야
        /// 결국 그 포인트와 합쳐진다. 중앙부터 쏘므로(<see cref="EnsureScanPattern"/>) 먼저 들어간 쪽이 대개 더 중앙이다.
        /// </summary>
        private bool IsNearFoundPoint(Collider hitCollider, Vector3 hitPoint)
        {
            float mergeSqr = mergeDistance * mergeDistance;
            for (int i = 0; i < _scanPoints.Count; i++)
            {
                if (_scanPoints[i].Collider != hitCollider) continue;
                Vector3 offset = _scanPoints[i].Perch - hitPoint;
                offset.y = 0f;
                if (offset.sqrMagnitude <= mergeSqr) return true;
            }
            return false;
        }

        /// <summary>
        /// 화면 중앙(카메라 정면)과의 각도가 가장 작은 포인트를 우클릭 목표로 고른다.
        /// 훑기 사이에도 매 프레임 다시 골라서, 카메라를 돌리면 고른 아이콘이 바로 따라 바뀐다.
        /// </summary>
        private void SelectNearestToCenter()
        {
            _selected = -1;
            float best = float.MaxValue;
            Transform cam = gun.AimCamera.transform;
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i].Collider == null) continue; // 그새 사라진 건물
                float angle = Vector3.Angle(cam.forward, _points[i].Perch - cam.position);
                if (angle < best) { best = angle; _selected = i; }
            }

            HasPoint = _selected >= 0;
            if (!HasPoint) return;
            Point p = _points[_selected];
            PerchPoint = p.Perch;
            ApproachPoint = p.Approach;
            EdgeNormal = p.EdgeNormal;
            PointCollider = p.Collider;
        }

        /// <summary>
        /// 레이가 맞은 곳 하나를 포인트로 만든다(10-17·10-18 규칙 그대로). 못 쓰는 곳이면 false.
        /// </summary>
        private bool TryMakePoint(RaycastHit hit, out Point point)
        {
            point = default;
            LayerMask mask = gun.AnchorMask;
            Vector3 feet = transform.position;
            bool roofHit = hit.normal.y >= gun.RoofNormalMinY;

            // 추가 생성 (2026-10-22) — 모서리의 수평 위치 = 맞은 점의 수평 위치라, 수평 거리만으로 사거리 밖이면
            // 실제 거리도 반드시 사거리 밖이다. 옥상 높이 재기(검사 2번) 전에 미리 버린다.
            Vector3 flat = hit.point - feet;
            flat.y = 0f;
            if (flat.sqrMagnitude > pointRange * pointRange) return false;

            Vector3 perch;
            Vector3 edgeNormal;
            if (roofHit)
            {
                // 옥상을 맞혔으면 그 자리에 올라선다.
                perch = hit.point;
                edgeNormal = Vector3.zero;
            }
            else
            {
                // 벽 법선의 수평 성분 = 벽에서 바깥으로 나가는 방향.
                edgeNormal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (edgeNormal.sqrMagnitude < 0.0001f) return false;
                edgeNormal.Normalize();

                // 벽 바로 안쪽, 사거리 높이에서 아래로 쏜다 → 처음 맞는 위를 보는 면이 이 벽의 옥상이다.
                Vector3 top = hit.point - edgeNormal * roofProbeInset;
                Vector3 start = new Vector3(top.x, hit.point.y + pointRange, top.z);
                // 시작점이 건물 속이면(사거리보다 높은 건물) 레이가 그 건물을 무시하므로 쓸 수 없다.
                if (Physics.CheckSphere(start, 0.1f, mask, QueryTriggerInteraction.Ignore)) return false;
                if (!Physics.Raycast(start, Vector3.down, out RaycastHit roof, pointRange + 1f,
                                     mask, QueryTriggerInteraction.Ignore)) return false;
                if (roof.normal.y < gun.RoofNormalMinY) return false;
                // 옥상이 맞힌 점보다 낮으면 다른 건물(뒤쪽 낮은 지붕)을 잰 것이다.
                if (roof.point.y < hit.point.y - 0.5f) return false;

                // 모서리 = 맞힌 벽 위치의 수평 좌표 + 옥상 높이.
                perch = new Vector3(hit.point.x, roof.point.y, hit.point.z);
            }

            // 수정 (2026-10-19) — 땅에서도 공중과 같은 규칙: 발 아래 maxDropInAir까지 허용(아래쪽 옥상으로도 간다).
            // 대신 너무 가까운 점(내가 서 있는 옥상의 바로 옆 모서리 등)은 뺀다.
            float minHeight = feet.y - maxDropInAir;
            if (perch.y < minHeight) return false;
            float distance = Vector3.Distance(feet, perch);
            if (distance > pointRange || distance < minPointDistance) return false;

            // 처마가 벽보다 튀어나온 만큼 목표를 더 바깥으로 뺀다. 벽 바깥에서 옥상 바로 아래 높이로 벽 쪽을 향해
            // 쏘면, 처마가 있으면 벽보다 먼저 처마 앞면에 맞는다. 맞은 점이 원래 벽보다 바깥으로 나온 거리 = 처마 돌출.
            float overhang = 0f;
            if (!roofHit)
            {
                Vector3 probe = perch + edgeNormal * overhangProbe + Vector3.down * 0.4f;
                if (Physics.Raycast(probe, -edgeNormal, out RaycastHit face, overhangProbe, mask,
                                    QueryTriggerInteraction.Ignore))
                    overhang = Mathf.Max(0f, Vector3.Dot(face.point - hit.point, edgeNormal));
            }

            Vector3 approach = perch + edgeNormal * (approachOffset.x + overhang) + Vector3.up * approachOffset.y;

            // 가는 길을 몸 굵기(SphereCast)로 쓸어본다. 처마 밑이나 모서리를 긁는 길이면 끌려가다 걸리므로
            // 아예 목표로 안 쓴다. 목표 바로 앞(0.8 m)은 그 모서리 자신이라 봐준다.
            Vector3 chest = feet + Vector3.up * 1f;
            Vector3 toApproach = approach - chest;
            if (Physics.SphereCast(chest, bodyRadius, toApproach.normalized, out RaycastHit block,
                                   toApproach.magnitude, mask, QueryTriggerInteraction.Ignore) &&
                block.distance < toApproach.magnitude - 0.8f) return false;

            point = new Point { Perch = perch, Approach = approach, EdgeNormal = edgeNormal, Collider = hit.collider };
            return true;
        }

        /// <summary>
        /// 포인트마다 마름모 아이콘을 띄운다. 고른 것은 크고 밝게, 나머지는 작고 어둡게.
        /// 에셋 없이 유니티 기본 Quad와 URP Unlit 셰이더로 만들고, 카메라 쪽으로 돌려(빌보드) 띄운다.
        /// 거리에 비례해 키워서 멀어도 화면에서 같은 크기다.
        /// </summary>
        private void UpdateMarkers()
        {
            int shown = showMarker ? _points.Count : 0;
            while (_markers.Count < shown) CreateMarker();

            Transform cam = gun.AimCamera.transform;
            for (int i = 0; i < _markers.Count; i++)
            {
                Transform marker = _markers[i];
                bool active = i < shown;
                if (marker.gameObject.activeSelf != active) marker.gameObject.SetActive(active);
                if (!active) continue;

                bool selected = i == _selected;
                Vector3 position = _points[i].Perch + Vector3.up * 0.3f;
                float size = Vector3.Distance(cam.position, position) * markerScreenSize *
                             (selected ? 1f : otherMarkerScale);
                marker.position = position;
                // 카메라를 마주 보게 하고 Z축으로 45° 돌려 마름모로 만든다.
                marker.rotation = Quaternion.LookRotation(position - cam.position, cam.up) * Quaternion.Euler(0f, 0f, 45f);
                marker.localScale = Vector3.one * size;
                // 수정 (2026-10-22) — 매 프레임 GetComponent 하지 않고 만들 때 챙겨 둔 렌더러를 쓴다. 머티리얼도 바뀔 때만 넣는다.
                MeshRenderer renderer = _markerRenderers[i];
                Material material = selected ? _selectedMaterial : _otherMaterial;
                if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
            }
        }

        /// <summary>단색 머티리얼. URP Unlit은 색을 _BaseColor로 읽어서 color(=_Color)만 바꾸면 안 바뀐다 — 둘 다 넣는다.</summary>
        private static Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        // 수정 (2026-10-22) — 만든 아이콘과 그 렌더러를 목록에 직접 넣는다(렌더러를 매 프레임 찾지 않게).
        private void CreateMarker()
        {
            if (_selectedMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _selectedMaterial = MakeMaterial(shader, markerColor);
                _otherMaterial = MakeMaterial(shader, otherMarkerColor);
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "PerchPointMarker";
            Destroy(go.GetComponent<Collider>()); // 아이콘이 레이를 막으면 안 된다
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _selectedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _markers.Add(go.transform);
            _markerRenderers.Add(renderer);
        }
    }
}
