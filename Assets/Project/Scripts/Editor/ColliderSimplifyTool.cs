using UnityEditor;
using UnityEngine;

namespace Momentum.Level.EditorTools
{
    /// <summary>
    /// 선택한 오브젝트 아래의 MeshCollider들을 BoxCollider 하나로 합친다.
    ///
    /// 동적 Rigidbody에는 Mesh Collider보다 프리미티브 콜라이더(Box 등)를 쓰는 게 훨씬
    /// 싸다는 게 유니티 물리 최적화에서 가장 잘 알려진 원칙이다. 화물 컨테이너처럼
    /// 부품별(문·측면·프레임)로 MeshCollider를 따로 붙인 복합 콜라이더는, 접지면이
    /// 부품 경계마다 완벽히 평평하게 안 맞아 미세하게 계속 흔들리며 OnCollisionEnter가
    /// 반복 재발동하는 문제까지 겸해서 낼 수 있다 — 콜라이더가 하나면 접촉면도
    /// 하나뿐이라 흔들릴 수가 없다.
    ///
    /// 치수를 손으로 재서 넣지 않는 이유는 <see cref="BuildingPrefabTool"/>과 같다 —
    /// 눈대중은 반드시 어긋난다. <see cref="Collider.bounds"/>가 이미 정확한 월드 AABB를
    /// 들고 있으니 그걸 그대로 합쳐 되돌린다.
    /// </summary>
    public static class ColliderSimplifyTool
    {
        private const string MenuPath = "Momentum/선택한 오브젝트 콜라이더를 BoxCollider 하나로 합치기";

        [MenuItem(MenuPath, false, 102)]
        private static void Simplify()
        {
            var targets = Selection.gameObjects;
            if (targets.Length == 0)
            {
                Debug.LogWarning("[ColliderSimplifyTool] 씬에서 오브젝트를 선택하고 실행할 것.");
                return;
            }

            int done = 0;
            foreach (var go in targets)
            {
                if (SimplifyOne(go)) done++;
            }

            Debug.Log($"[ColliderSimplifyTool] {done}/{targets.Length}개 처리 완료.");
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.gameObjects.Length > 0;

        private static bool SimplifyOne(GameObject go)
        {
            var meshColliders = go.GetComponentsInChildren<MeshCollider>();
            if (meshColliders.Length == 0)
            {
                Debug.LogWarning($"[ColliderSimplifyTool] '{go.name}' 아래에 MeshCollider가 없어 건너뜀.");
                return false;
            }

            // 월드 공간 AABB를 합친다 — Collider.bounds는 이미 실제 충돌 형태를 반영한
            // 정확한 값이라, 회전·스케일을 우리가 따로 계산할 필요가 없다.
            Bounds worldBounds = meshColliders[0].bounds;
            for (int i = 1; i < meshColliders.Length; i++)
                worldBounds.Encapsulate(meshColliders[i].bounds);

            // 지우기 전에 레이어를 기억해둔다 — 콜라이더가 하위 오브젝트에 있었을 뿐
            // 루트 자체는 다른(혹은 기본) 레이어일 수 있다. 콜라이더가 사라진 뒤에는
            // 루트의 레이어가 곧 이 오브젝트의 판정 레이어가 되므로, 여기서 반드시
            // 맞춰줘야 Grabbable 등 레이어 기반 판정(GrappleGun.IsPropAnchor)이 안 깨진다.
            int layer = meshColliders[0].gameObject.layer;

            Undo.RegisterCompleteObjectUndo(go, "Simplify Colliders");
            foreach (var mc in meshColliders)
                Undo.DestroyObjectImmediate(mc);

            var box = Undo.AddComponent<BoxCollider>(go);
            box.center = go.transform.InverseTransformPoint(worldBounds.center);

            Vector3 lossyScale = go.transform.lossyScale;
            box.size = new Vector3(
                worldBounds.size.x / Mathf.Max(Mathf.Abs(lossyScale.x), 0.0001f),
                worldBounds.size.y / Mathf.Max(Mathf.Abs(lossyScale.y), 0.0001f),
                worldBounds.size.z / Mathf.Max(Mathf.Abs(lossyScale.z), 0.0001f));

            go.layer = layer;

            Debug.Log($"[ColliderSimplifyTool] '{go.name}': MeshCollider {meshColliders.Length}개 → " +
                      $"BoxCollider 1개 (size {box.size}, center {box.center}, layer {layer}).");
            return true;
        }
    }
}
