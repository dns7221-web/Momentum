using System;
using System.Collections.Generic;
using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 남북으로 뻗는 애비뉴(간선) 한 줄.
    /// 위치와 폭만 적는다. 어디서 끊기고 어디가 블록이 되는지는 다른 도로와 공원을 보고
    /// <see cref="CityPlanLayout"/>이 계산한다 — 사람이 적는 값을 최소로 두어야 고칠 때 한 군데만 고친다.
    /// </summary>
    [Serializable]
    public class PlanAvenue
    {
        public string id = "A";
        [Tooltip("중심선 X 좌표(m, 생성기 로컬).")]
        public float centerX;
        [Tooltip("인도까지 포함한 전체 폭(m). 4Lane 18 + 인도 3 × 2 = 24.")]
        [Min(1f)] public float width = 24f;

        public PlanAvenue() { }

        public PlanAvenue(string id, float centerX, float width)
        {
            this.id = id;
            this.centerX = centerX;
            this.width = width;
        }

        public float MinX => centerX - width * 0.5f;
        public float MaxX => centerX + width * 0.5f;
    }

    /// <summary>동서로 뻗는 스트리트(이면도로) 한 줄. 짧고 좁아서 방향을 꺾는 데 쓰인다.</summary>
    [Serializable]
    public class PlanStreet
    {
        public string id = "S";
        [Tooltip("중심선 Z 좌표(m, 생성기 로컬).")]
        public float centerZ;
        [Tooltip("전체 폭(m). Street_2Lane 한 장 = 12.")]
        [Min(1f)] public float width = 12f;

        public PlanStreet() { }

        public PlanStreet(string id, float centerZ, float width)
        {
            this.id = id;
            this.centerZ = centerZ;
            this.width = width;
        }

        public float MinZ => centerZ - width * 0.5f;
        public float MaxZ => centerZ + width * 0.5f;
    }

    /// <summary>
    /// 축 정렬 사각형. 공원·산책로·강·랜드마크 필지처럼 "어디부터 어디까지"로 적는 영역에 쓴다.
    /// Unity의 <see cref="Rect"/>를 쓰지 않은 이유: Rect는 (x, y, 폭, 높이)라 인스펙터에서
    /// 기획서의 "X −72 ~ 72" 같은 범위 표기를 그대로 옮겨 적기 어렵다.
    /// </summary>
    [Serializable]
    public class PlanRect
    {
        public float x0, x1, z0, z1;

        public PlanRect() { }

        public PlanRect(float x0, float x1, float z0, float z1)
        {
            this.x0 = x0;
            this.x1 = x1;
            this.z0 = z0;
            this.z1 = z1;
        }

        public Vector2 Center => new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
        public Vector2 Size => new Vector2(x1 - x0, z1 - z0);

        public bool Contains(Vector2 p) => p.x > x0 && p.x < x1 && p.y > z0 && p.y < z1;

        public bool Overlaps(float ox0, float ox1, float oz0, float oz1) =>
            ox0 < x1 && ox1 > x0 && oz0 < z1 && oz1 > z0;

        /// <summary>점에서 사각형 테두리까지의 거리. 안쪽이면 0.</summary>
        public float DistanceTo(Vector2 p)
        {
            float dx = Mathf.Max(x0 - p.x, 0f, p.x - x1);
            float dz = Mathf.Max(z0 - p.y, 0f, p.y - z1);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>사선 대로가 애비뉴·스트리트와 만나는 자리의 원형 광장.</summary>
    [Serializable]
    public class PlanPlaza
    {
        public string id = "P";
        [Tooltip("(X, Z)")] public Vector2 center;
        [Min(1f)] public float radius = 34f;

        public PlanPlaza() { }

        public PlanPlaza(string id, Vector2 center, float radius)
        {
            this.id = id;
            this.center = center;
            this.radius = radius;
        }
    }

    /// <summary>랜드마크 타워. 프리팹 적층으로는 87 m가 상한이라 회색 박스로 세운다.</summary>
    [Serializable]
    public class PlanLandmark
    {
        public string id = "L";
        public PlanRect area = new PlanRect();
        [Min(1f)] public float height = 150f;

        public PlanLandmark() { }

        public PlanLandmark(string id, PlanRect area, float height)
        {
            this.id = id;
            this.area = area;
            this.height = height;
        }
    }

    /// <summary>높이 지도의 봉우리 하나. 가우시안 언덕으로 더해진다.</summary>
    [Serializable]
    public class PlanPeak
    {
        [Tooltip("(X, Z)")] public Vector2 center;
        public float amplitude = 50f;
        [Min(1f)] public float sigma = 90f;

        public PlanPeak() { }

        public PlanPeak(Vector2 center, float amplitude, float sigma)
        {
            this.center = center;
            this.amplitude = amplitude;
            this.sigma = sigma;
        }
    }

    /// <summary>지상 잡동사니 한 종류. Kenney처럼 작은 모델을 실측해 목표 길이로 키운다.</summary>
    [Serializable]
    public class PlanProp
    {
        public GameObject prefab;
        [Tooltip("가장 긴 수평 변을 이 길이(m)로 맞춘다. 0이면 원래 크기. 예: 컨테이너 6, 트럭 7, 팔레트 1.2.")]
        [Min(0f)] public float length = 0f;
        [Tooltip("뽑힐 비중. 0이면 쓰지 않는다.")]
        [Min(0f)] public float weight = 1f;
    }

    /// <summary>
    /// 기획 v3 "방향이 보이는 도시"의 도로망·특수 요소·필지 규칙을 담는 에셋.
    ///
    /// 기존 <see cref="CityLayoutSettings"/>는 "칸 하나"를 반복하는 격자라 칸 크기 몇 개로
    /// 도시 전체가 정해졌다. 이 에셋은 반대로 <b>도로망을 데이터로 적고</b> 블록과 필지는
    /// 거기서 계산한다. 그래서 애비뉴 하나를 옮기면 양옆 블록 폭이 자동으로 따라 바뀐다.
    ///
    /// 필드 초기값이 기획 v3 상세 배치도(Docs/city-plan-v3.json)의 좌표 그대로다.
    /// 값을 망가뜨렸을 때는 인스펙터 ⋮ 메뉴의 Reset으로 기획값에 되돌아온다 — 유니티가
    /// 필드 초기화 식을 다시 실행해주므로 되돌리기 함수를 따로 만들지 않았다.
    /// </summary>
    [CreateAssetMenu(menuName = "Momentum/City Plan Settings", fileName = "CityPlan_v3")]
    public class CityPlanSettings : ScriptableObject
    {
        [Header("땅 · 도시 범위 (생성기 로컬 좌표, m)")]
        public PlanRect land = new PlanRect(-200f, 200f, -430f, 430f);
        [Tooltip("블록이 시작되는 남쪽 경계 = 남쪽 산책로 안쪽 끝.")]
        public float citySouthZ = -406f;
        [Tooltip("블록이 끝나는 북쪽 경계 = 북쪽 원경 띠 안쪽 끝.")]
        public float cityNorthZ = 416f;

        [Header("도로망")]
        public List<PlanAvenue> avenues = new List<PlanAvenue>
        {
            new PlanAvenue("A1", -168f, 24f),
            new PlanAvenue("A2", -84f, 24f),
            new PlanAvenue("A3", 0f, 24f),
            new PlanAvenue("A4", 84f, 24f),
            new PlanAvenue("A5", 168f, 24f),
        };

        public List<PlanStreet> streets = new List<PlanStreet>
        {
            new PlanStreet("S1", -250f, 12f),
            new PlanStreet("S2", -88f, 12f),
            new PlanStreet("S3", 74f, 12f),
            new PlanStreet("S4", 266f, 12f),
        };

        [Tooltip("사선 대로 D1 시작점 (X, Z). 남동쪽 강변.")]
        public Vector2 diagonalFrom = new Vector2(164.9f, -406f);
        [Tooltip("사선 대로 D1 끝점 (X, Z). 공원 남서 모서리 광장.")]
        public Vector2 diagonalTo = new Vector2(-84f, 74f);
        [Min(1f)] public float diagonalWidth = 24f;

        [Header("열린 공간")]
        public PlanRect park = new PlanRect(-72f, 72f, 80f, 260f);

        public List<PlanPlaza> plazas = new List<PlanPlaza>
        {
            new PlanPlaza("P1", new Vector2(84f, -250f), 34f),
            new PlanPlaza("P2", new Vector2(0f, -88f), 34f),
            new PlanPlaza("P3", new Vector2(-84f, 74f), 34f),
        };

        public PlanRect westPromenade = new PlanRect(-200f, -180f, -430f, 430f);
        // 서쪽 산책로와 모서리(X −200~−180)가 겹치면 같은 높이 면끼리 z-fighting이 나서 −180부터 시작한다.
        public PlanRect southPromenade = new PlanRect(-180f, 180f, -430f, -406f);

        [Header("랜드마크 (회색 박스 3단 셋백)")]
        public List<PlanLandmark> landmarks = new List<PlanLandmark>
        {
            new PlanLandmark("L1", new PlanRect(12f, 44f, -348f, -312f), 150f),
            new PlanLandmark("L2", new PlanRect(12f, 46f, -22f, 14f), 171f),
            new PlanLandmark("L3", new PlanRect(96f, 128f, 152f, 188f), 132f),
        };

        [Header("고가철도 (A5 동쪽 차로 위)")]
        public PlanRect railDeck = new PlanRect(173f, 179f, -390f, 68f);
        [Tooltip("상판 윗면 높이(m). 건물보다 낮은 앵커 줄을 만드는 것이 목적이다.")]
        public float railDeckTop = 12f;
        [Min(0.1f)] public float railDeckThickness = 1.5f;
        [Min(1f)] public float railPillarSpacing = 24f;
        [Min(0.1f)] public float railPillarSize = 1.2f;

        [Header("강")]
        public List<PlanRect> water = new List<PlanRect>
        {
            new PlanRect(-260f, -200f, -490f, 430f),
            new PlanRect(-200f, 200f, -490f, -430f),
        };
        public float waterSurfaceY = -2f;
        [Tooltip("강바닥 윗면. 떨어진 플레이어가 서는 곳이다 — 수면 바로 아래에 둔다.")]
        public float riverbedY = -3f;
        public List<PlanRect> piers = new List<PlanRect>
        {
            new PlanRect(-228f, -200f, -334f, -326f),
            new PlanRect(-228f, -200f, -154f, -146f),
            new PlanRect(-228f, -200f, 106f, 114f),
            new PlanRect(-228f, -200f, 226f, 234f),
            new PlanRect(-144f, -136f, -458f, -430f),
            new PlanRect(36f, 44f, -458f, -430f),
        };

        [Header("원경 실루엣 · 경계")]
        public PlanRect backdropEast = new PlanRect(180f, 200f, -406f, 416f);
        public PlanRect backdropNorth = new PlanRect(-200f, 200f, 416f, 430f);
        [Min(1f)] public float backdropSegment = 20f;
        [Tooltip("원경 박스 높이 범위 (최소, 최대).")]
        public Vector2 backdropHeight = new Vector2(60f, 120f);
        [Tooltip("땅과 강을 감싸는 경계벽 높이. 도시 어디서 봐도 밖이 안 보이도록 랜드마크(171 m)보다 충분히 높게 둔다.")]
        [Min(10f)] public float boundaryHeight = 300f;
        [Tooltip("경계벽을 보이게 한다. 끄면 예전처럼 충돌체만 남는다.")]
        public bool outerWallVisible = true;
        [Min(0.5f)] public float outerWallThickness = 5f;
        [Tooltip("경계벽에 웹이 붙게 할지. 기본은 끔 — 맵 끝 벽을 타고 도는 플레이를 막는다.")]
        public bool outerWallAnchorable = false;

        [Header("필지 규칙")]
        [Tooltip("블록 둘레 필지의 깊이(m). 블록 폭 60에서 양쪽 20씩 빼면 가운데 20 m가 빈 마당이 된다.")]
        [Min(1f)] public float frontageDepth = 20f;
        [Tooltip("Large_2(20 × 16)가 들어가는 필지 폭.")]
        public float lotWidthLarge = 20f;
        [Tooltip("Medium_2_001(14.3 × 12.2)이 들어가는 필지 폭.")]
        public float lotWidthMedium = 15f;
        [Tooltip("Small_1(12.3 × 12.3)이 들어가는 필지 폭.")]
        public float lotWidthSmall = 13f;
        [Tooltip("남은 길이가 이보다 짧으면 마지막 필지가 나머지를 흡수하고 회색 박스가 된다.")]
        public float minRemainder = 13f;

        [Tooltip("사선 중심선에서 이 거리 안쪽과 겹치는 필지는 지운다. 그 띠는 사선 가로벽 자리다.")]
        public float diagonalBandOuter = 28f;
        public float diagonalWallOffset = 20f;
        public float diagonalWallLength = 18f;
        public float diagonalWallDepth = 16f;
        public float diagonalWallSpacing = 19f;
        public float diagonalWallStart = 10f;

        [Tooltip("광장 중심에서 이 거리 안쪽에 중심이 있는 필지는 지운다.")]
        public float plazaLotClearance = 34f;
        public float plazaDiagonalClearance = 36f;
        public float landmarkDiagonalClearance = 12f;

        [Tooltip("필지 폭을 고르는 난수 시드. 웹 배치도와 같은 값이면 같은 필지가 나온다.")]
        public int lotSeed = 20261007;
        [Tooltip("필지 높이 흔들기 난수 시드.")]
        public int jitterSeed = 4242;

        [Header("높이 규칙")]
        public float baseHeight = 36f;
        public List<PlanPeak> peaks = new List<PlanPeak>
        {
            new PlanPeak(new Vector2(20f, -320f), 56f, 85f),   // 다운타운
            new PlanPeak(new Vector2(-10f, 0f), 64f, 100f),    // 미드타운
        };
        [Tooltip("이 Z보다 북쪽은 저층 주거지로 높이를 고정한다.")]
        public float northZ = 272f;
        public float northHeight = 38f;
        [Tooltip("공원 테두리에서 이 거리 안이면 한 단 더 올린다.")]
        public float parkEdgeDistance = 40f;
        public float parkEdgeBonus = 22f;
        [Tooltip("필지마다 ± 이만큼 흔든다.")]
        public float heightJitter = 6f;
        [Tooltip("회색 박스 최소 높이. 2겹 최저(Small+Small 34 m)와 맞췄다.")]
        public float minHeight = 34f;
        [Tooltip("필지 높이가 프리팹 4겹 최대보다 이만큼 넘게 높으면 회색 박스로 바꾼다.")]
        public float greyboxOverMax = 8f;
        [Tooltip("회색 박스 높이를 맞출 단위. 층고 3 m.")]
        public float greyboxHeightStep = 3f;

        [Header("공원 나무")]
        public float treeSpacing = 15f;
        public float treeInset = 8f;
        [Range(0f, 1f)] public float treeSkipChance = 0.25f;
        [Tooltip("나무 높이(m). 공원 안에서 저공 앵커가 된다.")]
        public float treeHeight = 12f;
        public int treeSeed = 77;

        [Header("기타 시드")]
        public int backdropSeed = 31;
        [Tooltip("건물 방향(90°·180° 뒤집기) 난수 시드.")]
        public int orientationSeed = 911;

        [Header("물리")]
        [Tooltip("이 도시가 가정하는 중력 배율. 생성기 Awake에서 Physics.gravity에 건다.")]
        [Range(0.5f, 5f)] public float gravityScale = 1.5f;

        [Header("검증 기준 (인스펙터 표시용)")]
        [Tooltip("GrappleGun.maxRange와 같은 값. 애비뉴 폭이 사거리 안인지 확인하는 데만 쓴다.")]
        [Min(1f)] public float webRange = 22f;

        [Header("건물 프리팹 (BuildingPrefabTool로 만든 것)")]
        [Tooltip("바닥이 가장 넓은 것. 셋백은 Large → Medium → Small 순서로 올라간다.")]
        public GameObject largePrefab;
        public GameObject mediumPrefab;
        public GameObject smallPrefab;
        [Tooltip("겹마다 180° 뒤집어 같은 얼굴이 위아래로 이어지지 않게 한다.")]
        public bool rotateTiers = true;

        [Header("도로 타일")]
        [Tooltip("애비뉴·사선 대로 차도 (Street_4Lane).")]
        public GameObject avenueRoadPrefab;
        [Tooltip("애비뉴 양옆에 덧대는 인도 (Sidewalk_NoCurb_3m 권장 — 연석 방향을 신경 쓸 필요가 없다).")]
        public GameObject sidewalkStripPrefab;
        [Min(0.1f)] public float sidewalkStripWidth = 3f;
        [Tooltip("스트리트 차도 (Street_2Lane).")]
        public GameObject streetRoadPrefab;
        [Tooltip("애비뉴 × 스트리트 교차로 (Street_Asphalt_6x6).")]
        public GameObject intersectionPrefab;
        [Tooltip("도로 타일에 메시 충돌체를 붙인다. 이 키트 FBX에는 충돌체가 없다.")]
        public bool surfaceColliders = true;

        [Header("옥상")]
        [Tooltip("길 쪽 옥상 모서리에 두를 난간 조각 (Trim_Wall_Guard). 비우면 두르지 않는다.")]
        public GameObject roofRailPrefab;
        [Min(0f)] public float roofRailInset = 0.3f;

        [Header("공원 나무 프리팹 (비우면 회색 기둥 + 구)")]
        public GameObject treePrefab;
        [Tooltip("나무 프리팹을 treeHeight에 맞춰 키운다. Kenney 나무처럼 작은 모델용.")]
        public bool treeFitHeight = true;

        [Header("WhiteCity 건물 (회색 박스 대체) — 비우면 회색 박스 그대로")]
        [Tooltip("WhiteCity/Prefabs/Buildings의 건물들. 필지마다 가장 잘 맞는 것을 골라 높이를 맞춰 세운다.")]
        public List<GameObject> whiteCityBuildings = new List<GameObject>();
        [Tooltip("세로로만 늘이는 한계. 1.8이면 원래 비율보다 1.8배 길쭉하거나 납작한 것까지 허용. 넘으면 회색 박스로 남는다.")]
        [Range(1f, 4f)] public float whiteCityMaxStretch = 1.8f;
        [Tooltip("필지에 맞춰 바닥을 키우거나 줄이는 배율 범위 (최소, 최대).")]
        public Vector2 whiteCityFootprintScale = new Vector2(0.5f, 2f);
        [Tooltip("필지를 얼마나 채울지. 1이면 필지 경계까지 꽉 채운다.")]
        [Range(0.5f, 1f)] public float whiteCityFill = 0.92f;
        [Tooltip("사선 대로 가로벽도 WhiteCity로 세운다.")]
        public bool whiteCityOnDiagonalWalls = true;
        public int whiteCitySeed = 1357;

        [Header("옥상 소품 (WhiteCity/Prefabs/Roof) — WhiteCity 건물 위에만")]
        public List<GameObject> roofPropPrefabs = new List<GameObject>();
        [Tooltip("건물 하나당 개수 (최소, 최대).")]
        public Vector2Int roofPropsPerBuilding = new Vector2Int(0, 3);
        [Min(0f)] public float roofPropEdgeMargin = 2f;
        [Tooltip("소품에 충돌체를 단다. 끄면 보이기만 하고 착지에 걸리지 않는다.")]
        public bool roofPropColliders = false;

        [Header("도로 소품 (Kenney city-kit-roads) — 빈 칸은 건너뛴다")]
        [Tooltip("light-curved 등. 피벗(기둥 밑)을 기준으로 세운다.")]
        public GameObject streetLightPrefab;
        [Min(1f)] public float streetLightHeight = 8f;
        [Min(4f)] public float streetLightSpacing = 24f;
        [Tooltip("팔이 차도를 향하지 않으면 90 / 180 / 270으로 바꿔본다. 모델마다 앞 방향이 다르다.")]
        public float streetLightYawOffset = 0f;
        [Tooltip("traffic-light 등.")]
        public GameObject trafficLightPrefab;
        [Min(1f)] public float trafficLightHeight = 6f;
        public float trafficLightYawOffset = 0f;
        [Tooltip("sign-highway-wide 등. 애비뉴 폭에 맞춰 가로지르게 세운다.")]
        public GameObject gantryPrefab;
        public string gantryAvenue = "A3";
        [Tooltip("갠트리를 걸 Z 좌표들. 광장·공원 안이면 건너뛴다.")]
        public List<float> gantryZ = new List<float> { -330f, -180f, 20f };
        [Min(1f)] public float gantryHeight = 8f;
        public float gantryYawOffset = 0f;
        [Tooltip("도로 소품에 웹이 붙게 할지. 기본은 끔 — 지면 근처에서 웹이 걸리지 않게.")]
        public bool streetPropsAnchorable = false;

        [Header("고가 구조물 프리팹 — 비우면 회색 박스")]
        [Tooltip("road-bridge 등. 상판 폭에 맞춰 상판 길이만큼 이어 깔다.")]
        public GameObject elevatedDeckPrefab;
        [Tooltip("bridge-pillar 등. 상판 밑면까지 늘여 세운다.")]
        public GameObject elevatedPillarPrefab;
        [Tooltip("난간이 진행 방향을 가로막으면 90으로 바꾼다.")]
        public float elevatedDeckYawOffset = 0f;

        [Header("지상 잡동사니 (Kenney retro-urban) — 비우면 건너뛴다")]
        [Tooltip("부두: 컨테이너·트럭·팔레트. 잔교 위 한 줄 + 뒤쪽 하역장.")]
        public List<PlanProp> dockProps = new List<PlanProp>();
        [Min(0f)] public float dockPropGap = 2f;
        [Range(0f, 1f)] public float dockPropSkipChance = 0.25f;
        [Tooltip("블록 안쪽 빈 마당: 쓰레기통·울타리·팔레트.")]
        public List<PlanProp> yardProps = new List<PlanProp>();
        [Min(0)] public int yardPropsPerBlock = 4;
        public int clutterSeed = 2024;

        [Header("머티리얼 (비우면 아래 대체값)")]
        [Tooltip("회색 박스 필지·사선 가로벽에 번갈아 쓴다. 여러 개 넣으면 반복감이 준다.")]
        public List<Material> greyboxMaterials = new List<Material>();
        public Material landmarkMaterial;
        public Material backdropMaterial;
        public Material groundMaterial;
        [Tooltip("산책로·블록 마당·광장 바닥.")]
        public Material pavingMaterial;
        public Material parkMaterial;
        public Material waterMaterial;
        public Material railMaterial;
        public Material treeMaterial;
        [Tooltip("경계벽. 하늘·안개 색 단색(Unlit)을 넣으면 벽이 아니라 먼 안개처럼 보인다. 비우면 원경 머티리얼.")]
        public Material outerWallMaterial;
        [Tooltip("블록 바닥 텍스처 한 장이 덮는 거리(m).")]
        [Min(0.1f)] public float pavingUvSize = 10f;

        [Header("성능")]
        [Tooltip("생성물을 Static으로 표시해 정적 배칭을 받는다.")]
        public bool markStatic = true;

        // ─── 파생값 ───

        public float GravityMagnitude => SwingMetrics.StandardGravity * gravityScale;

        /// <summary>X 순으로 정렬한 애비뉴. 인스펙터 리스트 순서가 섞여도 블록 계산이 틀리지 않게.</summary>
        public List<PlanAvenue> SortedAvenues()
        {
            var list = new List<PlanAvenue>();
            foreach (var a in avenues) if (a != null) list.Add(a);
            list.Sort((a, b) => a.centerX.CompareTo(b.centerX));
            return list;
        }

        /// <summary>Z 순으로 정렬한 스트리트.</summary>
        public List<PlanStreet> SortedStreets()
        {
            var list = new List<PlanStreet>();
            foreach (var s in streets) if (s != null) list.Add(s);
            list.Sort((a, b) => a.centerZ.CompareTo(b.centerZ));
            return list;
        }

        /// <summary>
        /// 블록 열의 X 범위 (x = 서쪽 끝, y = 동쪽 끝).
        /// 이웃한 두 애비뉴 사이가 곧 블록 한 열이다 — 블록 좌표를 따로 적지 않는 이유.
        /// </summary>
        public List<Vector2> BlockColumns()
        {
            var list = new List<Vector2>();
            var sorted = SortedAvenues();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                float x0 = sorted[i].MaxX;
                float x1 = sorted[i + 1].MinX;
                if (x1 - x0 > 1f) list.Add(new Vector2(x0, x1));
            }
            return list;
        }

        /// <summary>블록 행의 Z 범위 (x = 남쪽 끝, y = 북쪽 끝). 도시 남북 경계와 스트리트 사이.</summary>
        public List<Vector2> BlockRows()
        {
            var list = new List<Vector2>();
            float start = citySouthZ;
            foreach (var street in SortedStreets())
            {
                if (street.MinZ - start > 1f) list.Add(new Vector2(start, street.MinZ));
                start = street.MaxZ;
            }
            if (cityNorthZ - start > 1f) list.Add(new Vector2(start, cityNorthZ));
            return list;
        }

        /// <summary>
        /// 높이 지도. 두 봉우리(가우시안) + 북부 고정 + 공원 가장자리 보정.
        ///
        /// 칸마다 무작위였던 기존 높이와 달리 <b>이웃 필지끼리 높이가 이어진다.</b>
        /// 그래서 애비뉴를 따라가면 올라갔다 내려가는 지형이 생기고, 그게 속도의 근거가 된다
        /// (높은 앵커에서 낮은 앵커로 가면 위치에너지가 속도로 바뀐다).
        /// </summary>
        public float HeightAt(Vector2 p)
        {
            float h = baseHeight;
            foreach (var peak in peaks)
            {
                if (peak == null || peak.sigma <= 0f) continue;
                float d2 = (p - peak.center).sqrMagnitude;
                h += peak.amplitude * Mathf.Exp(-d2 / (2f * peak.sigma * peak.sigma));
            }
            if (p.y > northZ) h = northHeight;
            if (park.DistanceTo(p) < parkEdgeDistance) h += parkEdgeBonus;
            return h;
        }
    }
}
