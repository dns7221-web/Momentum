using System;
using System.Collections.Generic;
using UnityEngine;

namespace Momentum.Level
{
    /// <summary>필지에 무엇을 세울지. 숫자 값이 프리팹 배열 인덱스(셋백 순서)와 같다.</summary>
    public enum PlanLotKind
    {
        Large = 0,
        Medium = 1,
        Small = 2,
        Greybox = 3,
    }

    /// <summary>필지가 블록의 어느 변에 붙어 있는지 = 건물 정면이 어느 길을 보는지.</summary>
    public enum PlanLotSide
    {
        West,
        East,
        South,
        North,
    }

    /// <summary>
    /// 웹 배치도(JavaScript)와 <b>같은 수열</b>을 내는 난수기 (mulberry32).
    ///
    /// <see cref="System.Random"/>이나 <see cref="UnityEngine.Random"/>을 쓰지 않은 이유:
    /// 기획 문서의 배치도와 유니티 결과가 필지 단위로 일치해야 "R2C3-W04가 이상하다" 같은
    /// 피드백을 그대로 코드 위치로 옮길 수 있다. 두 언어에서 비트 단위로 같은 결과를 내는
    /// 가장 짧은 알고리즘이 mulberry32다. UnityEngine.Random은 전역 상태라 다른 시스템의
    /// 난수 소비 순서에 따라 결과가 바뀌는 문제도 있다.
    /// </summary>
    public sealed class PlanRandom
    {
        private uint state;

        public PlanRandom(int seed)
        {
            state = unchecked((uint)seed);
        }

        /// <summary>[0, 1) 실수.</summary>
        public double Next()
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    /// <summary>도로 사이 사각형 하나.</summary>
    public struct PlanBlock
    {
        public string id;
        public int row;
        public int col;
        public float x0, x1, z0, z1;

        public Vector2 Center => new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
    }

    /// <summary>블록 둘레의 필지 하나와 거기 세울 건물 사양.</summary>
    public struct PlanLot
    {
        public string id;
        public int row;
        public int col;
        public PlanLotSide side;
        public float x0, x1, z0, z1;
        public PlanLotKind kind;
        /// <summary>프리팹 적층 겹 수 (2~4). 회색 박스는 0.</summary>
        public int tiers;
        /// <summary>실제로 설 높이. 프리팹이면 겹 높이 합, 회색 박스면 3 m 단위로 맞춘 값.</summary>
        public float height;

        public Vector2 Center => new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
    }

    /// <summary>사선 대로를 따라 세우는 회색 박스 한 채.</summary>
    public struct PlanDiagonalWall
    {
        public string id;
        public Vector2 center;
        /// <summary>+Z에서 잰 회전각. 로컬 Z가 사선 방향을 보게 된다.</summary>
        public float yawDeg;
        /// <summary>사선 방향 길이.</summary>
        public float length;
        /// <summary>사선에 수직인 깊이.</summary>
        public float depth;
        public float height;
    }

    /// <summary>
    /// <see cref="CityPlanSettings"/>에서 블록·필지·사선 가로벽을 <b>계산만</b> 하는 클래스.
    ///
    /// 오브젝트를 만들지 않는 순수 C#으로 분리한 이유는 세 가지다.
    /// 1) 생성 버튼을 누르기 전에도 기즈모와 인스펙터가 같은 결과를 미리 보여줄 수 있다.
    /// 2) 수천 개 오브젝트를 만들지 않고도 "필지가 몇 개, 회색 박스가 몇 개"를 바로 셀 수 있다.
    /// 3) 규칙 검증(웹 배치도와 같은 결과인지)을 씬 없이 할 수 있다.
    ///
    /// 계산 순서(블록 → 서쪽 변 → 동쪽 변 → 남 → 북, 그다음 사선 가로벽)와 난수 소비 순서가
    /// 웹 배치도와 같아야 같은 필지가 나온다. 순서를 바꾸면 결과 전체가 달라진다.
    /// </summary>
    public sealed class CityPlanLayout
    {
        public readonly List<PlanBlock> blocks = new List<PlanBlock>();
        public readonly List<PlanLot> lots = new List<PlanLot>();
        public readonly List<PlanDiagonalWall> diagonalWalls = new List<PlanDiagonalWall>();

