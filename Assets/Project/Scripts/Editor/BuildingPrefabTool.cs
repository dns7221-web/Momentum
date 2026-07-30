using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Momentum.Level.EditorTools
{
    /// <summary>
    /// FBX 모델을 스윙에 쓸 수 있는 건물 프리팹으로 바꾼다.
    ///
    /// 손으로 하면 건물 하나에 네 단계다 — 씬에 놓고, 콜라이더 크기를 재서 넣고,
    /// 레이어를 지정하고, 프리팹으로 저장. 건물이 늘어나면 그만큼 반복된다.
    /// 자동화하는 이유는 시간이 아니라 <b>실측</b>이다. 콜라이더를 눈대중으로 맞추면
    /// 앵커 위치가 실제 옥상과 어긋나고, 그건 스윙에서 바로 티가 난다.
    /// </summary>
    public static class BuildingPrefabTool
    {
        private const string OutputFolder = "Assets/Project/City/Prefabs";
        private const string MenuPath = "Momentum/선택한 모델을 건물 프리팹으로 만들기";
        private const string MeasureMenuPath = "Momentum/선택한 모델 치수 재기";

        [MenuItem(MenuPath, false, 100)]
        private static void BuildFromSelection()
        {
            var models = CollectSelectedModels();
            if (models.Count == 0)
            {
                Debug.LogWarning("[BuildingPrefabTool] 프로젝트 창에서 FBX 모델을 선택하고 실행할 것.");
                return;
            }

            if (LayerMask.NameToLayer(CityGenerator.AnchorLayerName) < 0)
            {
                Debug.LogWarning(
                    $"[BuildingPrefabTool] '{CityGenerator.AnchorLayerName}' 레이어가 없어 Default로 만든다. " +
                    "CityGenerator 인스펙터의 레이어 생성 버튼을 먼저 누르는 편이 낫다.");
            }

            EnsureOutputFolder();

            var created = new List<Object>();
            var report = new StringBuilder("[BuildingPrefabTool] 실측 결과\n");

            foreach (var model in models)
            {
                var prefab = BuildOne(model, report);
                if (prefab != null) created.Add(prefab);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());

            if (created.Count == 0) return;

            Selection.objects = created.ToArray();
            Debug.Log($"[BuildingPrefabTool] 프리팹 {created.Count}개를 {OutputFolder}에 만들었다. " +
                      "CityLayoutSettings의 Building Prefabs에 넣고 Generate할 것.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateSelection()
        {
            return Selection.objects.Length > 0;
        }

        /// <summary>
        /// 프리팹은 만들지 않고 치수만 잰다.
        ///
        /// 도로·인도 타일을 어떤 격자에 깔 수 있는지는 타일 실측 폭이 결정한다.
        /// 4차선 도로가 18m인데 물리에서 역산한 도로 폭이 12m라면 그 타일은 쓸 수 없고,
        /// 그건 재보기 전에는 알 수 없다.
        /// </summary>
        [MenuItem(MeasureMenuPath, false, 101)]
        private static void MeasureSelection()
        {
            var models = CollectSelectedModels();
            if (models.Count == 0)
            {
                Debug.LogWarning("[BuildingPrefabTool] 프로젝트 창에서 모델을 선택하고 실행할 것.");
                return;
            }

            var report = new StringBuilder($"[BuildingPrefabTool] 치수 {models.Count}건\n");
            report.AppendLine("  이름                              폭 X   높이 Y    깊이 Z   격자");

            foreach (var model in models)
            {
                if (!TryMeasureTemporary(model, out Bounds bounds))
                {
                    report.AppendLine($"  {model.name,-32} 렌더러 없음");
                    continue;
                }

                report.AppendLine(
                    $"  {model.name,-32} {bounds.size.x,6:F2} {bounds.size.y,7:F2} {bounds.size.z,8:F2}" +
                    $"   {GridNote(bounds.size)}");
            }

            // 하나만 선택했으면 자식 렌더러까지 뜯어본다.
            // 바운즈가 본체보다 크게 잡히는 원인은 거의 항상 튀어나온 자식 하나이고,
            // 합계만 보면 그게 안 보인다.
            if (models.Count == 1) AppendRendererBreakdown(models[0], report);

            Debug.Log(report.ToString());
        }

        [MenuItem(MeasureMenuPath, true)]
        private static bool ValidateMeasure()
        {
            return Selection.objects.Length > 0;
        }

        /// <summary>
        /// 자식 렌더러를 하나씩 나열한다. 전체 바운즈를 벗어나게 만드는 놈을 찾기 위한 것.
        /// 각 렌더러가 전체 바운즈의 어느 면을 밀어내고 있는지 표시한다.
        /// </summary>
        private static void AppendRendererBreakdown(GameObject model, StringBuilder report)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length <= 1)
            {
                Object.DestroyImmediate(instance);
                return;
            }

            TryMeasure(instance, out Bounds total);

            report.AppendLine($"\n  자식 렌더러 {renderers.Length}개 — 전체 바운즈를 밀어내는 것 표시");
            foreach (var renderer in renderers)
            {
                var b = renderer.bounds;
                var extremes = new List<string>();
                if (b.max.y >= total.max.y - 0.01f) extremes.Add("위");
                if (b.min.y <= total.min.y + 0.01f) extremes.Add("아래");
                if (b.max.x >= total.max.x - 0.01f) extremes.Add("+X");
                if (b.min.x <= total.min.x + 0.01f) extremes.Add("-X");
                if (b.max.z >= total.max.z - 0.01f) extremes.Add("+Z");
                if (b.min.z <= total.min.z + 0.01f) extremes.Add("-Z");

                report.AppendLine(
                    $"    {renderer.name,-28} {b.size.x,6:F2} {b.size.y,6:F2} {b.size.z,6:F2}" +
                    $"   y {b.min.y,6:F2}~{b.max.y,-6:F2}" +
                    (extremes.Count > 0 ? $"  ← {string.Join(" ", extremes)}" : string.Empty));
            }

            Object.DestroyImmediate(instance);
        }

        /// <summary>에셋 모듈 격자(2m 가로 / 3m 층고)에 맞는지 한눈에 보이게.</summary>
        private static string GridNote(Vector3 size)
        {
            var notes = new List<string>();
            if (IsMultipleOf(size.x, CityLayoutSettings.ModuleWidth) &&
                IsMultipleOf(size.z, CityLayoutSettings.ModuleWidth)) notes.Add("2m 배수");
            if (IsMultipleOf(size.x, 3f) && IsMultipleOf(size.z, 3f)) notes.Add("3m 배수");
            return notes.Count == 0 ? "-" : string.Join(" / ", notes);
        }

        private static bool IsMultipleOf(float value, float step)
        {
            float remainder = Mathf.Abs(value / step % 1f);
            return remainder < 0.02f || remainder > 0.98f;
        }

        /// <summary>씬에 잠깐 놓아 재고 바로 지운다.</summary>
        private static bool TryMeasureTemporary(GameObject model, out Bounds bounds)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            bool measured = TryMeasure(instance, out bounds);
            Object.DestroyImmediate(instance);
            return measured;
        }

        private static List<GameObject> CollectSelectedModels()
        {
            var models = new List<GameObject>();
            foreach (var obj in Selection.objects)
            {
                if (obj is GameObject go && AssetDatabase.Contains(go))
                    models.Add(go);
            }
            return models;
        }

        private static GameObject BuildOne(GameObject model, StringBuilder report)
        {
            // 루트를 따로 두는 이유는 피벗과 콜라이더를 우리가 통제하기 위해서다.
            // 모델을 직접 프리팹 루트로 쓰면 임포터 설정이 바뀔 때마다 콜라이더가 흔들린다.
            var root = new GameObject(model.name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            if (!TryMeasure(instance, out Bounds bounds))
            {
                report.AppendLine($"  {model.name} — 건너뜀 (렌더러가 없다)");
                Object.DestroyImmediate(root);
                return null;
            }

            // 바닥 크기는 전체 바운즈가 아니라 본체 단면으로 잡는다.
            // 진입 계단·난간이 인도로 튀어나와 있어서 전체 바운즈를 쓰면 콜라이더가
            // 계단까지 덮고, 건물이 실제보다 크게 잡힌다.
            var body = MeasureBodyFootprint(instance, bounds);

            // 지오메트리를 루트 피벗에 정렬한다.
            //
            // 이 키트의 건물은 자기 피벗에서 5~8m 떨어진 곳에 지오메트리가 있다.
            // 정렬하지 않으면 격자 중심에 놓아도 건물이 그만큼 치우쳐 도로를 침범하고,
            // 옥상 앵커 좌표도 실제 건물과 어긋난다. 회전을 주면 오차가 더 커진다.
            // 기준은 본체 중심이다 — 계단이 붙은 쪽으로 끌려가지 않게.
            instance.transform.localPosition =
                new Vector3(-body.center.x, -bounds.min.y, -body.center.y);

            float height = bounds.size.y;
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.5f, 0f);
            box.size = new Vector3(body.size.x, height, body.size.y);

            var meta = root.AddComponent<CityBuilding>();
            meta.height = height;
            meta.footprint = body.size;
            meta.floorCount = Mathf.Max(1, Mathf.RoundToInt(meta.height / CityLayoutSettings.FloorHeight));
            meta.isShell = true; // 실제 건물은 속이 빈 껍데기다. 터널링 판정에서 이 구분이 필요하다.

            int layer = LayerMask.NameToLayer(CityGenerator.AnchorLayerName);
            if (layer >= 0) SetLayerRecursive(root, layer);

            string path = $"{OutputFolder}/{model.name}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            float trimmedX = bounds.size.x - body.size.x;
            float trimmedZ = bounds.size.z - body.size.y;
            string trimNote = Mathf.Max(trimmedX, trimmedZ) > 0.1f
                ? $"   전체 {bounds.size.x:F1}×{bounds.size.z:F1} → 계단 등 " +
                  $"{Mathf.Max(trimmedX, trimmedZ):F1}m 제외"
                : string.Empty;

            report.AppendLine(
                $"  {model.name,-24} {body.size.x,5:F1} × {meta.height,5:F1} × {body.size.y,5:F1} m" +
                $"   약 {meta.floorCount}층{trimNote}");

            return saved;
        }

        /// <summary>바닥 크기를 재는 높이 구간. 지상 계단·난간과 옥상 구조물을 함께 걸러낸다.</summary>
        private const float BodyBandLow = 0.25f;
        private const float BodyBandHigh = 0.75f;

        /// <summary>
        /// 건물 몸통의 수평 단면. 중심과 크기를 XZ 평면에서 돌려준다 (<c>Rect</c>가 아니라
        /// <c>center</c>·<c>size</c>만 쓰므로 <c>Bounds</c>의 2D 대응으로 읽으면 된다).
        ///
        /// 전체 바운즈를 쓰지 않는 이유는 진입 계단·난간이 인도로 튀어나와 있어서다.
        /// 높이의 중간 구간만 보면 지상 돌출물과 옥상 구조물이 자연히 빠지고 몸통만 남는다.
        /// 정점을 읽을 수 없으면 전체 바운즈로 물러난다.
        /// </summary>
        private static (Vector2 center, Vector2 size) MeasureBodyFootprint(
            GameObject instance, Bounds full)
        {
            float low = full.min.y + full.size.y * BodyBandLow;
            float high = full.min.y + full.size.y * BodyBandHigh;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var vertices = mesh.vertices;
                var local = filter.transform;
                foreach (var vertex in vertices)
                {
                    // 루트가 원점에 있으므로 월드 좌표가 곧 루트 로컬 좌표다.
                    Vector3 p = local.TransformPoint(vertex);
                    if (p.y < low || p.y > high) continue;

                    any = true;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                }
            }

            if (!any)
                return (new Vector2(full.center.x, full.center.z),
                        new Vector2(full.size.x, full.size.z));

            return (new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f),
                    new Vector2(maxX - minX, maxZ - minZ));
        }

        private static bool TryMeasure(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private static void EnsureOutputFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder)) return;
            AssetDatabase.CreateFolder("Assets/Project/City", "Prefabs");
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }
    }
}
