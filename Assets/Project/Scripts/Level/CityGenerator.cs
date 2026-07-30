using System.Collections.Generic;
using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 회색 박스 도시를 만드는 공장.
    ///
    /// 오늘 만드는 것은 맵이 아니라 맵 공장이다.
    /// 맵을 완성하려면 건물 높이 H와 간격 D를 알아야 하는데 그 수치는 스윙을 굴려봐야 나오고,
    /// 스윙을 굴리려면 맵이 있어야 한다 — 순환이다.
    /// 맵이 1초에 재생성되는 물건이 되면 그 순환이 끊긴다. 그게 이 클래스의 존재 이유다.
    ///
    /// 생성은 에디터에서 버튼으로 한다. 매 프레임 자동 재생성하지 않는 이유는 두 가지다.
    /// 값 하나 만질 때마다 수백 오브젝트를 파괴/생성하면 에디터가 버벅이고,
    /// 무엇보다 물리 측정은 같은 맵에서 반복해야 의미가 있다.
    /// </summary>
    public class CityGenerator : MonoBehaviour
    {
        /// <summary>앵커 레이캐스트가 찾을 레이어. 도로와 지면을 걸러내기 위해 존재한다.</summary>
        public const string AnchorLayerName = "Anchorable";

        /// <summary>
        /// 지면 레이어. 앵커 레이어와 <b>반드시</b> 분리되어야 한다.
        /// 웹이 아스팔트에 붙으면 스윙이 아니라 지면 견인이 되기 때문이다.
        /// </summary>
        public const string GroundLayerName = "Ground";

        private const string RootName = "_GeneratedCity";

        [SerializeField] private CityLayoutSettings settings = null;

        [Header("시각화")]
        [Tooltip("씬 뷰에 앵커 후보점과 간격 라벨을 그린다.")]
        [SerializeField] private bool drawGizmos = true;
        [Tooltip("라벨을 그릴 건물 수 상한. 도시가 커지면 에디터가 느려지므로 자른다.")]
        [SerializeField] private int maxGizmoLabels = 40;

        public CityLayoutSettings Settings => settings;

        public Transform GeneratedRoot => transform.Find(RootName);

        public bool HasGenerated => GeneratedRoot != null;

        // ─── 생성 ───

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (settings == null)
            {
                Debug.LogError("[CityGenerator] CityLayoutSettings가 비어 있다. 프리셋 에셋을 지정할 것.", this);
                return;
            }

            Clear();

            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);

            int layer = LayerMask.NameToLayer(AnchorLayerName);
            if (layer < 0)
            {
                Debug.LogWarning(
                    $"[CityGenerator] '{AnchorLayerName}' 레이어가 없다. Default로 생성한다. " +
                    "앵커 레이캐스트가 지면까지 잡게 되므로 인스펙터의 레이어 생성 버튼을 누를 것.", this);
            }

            // 격자를 이 오브젝트 중심에 맞춘다.
            float originX = -(settings.blockCountX - 1) * settings.PitchX * 0.5f;
            float originZ = -(settings.blockCountZ - 1) * settings.PitchZ * 0.5f;

            var emptyMask = settings.BuildEmptyMask();
            var placed = new List<CityBuilding>();

            for (int x = 0; x < settings.blockCountX; x++)
            {
                for (int z = 0; z < settings.blockCountZ; z++)
                {
                    // 빈 칸은 광장으로 남긴다. 도로는 건물과 무관하게 깔리므로 그대로 이어진다.
                    if (emptyMask[x, z]) continue;

                    int targetFloors = settings.FloorCountAt(x, z);
                    var basePos = new Vector3(originX + x * settings.PitchX, 0f, originZ + z * settings.PitchZ);

                    var building = settings.HasPrefabs
                        ? CreatePrefabBuilding(x, z, targetFloors)
                        : CreateGreyboxBuilding(targetFloors);

                    if (building == null) continue;

                    building.transform.SetParent(root.transform, false);
                    building.transform.localPosition = basePos;

                    // 프리팹은 이미 실측 치수가 담긴 CityBuilding을 들고 온다. 격자 좌표만 채운다.
                    var meta = building.GetComponent<CityBuilding>();
                    if (meta == null) meta = building.AddComponent<CityBuilding>();
                    meta.gridX = x;
                    meta.gridZ = z;

                    // 바닥 크기는 로컬 값으로 남겨둔다. 앵커 좌표가 TransformPoint를 타므로
                    // 회전은 그쪽에서 알아서 반영된다.
                    if (settings.randomYRotation)
                    {
                        int steps = settings.YRotationSteps(x, z);
                        building.transform.localRotation = Quaternion.Euler(0f, steps * 90f, 0f);
                    }

                    building.name = $"Building_{x}_{z}_{meta.floorCount}F";
                    if (layer >= 0) SetLayerRecursive(building, layer);
                    placed.Add(meta);
                }
            }

            int props = CreateRoofProps(root.transform, placed);

            // 도로와 광장을 먼저 깐다. 표면이 y=0보다 얼마나 파여 있는지를 재고 나서야
            // 지면을 그보다 아래에 둘 수 있기 때문이다.
            int roadTiles = CreateRoads(root.transform, originX, originZ,
                                        out float surfaceRecess, out int crosswalks);
            var (plazaFloorTiles, plazaProps) =
                CreatePlazas(root.transform, emptyMask, originX, originZ, ref surfaceRecess);
            if (settings.generateGround) CreateGround(root.transform, surfaceRecess);

            if (settings.markStatic) MarkStaticRecursive(root);

            int emptyCells = settings.TotalBuildings - placed.Count;
            Debug.Log($"[CityGenerator] 건물 {placed.Count}채 생성" +
                      (emptyCells > 0 ? $" (빈 칸 {emptyCells}개)" : string.Empty) +
                      $". 앵커 간격 {settings.PitchX:F1}m / 최고 높이 {settings.MaxHeight:F0}m" +
                      (roadTiles > 0 ? $" / 도로 타일 {roadTiles}장" : string.Empty) +
                      (crosswalks > 0 ? $" / 횡단보도 {crosswalks}개" : string.Empty) +
                      (plazaFloorTiles > 0 ? $" / 광장 바닥 {plazaFloorTiles}장" : string.Empty) +
                      (plazaProps > 0 ? $" / 광장 소품 {plazaProps}개" : string.Empty) +
                      (props > 0 ? $" / 옥상 소품 {props}개" : string.Empty), this);
        }

        /// <summary>
        /// 옥상에 소품을 흩어놓는다.
        ///
        /// 스윙 고도에서 시선이 가장 오래 머무는 면이 옥상이다. 지상 소품(볼라드·맨홀)은
        /// 아무리 깔아도 보이지 않는 반면, 옥상 실외기 몇 개가 체감에 훨씬 크게 기여한다.
        ///
        /// 소품을 건물의 자식으로 넣지 않고 별도 루트에 둔다. 프리팹 인스턴스에 자식을
        /// 추가하면 오버라이드가 쌓이므로, 대신 건물의 <c>TransformPoint</c>로 월드 좌표를
        /// 계산해 배치한다 — 건물에 준 90° 회전이 그대로 반영된다.
        /// </summary>
        private int CreateRoofProps(Transform parent, List<CityBuilding> buildings)
        {
            if (buildings.Count == 0 || settings.roofPropCountMax <= 0) return 0;

            // 프리팹별 규격을 한 번만 재둔다. 피벗이 바닥이 아닌 소품도 제자리에 앉게.
            var specs = new List<(GameObject prefab, Bounds bounds)>();
            foreach (var prefab in settings.roofPropPrefabs)
            {
                if (prefab == null) continue;
                if (TryMeasurePrefab(prefab, parent, out Bounds bounds)) specs.Add((prefab, bounds));
            }
            if (specs.Count == 0) return 0;

            var root = new GameObject("_RoofProps");
            root.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer(AnchorLayerName);

            int lowCount = Mathf.Min(settings.roofPropCountMin, settings.roofPropCountMax);
            int highCount = Mathf.Max(settings.roofPropCountMin, settings.roofPropCountMax);
            int total = 0;

            foreach (var building in buildings)
            {
                var rng = settings.RoofPropRandom(building.gridX, building.gridZ);
                int count = rng.Next(lowCount, highCount + 1);

                float halfX = building.footprint.x * 0.5f - settings.roofPropEdgeMargin;
                float halfZ = building.footprint.y * 0.5f - settings.roofPropEdgeMargin;
                if (halfX <= 0f || halfZ <= 0f) continue; // 옥상이 여유분보다 좁으면 건너뛴다

                for (int i = 0; i < count; i++)
                {
                    var spec = specs[rng.Next(specs.Count)];
                    float yaw = (float)rng.NextDouble() * 360f;

                    Vector2 local;
                    if (settings.roofPropPlacement == RoofPropPlacement.Perimeter)
                    {
                        // 가장자리를 균등하게 돈다. 시작 위치만 무작위로 흔들어 건물마다 다르게.
                        float t = (i + 0.5f) / count + (float)rng.NextDouble();
                        local = PointOnRectPerimeter(t, halfX, halfZ);
                    }
                    else
                    {
                        local = new Vector2((float)(rng.NextDouble() * 2.0 - 1.0) * halfX,
                                            (float)(rng.NextDouble() * 2.0 - 1.0) * halfZ);
                    }

                    Vector3 spot = building.transform.TransformPoint(
                        new Vector3(local.x, building.height, local.y));

                    var prop = InstantiateLinked(spec.prefab);
                    prop.transform.SetParent(root.transform, false);
                    prop.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    // 소품 자체의 밑면이 옥상에 닿게 내려준다.
                    prop.transform.position = spot - Vector3.up * spec.bounds.min.y;
                    if (layer >= 0) SetLayerRecursive(prop, layer);
                    total++;
                }
            }

            return total;
        }

        /// <summary>
        /// 직사각형 둘레를 한 바퀴 도는 경로에서 <paramref name="t"/>(0~1) 지점을 돌려준다.
        /// 옥상 가장자리에 소품을 균등하게 돌리는 데 쓴다.
        /// </summary>
        private static Vector2 PointOnRectPerimeter(float t, float halfX, float halfZ)
        {
            float width = halfX * 2f;
            float depth = halfZ * 2f;
            float distance = Mathf.Repeat(t, 1f) * (width + depth) * 2f;

            if (distance < width) return new Vector2(-halfX + distance, -halfZ);
            distance -= width;
            if (distance < depth) return new Vector2(halfX, -halfZ + distance);
            distance -= depth;
            if (distance < width) return new Vector2(halfX - distance, halfZ);
            distance -= width;
            return new Vector2(-halfX, halfZ - distance);
        }

        // ─── 광장 ───
        //
        // 빈 칸을 그대로 두면 광장이 아니라 "빠뜨린 칸"으로 보인다.
        // 바닥을 깔고 소품을 몇 개 놓는 것만으로 의도된 공간으로 읽힌다.

        /// <returns>
        /// (바닥 타일 수, 소품 수)를 나눠 돌려준다. 합쳐서 반환하면 로그에서
        /// "광장 267개"가 실제로는 대부분 바닥 타일이라는 걸 알 수 없어 혼동을 준다.
        /// </returns>
        private (int floorTiles, int props) CreatePlazas(
            Transform parent, bool[,] emptyMask, float originX, float originZ, ref float surfaceRecess)
        {
            bool hasFloor = settings.plazaFloorPrefab != null;
            var propSpecs = new List<(GameObject prefab, Bounds bounds)>();
            foreach (var prefab in settings.plazaPropPrefabs)
            {
                if (prefab == null) continue;
                if (TryMeasurePrefab(prefab, parent, out Bounds bounds)) propSpecs.Add((prefab, bounds));
            }
            if (!hasFloor && propSpecs.Count == 0) return (0, 0);

            Bounds floorBounds = default;
            if (hasFloor && !TryMeasurePrefab(settings.plazaFloorPrefab, parent, out floorBounds))
                hasFloor = false;
            if (hasFloor) surfaceRecess = Mathf.Max(surfaceRecess, -floorBounds.min.y);

            var root = new GameObject("_Plazas");
            root.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer(GroundLayerName);

            int lowCount = Mathf.Min(settings.plazaPropCountMin, settings.plazaPropCountMax);
            int highCount = Mathf.Max(settings.plazaPropCountMin, settings.plazaPropCountMax);
            int floorTiles = 0;
            int props = 0;

            for (int x = 0; x < settings.blockCountX; x++)
            {
                for (int z = 0; z < settings.blockCountZ; z++)
                {
                    if (!emptyMask[x, z]) continue;

                    float cx = originX + x * settings.PitchX;
                    float cz = originZ + z * settings.PitchZ;
                    var center = new Vector2(cx, cz);

                    if (hasFloor)
                    {
                        floorTiles += TileArea(settings.plazaFloorPrefab, floorBounds, center,
                                               settings.footprintX, settings.footprintZ, root.transform, layer);
                    }

                    if (propSpecs.Count == 0) continue;

                    var rng = settings.PlazaPropRandom(x, z);
                    int count = rng.Next(lowCount, highCount + 1);
                    float halfX = settings.footprintX * 0.5f - settings.plazaPropEdgeMargin;
                    float halfZ = settings.footprintZ * 0.5f - settings.plazaPropEdgeMargin;
                    if (halfX <= 0f || halfZ <= 0f) continue;

                    for (int i = 0; i < count; i++)
                    {
                        var spec = propSpecs[rng.Next(propSpecs.Count)];
                        var prop = InstantiateLinked(spec.prefab);
                        prop.transform.SetParent(root.transform, false);
                        prop.transform.localRotation =
                            Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        prop.transform.localPosition = new Vector3(
                            cx + (float)(rng.NextDouble() * 2.0 - 1.0) * halfX,
                            -spec.bounds.min.y,
                            cz + (float)(rng.NextDouble() * 2.0 - 1.0) * halfZ);
                        if (layer >= 0) SetLayerRecursive(prop, layer);
                        props++;
                    }
                }
            }

            return (floorTiles, props);
        }

        // ─── 도로 ───
        //
        // 격자는 이미 알고 있으므로 도로는 "블록 사이 통로"를 채우는 문제로 환원된다.
        // 타일 규격을 설정에 적어두지 않고 매번 재는 이유는, 적어둔 값과 실제 에셋이
        // 어긋나는 순간 도시 전체가 조용히 밀리기 때문이다. 재면 어긋날 수가 없다.

        /// <summary>두 면이 정확히 겹치면 z-fighting이 나므로 이만큼 떨어뜨린다.</summary>
        private const float SurfaceEpsilon = 0.01f;

        /// <param name="roadRecess">가장 깊이 파인 도로 표면의 깊이. 지면 높이를 정하는 데 쓴다.</param>
        private int CreateRoads(Transform parent, float originX, float originZ, out float roadRecess,
                                out int crosswalks)
        {
            roadRecess = 0f;
            crosswalks = 0;
            if (settings.roadSegmentPrefab == null) return 0;
            if (!TryMeasurePrefab(settings.roadSegmentPrefab, parent, out Bounds segment)) return 0;

            roadRecess = Mathf.Max(0f, -segment.min.y);

            float segmentLength = Mathf.Min(segment.size.x, segment.size.z);
            float crossSection = Mathf.Max(segment.size.x, segment.size.z);
            // 폭이 어느 축에 있는지에 따라 통로에 맞추는 회전이 달라진다.
            bool crossAlongZ = segment.size.z >= segment.size.x;

            if (Mathf.Abs(crossSection - settings.streetWidth) > 0.1f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 도로 타일 폭이 {crossSection:F2}m인데 설정 도로 폭은 " +
                    $"{settings.streetWidth:F2}m다. 통로와 어긋나므로 streetWidth를 " +
                    $"{crossSection:F2}로 맞추는 편이 낫다.", this);
            }

            int tilesAlongZ = Mathf.Max(1, Mathf.RoundToInt(settings.footprintZ / segmentLength));
            int tilesAlongX = Mathf.Max(1, Mathf.RoundToInt(settings.footprintX / segmentLength));
            WarnIfNotDivisible(settings.footprintZ, segmentLength);
            WarnIfNotDivisible(settings.footprintX, segmentLength);

            var roads = new GameObject("_Roads");
            roads.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer(GroundLayerName);
            int placed = 0;

            // Z 방향으로 뻗는 통로 — 블록의 +X 쪽. 마지막 열 바깥에는 깔지 않는다.
            for (int x = 0; x < settings.blockCountX - 1; x++)
            {
                for (int z = 0; z < settings.blockCountZ; z++)
                {
                    float cx = originX + x * settings.PitchX + settings.PitchX * 0.5f;
                    float cz = originZ + z * settings.PitchZ;
                    for (int k = 0; k < tilesAlongZ; k++)
                    {
                        float tz = cz - settings.footprintZ * 0.5f + segmentLength * (k + 0.5f);
                        PlaceTile(settings.roadSegmentPrefab, segment, new Vector2(cx, tz),
                                  crossAlongZ ? 90f : 0f, roads.transform, layer);
                        placed++;
                    }
                }
            }

            // X 방향으로 뻗는 통로 — 블록의 +Z 쪽.
            for (int z = 0; z < settings.blockCountZ - 1; z++)
            {
                for (int x = 0; x < settings.blockCountX; x++)
                {
                    float cx = originX + x * settings.PitchX;
                    float cz = originZ + z * settings.PitchZ + settings.PitchZ * 0.5f;
                    for (int k = 0; k < tilesAlongX; k++)
                    {
                        float tx = cx - settings.footprintX * 0.5f + segmentLength * (k + 0.5f);
                        PlaceTile(settings.roadSegmentPrefab, segment, new Vector2(tx, cz),
                                  crossAlongZ ? 0f : 90f, roads.transform, layer);
                        placed++;
                    }
                }
            }

            placed += CreateIntersections(roads.transform, originX, originZ, layer, ref roadRecess);
            crosswalks = CreateCrosswalks(roads.transform, originX, originZ, layer);
            return placed;
        }

        /// <summary>
        /// 교차로마다 네 방향 진입부에 횡단보도를 깐다.
        ///
        /// 애비뉴(세로, Z축 방향 도로)의 남·북 진입부는 도로 세그먼트와 같은 방향
        /// 규칙을 쓰고, 스트리트(가로, X축 방향 도로)의 동·서 진입부는 반대로 쓴다.
        /// 데칼이 도로 표면과 같은 y=0에 놓이면 서로 겹쳐 z-fighting이 나므로 살짝 띄운다.
        /// </summary>
        private int CreateCrosswalks(Transform parent, float originX, float originZ, int layer)
        {
            if (settings.crosswalkPrefab == null) return 0;
            if (!TryMeasurePrefab(settings.crosswalkPrefab, parent, out Bounds decal)) return 0;

            float crossSection = Mathf.Max(decal.size.x, decal.size.z);
            float thickness = Mathf.Max(0.05f, Mathf.Min(decal.size.x, decal.size.z));
            bool crossAlongZ = decal.size.z >= decal.size.x;

            // 도로 폭보다 좁은 것은 정상이다 — 횡단보도는 차도만 칠하고 인도는 비워두는 게
            // 맞는 그림이고, 배치는 도로 중심선 기준이라 좁아도 차도 중앙에 그대로 앉는다.
            // 반대로 도로 폭보다 크면 인도나 건물 쪽으로 삐져나오므로 그 경우만 경고한다.
            if (crossSection > settings.streetWidth + 0.1f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 횡단보도 폭이 {crossSection:F2}m인데 도로 폭은 " +
                    $"{settings.streetWidth:F2}m다. 도로보다 커서 인도 쪽으로 넘친다.", this);
            }

            float half = settings.streetWidth * 0.5f - thickness * 0.5f;
            if (half <= 0f)
            {
                Debug.LogWarning("[CityGenerator] 횡단보도가 도로 폭보다 두꺼워 놓을 자리가 없다.", this);
                return 0;
            }

            int placed = 0;
            for (int x = 0; x < settings.blockCountX - 1; x++)
            {
                for (int z = 0; z < settings.blockCountZ - 1; z++)
                {
                    float cx = originX + x * settings.PitchX + settings.PitchX * 0.5f;
                    float cz = originZ + z * settings.PitchZ + settings.PitchZ * 0.5f;

                    // 애비뉴 진입부 — 남 · 북
                    PlaceAtIntersection(new Vector2(cx, cz - half), crossAlongZ ? 90f : 0f);
                    PlaceAtIntersection(new Vector2(cx, cz + half), crossAlongZ ? 90f : 0f);
                    // 스트리트 진입부 — 서 · 동
                    PlaceAtIntersection(new Vector2(cx - half, cz), crossAlongZ ? 0f : 90f);
                    PlaceAtIntersection(new Vector2(cx + half, cz), crossAlongZ ? 0f : 90f);
                    placed += 4;
                }
            }
            return placed;

            void PlaceAtIntersection(Vector2 pos, float yaw) =>
                PlaceTile(settings.crosswalkPrefab, decal, pos, yaw, parent, layer, SurfaceEpsilon);
        }

        /// <summary>
        /// 통로가 교차하는 정사각 자리를 채운다.
        ///
        /// 타일 하나로 덮는 게 아니라 <b>n×n으로 깐다.</b> 이 키트의 4차선 교차로는 24.67m라
        /// 12m 통로에 안 들어가고, 대신 6×6m 아스팔트를 2×2로 깔면 정확히 맞는다.
        /// 통로 폭을 타일 크기로 나눠 몇 장인지 스스로 계산한다.
        /// </summary>
        private int CreateIntersections(Transform parent, float originX, float originZ, int layer,
                                        ref float roadRecess)
        {
            if (settings.intersectionPrefab == null) return 0;
            if (!TryMeasurePrefab(settings.intersectionPrefab, parent, out Bounds cross)) return 0;

            float tileSize = Mathf.Max(cross.size.x, cross.size.z);
            if (tileSize > settings.streetWidth + 0.1f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 교차로 타일이 {tileSize:F2}m인데 통로는 {settings.streetWidth:F2}m다. " +
                    "통로보다 커서 깔지 않는다. 더 작은 타일(예: Street_Asphalt_6x6)을 쓸 것.", this);
                return 0;
            }

            roadRecess = Mathf.Max(roadRecess, -cross.min.y);

            int placed = 0;
            for (int x = 0; x < settings.blockCountX - 1; x++)
            {
                for (int z = 0; z < settings.blockCountZ - 1; z++)
                {
                    float cx = originX + x * settings.PitchX + settings.PitchX * 0.5f;
                    float cz = originZ + z * settings.PitchZ + settings.PitchZ * 0.5f;
                    placed += TileArea(settings.intersectionPrefab, cross, new Vector2(cx, cz),
                                       settings.streetWidth, settings.streetWidth, parent, layer);
                }
            }
            return placed;
        }

        /// <summary>
        /// 정사각형 구역을 타일로 채운다. 교차로와 광장이 같은 문제라 함께 쓴다.
        /// 타일 개수는 구역 크기를 타일 크기로 나눠 스스로 정하고, 나누어지지 않으면 경고한다.
        /// </summary>
        private int TileArea(GameObject prefab, Bounds bounds, Vector2 center,
                             float spanX, float spanZ, Transform parent, int layer)
        {
            float tileX = Mathf.Max(0.01f, bounds.size.x);
            float tileZ = Mathf.Max(0.01f, bounds.size.z);
            int countX = Mathf.Max(1, Mathf.RoundToInt(spanX / tileX));
            int countZ = Mathf.Max(1, Mathf.RoundToInt(spanZ / tileZ));

            if (Mathf.Abs(countX * tileX - spanX) > 0.1f || Mathf.Abs(countZ * tileZ - spanZ) > 0.1f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] '{prefab.name}' {tileX:F2}×{tileZ:F2}m가 " +
                    $"{spanX:F1}×{spanZ:F1}m 구역에 정확히 들어가지 않는다 " +
                    $"({spanX / tileX:F2}×{spanZ / tileZ:F2}장). 틈이나 겹침이 생긴다.", this);
            }

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
        /// 타일 중심이 목표 지점에 오도록 놓는다.
        ///
        /// 높이는 손대지 않고 y=0에 둔다. 이 키트는 y=0을 인도 표면으로 잡고 차도를 그 아래로
        /// 파놨고, 건물 피벗도 y=0이다. 즉 y=0이 에셋 전체의 지면 평면이므로 거기 맞추는 것이
        /// 곧 인도와 건물 밑동이 맞아떨어지는 방법이다. 지면 박스를 대신 내린다.
        ///
        /// 가로 위치는 피벗이 중앙이 아닌 모델도 있으므로 바운즈 중심으로 보정한다.
        /// </summary>
        private void PlaceTile(GameObject prefab, Bounds bounds, Vector2 target, float yaw,
                               Transform parent, int layer, float yOffset = 0f)
        {
            var tile = InstantiateLinked(prefab);
            tile.transform.SetParent(parent, false);

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            tile.transform.localRotation = rotation;

            Vector3 pivotOffset = rotation * new Vector3(bounds.center.x, 0f, bounds.center.z);
            tile.transform.localPosition = new Vector3(
                target.x - pivotOffset.x, yOffset, target.y - pivotOffset.z);

            if (layer >= 0) SetLayerRecursive(tile, layer);
        }

        /// <summary>
        /// 프리팹을 잠깐 놓아 규격을 재고 지운다.
        /// 에셋에서 바운즈를 직접 읽는 방법도 있지만, 실제로 놓은 것을 재는 편이 확실하다.
        /// </summary>
        private bool TryMeasurePrefab(GameObject prefab, Transform parent, out Bounds bounds)
        {
            var probe = InstantiateLinked(prefab);
            probe.transform.SetParent(parent, false);
            probe.transform.localPosition = Vector3.zero;
            probe.transform.localRotation = Quaternion.identity;

            var renderers = probe.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[CityGenerator] '{prefab.name}'에 렌더러가 없어 규격을 잴 수 없다.", this);
                bounds = default;
                DestroyObject(probe);
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // min.y가 음수인 것은 정상이다 — 차도가 인도 표면 아래로 파여 있다는 뜻이고,
            // 지면 높이를 그만큼 내려서 처리한다. 반대로 양수면 타일이 공중에 뜬다.
            if (bounds.min.y > 0.05f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] '{prefab.name}'의 밑면이 y={bounds.min.y:F2}에 있다. " +
                    "지면 위에 떠 보인다.", this);
            }

            DestroyObject(probe);
            return true;
        }

        private GameObject InstantiateLinked(GameObject prefab)
        {
#if UNITY_EDITOR
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

        private void WarnIfNotDivisible(float span, float tileLength)
        {
            float tiles = span / tileLength;
            if (Mathf.Abs(tiles - Mathf.Round(tiles)) < 0.02f) return;

            Debug.LogWarning(
                $"[CityGenerator] 블록 한 변 {span:F1}m가 타일 길이 {tileLength:F1}m로 나누어지지 않는다 " +
                $"({tiles:F2}장). 통로 끝에 틈이 생긴다. " +
                $"바닥 크기를 {Mathf.Round(tiles) * tileLength:F0}m로 맞추는 편이 낫다.", this);
        }

        /// <summary>
        /// 추락을 받아줄 지면.
        ///
        /// Plane이 아니라 두꺼운 박스를 쓴다. Plane은 두께가 0이라 고속 낙하에서 그냥 뚫리고,
        /// 스윙을 놓친 플레이어는 정확히 그 고속 낙하 상태다.
        /// 윗면이 Y=0에 오도록 아래로 내려 배치한다.
        ///
        /// 레이어는 앵커와 분리한다. 지면에 웹이 붙으면 스윙이 성립하지 않기 때문이다.
        /// </summary>
        /// <param name="roadRecess">
        /// 차도가 인도 표면(y=0)보다 파여 있는 깊이. 지면 윗면을 이보다 아래로 내려야
        /// 아스팔트가 지면 박스에 파묻히지 않는다.
        /// </param>
        private void CreateGround(Transform parent, float roadRecess)
        {
            var span = settings.CitySpan;
            float sizeX = span.x + settings.groundMargin * 2f;
            float sizeZ = span.y + settings.groundMargin * 2f;
            float surfaceY = -roadRecess - SurfaceEpsilon;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "_Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(sizeX, settings.groundThickness, sizeZ);
            ground.transform.localPosition =
                new Vector3(0f, surfaceY - settings.groundThickness * 0.5f, 0f);

            if (settings.groundMaterial != null)
                ground.GetComponent<MeshRenderer>().sharedMaterial = settings.groundMaterial;

            int layer = LayerMask.NameToLayer(GroundLayerName);
            if (layer >= 0) ground.layer = layer;
            else Debug.LogWarning(
                $"[CityGenerator] '{GroundLayerName}' 레이어가 없어 지면을 Default로 둔다. " +
                "이대로면 웹이 아스팔트에 붙는다.", this);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            var root = GeneratedRoot;
            if (root == null) return;
            DestroyObject(root.gameObject);
        }

        /// <summary>
        /// 실제 건물 프리팹 한 채.
        ///
        /// 프리팹 링크를 유지해서 인스턴스화한다. 36채를 놓은 뒤 프리팹 하나를 고치면
        /// 전부 따라오게 하려는 것이다. 링크가 없으면 콜라이더 하나 수정에 36번을 손대야 한다.
        /// </summary>
        private GameObject CreatePrefabBuilding(int x, int z, int targetFloors)
        {
            var prefab = settings.PickPrefab(x, z, targetFloors);
            if (prefab == null) return null;

            if (CityLayoutSettings.MetaOf(prefab) == null)
            {
                Debug.LogError($"[CityGenerator] '{prefab.name}'에 CityBuilding이 없다. " +
                               "실측 치수를 모르므로 배치할 수 없다. BuildingPrefabTool로 다시 만들 것.", prefab);
                return null;
            }

            return InstantiateLinked(prefab);
        }

        private GameObject CreateGreyboxBuilding(int floors)
        {
            float height = floors * CityLayoutSettings.FloorHeight;
            var building = settings.shellMode ? CreateShellBuilding(height) : CreateSolidBuilding(height);

            var meta = building.AddComponent<CityBuilding>();
            meta.floorCount = floors;
            meta.height = height;
            meta.footprint = new Vector2(settings.footprintX, settings.footprintZ);
            meta.isShell = settings.shellMode;
            return building;
        }

        /// <summary>속 찬 큐브 한 개. 기본 모드.</summary>
        private GameObject CreateSolidBuilding(float height)
        {
            // 큐브는 피벗이 중앙이다. 래퍼를 씌워 바닥 피벗으로 만든다 —
            // 에셋 모델이 전부 Y=0 기준이라 교체할 때 보정이 없어야 한다.
            var wrapper = new GameObject("wrapper");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(wrapper.transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(settings.footprintX, height, settings.footprintZ);
            ApplyMaterial(body);

            // 콜라이더는 건물 루트에 하나만 둔다.
            // 프리미티브가 들고 온 자식 콜라이더를 그대로 두면 레이캐스트 결과가 Body를 가리켜
            // 앵커 코드가 GetComponentInParent를 타야 한다. 루트에 모으면 히트 → CityBuilding이 한 홉이다.
            DestroyPrimitiveCollider(body);
            var box = wrapper.AddComponent<BoxCollider>();
            box.size = new Vector3(settings.footprintX, height, settings.footprintZ);
            box.center = new Vector3(0f, height * 0.5f, 0f);
            return wrapper;
        }

        /// <summary>
        /// 얇은 벽 4장 + 옥상. 터널링 검증용.
        /// 속 찬 큐브에서는 고속 충돌 문제가 관측되지 않기 때문에 필요하다.
        /// 실제 건물 에셋은 두께 0.2m 껍데기이고, 터널링은 그때 처음 등장한다.
        ///
        /// 이 모드만 예외적으로 벽마다 콜라이더를 남긴다.
        /// 얇은 벽을 통과하는지 보는 것이 목적이므로 루트 BoxCollider 하나로 덮으면 의미가 없다.
        /// </summary>
        private GameObject CreateShellBuilding(float height)
        {
            var wrapper = new GameObject("wrapper");
            float t = settings.wallThickness;
            float hx = settings.footprintX * 0.5f;
            float hz = settings.footprintZ * 0.5f;

            AddPart(wrapper, "Wall_N", new Vector3(0f, height * 0.5f, hz - t * 0.5f),
                    new Vector3(settings.footprintX, height, t));
            AddPart(wrapper, "Wall_S", new Vector3(0f, height * 0.5f, -hz + t * 0.5f),
                    new Vector3(settings.footprintX, height, t));
            AddPart(wrapper, "Wall_E", new Vector3(hx - t * 0.5f, height * 0.5f, 0f),
                    new Vector3(t, height, settings.footprintZ - t * 2f));
            AddPart(wrapper, "Wall_W", new Vector3(-hx + t * 0.5f, height * 0.5f, 0f),
                    new Vector3(t, height, settings.footprintZ - t * 2f));
            AddPart(wrapper, "Roof", new Vector3(0f, height - t * 0.5f, 0f),
                    new Vector3(settings.footprintX, t, settings.footprintZ));

            return wrapper;
        }

        private void AddPart(GameObject parent, string partName, Vector3 localPos, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;
            ApplyMaterial(part);
        }

        /// <summary>프리미티브가 기본으로 들고 오는 콜라이더를 떼어낸다.</summary>
        private static void DestroyPrimitiveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        private void ApplyMaterial(GameObject go)
        {
            if (settings.greyboxMaterial == null) return;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = settings.greyboxMaterial;
        }

        /// <summary>
        /// 생성물을 Static으로 표시한다.
        /// 도시를 10×10으로 키우면 오브젝트가 2천 개를 넘는데, 정적 배칭이 없으면
        /// 그 수가 드로우 콜로 그대로 나온다. 도시는 움직이지 않으니 전부 켜도 된다.
        /// </summary>
        private static void MarkStaticRecursive(GameObject go)
        {
            go.isStatic = true;
            foreach (Transform child in go.transform) MarkStaticRecursive(child.gameObject);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        // ─── 시각화 ───
        //
        // 생성기만 만들면 결국 눈으로 "간격이 맞나?"를 판단하게 된다. 그건 실측이 아니다.
        // 이론 사거리를 씬에 그려두면 물리를 붙이기 전에도 "이 맵은 앵커가 듬성하다"를 판정할 수 있다.

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || settings == null) return;

            var budget = settings.Evaluate();
            var buildings = new List<CityBuilding>(GetComponentsInChildren<CityBuilding>());
            bool pitchTooWide = settings.PitchX > budget.Reach;

            // 앵커 후보점
            Gizmos.color = pitchTooWide ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 1f, 0.5f);
            foreach (var b in buildings)
            {
                foreach (var p in b.RoofAnchorCandidates())
                    Gizmos.DrawSphere(p, 0.6f);
            }

            // 이론 사거리 — 첫 건물 옥상에서 로프가 닿는 범위
            if (buildings.Count > 0)
            {
                Vector3 probe = buildings[0].RoofCenter;
                Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.35f);
                Gizmos.DrawWireSphere(probe, settings.ropeLength);
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
                Gizmos.DrawWireSphere(probe, budget.Reach);
            }

            // 간격 라벨
            int labelled = 0;
            var style = new GUIStyle
            {
                normal = { textColor = pitchTooWide ? Color.red : Color.white },
                fontSize = 11,
            };
            foreach (var b in buildings)
            {
                if (labelled++ >= maxGizmoLabels) break;
                if (b.gridZ != 0) continue; // 첫 열만 — 라벨이 겹치면 못 읽는다
                Vector3 at = b.RoofCenter + Vector3.up * 2f;
                UnityEditor.Handles.Label(at, $"{b.floorCount}F  {b.height:F0}m", style);
            }

            // 최소 높이 기준선 — 앵커가 이보다 낮으면 로프가 지면에 닿는다
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            float span = Mathf.Max(settings.blockCountX * settings.PitchX,
                                   settings.blockCountZ * settings.PitchZ) * 0.5f;
            Vector3 c = transform.position + Vector3.up * budget.MinHeight;
            Gizmos.DrawLine(c + new Vector3(-span, 0f, 0f), c + new Vector3(span, 0f, 0f));
            Gizmos.DrawLine(c + new Vector3(0f, 0f, -span), c + new Vector3(0f, 0f, span));
        }
#endif
    }
}
