using UnityEditor;
using UnityEngine;

namespace Momentum.Level.EditorTools
{
    /// <summary>
    /// CityPlanGenerator 인스펙터 — 생성 버튼, 생성 전 집계, 설정 경고.
    ///
    /// 버튼보다 경고가 본체다. 이 도시는 프리팹 크기·도로 폭·웹 사거리가 서로 맞물린 치수로
    /// 짜여 있어서, 하나가 어긋나면 "건물이 길을 침범한다"거나 "애비뉴 가운데서 벽에 안 닿는다"는
    /// 문제가 플레이해보기 전까지 안 보인다. 그걸 생성 전에 보이게 하는 것이 이 인스펙터의 일이다.
    /// </summary>
    [CustomEditor(typeof(CityPlanGenerator))]
    public class CityPlanGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (CityPlanGenerator)target;
            var settings = generator.Settings;
            EditorGUILayout.Space();

            if (settings == null)
            {
                EditorGUILayout.HelpBox("CityPlanSettings 에셋을 지정할 것.\n" +
                                        "Assets > Create > Momentum > City Plan Settings (초기값이 기획 v3 좌표)",
                                        MessageType.Error);
                return;
            }

            DrawLayerSetup();
            DrawSceneConflicts();
            EditorGUILayout.Space();
            DrawPreview(generator, settings);
            EditorGUILayout.Space();
            DrawButtons(generator);
        }

        /// <summary>앵커·지면 레이어가 없으면 만드는 버튼. 기존 CityGeneratorEditor와 같은 동작.</summary>
        private static void DrawLayerSetup()
        {
            bool anchorMissing = LayerMask.NameToLayer(CityGenerator.AnchorLayerName) < 0;
            bool groundMissing = LayerMask.NameToLayer(CityGenerator.GroundLayerName) < 0;
            if (!anchorMissing && !groundMissing) return;

            EditorGUILayout.HelpBox("레이어가 빠져 있다. 앵커와 지면이 같은 레이어면 웹이 아스팔트에 붙는다.", MessageType.Warning);
            if (anchorMissing && GUILayout.Button($"'{CityGenerator.AnchorLayerName}' 레이어 생성"))
                CreateLayer(CityGenerator.AnchorLayerName);
            if (groundMissing && GUILayout.Button($"'{CityGenerator.GroundLayerName}' 레이어 생성"))
                CreateLayer(CityGenerator.GroundLayerName);
        }

        private static void CreateLayer(string layerName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++) // 0~7은 유니티 예약 슬롯
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return;
            }
            Debug.LogError("[CityPlanGenerator] 빈 레이어 슬롯이 없다.");
        }

        /// <summary>기존 CityGenerator가 같은 씬에서 켜져 있으면 중력과 도시가 겹친다.</summary>
        private static void DrawSceneConflicts()
        {
            var olds = Object.FindObjectsByType<CityGenerator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (olds.Length == 0) return;

            EditorGUILayout.HelpBox(
                $"씬에 켜진 CityGenerator가 {olds.Length}개 있다. 두 생성기가 Awake에서 각자 중력을 걸어서 " +
                "어느 쪽 값이 남을지 정해지지 않고, 생성된 도시도 같은 자리에 겹친다. " +
                "기존 CityGenerator 오브젝트를 비활성화하고 쓸 것.",
                MessageType.Warning);
        }

        /// <summary>생성 전 집계와 치수 경고.</summary>
        private static void DrawPreview(CityPlanGenerator generator, CityPlanSettings s)
        {
            var layout = generator.BuildLayout();
            var prefabs = generator.BuildingPrefabs;

            EditorGUILayout.LabelField("생성 전 집계", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                int large = layout.CountKind(PlanLotKind.Large);
                int medium = layout.CountKind(PlanLotKind.Medium);
                int small = layout.CountKind(PlanLotKind.Small);
                int grey = layout.CountKind(PlanLotKind.Greybox);
                int total = layout.lots.Count + layout.diagonalWalls.Count + s.landmarks.Count;

                Row("블록", $"{layout.blocks.Count} 개");
                Row("프리팹 타워", $"{large + medium + small} 채", $"Large {large} · Medium {medium} · Small {small}");
                Row("회색 박스", $"{grey + layout.diagonalWalls.Count} 채", $"필지 {grey} · 사선 가로벽 {layout.diagonalWalls.Count}");
                Row("랜드마크", $"{s.landmarks.Count} 채");
                Row("합계", $"{total} 채", "기존 격자 도시 87채");
                Row("최고 필지", $"{layout.TallestLot():F0} m");
                Row("중력", $"{s.GravityMagnitude:F2} m/s²", $"×{s.gravityScale:F1}");
            }

            EditorGUILayout.Space();
            bool clean = true;

            // 프리팹 슬롯과 바닥 크기 — 필지 폭에 안 들어가면 길을 침범한다.
            string[] names = { "Large", "Medium", "Small" };
            float[] lotWidths = { s.lotWidthLarge, s.lotWidthMedium, s.lotWidthSmall };
            float previousArea = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var meta = CityLayoutSettings.MetaOf(prefabs[i]);
                if (meta == null)
                {
                    clean = false;
                    EditorGUILayout.HelpBox($"{names[i]} 프리팹이 없거나 CityBuilding이 없다. 그 필지는 전부 회색 박스가 된다.",
                                            MessageType.Warning);
                    continue;
                }

                if (meta.footprint.x > lotWidths[i] + 0.01f || meta.footprint.y > s.frontageDepth + 0.01f)
                {
                    clean = false;
                    EditorGUILayout.HelpBox(
                        $"{names[i]} 바닥 {meta.footprint.x:F1} × {meta.footprint.y:F1} m가 필지 " +
                        $"{lotWidths[i]:F0} × {s.frontageDepth:F0} m보다 크다. 이웃 건물이나 길을 침범한다.",
                        MessageType.Error);
                }

                float area = meta.footprint.x * meta.footprint.y;
                if (area > previousArea + 0.01f)
                {
                    clean = false;
                    EditorGUILayout.HelpBox($"{names[i]}의 바닥이 앞 칸보다 넓다. 셋백은 Large → Medium → Small로 " +
                                            "좁아진다고 가정하므로 위 겹이 아래 겹 밖으로 튀어나온다.", MessageType.Warning);
                }
                previousArea = area;
            }

            // 스트리트 면 필지(블록 폭 − 깊이 × 2)에 Large가 들어가는지.
            var cols = s.BlockColumns();
            if (cols.Count > 0)
            {
                float shortLot = (cols[0].y - cols[0].x) - s.frontageDepth * 2f;
                if (Mathf.Abs(shortLot - s.lotWidthLarge) > 0.01f)
                {
                    clean = false;
                    EditorGUILayout.HelpBox($"스트리트 면 필지가 {shortLot:F1} m라 Large({s.lotWidthLarge:F0} m)와 안 맞는다. " +
                                            "블록 폭 = Large 폭 + 필지 깊이 × 2가 되게 애비뉴 간격을 맞출 것.",
                                            MessageType.Warning);
                }
            }

            // 웹 사거리 — 애비뉴 한가운데에서 양쪽 벽에 닿는지.
            float widest = 0f;
            foreach (var a in s.avenues) if (a != null) widest = Mathf.Max(widest, a.width);
            float half = widest * 0.5f;
            if (half >= s.webRange)
            {
                clean = false;
                EditorGUILayout.HelpBox($"가장 넓은 애비뉴({widest:F0} m) 가운데에서 벽까지 {half:F0} m로 사거리 " +
                                        $"{s.webRange:F0} m 밖이다. 가운데서 쏘면 벽에 안 닿는다.", MessageType.Error);
            }
            else
            {
                float reach = Mathf.Sqrt(s.webRange * s.webRange - half * half);
                EditorGUILayout.HelpBox($"애비뉴 가운데에서 벽까지 {half:F0} m — 사거리 {s.webRange:F0} m 원이 " +
                                        $"벽의 {reach:F1} m 위(올려다보는 각 {Mathf.Atan2(reach, half) * Mathf.Rad2Deg:F0}°)에 닿는다.",
                                        MessageType.Info);
            }

            if (s.avenueRoadPrefab == null || s.streetRoadPrefab == null || s.intersectionPrefab == null)
            {
                clean = false;
                EditorGUILayout.HelpBox("도로 타일 칸이 비어 있다. 빈 종류의 도로는 깔리지 않고 지면이 그대로 보인다.",
                                        MessageType.Info);
            }

            if (s.greyboxMaterials.Count == 0)
            {
                EditorGUILayout.HelpBox("회색 박스 머티리얼이 비어 있다. 유니티 기본 머티리얼(흰색)로 나온다.", MessageType.Info);
            }

            if (clean) EditorGUILayout.HelpBox("치수 조건을 모두 만족한다.", MessageType.Info);
        }

        /// <summary>생성/정리 버튼. 재생성도 Undo 한 번으로 되돌아가게 묶는다 (기존 인스펙터와 같은 방식).</summary>
        private static void DrawButtons(CityPlanGenerator generator)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(30)))
                {
                    Undo.SetCurrentGroupName("Generate Plan City");
                    int group = Undo.GetCurrentGroup();

                    var existing = generator.GeneratedRoot;
                    if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

                    generator.Generate();
                    var created = generator.GeneratedRoot;
                    if (created != null) Undo.RegisterCreatedObjectUndo(created.gameObject, "Generate Plan City");

                    Undo.CollapseUndoOperations(group);
                    MarkSceneDirty(generator);
                }

                using (new EditorGUI.DisabledScope(!generator.HasGenerated))
                {
                    if (GUILayout.Button("Clear", GUILayout.Height(30)))
                    {
                        var root = generator.GeneratedRoot;
                        if (root != null) Undo.DestroyObjectImmediate(root.gameObject);
                        MarkSceneDirty(generator);
                    }
                }
            }
        }

        private static void MarkSceneDirty(Component c)
        {
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        private static void Row(string label, string value, string hint = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(110));
                if (!string.IsNullOrEmpty(hint)) EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
            }
        }
    }
}
