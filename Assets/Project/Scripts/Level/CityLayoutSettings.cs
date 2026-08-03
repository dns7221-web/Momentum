using System.Collections.Generic;
using UnityEngine;

namespace Momentum.Level
{
    /// <summary>건물 높이를 어떻게 나눠줄지.</summary>
    public enum HeightMode
    {
        /// <summary>전부 같은 높이. 측정 단계의 기본값 — 변수를 하나로 묶어야 결과가 읽힌다.</summary>
        Uniform,
        /// <summary>최소~최대 사이 무작위. 시드로 재현된다.</summary>
        Random,
        /// <summary>중앙이 높고 외곽이 낮은 분포. 실제 도시의 스카이라인.</summary>
        Ridge,
    }

    /// <summary>옥상 소품을 어디에 놓을지.</summary>
    public enum RoofPropPlacement
    {
        /// <summary>옥상 안쪽에 무작위로 흩는다.</summary>
        Scatter,
        /// <summary>옥상 가장자리를 따라 돌린다. 멀리서 실루엣으로 읽힌다.</summary>
        Perimeter,
    }

    /// <summary>실제 건물 프리팹이 여러 개일 때 어느 것을 놓을지.</summary>
    public enum PrefabPickMode
    {
        /// <summary>목표 층수에 가장 가까운 것. 스카이라인 의도를 지키므로 측정 단계에 맞다.</summary>
        ClosestHeight,
        /// <summary>격자 좌표로 결정되는 무작위. 도시가 단조로워 보이지 않게 하려는 것.</summary>
        Random,
    }

    /// <summary>
    /// 회색 박스 도시 한 세트의 파라미터.
    ///
    /// 인스펙터에 값을 직접 두지 않고 에셋으로 뺀 이유는 실험 기록을 남기기 위해서다.
    /// 이 프로젝트의 산출물은 "손맛 나는 스윙"이 아니라 "어떤 수치에서 손맛이 났는가"이고,
    /// 그 답은 이 에셋 파일들로 남는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Momentum/City Layout Settings", fileName = "Layout_New")]
    public class CityLayoutSettings : ScriptableObject
    {
        /// <summary>에셋의 층고. Downtown City MegaKit의 벽 패널이 3m다.</summary>
        public const float FloorHeight = 3f;

        /// <summary>에셋의 가로 모듈 폭. 벽 패널이 2m 단위다.</summary>
        public const float ModuleWidth = 2f;

        [Header("격자")]
        [Min(1)] public int blockCountX = 6;
        [Min(1)] public int blockCountZ = 6;

        [Header("건물 바닥 (ModuleWidth 배수로 스냅)")]
        [Min(ModuleWidth)] public float footprintX = 20f;
        [Min(ModuleWidth)] public float footprintZ = 20f;

        [Header("통로 폭 — 앵커 간격이자, 진자가 흔들릴 수 있는 폭이다")]
        [Tooltip("<b>X축으로 잰</b> 통로 폭. 이 통로는 Z 방향으로 뻗는다 (이면도로).\n\n" +
                 "PitchX = footprintX + 이 값.")]
        [Min(1f)] public float streetWidthX = 12f;

        [Tooltip("<b>Z축으로 잰</b> 통로 폭. 이 통로는 X 방향으로 뻗는다 (대로).\n\n" +
                 "여기를 넓히면 X 방향이 스윙 대로가 된다. 두 축을 같은 폭으로 두면 " +
                 "사방이 똑같이 좁아 어느 쪽으로도 진자를 크게 흔들 수 없다 — 맨해튼이 " +
                 "애비뉴와 스트리트를 다른 폭으로 두는 이유와 같다.\n\n" +
                 "PitchZ = footprintZ + 이 값.")]
        [Min(1f)] public float streetWidthZ = 18f;

        [Header("높이 (FloorHeight 배수로 스냅)")]
        public HeightMode heightMode = HeightMode.Uniform;
        [Min(1)] public int floorCount = 12;
        [Min(1)] public int floorCountMin = 6;
        [Min(1)] public int floorCountMax = 20;
        public int randomSeed = 20260730;

        [Header("실제 건물 프리팹 — 비어 있으면 회색 박스로 만든다")]
        [Tooltip("BuildingPrefabTool로 만든 건물 프리팹들. 각 프리팹은 실측 치수를 담은 " +
                 "CityBuilding 컴포넌트를 들고 있어야 한다.")]
        public List<GameObject> buildingPrefabs = new List<GameObject>();
        public PrefabPickMode prefabPickMode = PrefabPickMode.Random;
        [Tooltip("Y축 90° 단위로 무작위 회전. 완성 건물이 3종뿐이라 같은 얼굴이 반복되는 것을 줄인다.")]
        public bool randomYRotation = true;

