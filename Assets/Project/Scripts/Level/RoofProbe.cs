using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 옥상 <b>밟을 수 있는</b> 표면을 레이로 찾는다.
    ///
    /// 건물 메시에는 옥상 바닥·파라펫·실외기가 한 덩어리로 들어 있다.
    /// 위에서 아래로 한 번만 쏘면 돌출물 꼭대기를 먼저 맞는다 — 그게 플레이어 발밑,
    /// 난간 밑면, 소품 밑면이 전부 공중에 뜨는 원인이었다.
    /// BoxCollider는 근사 박스라 제외하고, 메시 히트 중 법선이 위를 향하는 면 가운데
    /// <b>가장 낮은</b> 지점을 고른다. 실외기 꼭대기(28m)와 바닥(26m)이 같이 걸리면
    /// 26m를 쓴다.
    /// </summary>
    public static class RoofProbe
    {
        public const float Margin = 4f;

        /// <summary>이 값 이상이면 거의 수평한 윗면으로 본다.</summary>
        public const float WalkableNormalMinY = 0.85f;

        /// <param name="scope">
        /// 히트를 받아들일 루트. <see cref="Transform.IsChildOf"/>로 걸러낸다.
        /// </param>
        public static bool TryFindWalkableSurface(
            Vector3 origin, float probeDistance, Transform scope, int layerMask, out Vector3 point)
        {
            Physics.SyncTransforms();

            var hits = Physics.RaycastAll(origin, Vector3.down, probeDistance, layerMask,
                                          QueryTriggerInteraction.Ignore);

            float bestWalkableY = float.MaxValue;
            float bestAnyY = float.MaxValue;
            Vector3 bestWalkable = default;
            Vector3 bestAny = default;
            bool foundWalkable = false;
            bool foundAny = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (scope != null && !hit.transform.IsChildOf(scope)) continue;
                if (hit.collider is BoxCollider) continue;

                if (hit.point.y < bestAnyY)
                {
                    bestAnyY = hit.point.y;
                    bestAny = hit.point;
                    foundAny = true;
                }

                if (hit.normal.y < WalkableNormalMinY) continue;
                if (hit.point.y >= bestWalkableY) continue;

                bestWalkableY = hit.point.y;
                bestWalkable = hit.point;
                foundWalkable = true;
            }

            if (foundWalkable)
            {
                point = bestWalkable;
                return true;
            }

            if (foundAny)
            {
                point = bestAny;
                return true;
            }

            point = default;
            return false;
        }

        // 추가 생성 (2026-10-14)
        /// <summary>
        /// 위에서부터 내려가며 옥상을 찾는다. 먼저 위를 보는 면 중 <b>가장 높은</b> 것을 찾고,
        /// 그 아래 <see cref="Margin"/> 안에서 <b>가장 낮은</b> 면(실외기 꼭대기가 아니라 옥상 바닥)을 고른다.
        ///
        /// <see cref="TryFindWalkableSurface"/>는 계획 높이 ±Margin 창 안에서만 찾는다. 안테나·첨탑이 있는
        /// 에셋 건물은 높이가 안테나 끝까지 잡혀 실제 옥상이 그 창 밖에 있어서 못 찾았다.
        /// 이 함수는 건물 높이 전체를 훑으므로 그런 건물이나 계단식 건물 모서리에서도 옥상을 찾는다.
        /// </summary>
        public static bool TryFindRoofFromTop(
            Vector3 origin, float probeDistance, Transform scope, int layerMask, out Vector3 point)
        {
            Physics.SyncTransforms();

            var hits = Physics.RaycastAll(origin, Vector3.down, probeDistance, layerMask,
                                          QueryTriggerInteraction.Ignore);

            // 1) 위를 보는 면 중 가장 높은 것 = 맨 위 옥상
            float topY = float.MinValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Accept(hits[i], scope)) topY = Mathf.Max(topY, hits[i].point.y);
            }

            point = default;
            if (topY == float.MinValue) return false;

            // 2) 그 아래 Margin 안에서 가장 낮은 면 = 실외기·파라펫 위가 아닌 옥상 바닥
            float bestY = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!Accept(hit, scope)) continue;
                if (hit.point.y < topY - Margin || hit.point.y >= bestY) continue;
                bestY = hit.point.y;
                point = hit.point;
                found = true;
            }
            return found;
        }

        // 추가 생성
        /// <summary>
        /// <see cref="TryFindWalkableSurface"/>와 같은 거르기 규칙:
        /// 범위(scope) 밖, 근사 BoxCollider, 거의 수평이 아닌 면은 받지 않는다.
        /// </summary>
        private static bool Accept(RaycastHit hit, Transform scope)
        {
            if (scope != null && !hit.transform.IsChildOf(scope)) return false;
            if (hit.collider is BoxCollider) return false;
            return hit.normal.y >= WalkableNormalMinY;
        }

        /// <summary>정렬된 프리팹/겹 루트의 로컬 Y 높이를 잰다. 실패 시 <paramref name="fallback"/>.</summary>
        public static float MeasureWalkableHeight(Transform scope, Bounds aligned, int layerMask, float fallback)
        {
            Vector3 origin = new Vector3(0f, aligned.max.y + Margin, 0f);
            float distance = aligned.size.y + Margin * 2f;

            if (!TryFindWalkableSurface(origin, distance, scope, layerMask, out Vector3 spot))
            {
                Debug.LogWarning(
                    $"[RoofProbe] '{scope.name}' 중심에서 밟을 옥상을 못 찾아 " +
                    $"전체 높이 {fallback:F2}m로 물러난다.", scope);
                return fallback;
            }

            return Mathf.Max(0f, spot.y);
        }
    }
}