        /// <summary>프리팹 종류별 2·3·4겹 높이. 프리팹이 없으면 null이고 그 필지는 회색 박스가 된다.</summary>
        public readonly float[][] stacks = new float[3][];

        private CityPlanSettings s;
        private PlanRandom jitter;
        private Vector2 diagFrom;
        private Vector2 diagDir;
        private Vector2 diagNormal;
        private float diagLength;

        /// <param name="prefabHeights">Large, Medium, Small 한 겹 실측 높이. 없으면 0 이하.</param>
        public static CityPlanLayout Build(CityPlanSettings settings, float[] prefabHeights)
        {
            var layout = new CityPlanLayout();
            if (settings == null) return layout;
            layout.Run(settings, prefabHeights);
            return layout;
        }

        private void Run(CityPlanSettings settings, float[] prefabHeights)
        {
            s = settings;
            ComputeStacks(prefabHeights);

            diagFrom = s.diagonalFrom;
            Vector2 span = s.diagonalTo - s.diagonalFrom;
            diagLength = Mathf.Max(0.001f, span.magnitude);
            diagDir = span / diagLength;
            // 진행 방향의 왼쪽 법선. 웹 배치도의 (−uz, ux)와 같다.
            diagNormal = new Vector2(-diagDir.y, diagDir.x);

            var cols = s.BlockColumns();
            var rows = s.BlockRows();
            var pick = new PlanRandom(s.lotSeed);
            jitter = new PlanRandom(s.jitterSeed);
            float[] widths = { s.lotWidthLarge, s.lotWidthMedium, s.lotWidthSmall };
            float depth = s.frontageDepth;

            for (int ri = 0; ri < rows.Count; ri++)
            {
                for (int ci = 0; ci < cols.Count; ci++)
                {
                    float x0 = cols[ci].x, x1 = cols[ci].y;
                    float z0 = rows[ri].x, z1 = rows[ri].y;
                    var block = new PlanBlock { id = $"R{ri + 1}C{ci + 1}", row = ri, col = ci, x0 = x0, x1 = x1, z0 = z0, z1 = z1 };

                    // 공원 자리는 블록이 아니다. 블록 중심이 공원 안이면 통째로 뺀다.
                    if (s.park.Contains(block.Center)) continue;
                    blocks.Add(block);

                    // 블록 폭이 필지 두 겹보다 좁으면 둘레를 채울 수 없다.
                    if (x1 - x0 < depth * 2f + 1f) continue;

                    float length = z1 - z0;

                    // 애비뉴 면(긴 변) — 서쪽 변을 끝까지 끊은 뒤 동쪽 변. 이 순서가 난수 소비 순서다.
                    for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                    {
                        var side = sideIndex == 0 ? PlanLotSide.West : PlanLotSide.East;
                        float along = 0f;
                        int number = 1;
                        while (along < length - 0.01f)
                        {
                            float w = widths[(int)Math.Floor(pick.Next() * 3.0)];
                            // 남은 길이가 가장 좁은 필지보다 짧아지면 마지막 필지가 전부 가져간다.
                            if (length - along - w < s.minRemainder) w = length - along;

                            float lx0 = side == PlanLotSide.West ? x0 : x1 - depth;
                            TryAddLot(block, side, number++, lx0, lx0 + depth, z0 + along, z0 + along + w, KindForWidth(w));
                            along += w;
                        }
                    }

                    // 스트리트 면(짧은 변) — 블록 폭 60에서 양쪽 필지 20씩 빼면 20 m, Large_2가 그대로 들어간다.
                    TryAddLot(block, PlanLotSide.South, 1, x0 + depth, x1 - depth, z0, z0 + depth, PlanLotKind.Large);
                    TryAddLot(block, PlanLotSide.North, 1, x0 + depth, x1 - depth, z1 - depth, z1, PlanLotKind.Large);
                }
            }

            BuildDiagonalWalls();
        }