        [Header("타워 적층 — 완성 건물을 겹쳐 높이를 번다")]
        [Tooltip("한 채를 몇 겹으로 쌓을지 (Uniform 모드). 1이면 적층 없이 지금까지와 같다.\n\n" +
                 "에셋 최고 건물이 28m라 아치가 지상 6~16m 구간에만 머무는 것을 푸는 값이다. " +
                 "겹을 쌓아도 로프 길이와 앵커 간격은 그대로이므로 역산 사슬은 건드리지 않는다 " +
                 "— 아치의 모양은 같고 고도만 통째로 올라간다.")]
        [Min(1)] public int towerTiers = 1;
        [Tooltip("Random / Ridge 모드에서 쓸 겹 수 범위. Ridge면 중앙이 높고 외곽이 낮아진다.")]
        [Min(1)] public int towerTiersMin = 1;
        [Min(1)] public int towerTiersMax = 3;

        [Tooltip("위로 갈수록 바닥이 좁은 프리팹을 올린다 — 셋백(setback) 실루엣.\n\n" +
                 "끄면 같은 건물을 그대로 반복해 쌓는다. 켜는 쪽이 나은 이유는 미관만이 아니다 " +
                 "— 좁아지면서 생기는 중간 옥상이 그대로 앵커 선반이 되어, 꼭대기 말고도 " +
                 "걸 자리가 생긴다.")]
        public bool towerSetback = true;

        [Tooltip("겹마다 90° 단위로 다르게 돌린다. 같은 건물을 쌓을 때 출입구와 계단이 " +
                 "위아래로 일직선에 놓이면 복사한 티가 그대로 읽히는데, 겹마다 다른 방향을 " +
                 "보게 하면 그 줄이 끊긴다.")]
        public bool rotateTiers = true;

        [Header("칸 안 흔들기 — 격자 선을 끊는다")]
        [Tooltip("건물을 칸 정중앙이 아니라 조금 밀어 놓는다. 1이면 도로를 침범하지 않는 " +
                 "한계까지 민다.\n\n" +
                 "높이와 종류를 아무리 섞어도 <b>모든 건물이 같은 선 위에 서 있으면</b> " +
                 "눈은 그 선을 먼저 읽는다. 격자로 보이는 것의 절반은 그 정렬이다.\n\n" +
                 "다만 미는 만큼 통로가 좁아진다 — 마주 본 두 건물이 서로를 향해 밀리면 " +
                 "스윙 통로가 그 두 배만큼 줄어드므로, 통로 폭을 로프에 맞춰둔 것을 " +
                 "여기서 도로 까먹지 않게 적당히 둘 것.")]
        [Range(0f, 1f)] public float cellJitter = 0.35f;

        [Header("빈 셀 — 격자 규칙성 깨기")]
        [Tooltip("건물을 세우지 않고 비워 광장으로 남길 확률. " +
                 "붙어 있는 두 칸이 동시에 비지는 않는다 — 앵커 간격이 두 배가 되면 스윙이 끊긴다.")]
        [Range(0f, 0.4f)] public float emptyCellChance = 0.12f;

        [Header("옥상 소품")]
        [Tooltip("옥상에 흩어놓을 것. 스윙 고도에서 가장 오래 보이는 면이 옥상이다.")]
        public List<GameObject> roofPropPrefabs = new List<GameObject>();
        public RoofPropPlacement roofPropPlacement = RoofPropPlacement.Scatter;
        [Min(0)] public int roofPropCountMin = 1;
        [Min(0)] public int roofPropCountMax = 4;
        [Tooltip("옥상 가장자리에서 띄울 거리. 난간 밖으로 떨어져 보이지 않게.")]
        [Min(0f)] public float roofPropEdgeMargin = 2f;

        [Header("옥상 난간 — 옥상 둘레를 두른다")]
        [Tooltip("옥상 가장자리를 따라 빈틈없이 두를 조각 (Trim_Wall_Guard — 폭 2m·두께 12cm, " +
                 "키트 모듈 격자에 맞는 파라펫). Stairs_Rails 계열은 깊이가 1~2m라 " +
                 "옥상에선 난간이 아니라 두꺼운 덩어리가 되므로 쓰지 않는다. " +
                 "비우면 두르지 않는다.\n\n" +
                 "옥상 소품과 다른 항목인 이유는 배치 규칙이 다르기 때문이다. 소품은 몇 개를 " +
                 "흩는 것이라 개수를 사람이 정하지만, 난간은 <b>둘레를 채우는 것</b>이라 개수가 " +
                 "옥상 크기에서 나온다. 방향도 무작위가 아니라 변을 따라야 한다.\n\n" +
                 "실외기 100개보다 난간 한 줄이 실루엣에 크게 기여한다 — 멀리서 옥상은 " +
                 "면이 아니라 <b>윤곽선</b>으로 읽히기 때문이다.")]
        public GameObject roofRailPrefab;

