using UnityEditor;
using UnityEngine;

namespace Momentum.Level.EditorTools
{
    /// <summary>
    /// 생성 버튼과 역산 결과 표시.
    ///
    /// 이 인스펙터의 목적은 버튼 두 개가 아니라 <b>경고</b>다.
    /// 건물 간격과 높이는 취향으로 고르는 값이 아니라 로프 길이에서 역산되는 값이므로,
    /// 어긋났을 때 눈에 보이게 만드는 것이 생성기의 실질적인 가치다.
    /// </summary>
    [CustomEditor(typeof(CityGenerator))]
    public class CityGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (CityGenerator)target;
            var settings = generator.Settings;

            EditorGUILayout.Space();

            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "CityLayoutSettings 프리셋을 지정할 것.\n" +
                    "Assets > Create > Momentum > City Layout Settings",
                    MessageType.Error);
                return;
            }

            DrawLayerSetup();
            EditorGUILayout.Space();
            DrawMetrics(settings);
            EditorGUILayout.Space();
            DrawButtons(generator);
        }

        private static void DrawLayerSetup()
        {
            bool anchorMissing = LayerMask.NameToLayer(CityGenerator.AnchorLayerName) < 0;
            bool groundMissing = LayerMask.NameToLayer(CityGenerator.GroundLayerName) < 0;
            if (!anchorMissing && !groundMissing) return;

            EditorGUILayout.HelpBox(
                "레이어가 빠져 있다. 앵커와 지면이 같은 레이어에 있으면 " +
                "웹이 아스팔트에 붙어 스윙이 지면 견인이 된다.",
                MessageType.Warning);

            if (anchorMissing && GUILayout.Button($"'{CityGenerator.AnchorLayerName}' 레이어 생성"))
                CreateLayer(CityGenerator.AnchorLayerName);

            if (groundMissing && GUILayout.Button($"'{CityGenerator.GroundLayerName}' 레이어 생성"))
                CreateLayer(CityGenerator.GroundLayerName);
        }

        /// <summary>비어 있는 첫 사용자 레이어 슬롯에 레이어를 만든다.</summary>
        private static void CreateLayer(string layerName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[CityGenerator] TagManager.asset을 열 수 없다. 레이어를 직접 추가할 것.");
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");

            // 0~7은 Unity 예약 슬롯이므로 8번부터 찾는다.
            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[CityGenerator] 레이어 {i}번에 '{layerName}' 추가.");
                return;
            }

            Debug.LogError("[CityGenerator] 빈 레이어 슬롯이 없다.");
        }

        private static void DrawMetrics(CityLayoutSettings s)
        {
            var b = s.Evaluate();

            EditorGUILayout.LabelField("물리에서 역산한 값", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                Row("중력", $"{b.Gravity:F2} m/s²  (×{s.gravityScale:F1})");
                Row("진자 반주기", $"{b.HalfPeriod:F2} s", "스윙 한 번의 체감 길이");
                Row("최저점 속도", $"{b.BottomSpeed:F1} m/s");
                Row("로프 장력", $"중력의 {b.TensionRatio:F1}배");
                EditorGUILayout.Space(2);
                Row("수평 이동 d", $"{b.Reach:F1} m", "d = 2·L·sin θ — 앵커 간격의 상한");
                Row("최소 건물 높이", $"{b.MinHeight:F1} m", "로프 길이 + 여유");
                Row("권장 도로 폭", $"{b.RecommendedStreet:F1} m");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("현재 설정", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                Row("건물 소스", s.HasPrefabs ? "실제 에셋 프리팹" : "회색 박스");
                Row("앵커 간격", $"{s.PitchX:F1} × {s.PitchZ:F1} m", "바닥 크기 + 도로 폭");
                Row("최고 건물 높이", $"{s.MaxHeight:F1} m",
                    s.HasPrefabs ? "프리팹 실측값 — 설정으로 바꿀 수 없다" : null);
                Row("건물 수", $"{s.TotalBuildings} 채");
                Row("스텝당 이동", $"{b.StepDistance:F2} m",
                    $"고정 타임스텝 {Time.fixedDeltaTime * 1000f:F0}ms 기준");
            }

            if (s.HasPrefabs) DrawPrefabTable(s);

            EditorGUILayout.Space();
            DrawWarnings(s, b);
        }

        private static void DrawPrefabTable(CityLayoutSettings s)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("프리팹 실측값", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var prefab in s.buildingPrefabs)
                {
                    if (prefab == null) continue;
                    var meta = CityLayoutSettings.MetaOf(prefab);
                    if (meta == null)
                    {
                        Row(prefab.name, "CityBuilding 없음", "BuildingPrefabTool로 다시 만들 것");
                        continue;
                    }
                    Row(prefab.name,
                        $"{meta.height:F1} m ({meta.floorCount}층)",
                        $"바닥 {meta.footprint.x:F1} × {meta.footprint.y:F1} m");
                }
            }

            var largest = s.LargestPrefabFootprint;
            float needed = Mathf.Max(largest.x, largest.y);
            if (needed > s.footprintX || needed > s.footprintZ)
            {
                EditorGUILayout.HelpBox(
                    $"가장 큰 프리팹 바닥이 {largest.x:F1} × {largest.y:F1} m인데 " +
                    $"설정 바닥은 {s.footprintX:F0} × {s.footprintZ:F0} m다.\n" +
                    "격자가 좁아 건물이 도로를 침범한다. 90° 회전을 켰으므로 긴 쪽 기준으로 맞춰야 한다.",
                    MessageType.Warning);

                if (GUILayout.Button($"바닥 크기를 프리팹 실측값({needed:F1}m)에 맞추기"))
                {
                    Undo.RecordObject(s, "Fit Footprint To Prefabs");
                    s.FitFootprint(needed);
                    EditorUtility.SetDirty(s);
                }
            }
        }

        private static void DrawWarnings(CityLayoutSettings s, SwingMetrics.Budget b)
        {
            bool clean = true;

            if (s.PitchX > b.Reach || s.PitchZ > b.Reach)
            {
                clean = false;
                EditorGUILayout.HelpBox(
                    $"앵커 간격({Mathf.Max(s.PitchX, s.PitchZ):F1}m)이 이론 사거리({b.Reach:F1}m)를 넘는다.\n" +
                    $"스윙이 이어지지 않고 추락한다. 도로 폭을 {b.RecommendedStreet:F0}m 이하로 줄이거나 " +
                    "로프 길이를 늘릴 것.",
                    MessageType.Error);
            }

            if (s.MaxHeight < b.MinHeight)
            {
                clean = false;

                if (s.HasPrefabs)
                {
                    // 프리팹을 쓰면 높이는 에셋이 정한 값이라 늘릴 수 없다.
                    // 그래서 조언의 방향이 반대가 된다 — 건물을 높이는 게 아니라 로프를 줄이는 것.
                    float maxRope = Mathf.Max(1f, s.MaxHeight - s.groundClearance);
                    float gravityForSameSpeed = Mathf.Max(1f,
                        s.gravityScale * (s.ropeLength / maxRope));
                    EditorGUILayout.HelpBox(
                        $"에셋의 최고 건물이 {s.MaxHeight:F1}m인데 로프 {s.ropeLength:F0}m는 " +
                        $"{b.MinHeight:F1}m를 요구한다.\n" +
                        $"건물 높이는 에셋이 정한 값이라 늘릴 수 없다. 로프를 {maxRope:F0}m 이하로 줄이고, " +
                        $"같은 속도를 유지하려면 중력 배율을 ×{gravityForSameSpeed:F1} 정도로 올릴 것.",
                        MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"최고 건물이 {s.MaxHeight:F0}m인데 최소 필요 높이는 {b.MinHeight:F0}m다.\n" +
                        $"앵커가 머리 위에 오지 않아 스윙이 성립하지 않는다. " +
                        $"{Mathf.CeilToInt(b.MinHeight / CityLayoutSettings.FloorHeight)}층 이상이 필요하다.",
                        MessageType.Error);
                }
            }

            // 터널링. 실제 건물 프리팹은 속이 빈 껍데기이므로 껍데기 모드와 같이 취급한다.
            bool thinWalled = s.shellMode || s.HasPrefabs;
            if (b.StepDistance > s.wallThickness * 0.5f)
            {
                clean = false;
                if (thinWalled)
                {
                    EditorGUILayout.HelpBox(
                        $"한 물리 스텝에 {b.StepDistance:F2}m 이동하는데 벽 두께는 {s.wallThickness:F2}m 수준이다.\n" +
                        $"벽을 통과한다. Fixed Timestep을 {b.SafeTimestep * 1000f:F0}ms 이하로 내리거나 " +
                        "Rigidbody를 Continuous Dynamic으로 둘 것.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"스텝당 {b.StepDistance:F2}m 이동한다. 속 찬 큐브에서는 문제가 드러나지 않지만 " +
                        "실제 건물(껍데기)로 바꾸면 터널링이 나타난다. 껍데기 모드로 미리 검증할 것.",
                        MessageType.Info);
                }
            }

            if (s.heightMode != HeightMode.Uniform)
            {
                EditorGUILayout.HelpBox(
                    "측정 단계에서는 Uniform을 권장한다. 높이가 섞이면 " +
                    "\"여기선 되고 저기선 안 되는\" 상황이 생겨 수치를 읽을 수 없다.",
                    MessageType.Info);
                clean = false;
            }

            if (clean)
                EditorGUILayout.HelpBox("역산 조건을 모두 만족한다.", MessageType.Info);
        }

        private static void DrawButtons(CityGenerator generator)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(30)))
                {
                    Undo.SetCurrentGroupName("Generate City");
                    int group = Undo.GetCurrentGroup();

                    // 재생성도 Undo 한 번으로 되돌아가야 하므로 기존 도시 파괴까지 같은 그룹에 넣는다.
                    // Generate() 내부의 Clear()는 이 시점에 대상이 없어 무시된다.
                    var existing = generator.GeneratedRoot;
                    if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

                    generator.Generate();
                    var created = generator.GeneratedRoot;
                    if (created != null) Undo.RegisterCreatedObjectUndo(created.gameObject, "Generate City");

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
                EditorGUILayout.LabelField(label, GUILayout.Width(120));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(140));
                if (!string.IsNullOrEmpty(hint))
                    EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
            }
        }
    }
}
