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
        /// 물리로 움직이는 프롭(드럼통·화분 등) 전용 레이어. <c>Anchorable</c>과 반드시
        /// 분리해야 한다 — 안 나누면 스윙하려고 쏜 갈고리가 프롭에 걸려 프롭만 딸려오고
        /// 플레이어는 추락한다(README "레이어를 반드시 나눈다" 참고).
        /// </summary>
        public const string GrabbableLayerName = "Grabbable";

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

        // ─── 중력 ───

        /// <summary>
        /// 이 도시를 만들 때 가정한 중력을 <b>실제 물리에도 건다.</b>
        ///
        /// 여기가 아니면 자리가 없다. <c>gravityScale</c>은 레벨 메트릭의 입력이고
        /// (건물 높이·간격·최고 건물이 전부 이 값에서 역산된다), 그 값으로 세운 도시를
        /// 다른 중력에서 플레이하면 도시가 근거를 잃는다. 생성기가 그 가정의 주인이므로
        /// 적용도 생성기가 한다 — 프로젝트 설정에 숫자를 따로 적어두면 프리셋을 바꿀 때
        /// 한쪽만 고치는 사고가 나고, 그게 실제로 났던 사고다.
        ///
        /// <b>실제로 났던 사고가 이것이다.</b> 도시는 <c>gravityScale 3</c>(29.43 m/s²)으로
        /// 생성돼 있었는데 <c>Physics.gravity</c>는 아무도 안 건드려 기본값 9.81로 돌고 있었다.
        /// 진자 주기가 √3배 늘어져 스윙이 굼떴고, 최저점 속도는 설계의 0.58배밖에 안 나왔으며,
        /// 무엇보다 <c>SwingConstraint</c>의 펌프 가속도(18)가 중력보다 <b>커져서</b>
        /// "입력이 물리보다 덜 지배적이어야 한다"는 전제 자체가 뒤집혀 있었다.
        ///
        /// <c>Awake</c>인 이유는 <c>Physics.gravity</c>가 캐시되지 않고 매 물리 스텝에
        /// 읽히기 때문이다. 첫 <c>FixedUpdate</c> 전에만 서 있으면 되고, 그래서 다른
        /// 스크립트와의 실행 순서를 맞출 필요가 없다.
        /// </summary>
        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogWarning("[CityGenerator] 프리셋이 비어 있어 중력을 기본값으로 둔다. " +
                                 "도시가 다른 중력을 가정하고 생성됐다면 스윙 주기가 어긋난다.", this);
                return;
            }

            Physics.gravity = Vector3.down * settings.GravityMagnitude;
        }

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

                    // 칸 정중앙이 아니라 조금 밀어 놓는다. 도로·광장·교차로는 격자 그대로
                    // 깔리므로 건물만 흔들리고, 그래서 눈에 보이던 격자 선이 끊긴다.
                    Vector2 jitter = settings.CellOffset(x, z);
                    var basePos = new Vector3(originX + x * settings.PitchX + jitter.x, 0f,
                                              originZ + z * settings.PitchZ + jitter.y);

                    var building = settings.HasPrefabs
                        ? CreatePrefabBuilding(x, z, targetFloors)
                        : CreateGreyboxBuilding(targetFloors);

                    if (building == null) continue;

                    building.transform.SetParent(root.transform, false);
                    building.transform.localPosition = basePos;

                    // 치수는 만드는 쪽에서 이미 채워져 있다 (타워는 겹 합계, 회색 박스는 층수).
                    // 여기서는 격자 좌표만 채운다.
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
            int rails = CreateRoofRails(root.transform, placed);

            // 도로와 광장을 먼저 깐다. 표면이 y=0보다 얼마나 파여 있는지를 재고 나서야
            // 지면을 그보다 아래에 둘 수 있기 때문이다.
            int roadTiles = CreateRoads(root.transform, originX, originZ,
                                        out float surfaceRecess, out int crosswalks);
            var (plazaFloorTiles, plazaProps) =
                CreatePlazas(root.transform, emptyMask, originX, originZ, ref surfaceRecess);
            if (settings.generateGround) CreateGround(root.transform, surfaceRecess);
            if (settings.generateBoundaryWall) CreateBoundaryWall(root.transform, surfaceRecess);

            if (settings.markStatic) MarkStaticRecursive(root);

            // 실제로 선 높이를 보고한다. 설정값(MaxHeight)은 이론 상한이라, 셋백 조합에 따라
            // 실제 최고 타워가 그보다 낮게 나올 수 있다 — 튜닝에 쓸 숫자는 이쪽이다.
            float shortest = float.MaxValue, tallest = 0f;
            foreach (var b in placed)
            {
                shortest = Mathf.Min(shortest, b.height);
                tallest = Mathf.Max(tallest, b.height);
            }
            string heightNote = placed.Count == 0
                ? $"최고 높이 {settings.MaxHeight:F0}m"
                : $"건물 높이 {shortest:F0}~{tallest:F0}m";

            int emptyCells = settings.TotalBuildings - placed.Count;
            Debug.Log($"[CityGenerator] 건물 {placed.Count}채 생성" +
                      (emptyCells > 0 ? $" (빈 칸 {emptyCells}개)" : string.Empty) +
                      $". 앵커 간격 {settings.PitchX:F1}m / {heightNote}" +
                      (roadTiles > 0 ? $" / 도로 타일 {roadTiles}장" : string.Empty) +
                      (crosswalks > 0 ? $" / 횡단보도 {crosswalks}개" : string.Empty) +
                      (plazaFloorTiles > 0 ? $" / 광장 바닥 {plazaFloorTiles}장" : string.Empty) +
                      (plazaProps > 0 ? $" / 광장 소품 {plazaProps}개" : string.Empty) +
                      (props > 0 ? $" / 옥상 소품 {props}개" : string.Empty) +
                      (rails > 0 ? $" / 옥상 난간 {rails}개" : string.Empty) +
                      (settings.generateBoundaryWall
                          ? $" / 경계벽 {settings.BoundaryWallHeight:F0}m"
                          : string.Empty), this);
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
            int anchorMask = layer >= 0 ? (1 << layer) : ~0;
            int grabbableLayer = ResolveGrabbableLayer();

            int lowCount = Mathf.Min(settings.roofPropCountMin, settings.roofPropCountMax);
            int highCount = Mathf.Max(settings.roofPropCountMin, settings.roofPropCountMax);
            int total = 0;
            int missed = 0;

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

                    if (!TryFindRoofSurface(building, local, anchorMask, out Vector3 spot))
                    {
                        missed++;
                        continue;
                    }

                    var prop = InstantiateLinked(spec.prefab);
                    prop.transform.SetParent(root.transform, false);
                    prop.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    // 소품 자체의 밑면이 옥상에 닿게 내려준다.
                    prop.transform.position = spot - Vector3.up * spec.bounds.min.y;
                    if (settings.propColliders) AddMeshColliders(prop);
                    // Anchorable이 아니라 Grabbable로 둔다 — 물리로 움직이는 프롭이라
                    // 스윙 갈고리가 걸리면 안 된다. 레이어 지정이 AddPhysicsToProp보다
                    // 먼저인 이유는 없다 — 부착 순서 자체는 무관하다.
                    if (grabbableLayer >= 0) SetLayerRecursive(prop, grabbableLayer);
                    AddPhysicsToProp(prop);
                    if (settings.propLodCulling) AddLodCulling(prop, settings.propLodCullHeight);
                    total++;
                }
            }

            if (missed > 0)
                Debug.LogWarning($"[CityGenerator] 옥상 소품 {missed}개는 표면을 못 찾아 건너뛰었다. " +
                                  "RoofProbe.Margin이 부족하거나 건물에 콜라이더가 없을 수 있다.", this);
            return total;
        }

        /// <summary>
        /// 계산한 높이 위에서 아래로 쏴 <b>실제 밟을 수 있는</b> 표면을 찾는다.
        ///
        /// <see cref="RoofProbe"/>와 같은 규칙을 쓴다. BoxCollider는 근사 박스라 제외하고,
        /// 메시 히트 중 윗면(법선↑) 가운데 가장 낮은 점을 고른다 — 돌출물 꼭대기가 아니라
        /// 옥상 바닥에 플레이어·난간·소품을 맞추기 위해서다.
        /// </summary>
        private bool TryFindRoofSurface(CityBuilding building, Vector2 local, int anchorMask, out Vector3 spot)
        {
            Vector3 origin = building.transform.TransformPoint(
                new Vector3(local.x, building.height + RoofProbe.Margin, local.y));

            return RoofProbe.TryFindWalkableSurface(
                origin, RoofProbe.Margin * 2f, building.transform, anchorMask, out spot);
        }

        /// <summary>
        /// 옥상 둘레를 난간으로 두른다.
        ///
        /// 옥상 소품과 별도 함수인 이유는 배치를 정하는 주체가 다르기 때문이다. 소품은
        /// "몇 개를 흩을까"라서 개수가 설정값이지만, 난간은 <b>둘레를 채우는 것</b>이라
        /// 개수가 옥상 크기에서 나온다 — 사람이 정할 값이 아니다. 방향도 마찬가지로
        /// 무작위가 아니라 변을 따라야 한다.
        ///
        /// 스윙 고도에서 옥상은 면이 아니라 <b>윤곽선</b>으로 읽힌다. 그래서 실외기를
        /// 안쪽에 아무리 흩어도 실루엣이 안 생기고, 가장자리를 한 줄 두르는 쪽이 크게 이긴다.
        /// </summary>
        private int CreateRoofRails(Transform parent, List<CityBuilding> buildings)
        {
            if (settings.roofRailPrefab == null || buildings.Count == 0) return 0;
            if (!TryMeasurePrefab(settings.roofRailPrefab, parent, out Bounds rail)) return 0;

            float pieceLength = Mathf.Max(rail.size.x, rail.size.z);
            if (pieceLength < 0.1f) return 0;

            // 조각의 긴 축이 어느 쪽인지에 따라 변에 맞추는 회전이 90° 달라진다.
            bool longAlongX = rail.size.x >= rail.size.z;

            var root = new GameObject("_RoofRails");
            root.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer(AnchorLayerName);
            int anchorMask = layer >= 0 ? (1 << layer) : ~0;
            int total = 0;
            int missed = 0;

            foreach (var building in buildings)
            {
                float hx = building.footprint.x * 0.5f - settings.roofRailInset;
                float hz = building.footprint.y * 0.5f - settings.roofRailInset;
                if (hx <= 0f || hz <= 0f) continue;

                var corners = new[]
                {
                    new Vector2(-hx, +hz), new Vector2(+hx, +hz),
                    new Vector2(+hx, -hz), new Vector2(-hx, -hz),
                };

                for (int i = 0; i < 4; i++)
                {
                    total += PlaceRailRun(root.transform, building, rail, pieceLength, longAlongX,
                                          layer, anchorMask, corners[i], corners[(i + 1) % 4], ref missed);
                }
            }

            if (missed > 0)
                Debug.LogWarning($"[CityGenerator] 옥상 난간 {missed}칸은 표면을 못 찾아 건너뛰었다.", this);
            return total;
        }

        /// <summary>옥상 한 변을 난간 조각으로 채운다. 좌표는 건물 로컬 XZ.</summary>
        private int PlaceRailRun(Transform parent, CityBuilding building, Bounds rail,
                                 float pieceLength, bool longAlongX, int layer, int anchorMask,
                                 Vector2 from, Vector2 to, ref int missed)
        {
            Vector2 span = to - from;
            float run = span.magnitude;
            if (run < 0.01f) return 0;

            // 조각 길이로 나눠 몇 개인지 스스로 정하고, 남는 만큼은 간격을 늘려 메운다.
            // 변 길이가 조각 길이의 배수가 아닌 것이 보통이라, 개수를 맞추는 대신
            // 간격을 늘리는 쪽이 틈이 한 군데로 몰리지 않는다.
            int count = Mathf.Max(1, Mathf.RoundToInt(run / pieceLength));
            Vector2 step = span / count;

            // +Z를 0°로 보는 각. 변 방향으로 조각의 앞을 돌린 뒤, 긴 축이 X면 90°를 더한다.
            float edgeYaw = Mathf.Atan2(span.x, span.y) * Mathf.Rad2Deg;
            float align = longAlongX ? 90f : 0f;
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 local = from + step * (i + 0.5f);

                if (!TryFindRoofSurface(building, local, anchorMask, out Vector3 spot))
                {
                    missed++;
                    continue;
                }

                var piece = InstantiateLinked(settings.roofRailPrefab);
                piece.transform.SetParent(parent, false);
                piece.transform.rotation =
                    building.transform.rotation * Quaternion.Euler(0f, edgeYaw + align, 0f);
                // 조각 밑면을 옥상에 앉힌다. 피벗이 바닥이 아닌 것이 섞여 있어 실측으로 보정한다.
                piece.transform.position = spot - Vector3.up * rail.min.y;

                if (layer >= 0) SetLayerRecursive(piece, layer);
                if (settings.propColliders) AddMeshColliders(piece);
                placed++;
            }

            return placed;
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
            int grabbableLayer = ResolveGrabbableLayer();

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
                        if (settings.propColliders) AddMeshColliders(prop);
                        // 바닥 타일(floorTiles)은 그대로 Ground에 남지만, 물리로 움직이는
                        // 프롭은 Grabbable로 — 스윙 갈고리가 걸리면 안 되는 이유는 옥상
                        // 프롭과 같다.
                        if (grabbableLayer >= 0) SetLayerRecursive(prop, grabbableLayer);
                        AddPhysicsToProp(prop);
                        if (settings.propLodCulling) AddLodCulling(prop, settings.propLodCullHeight);
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

            // 대로용 프리팹이 따로 있으면 그것으로 잰다. 없으면 같은 것을 쓴다 —
            // 그때는 폭이 안 맞아 통로 양옆에 지면이 드러나고, 아래 경고가 그걸 알려준다.
            var avenuePrefab = settings.avenueSegmentPrefab != null
                ? settings.avenueSegmentPrefab
                : settings.roadSegmentPrefab;
            if (!TryMeasurePrefab(avenuePrefab, parent, out Bounds avenue)) avenue = segment;
            roadRecess = Mathf.Max(roadRecess, -avenue.min.y);

            float segmentLength = Mathf.Min(segment.size.x, segment.size.z);
            float avenueLength = Mathf.Min(avenue.size.x, avenue.size.z);
            // 폭이 어느 축에 있는지에 따라 통로에 맞추는 회전이 달라진다.
            bool crossAlongZ = segment.size.z >= segment.size.x;
            bool avenueCrossAlongZ = avenue.size.z >= avenue.size.x;

            WarnRoadWidth(segment, settings.streetWidthX, nameof(settings.streetWidthX),
                          settings.roadSegmentPrefab);
            WarnRoadWidth(avenue, settings.streetWidthZ, nameof(settings.streetWidthZ), avenuePrefab);

            int tilesAlongZ = Mathf.Max(1, Mathf.RoundToInt(settings.footprintZ / segmentLength));
            int tilesAlongX = Mathf.Max(1, Mathf.RoundToInt(settings.footprintX / avenueLength));
            WarnIfNotDivisible(settings.footprintZ, segmentLength);
            WarnIfNotDivisible(settings.footprintX, avenueLength);

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
                        float tx = cx - settings.footprintX * 0.5f + avenueLength * (k + 0.5f);
                        PlaceTile(avenuePrefab, avenue, new Vector2(tx, cz),
                                  avenueCrossAlongZ ? 0f : 90f, roads.transform, layer);
                        placed++;
                    }
                }
            }

            placed += CreateIntersections(roads.transform, originX, originZ, layer, ref roadRecess);
            crosswalks = CreateCrosswalks(roads.transform, originX, originZ, layer);
            return placed;
        }

        /// <summary>도로 타일 폭이 그 통로의 설정 폭과 맞는지.</summary>
        private void WarnRoadWidth(Bounds tile, float corridorWidth, string fieldName, GameObject prefab)
        {
            float crossSection = Mathf.Max(tile.size.x, tile.size.z);
            if (Mathf.Abs(crossSection - corridorWidth) <= 0.1f) return;

            Debug.LogWarning(
                $"[CityGenerator] '{prefab.name}' 폭이 {crossSection:F2}m인데 {fieldName}는 " +
                $"{corridorWidth:F2}m다. 통로와 어긋나므로 {fieldName}를 {crossSection:F2}로 " +
                "맞추거나, 그 폭에 맞는 도로 프리팹을 쓸 것.", this);
        }

        /// <summary>
        /// 교차로마다 네 방향 진입부에 횡단보도를 깐다.
        ///
        /// 애비뉴(세로, Z축 방향 도로)의 남·북 진입부는 도로 세그먼트와 같은 방향
        /// 규칙을 쓰고, 스트리트(가로, X축 방향 도로)의 동·서 진입부는 반대로 쓴다.
        /// 데칼이 도로 표면과 같은 y=0에 놓이면 서로 겹쳐 z-fighting이 나므로 살짝 띄운다.
        ///
        /// 자리는 교차로 사각형 <b>바깥</b>, 진입 차선 위다. 실제 교차로가 그런 모양이기도 하고,
        /// 안쪽에 넣으면 반드시 겹치기 때문이기도 하다 — 아래 <c>half</c> 참고.
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
            float narrowest = Mathf.Min(settings.streetWidthX, settings.streetWidthZ);
            if (crossSection > narrowest + 0.1f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 횡단보도 폭이 {crossSection:F2}m인데 좁은 쪽 도로는 " +
                    $"{narrowest:F2}m다. 그 도로에서는 인도 쪽으로 넘친다.", this);
            }

            // 교차로 사각형 바깥에 붙인다. 안쪽에 넣으면 네 장이 고리처럼 이어지면서 모서리마다
            // 서로 물리는데, 그건 값을 조절해서 피할 수 있는 종류가 아니다. 네 장이 안 겹치려면
            // 도로 폭 ≥ 가로지르는 길이 + 두께 × 2 여야 하고, 이 키트는 5.4 + 4.5×2 = 14.5m라
            // 12m 도로에 애초에 자리가 없다. 밖으로 빼면 남북 한 쌍과 동서 한 쌍이 아예
            // 다른 통로에 놓이므로 폭과 무관하게 안 겹친다.
            // 축마다 빠져나가는 폭이 다르다. 남·북으로 뺄 때는 교차로의 Z 변을,
            // 서·동으로 뺄 때는 X 변을 넘어야 한다.
            float halfZ = settings.streetWidthZ * 0.5f + thickness * 0.5f;
            float halfX = settings.streetWidthX * 0.5f + thickness * 0.5f;

            // 밖으로 뺀 대신, 이번엔 이웃 교차로에서 마주 보고 오는 장과 통로 한가운데서
            // 만날 수 있다. 통로 길이는 블록 한 변이므로 두께 두 장이 거기 들어가야 한다.
            if (thickness * 2f > settings.MinFootprint)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 횡단보도 두께 {thickness:F2}m가 블록 한 변 " +
                    $"{settings.MinFootprint:F1}m의 절반을 넘는다. 이웃 교차로에서 온 것과 " +
                    "도로 중간에서 맞물린다.", this);
            }

            int placed = 0;
            for (int x = 0; x < settings.blockCountX - 1; x++)
            {
                for (int z = 0; z < settings.blockCountZ - 1; z++)
                {
                    float cx = originX + x * settings.PitchX + settings.PitchX * 0.5f;
                    float cz = originZ + z * settings.PitchZ + settings.PitchZ * 0.5f;

                    // 이면도로(Z 방향) 진입부 — 남 · 북
                    PlaceAtIntersection(new Vector2(cx, cz - halfZ), crossAlongZ ? 90f : 0f);
                    PlaceAtIntersection(new Vector2(cx, cz + halfZ), crossAlongZ ? 90f : 0f);
                    // 대로(X 방향) 진입부 — 서 · 동
                    PlaceAtIntersection(new Vector2(cx - halfX, cz), crossAlongZ ? 0f : 90f);
                    PlaceAtIntersection(new Vector2(cx + halfX, cz), crossAlongZ ? 0f : 90f);
                    placed += 4;
                }
            }
            return placed;

            void PlaceAtIntersection(Vector2 pos, float yaw) =>
                PlaceTile(settings.crosswalkPrefab, decal, pos, yaw, parent, layer,
                          SurfaceEpsilon, collide: false);
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
            float narrowest = Mathf.Min(settings.streetWidthX, settings.streetWidthZ);
            if (tileSize > narrowest + 0.1f)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 교차로 타일이 {tileSize:F2}m인데 좁은 쪽 통로는 {narrowest:F2}m다. " +
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
                    // 두 통로의 폭이 다르므로 교차로 자리도 정사각형이 아니다.
                    // TileArea는 원래부터 축마다 다른 길이를 받으므로 값만 나눠 넘기면 된다.
                    placed += TileArea(settings.intersectionPrefab, cross, new Vector2(cx, cz),
                                       settings.streetWidthX, settings.streetWidthZ, parent, layer);
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
        /// <param name="collide">
        /// 밟을 수 있는 면인지. 도로·교차로·광장 바닥은 밟는 면이고, 그 위에 얹는 데칼은
        /// 아니다. 데칼에까지 충돌체를 붙이면 아스팔트 위에 1cm 턱이 하나 생길 뿐이다.
        /// </param>
        private void PlaceTile(GameObject prefab, Bounds bounds, Vector2 target, float yaw,
                               Transform parent, int layer, float yOffset = 0f, bool collide = true)
        {
            var tile = InstantiateLinked(prefab);
            tile.transform.SetParent(parent, false);

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            tile.transform.localRotation = rotation;

            Vector3 pivotOffset = rotation * new Vector3(bounds.center.x, 0f, bounds.center.z);
            tile.transform.localPosition = new Vector3(
                target.x - pivotOffset.x, yOffset, target.y - pivotOffset.z);

            if (layer >= 0) SetLayerRecursive(tile, layer);
            if (collide && settings.surfaceColliders) AddMeshColliders(tile);
            // 추가 생성 (2026-10-23) — 평평한 바닥·데칼은 그림자를 드리우지 않는다(렌더 최적화, groundCastShadows 설명 참고).
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

        /// <summary>
        /// 생긴 그대로 충돌체를 붙인다. 바닥 타일과 소품이 같은 함수를 쓴다 —
        /// 이 키트 FBX가 전부 충돌체 없이 임포트되어 있다는 같은 사정에서 나온 문제다.
        ///
        /// 바운즈 박스로 감싸지 않는 이유가 양쪽에 하나씩 있다.
        /// 바닥에서는 <c>Street_2Lane</c> 한 장에 인도(y=0)와 아스팔트(y=-0.15)가 같이
        /// 들어 있어, 박스로 감싸면 윗면이 0으로 평평해져 아스팔트 위를 15cm 떠서 걷게 된다.
        /// 소품에서는 화분처럼 <b>속이 빈</b> 것이 통째로 막힌 덩어리가 되어, 난간을 뛰어넘어
        /// 안에 들어서는 것이 불가능해진다. 실제 형상대로 두면 빈 곳은 빈 곳으로 남는다.
        ///
        /// 개수가 천 단위지만 물리 메시는 <b>메시 종류당 한 번만</b> 구워져 인스턴스끼리
        /// 공유된다. 도로 타일 1044장이 도로 메시 하나 값이다.
        ///
        /// 대신 오목한 메시라 움직이는 리지드바디에는 붙일 수 없다. 나중에 소품을 집어
        /// 던지게 만들 때, 그 소품의 충돌체만 <c>convex</c>를 켜야 한다.
        ///
        /// 에디터에서 생성해 씬에 저장하는 것을 전제로 한다. 재생 중에 생성하면 원본 메시의
        /// Read/Write가 꺼져 있는 경우 충돌체를 못 만든다.
        /// </summary>
        /// <remarks>프리팹이 이미 충돌체를 들고 있으면 손대지 않는다. 저자가 정해둔 것이 우선이다.</remarks>
        private static void AddMeshColliders(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;

            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                mc.convex = true;
            }
        }

        /// <summary>
        /// 소품에 물리 강체(Rigidbody) 및 전투 피드백 컴포넌트(CombatProp)를 자동으로 주입한다.
        ///
        /// <b>isStatic을 여기서 끈다.</b> 이 함수가 끝난 뒤 <c>Generate()</c>가
        /// <c>MarkStaticRecursive</c>로 도시 전체를 다시 Static 표시하므로, 여기서 꺼도
        /// 소용없어 보일 수 있지만 <see cref="MarkStaticRecursive"/> 쪽도 Rigidbody가 있는
        /// 오브젝트는 건너뛰도록 같이 고쳐뒀다 — 두 코드가 서로 다른 것에 의존하면 나중에
        /// 한쪽만 고치는 사고가 나므로, 신호(Rigidbody 존재 여부) 하나로 두 곳 다 판단한다.
        /// </summary>
        private static void AddPhysicsToProp(GameObject prop)
        {
            if (prop == null) return;

            if (prop.GetComponentInChildren<Rigidbody>() == null)
            {
                var rb = prop.AddComponent<Rigidbody>();
                rb.mass = 30f; // 기본 프롭 질량 (30kg)
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                // <b>Discrete로 낳는다.</b> 예전에는 여기서 Continuous를 박았는데, 터널링을
                // 막아야 하는 것은 던져서 날아가는 그 한 번뿐인데도 옥상에 가만히 놓인
                // 수백 개까지 매 물리 스텝 스윕 판정을 받고 있었다(물리가 200Hz라 초당
                // 수만 번). 날아가는 동안만 올리는 일은 CombatProp이 맡는다 —
                // <see cref="CombatProp.BeginThrownFlight"/>.
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            if (prop.GetComponent<CombatProp>() == null)
            {
                prop.AddComponent<CombatProp>();
            }

            // 정적 배칭 대상이던 오브젝트에 물리를 얹은 것이므로 Static 표시를 뗀다.
            // 안 떼면 렌더러가 정적 배치로 구워진 위치에 그대로 남아, 실제 물리 위치와
            // 화면에 보이는 위치가 어긋난다 — 가만히 있어야 할 것끼리 미세하게 계속
            // 파고들며 부딪히는 것처럼 보이던 증상이 이것이었다.
            prop.isStatic = false;
        }

        /// <summary>
        /// 소품에 <see cref="LODGroup"/>을 붙여, 화면에서 차지하는 비중이
        /// <paramref name="cullHeight"/> 아래로 떨어지면 렌더링·그림자 캐스팅을 통째로
        /// 끈다. LOD 단계를 하나만(<c>LOD0</c>) 등록하면 유니티가 그 아래는 자동으로
        /// "컬링"으로 처리한다 — 저폴리 대체 메시가 없어도 되는 이유다.
        ///
        /// 이 키트 소품엔 단계별 저폴리 메시가 따로 없어 진짜 LOD(멀어질수록 폴리곤이
        /// 줄어드는 것)는 못 만든다. 대신 "충분히 작아지면 아예 안 그린다"만으로도
        /// 도시처럼 소품이 수백~수천 개인 씬에서는 드로우콜·섀도우 캐스터 수가 크게
        /// 줄어든다 — 특히 그림자는 컬링된 오브젝트에서 아예 안 나가니, Stats 패널의
        /// Shadow Casters 수가 이 함수 하나로 직접 줄어드는 걸 확인할 수 있다.
        /// </summary>
        private static void AddLodCulling(GameObject prop, float cullHeight)
        {
            if (prop == null) return;

            var renderers = prop.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var lodGroup = prop.GetComponent<LODGroup>();
            if (lodGroup == null) lodGroup = prop.AddComponent<LODGroup>();

            lodGroup.SetLODs(new[] { new LOD(cullHeight, renderers) });
            lodGroup.RecalculateBounds();
        }

        /// <summary>
        /// <see cref="GrabbableLayerName"/> 레이어 인덱스. 레이어가 아직 안 만들어져 있으면
        /// -1을 돌려주고, 호출부는 그 경우 레이어 지정을 건너뛴다 — 없는 레이어를 억지로
        /// 쓰려 하면 조용히 Default로 떨어져 버그를 숨긴다.
        /// </summary>
        private static int ResolveGrabbableLayer()
        {
            int layer = LayerMask.NameToLayer(GrabbableLayerName);
            if (layer < 0)
                Debug.LogWarning($"[CityGenerator] '{GrabbableLayerName}' 레이어가 없다. " +
                                 "프롭이 원래 레이어에 그대로 남아, 스윙 갈고리가 걸릴 수 있다.");
            return layer;
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

        /// <summary>
        /// 도시를 둘러싸는 벽.
        ///
        /// 하는 일은 가두는 것이 아니라 <b>가리는 것이다.</b> 지면 박스는 어디선가 끝나고,
        /// 그 끝선과 너머의 허공이 눈에 들어오는 순간 도시는 바닥에 얹힌 모형이 된다.
        /// 그래서 벽은 지면 박스 가장자리에 딱 맞춰 세운다 — 가려야 할 선이 정확히 거기다.
        ///
        /// 레이어는 지면이 아니라 <b>앵커다.</b> 즉 웹이 붙는다. 격자 밖까지 날아간 플레이어에게
        /// 벽은 붙잡을 것이 되고, 벽을 차고 되돌아오는 선택지가 생긴다. 지면과 달리 벽은
        /// 수직면이라 여기 붙어도 스윙이 지면 견인으로 무너지지 않는다.
        /// </summary>
        /// <param name="roadRecess">차도가 y=0보다 파여 있는 깊이. 지면 윗면을 찾는 데 쓴다.</param>
        private void CreateBoundaryWall(Transform parent, float roadRecess)
        {
            var span = settings.CitySpan;
            float sizeX = span.x + settings.groundMargin * 2f;
            float sizeZ = span.y + settings.groundMargin * 2f;
            float thickness = settings.boundaryWallThickness;
            float height = settings.BoundaryWallHeight;

            if (height < settings.MaxHeight)
            {
                Debug.LogWarning(
                    $"[CityGenerator] 경계벽 {height:F0}m가 최고 건물 {settings.MaxHeight:F0}m보다 " +
                    "낮다. 옥상에 올라서면 벽 너머가 보인다.", this);
            }

            var walls = new GameObject("_BoundaryWall");
            walls.transform.SetParent(parent, false);

            int layer = LayerMask.NameToLayer(AnchorLayerName);
            var material = settings.boundaryWallMaterial != null
                ? settings.boundaryWallMaterial
                : settings.groundMaterial;

            float halfX = sizeX * 0.5f;
            float halfZ = sizeZ * 0.5f;
            float baseY = -roadRecess - SurfaceEpsilon;     // 지면 윗면
            float centerY = baseY + height * 0.5f;

            // 옆벽은 앞뒤벽 두께만큼 짧게 잡는다. 그러지 않으면 네 모서리에서 두 상자의 면이
            // 정확히 겹쳐 z-fighting이 난다.
            float sideSpan = Mathf.Max(0.1f, sizeZ - thickness * 2f);

            Slab("North", new Vector3(0f, centerY, halfZ - thickness * 0.5f),
                 new Vector3(sizeX, height, thickness));
            Slab("South", new Vector3(0f, centerY, -halfZ + thickness * 0.5f),
                 new Vector3(sizeX, height, thickness));
            Slab("East", new Vector3(halfX - thickness * 0.5f, centerY, 0f),
                 new Vector3(thickness, height, sideSpan));
            Slab("West", new Vector3(-halfX + thickness * 0.5f, centerY, 0f),
                 new Vector3(thickness, height, sideSpan));

            void Slab(string side, Vector3 center, Vector3 size)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Wall_{side}";
                slab.transform.SetParent(walls.transform, false);
                slab.transform.localScale = size;
                slab.transform.localPosition = center;

                if (material != null)
                    slab.GetComponent<MeshRenderer>().sharedMaterial = material;
                if (layer >= 0) slab.layer = layer;
            }
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            var root = GeneratedRoot;
            if (root == null) return;
            DestroyObject(root.gameObject);
        }

        /// <summary>
        /// 실제 건물 프리팹으로 세운 타워 한 채.
        ///
        /// 프리팹 링크를 유지해서 인스턴스화한다. 36채를 놓은 뒤 프리팹 하나를 고치면
        /// 전부 따라오게 하려는 것이다. 링크가 없으면 콜라이더 하나 수정에 36번을 손대야 한다.
        ///
        /// <b>여러 겹을 쌓아도 역산 사슬은 한 글자도 안 바뀐다.</b> 높이 조건(<c>H ≥ 로프 + 여유</c>)과
        /// 간격 조건(<c>피치 ≤ 2·L·sinθ</c>)은 서로 독립이라, 겹을 올리면 두 번째 식을 건드리지 않고
        /// 첫 번째 식의 여유만 커진다. 로프 22m·간격 36m를 그대로 두고 아치의 고도만 올리는 것이
        /// 이 함수가 하는 일의 전부다.
        /// </summary>
        private GameObject CreatePrefabBuilding(int x, int z, int targetFloors)
        {
            int tiers = settings.TowerTiersAt(x, z);

            // 겹을 담을 빈 루트를 따로 둔다. 맨 아래 겹을 루트로 쓰면 그 겹의 실측값
            // (28m·9층)이 타워 전체의 값 행세를 하게 되고, 앵커와 옥상 소품이 중간 높이에 걸린다.
            var tower = new GameObject("Tower");
            var meta = tower.AddComponent<CityBuilding>();

            float cursor = 0f;
            int floors = 0;
            var topFootprint = Vector2.zero;
            int stacked = 0;
            GameObject topInstance = null;

            for (int tier = 0; tier < tiers; tier++)
            {
                var prefab = settings.TierPrefab(x, z, tier, targetFloors);
                if (prefab == null) break;

                var tierMeta = CityLayoutSettings.MetaOf(prefab);
                if (tierMeta == null)
                {
                    Debug.LogError($"[CityGenerator] '{prefab.name}'에 CityBuilding이 없다. " +
                                   "실측 치수를 모르므로 배치할 수 없다. BuildingPrefabTool로 다시 만들 것.", prefab);
                    break;
                }

                var instance = InstantiateLinked(prefab);
                instance.name = $"Tier{tier}_{prefab.name}";
                instance.transform.SetParent(tower.transform, false);

                int steps = settings.TierRotationSteps(x, z, tier);
                instance.transform.localRotation = Quaternion.Euler(0f, steps * 90f, 0f);

                // 실측 높이가 렌더러 바운즈 전체라 옥상 구조물까지 포함된 값이다.
                // 그래서 바로 이어 붙이면 위 겹이 아래 겹의 가장 높은 점 위에 앉는다.
                instance.transform.localPosition = new Vector3(0f, cursor, 0f);

                // 겹이 들고 온 CityBuilding을 떼어낸다. 남겨두면 도시를 GetComponentsInChildren로
                // 훑는 쪽(기즈모)이 한 채를 여러 채로 세고, 무엇보다 앵커가 맞은 콜라이더에서
                // GetComponentInParent로 건물을 찾는 순간 타워가 아니라 그 겹이 잡힌다.
                StripComponent(instance.GetComponent<CityBuilding>());

                cursor += tierMeta.height;
                floors += tierMeta.floorCount;

                // 옥상은 맨 위 겹의 것이다. 90°·270°로 돌아간 겹은 바닥의 두 축이 바뀌므로
                // 타워 로컬 기준으로 되돌려 기록한다 — 안 그러면 소품이 옥상 밖으로 떨어진다.
                topFootprint = steps % 2 == 0
                    ? tierMeta.footprint
                    : new Vector2(tierMeta.footprint.y, tierMeta.footprint.x);
                stacked++;
                topInstance = instance;
            }

            if (stacked == 0)
            {
                DestroyObject(tower);
                return null;
            }

            meta.height = cursor;
            meta.floorCount = floors;
            meta.footprint = topFootprint;
            meta.isShell = true;

            // 겹마다 따로(혼자 놓인 채로) 잰 높이를 그냥 더하기만 했다. 각 겹의 측정 자체는
            // 정확해도, 여러 겹을 쌓았을 때 맨 위 실제로 밟히는 표면이 그 합과 같다는
            // 보장은 없다 — 특히 맨 위 겹의 BoxCollider는 그 겹 <b>혼자</b> 있다고 가정하고
            // 구운 값이라, 플레이어가 실제로 서는 물리 표면과 어긋날 수 있다. 여기서
            // 조립이 끝난 실물에 대고 다시 재서 바로잡는다 — 추정을 쌓지 않고 마지막에
            // 한 번 실측으로 확정한다.
            if (tiers > 1 && topInstance != null)
            {
                int probeMask = LayerMask.GetMask(AnchorLayerName);
                if (probeMask == 0) probeMask = ~0;

                // 겹수가 많을수록 겹마다 붙는 작은 오차도 더 쌓일 수 있으므로 마진을
                // 겹수에 비례해 넉넉히 둔다.
                float verifyMargin = RoofProbe.Margin * (1 + tiers);
                Vector3 origin = tower.transform.TransformPoint(new Vector3(0f, cursor + verifyMargin, 0f));

                if (RoofProbe.TryFindWalkableSurface(origin, verifyMargin * 2f, tower.transform,
                                                     probeMask, out Vector3 verified))
                {
                    float verifiedHeight = verified.y; // 타워가 원점 기준이라 로컬 y = 월드 y
                    var topBox = topInstance.GetComponent<BoxCollider>();

                    if (topBox != null && Mathf.Abs(verifiedHeight - cursor) > 0.05f)
                    {
                        // 박스는 "그 겹의 로컬 바닥(0)부터 자기 높이만큼"으로 구워져 있었다.
                        // 검증된 높이는 타워 전체 기준이므로, 그 겹의 로컬 바닥
                        // (= cursor - 그 겹 원래 높이)을 빼서 그 겹 로컬 좌표로 되돌린다.
                        float tierFloor = cursor - topBox.size.y;
                        float tierLocalHeight = Mathf.Max(0.1f, verifiedHeight - tierFloor);
                        topBox.size = new Vector3(topBox.size.x, tierLocalHeight, topBox.size.z);
                        topBox.center = new Vector3(topBox.center.x, tierLocalHeight * 0.5f, topBox.center.z);
                    }

                    meta.height = verifiedHeight;
                }
            }

            return tower;
        }

        /// <summary>
        /// 프리팹 인스턴스에서 컴포넌트 하나를 떼어낸다.
        /// 에디터에서는 프리팹 오버라이드로 기록되므로 원본 프리팹은 그대로다.
        /// </summary>
        private static void StripComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Destroy(component);
            else DestroyImmediate(component);
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
        ///
        /// <b>단, Rigidbody가 있는 오브젝트는 건너뛴다.</b> 이 함수는 프롭 배치보다
        /// <b>나중에</b> 도시 전체에 대고 돌기 때문에, 걸러내지 않으면 <c>AddPhysicsToProp</c>이
        /// 방금 꺼둔 <c>isStatic</c>을 여기서 다시 켜버린다. 정적 배칭으로 구워진 오브젝트에
        /// 물리를 걸면 렌더러 위치와 실제 물리 위치가 어긋나는데, 실제로 그 증상이
        /// 콘솔에서 "가만히 있는 프롭끼리 계속 부딪힌다"는 로그로 나타났었다.
        /// </summary>
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