        [Tooltip("난간을 옥상 모서리에서 안으로 얼마나 들일지(m). 0이면 모서리에 딱 붙는다.")]
        [Min(0f)] public float roofRailInset = 0.3f;

        [Header("광장 — 빈 칸 꾸미기")]
        [Tooltip("빈 칸을 덮을 바닥 타일. Sidewalk_NoCurb_3m 같은 턱 없는 포장재가 맞다. " +
                 "비우면 빈 칸에 지면이 그대로 노출되어 '빠뜨린 칸'으로 보인다.")]
        public GameObject plazaFloorPrefab;
        [Tooltip("광장에 놓을 것. Sidewalk_Planter · Prop_Planter_Single 등.")]
        public List<GameObject> plazaPropPrefabs = new List<GameObject>();
        [Min(0)] public int plazaPropCountMin = 2;
        [Min(0)] public int plazaPropCountMax = 5;
        [Min(0f)] public float plazaPropEdgeMargin = 3f;

        [Header("충돌체 — 이 키트 FBX에는 충돌체가 없다")]
        [Tooltip("옥상·광장 소품에 생긴 그대로 메시 충돌체를 붙인다. " +
                 "끄면 플레이어가 화분과 실외기를 그대로 통과한다. " +
                 "속이 빈 소품은 빈 채로 남아 난간을 뛰어넘어 안에 들어설 수 있다. " +
                 "프리팹이 이미 충돌체를 들고 있으면 그쪽을 그대로 둔다.")]
        public bool propColliders = true;
        [Tooltip("도로·교차로·광장 바닥에 메시 충돌체를 붙인다. 끄면 밟는 바닥이 지면 박스뿐이라, " +
                 "그보다 높은 인도와 광장 포장재에서 발이 15cm 파묻힌다.")]
        public bool surfaceColliders = true;

        [Header("껍데기 모드 — 터널링 검증용 (회색 박스에만 적용)")]
        [Tooltip("켜면 속 찬 큐브 대신 얇은 벽 4장 + 옥상으로 만든다. " +
                 "속 찬 큐브에서는 터널링이 관측되지 않으므로, 실제 건물로 교체하기 전에 " +
                 "고속 충돌 설정을 검증하려면 이걸 켠다.")]
        public bool shellMode = false;
        [Min(0.05f)] public float wallThickness = 0.2f;

        [Header("스윙 기준값 — 레벨 메트릭 역산의 입력")]
        [Tooltip("로프 길이 L. 진자 주기와 수평 이동 거리가 여기서 나온다.")]
        [Min(1f)] public float ropeLength = 25f;
        [Tooltip("진폭 θ. 수직에서 벗어난 최대 각도.")]
        [Range(5f, 89f)] public float swingAmplitudeDeg = 45f;
        [Tooltip("중력 배율. 1이 지구(9.81). 올리면 같은 로프로 주기가 짧아진다.")]
        [Range(0.5f, 5f)] public float gravityScale = 1f;
        [Tooltip("플레이어가 지면에 스치지 않을 여유. 최소 건물 높이 = 로프 길이 + 이 값.")]
        [Min(0f)] public float groundClearance = 5f;

        [Header("성능")]
        [Tooltip("생성물을 Static으로 표시한다. 도시를 키우면 오브젝트가 수천 개가 되므로 " +
                 "정적 배칭이 없으면 드로우 콜이 그대로 늘어난다. 도시는 움직이지 않으니 켜도 안전하다.")]
        public bool markStatic = true;

        [Header("지면")]
        [Tooltip("추락을 받아줄 지면. 없으면 스윙을 놓쳤을 때 무한히 떨어진다.")]
        public bool generateGround = true;
        [Tooltip("도시 바깥으로 지면을 얼마나 더 넓힐지. 스윙이 격자 밖으로 나가는 것을 감안한다.")]
        [Min(0f)] public float groundMargin = 60f;
        [Tooltip("지면 두께. 얇은 판은 고속 낙하에서 뚫린다.")]
        [Min(1f)] public float groundThickness = 5f;
        public Material groundMaterial;

        [Header("경계벽 — 도시 바깥을 가린다")]
        [Tooltip("지면 박스 가장자리를 따라 벽을 두른다. 없으면 지면이 끝나는 선과 " +
                 "그 너머 허공이 그대로 보인다.")]
        public bool generateBoundaryWall = true;
        [Tooltip("벽 높이. 0이면 가장 높은 건물의 1.5배로 자동 계산한다. " +
                 "건물보다 낮으면 옥상에 올라선 순간 벽 너머가 다시 보인다.")]
        [Min(0f)] public float boundaryWallHeight = 0f;
        [Tooltip("벽 두께. 얇으면 스윙 속도로 부딪힐 때 뚫고 나간다.")]
        [Min(0.5f)] public float boundaryWallThickness = 5f;
        [Tooltip("비우면 지면 머티리얼을 그대로 쓴다.")]
        public Material boundaryWallMaterial;

