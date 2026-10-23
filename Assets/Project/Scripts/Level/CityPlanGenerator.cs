using System;
using System.Collections.Generic;
using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 기획 v3 도시를 실제로 세우는 생성기.
    ///
    /// 기존 <see cref="CityGenerator"/>는 건드리지 않고 옆에 새로 둔다. 기존 것은 칸 반복 격자로
    /// 스윙 수치를 측정하던 도구라 비교 실험용으로 남겨둘 가치가 있고, 두 도시를 같은 플레이어로
    /// 번갈아 타보는 것 자체가 포트폴리오에서 "왜 바꿨는지"를 보여주는 근거가 된다.
    ///
    /// 계산은 <see cref="CityPlanLayout"/>이 하고, 이 클래스는 그 결과를 오브젝트로 옮기기만 한다.
    /// 레이어 이름과 물리 탐침(<see cref="RoofProbe"/>)·건물 메타(<see cref="CityBuilding"/>)는
    /// 기존 시스템 것을 그대로 쓴다 — GrappleGun이 이 이름과 컴포넌트를 기준으로 앵커를 찾기 때문이다.
    ///
    /// 전제: 생성기 오브젝트는 원점, 회전 0, 크기 1에 둔다. 모든 좌표가 기획서 좌표 그대로다.
    /// </summary>
    public partial class CityPlanGenerator : MonoBehaviour
    {
        private const string RootName = "_GeneratedPlanCity";

        /// <summary>두 면이 정확히 겹치면 z-fighting이 나므로 이만큼 띄운다.</summary>
        private const float SurfaceEpsilon = 0.01f;

        /// <summary>
        /// 이웃 회색 박스끼리 벽면이 같은 평면에 붙으면 z-fighting이 나서 이만큼 좁힌다.
        /// 스윙 고도에서 20 cm 틈은 보이지 않는다.
        /// </summary>
        private const float LotGap = 0.2f;

        [SerializeField] private CityPlanSettings settings = null;

        [Header("시각화")]
        [Tooltip("선택했을 때 생성 전 미리보기(필지·사선 벽·광장)를 씬 뷰에 그린다.")]
        [SerializeField] private bool drawGizmos = true;

        // 추가 생성 — 플레이어 시작 위치 (2026-10-14)
        [Header("플레이어 시작 위치")]
        [Tooltip("Generate가 끝나면 이 오브젝트를 시작 옥상 위로 옮긴다. 비우면 옮기지 않는다.")]
        [SerializeField] private Transform spawnTarget = null;

        public CityPlanSettings Settings => settings;
        public Transform GeneratedRoot => transform.Find(RootName);
        public bool HasGenerated => GeneratedRoot != null;

        /// <summary>도로 타일 한 종류의 실측 규격.</summary>
        private struct TileSpec
        {
            public GameObject prefab;
            public Bounds bounds;
            public float length;
            public float cross;
            public bool crossIsX;
        }

        /// <summary>세운 건물과 그 필지. 옥상 난간을 두를 때 길 쪽 변을 찾는 데 쓴다.</summary>
        private struct BuiltLot
        {
            public CityBuilding meta;
            public PlanLot lot;
            public float yaw;
            public bool greybox;
        }

        /// <summary>도로 타일 한 장이 바닥에 차지하는 직사각형 (생성기 로컬 XZ).</summary>
        private struct TileFootprint
        {
            public Vector2 center, dir, right;
            public float halfLength, halfCross;

            public Vector2 Corner(int i) =>
                center + dir * ((i & 1) == 0 ? -halfLength : halfLength) + right * ((i & 2) == 0 ? -halfCross : halfCross);
        }

        /// <summary>
        /// 기준선(origin, dir)을 따라 놓인 직사각형 띠. lat은 right 방향 거리, s는 dir 방향 거리.
        /// 사선 대로처럼 축에 맞지 않는 영역을 블록 바닥에서 파내거나 메울 때 쓴다.
        /// </summary>
        private struct CutStrip
        {
            public Vector2 origin, dir, right;
            public float latMin, latMax, s0, s1;

            public CutStrip(Vector2 origin, Vector2 dir, Vector2 right, float latMin, float latMax, float s0, float s1)
            {
                this.origin = origin;
                this.dir = dir;
                this.right = right;
                this.latMin = latMin;
                this.latMax = latMax;
                this.s0 = s0;
                this.s1 = s1;
            }
        }

        /// <summary>사선 대로 한 줄(차도 또는 한쪽 인도). 타일이 덮은 구간과, 못 덮은 곳을 메울 면의 사양.</summary>
        private sealed class DiagonalLane
        {
            public string name;
            public CutStrip area;
            public float top;
            public Material material;
            public float uvSize = 6f;
            public readonly List<CutStrip> covered = new List<CutStrip>();
        }

        /// <summary>사선 대로 전체. band는 블록 바닥에서 파낼 전체 폭(차도 + 양쪽 인도).</summary>
        private sealed class DiagonalRoad
        {
            public CutStrip band;
            public readonly List<DiagonalLane> lanes = new List<DiagonalLane>();
        }

        // ─── 중력 ───

        /// <summary>
        /// 이 도시가 가정한 중력을 실제 물리에 건다. 기존 CityGenerator와 같은 이유다 —
        /// 중력을 프로젝트 설정에 따로 적어두면 한쪽만 고치는 사고가 난다.
        /// 씬에 기존 CityGenerator가 함께 켜져 있으면 둘이 각자 중력을 걸어 결과가 정해지지
        /// 않으므로 인스펙터에서 경고한다.
        /// </summary>
        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogWarning("[CityPlanGenerator] 설정 에셋이 비어 있어 중력을 건드리지 않는다.", this);
                return;
            }
            Physics.gravity = Vector3.down * settings.GravityMagnitude;
        }

        // ─── 미리보기용 공개 함수 ───

        /// <summary>셋백 순서대로 정렬된 건물 프리팹 (Large, Medium, Small).</summary>
        public GameObject[] BuildingPrefabs => settings == null
            ? new GameObject[3]
            : new[] { settings.largePrefab, settings.mediumPrefab, settings.smallPrefab };

        /// <summary>프리팹 한 겹 실측 높이. 프리팹이나 CityBuilding이 없으면 0.</summary>
        public float[] PrefabHeights()
        {
            var prefabs = BuildingPrefabs;
            var heights = new float[3];
            for (int i = 0; i < 3; i++)
            {
                var meta = CityLayoutSettings.MetaOf(prefabs[i]);
                heights[i] = meta != null ? meta.height : 0f;
            }
            return heights;
        }

        public CityPlanLayout BuildLayout() =>
            settings == null ? null : CityPlanLayout.Build(settings, PrefabHeights());

        // ─── 생성 ───

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (settings == null)
            {
                Debug.LogError("[CityPlanGenerator] CityPlanSettings가 비어 있다.", this);
                return;
            }

            Clear();

            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);

            int anchorLayer = LayerMask.NameToLayer(CityGenerator.AnchorLayerName);
            int groundLayer = LayerMask.NameToLayer(CityGenerator.GroundLayerName);
            if (anchorLayer < 0 || groundLayer < 0)
            {
                Debug.LogWarning("[CityPlanGenerator] Anchorable / Ground 레이어가 없다. " +
                                 "웹이 아스팔트에 붙을 수 있으니 인스펙터의 레이어 생성 버튼을 먼저 누를 것.", this);
            }

            var layout = BuildLayout();

            // 도로를 먼저 깐다. 차도가 y=0보다 얼마나 파였는지 재야 지면 높이를 정할 수 있다.
            int roadTiles = CreateRoads(root.transform, layout, groundLayer, out float recess, out DiagonalRoad diagonal);
            CreateGroundAndWater(root.transform, recess, groundLayer);
            CreateSurfaces(root.transform, layout, groundLayer, diagonal);

            var built = new List<BuiltLot>();
            PrepareWhiteCity(root.transform);
            CreateLotBuildings(root.transform, layout, anchorLayer, built);
            int walls = CreateDiagonalWalls(root.transform, layout, anchorLayer);
            int landmarks = CreateLandmarks(root.transform, anchorLayer);
            CreateRail(root.transform, anchorLayer);
            int trees = CreateTrees(root.transform, anchorLayer);
            int backdrop = CreateBackdrop(root.transform, anchorLayer);
            CreateBoundary(root.transform, anchorLayer);
            _railMiss.Clear(); // 추가 생성 — 진단 기록을 이번 생성 것만 남긴다
            _propMiss.Clear();
            int rails = CreateFrontageRails(root.transform, built, anchorLayer);
            int roofProps = CreateRoofProps(root.transform, anchorLayer);
            // 추가 생성 — 진단: 난간·소품이 옥상을 못 찾은 건물을 건물별로 콘솔에 찍는다
            int diagMask = anchorLayer >= 0 ? (1 << anchorLayer) : ~0;
            LogMissReport("옥상 난간", _railMiss, diagMask);
            LogMissReport("옥상 소품", _propMiss, diagMask);
            int streetProps = CreateStreetProps(root.transform, anchorLayer);
            int clutter = CreateClutter(root.transform, layout);
            // 추가 생성 — 도시를 다시 만들 때마다 플레이어가 건물 속에 박히지 않게 시작 옥상으로 옮긴다.
            PlaceSpawn(built, anchorLayer);

            if (settings.markStatic) MarkStaticRecursive(root);

            int prefabTowers = 0;
            foreach (var b in built) if (b.lot.kind != PlanLotKind.Greybox) prefabTowers++;
            int greyLots = built.Count - prefabTowers - whiteCityLotCount;

            Debug.Log($"[CityPlanGenerator] 건물 {built.Count + walls + landmarks}채 " +
                      $"(프리팹 타워 {prefabTowers} · WhiteCity {whiteCityBuilt.Count} · 회색 박스 필지 {greyLots} · " +
                      $"사선 가로벽 {walls} · 랜드마크 {landmarks}) / 블록 {layout.blocks.Count} / " +
                      $"도로 타일 {roadTiles} / 나무 {trees} / 원경 {backdrop} / 옥상 난간 {rails} / " +
                      $"옥상 소품 {roofProps} / 도로 소품 {streetProps} / 잡동사니 {clutter} / " +
                      $"최고 필지 {layout.TallestLot():F0} m", this);

            if (whiteCityFallback > 0)
            {
                Debug.Log($"[CityPlanGenerator] WhiteCity에 맞는 건물이 없어 회색 박스로 남긴 자리 {whiteCityFallback}곳. " +
                          "대부분 봉우리 근처의 아주 높은 필지다. whiteCityMaxStretch를 올리면 줄어든다.", this);
            }
        }

        // ─── 진단: 옥상을 못 찾은 건물 (2026-10-15) ───

        // 추가 생성 — 건물별로 못 찾은 개수. 생성할 때마다 비운다.
        private readonly Dictionary<CityBuilding, int> _railMiss = new Dictionary<CityBuilding, int>();
        private readonly Dictionary<CityBuilding, int> _propMiss = new Dictionary<CityBuilding, int>();

        // 추가 생성
        /// <summary>못 찾은 횟수를 건물별로 하나 올린다.</summary>
        private static void RecordMiss(Dictionary<CityBuilding, int> misses, CityBuilding building)
        {
            if (building == null) return;
            misses.TryGetValue(building, out int count);
            misses[building] = count + 1;
        }

        // 추가 생성
        /// <summary>
        /// 못 찾은 건물을 한 줄씩 찍는다. 원인을 가르려고 <b>건물 중심</b>에서도 옥상을 한 번 찾아본다.
        /// 중심에서는 찾는데 가장자리에서 못 찾으면 → 건물이 상자 모양이 아니다(바운즈 모서리 위가 허공).
        /// 중심에서도 못 찾으면 → 꼭대기에 평평한 면이 없다(첨탑·경사 지붕).
        /// </summary>
        private void LogMissReport(string what, Dictionary<CityBuilding, int> misses, int anchorMask)
        {
            if (misses.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[진단] {what}이(가) 옥상을 못 찾은 건물 {misses.Count}채");
            foreach (var pair in misses)
            {
                CityBuilding b = pair.Key;
                if (b == null) continue;
                // WhiteCity·vrbn 건물은 루트 아래 첫 자식이 모델(이름 = 프리팹 이름)이다.
                string model = b.transform.childCount > 0 ? b.transform.GetChild(0).name : "?";
                Vector3 origin = b.transform.position + Vector3.up * (b.height + RoofProbe.Margin);
                bool centerRoof = RoofProbe.TryFindRoofFromTop(origin, b.height + RoofProbe.Margin * 2f,
                                                               b.transform, anchorMask, out Vector3 roof);
                string center = centerRoof ? $"중심 옥상 있음(y {roof.y:F1}, 계획 {b.height:F0})" : "중심 옥상 없음";
                sb.AppendLine($"  {b.name} [{model}] {pair.Value}개 — {center}, " +
                              $"위치 ({b.transform.position.x:F0}, {b.transform.position.z:F0}), " +
                              $"바닥 {b.footprint.x:F1} × {b.footprint.y:F1} m");
            }
            Debug.Log(sb.ToString(), this);
        }

        // ─── 플레이어 시작 위치 ───

        // 추가 생성 (2026-10-14)
        /// <summary>
        /// 플레이어를 spawnAvenue(기본 A3, R-A 중앙 직선)의 남쪽 끝에서 <b>가장 높은 옥상</b>,
        /// 애비뉴 쪽 가장자리에 북쪽(+Z)을 보게 세운다.
        ///
        /// 웹스윙 기준으로 고른 자리다. 높은 곳은 위치에너지라 첫 낙하에서 바로 속도가 붙고,
        /// 앞에는 양쪽 벽이 12 m 거리에 있는 474 m 직선이 놓여 시작하자마자 스윙으로 이어진다.
        /// 좌표를 손으로 박아 두면 도시를 다시 만들 때마다 건물 속에 갇히므로 생성 결과에서 찾는다.
        /// </summary>
        private void PlaceSpawn(List<BuiltLot> built, int anchorLayer)
        {
            if (spawnTarget == null) return;

            PlanAvenue avenue = settings.SortedAvenues().Find(a => a.id == settings.spawnAvenue);
            if (avenue == null)
            {
                Debug.LogWarning($"[CityPlanGenerator] 시작 애비뉴 '{settings.spawnAvenue}'가 없어 플레이어를 옮기지 않았다.", this);
                return;
            }

            // 1) Z 범위 안에서 애비뉴를 바라보는 필지 중 가장 높은 것.
            //    서쪽 면 필지의 서쪽 끝이 애비뉴 동쪽 끝에 붙어 있거나, 동쪽 면 필지의 동쪽 끝이 애비뉴 서쪽 끝에 붙어 있으면 바라보는 것이다.
            bool found = false;
            BuiltLot best = default;
            foreach (var b in built)
            {
                float cz = (b.lot.z0 + b.lot.z1) * 0.5f;
                if (cz < settings.spawnZRange.x || cz > settings.spawnZRange.y) continue;

                bool faces = (b.lot.side == PlanLotSide.West && Mathf.Abs(b.lot.x0 - avenue.MaxX) < 0.5f) ||
                             (b.lot.side == PlanLotSide.East && Mathf.Abs(b.lot.x1 - avenue.MinX) < 0.5f);
                if (!faces) continue;

                if (!found || b.meta.height > best.meta.height)
                {
                    best = b;
                    found = true;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"[CityPlanGenerator] {settings.spawnAvenue}를 바라보는 필지가 Z {settings.spawnZRange} 안에 없어 " +
                                 "플레이어를 옮기지 않았다.", this);
                return;
            }

            // 2) 옥상의 애비뉴 쪽 가장자리에서 spawnEdgeInset만큼 안쪽, 필지 Z 가운데.
            float edgeX = best.lot.side == PlanLotSide.West
                ? best.lot.x0 + settings.spawnEdgeInset
                : best.lot.x1 - settings.spawnEdgeInset;
            float z = (best.lot.z0 + best.lot.z1) * 0.5f;

            Vector3 roof;
            if (best.greybox)
            {
                // 회색 박스는 평평한 박스 윗면이라 높이를 그대로 안다 (RoofProbe는 BoxCollider를 무시한다).
                roof = transform.TransformPoint(new Vector3(edgeX, best.meta.height, z));
            }
            else
            {
                // 에셋 건물은 파라펫·실외기·안테나가 섞여 있어 실제 밟히는 옥상을 레이로 찾는다.
                int mask = anchorLayer >= 0 ? (1 << anchorLayer) : ~0;
                Vector3 origin = transform.TransformPoint(new Vector3(edgeX, best.meta.height + RoofProbe.Margin, z));
                if (!RoofProbe.TryFindRoofFromTop(origin, best.meta.height + RoofProbe.Margin * 2f,
                                                  best.meta.transform, mask, out roof))
                {
                    Debug.LogWarning($"[CityPlanGenerator] 시작 필지 {best.lot.id}에서 옥상 면을 못 찾아 플레이어를 옮기지 않았다.", this);
                    return;
                }
            }

            // 3) Undo에 남기고 옮긴다. 5 cm 띄워 시작 프레임에 바닥과 겹치지 않게 한다.
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(spawnTarget, "Place Player Spawn");
#endif
            spawnTarget.SetPositionAndRotation(roof + Vector3.up * 0.05f,
                                               Quaternion.LookRotation(transform.forward, Vector3.up));

            Debug.Log($"[CityPlanGenerator] 플레이어 시작 위치: {best.lot.id} 옥상 ({roof.x:F1}, {roof.y:F1}, {roof.z:F1}), " +
                      $"{settings.spawnAvenue}를 따라 북쪽을 본다.", spawnTarget);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            var root = GeneratedRoot;
            if (root == null) return;
            DestroyObject(root.gameObject);
        }

        // ─── 도로 ───

        /// <summary>
        /// 애비뉴(차도 + 양쪽 인도 띠) → 스트리트 → 교차로 → 사선 대로 순으로 깐다.
        /// 광장 원 안의 타일은 건너뛴다 — 광장 바닥이 그 자리를 덮는다.
        /// 사선 대로는 블록 하나 안에 통째로 들어가는 타일만 깔고, 덮은 구간을 <paramref name="diagonal"/>에 기록한다.
        /// </summary>
        /// <param name="recess">가장 깊이 파인 차도 표면 깊이. 지면 박스를 그보다 아래에 둔다.</param>
        private int CreateRoads(Transform parent, CityPlanLayout layout, int layer, out float recess, out DiagonalRoad diagonal)
        {
            recess = 0f;
            diagonal = null;
            var roads = new GameObject("_Roads");
            roads.transform.SetParent(parent, false);

            var avenues = settings.SortedAvenues();
            var streets = settings.SortedStreets();
            var rows = settings.BlockRows();
            var cols = settings.BlockColumns();

            float avenueWidth = avenues.Count > 0 ? avenues[0].width : 24f;
            float streetWidth = streets.Count > 0 ? streets[0].width : 12f;

            bool hasStrip = TryMakeTileSpec(settings.sidewalkStripPrefab, roads.transform,
                                            settings.sidewalkStripWidth, out TileSpec strip);
            // 수정 (2026-10-15) — 인도를 한쪽에 여러 줄(sidewalkStripsPerSide) 깔 수 있게 한다. V5 넓은 애비뉴용.
            int stripsPerSide = Mathf.Max(1, settings.sidewalkStripsPerSide);
            float expectedAvenueRoad = hasStrip ? avenueWidth - strip.cross * 2f * stripsPerSide : avenueWidth;
            bool hasAvenue = TryMakeTileSpec(settings.avenueRoadPrefab, roads.transform, expectedAvenueRoad, out TileSpec avenue);
            bool hasStreet = TryMakeTileSpec(settings.streetRoadPrefab, roads.transform, streetWidth, out TileSpec street);

            Bounds cross = default;
            bool hasCross = settings.intersectionPrefab != null &&
                            TryMeasurePrefab(settings.intersectionPrefab, roads.transform, out cross);

            if (hasAvenue) recess = Mathf.Max(recess, -avenue.bounds.min.y);
            if (hasStrip) recess = Mathf.Max(recess, -strip.bounds.min.y);
            if (hasStreet) recess = Mathf.Max(recess, -street.bounds.min.y);
            if (hasCross) recess = Mathf.Max(recess, -cross.min.y);

            if (hasAvenue)
            {
                float total = avenue.cross + (hasStrip ? strip.cross * 2f * stripsPerSide : 0f); // 수정 — 여러 줄 인도 포함
                if (Mathf.Abs(total - avenueWidth) > 0.1f)
                {
                    Debug.LogWarning($"[CityPlanGenerator] 애비뉴 타일 조합 폭이 {total:F2} m인데 설정 폭은 " +
                                     $"{avenueWidth:F2} m다. 블록 가장자리에 틈이나 겹침이 생긴다. " +
                                     "애비뉴 폭을 타일 조합에 맞추거나 인도 띠 프리팹을 바꿀 것.", this);
                }
            }

            bool InPlaza(Vector2 p)
            {
                foreach (var plaza in settings.plazas)
                {
                    if (plaza != null && Vector2.Distance(p, plaza.center) < plaza.radius) return true;
                }
                return false;
            }

            bool SkipPlaza(TileFootprint f) => InPlaza(f.center);

            // 사선 타일은 블록 하나 안에 통째로 들어갈 때만 깐다. 애비뉴·스트리트·산책로에 조금이라도 걸치면
            // 같은 높이 면끼리 겹쳐 z-fighting이 난다. 못 깐 자리는 바닥면 단계에서 메시로 메운다.
            bool InsideOneBlock(TileFootprint f)
            {
                const float e = 0.01f;
                foreach (var b in layout.blocks)
                {
                    bool all = true;
                    for (int k = 0; k < 4 && all; k++)
                    {
                        Vector2 p = f.Corner(k);
                        all = p.x >= b.x0 - e && p.x <= b.x1 + e && p.y >= b.z0 - e && p.y <= b.z1 + e;
                    }
                    if (all) return true;
                }
                return false;
            }

            bool SkipDiagonal(TileFootprint f) => InPlaza(f.center) || !InsideOneBlock(f);

            int placed = 0;

            // 애비뉴 — 블록 행 하나 길이씩 끊어 깐다. 교차로 자리는 비워두고 아래에서 따로 채운다.
            if (hasAvenue)
            {
                foreach (var a in avenues)
                {
                    foreach (var row in rows)
                    {
                        var mid = new Vector2(a.centerX, (row.x + row.y) * 0.5f);
                        if (settings.park.Contains(mid)) continue; // A3는 공원 안에서 끊긴다

                        var from = new Vector2(a.centerX, row.x);
                        var to = new Vector2(a.centerX, row.y);
                        placed += LayRun(avenue, from, to, 0f, roads.transform, layer, SkipPlaza);

                        if (hasStrip)
                        {
                            // 수정 (2026-10-15) — 차도 바깥으로 인도 띠를 stripsPerSide줄 이어 깐다.
                            for (int k = 0; k < stripsPerSide; k++)
                            {
                                float offset = avenue.cross * 0.5f + strip.cross * (k + 0.5f);
                                placed += LayRun(strip, from, to, offset, roads.transform, layer, SkipPlaza);
                                placed += LayRun(strip, from, to, -offset, roads.transform, layer, SkipPlaza);
                            }
                        }
                    }
                }
            }

            // 스트리트 — 블록 열 하나 폭씩.
            if (hasStreet)
            {
                foreach (var st in streets)
                {
                    foreach (var col in cols)
                    {
                        placed += LayRun(street, new Vector2(col.x, st.centerZ), new Vector2(col.y, st.centerZ),
                                         0f, roads.transform, layer, SkipPlaza);
                    }
                }
            }

            // 교차로 — 애비뉴 폭 × 스트리트 폭 직사각형을 6 × 6 아스팔트로 채운다.
            if (hasCross)
            {
                foreach (var a in avenues)
                {
                    foreach (var st in streets)
                    {
                        var c = new Vector2(a.centerX, st.centerZ);
                        if (InPlaza(c)) continue;
                        placed += TileArea(settings.intersectionPrefab, cross, c, a.width, st.width, roads.transform, layer);
                    }
                }
            }

            // 사선 대로 — 같은 4차선 타일을 사선 각도로 돌려 깐다. 타일이 덮은 구간을 줄마다 기록해 두면
            // 바닥면 단계에서 블록 바닥을 띠 모양으로 파내고, 타일이 못 들어간 끝부분을 같은 높이 면으로 메운다.
            if (hasAvenue)
            {
                Vector2 from = settings.diagonalFrom, to = settings.diagonalTo;
                float length = Vector2.Distance(from, to);
                if (length > 0.01f)
                {
                    Vector2 dir = (to - from) / length;
                    Vector2 right = new Vector2(dir.y, -dir.x); // LayRun의 lateral 방향과 같다
                    float half = avenue.cross * 0.5f;
                    float outer = half + (hasStrip ? strip.cross * stripsPerSide : 0f); // 수정 — 여러 줄 인도 포함
                    diagonal = new DiagonalRoad { band = new CutStrip(from, dir, right, -outer, outer, 0f, length) };

                    // 차도 메움 면은 교차로 아스팔트에서 잰다. 차선 무늬가 없어 월드 좌표로 반복해도 어색하지 않다.
                    var asphalt = new DiagonalLane { name = "Asphalt", area = new CutStrip(from, dir, right, -half, half, 0f, length) };
                    if (hasCross)
                    {
                        asphalt.uvSize = Mathf.Max(cross.size.x, cross.size.z);
                        MeasureLane(asphalt, settings.intersectionPrefab, new Vector2(cross.center.x, cross.center.z),
                                    roads.transform, avenue.bounds.min.y);
                    }
                    else
                    {
                        asphalt.uvSize = avenue.length;
                        Vector2 lane = new Vector2(avenue.bounds.center.x, avenue.bounds.center.z) +
                                       (avenue.crossIsX ? Vector2.right : Vector2.up) * (avenue.cross * 0.25f);
                        MeasureLane(asphalt, avenue.prefab, lane, roads.transform, avenue.bounds.min.y);
                    }
                    placed += LayRun(avenue, from, to, 0f, roads.transform, layer, SkipDiagonal, asphalt.covered);
                    diagonal.lanes.Add(asphalt);

                    if (hasStrip)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            float sign = k == 0 ? 1f : -1f;
                            var walk = new DiagonalLane
                            {
                                name = k == 0 ? "Sidewalk_R" : "Sidewalk_L",
                                area = sign > 0f ? new CutStrip(from, dir, right, half, outer, 0f, length)
                                                 : new CutStrip(from, dir, right, -outer, -half, 0f, length),
                                uvSize = strip.cross,
                            };
                            MeasureLane(walk, strip.prefab, new Vector2(strip.bounds.center.x, strip.bounds.center.z),
                                        roads.transform, strip.bounds.max.y);
                            // 수정 (2026-10-15) — 사선 대로도 인도 띠를 stripsPerSide줄 이어 깐다. 덮은 구간은 같은 면(walk)에 모은다.
                            for (int s = 0; s < stripsPerSide; s++)
                            {
                                float offset = half + strip.cross * (s + 0.5f);
                                placed += LayRun(strip, from, to, offset * sign, roads.transform, layer, SkipDiagonal, walk.covered);
                            }
                            diagonal.lanes.Add(walk);
                        }
                    }
                }
            }

            return placed;
        }

        /// <summary>
        /// 프리팹을 재서 "가로 폭"과 "진행 방향 길이"를 알아낸다.
        /// 어느 축이 가로인지는 기대 폭에 가까운 쪽으로 판정한다 — 키트마다 모델 방향이 달라서
        /// 축을 설정에 적어두면 에셋을 바꿀 때마다 틀린다.
        /// </summary>
        private bool TryMakeTileSpec(GameObject prefab, Transform probeParent, float expectedCross, out TileSpec spec)
        {
            spec = default;
            if (prefab == null) return false;
            if (!TryMeasurePrefab(prefab, probeParent, out Bounds b)) return false;

            bool crossIsX = Mathf.Abs(b.size.x - expectedCross) <= Mathf.Abs(b.size.z - expectedCross);
            spec.prefab = prefab;
            spec.bounds = b;
            spec.crossIsX = crossIsX;
            spec.cross = crossIsX ? b.size.x : b.size.z;
            spec.length = crossIsX ? b.size.z : b.size.x;

            if (Mathf.Abs(spec.cross - expectedCross) > 0.1f)
            {
                Debug.LogWarning($"[CityPlanGenerator] '{prefab.name}' 폭이 {spec.cross:F2} m인데 " +
                                 $"기대 폭은 {expectedCross:F2} m다.", this);
            }
            return true;
        }

        /// <summary>
        /// 시작점에서 끝점까지 타일을 한 줄로 깐다. 방향이 임의라 애비뉴·스트리트·사선이 같은 함수를 쓴다.
        /// 길이가 타일로 나누어지지 않으면 남는 길이를 양 끝에 반씩 틈으로 둔다 — 마지막 타일을
        /// 억지로 끼우면 교차로 타일과 겹쳐 z-fighting이 나기 때문이다.
        /// </summary>
        /// <param name="lateral">진행 방향 오른쪽으로 띄울 거리. 인도 띠를 차도 옆에 붙일 때 쓴다.</param>
        /// <param name="covered">연속으로 깐 타일 묶음마다 덮은 띠를 여기에 더한다 (사선 바닥 파내기용).</param>
        private int LayRun(TileSpec spec, Vector2 from, Vector2 to, float lateral, Transform parent, int layer,
                           Func<TileFootprint, bool> skip, List<CutStrip> covered = null)
        {
            Vector2 span = to - from;
            float length = span.magnitude;
            if (length < 0.01f || spec.length < 0.01f) return 0;

            int count = Mathf.FloorToInt(length / spec.length + 0.02f);
            if (count <= 0) return 0;

            Vector2 dir = span / length;
            Vector2 right = new Vector2(dir.y, -dir.x);
            float pad = (length - count * spec.length) * 0.5f;
            float runYaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            // 타일의 가로 축이 진행 방향에 수직이 되게 돌린다.
            float tileYaw = runYaw + (spec.crossIsX ? 0f : 90f);

            int placed = 0, runStart = -1;
            for (int i = 0; i <= count; i++)
            {
                bool laid = false;
                if (i < count)
                {
                    var f = new TileFootprint
                    {
                        center = from + dir * (pad + spec.length * (i + 0.5f)) + right * lateral,
                        dir = dir,
                        right = right,
                        halfLength = spec.length * 0.5f,
                        halfCross = spec.cross * 0.5f,
                    };
                    if (skip == null || !skip(f))
                    {
                        PlaceTile(spec.prefab, spec.bounds, f.center, tileYaw, parent, layer);
                        placed++;
                        laid = true;
                    }
                }

                if (laid)
                {
                    if (runStart < 0) runStart = i;
                    continue;
                }
                if (runStart >= 0 && covered != null)
                {
                    covered.Add(new CutStrip(from, dir, right, lateral - spec.cross * 0.5f, lateral + spec.cross * 0.5f,
                                             pad + spec.length * runStart, pad + spec.length * i));
                }
                runStart = -1;
            }
            return placed;
        }

        /// <summary>직사각형 구역을 타일로 채운다 (교차로). 개수는 구역 크기를 타일 크기로 나눠 정한다.</summary>
        private int TileArea(GameObject prefab, Bounds bounds, Vector2 center, float spanX, float spanZ,
                             Transform parent, int layer)
        {
            float tileX = Mathf.Max(0.01f, bounds.size.x);
            float tileZ = Mathf.Max(0.01f, bounds.size.z);
            int countX = Mathf.Max(1, Mathf.RoundToInt(spanX / tileX));
            int countZ = Mathf.Max(1, Mathf.RoundToInt(spanZ / tileZ));

            int placed = 0;
            for (int i = 0; i < countX; i++)
            {
                for (int j = 0; j < countZ; j++)
                {
                    float tx = center.x - spanX * 0.5f + tileX * (i + 0.5f);
                    float tz = center.y - spanZ * 0.5f + tileZ * (j + 0.5f);
                    PlaceTile(prefab, bounds, new Vector2(tx, tz), 0f, parent, layer);
                    placed++;
                }
            }
            return placed;
        }

        /// <summary>
        /// 타일 중심이 목표 지점에 오도록 놓는다. 높이는 y=0 — 이 키트는 y=0이 인도 표면이고
        /// 차도가 그 아래로 파여 있다. 피벗이 중앙이 아닌 모델이 있어 바운즈 중심으로 보정한다.
        /// </summary>
        private void PlaceTile(GameObject prefab, Bounds bounds, Vector2 target, float yaw, Transform parent, int layer)
        {
            var tile = InstantiateLinked(prefab);
            tile.transform.SetParent(parent, false);

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            tile.transform.localRotation = rotation;
            Vector3 pivotOffset = rotation * new Vector3(bounds.center.x, 0f, bounds.center.z);
            tile.transform.localPosition = new Vector3(target.x - pivotOffset.x, 0f, target.y - pivotOffset.z);

            if (layer >= 0) SetLayerRecursive(tile, layer);
            // 정적인 바닥이라 오목한 메시 충돌체를 그대로 쓴다. 볼록(convex)으로 감싸면
            // 인도(y=0)와 차도(y=-0.15)의 단차가 사라져 차도 위를 떠서 걷게 된다.
            if (settings.surfaceColliders) AddMeshColliders(tile, false);
            // 추가 생성 (2026-10-23) — 평평한 바닥은 그림자를 드리우지 않는다(렌더 최적화, groundCastShadows 설명 참고).
            if (!settings.groundCastShadows) DisableShadowCasting(tile);
        }

        // 추가 생성 (2026-10-23)
        /// <summary>
        /// 그 아래 모든 렌더러가 그림자를 드리우지 않게 한다. 받는 쪽(receiveShadows)은 그대로 둬서 건물 그림자는 계속 진다.
        /// 이미 깔린 씬의 바닥은 에디터 메뉴 Momentum/렌더 최적화의 "바닥 조각 그림자"로 같은 일을 한다.
        /// </summary>
        private static void DisableShadowCasting(GameObject go)
        {
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>사선 메움 면의 높이·머티리얼을 실제 타일에서 잰다. 못 재면 바운즈 값과 첫 머티리얼로 대신한다.</summary>
        private void MeasureLane(DiagonalLane lane, GameObject prefab, Vector2 at, Transform parent, float fallbackTop)
        {
            lane.top = fallbackTop;
            lane.material = FirstMaterial(prefab);
            if (!ProbeSurface(prefab, at, parent, out float top, out Material material)) return;
            lane.top = top;
            if (material != null) lane.material = material;
        }

        /// <summary>
        /// 프리팹을 잠깐 놓고 위에서 레이를 쏴 실제 밟히는 면 높이와 그 면의 머티리얼을 잰다.
        /// 바운즈 최저점은 메시 두께가 있으면 바닥 밑면이라 표면 높이로 믿을 수 없다.
        /// </summary>
        private bool ProbeSurface(GameObject prefab, Vector2 at, Transform parent, out float top, out Material material)
        {
            top = 0f;
            material = null;
            if (prefab == null) return false;

            var probe = InstantiateLinked(prefab);
            probe.transform.SetParent(parent, false);
            probe.transform.localPosition = Vector3.zero;
            probe.transform.localRotation = Quaternion.identity;

            var added = new List<MeshCollider>();
            foreach (var filter in probe.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                added.Add(mc);
            }
            Physics.SyncTransforms();

            var ray = new Ray(parent.TransformPoint(new Vector3(at.x, 50f, at.y)), Vector3.down);
            float best = float.MaxValue;
            foreach (var mc in added)
            {
                if (!mc.Raycast(ray, out RaycastHit hit, 100f) || hit.distance >= best) continue;
                best = hit.distance;
                top = parent.InverseTransformPoint(hit.point).y;
                material = MaterialAt(mc, hit.triangleIndex);
            }

            DestroyObject(probe);
            return best < float.MaxValue;
        }

        /// <summary>레이가 맞은 삼각형이 속한 서브메시의 머티리얼.</summary>
        private static Material MaterialAt(MeshCollider mc, int triangle)
        {
            var renderer = mc.GetComponent<Renderer>();
            if (renderer == null) return null;
            var materials = renderer.sharedMaterials;
            var mesh = mc.sharedMesh;
            if (mesh != null && triangle >= 0)
            {
                int index = triangle * 3;
                for (int i = 0; i < mesh.subMeshCount && i < materials.Length; i++)
                {
                    var sub = mesh.GetSubMesh(i);
                    if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount) return materials[i];
                }
            }
            return materials.Length > 0 ? materials[0] : null;
        }

        private static Material FirstMaterial(GameObject prefab)
        {
            var renderer = prefab != null ? prefab.GetComponentInChildren<Renderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }

        // ─── 지면 · 강 · 바닥면 ───

        /// <summary>
        /// 땅 지면 박스, 강바닥, 수면, 부두.
        /// 수면에는 충돌체가 없고 그 바로 아래 강바닥이 받는다 — 강에 떨어지면 물속에 서 있게 된다.
        /// 헤엄이나 리스폰은 아직 없다.
        /// </summary>
        private void CreateGroundAndWater(Transform parent, float recess, int groundLayer)
        {
            var root = new GameObject("_GroundWater");
            root.transform.SetParent(parent, false);

            float groundTop = -recess - SurfaceEpsilon;
            // 가장자리를 5 cm 들인다. 산책로 박스의 강 쪽 벽면과 지면 박스 옆면이 같은 평면에 놓여
            // 강에서 안벽을 보면 z-fighting이 났다.
            const float inset = 0.05f;
            var land = settings.land;
            var ground = new PlanRect(land.x0 + inset, land.x1 - inset, land.z0 + inset, land.z1 - inset);
            CreateSlab(root.transform, "Ground", ground, groundTop, 5f, settings.groundMaterial, groundLayer, true);

            for (int i = 0; i < settings.water.Count; i++)
            {
                var area = settings.water[i];
                if (area == null) continue;
                CreateSlab(root.transform, $"Riverbed_{i}", area, settings.riverbedY, 5f, settings.groundMaterial, groundLayer, true);
                CreateSlab(root.transform, $"Water_{i}", area, settings.waterSurfaceY, 0.1f, settings.waterMaterial, -1, false);
            }

            var paving = Pick(settings.pavingMaterial, settings.groundMaterial);
            float pierDepth = -settings.riverbedY + 0.2f;
            for (int i = 0; i < settings.piers.Count; i++)
            {
                if (settings.piers[i] == null) continue;
                CreateSlab(root.transform, $"Pier_{i}", settings.piers[i], 0f, pierDepth, paving, groundLayer, true);
            }
        }

        /// <summary>
        /// 산책로, 블록 마당, 공원, 광장 바닥.
        /// 타일 수천 장 대신 박스 몇 개로 덮는다 — 산책로만 타일로 깔아도 3천 장이 넘어 에디터가 무거워진다.
        /// 산책로 박스는 강바닥까지 내려 그대로 안벽(岸壁)이 된다.
        /// </summary>
        private void CreateSurfaces(Transform parent, CityPlanLayout layout, int groundLayer, DiagonalRoad diagonal)
        {
            var root = new GameObject("_Surfaces");
            root.transform.SetParent(parent, false);

            var paving = Pick(settings.pavingMaterial, settings.groundMaterial);
            float quayDepth = -settings.riverbedY + 0.2f;

            CreateSlab(root.transform, "Promenade_West", settings.westPromenade, 0f, quayDepth, paving, groundLayer, true);
            CreateSlab(root.transform, "Promenade_South", settings.southPromenade, 0f, quayDepth, paving, groundLayer, true);

            // 블록 마당은 박스 대신 메시로 세운다. 사선 대로가 지나는 블록은 그 띠를 파내야 하는데 박스로는
            // 사선으로 자를 수 없다. 예전엔 박스 윗면(y=0)이 사선 인도 타일(y=0)과 겹쳐 z-fighting이 났고,
            // 차도(y≈−0.15)는 박스 밑에 묻혀 보이지 않았다.
            foreach (var b in layout.blocks)
            {
                var pieces = new List<List<Vector2>> { RectPolygon(b.x0, b.x1, b.z0, b.z1) };
                if (diagonal != null) Subtract(pieces, diagonal.band);
                CreateExtrusion(root.transform, $"Block_{b.id}", pieces, 0f, -1f, paving, settings.pavingUvSize, groundLayer);
            }

            // 사선 타일이 못 들어간 자리(애비뉴·광장에 비스듬히 닿는 끝부분)를 같은 높이 면으로 메운다.
            // 메움 = (블록 ∩ 차선 띠) − 타일이 덮은 구간. 타일·메움·블록 바닥이 서로 한 치도 겹치지 않는다.
            if (diagonal != null)
            {
                foreach (var lane in diagonal.lanes)
                {
                    var pieces = new List<List<Vector2>>();
                    foreach (var b in layout.blocks)
                    {
                        var inside = ClipToStrip(RectPolygon(b.x0, b.x1, b.z0, b.z1), lane.area);
                        if (inside.Count < 3 || PolygonArea(inside) < MinPieceArea) continue;
                        var list = new List<List<Vector2>> { inside };
                        foreach (var run in lane.covered) Subtract(list, run);
                        pieces.AddRange(list);
                    }
                    if (pieces.Count > 0)
                    {
                        CreateExtrusion(root.transform, $"DiagonalFill_{lane.name}", pieces, lane.top, lane.top - 0.3f,
                                        lane.material, lane.uvSize, groundLayer);
                    }
                }
            }

            CreateSlab(root.transform, "Park", settings.park, 0f, 1f, Pick(settings.parkMaterial, paving), groundLayer, true);

            foreach (var plaza in settings.plazas)
            {
                if (plaza == null) continue;
                // 원판 하나. 원기둥 프리미티브는 캡슐 충돌체를 달고 오는데 납작하게 누르면
                // 모양이 안 맞아서, 메시 충돌체로 바꾼다.
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = $"Plaza_{plaza.id}";
                disc.transform.SetParent(root.transform, false);
                const float thickness = 0.2f;
                disc.transform.localScale = new Vector3(plaza.radius * 2f, thickness * 0.5f, plaza.radius * 2f);
                // 도로 인도면(y=0)과 겹치지 않게 3 cm 올린다.
                disc.transform.localPosition = new Vector3(plaza.center.x, 0.03f - thickness * 0.5f, plaza.center.y);
                if (paving != null) disc.GetComponent<MeshRenderer>().sharedMaterial = paving;
                DestroyCollider(disc);
                var mc = disc.AddComponent<MeshCollider>();
                mc.sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;
                if (groundLayer >= 0) disc.layer = groundLayer;
            }
        }

        /// <summary>윗면 높이와 두께로 축 정렬 박스를 만든다. 바닥면·원경·철도가 전부 이걸 쓴다.</summary>
        private static GameObject CreateSlab(Transform parent, string name, PlanRect area, float topY, float thickness,
                                             Material material, int layer, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(area.x1 - area.x0, thickness, area.z1 - area.z0);
            go.transform.localPosition = new Vector3((area.x0 + area.x1) * 0.5f, topY - thickness * 0.5f, (area.z0 + area.z1) * 0.5f);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) DestroyCollider(go);
            if (layer >= 0) go.layer = layer;
            return go;
        }

        // ─── 건물 ───

        /// <summary>
        /// 필지마다 건물을 세운다. 프리팹 필지는 겹 쌓기 타워, 나머지는 회색 박스.
        /// 타워는 필지 가운데가 아니라 <b>길 쪽 변에 붙여</b> 세운다 — 블록 둘레 벽이
        /// 한 줄로 이어지는 것(가로벽)이 이번 기획의 핵심이라 정면을 길에 맞춘다.
        /// </summary>
        private void CreateLotBuildings(Transform parent, CityPlanLayout layout, int anchorLayer, List<BuiltLot> built)
        {
            var root = new GameObject("_Buildings");
            root.transform.SetParent(parent, false);
            var prefabs = BuildingPrefabs;

            for (int i = 0; i < layout.lots.Count; i++)
            {
                var lot = layout.lots[i];
                var orient = new PlanRandom(settings.orientationSeed + i * 7919);
                bool greybox = lot.kind == PlanLotKind.Greybox;
                float yaw = 0f;
                GameObject go;

                if (greybox)
                {
                    // WhiteCity 건물을 먼저 시도하고, 맞는 게 없으면 회색 박스로 남긴다.
                    float sizeX = lot.x1 - lot.x0 - LotGap, sizeZ = lot.z1 - lot.z0 - LotGap;
                    go = TryCreateWhiteCity(lot.id, sizeX, sizeZ, lot.height, orient);
                    if (go != null)
                    {
                        greybox = false; // 옥상 난간을 레이로 찾게
                        whiteCityLotCount++;
                    }
                    else
                    {
                        go = CreateGreyboxVolume(lot.id, sizeX, sizeZ, lot.height, GreyboxMaterial(orient));
                    }
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(lot.Center.x, 0f, lot.Center.y);
                }
                else
                {
                    go = CreatePrefabTower(lot, prefabs, orient);
                    if (go == null) continue;

                    // 애비뉴 면(서·동)은 프리팹의 긴 변(로컬 X)이 Z를 따라가야 하므로 90° 돌린다.
                    // 90/270, 0/180 중 어느 쪽인지는 난수 — 같은 얼굴이 블록마다 같은 방향을 보지 않게.
                    var baseMeta = CityLayoutSettings.MetaOf(prefabs[(int)lot.kind]);
                    bool alongZ = lot.side == PlanLotSide.West || lot.side == PlanLotSide.East;
                    bool flip = orient.Next() < 0.5;
                    yaw = alongZ ? (flip ? 270f : 90f) : (flip ? 180f : 0f);

                    // 깊이 방향 크기는 회전 후에도 프리팹 로컬 Z(footprint.y)다.
                    float depth = baseMeta.footprint.y;
                    Vector2 c = lot.Center;
                    switch (lot.side)
                    {
                        case PlanLotSide.West: c.x = lot.x0 + depth * 0.5f; break;
                        case PlanLotSide.East: c.x = lot.x1 - depth * 0.5f; break;
                        case PlanLotSide.South: c.y = lot.z0 + depth * 0.5f; break;
                        default: c.y = lot.z1 - depth * 0.5f; break;
                    }

                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(c.x, 0f, c.y);
                    go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }

                var meta = go.GetComponent<CityBuilding>();
                meta.gridX = lot.row;
                meta.gridZ = lot.col;
                go.name = $"{lot.id}_{meta.height:F0}m";
                if (anchorLayer >= 0) SetLayerRecursive(go, anchorLayer);
                built.Add(new BuiltLot { meta = meta, lot = lot, yaw = yaw, greybox = greybox });
            }
        }

        /// <summary>
        /// 프리팹을 셋백 순서로 쌓은 타워. 기존 CityGenerator.CreatePrefabBuilding과 같은 방식이다
        /// (겹이 들고 온 CityBuilding 제거, 겹 높이 누적, 맨 위 표면 실측 보정).
        /// 기존 함수는 private이고 CityLayoutSettings에 묶여 있어 재사용 대신 옮겨 왔다.
        /// </summary>
        private GameObject CreatePrefabTower(PlanLot lot, GameObject[] prefabs, PlanRandom orient)
        {
            int baseIndex = (int)lot.kind;
            var tower = new GameObject("Tower");
            var meta = tower.AddComponent<CityBuilding>();

            float cursor = 0f;
            int floors = 0;
            int stacked = 0;
            Vector2 top = Vector2.zero;
            GameObject topInstance = null;

            for (int t = 0; t < lot.tiers; t++)
            {
                var prefab = prefabs[Mathf.Min(baseIndex + t, prefabs.Length - 1)];
                var tierMeta = CityLayoutSettings.MetaOf(prefab);
                if (prefab == null || tierMeta == null)
                {
                    Debug.LogError($"[CityPlanGenerator] {lot.id}: 겹 {t}에 쓸 프리팹이나 CityBuilding이 없다.", this);
                    break;
                }

                var instance = InstantiateLinked(prefab);
                instance.name = $"Tier{t}_{prefab.name}";
                instance.transform.SetParent(tower.transform, false);

                // 180°만 쓴다. 90°는 직사각형 바닥이 아래 겹 밖으로 튀어나와 진자가 부딪히는 턱이 된다.
                float flip = settings.rotateTiers && orient.Next() < 0.5 ? 180f : 0f;
                instance.transform.localRotation = Quaternion.Euler(0f, flip, 0f);
                instance.transform.localPosition = new Vector3(0f, cursor, 0f);

                StripComponent(instance.GetComponent<CityBuilding>());

                cursor += tierMeta.height;
                floors += tierMeta.floorCount;
                top = tierMeta.footprint;
                topInstance = instance;
                stacked++;
            }

            if (stacked == 0)
            {
                DestroyObject(tower);
                return null;
            }

            meta.height = cursor;
            meta.floorCount = floors;
            meta.footprint = top;
            meta.isShell = true;

            if (stacked > 1 && topInstance != null) VerifyTopSurface(tower, topInstance, meta, cursor, stacked);
            return tower;
        }

        /// <summary>
        /// 겹 높이를 더한 값과 실제로 밟히는 맨 위 표면이 다를 수 있어, 조립이 끝난 실물에 대고 다시 잰다.
        /// 맨 위 겹의 BoxCollider는 그 겹이 혼자 있다고 가정하고 구운 값이라 함께 보정한다.
        /// (기존 CityGenerator의 같은 보정을 그대로 옮김)
        /// </summary>
        private static void VerifyTopSurface(GameObject tower, GameObject topInstance, CityBuilding meta, float cursor, int tiers)
        {
            int probeMask = LayerMask.GetMask(CityGenerator.AnchorLayerName);
            if (probeMask == 0) probeMask = ~0;

            float margin = RoofProbe.Margin * (1 + tiers);
            Vector3 origin = tower.transform.TransformPoint(new Vector3(0f, cursor + margin, 0f));
            if (!RoofProbe.TryFindWalkableSurface(origin, margin * 2f, tower.transform, probeMask, out Vector3 verified)) return;

            float verifiedHeight = tower.transform.InverseTransformPoint(verified).y;
            var topBox = topInstance.GetComponent<BoxCollider>();
            if (topBox != null && Mathf.Abs(verifiedHeight - cursor) > 0.05f)
            {
                float tierFloor = cursor - topBox.size.y;
                float tierLocalHeight = Mathf.Max(0.1f, verifiedHeight - tierFloor);
                topBox.size = new Vector3(topBox.size.x, tierLocalHeight, topBox.size.z);
                topBox.center = new Vector3(topBox.center.x, tierLocalHeight * 0.5f, topBox.center.z);
            }
            meta.height = verifiedHeight;
        }

        /// <summary>바닥 피벗 회색 박스 한 채. 루트에 CityBuilding, 자식 큐브에 BoxCollider.</summary>
        private static GameObject CreateGreyboxVolume(string name, float sizeX, float sizeZ, float height, Material material)
        {
            var root = new GameObject(name);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(sizeX, height, sizeZ);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            if (material != null) body.GetComponent<MeshRenderer>().sharedMaterial = material;

            var meta = root.AddComponent<CityBuilding>();
            meta.height = height;
            meta.floorCount = Mathf.Max(1, Mathf.RoundToInt(height / CityLayoutSettings.FloorHeight));
            meta.footprint = new Vector2(sizeX, sizeZ);
            meta.isShell = false;
            return root;
        }

        /// <summary>회색 박스 머티리얼을 번갈아 고른다. MaterialPropertyBlock 대신 머티리얼을 여러 개 쓰는
        /// 이유: 속성 블록을 쓰면 정적 배칭과 SRP Batcher에서 빠진다.</summary>
        private Material GreyboxMaterial(PlanRandom rng)
        {
            var list = settings.greyboxMaterials;
            int valid = 0;
            foreach (var m in list) if (m != null) valid++;
            if (valid == 0) return null;

            int pick = (int)(rng.Next() * valid);
            foreach (var m in list)
            {
                if (m == null) continue;
                if (pick-- == 0) return m;
            }
            return null;
        }

        /// <summary>사선 대로 양옆 회색 박스. 로컬 Z가 사선 방향을 보도록 돌린다.</summary>
        private int CreateDiagonalWalls(Transform parent, CityPlanLayout layout, int anchorLayer)
        {
            var root = new GameObject("_DiagonalWalls");
            root.transform.SetParent(parent, false);

            for (int i = 0; i < layout.diagonalWalls.Count; i++)
            {
                var w = layout.diagonalWalls[i];
                var rng = new PlanRandom(settings.orientationSeed ^ (i * 104729));
                string id = $"{w.id}_{w.height:F0}m";
                var go = settings.whiteCityOnDiagonalWalls ? TryCreateWhiteCity(id, w.depth, w.length, w.height, rng) : null;
                if (go == null) go = CreateGreyboxVolume(id, w.depth, w.length, w.height, GreyboxMaterial(rng));
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(w.center.x, 0f, w.center.y);
                go.transform.localRotation = Quaternion.Euler(0f, w.yawDeg, 0f);
                if (anchorLayer >= 0) SetLayerRecursive(go, anchorLayer);
            }
            return layout.diagonalWalls.Count;
        }

        /// <summary>
        /// 랜드마크 — 3단 셋백 회색 박스. 아래가 넓고 위로 좁아져서 멀리서도 다른 건물과 구분된다.
        /// 단마다 생기는 중간 옥상은 그대로 앵커 선반이 된다.
        /// </summary>
        private int CreateLandmarks(Transform parent, int anchorLayer)
        {
            var root = new GameObject("_Landmarks");
            root.transform.SetParent(parent, false);
            float[] share = { 0.55f, 0.30f, 0.15f };
            float[] scale = { 1f, 0.78f, 0.56f };
            var material = Pick(settings.landmarkMaterial, GreyboxMaterial(new PlanRandom(1)));
            int count = 0;

            foreach (var lm in settings.landmarks)
            {
                if (lm == null) continue;
                var go = new GameObject($"{lm.id}_{lm.height:F0}m");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(lm.area.Center.x, 0f, lm.area.Center.y);

                Vector2 size = lm.area.Size;
                float y = 0f;
                // 추가 생성 — 단마다 건물 목록에서 맞는 건물을 고를 난수. 랜드마크마다 다르게.
                var tierRng = new PlanRandom(settings.whiteCitySeed ^ (count * 7919 + 17));
                for (int i = 0; i < 3; i++)
                {
                    float h = lm.height * share[i];

                    // 추가 생성 — 단 하나하나를 건물 목록의 건물로 세운다. 랜드마크 전체를 건물 한 채로
                    // 바꾸지 않는 이유: 기획안(City Plan v3)에서 3단 셋백의 중간 옥상이 앵커 선반이다.
                    // 단을 쌓는 구조를 그대로 두면 선반 높이(55%·85% 지점)도 그대로 남는다.
                    // TryCreateWhiteCity는 바닥 중앙 피벗 루트를 돌려주므로 단의 바닥 높이(y)에 올리면 된다.
                    if (settings.whiteCityOnLandmarks)
                    {
                        var fitted = TryCreateWhiteCity($"Tier{i}", size.x * scale[i], size.y * scale[i], h, tierRng);
                        if (fitted != null)
                        {
                            fitted.transform.SetParent(go.transform, false);
                            fitted.transform.localPosition = new Vector3(0f, y, 0f);
                            y += h;
                            continue;
                        }
                    }

                    var tier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tier.name = $"Tier{i}";
                    tier.transform.SetParent(go.transform, false);
                    tier.transform.localScale = new Vector3(size.x * scale[i], h, size.y * scale[i]);
                    tier.transform.localPosition = new Vector3(0f, y + h * 0.5f, 0f);
                    if (material != null) tier.GetComponent<MeshRenderer>().sharedMaterial = material;
                    y += h;
                }

                var meta = go.AddComponent<CityBuilding>();
                meta.height = lm.height;
                meta.floorCount = Mathf.RoundToInt(lm.height / CityLayoutSettings.FloorHeight);
                meta.footprint = size * scale[2];
                meta.isShell = false;

                if (anchorLayer >= 0) SetLayerRecursive(go, anchorLayer);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 고가철도 상판 + 기둥. 상판 12 m는 건물보다 낮은 앵커 줄이고,
        /// 24 m마다 서는 기둥은 저공 질주할 때 스쳐 지나가며 속도를 눈에 보이게 한다.
        /// </summary>
        private void CreateRail(Transform parent, int anchorLayer)
        {
            var root = new GameObject("_Rail");
            root.transform.SetParent(parent, false);

            var deck = settings.railDeck;
            var material = Pick(settings.railMaterial, GreyboxMaterial(new PlanRandom(2)));

            // 프리팹 칸이 채워져 있으면 교체하고, 비어 있으면 회색 박스 그대로. 상판·기둥을 따로 판단한다.
            float deckBottom = settings.railDeckTop - settings.railDeckThickness;
            float kitScale = 0f;
            if (!TryLayElevatedDeck(root.transform, anchorLayer, ref deckBottom, ref kitScale))
                CreateSlab(root.transform, "Deck", deck, settings.railDeckTop, settings.railDeckThickness, material, anchorLayer, true);

            Bounds pillarBounds = default;
            bool pillarPrefab = settings.elevatedPillarPrefab != null &&
                                TryMeasurePrefab(settings.elevatedPillarPrefab, root.transform, out pillarBounds);
            float half = settings.railPillarSize * 0.5f;
            float cx = deck.Center.x;
            int k = 0;
            for (float z = deck.z0; z <= deck.z1 + 0.01f; z += settings.railPillarSpacing, k++)
            {
                if (pillarPrefab)
                    PlaceElevatedPillar(root.transform, pillarBounds, new Vector2(cx, z), deckBottom, kitScale, anchorLayer, k);
                else
                    CreateSlab(root.transform, $"Pillar_{k:00}", new PlanRect(cx - half, cx + half, z - half, z + half),
                               deckBottom, deckBottom, material, anchorLayer, true);
            }
        }

        /// <summary>
        /// 공원 나무. 공원은 140 m라 사거리 22 m로는 건물 앵커만으로 건널 수 없으므로,
        /// 12 m 나무를 낮은 앵커로 깔아 저공 구간으로 만든다.
        /// 프리팹이 없으면 기둥 + 구 회색 볼륨으로 대신한다.
        /// </summary>
        private int CreateTrees(Transform parent, int anchorLayer)
        {
            var root = new GameObject("_Trees");
            root.transform.SetParent(parent, false);

            var rng = new PlanRandom(settings.treeSeed);
            // 추가 생성 — 종류·높이 고르기는 별도 난수로 한다. 위치 난수(rng)를 같이 쓰면
            // 나무 종류를 추가하는 것만으로 모든 나무 위치가 바뀌어 버린다.
            var kindRng = new PlanRandom(settings.treeSeed ^ 0x5bd1e995);
            PrepareTreeFit(root.transform);
            var p = settings.park;
            float inset = settings.treeInset;
            float spacing = Mathf.Max(1f, settings.treeSpacing);
            int count = 0;

            for (float x = p.x0 + inset; x <= p.x1 - inset + 0.01f; x += spacing)
            {
                for (float z = p.z0 + inset; z <= p.z1 - inset + 0.01f; z += spacing)
                {
                    if (rng.Next() < settings.treeSkipChance) continue;
                    float jx = (float)(rng.Next() * 5.0 - 2.5);
                    float jz = (float)(rng.Next() * 5.0 - 2.5);
                    CreateTree(root.transform, new Vector2(x + jx, z + jz), anchorLayer, count, kindRng);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 나무 한 그루. 프리팹이 있으면 그중 하나를 골라 높이를 맞춰 세우고, 없으면 회색 기둥 + 구.
        /// 수정 — kindRng로 종류와 높이 흔들기(treeHeightJitter)를 고른다.
        /// </summary>
        private void CreateTree(Transform parent, Vector2 at, int anchorLayer, int index, PlanRandom kindRng)
        {
            GameObject tree;
            float lift = 0f;
            if (treeSpecs.Count > 0)
            {
                var spec = treeSpecs[Mathf.Min(treeSpecs.Count - 1, (int)(kindRng.Next() * treeSpecs.Count))];
                // 1 ± jitter. 배율과 띄움을 같은 비율로 키워야 바닥이 땅에 그대로 붙는다.
                float jitter = 1f + (float)(kindRng.Next() * 2.0 - 1.0) * settings.treeHeightJitter;

                tree = InstantiateLinked(spec.prefab);
                tree.transform.SetParent(parent, false);
                tree.transform.localRotation = Quaternion.Euler(0f, (index * 137.5f) % 360f, 0f);
                tree.transform.localScale *= spec.scale * jitter;
                lift = spec.lift * jitter;
                AddTreeColliders(tree); // 수정 (2026-10-20) — 잎에는 충돌체를 안 붙인다(아래 함수 설명)
                // 추가 생성 — 나무 프리팹에 딸린 파티클(나뭇잎 떨어지기 등)을 끈다. 설정에서 켜면 그대로 둔다.
                if (!settings.treeLeafParticles) DisableParticles(tree);
            }
            else
            {
                tree = new GameObject("Tree");
                tree.transform.SetParent(parent, false);
                float canopyRadius = 3.5f;
                float trunkHeight = Mathf.Max(1f, settings.treeHeight - canopyRadius * 2f + 1f);
                var material = Pick(settings.treeMaterial, settings.parkMaterial);

                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "Trunk";
                trunk.transform.SetParent(tree.transform, false);
                trunk.transform.localScale = new Vector3(0.8f, trunkHeight * 0.5f, 0.8f); // 원기둥 높이 2 단위
                trunk.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);

                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                canopy.name = "Canopy";
                canopy.transform.SetParent(tree.transform, false);
                canopy.transform.localScale = Vector3.one * canopyRadius * 2f;
                canopy.transform.localPosition = new Vector3(0f, settings.treeHeight - canopyRadius, 0f);

                if (material != null)
                {
                    trunk.GetComponent<MeshRenderer>().sharedMaterial = material;
                    canopy.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }

            tree.name = $"Tree_{index:000}";
            tree.transform.localPosition = new Vector3(at.x, lift, at.y); // 수정 — 고른 종류의 띄움 값
            if (anchorLayer >= 0) SetLayerRecursive(tree, anchorLayer);
        }

        /// <summary>
        /// 동·북쪽 원경 실루엣. 기존 통짜 경계벽 대신 높이가 들쭉날쭉한 박스 줄을 세워
        /// 도시가 계속 이어지는 것처럼 보이게 한다. 웹이 붙도록 Anchorable로 둔다.
        /// </summary>
        private int CreateBackdrop(Transform parent, int anchorLayer)
        {
            var root = new GameObject("_Backdrop");
            root.transform.SetParent(parent, false);

            var rng = new PlanRandom(settings.backdropSeed);
            var material = Pick(settings.backdropMaterial, GreyboxMaterial(new PlanRandom(3)));
            float seg = Mathf.Max(1f, settings.backdropSegment);
            Vector2 range = settings.backdropHeight;
            int count = 0;

            var east = settings.backdropEast;
            for (float z = east.z0; z < east.z1 - 0.01f; z += seg)
            {
                float h = Mathf.Lerp(range.x, range.y, (float)rng.Next());
                float z1 = Mathf.Min(z + seg, east.z1) - LotGap;
                var area = new PlanRect(east.x0, east.x1, z, z1);
                // 수정 — 건물 목록으로 먼저 세워 보고, 안 맞는 칸만 회색 판으로 둔다.
                if (!TryBackdropBuilding(root.transform, $"East_{count:00}", area, h, count, anchorLayer))
                    CreateSlab(root.transform, $"East_{count:00}", area, h, h, material, anchorLayer, true);
                count++;
            }

            var north = settings.backdropNorth;
            for (float x = north.x0; x < north.x1 - 0.01f; x += seg)
            {
                float h = Mathf.Lerp(range.x, range.y, (float)rng.Next());
                float x1 = Mathf.Min(x + seg, north.x1) - LotGap;
                var area = new PlanRect(x, x1, north.z0, north.z1);
                // 수정 — 위와 같다.
                if (!TryBackdropBuilding(root.transform, $"North_{count:00}", area, h, count, anchorLayer))
                    CreateSlab(root.transform, $"North_{count:00}", area, h, h, material, anchorLayer, true);
                count++;
            }
            return count;
        }

        // 추가 생성 — 원경 한 칸을 건물 목록의 건물로 세운다 (2026-10-12)
        /// <summary>
        /// 원경 한 칸(area, 높이 height)에 건물 목록에서 맞는 건물을 세운다. 세웠으면 true.
        /// whiteCityOnBackdrop이 꺼져 있거나 맞는 건물이 없으면 false — 호출하는 쪽이 회색 판을 세운다.
        ///
        /// 높이 난수(rng)는 건드리지 않는다. 여기서 같은 난수를 쓰면 회색 판의 높이까지 바뀌어
        /// 건물 목록을 비워도 예전 원경으로 돌아가지 않는다. 그래서 칸 번호로 따로 시드를 만든다.
        /// </summary>
        private bool TryBackdropBuilding(Transform parent, string name, PlanRect area, float height, int index, int anchorLayer)
        {
            if (!settings.whiteCityOnBackdrop) return false;

            var pick = new PlanRandom(settings.backdropSeed ^ (index * 104729 + 31));
            var go = TryCreateWhiteCity(name, area.Size.x, area.Size.y, height, pick);
            if (go == null) return false;

            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(area.Center.x, 0f, area.Center.y);
            if (anchorLayer >= 0) SetLayerRecursive(go, anchorLayer);
            return true;
        }

        /// <summary>
        /// 땅과 강을 모두 감싸는 경계벽. 도시 어디서 봐도 맵 바깥(빈 공간)이 보이지 않게 막는다.
        /// 벽 하나가 시야 차단과 충돌을 같이 맡는다. 강 쪽은 강바닥 아래까지 내려 수면 밑 틈도 막는다.
        /// 그림자를 끈다 — 300 m 벽이 해 각도에 따라 도시 절반을 덮기 때문이다.
        /// 기본은 Default 레이어라 웹이 붙지 않는다 (맵 끝 벽을 타고 도는 플레이 방지, 설정에서 바꿀 수 있다).
        /// </summary>
        private void CreateBoundary(Transform parent, int anchorLayer)
        {
            var root = new GameObject("_Boundary");
            root.transform.SetParent(parent, false);

            float minX = settings.land.x0, maxX = settings.land.x1;
            float minZ = settings.land.z0, maxZ = settings.land.z1;
            foreach (var w in settings.water)
            {
                if (w == null) continue;
                minX = Mathf.Min(minX, w.x0);
                maxX = Mathf.Max(maxX, w.x1);
                minZ = Mathf.Min(minZ, w.z0);
                maxZ = Mathf.Max(maxZ, w.z1);
            }

            float top = settings.boundaryHeight;
            float bottom = Mathf.Min(settings.riverbedY, 0f) - 2f;
            float h = top - bottom;
            float cy = bottom + h * 0.5f;
            float t = Mathf.Max(0.5f, settings.outerWallThickness);
            var material = Pick(settings.outerWallMaterial, settings.backdropMaterial);
            int layer = settings.outerWallAnchorable && anchorLayer >= 0 ? anchorLayer : -1;

            // 동·서 벽은 Z 범위만, 남·북 벽은 두께만큼 양 끝을 늘려 모서리를 덮는다 — 모서리에서 두 벽 면이 겹치지 않게.
            Wall("West", new Vector3(minX - t * 0.5f, cy, (minZ + maxZ) * 0.5f), new Vector3(t, h, maxZ - minZ));
            Wall("East", new Vector3(maxX + t * 0.5f, cy, (minZ + maxZ) * 0.5f), new Vector3(t, h, maxZ - minZ));
            Wall("South", new Vector3((minX + maxX) * 0.5f, cy, minZ - t * 0.5f), new Vector3(maxX - minX + t * 2f, h, t));
            Wall("North", new Vector3((minX + maxX) * 0.5f, cy, maxZ + t * 0.5f), new Vector3(maxX - minX + t * 2f, h, t));

            void Wall(string side, Vector3 center, Vector3 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Boundary_{side}";
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = center;
                go.transform.localScale = size;

                var renderer = go.GetComponent<MeshRenderer>();
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.enabled = settings.outerWallVisible;
                if (layer >= 0) go.layer = layer;
            }
        }

        // ─── 옥상 난간 ───

        /// <summary>
        /// 건물마다 <b>길 쪽 옥상 모서리 한 변</b>에만 난간을 두른다.
        /// 기존 생성기는 네 변을 다 둘렀는데, 건물이 87채에서 400채 넘게 늘어나면 난간 조각이
        /// 만 개를 넘는다. 스윙 중에 보이는 실루엣은 길 쪽 변이라 그 한 변이 효과의 대부분이다.
        /// </summary>
        private int CreateFrontageRails(Transform parent, List<BuiltLot> built, int anchorLayer)
        {
            if (settings.roofRailPrefab == null || built.Count == 0) return 0;

            var root = new GameObject("_RoofRails");
            root.transform.SetParent(parent, false);
            if (!TryMeasurePrefab(settings.roofRailPrefab, root.transform, out Bounds rail)) return 0;

            float pieceLength = Mathf.Max(rail.size.x, rail.size.z);
            if (pieceLength < 0.1f) return 0;
            bool longAlongX = rail.size.x >= rail.size.z;
            int anchorMask = anchorLayer >= 0 ? (1 << anchorLayer) : ~0;
            int total = 0, missed = 0;

            foreach (var b in built)
            {
                // 90°·270°로 돈 타워는 옥상의 두 축이 월드에서 뒤바뀐다.
                bool swap = Mathf.Abs(Mathf.Repeat(b.yaw, 180f) - 90f) < 1f;
                float extX = swap ? b.meta.footprint.y : b.meta.footprint.x;
                float extZ = swap ? b.meta.footprint.x : b.meta.footprint.y;
                float hx = extX * 0.5f - settings.roofRailInset;
                float hz = extZ * 0.5f - settings.roofRailInset;
                if (hx <= 0f || hz <= 0f) continue;

                Vector3 p = b.meta.transform.localPosition;
                Vector2 from, to;
                switch (b.lot.side)
                {
                    case PlanLotSide.West: from = new Vector2(p.x - hx, p.z - hz); to = new Vector2(p.x - hx, p.z + hz); break;
                    case PlanLotSide.East: from = new Vector2(p.x + hx, p.z - hz); to = new Vector2(p.x + hx, p.z + hz); break;
                    case PlanLotSide.South: from = new Vector2(p.x - hx, p.z - hz); to = new Vector2(p.x + hx, p.z - hz); break;
                    default: from = new Vector2(p.x - hx, p.z + hz); to = new Vector2(p.x + hx, p.z + hz); break;
                }

                total += PlaceRailRun(root.transform, b, rail, pieceLength, longAlongX, anchorLayer, anchorMask, from, to, ref missed);
            }

            if (missed > 0)
                Debug.LogWarning($"[CityPlanGenerator] 옥상 난간 {missed}칸은 표면을 못 찾아 건너뛰었다.", this);
            return total;
        }

        // 추가 생성 — 난간 자리에서 옥상을 못 찾았을 때 건물 중심 쪽으로 당겨 다시 쏘는 간격(m)과 횟수 (2026-10-15)
        private const float RailInsetStep = 1f;
        private const int RailInsetRetries = 4;

        /// <summary>옥상 한 변을 난간 조각으로 채운다. 좌표는 생성기 로컬 XZ.</summary>
        private int PlaceRailRun(Transform parent, BuiltLot b, Bounds rail, float pieceLength, bool longAlongX,
                                 int layer, int anchorMask, Vector2 from, Vector2 to, ref int missed)
        {
            Vector2 span = to - from;
            float run = span.magnitude;
            if (run < 0.01f) return 0;

            int count = Mathf.Max(1, Mathf.RoundToInt(run / pieceLength));
            Vector2 step = span / count;
            float edgeYaw = Mathf.Atan2(span.x, span.y) * Mathf.Rad2Deg;
            float align = longAlongX ? 90f : 0f;
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 at = from + step * (i + 0.5f);
                Vector3 spotLocal;

                if (b.greybox)
                {
                    // 회색 박스 옥상은 평평한 박스 윗면이라 높이를 그대로 안다.
                    spotLocal = new Vector3(at.x, b.meta.height, at.y);
                }
                else
                {
                    // 프리팹 옥상은 파라펫·실외기가 섞여 있어 실제 밟히는 면을 레이로 찾는다.
                    // 수정 (2026-10-15) — 못 찾으면 건물 중심 쪽으로 RailInsetStep씩 최대 RailInsetRetries번 당겨 다시 쏜다.
                    // 진단 결과 WhiteCity 모델 10은 옆으로 튀어나온 부분 때문에 바운즈 상자가 실제 옥상보다 커서,
                    // 상자 모서리를 따라 깐 난간 아래가 허공이었다. 안쪽으로 당기면 실제 옥상 가장자리에 붙는다.
                    Vector2 center = new Vector2(b.meta.transform.localPosition.x, b.meta.transform.localPosition.z);
                    Vector2 inward = center - at;
                    inward = inward.sqrMagnitude > 0.0001f ? inward.normalized : Vector2.zero;

                    Vector3 spotWorld = default;
                    bool foundRoof = false;
                    for (int attempt = 0; attempt <= RailInsetRetries && !foundRoof; attempt++)
                    {
                        Vector2 probe = at + inward * (RailInsetStep * attempt);
                        Vector3 origin = parent.TransformPoint(new Vector3(probe.x, b.meta.height + RoofProbe.Margin, probe.y));
                        // 수정 — ±Margin 창 대신 건물 높이 전체를 위에서부터 훑는다 (안테나 있는 건물·계단식 모서리 대응, 2026-10-14)
                        foundRoof = RoofProbe.TryFindRoofFromTop(origin, b.meta.height + RoofProbe.Margin * 2f,
                                                                 b.meta.transform, anchorMask, out spotWorld);
                    }
                    if (!foundRoof)
                    {
                        missed++;
                        RecordMiss(_railMiss, b.meta); // 추가 생성 — 진단: 어느 건물에서 못 찾았는지
                        continue;
                    }
                    spotLocal = parent.InverseTransformPoint(spotWorld);
                }

                var piece = InstantiateLinked(settings.roofRailPrefab);
                piece.transform.SetParent(parent, false);
                piece.transform.localRotation = Quaternion.Euler(0f, edgeYaw + align, 0f);
                piece.transform.localPosition = spotLocal - Vector3.up * rail.min.y;
                if (layer >= 0) SetLayerRecursive(piece, layer);
                AddMeshColliders(piece, true);
                placed++;
            }
            return placed;
        }

        // ─── 다각형 (사선 자르기) ───

        /// <summary>이보다 작은 조각은 버린다. 부동소수 오차로 생기는 실오라기 면을 거르는 기준(m²).</summary>
        private const float MinPieceArea = 0.01f;

        /// <summary>위에서 볼 때 반시계 방향 사각형. 잘라도 방향이 유지되어 메시 앞면 판정이 일정하다.</summary>
        private static List<Vector2> RectPolygon(float x0, float x1, float z0, float z1) =>
            new List<Vector2> { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };

        /// <summary>반평면 dot(p, n) ≥ k 쪽만 남긴다 (Sutherland–Hodgman 한 단계). 볼록 다각형은 볼록으로 남는다.</summary>
        private static List<Vector2> ClipHalfPlane(List<Vector2> poly, Vector2 n, float k)
        {
            var result = new List<Vector2>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = Vector2.Dot(a, n) - k, db = Vector2.Dot(b, n) - k;
                if (da >= 0f) result.Add(a);
                if ((da >= 0f) != (db >= 0f)) result.Add(a + (b - a) * (da / (da - db)));
            }
            return result;
        }

        private static List<Vector2> ClipToStrip(List<Vector2> poly, CutStrip s)
        {
            float dr = Vector2.Dot(s.origin, s.right), du = Vector2.Dot(s.origin, s.dir);
            poly = ClipHalfPlane(poly, s.right, s.latMin + dr);
            poly = ClipHalfPlane(poly, -s.right, -(s.latMax + dr));
            poly = ClipHalfPlane(poly, s.dir, s.s0 + du);
            return ClipHalfPlane(poly, -s.dir, -(s.s1 + du));
        }

        /// <summary>
        /// 볼록 다각형 목록에서 띠를 뺀다. 볼록 하나에서 띠를 빼면 볼록 조각 최대 4개(띠 오른쪽·왼쪽·앞·뒤)가 남는다.
        /// 오목 다각형을 만들지 않고 볼록 조각으로만 유지하면 삼각형 분할이 부채꼴 한 번으로 끝난다.
        /// </summary>
        private static void Subtract(List<List<Vector2>> polys, CutStrip s)
        {
            float dr = Vector2.Dot(s.origin, s.right), du = Vector2.Dot(s.origin, s.dir);
            for (int i = polys.Count - 1; i >= 0; i--)
            {
                var poly = polys[i];
                var overlap = ClipToStrip(poly, s);
                if (overlap.Count < 3 || PolygonArea(overlap) < MinPieceArea) continue;

                polys.RemoveAt(i);
                AddPiece(polys, ClipHalfPlane(poly, s.right, s.latMax + dr));
                AddPiece(polys, ClipHalfPlane(poly, -s.right, -(s.latMin + dr)));
                var mid = ClipHalfPlane(ClipHalfPlane(poly, s.right, s.latMin + dr), -s.right, -(s.latMax + dr));
                AddPiece(polys, ClipHalfPlane(mid, -s.dir, -(s.s0 + du)));
                AddPiece(polys, ClipHalfPlane(mid, s.dir, s.s1 + du));
            }
        }

        private static void AddPiece(List<List<Vector2>> polys, List<Vector2> piece)
        {
            if (piece.Count >= 3 && PolygonArea(piece) >= MinPieceArea) polys.Add(piece);
        }

        private static float PolygonArea(List<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>볼록 조각들을 윗면 + 옆면 메시 하나로 세운다. 메시 충돌체를 같이 단다.</summary>
        private static GameObject CreateExtrusion(Transform parent, string name, List<List<Vector2>> polygons, float top, float bottom,
                                                  Material material, float uvSize, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = BuildExtrusionMesh(polygons, top, bottom, uvSize);
            mesh.name = name;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material != null ? material : DefaultMaterial();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (layer >= 0) go.layer = layer;
            return go;
        }

        /// <summary>
        /// 윗면 UV는 월드 XZ 기준이라 조각 경계에서 무늬가 끊기지 않는다.
        /// 유니티 앞면은 시계 방향이라 반시계 다각형을 (0, i+1, i) 순서로 묶는다.
        /// 옆면은 단차(블록 바닥 0 → 차도 −0.15)에서 연석 면이 된다.
        /// </summary>
        private static Mesh BuildExtrusionMesh(List<List<Vector2>> polygons, float top, float bottom, float uvSize)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float inv = 1f / Mathf.Max(0.01f, uvSize);

            foreach (var poly in polygons)
            {
                int n = poly.Count;
                if (n < 3) continue;

                int b0 = verts.Count;
                foreach (var p in poly)
                {
                    verts.Add(new Vector3(p.x, top, p.y));
                    uvs.Add(p * inv);
                }
                for (int i = 1; i < n - 1; i++)
                {
                    tris.Add(b0);
                    tris.Add(b0 + i + 1);
                    tris.Add(b0 + i);
                }

                if (bottom >= top) continue;
                float run = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = poly[i], c = poly[(i + 1) % n];
                    float len = Vector2.Distance(a, c);
                    int s = verts.Count;
                    verts.Add(new Vector3(a.x, top, a.y));
                    verts.Add(new Vector3(c.x, top, c.y));
                    verts.Add(new Vector3(c.x, bottom, c.y));
                    verts.Add(new Vector3(a.x, bottom, a.y));
                    uvs.Add(new Vector2(run, top) * inv);
                    uvs.Add(new Vector2(run + len, top) * inv);
                    uvs.Add(new Vector2(run + len, bottom) * inv);
                    uvs.Add(new Vector2(run, bottom) * inv);
                    tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                    tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
                    run += len;
                }
            }

            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material defaultMaterial;

        /// <summary>머티리얼 칸이 비었을 때 프리미티브와 같은 기본 머티리얼을 쓴다 (분홍색 방지).</summary>
        private static Material DefaultMaterial()
        {
            if (defaultMaterial != null) return defaultMaterial;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            defaultMaterial = temp.GetComponent<MeshRenderer>().sharedMaterial;
            DestroyObject(temp);
            return defaultMaterial;
        }

        // ─── 공용 도우미 ───

        /// <summary>
        /// UnityEngine.Object는 ?? 연산자가 "파괴됐거나 비어 있는" 참조를 null로 보지 않는다.
        /// 인스펙터에서 비운 머티리얼 칸을 제대로 거르려고 != 비교로 고른다.
        /// </summary>
        private static Material Pick(Material preferred, Material fallback) => preferred != null ? preferred : fallback;

        /// <summary>프리팹을 잠깐 놓아 규격을 재고 지운다 (기존 CityGenerator와 같은 방식).</summary>
        private bool TryMeasurePrefab(GameObject prefab, Transform parent, out Bounds bounds)
        {
            bounds = default;
            if (prefab == null) return false;

            var probe = InstantiateLinked(prefab);
            probe.transform.SetParent(parent, false);
            probe.transform.localPosition = Vector3.zero;
            probe.transform.localRotation = Quaternion.identity;

            var renderers = probe.GetComponentsInChildren<Renderer>();

            // 수정 — 파티클 렌더러는 규격 측정에서 뺀다 (2026-10-14).
            // Realistic Tree 나무처럼 루트에 나뭇잎 파티클이 붙은 프리팹은 파티클 범위가 섞이면
            // 키가 실제와 다르게 재져서 treeHeight에 맞출 때 크기가 어긋난다.
            bool found = false;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!found)
            {
                Debug.LogWarning($"[CityPlanGenerator] '{prefab.name}'에 렌더러가 없어 규격을 잴 수 없다.", this);
                DestroyObject(probe);
                return false;
            }

            DestroyObject(probe);
            return true;
        }

        /// <summary>
        /// 생긴 그대로 메시 충돌체를 붙인다. 이 키트 FBX는 충돌체 없이 들어온다.
        /// 프리팹이 이미 충돌체를 갖고 있으면 저자가 정한 것을 우선한다.
        /// </summary>
        /// <param name="convex">작은 소품은 볼록, 밟는 바닥은 오목(실제 단차 유지).</param>
        private static void AddMeshColliders(GameObject go, bool convex)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                mc.convex = convex;
            }
        }

        // 추가 생성 (2026-10-20)
        /// <summary>
        /// 나무 충돌체 — 줄기·가지에만 볼록(convex) MeshCollider를 붙이고 <b>잎(Leaves) 메시는 건너뛴다</b>.
        /// 잎 메시는 면이 수천 개라 볼록 충돌체 한도(256면)를 넘어, 씬을 열 때마다 "Couldn't create a Convex Mesh …
        /// Leaves_LOD0" 경고가 나무 수만큼 떴다. 잎은 지나가도 되는 부분이라 충돌이 필요 없다.
        /// </summary>
        private static void AddTreeColliders(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                string meshName = filter.sharedMesh.name;
                if (meshName.IndexOf("Leaves", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    meshName.IndexOf("Leaf", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                mc.convex = true;
            }
        }

        // 추가 생성
        /// <summary>
        /// 프리팹에 딸린 파티클을 모두 끈다 (2026-10-14).
        /// 공원 나무가 수십 그루라 파티클이 전부 돌면 비용이 크고, 스윙 속도에서는 거의 보이지 않는다.
        /// 프리팹 인스턴스라 컴포넌트를 지우지 않고 꺼 두기만 한다 — 프리팹 연결이 그대로 남는다.
        /// </summary>
        private static void DisableParticles(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.playOnAwake = false;                                       // Play 모드에서 저절로 재생되지 않게
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 이미 돌고 있는 것도 멈추고 지운다
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null) r.enabled = false;                               // 혹시 재생돼도 그리지 않게
            }
        }

        private GameObject InstantiateLinked(GameObject prefab)
        {
#if UNITY_EDITOR
            // 에디터에서는 프리팹 링크를 유지한다. 원본 프리팹을 고치면 도시 전체가 따라온다.
            if (!Application.isPlaying)
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
#endif
            return Instantiate(prefab);
        }

        private static void DestroyObject(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private static void StripComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Destroy(component);
            else DestroyImmediate(component);
        }

        private static void DestroyCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        /// <summary>Rigidbody가 있는 오브젝트는 건너뛴다 (정적 배칭과 물리 위치가 어긋나는 문제 — 기존과 같은 규칙).</summary>
        private static void MarkStaticRecursive(GameObject go)
        {
            // 추가 생성 (2026-10-17) — 공원 나무는 Static에서 뺀다. Realistic Tree 잎은 나무 자신의 좌표로 흔드는
            // 바람 셰이더인데, 정적 배칭이 메시를 월드 좌표로 합쳐 버리면 원점에서 먼 나무일수록 크게 휘어 잎이
            // 옆 지역까지 튀어 보였다. 나무 묶음과 그 아래 전부 건너뛴다.
            if (go.name == "_Trees") return;
            if (go.GetComponent<Rigidbody>() == null) go.isStatic = true;
            foreach (Transform child in go.transform) MarkStaticRecursive(child.gameObject);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        // ─── 미리보기 기즈모 ───

#if UNITY_EDITOR
        /// <summary>
        /// 생성 전에 필지를 높이 그대로 와이어 박스로 그린다. 수천 개 오브젝트를 만들지 않고
        /// 배치를 확인할 수 있어서, 설정값을 바꿔가며 보는 반복이 빨라진다.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || settings == null) return;
            var layout = BuildLayout();
            if (layout == null) return;

            var previous = Gizmos.matrix;
            Matrix4x4 local = transform.localToWorldMatrix;
            Gizmos.matrix = local;

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            foreach (var b in layout.blocks)
                Gizmos.DrawWireCube(new Vector3(b.Center.x, 0.2f, b.Center.y), new Vector3(b.x1 - b.x0, 0.4f, b.z1 - b.z0));

            foreach (var lot in layout.lots)
            {
                Gizmos.color = KindColor(lot.kind);
                Vector2 c = lot.Center;
                Gizmos.DrawWireCube(new Vector3(c.x, lot.height * 0.5f, c.y), new Vector3(lot.x1 - lot.x0, lot.height, lot.z1 - lot.z0));
            }

            Gizmos.color = KindColor(PlanLotKind.Greybox);
            foreach (var w in layout.diagonalWalls)
            {
                Gizmos.matrix = local * Matrix4x4.TRS(new Vector3(w.center.x, w.height * 0.5f, w.center.y),
                                                      Quaternion.Euler(0f, w.yawDeg, 0f), Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(w.depth, w.height, w.length));
            }
            Gizmos.matrix = local;

            Gizmos.color = new Color(0.88f, 0.64f, 0.36f);
            foreach (var lm in settings.landmarks)
            {
                if (lm == null) continue;
                Gizmos.DrawWireCube(new Vector3(lm.area.Center.x, lm.height * 0.5f, lm.area.Center.y),
                                    new Vector3(lm.area.Size.x, lm.height, lm.area.Size.y));
            }

            Gizmos.color = new Color(0.95f, 0.76f, 0.55f);
            Gizmos.DrawLine(new Vector3(settings.diagonalFrom.x, 0.5f, settings.diagonalFrom.y),
                            new Vector3(settings.diagonalTo.x, 0.5f, settings.diagonalTo.y));

            UnityEditor.Handles.matrix = local;
            UnityEditor.Handles.color = new Color(0.95f, 0.76f, 0.55f);
            foreach (var plaza in settings.plazas)
            {
                if (plaza == null) continue;
                UnityEditor.Handles.DrawWireDisc(new Vector3(plaza.center.x, 0.3f, plaza.center.y), Vector3.up, plaza.radius);
            }
            UnityEditor.Handles.matrix = Matrix4x4.identity;

            Gizmos.matrix = previous;
        }

        private static Color KindColor(PlanLotKind kind)
        {
            switch (kind)
            {
                case PlanLotKind.Large: return new Color(0.69f, 0.55f, 0.37f);
                case PlanLotKind.Medium: return new Color(0.49f, 0.54f, 0.43f);
                case PlanLotKind.Small: return new Color(0.46f, 0.47f, 0.56f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }
#endif
    }
}