        /// <summary>
        /// 셋백 조합 높이표. Large 밑칸이면 Large → Medium → Small → Small 순으로 쌓는다.
        /// 기존 <see cref="CityLayoutSettings.TierPrefab"/>과 같은 규칙(좁은 쪽으로 한 칸씩, 다 쓰면 가장 좁은 것 반복)이다.
        /// </summary>
        private void ComputeStacks(float[] heights)
        {
            for (int k = 0; k < 3; k++)
            {
                stacks[k] = null;
                if (heights == null || heights.Length < 3) continue;

                var stack = new float[3];
                bool valid = true;
                for (int tiers = 2; tiers <= 4 && valid; tiers++)
                {
                    float sum = 0f;
                    for (int t = 0; t < tiers; t++)
                    {
                        float h = heights[Mathf.Min(k + t, 2)];
                        if (h <= 0f) { valid = false; break; }
                        sum += h;
                    }
                    stack[tiers - 2] = sum;
                }
                if (valid) stacks[k] = stack;
            }
        }

        private PlanLotKind KindForWidth(float w)
        {
            if (Mathf.Abs(w - s.lotWidthLarge) < 0.01f) return PlanLotKind.Large;
            if (Mathf.Abs(w - s.lotWidthMedium) < 0.01f) return PlanLotKind.Medium;
            if (Mathf.Abs(w - s.lotWidthSmall) < 0.01f) return PlanLotKind.Small;
            return PlanLotKind.Greybox;
        }

        /// <summary>필지 하나를 거르기 규칙에 통과시킨 뒤 높이와 에셋을 정해 넣는다.</summary>
        private void TryAddLot(PlanBlock block, PlanLotSide side, int number,
                               float x0, float x1, float z0, float z1, PlanLotKind wanted)
        {
            if (IntersectsDiagonalBand(x0, x1, z0, z1)) return;

            var center = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            foreach (var plaza in s.plazas)
            {
                if (plaza != null && Vector2.Distance(center, plaza.center) < s.plazaLotClearance) return;
            }
            foreach (var landmark in s.landmarks)
            {
                if (landmark != null && landmark.area.Overlaps(x0, x1, z0, z1)) return;
            }

            // 흔들기 난수는 거르기를 통과한 필지에서만 소비한다 (웹 배치도와 같은 순서).
            float h = s.HeightAt(center) + (float)(jitter.Next() * 2.0 * s.heightJitter - s.heightJitter);
            Assign(wanted, h, out var kind, out int tiers, out float height);

            char sideChar = side == PlanLotSide.West ? 'W' : side == PlanLotSide.East ? 'E' : side == PlanLotSide.South ? 'S' : 'N';
            lots.Add(new PlanLot
            {
                id = $"{block.id}-{sideChar}{number:00}",
                row = block.row,
                col = block.col,
                side = side,
                x0 = x0, x1 = x1, z0 = z0, z1 = z1,
                kind = kind,
                tiers = tiers,
                height = height,
            });
        }

        /// <summary>
        /// 목표 높이에 가장 가까운 겹 수를 고른다. 프리팹 4겹으로도 모자라면 회색 박스로 바꾼다
        /// — 봉우리 근처를 프리팹 상한(87 m)에 눌러두면 높이 지형이 평평하게 잘린다.
        /// </summary>
        private void Assign(PlanLotKind wanted, float h, out PlanLotKind kind, out int tiers, out float height)
        {
            float[] stack = wanted == PlanLotKind.Greybox ? null : stacks[(int)wanted];

            if (stack == null)
            {
                kind = PlanLotKind.Greybox;
                tiers = 0;
                height = RoundStep(Mathf.Max(s.minHeight, h), s.greyboxHeightStep);
                return;
            }

            if (h > stack[stack.Length - 1] + s.greyboxOverMax)
            {
                kind = PlanLotKind.Greybox;
                tiers = 0;
                height = RoundStep(h, s.greyboxHeightStep);
                return;
            }

            int best = 0;
            for (int i = 1; i < stack.Length; i++)
            {
                if (Mathf.Abs(stack[i] - h) < Mathf.Abs(stack[best] - h)) best = i;
            }
            kind = wanted;
            tiers = best + 2;
            height = stack[best];
        }

        /// <summary>
        /// 반올림. Mathf.Round는 .5에서 짝수 쪽으로 가는 은행원 반올림이라
        /// JavaScript Math.round(.5는 올림)와 결과가 달라진다. 웹 배치도와 맞추려고 직접 쓴다.
        /// </summary>
        private static float RoundStep(float value, float step)
        {
            if (step <= 0f) return value;
            return (float)(Math.Floor(value / step + 0.5) * step);
        }

