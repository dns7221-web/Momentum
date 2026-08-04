using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 씬이 시작될 때 씬에 생성된 모든 옥상/광장 소품(_RoofProps, _PlazaProps)에
    /// 자동으로 Rigidbody와 CombatProp을 찾아 붙여주는 런타임 자동화 클래스.
    /// </summary>
    public class PropPhysicsInitializer : MonoBehaviour
    {
        [Header("자동 물리 주입 설정")]
        [Tooltip("기본 프롭 질량 (kg)")]
        [SerializeField] private float defaultPropMass = 30f;

        private void Start()
        {
            InitializeAllPropsInScene();
        }

        /// <summary>
        /// 씬 내부의 모든 _RoofProps, _PlazaProps를 찾아 자식 오브젝트에 물리를 주입합니다.
        /// </summary>
        public void InitializeAllPropsInScene()
        {
            int count = 0;
            var propRoots = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);

            foreach (var tf in propRoots)
            {
                if (tf.name == "_RoofProps" || tf.name == "_PlazaProps")
                {
                    for (int i = 0; i < tf.childCount; i++)
                    {
                        Transform child = tf.GetChild(i);
                        ApplyPhysicsToProp(child.gameObject);
                        count++;
                    }
                }
            }

            Debug.Log($"[PropPhysicsInitializer] 총 {count}개의 옥상/광장 소품에 Rigidbody 및 CombatProp 자동 주입 완료!");
        }

        private void ApplyPhysicsToProp(GameObject prop)
        {
            if (prop == null) return;

            // Colliders가 없으면 MeshCollider 추가
            if (prop.GetComponentInChildren<Collider>() == null)
            {
                foreach (var filter in prop.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh != null)
                    {
                        var mc = filter.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = filter.sharedMesh;
                        mc.convex = true;
                    }
                }
            }

            // Rigidbody 추가
            if (prop.GetComponentInChildren<Rigidbody>() == null)
            {
                var rb = prop.AddComponent<Rigidbody>();
                rb.mass = defaultPropMass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            // CombatProp 추가
            if (prop.GetComponent<CombatProp>() == null)
            {
                prop.AddComponent<CombatProp>();
            }

            // 도시 생성기가 Static으로 구워둔 프롭이라, 물리를 얹었으면 반드시 꺼야 한다.
            // 안 끄면 정적 배칭으로 구워진 렌더러 위치와 실제 물리 위치가 어긋난다.
            prop.isStatic = false;

            // Anchorable에 남아 있으면 스윙 갈고리가 이 프롭에 걸릴 수 있다 — Grabbable로 옮긴다.
            int grabbableLayer = LayerMask.NameToLayer("Grabbable");
            if (grabbableLayer >= 0) SetLayerRecursive(prop, grabbableLayer);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }
    }
}
