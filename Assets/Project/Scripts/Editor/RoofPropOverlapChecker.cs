using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Momentum.Level.EditorTools
{
    /// <summary>
    /// 옥상 소품·난간이 자기 건물이 아닌 <b>다른</b> 건물(주로 옆 건물의 벽)과 겹쳐 있는지
    /// 찾는다.
    ///
    /// <see cref="CityGenerator"/>의 <c>CreateRoofProps</c>/<c>CreateRoofRails</c>는 소품을
    /// 놓을 때 자기 건물 발자국(footprint) 기준으로만 여백(roofPropEdgeMargin·
    /// roofRailInset)을 재고, 옆 건물이 얼마나 가까운지는 전혀 모른다 — 건물 사이 간격이
    /// 좁으면(cellJitter가 크거나, 타워 세트백으로 위층이 아래층 옥상 위로 튀어나와
    /// 있으면) 소품이 옆 건물·위층 벽에 박힌 것처럼 보일 수 있다. 손으로 87개 건물을 다
    /// 돌며 눈으로 찾는 대신, 배치된 모든 옥상 소품·난간에 대해 물리 겹침 검사를 자동으로
    /// 돌린다.
    ///
    /// "자기 건물"을 구분하는 방법: 소품 바로 위에서 아래로 쏜 레이캐스트가 처음 맞히는
    /// 건물이 그 소품을 앉힌 건물이다(배치 당시 <c>TryFindRoofSurface</c>가 쓴 것과 같은
    /// 원리). 그 다음 소품의 실제 바운즈로 겹침 검사를 해서, "자기 건물"이 아닌 다른
    /// 건물이 걸리면 보고한다.
    /// </summary>
    public static class RoofPropOverlapChecker
    {
        private const string MenuPath = "Momentum/옥상 소품·난간이 다른 건물과 겹치는지 검사";

        [MenuItem(MenuPath, false, 103)]
        private static void Check()
        {
            int layer = LayerMask.NameToLayer(CityGenerator.AnchorLayerName);
            if (layer < 0)
            {
                Debug.LogWarning($"[RoofPropOverlapChecker] '{CityGenerator.AnchorLayerName}' 레이어가 없다.");
                return;
            }
            int mask = 1 << layer;

            var targets = new List<Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.parent == null) continue;
                if (t.parent.name == "_RoofProps" || t.parent.name == "_RoofRails")
                    targets.Add(t);
            }

            int checkedCount = 0;
            int flaggedCount = 0;

            foreach (var t in targets)
            {
                var renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                checkedCount++;

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                var home = FindHomeBuilding(bounds.center, mask, t);

                // 소품의 실제 바운즈로 겹침 검사 — 0.95를 곱해 딱 맞닿은 정도(발이 살짝
                // 걸치는 정상적인 경우)는 오탐으로 잡지 않는다. 진짜 파고든 경우만 남긴다.
                var hits = Physics.OverlapBox(bounds.center, bounds.extents * 0.95f, t.rotation, mask,
                                               QueryTriggerInteraction.Ignore);

                foreach (var hit in hits)
                {
                    // 자기 자신(난간 조각의 콜라이더 자신)은 겹침이 아니다.
                    if (hit.transform == t || hit.transform.IsChildOf(t)) continue;

                    var hitBuilding = hit.GetComponentInParent<CityBuilding>();
                    if (hitBuilding == null || hitBuilding == home) continue;

                    Debug.LogWarning(
                        $"[RoofPropOverlapChecker] '{t.name}'이(가) 다른 건물('{hitBuilding.name}')과 " +
                        $"겹침 — 자기 건물은 '{(home != null ? home.name : "?")}'. 위치 {t.position}",
                        t.gameObject);
                    flaggedCount++;
                    break;
                }
            }

            Debug.Log($"[RoofPropOverlapChecker] 옥상 소품·난간 {checkedCount}개 검사, " +
                      $"다른 건물과 겹침 {flaggedCount}건. 위 경고 로그를 더블클릭하면 그 오브젝트로 바로 이동한다.");
        }

        /// <summary>
        /// 소품 바로 위에서 아래로 쏴 처음 맞는 건물 — 배치 당시 그 소품이 앉은 건물.
        ///
        /// <paramref name="self"/>를 반드시 받아 그 자신의 콜라이더는 건너뛴다. 난간 조각은
        /// 옥상 표면과 같은 Anchorable 레이어에 있어서(PlaceRailRun), 자기 자신을 걸러내지
        /// 않으면 아래로 쏜 레이가 실제 옥상에 닿기도 전에 난간 자신의 콜라이더부터 맞고,
        /// 거기서는 CityBuilding을 못 찾아 home이 항상 null이 되어 모든 난간이 오탐 처리된다.
        /// </summary>
        private static CityBuilding FindHomeBuilding(Vector3 propCenter, int mask, Transform self)
        {
            Vector3 origin = propCenter + Vector3.up * 20f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 40f, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.transform == self || hit.transform.IsChildOf(self)) continue;
                var building = hit.collider.GetComponentInParent<CityBuilding>();
                if (building != null) return building;
            }
            return null;
        }
    }
}