        [Header("도로 타일 (선택) — 비우면 지면 머티리얼만 보인다")]
        [Tooltip("Z 방향으로 뻗는 이면도로에 깔 구간. 폭이 streetWidthX와 같아야 한다. " +
                 "긴 축·짧은 축은 생성기가 재서 알아낸다.")]
        public GameObject roadSegmentPrefab;

        [Tooltip("X 방향으로 뻗는 대로에 깔 구간. 폭이 streetWidthZ와 같아야 한다 " +
                 "(예: Street_4Lane). 비우면 위 이면도로 프리팹을 그대로 쓰는데, " +
                 "폭이 다르면 통로 양옆에 지면이 드러난다.")]
        public GameObject avenueSegmentPrefab;
        [Tooltip("교차로 자리를 채울 타일. 자리는 streetWidthX × streetWidthZ 직사각형이고 " +
                 "생성기가 몇 장 깔지 스스로 계산하므로, 두 폭을 모두 나누는 작은 타일 " +
                 "(예: Street_Asphalt_6x6)이 맞다. 비우면 교차로 자리에 지면이 그대로 보인다.")]
        public GameObject intersectionPrefab;
        [Tooltip("Decal_Crosswalk 같은 횡단보도 데칼. 교차로마다 4방향에 자동으로 깐다. " +
                 "긴 축이 streetWidth와 맞아야 한다.")]
        public GameObject crosswalkPrefab;

        [Header("외형 (측정에는 영향 없음)")]
        public Material greyboxMaterial;

        // ─── 파생값 ───

        /// <summary>격자 피치 = 바닥 크기 + 도로 폭. 인접 건물 중심 간 거리이자 앵커 간격.</summary>
        public float PitchX => footprintX + streetWidthX;
        public float PitchZ => footprintZ + streetWidthZ;

        /// <summary>
        /// 진자가 실제로 흔들릴 수 있는 폭 — 마주 보는 건물 <b>벽면 사이</b> 거리다.
        ///
        /// 통로 폭(<see cref="streetWidthZ"/>)이 아니라 이 값이 기준인 이유는, 건물이
        /// 격자 칸보다 작아서 칸 안에 여백이 남기 때문이다. 도로가 18m라도 칸이 30m이고
        /// 건물이 20m면 실제로 비어 있는 폭은 28m다.
        ///
        /// 로프 길이와 견줄 값이 이것이다. 이 폭이 로프보다 한참 좁으면 진자를 조금만
        /// 흔들어도 맞은편 벽에 닿는다.
        /// </summary>
        public float SwingCorridorZ => PitchZ - LargestPrefabSpan;
        public float SwingCorridorX => PitchX - LargestPrefabSpan;

        /// <summary>
        /// 프리팹 중 가장 긴 한 변. 건물은 Y축 90° 단위로 돌아가므로 어느 축에 긴 변이
        /// 올지 알 수 없고, 통로 폭을 보수적으로 재려면 긴 쪽을 써야 한다.
        /// </summary>
        public float LargestPrefabSpan
        {
            get
            {
                var footprint = LargestPrefabFootprint;
                float span = Mathf.Max(footprint.x, footprint.y);
                return span > 0f ? span : Mathf.Max(footprintX, footprintZ);
            }
        }

        public float GravityMagnitude => SwingMetrics.StandardGravity * gravityScale;

        /// <summary>가장 짧은 쪽 바닥 크기. 역산 시 보수적으로 이걸 쓴다.</summary>
        public float MinFootprint => Mathf.Min(footprintX, footprintZ);

