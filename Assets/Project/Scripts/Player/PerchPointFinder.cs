using System.Collections.Generic;
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
        [Tooltip("다시 훑는 간격(초). 그 사이엔 카메라 방향만 보고 고른다.")]
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
        }

        private readonly List<Point> _points = new List<Point>();
        private readonly List<Transform> _markers = new List<Transform>();
        private Material _selectedMaterial;
        private Material _otherMaterial;
        private int _selected = -1;
        private float _nextScanTime;
        private PlayerMovement _movement; // 추가 생성 (2026-10-18) — 땅/공중 판단

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

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + scanInterval;
                Scan();
            }

            SelectNearestToCenter();
            UpdateMarkers();
        }

        // 추가 생성 (2026-10-19)
        /// <summary>
        /// 화면 가운데 영역을 격자로 훑어 포인트 목록을 새로 만든다.
        /// ViewportPointToRay: 화면 좌표(0~1)를 카메라에서 나가는 레이로 바꿔 주는 유니티 내장 함수.
        /// </summary>
        private void Scan()
        {
            _points.Clear();
            Camera cam = gun.AimCamera;
            LayerMask mask = gun.AnchorMask;
            // 사거리는 플레이어 기준이라 카메라 거리만큼 더 쏜다.
            float distance = pointRange + Vector3.Distance(cam.transform.position, transform.position);

            // 1) 정중앙 — 빗나가면 굵은 SphereCast(에임 어시스트). 멀리 있는 모서리는 화면에서 작기 때문.
            Ray center = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(center, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore) ||
                (aimAssistRadius > 0f &&
                 Physics.SphereCast(center, aimAssistRadius, out hit, distance, mask, QueryTriggerInteraction.Ignore)))
                AddPoint(hit);

            // 2) 격자 — 화면 가운데 scanArea 범위를 고르게 나눠 쏜다.
            int nx = Mathf.Max(1, scanGrid.x), ny = Mathf.Max(1, scanGrid.y);
            for (int ix = 0; ix < nx; ix++)
            {
                float u = nx == 1 ? 0.5f : 0.5f + (ix / (float)(nx - 1) - 0.5f) * scanArea.x;
                for (int iy = 0; iy < ny; iy++)
                {
                    float v = ny == 1 ? 0.5f : 0.5f + (iy / (float)(ny - 1) - 0.5f) * scanArea.y;
                    Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0f));
                    if (Physics.Raycast(ray, out hit, distance, mask, QueryTriggerInteraction.Ignore))
                        AddPoint(hit);
                }
            }

            // 3) 화면 중앙에 가까운 순으로 maxPoints개만 남긴다.
            Vector3 camPos = cam.transform.position, camForward = cam.transform.forward;
            _points.Sort((a, b) => Vector3.Angle(camForward, a.Perch - camPos)
                                   .CompareTo(Vector3.Angle(camForward, b.Perch - camPos)));
            if (_points.Count > maxPoints) _points.RemoveRange(maxPoints, _points.Count - maxPoints);
        }

        /// <summary>맞은 곳을 포인트로 만들어, 이미 있는 포인트와 가까우면 합치고(더 중앙에 가까운 쪽을 남김) 아니면 더한다.</summary>
        private void AddPoint(RaycastHit hit)
        {
            if (!TryMakePoint(hit, out Point point)) return;

            Transform cam = gun.AimCamera.transform;
            for (int i = 0; i < _points.Count; i++)
            {
                if (Vector3.Distance(_points[i].Perch, point.Perch) > mergeDistance) continue;
                if (Vector3.Angle(cam.forward, point.Perch - cam.position) <
                    Vector3.Angle(cam.forward, _points[i].Perch - cam.position))
                    _points[i] = point;
                return;
            }
            _points.Add(point);
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
            while (_markers.Count < shown) _markers.Add(CreateMarker());

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
                var renderer = marker.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = selected ? _selectedMaterial : _otherMaterial;
            }
        }

        /// <summary>단색 머티리얼. URP Unlit은 색을 _BaseColor로 읽어서 color(=_Color)만 바꾸면 안 바뀐다 — 둘 다 넣는다.</summary>
        private static Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        private Transform CreateMarker()
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
            return go.transform;
        }
    }
}
