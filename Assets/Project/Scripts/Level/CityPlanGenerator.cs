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
    public class CityPlanGenerator : MonoBehaviour
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
            int roadTiles = CreateRoads(root.transform, groundLayer, out float recess);
            CreateGroundAndWater(root.transform, recess, groundLayer);
            CreateSurfaces(root.transform, layout, groundLayer);

            var built = new List<BuiltLot>();
            CreateLotBuildings(root.transform, layout, anchorLayer, built);
            int walls = CreateDiagonalWalls(root.transform, layout, anchorLayer);
            int landmarks = CreateLandmarks(root.transform, anchorLayer);
            CreateRail(root.transform, anchorLayer);
            int trees = CreateTrees(root.transform, anchorLayer);
            int backdrop = CreateBackdrop(root.transform, anchorLayer);
            CreateBoundary(root.transform);
            int rails = CreateFrontageRails(root.transform, built, anchorLayer);

            if (settings.markStatic) MarkStaticRecursive(root);

            int prefabTowers = 0;
            foreach (var b in built) if (!b.greybox) prefabTowers++;

            Debug.Log($"[CityPlanGenerator] 건물 {built.Count + walls + landmarks}채 " +
                      $"(프리팹 타워 {prefabTowers} · 회색 박스 필지 {built.Count - prefabTowers} · " +
                      $"사선 가로벽 {walls} · 랜드마크 {landmarks}) / 블록 {layout.blocks.Count} / " +
                      $"도로 타일 {roadTiles} / 나무 {trees} / 원경 {backdrop} / 옥상 난간 {rails} / " +
                      $"최고 필지 {layout.TallestLot():F0} m", this);
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
        /// </summary>
        /// <param name="recess">가장 깊이 파인 차도 표면 깊이. 지면 박스를 그보다 아래에 둔다.</param>
        private int CreateRoads(Transform parent, int layer, out float recess)
        {
            recess = 0f;
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
            float expectedAvenueRoad = hasStrip ? avenueWidth - strip.cross * 2f : avenueWidth;
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
                float total = avenue.cross + (hasStrip ? strip.cross * 2f : 0f);
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

            bool InAvenue(Vector2 p)
            {
                foreach (var a in avenues)
                {
                    if (Mathf.Abs(p.x - a.centerX) < a.width * 0.5f) return true;
                }
                return false;
            }

            bool SkipDiagonal(Vector2 p) => InPlaza(p) || InAvenue(p);

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
                        placed += LayRun(avenue, from, to, 0f, roads.transform, layer, InPlaza);

                        if (hasStrip)
                        {
                            float offset = avenue.cross * 0.5f + strip.cross * 0.5f;
                            placed += LayRun(strip, from, to, offset, roads.transform, layer, InPlaza);
                            placed += LayRun(strip, from, to, -offset, roads.transform, layer, InPlaza);
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
                                         0f, roads.transform, layer, InPlaza);
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

            // 사선 대로 — 같은 4차선 타일을 사선 각도로 돌려 깐다. 애비뉴 위를 지나는 구간은 건너뛴다.
            if (hasAvenue)
            {
                placed += LayRun(avenue, settings.diagonalFrom, settings.diagonalTo, 0f, roads.transform, layer, SkipDiagonal);
                if (hasStrip)
                {
                    float offset = avenue.cross * 0.5f + strip.cross * 0.5f;
                    placed += LayRun(strip, settings.diagonalFrom, settings.diagonalTo, offset, roads.transform, layer, SkipDiagonal);
                    placed += LayRun(strip, settings.diagonalFrom, settings.diagonalTo, -offset, roads.transform, layer, SkipDiagonal);
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
        private int LayRun(TileSpec spec, Vector2 from, Vector2 to, float lateral, Transform parent, int layer,
                           Func<Vector2, bool> skip)
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

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 c = from + dir * (pad + spec.length * (i + 0.5f)) + right * lateral;
                if (skip != null && skip(c)) continue;
                PlaceTile(spec.prefab, spec.bounds, c, tileYaw, parent, layer);
                placed++;
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
            CreateSlab(root.transform, "Ground", settings.land, groundTop, 5f, settings.groundMaterial, groundLayer, true);

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
        private void CreateSurfaces(Transform parent, CityPlanLayout layout, int groundLayer)
        {
            var root = new GameObject("_Surfaces");
            root.transform.SetParent(parent, false);

            var paving = Pick(settings.pavingMaterial, settings.groundMaterial);
            float quayDepth = -settings.riverbedY + 0.2f;

            CreateSlab(root.transform, "Promenade_West", settings.westPromenade, 0f, quayDepth, paving, groundLayer, true);
            CreateSlab(root.transform, "Promenade_South", settings.southPromenade, 0f, quayDepth, paving, groundLayer, true);

            foreach (var b in layout.blocks)
            {
                CreateSlab(root.transform, $"Block_{b.id}", new PlanRect(b.x0, b.x1, b.z0, b.z1), 0f, 1f, paving, groundLayer, true);
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
                    go = CreateGreyboxVolume(lot.id, lot.x1 - lot.x0 - LotGap, lot.z1 - lot.z0 - LotGap,
                                             lot.height, GreyboxMaterial(orient));
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
                var go = CreateGreyboxVolume($"{w.id}_{w.height:F0}m", w.depth, w.length, w.height,
                                             GreyboxMaterial(new PlanRandom(settings.orientationSeed ^ (i * 104729))));
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
                for (int i = 0; i < 3; i++)
                {
                    float h = lm.height * share[i];
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
            CreateSlab(root.transform, "Deck", deck, settings.railDeckTop, settings.railDeckThickness, material, anchorLayer, true);

            float pillarTop = settings.railDeckTop - settings.railDeckThickness;
            float half = settings.railPillarSize * 0.5f;
            float cx = deck.Center.x;
            int k = 0;
            for (float z = deck.z0; z <= deck.z1 + 0.01f; z += settings.railPillarSpacing, k++)
            {
                CreateSlab(root.transform, $"Pillar_{k:00}", new PlanRect(cx - half, cx + half, z - half, z + half),
                           pillarTop, pillarTop, material, anchorLayer, true);
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
                    CreateTree(root.transform, new Vector2(x + jx, z + jz), anchorLayer, count);
                    count++;
                }
            }
            return count;
        }

        private void CreateTree(Transform parent, Vector2 at, int anchorLayer, int index)
        {
            GameObject tree;
            if (settings.treePrefab != null)
            {
                tree = InstantiateLinked(settings.treePrefab);
                tree.transform.SetParent(parent, false);
                tree.transform.localRotation = Quaternion.Euler(0f, (index * 137.5f) % 360f, 0f);
                AddMeshColliders(tree, true);
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
            tree.transform.localPosition = new Vector3(at.x, 0f, at.y);
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
                CreateSlab(root.transform, $"East_{count:00}", new PlanRect(east.x0, east.x1, z, z1), h, h, material, anchorLayer, true);
                count++;
            }

            var north = settings.backdropNorth;
            for (float x = north.x0; x < north.x1 - 0.01f; x += seg)
            {
                float h = Mathf.Lerp(range.x, range.y, (float)rng.Next());
                float x1 = Mathf.Min(x + seg, north.x1) - LotGap;
                CreateSlab(root.transform, $"North_{count:00}", new PlanRect(x, x1, north.z0, north.z1), h, h, material, anchorLayer, true);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 맵 바깥 투명 경계. 렌더러 없이 충돌체만 둔다.
        /// Default 레이어라 웹은 붙지 않는다 — 보이지 않는 벽에 매달리는 일이 없게.
        /// </summary>
        private void CreateBoundary(Transform parent)
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

            float h = settings.boundaryHeight;
            const float t = 5f;
            Wall("West", new Vector3(minX - t * 0.5f, h * 0.5f, (minZ + maxZ) * 0.5f), new Vector3(t, h, maxZ - minZ));
            Wall("East", new Vector3(maxX + t * 0.5f, h * 0.5f, (minZ + maxZ) * 0.5f), new Vector3(t, h, maxZ - minZ));
            Wall("South", new Vector3((minX + maxX) * 0.5f, h * 0.5f, minZ - t * 0.5f), new Vector3(maxX - minX + t * 2f, h, t));
            Wall("North", new Vector3((minX + maxX) * 0.5f, h * 0.5f, maxZ + t * 0.5f), new Vector3(maxX - minX + t * 2f, h, t));

            void Wall(string side, Vector3 center, Vector3 size)
            {
                var go = new GameObject($"Boundary_{side}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = center;
                var box = go.AddComponent<BoxCollider>();
                box.size = size;
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
                    Vector3 origin = parent.TransformPoint(new Vector3(at.x, b.meta.height + RoofProbe.Margin, at.y));
                    if (!RoofProbe.TryFindWalkableSurface(origin, RoofProbe.Margin * 2f, b.meta.transform, anchorMask, out Vector3 spotWorld))
                    {
                        missed++;
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
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[CityPlanGenerator] '{prefab.name}'에 렌더러가 없어 규격을 잴 수 없다.", this);
                DestroyObject(probe);
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
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
