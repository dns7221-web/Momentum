using System;
using System.Collections.Generic;
using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 도시 꾸미기 — WhiteCity 건물 교체, 옥상 소품, 도로 소품, 고가 구조물 프리팹, 지상 잡동사니.
    ///
    /// 뼈대(도로·블록·필지·높이)를 만드는 본 파일과 분리했다. 뼈대는 스윙 수치를 정하는 부분이라
    /// 자주 건드리지 않고, 꾸미기는 에셋을 바꿀 때마다 손이 가서 한 파일에 두면 고칠 곳을 찾기 어렵다.
    ///
    /// 규칙은 하나다: <b>설정 칸이 비어 있으면 그 단계는 아무것도 하지 않는다.</b>
    /// 그래서 에셋을 하나씩 넣어가며 결과를 볼 수 있고, 빼면 이전 회색 박스 상태로 돌아간다.
    ///
    /// 크기는 모두 실측해서 맞춘다. Kenney 모델은 1칸이 1유닛 정도로 작고 WhiteCity는 건물마다
    /// 크기가 달라서, 배율을 설정에 적어두면 에셋을 바꿀 때마다 틀린다.
    /// </summary>
    public partial class CityPlanGenerator
    {
        private struct WhiteCitySpec
        {
            public GameObject prefab;
            public Bounds bounds;
        }

        /// <summary>실측한 소품 한 종류. size는 배율을 곱한 뒤의 (X, Z) 크기.</summary>
        private struct PropSpec
        {
            public GameObject prefab;
            public Bounds bounds;
            public float scale;
            public float weight;
            public Vector2 size;
        }

        private readonly List<WhiteCitySpec> whiteCitySpecs = new List<WhiteCitySpec>();
        private readonly List<CityBuilding> whiteCityBuilt = new List<CityBuilding>();
        private int whiteCityFallback;
        private int whiteCityLotCount;
        private float treeScale = 1f;
        private float treeLift;

        // ─── 공용 배치 ───

        /// <summary>
        /// 실측 바운즈 기준으로 프리팹을 놓는다. 바닥이 <paramref name="baseY"/>에 닿고,
        /// <paramref name="centerOnBounds"/>면 바운즈 중심이, 아니면 피벗이 <paramref name="at"/>에 온다.
        /// 배율은 프리팹 로컬 축 기준이다 — 잴 때 회전 0으로 쟀기 때문에 로컬 축 = 바운즈 축이다.
        /// </summary>
        private GameObject PlaceFitted(GameObject prefab, Bounds b, Vector3 mult, float yaw, Vector2 at, float baseY,
                                       Transform parent, bool centerOnBounds)
        {
            var go = InstantiateLinked(prefab);
            go.transform.SetParent(parent, false);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.Scale(go.transform.localScale, mult);
            Vector3 off = centerOnBounds ? rot * Vector3.Scale(new Vector3(b.center.x, 0f, b.center.z), mult) : Vector3.zero;
            go.transform.localPosition = new Vector3(at.x - off.x, baseY - b.min.y * mult.y, at.y - off.z);
            return go;
        }

        // ─── WhiteCity 건물 ───

        private void PrepareWhiteCity(Transform probeParent)
        {
            whiteCitySpecs.Clear();
            whiteCityBuilt.Clear();
            whiteCityFallback = 0;
            whiteCityLotCount = 0;

            foreach (var prefab in settings.whiteCityBuildings)
            {
                if (prefab == null) continue;
                if (!TryMeasurePrefab(prefab, probeParent, out Bounds b)) continue;
                if (b.size.y < 1f || b.size.x < 0.5f || b.size.z < 0.5f) continue;
                whiteCitySpecs.Add(new WhiteCitySpec { prefab = prefab, bounds = b });
            }
        }

        /// <summary>
        /// 회색 박스 자리(slotX × slotZ, 높이 height)에 가장 잘 맞는 WhiteCity 건물을 세운다. 맞는 게 없으면 null.
        ///
        /// 바닥은 필지에 맞춰 <b>균일하게</b> 키우거나 줄이고, 높이는 세로로만 늘여 정확히 맞춘다.
        /// 높이를 맞추는 이유: 필지 높이는 높이 지도에서 나온 앵커 고도라 스윙 경로가 거기에 기대고 있다.
        /// 세로로 늘인 정도(원래 비율 대비)가 한계를 넘으면 창문이 눈에 띄게 늘어나 보여서 거른다.
        ///
        /// 반환값은 바닥 중앙 피벗의 빈 루트(CityBuilding 포함)이고, 건물은 그 자식이다.
        /// 회색 박스와 같은 규약이라 호출하는 쪽은 위치·회전만 잡으면 된다.
        /// </summary>
        private GameObject TryCreateWhiteCity(string name, float slotX, float slotZ, float height, PlanRandom rng)
        {
            if (whiteCitySpecs.Count == 0 || slotX <= 0f || slotZ <= 0f) return null;

            float minScale = settings.whiteCityFootprintScale.x;
            float maxScale = settings.whiteCityFootprintScale.y;
            float maxLog = Mathf.Log(Mathf.Max(1.0001f, settings.whiteCityMaxStretch));
            float fill = settings.whiteCityFill;

            var candidates = new List<(int spec, int quarter, float sxz, float score)>();
            float best = float.MaxValue;
            for (int i = 0; i < whiteCitySpecs.Count; i++)
            {
                Vector3 size = whiteCitySpecs[i].bounds.size;
                for (int q = 0; q < 2; q++)
                {
                    float bx = q == 0 ? size.x : size.z;
                    float bz = q == 0 ? size.z : size.x;
                    float sxz = Mathf.Min(slotX * fill / bx, slotZ * fill / bz);
                    if (sxz < minScale) continue; // 줄여도 필지를 넘친다
                    sxz = Mathf.Min(sxz, maxScale);

                    float stretch = Mathf.Abs(Mathf.Log(height / size.y / sxz));
                    if (stretch > maxLog) continue;

                    float coverage = bx * bz * sxz * sxz / (slotX * slotZ);
                    float score = stretch + (1f - coverage) * 0.5f;
                    candidates.Add((i, q, sxz, score));
                    best = Mathf.Min(best, score);
                }
            }

            if (candidates.Count == 0)
            {
                whiteCityFallback++;
                return null;
            }

            // 최선에 가까운 후보 중에서 고른다 — 같은 건물이 줄줄이 반복되지 않게.
            var near = candidates.FindAll(c => c.score <= best + 0.15f);
            var pick = near[Mathf.Min(near.Count - 1, (int)(rng.Next() * near.Count))];
            var spec = whiteCitySpecs[pick.spec];
            Vector3 s = spec.bounds.size;

            float yaw = pick.quarter * 90f + (rng.Next() < 0.5 ? 180f : 0f);
            var mult = new Vector3(pick.sxz, height / s.y, pick.sxz);

            var root = new GameObject(name);
            var building = PlaceFitted(spec.prefab, spec.bounds, mult, yaw, Vector2.zero, 0f, root.transform, true);
            building.name = spec.prefab.name;
            // 옥상 탐침(RoofProbe)은 BoxCollider를 무시하므로 메시 충돌체를 쓴다. 정적이라 오목해도 된다.
            AddMeshColliders(building, false);

            var meta = root.AddComponent<CityBuilding>();
            meta.height = height;
            meta.floorCount = Mathf.Max(1, Mathf.RoundToInt(height / CityLayoutSettings.FloorHeight));
            meta.footprint = pick.quarter == 0 ? new Vector2(s.x, s.z) * pick.sxz : new Vector2(s.z, s.x) * pick.sxz;
            meta.isShell = true;
            whiteCityBuilt.Add(meta);
            return root;
        }

        // ─── 옥상 소품 ───

        /// <summary>
        /// WhiteCity 건물 옥상에 소품을 흩는다. 기존 CityGenerator와 같은 방식으로 레이를 쏴
        /// 실제 밟히는 면을 찾아 앉힌다. 기본은 충돌체 없음 — 착지할 때 발에 걸리지 않게.
        /// </summary>
        private int CreateRoofProps(Transform parent, int anchorLayer)
        {
            if (whiteCityBuilt.Count == 0 || settings.roofPropPrefabs.Count == 0) return 0;

            var root = new GameObject("_RoofProps");
            root.transform.SetParent(parent, false);

            var specs = new List<(GameObject prefab, Bounds bounds)>();
            foreach (var prefab in settings.roofPropPrefabs)
            {
                if (prefab != null && TryMeasurePrefab(prefab, root.transform, out Bounds b)) specs.Add((prefab, b));
            }
            if (specs.Count == 0) return 0;

            int anchorMask = anchorLayer >= 0 ? (1 << anchorLayer) : ~0;
            int low = Mathf.Max(0, Mathf.Min(settings.roofPropsPerBuilding.x, settings.roofPropsPerBuilding.y));
            int high = Mathf.Max(settings.roofPropsPerBuilding.x, settings.roofPropsPerBuilding.y);
            var rng = new PlanRandom(settings.whiteCitySeed + 17);
            int total = 0, missed = 0;

            foreach (var building in whiteCityBuilt)
            {
                int count = low + (int)(rng.Next() * (high - low + 1));
                float hx = building.footprint.x * 0.5f - settings.roofPropEdgeMargin;
                float hz = building.footprint.y * 0.5f - settings.roofPropEdgeMargin;
                if (hx <= 0f || hz <= 0f) continue;

                for (int i = 0; i < count; i++)
                {
                    var spec = specs[Mathf.Min(specs.Count - 1, (int)(rng.Next() * specs.Count))];
                    var local = new Vector2((float)(rng.Next() * 2.0 - 1.0) * hx, (float)(rng.Next() * 2.0 - 1.0) * hz);
                    float yaw = (float)rng.Next() * 360f;

                    Vector3 origin = building.transform.TransformPoint(
                        new Vector3(local.x, building.height + RoofProbe.Margin, local.y));
                    if (!RoofProbe.TryFindWalkableSurface(origin, RoofProbe.Margin * 2f, building.transform, anchorMask,
                                                          out Vector3 spot))
                    {
                        missed++;
                        continue;
                    }

                    var prop = InstantiateLinked(spec.prefab);
                    prop.transform.SetParent(root.transform, false);
                    prop.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    prop.transform.position = spot - Vector3.up * spec.bounds.min.y;
                    if (settings.roofPropColliders) AddMeshColliders(prop, true);
                    total++;
                }
            }

            if (missed > 0)
                Debug.LogWarning($"[CityPlanGenerator] 옥상 소품 {missed}개는 옥상 면을 못 찾아 건너뛰었다.", this);
            return total;
        }

        // ─── 도로 소품 ───

        /// <summary>
        /// 가로등(애비뉴 인도), 신호등(교차로 모서리), 표지판 갠트리(중앙 직선). 프리팹 칸이 빈 것은 건너뛴다.
        /// 광장·공원·고가 구조물 아래는 피한다. 기본은 앵커가 아니다 — 지면 가까이서 웹이 걸리지 않게.
        /// 소품은 피벗을 기준으로 세운다: 가로등은 팔이 바운즈를 한쪽으로 늘여서 바운즈 중심이 기둥이 아니다.
        /// </summary>
        private int CreateStreetProps(Transform parent, int anchorLayer)
        {
            if (settings.streetLightPrefab == null && settings.trafficLightPrefab == null && settings.gantryPrefab == null)
                return 0;

            var root = new GameObject("_StreetProps");
            root.transform.SetParent(parent, false);
            int layer = settings.streetPropsAnchorable ? anchorLayer : -1;
            var avenues = settings.SortedAvenues();
            var streets = settings.SortedStreets();
            int count = 0;

            bool Blocked(Vector2 p, float margin)
            {
                foreach (var plaza in settings.plazas)
                {
                    if (plaza != null && Vector2.Distance(p, plaza.center) < plaza.radius + margin) return true;
                }
                if (settings.park.Contains(p)) return true;
                var deck = settings.railDeck;
                return p.x > deck.x0 - margin && p.x < deck.x1 + margin && p.y > deck.z0 - margin && p.y < deck.z1 + margin;
            }

            void Finish(GameObject go)
            {
                AddMeshColliders(go, false);
                if (layer >= 0) SetLayerRecursive(go, layer);
                count++;
            }

            // 가로등 — 인도 띠 안쪽(차도 쪽) 가장자리. 팔이 차도를 보게 서쪽 인도는 +X, 동쪽 인도는 −X를 향한다.
            if (TryMeasurePrefab(settings.streetLightPrefab, root.transform, out Bounds lb) && lb.size.y > 0.01f)
            {
                var mult = Vector3.one * (settings.streetLightHeight / lb.size.y);
                float spacing = Mathf.Max(4f, settings.streetLightSpacing);
                foreach (var a in avenues)
                {
                    float lateral = a.width * 0.5f - settings.sidewalkStripWidth + 0.6f;
                    foreach (var row in settings.BlockRows())
                    {
                        for (float z = row.x + spacing * 0.5f; z < row.y - 1f; z += spacing)
                        {
                            for (int side = -1; side <= 1; side += 2)
                            {
                                var p = new Vector2(a.centerX + side * lateral, z);
                                if (Blocked(p, 2f)) continue;
                                float yaw = (side > 0 ? 270f : 90f) + settings.streetLightYawOffset;
                                Finish(PlaceFitted(settings.streetLightPrefab, lb, mult, yaw, p, 0f, root.transform, false));
                            }
                        }
                    }
                }
            }

            // 신호등 — 교차로마다 대각 두 모서리의 애비뉴 인도. 교차로 중심을 보게 돌린다.
            if (TryMeasurePrefab(settings.trafficLightPrefab, root.transform, out Bounds tb) && tb.size.y > 0.01f)
            {
                var mult = Vector3.one * (settings.trafficLightHeight / tb.size.y);
                foreach (var a in avenues)
                {
                    foreach (var st in streets)
                    {
                        var c = new Vector2(a.centerX, st.centerZ);
                        if (Blocked(c, 0f)) continue;
                        for (int k = 0; k < 2; k++)
                        {
                            float sign = k == 0 ? 1f : -1f;
                            var p = new Vector2(a.centerX + sign * (a.width * 0.5f - 1.2f),
                                                st.centerZ - sign * (st.width * 0.5f + 1.2f));
                            if (Blocked(p, 1f)) continue;
                            Vector2 look = c - p;
                            float yaw = Mathf.Atan2(look.x, look.y) * Mathf.Rad2Deg + settings.trafficLightYawOffset;
                            Finish(PlaceFitted(settings.trafficLightPrefab, tb, mult, yaw, p, 0f, root.transform, false));
                        }
                    }
                }
            }

            // 표지판 갠트리 — 지정한 애비뉴를 가로지른다. 폭은 애비뉴에 맞춰 균일 배율, 높이만 따로 맞춘다.
            var gantryAvenue = avenues.Find(a => a.id == settings.gantryAvenue);
            if (gantryAvenue != null && TryMeasurePrefab(settings.gantryPrefab, root.transform, out Bounds gb))
            {
                bool spanX = gb.size.x >= gb.size.z;
                float span = spanX ? gb.size.x : gb.size.z;
                if (span > 0.01f && gb.size.y > 0.01f)
                {
                    float sxz = (gantryAvenue.width - 1f) / span;
                    var mult = new Vector3(sxz, settings.gantryHeight / gb.size.y, sxz);
                    float yaw = (spanX ? 0f : 90f) + settings.gantryYawOffset;
                    foreach (float z in settings.gantryZ)
                    {
                        var p = new Vector2(gantryAvenue.centerX, z);
                        if (Blocked(p, 4f)) continue;
                        Finish(PlaceFitted(settings.gantryPrefab, gb, mult, yaw, p, 0f, root.transform, true));
                    }
                }
            }

            return count;
        }

        // ─── 고가 구조물 ───

        /// <summary>
        /// 상판 프리팹을 고가 상판 자리에 이어 깐다. 폭은 상판 폭에 맞춘 균일 배율이고, 진행 방향만
        /// 살짝 늘여 조각 수가 딱 떨어지게 한다. 밟히는 면 높이는 레이로 재서 railDeckTop에 맞춘다.
        /// </summary>
        /// <param name="deckBottom">상판 밑면 높이. 기둥 높이를 여기에 맞춘다.</param>
        /// <param name="kitScale">상판에 쓴 배율. 기둥도 같은 배율을 써서 키트 비율을 지킨다.</param>
        private bool TryLayElevatedDeck(Transform parent, int layer, ref float deckBottom, ref float kitScale)
        {
            var prefab = settings.elevatedDeckPrefab;
            if (prefab == null || !TryMeasurePrefab(prefab, parent, out Bounds b)) return false;

            var deck = settings.railDeck;
            float width = deck.x1 - deck.x0;
            float length = deck.z1 - deck.z0;
            float yawOffset = settings.elevatedDeckYawOffset;
            // 90°·270°로 돌리면 프리팹 로컬 X가 진행 방향(월드 Z)이 된다.
            bool swap = Mathf.Abs(Mathf.Repeat(yawOffset, 180f) - 90f) < 1f;
            float across = swap ? b.size.z : b.size.x;
            float along = swap ? b.size.x : b.size.z;
            if (across < 0.01f || along < 0.01f || width <= 0f || length <= 0f) return false;

            float s = width / across;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / (along * s)));
            float sAlong = length / (count * along);
            var mult = swap ? new Vector3(sAlong, s, s) : new Vector3(s, s, sAlong);

            float surface = ProbeSurface(prefab, new Vector2(b.center.x, b.center.z), parent, out float hit, out _) ? hit : b.max.y;
            float pivotY = settings.railDeckTop - surface * s;
            deckBottom = pivotY + b.min.y * s;
            kitScale = s;

            float tile = length / count;
            for (int i = 0; i < count; i++)
            {
                var at = new Vector2(deck.Center.x, deck.z0 + tile * (i + 0.5f));
                var go = PlaceFitted(prefab, b, mult, yawOffset, at, deckBottom, parent, true);
                go.name = $"Deck_{i:00}";
                AddMeshColliders(go, false);
                if (layer >= 0) SetLayerRecursive(go, layer);
            }
            return true;
        }

        /// <summary>기둥 프리팹 하나. 굵기는 상판과 같은 키트 배율(없으면 railPillarSize), 높이는 상판 밑면까지 늘인다.</summary>
        private void PlaceElevatedPillar(Transform parent, Bounds b, Vector2 at, float top, float kitScale, int layer, int index)
        {
            float footprint = Mathf.Max(b.size.x, b.size.z);
            if (footprint < 0.01f || b.size.y < 0.01f || top <= 0f) return;

            float deckWidth = settings.railDeck.x1 - settings.railDeck.x0;
            float sxz = kitScale > 0f ? Mathf.Min(kitScale, deckWidth * 0.8f / footprint) : settings.railPillarSize / footprint;
            var mult = new Vector3(sxz, top / b.size.y, sxz);

            var go = PlaceFitted(settings.elevatedPillarPrefab, b, mult, 0f, at, 0f, parent, true);
            go.name = $"Pillar_{index:00}";
            AddMeshColliders(go, false);
            if (layer >= 0) SetLayerRecursive(go, layer);
        }

        // ─── 공원 나무 크기 ───

        /// <summary>나무 프리팹을 treeHeight에 맞출 배율을 한 번만 잰다. Kenney 나무처럼 작은 모델용.</summary>
        private void PrepareTreeFit(Transform probeParent)
        {
            treeScale = 1f;
            treeLift = 0f;
            if (settings.treePrefab == null || !settings.treeFitHeight) return;
            if (!TryMeasurePrefab(settings.treePrefab, probeParent, out Bounds b) || b.size.y < 0.01f) return;
            treeScale = settings.treeHeight / b.size.y;
            treeLift = -b.min.y * treeScale;
        }

        // ─── 지상 잡동사니 ───

        /// <summary>
        /// 부두(잔교 위 한 줄 + 뒤쪽 하역장)와 블록 안쪽 빈 마당에 소품을 놓는다.
        /// 스윙 고도에서 내려다볼 때 바닥이 비어 보이는 것을 막는 용도라 앵커로 두지 않는다.
        /// 충돌체는 단다 — 트럭·컨테이너 위에 착지할 수 있어야 자연스럽다.
        /// </summary>
        private int CreateClutter(Transform parent, CityPlanLayout layout)
        {
            var root = new GameObject("_Clutter");
            root.transform.SetParent(parent, false);
            var dock = MeasureProps(settings.dockProps, root.transform);
            var yard = MeasureProps(settings.yardProps, root.transform);
            if (dock.Count == 0 && yard.Count == 0)
            {
                DestroyObject(root);
                return 0;
            }

            var rng = new PlanRandom(settings.clutterSeed);
            var placed = new List<Vector3>(); // (x, z, 반지름) — 소품끼리 겹침 검사
            int count = 0;

            if (dock.Count > 0)
            {
                foreach (var pier in settings.piers)
                {
                    if (pier == null) continue;
                    count += FillRow(pier, dock, rng, root.transform, placed);
                    var quay = QuayYard(pier);
                    if (quay != null) count += Scatter(quay, dock, rng, 4, root.transform, placed, null);
                }
            }

            if (yard.Count > 0)
            {
                Vector2 from = settings.diagonalFrom, to = settings.diagonalTo;
                float length = Vector2.Distance(from, to);
                Vector2 dir = length > 0.01f ? (to - from) / length : Vector2.up;

                bool YardBlocked(Vector2 p, float r)
                {
                    Vector2 rel = p - from;
                    float s = Vector2.Dot(rel, dir);
                    float d = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                    if (length > 0.01f && s > -r && s < length + r && d < settings.diagonalBandOuter + r) return true;
                    foreach (var plaza in settings.plazas)
                    {
                        if (plaza != null && Vector2.Distance(p, plaza.center) < plaza.radius + r) return true;
                    }
                    foreach (var lm in settings.landmarks)
                    {
                        if (lm == null) continue;
                        var a = lm.area;
                        if (p.x > a.x0 - r && p.x < a.x1 + r && p.y > a.z0 - r && p.y < a.z1 + r) return true;
                    }
                    return false;
                }

                float depth = settings.frontageDepth;
                foreach (var b in layout.blocks)
                {
                    if (b.x1 - b.x0 < depth * 2f + 4f || b.z1 - b.z0 < depth * 2f + 4f) continue;
                    var court = new PlanRect(b.x0 + depth + 1.5f, b.x1 - depth - 1.5f, b.z0 + depth + 1.5f, b.z1 - depth - 1.5f);
                    count += Scatter(court, yard, rng, settings.yardPropsPerBlock, root.transform, placed, YardBlocked);
                }
            }

            return count;
        }

        private List<PropSpec> MeasureProps(List<PlanProp> list, Transform probeParent)
        {
            var specs = new List<PropSpec>();
            foreach (var p in list)
            {
                if (p == null || p.prefab == null || p.weight <= 0f) continue;
                if (!TryMeasurePrefab(p.prefab, probeParent, out Bounds b)) continue;
                float longest = Mathf.Max(b.size.x, b.size.z);
                if (longest < 0.001f) continue;
                float s = p.length > 0f ? p.length / longest : 1f;
                specs.Add(new PropSpec
                {
                    prefab = p.prefab,
                    bounds = b,
                    scale = s,
                    weight = p.weight,
                    size = new Vector2(b.size.x * s, b.size.z * s),
                });
            }
            return specs;
        }

        private static int PickWeighted(List<PropSpec> specs, PlanRandom rng)
        {
            float total = 0f;
            foreach (var s in specs) total += s.weight;
            float r = (float)rng.Next() * total;
            for (int i = 0; i < specs.Count; i++)
            {
                r -= specs[i].weight;
                if (r <= 0f) return i;
            }
            return specs.Count - 1;
        }

        private static bool OverlapsPlaced(List<Vector3> placed, Vector2 p, float r)
        {
            foreach (var q in placed)
            {
                float dx = p.x - q.x, dz = p.y - q.y, rr = r + q.z;
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        /// <summary>잔교 위에 긴 방향으로 한 줄. 소품의 긴 변을 잔교 방향에 맞춘다.</summary>
        private int FillRow(PlanRect area, List<PropSpec> specs, PlanRandom rng, Transform parent, List<Vector3> placed)
        {
            bool alongX = area.x1 - area.x0 >= area.z1 - area.z0;
            float length = alongX ? area.x1 - area.x0 : area.z1 - area.z0;
            float width = alongX ? area.z1 - area.z0 : area.x1 - area.x0;
            float cursor = 1f;
            int done = 0;

            while (cursor < length - 1f)
            {
                if (rng.Next() < settings.dockPropSkipChance)
                {
                    cursor += 3f;
                    continue;
                }

                var spec = specs[PickWeighted(specs, rng)];
                bool longIsX = spec.size.x >= spec.size.y;
                float len = Mathf.Max(spec.size.x, spec.size.y);
                float wid = Mathf.Min(spec.size.x, spec.size.y);
                if (wid > width - 0.6f || cursor + len > length - 1f)
                {
                    cursor += 1f;
                    continue;
                }

                float along = cursor + len * 0.5f;
                float lat = (float)(rng.Next() * 2.0 - 1.0) * (width - wid) * 0.3f;
                var p = alongX ? new Vector2(area.x0 + along, area.Center.y + lat)
                               : new Vector2(area.Center.x + lat, area.z0 + along);
                float yaw = (longIsX == alongX ? 0f : 90f) + (rng.Next() < 0.5 ? 180f : 0f);

                var go = PlaceFitted(spec.prefab, spec.bounds, Vector3.one * spec.scale, yaw, p, 0f, parent, true);
                AddMeshColliders(go, false);
                placed.Add(new Vector3(p.x, p.y, len * 0.5f));
                done++;
                cursor += len + settings.dockPropGap;
            }
            return done;
        }

        /// <summary>구역 안에 무작위로 흩는다. 다른 소품·금지 구역과 겹치면 다시 뽑는다.</summary>
        private int Scatter(PlanRect area, List<PropSpec> specs, PlanRandom rng, int target, Transform parent,
                            List<Vector3> placed, Func<Vector2, float, bool> blocked)
        {
            int done = 0;
            for (int attempt = 0; attempt < target * 8 && done < target; attempt++)
            {
                var spec = specs[PickWeighted(specs, rng)];
                bool turn = rng.Next() < 0.5;
                float hx = (turn ? spec.size.y : spec.size.x) * 0.5f;
                float hz = (turn ? spec.size.x : spec.size.y) * 0.5f;
                if (area.x1 - area.x0 < hx * 2f || area.z1 - area.z0 < hz * 2f) continue;

                var p = new Vector2(Mathf.Lerp(area.x0 + hx, area.x1 - hx, (float)rng.Next()),
                                    Mathf.Lerp(area.z0 + hz, area.z1 - hz, (float)rng.Next()));
                float r = Mathf.Sqrt(hx * hx + hz * hz);
                if (blocked != null && blocked(p, r)) continue;
                if (OverlapsPlaced(placed, p, r)) continue;

                float yaw = (turn ? 90f : 0f) + (rng.Next() < 0.5 ? 180f : 0f);
                var go = PlaceFitted(spec.prefab, spec.bounds, Vector3.one * spec.scale, yaw, p, 0f, parent, true);
                AddMeshColliders(go, false);
                placed.Add(new Vector3(p.x, p.y, r));
                done++;
            }
            return done;
        }

        /// <summary>잔교 뒤 산책로의 하역장 자리. 서쪽 강변이면 산책로 안쪽 12 m, 남쪽이면 그 위쪽 12 m.</summary>
        private PlanRect QuayYard(PlanRect pier)
        {
            var land = settings.land;
            Vector2 c = pier.Center;
            if (pier.x1 <= land.x0 + 0.01f) return new PlanRect(land.x0 + 2f, land.x0 + 14f, c.y - 14f, c.y + 14f);
            if (pier.z1 <= land.z0 + 0.01f) return new PlanRect(c.x - 14f, c.x + 14f, land.z0 + 2f, land.z0 + 14f);
            return null;
        }
    }
}