        /// <summary>
        /// 이 설정에서 실제로 세워질 최고 건물 높이.
        ///
        /// 프리팹을 쓰면 한 겹의 높이는 <b>에셋이 결정한다.</b> 완성 건물이 28m뿐이면
        /// 회색 박스에서 12층(36m)을 적어도 28m가 상한이었다.
        /// <b>적층이 그 상한을 푸는 유일한 경로다</b> — 겹 수는 우리가 정하므로,
        /// 여기서 곱해주지 않으면 경계벽 높이와 역산 경고가 전부 한 겹 기준으로 남는다.
        /// </summary>
        public float MaxHeight
        {
            get
            {
                if (!HasPrefabs)
                    return FloorHeight * (heightMode == HeightMode.Uniform ? floorCount : floorCountMax);

                int tiers = MaxTiers;

                // 셋백은 위로 갈수록 낮은 것이 올라가므로 한 겹 높이를 곱하면 과대평가가 된다.
                // 가장 높은 것부터 순서대로 실제로 더한다 — 가장 높이 서는 타워가 이 조합이다.
                if (towerSetback)
                {
                    var order = SetbackOrder();
                    if (order.Count == 0) return 0f;

                    // 밑칸을 어느 것으로 시작하느냐에 따라 합이 달라진다. 정렬 기준이 넓이라
                    // 가장 넓은 것이 가장 높다는 보장이 없으므로, 시작점을 전부 대보고
                    // 실제로 가장 높이 서는 조합을 택한다. 여기서 낮게 잡으면 경계벽이 짧아진다.
                    float tallestTower = 0f;
                    for (int start = 0; start < order.Count; start++)
                    {
                        float stacked = 0f;
                        for (int i = 0; i < tiers; i++)
                        {
                            var meta = MetaOf(order[Mathf.Min(start + i, order.Count - 1)]);
                            if (meta != null) stacked += meta.height;
                        }
                        tallestTower = Mathf.Max(tallestTower, stacked);
                    }
                    return tallestTower;
                }

                float tallest = 0f;
                foreach (var prefab in buildingPrefabs)
                {
                    var meta = MetaOf(prefab);
                    if (meta != null) tallest = Mathf.Max(tallest, meta.height);
                }
                return tallest * tiers;
            }
        }

        /// <summary>이 설정이 만들 수 있는 최대 겹 수. 높이 분포 모드에 따라 출처가 다르다.</summary>
        public int MaxTiers => heightMode == HeightMode.Uniform
            ? Mathf.Max(1, towerTiers)
            : Mathf.Max(1, Mathf.Max(towerTiersMin, towerTiersMax));

        /// <summary>겹 수 범위. 층수 쪽 <see cref="EffectiveFloorRange"/>와 같은 역할이다.</summary>
        public Vector2Int TowerTierRange => new Vector2Int(
            Mathf.Max(1, Mathf.Min(towerTiersMin, towerTiersMax)),
            Mathf.Max(1, Mathf.Max(towerTiersMin, towerTiersMax)));

        public int TotalBuildings => blockCountX * blockCountZ;

        /// <summary>
        /// 실제로 세울 경계벽 높이.
        ///
        /// 자동값을 건물 높이의 2배로 잡는 이유는, 벽이 하는 일이 막는 것이 아니라
        /// <b>가리는 것</b>이기 때문이다. 눈높이는 최고 옥상까지 올라가고 거기서 벽까지는
        /// 수백 미터라, 벽이 건물보다 조금 높은 정도로는 내려다보는 각도를 못 막는다.
        /// 확실히 넘겨야 한다.
        /// </summary>
        public float BoundaryWallHeight =>
            boundaryWallHeight > 0f ? boundaryWallHeight : Mathf.Max(10f, MaxHeight * 2f);

        /// <summary>격자가 차지하는 실제 범위. 건물 바닥 바깥 끝까지.</summary>
        public Vector2 CitySpan => new Vector2(
            (blockCountX - 1) * PitchX + footprintX,
            (blockCountZ - 1) * PitchZ + footprintZ);

        public bool HasPrefabs
        {
            get
            {
                foreach (var prefab in buildingPrefabs)
                    if (prefab != null) return true;
                return false;
            }
        }

        /// <summary>프리팹 중 가장 큰 바닥. 격자 피치가 이보다 좁으면 건물이 도로를 침범한다.</summary>
        public Vector2 LargestPrefabFootprint
        {
            get
            {
                var largest = Vector2.zero;
                foreach (var prefab in buildingPrefabs)
                {
                    var meta = MetaOf(prefab);
                    if (meta == null) continue;
                    largest.x = Mathf.Max(largest.x, meta.footprint.x);
                    largest.y = Mathf.Max(largest.y, meta.footprint.y);
                }
                return largest;
            }
        }

        /// <summary>
        /// 프리팹 실측 바닥에 격자를 맞춘다.
        ///
        /// 스냅을 여기서 하는 이유는 <c>OnValidate</c>가 인스펙터 입력에만 호출되고
        /// 코드 대입에는 불리지 않기 때문이다. 그냥 대입하면 20.6m 같은 값이 그대로 남아
        /// 에셋의 2m 모듈 격자를 벗어난다.
        /// </summary>
        public void FitFootprint(float size)
        {
            footprintX = SnapUp(size, ModuleWidth);
            footprintZ = footprintX;
        }

        public static CityBuilding MetaOf(GameObject prefab)
        {
            return prefab == null ? null : prefab.GetComponent<CityBuilding>();
        }