        /// <summary>
        /// 필지가 사선 띠(중심선 ± diagonalBandOuter)에 걸리는지.
        /// 네 모서리 중 하나라도 띠 안이거나, 모서리들이 중심선 양쪽에 걸쳐 있으면 걸린 것이다.
        /// </summary>
        private bool IntersectsDiagonalBand(float x0, float x1, float z0, float z1)
        {
            bool anyInside = false, anyPositive = false, anyNegative = false, anyAlong = false;
            Span<Vector2> corners = stackalloc Vector2[4];
            corners[0] = new Vector2(x0, z0);
            corners[1] = new Vector2(x1, z0);
            corners[2] = new Vector2(x0, z1);
            corners[3] = new Vector2(x1, z1);

            for (int i = 0; i < 4; i++)
            {
                Vector2 rel = corners[i] - diagFrom;
                float t = Vector2.Dot(rel, diagDir) / diagLength;
                float d = Vector2.Dot(rel, diagNormal);

                if (t > -0.03f && t < 1.03f && Mathf.Abs(d) < s.diagonalBandOuter) anyInside = true;
                if (d > 0f) anyPositive = true;
                if (d < 0f) anyNegative = true;
                if (t > 0f && t < 1f) anyAlong = true;
            }
            return anyInside || (anyPositive && anyNegative && anyAlong);
        }

        /// <summary>
        /// 사선 대로 양옆에 회색 박스를 일정 간격으로 세운다.
        /// 사선이 블록을 자르고 지나가면 잘린 면에 벽이 없어서 R-B(사선 내리막) 경로에서
        /// 웹을 걸 곳이 사라진다. 프리팹은 축 정렬 바닥이라 사선 면을 못 채우므로 회색 박스로 채운다.
        /// </summary>
        private void BuildDiagonalWalls()
        {
            float yaw = Mathf.Atan2(diagDir.x, diagDir.y) * Mathf.Rad2Deg;
            int number = 1;

            for (float along = s.diagonalWallStart; along < diagLength; along += s.diagonalWallSpacing)
            {
                for (int k = 0; k < 2; k++)
                {
                    float side = k == 0 ? -1f : 1f;
                    Vector2 c = diagFrom + diagDir * along + diagNormal * (s.diagonalWallOffset * side);

                    if (!InsideAnyBlock(c, 4f)) continue;
                    if (NearPlaza(c, s.plazaDiagonalClearance)) continue;
                    if (NearLandmark(c, s.landmarkDiagonalClearance)) continue;

                    float h = s.HeightAt(c) + (float)(jitter.Next() * 2.0 * s.heightJitter - s.heightJitter);
                    diagonalWalls.Add(new PlanDiagonalWall
                    {
                        id = $"D1-{(side < 0f ? 'L' : 'R')}{number:00}",
                        center = c,
                        yawDeg = yaw,
                        length = s.diagonalWallLength,
                        depth = s.diagonalWallDepth,
                        height = RoundStep(Mathf.Max(s.minHeight, h), s.greyboxHeightStep),
                    });
                }
                number++;
            }
        }

        private bool InsideAnyBlock(Vector2 p, float margin)
        {
            foreach (var b in blocks)
            {
                if (p.x > b.x0 + margin && p.x < b.x1 - margin && p.y > b.z0 + margin && p.y < b.z1 - margin) return true;
            }
            return false;
        }

        private bool NearPlaza(Vector2 p, float clearance)
        {
            foreach (var plaza in s.plazas)
            {
                if (plaza != null && Vector2.Distance(p, plaza.center) < clearance) return true;
            }
            return false;
        }

        private bool NearLandmark(Vector2 p, float clearance)
        {
            foreach (var landmark in s.landmarks)
            {
                if (landmark == null) continue;
                var a = landmark.area;
                if (p.x > a.x0 - clearance && p.x < a.x1 + clearance && p.y > a.z0 - clearance && p.y < a.z1 + clearance) return true;
            }
            return false;
        }

        // ─── 집계 (인스펙터·로그용) ───

        public int CountKind(PlanLotKind kind)
        {
            int n = 0;
            foreach (var lot in lots) if (lot.kind == kind) n++;
            return n;
        }

        public float TallestLot()
        {
            float max = 0f;
            foreach (var lot in lots) max = Mathf.Max(max, lot.height);
            foreach (var w in diagonalWalls) max = Mathf.Max(max, w.height);
            return max;
        }
    }
}
