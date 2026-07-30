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

        [Header("도로 폭 — 사실상 앵커 간격을 결정한다")]
        [Min(1f)] public float streetWidth = 14f;

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

        [Header("광장 — 빈 칸 꾸미기")]
        [Tooltip("빈 칸을 덮을 바닥 타일. Sidewalk_NoCurb_3m 같은 턱 없는 포장재가 맞다. " +
                 "비우면 빈 칸에 지면이 그대로 노출되어 '빠뜨린 칸'으로 보인다.")]
        public GameObject plazaFloorPrefab;
        [Tooltip("광장에 놓을 것. Sidewalk_Planter · Prop_Planter_Single 등.")]
        public List<GameObject> plazaPropPrefabs = new List<GameObject>();
        [Min(0)] public int plazaPropCountMin = 2;
        [Min(0)] public int plazaPropCountMax = 5;
        [Min(0f)] public float plazaPropEdgeMargin = 3f;

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

        [Header("도로 타일 (선택) — 비우면 지면 머티리얼만 보인다")]
        [Tooltip("Street_2Lane 같은 도로 구간. 폭이 streetWidth와 같아야 한다. " +
                 "긴 축·짧은 축은 생성기가 재서 알아낸다.")]
        public GameObject roadSegmentPrefab;
        [Tooltip("Street_4WayIntersection 같은 교차로. streetWidth × streetWidth 정사각이어야 한다. " +
                 "비우면 교차로 자리에 지면이 그대로 보인다.")]
        public GameObject intersectionPrefab;
        [Tooltip("Decal_Crosswalk 같은 횡단보도 데칼. 교차로마다 4방향에 자동으로 깐다. " +
                 "긴 축이 streetWidth와 맞아야 한다.")]
        public GameObject crosswalkPrefab;

        [Header("외형 (측정에는 영향 없음)")]
        public Material greyboxMaterial;

        // ─── 파생값 ───

        /// <summary>격자 피치 = 바닥 크기 + 도로 폭. 인접 건물 중심 간 거리이자 앵커 간격.</summary>
        public float PitchX => footprintX + streetWidth;
        public float PitchZ => footprintZ + streetWidth;

        public float GravityMagnitude => SwingMetrics.StandardGravity * gravityScale;

        /// <summary>가장 짧은 쪽 바닥 크기. 역산 시 보수적으로 이걸 쓴다.</summary>
        public float MinFootprint => Mathf.Min(footprintX, footprintZ);

        /// <summary>
        /// 이 설정에서 실제로 세워질 최고 건물 높이.
        ///
        /// 프리팹을 쓰는 순간 높이는 설정값이 아니라 <b>에셋이 결정한다.</b>
        /// 회색 박스에서는 12층을 적으면 36m가 나오지만, 완성 건물이 28m뿐이면 28m가 상한이다.
        /// 역산 경고가 실제 에셋 기준으로 뜨게 하려면 이 구분이 필요하다.
        /// </summary>
        public float MaxHeight
        {
            get
            {
                if (!HasPrefabs)
                    return FloorHeight * (heightMode == HeightMode.Uniform ? floorCount : floorCountMax);

                float tallest = 0f;
                foreach (var prefab in buildingPrefabs)
                {
                    var meta = MetaOf(prefab);
                    if (meta != null) tallest = Mathf.Max(tallest, meta.height);
                }
                return tallest;
            }
        }

        public int TotalBuildings => blockCountX * blockCountZ;

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

        /// <summary>Y축 90° 단위 회전. 회전도 좌표에서 결정된다.</summary>
        public int YRotationSteps(int x, int z)
        {
            return randomYRotation ? CellRandom(x, z, 31).Next(4) : 0;
        }

        /// <summary>
        /// 격자 좌표 전용 난수. 생성 순서에 의존하지 않게 좌표와 시드만으로 만든다.
        /// </summary>
        private System.Random CellRandom(int x, int z, int salt)
        {
            int hash = randomSeed;
            unchecked
            {
                hash = hash * 31 + x;
                hash = hash * 31 + z;
                hash = hash * 31 + salt;
            }
            return new System.Random(hash);
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
                    float cx = (blockCountX - 1) * 0.5f;
                    float cz = (blockCountZ - 1) * 0.5f;
                    float maxDist = Mathf.Max(0.0001f, Mathf.Sqrt(cx * cx + cz * cz));
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                    float t = 1f - Mathf.Clamp01(dist / maxDist);
                    return Mathf.RoundToInt(Mathf.Lerp(range.x, range.y, t));
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