        /// <summary>
        /// 높이 분포를 만들 때 쓸 층수 범위.
        ///
        /// 프리팹을 쓰면 층수도 에셋이 정한다. 여기서 인스펙터 값(6~20층)을 그대로 쓰면
        /// Ridge 분포가 실제로 존재하지 않는 층수를 목표로 삼아 전부 같은 건물로 수렴한다.
        /// 그래서 프리팹이 있으면 그 층수 범위로 좁힌다.
        /// </summary>
        public Vector2Int EffectiveFloorRange
        {
            get
            {
                int authoredLow = Mathf.Min(floorCountMin, floorCountMax);
                int authoredHigh = Mathf.Max(floorCountMin, floorCountMax);
                if (!HasPrefabs) return new Vector2Int(authoredLow, authoredHigh);

                int low = int.MaxValue, high = int.MinValue;
                foreach (var prefab in buildingPrefabs)
                {
                    var meta = MetaOf(prefab);
                    if (meta == null) continue;
                    low = Mathf.Min(low, meta.floorCount);
                    high = Mathf.Max(high, meta.floorCount);
                }
                return low > high ? new Vector2Int(authoredLow, authoredHigh) : new Vector2Int(low, high);
            }
        }

        /// <summary>
        /// 비워둘 칸 표시. 광장이 되어 격자 규칙성을 깬다.
        ///
        /// <b>붙어 있는 두 칸을 동시에 비우지 않는다.</b> 빈 칸이 이어지면 앵커 간 거리가
        /// 두 배(72m)가 되어 이론 사거리(37m)를 훨씬 넘고, 그 구간에서 스윙이 끊긴다.
        /// 즉 이 제약은 미관이 아니라 물리에서 나온 것이다.
        /// </summary>
        public bool[,] BuildEmptyMask()
        {
            var empty = new bool[blockCountX, blockCountZ];
            if (emptyCellChance <= 0f) return empty;

            for (int x = 0; x < blockCountX; x++)
            {
                for (int z = 0; z < blockCountZ; z++)
                {
                    if (CellRandom(x, z, 53).NextDouble() >= emptyCellChance) continue;
                    if (x > 0 && empty[x - 1, z]) continue;
                    if (z > 0 && empty[x, z - 1]) continue;
                    empty[x, z] = true;
                }
            }
            return empty;
        }

        /// <summary>옥상 소품 배치용 난수. 격자 좌표에서 결정되므로 재생성해도 같다.</summary>
        public System.Random RoofPropRandom(int x, int z) => CellRandom(x, z, 71);

        /// <summary>광장 소품 배치용 난수.</summary>
        public System.Random PlazaPropRandom(int x, int z) => CellRandom(x, z, 89);

        /// <summary>
        /// 격자 좌표에 놓을 프리팹. 좌표만으로 결정되므로 재생성해도 같은 도시가 나온다.
        /// </summary>
        public GameObject PickPrefab(int x, int z, int targetFloors)
        {
            GameObject best = null;

            if (prefabPickMode == PrefabPickMode.Random)
            {
                // 유효한 것만 세어 인덱스를 고른다. null 슬롯이 섞여도 분포가 치우치지 않게.
                int valid = 0;
                foreach (var prefab in buildingPrefabs)
                    if (prefab != null) valid++;
                if (valid == 0) return null;

                int pick = CellRandom(x, z, 17).Next(valid);
                foreach (var prefab in buildingPrefabs)
                {
                    if (prefab == null) continue;
                    if (pick-- == 0) return prefab;
                }
                return null;
            }

            int bestGap = int.MaxValue;
            foreach (var prefab in buildingPrefabs)
            {
                var meta = MetaOf(prefab);
                if (meta == null) continue;
                int gap = Mathf.Abs(meta.floorCount - targetFloors);
                if (gap >= bestGap) continue;
                bestGap = gap;
                best = prefab;
            }
            return best;
        }

        /// <summary>
        /// 칸 안에서 건물을 얼마나 밀어 놓을지. 도로를 침범하지 않는 한계까지만 민다.
        ///
        /// 한계는 칸 크기와 건물 크기의 차이에서 나온다. 건물이 20m이고 칸이 30m면
        /// 한쪽으로 5m까지 밀 수 있고, 그 이상은 통로로 튀어나온다. 값을 눈대중으로
        /// 두지 않고 여기서 계산하는 이유가 그것이다 — 프리팹이나 칸 크기를 바꾸면
        /// 한계도 따라 움직여야 한다.
        /// </summary>
        public Vector2 CellOffset(int x, int z)
        {
            if (cellJitter <= 0f) return Vector2.zero;

            float span = LargestPrefabSpan;
            float limitX = Mathf.Max(0f, (footprintX - span) * 0.5f) * cellJitter;
            float limitZ = Mathf.Max(0f, (footprintZ - span) * 0.5f) * cellJitter;
            if (limitX <= 0f && limitZ <= 0f) return Vector2.zero;

            var rng = CellRandom(x, z, 191);
            return new Vector2((float)(rng.NextDouble() * 2.0 - 1.0) * limitX,
                               (float)(rng.NextDouble() * 2.0 - 1.0) * limitZ);
        }

        /// <summary>Y축 90° 단위 회전. 회전도 좌표에서 결정된다.</summary>
        public int YRotationSteps(int x, int z)
        {
            return randomYRotation ? CellRandom(x, z, 31).Next(4) : 0;
        }

        /// <summary>
        /// 겹마다 다른 회전. 겹 인덱스까지 섞어야 위아래가 같은 각도로 안 겹친다.
        ///
        /// <b>90°가 아니라 180° 단위인 것이 요점이다.</b> 완성 건물은 바닥이 20×16처럼
        /// 정사각이 아니라서, 한 겹만 90° 돌리면 16×20이 되어 아래 겹보다 옆으로 2m 튀어나온다.
        /// 그 턱은 미관 문제가 아니라 <b>진자가 실제로 부딪히는 물체</b>다 — 옥상 모서리에
        /// 걸고 아래로 흔들면 바로 그 자리를 지나간다.
        /// 180°는 실루엣을 그대로 두면서 출입구만 반대편으로 보낸다.
        ///
        /// 타워 전체를 돌리는 <see cref="YRotationSteps"/>는 90° 단위 그대로다 —
        /// 겹끼리의 상대 관계가 안 바뀌므로 턱이 생기지 않는다.
        /// </summary>
        public int TierRotationSteps(int x, int z, int tier)
        {
            return rotateTiers ? CellRandom(x, z, 127 + tier * 13).Next(2) * 2 : 0;
        }

        /// <summary>
        /// 격자 좌표에 세울 타워의 겹 수.
        ///
        /// 분포를 <see cref="FloorCountAt"/>와 같은 <see cref="heightMode"/>에 물려둔 이유는,
        /// 프리팹을 쓰는 순간 층수 쪽 분포가 사실상 죽기 때문이다 — 완성 건물이 6·8·9층
        /// 셋뿐이라 Ridge를 켜도 스카이라인이 3m 단위로만 흔들린다.
        /// <b>같은 스위치가 이제 겹 수를 흔들어 실제로 스카이라인을 만든다.</b>
        /// </summary>
        public int TowerTiersAt(int x, int z)
        {
            var range = TowerTierRange;
            switch (heightMode)
            {
                case HeightMode.Random:
                    return CellRandom(x, z, 113).Next(range.x, range.y + 1);

                case HeightMode.Ridge:
                    return Mathf.RoundToInt(Mathf.Lerp(range.x, range.y, RidgeWeight(x, z)));

                default:
                    return Mathf.Max(1, towerTiers);
            }
        }

        /// <summary>
        /// 타워의 <paramref name="tier"/>번째 겹에 올릴 프리팹.
        ///
        /// 셋백이면 <b>맨 아래 칸은 지금까지와 똑같이 고르고</b>, 그 위로는 바닥이 좁은 쪽으로
        /// 한 칸씩 내려간다. 밑칸을 고정하지 않는 이유는 변화를 잃지 않기 위해서다 —
        /// 항상 가장 큰 것에서 시작하면 도시의 모든 타워가 같은 조합이 된다.
        /// 종류보다 겹이 많으면 가장 좁은 것을 반복한다.
        /// </summary>
        public GameObject TierPrefab(int x, int z, int tier, int targetFloors)
        {
            var basePrefab = PickPrefab(x, z, targetFloors);
            if (basePrefab == null || tier <= 0 || !towerSetback) return basePrefab;

            var order = SetbackOrder();
            int baseIndex = order.IndexOf(basePrefab);
            if (baseIndex < 0) return basePrefab;

            return order[Mathf.Min(baseIndex + tier, order.Count - 1)];
        }

        /// <summary>
        /// 바닥이 넓은 순으로 세운 프리팹 목록. 셋백은 이 순서를 아래에서 위로 훑는 것이다.
        ///
        /// 정렬 기준이 높이가 아니라 <b>바닥 넓이</b>인 것이 요점이다. 셋백이 성립하려면
        /// 위 칸이 아래 칸 옥상 안에 들어가야 하고, 그걸 정하는 것은 높이가 아니다.
        /// </summary>
        private List<GameObject> SetbackOrder()
        {
            var order = new List<GameObject>();
            foreach (var prefab in buildingPrefabs)
            {
                if (prefab == null || MetaOf(prefab) == null) continue;
                order.Add(prefab);
            }

            order.Sort((a, b) => FootprintArea(b).CompareTo(FootprintArea(a)));
            return order;
        }

        private static float FootprintArea(GameObject prefab)
        {
            var meta = MetaOf(prefab);
            return meta == null ? 0f : meta.footprint.x * meta.footprint.y;
        }

        /// <summary>
        /// 격자 중심에서 멀어질수록 0에 가까워지는 가중치. 중앙이 높은 스카이라인을 만든다.
        /// 층수와 겹 수가 같은 분포를 써야 하므로 공식을 한 군데에 둔다.
        /// </summary>
        private float RidgeWeight(int x, int z)
        {
            float cx = (blockCountX - 1) * 0.5f;
            float cz = (blockCountZ - 1) * 0.5f;
            float maxDist = Mathf.Max(0.0001f, Mathf.Sqrt(cx * cx + cz * cz));
            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
            return 1f - Mathf.Clamp01(dist / maxDist);
        }

        /// <summary>
        /// 격자 좌표 전용 난수. 생성 순서에 의존하지 않게 좌표와 시드만으로 만든다.
        ///
        /// <b>시드를 넣기 전에 비트를 섞는 것이 이 함수의 핵심이다.</b>
        /// 원래는 <c>hash * 31 + 좌표</c>로만 만들어 넘겼는데, <c>System.Random</c>은
        /// <b>첫 출력값이 시드에 거의 비례한다.</b> 인접한 칸은 시드도 가까우므로 결과가
        /// 무작위로 흩어지지 않고 <b>띠를 이룬다</b> — 실제로 프리팹 선택이 한 행에서
        /// <c>2 2 2 2 2 2 1 1 1 1 1 1 1 0</c> 처럼 계단으로 나왔고, 그래서 왼쪽 여섯 칸이
        /// 전부 같은 건물이 됐다.
        ///
        /// 함정은 <b>개수는 멀쩡하다</b>는 것이다. 10×10 전체를 세면 세 종류가 33/33/34로
        /// 고르게 나오므로 분포만 보면 아무 문제가 없어 보이고, 틀어진 것은 공간 배열뿐이라
        /// 눈으로 도시를 봐야 드러난다. 격자 전체가 무작위가 아니라 그라데이션이었다.
        ///
        /// 아래 곱셈·시프트는 MurmurHash3 finalizer 계열의 섞기다. 한 비트만 달라도
        /// 결과 전체가 흩어지므로 인접 칸의 상관이 사라진다. 좌표만으로 결정된다는 성질은
        /// 그대로라 재생성해도 같은 도시가 나온다.
        /// </summary>
        private System.Random CellRandom(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)randomSeed;
                h = (h ^ (uint)x) * 0x9E3779B1u;
                h = (h ^ (h >> 15)) + (uint)z;
                h *= 0x85EBCA77u;
                h = (h ^ (h >> 13)) + (uint)salt;
                h *= 0xC2B2AE3Du;
                h ^= h >> 16;
                return new System.Random((int)h);
            }
        }

        public SwingMetrics.Budget Evaluate()
        {
            return new SwingMetrics.Budget(
                ropeLength, swingAmplitudeDeg, gravityScale, MinFootprint,
                groundClearance, Time.fixedDeltaTime, wallThickness);
        }

        /// <summary>
        /// 격자 좌표에 해당하는 층수. Random / Ridge 모드도 좌표만으로 결정되므로
        /// 몇 번 다시 생성해도 같은 도시가 나온다.
        /// </summary>
        public int FloorCountAt(int x, int z)
        {
            switch (heightMode)
            {
                case HeightMode.Random:
                {
                    var range = EffectiveFloorRange;
                    return CellRandom(x, z, 7).Next(range.x, range.y + 1);
                }

                case HeightMode.Ridge:
                {
                    // 격자 중심에서의 거리를 0~1로 정규화해 높이를 보간한다.
                    var range = EffectiveFloorRange;
                    return Mathf.RoundToInt(Mathf.Lerp(range.x, range.y, RidgeWeight(x, z)));
                }

                default:
                    return floorCount;
            }
        }

        private void OnValidate()
        {
            // 에셋 격자를 벗어난 값은 애초에 만들지 못하게 한다.
            // 회색 박스 단계에서 27.4m 같은 값으로 측정해두면 모듈 조각으로 교체할 때 전부 어긋난다.
            footprintX = SnapUp(footprintX, ModuleWidth);
            footprintZ = SnapUp(footprintZ, ModuleWidth);
            if (floorCountMax < floorCountMin) floorCountMax = floorCountMin;
            if (towerTiersMax < towerTiersMin) towerTiersMax = towerTiersMin;
        }

        /// <summary>
        /// 모듈 격자에 올림으로 맞춘다.
        ///
        /// 반올림이 아니라 올림인 이유가 있다. 프리팹 실측 바닥이 20.6m일 때 반올림하면 20m가 되어
        /// 건물이 격자보다 커지고, 그러면 도로를 침범하면서 경고도 사라지지 않는다.
        /// </summary>
        private static float SnapUp(float value, float step)
        {
            return Mathf.Max(step, Mathf.Ceil(value / step - 0.001f) * step);
        }
    }
}
