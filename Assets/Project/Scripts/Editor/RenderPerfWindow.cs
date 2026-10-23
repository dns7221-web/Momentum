using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Momentum.EditorTools
{
    // 추가 생성 (2026-10-23)
    /// <summary>
    /// 렌더 최적화 창 — 렉의 주범인 "그리기 횟수"를 줄이는 설정을 한곳에 모아 켜고 끈다.
    ///
    /// <b>왜 이 창이 생겼나</b> — 2026-10-22 렉 미터 기록(14,348프레임)에서 프레임 시간이 화면의 배치 수를
    /// 거의 그대로 따라갔다(배치 2천 미만 7 ms, 6~8천 21 ms, 1만 넘으면 33 ms). 물리·스크립트는 다 합쳐도 1~3 ms.
    /// 도시가 작은 조각 약 1만 8천 개(인도 타일 8,206·옥상 난간 4,824·차도 2,774 …)로 되어 있고,
    /// 그림자 4단계와 SSAO(Depth+Normals)가 이 조각들을 여러 번 다시 그린다.
    ///
    /// <b>무엇을 바꾸나</b> — 전부 유니티/URP에 이미 있는 설정이다. 그리는 코드를 새로 짜지 않고 스위치만 모았다.
    /// 하나씩 바꾸고 Play → 렉 미터(F3)로 FPS·배치를 비교할 수 있게, 항목마다 지금 값을 보여 주고 되돌릴 수 있다.
    ///
    /// <b>무엇을 안 바꾸나</b> — 웹스윙·퍼치에 쓰이는 충돌체, 레이어, Static 표시는 건드리지 않는다.
    /// 모바일 품질 단계의 URP 에셋도 그대로 둔다(지금 품질 단계의 에셋만 바꾼다).
    /// </summary>
    public class RenderPerfWindow : EditorWindow
    {
        /// <summary>
        /// 평평한 바닥 조각의 모델 이름. 평평해서 드리우는 그림자가 거의 안 보이는데, 그림자 단계마다 다시 그려진다.
        /// 다리(road-bridge)는 아래로 그림자가 지는 게 보이므로 넣지 않았다.
        /// </summary>
        private static readonly string[] GroundModelNames =
        {
            "Sidewalk_NoCurb_3m", "Street_4Lane", "Street_2Lane", "Street_Asphalt_6x6", "Decal_Crosswalk",
        };

        // SSAO의 DepthSource 값 순서(Depth = 0, DepthNormals = 1)와 같아야 한다 — URP 쪽 enum이 internal이라 숫자로 다룬다.
        private static readonly string[] SsaoSourceLabels = { "Depth (가벼움)", "Depth + Normals (지금까지)" };
        private static readonly string[] CascadeLabels = { "1단계", "2단계", "3단계", "4단계" };
        private static readonly int[] CascadeValues = { 1, 2, 3, 4 };

        private int _groundTotal = -1, _groundCasting = -1; // -1 = 아직 안 셈(씬의 렌더러 1만 개 넘게 훑으므로 버튼으로만 센다)
        private Vector2 _scroll;
        private GUIStyle _wrap;

        [MenuItem("Momentum/렌더 최적화", false, 130)]
        private static void Open() => GetWindow<RenderPerfWindow>("렌더 최적화");

        private void OnEnable() => CountGround();

        private void OnGUI()
        {
            _wrap ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true };

            UniversalRenderPipelineAsset asset = CurrentAsset();
            if (asset == null)
            {
                EditorGUILayout.HelpBox("지금 품질 단계가 URP 에셋을 쓰지 않는다. Project Settings > Quality를 확인할 것.", MessageType.Error);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("하나씩 바꾸고 Play → 렉 미터(F3)로 FPS·배치를 비교한다. 같은 자리·같은 방향에서 재야 비교가 된다. " +
                                    "씬 뷰 탭을 열어 두면 씬 뷰도 같이 그려져 숫자가 나빠지니 닫고 잰다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("바꾸는 URP 에셋", asset, typeof(UniversalRenderPipelineAsset), false);

            // Play 중엔 막는다 — 씬에서 바꾼 건 Play가 끝나면 되돌아가고, 정적 배칭은 씬을 불러올 때만 적용돼서 헷갈린다.
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            if (playing) EditorGUILayout.HelpBox("Play 중엔 바꾸지 않는다. Play를 끝내고 바꾼 뒤 다시 잰다.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(playing))
            {
                DrawResidentDrawer(asset);
                DrawStaticBatching();
                DrawGroundShadows();
                DrawSsao(asset);
                DrawCascades(asset);
            }
            EditorGUILayout.EndScrollView();
        }

        // ───────────────────────── 1. GPU Resident Drawer ─────────────────────────

        /// <summary>
        /// URP 내장 GPU Resident Drawer. 같은 메시·머티리얼을 쓰는 렌더러를 GPU 인스턴싱으로 몰아 그려서,
        /// CPU가 조각마다 그리기 명령을 만드는 비용(지금 렉의 대부분)을 줄인다. 보이는 결과는 그대로다.
        /// URP가 요구하는 조건 중 이 프로젝트에 빠진 건 Graphics 설정의 "BatchRendererGroup Variants = Keep All" 하나라,
        /// 켤 때 같이 바꾼다(Forward+ 렌더링과 SRP Batcher는 이미 켜져 있다).
        /// </summary>
        private void DrawResidentDrawer(UniversalRenderPipelineAsset asset)
        {
            Section("1. GPU Resident Drawer (몰아 그리기)",
                    "똑같은 조각 수천 개(인도·차도·난간)를 GPU에서 한 번에 몰아 그린다. 보이는 건 그대로다. " +
                    "켜면 Graphics 설정의 'BatchRendererGroup Variants'도 Keep All로 바꾼다 — 안 바꾸면 URP가 켜기를 거부한다.");

            bool on = asset.gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled;
            bool newOn = EditorGUILayout.Toggle("켜기", on);
            if (newOn != on)
            {
                Undo.RecordObject(asset, "GPU Resident Drawer");
                asset.gpuResidentDrawerMode = newOn ? GPUResidentDrawerMode.InstancedDrawing : GPUResidentDrawerMode.Disabled;
                if (newOn) SetBrgKeepAll(); // 수정 (2026-10-23): 직접 대입 → SetBrgKeepAll (읽기 전용 속성이라 컴파일 에러)
                Save(asset);
            }

            using (new EditorGUI.DisabledScope(!newOn))
            {
                EditorGUI.indentLevel++;
                bool occlusion = EditorGUILayout.Toggle(
                    new GUIContent("GPU 가림 컬링", "건물 뒤에 가려 안 보이는 조각을 GPU가 빼고 그린다. 보이는 건 그대로다."),
                    asset.gpuResidentDrawerEnableOcclusionCullingInCameras);
                if (occlusion != asset.gpuResidentDrawerEnableOcclusionCullingInCameras)
                {
                    Undo.RecordObject(asset, "GPU 가림 컬링");
                    asset.gpuResidentDrawerEnableOcclusionCullingInCameras = occlusion;
                    Save(asset);
                }

                // 화면 크기 대비 %. 0.3%는 1080p에서 3픽셀쯤 — 아주 먼 화분·실외기 정도만 빠지고, 3 m 바닥 타일은 먼 끝(1 km)까지 남는다.
                float small = EditorGUILayout.Slider(
                    new GUIContent("작은 조각 생략 (화면 %)", "화면에서 이 비율보다 작게 보이는 조각은 안 그린다. 0이면 끔. 추천 0.3."),
                    asset.smallMeshScreenPercentage, 0f, 2f);
                if (!Mathf.Approximately(small, asset.smallMeshScreenPercentage))
                {
                    Undo.RecordObject(asset, "작은 조각 생략");
                    asset.smallMeshScreenPercentage = small;
                    Save(asset);
                }
                EditorGUI.indentLevel--;
            }

            // 다른 곳에서 Keep All이 풀렸으면 알려 준다 — 이러면 켜기를 눌러도 Play 때 경고만 뜨고 꺼진 채로 돈다.
            if (newOn && EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != BatchRendererGroupStrippingMode.KeepAll)
            {
                EditorGUILayout.HelpBox("BatchRendererGroup Variants가 Keep All이 아니라 GPU Resident Drawer가 꺼진 채로 돈다.", MessageType.Warning);
                if (GUILayout.Button("Keep All로 바꾸기"))
                    SetBrgKeepAll(); // 수정 (2026-10-23): 직접 대입 → SetBrgKeepAll
            }
        }

        // 추가 생성 (2026-10-23)
        /// <summary>
        /// Graphics 설정의 "BatchRendererGroup Variants"를 Keep All로 바꾼다 — GPU Resident Drawer가 쓰는 셰이더 변형을 빌드에서 지우지 않게.
        /// <c>EditorGraphicsSettings.batchRendererGroupShaderStrippingMode</c>는 읽기 전용이라 대입하면 컴파일 에러가 난다.
        /// 그래서 Project Settings > Graphics 창과 같은 방식으로 GraphicsSettings 에셋의 저장 값(m_BrgStripping)을 바꾼다.
        /// 바뀌었는지는 같은 속성을 다시 읽어 확인하고, 안 바뀌었으면 손으로 바꾸는 곳을 알려 준다.
        /// </summary>
        private static void SetBrgKeepAll()
        {
            var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty mode = settings.FindProperty("m_BrgStripping");
            if (mode != null)
            {
                mode.intValue = (int)BatchRendererGroupStrippingMode.KeepAll;
                settings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets(); // ProjectSettings/GraphicsSettings.asset에 바로 써 둔다
            }

            if (EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != BatchRendererGroupStrippingMode.KeepAll)
                Debug.LogWarning("[렌더 최적화] BatchRendererGroup Variants를 Keep All로 못 바꿨다. " +
                                 "Project Settings > Graphics > Shader Stripping에서 직접 Keep All로 바꿀 것.");
        }

        // ───────────────────────── 2. 정적 배칭 ─────────────────────────

        /// <summary>
        /// Player 설정의 정적 배칭. Static 표시된 조각을 씬을 불러올 때 큰 메시로 합친다.
        /// 합쳐진 조각은 각자 "큰 메시의 일부"가 되어 더 이상 같은 메시가 아니므로, 1번의 몰아 그리기(인스턴싱)를 못 받는다.
        /// 그래서 1번을 켰다면 이걸 끄고 비교한다. 1번을 안 켰다면 켜 두는 게 낫다.
        /// </summary>
        private void DrawStaticBatching()
        {
            Section("2. 정적 배칭",
                    "Static 조각을 Play 시작 때 큰 메시로 미리 합친다. 1번과 같이 켜면 합쳐진 조각은 몰아 그리기를 못 받으니, " +
                    "1번을 켰다면 끄고 비교한다. Static 표시 자체는 그대로다.");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            bool on = PlayerSettings.GetStaticBatchingForPlatform(target);
            bool newOn = EditorGUILayout.Toggle($"켜기 ({target})", on);
            if (newOn != on)
            {
                PlayerSettings.SetStaticBatchingForPlatform(target, newOn);
                AssetDatabase.SaveAssets(); // ProjectSettings에 바로 써 둔다
            }
        }

        // ───────────────────────── 3. 바닥 조각 그림자 ─────────────────────────

        /// <summary>
        /// 평평한 바닥 조각이 그림자를 "드리우지" 않게 한다(받는 건 그대로라 건물 그림자는 바닥에 계속 진다).
        /// 그림자는 단계(캐스케이드)마다 범위 안의 물체를 다시 그리므로, 1만 개 넘는 바닥 조각이 그림자 패스에서 빠지는 효과가 크다.
        /// 씬에 이미 깔린 조각에 적용하는 버튼이다 — 새로 생성할 때는 생성기의 "바닥 그림자" 설정이 같은 일을 한다.
        /// </summary>
        private void DrawGroundShadows()
        {
            Section("3. 바닥 조각 그림자",
                    "인도·차도·교차로·횡단보도는 평평해서 드리우는 그림자가 거의 안 보이는데, 그림자 단계마다 다시 그려진다. " +
                    "끄면 그림자를 드리우지만 않고, 건물 그림자는 그대로 받는다. 대상: " + string.Join(", ", GroundModelNames));

            EditorGUILayout.LabelField(_groundTotal < 0
                ? "아직 안 셈"
                : $"그림자 드리우는 바닥 조각 {_groundCasting:N0} / {_groundTotal:N0}개");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("다시 세기")) CountGround();
                if (GUILayout.Button("그림자 끄기")) SetGroundShadows(false);
                if (GUILayout.Button("다시 켜기")) SetGroundShadows(true);
            }
        }

        /// <summary>열린 씬들에서 바닥 조각 렌더러를 모은다. 프리팹 인스턴스가 어느 모델에서 왔는지(파일 이름)로 고른다.</summary>
        private static List<MeshRenderer> FindGroundRenderers()
        {
            var names = new HashSet<string>(GroundModelNames);
            var result = new List<MeshRenderer>();
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer.gameObject);
                if (string.IsNullOrEmpty(path)) continue;
                if (names.Contains(Path.GetFileNameWithoutExtension(path))) result.Add(renderer);
            }
            return result;
        }

        private void CountGround()
        {
            List<MeshRenderer> renderers = FindGroundRenderers();
            _groundTotal = renderers.Count;
            _groundCasting = 0;
            foreach (MeshRenderer renderer in renderers)
                if (renderer.shadowCastingMode != ShadowCastingMode.Off) _groundCasting++;
            Repaint();
        }

        /// <summary>
        /// 바닥 조각 그림자를 끄거나 켠다. Undo 한 번으로 되돌릴 수 있고, 프리팹 인스턴스의 덮어쓰기로 씬에 저장된다
        /// (모델 원본은 안 바뀐다). 끝나면 씬을 저장해야 남는다.
        /// </summary>
        private void SetGroundShadows(bool cast)
        {
            var targetMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            var changed = new List<MeshRenderer>();
            foreach (MeshRenderer renderer in FindGroundRenderers())
                if (renderer.shadowCastingMode != targetMode) changed.Add(renderer);

            if (changed.Count == 0)
            {
                Debug.Log($"[렌더 최적화] 바꿀 바닥 조각이 없다 — 이미 전부 그림자 {(cast ? "켜짐" : "꺼짐")}.");
                CountGround();
                return;
            }

            Undo.RecordObjects(changed.ToArray(), cast ? "바닥 조각 그림자 켜기" : "바닥 조각 그림자 끄기");
            var scenes = new HashSet<UnityEngine.SceneManagement.Scene>();
            foreach (MeshRenderer renderer in changed)
            {
                renderer.shadowCastingMode = targetMode;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); // 프리팹 인스턴스 덮어쓰기로 남긴다
                scenes.Add(renderer.gameObject.scene);
            }
            foreach (UnityEngine.SceneManagement.Scene scene in scenes) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[렌더 최적화] 바닥 조각 {changed.Count:N0}개 그림자 {(cast ? "켬" : "끔")} — 씬을 저장해야 남는다.");
            CountGround();
        }

        // ───────────────────────── 4. SSAO ─────────────────────────

        /// <summary>
        /// SSAO의 깊이 소스. Depth+Normals는 SSAO에 쓸 법선을 얻으려고 화면의 모든 물체를 한 번 더 그린다(조각 1만 개면 1만 번).
        /// Depth는 이미 있는 깊이에서 법선을 짐작해 그 한 번이 빠진다. 대신 구석 그늘이 살짝 거칠어질 수 있어 눈으로 고른다.
        /// URP의 설정 클래스가 internal이라 SerializedObject로 "m_Settings.Source"를 바꾼다(인스펙터와 같은 방식).
        /// </summary>
        private void DrawSsao(UniversalRenderPipelineAsset asset)
        {
            Section("4. SSAO (구석 그늘)",
                    "Depth + Normals 방식은 SSAO만을 위해 화면의 모든 물체를 한 번 더 그린다. Depth 방식은 이미 있는 깊이로 계산해 " +
                    "그 한 번이 빠진다. 구석 그늘이 살짝 거칠어질 수 있다 — 눈으로 비교하고 고른다.");

            ScreenSpaceAmbientOcclusion ssao = FindSsao(asset, out ScriptableRendererData owner);
            if (ssao == null)
            {
                EditorGUILayout.LabelField("이 URP 에셋의 렌더러에 SSAO가 없다.");
                return;
            }
            if (!ssao.isActive) EditorGUILayout.LabelField("SSAO가 꺼져 있다 — 바꿔도 차이가 없다.");

            var serialized = new SerializedObject(ssao);
            SerializedProperty source = serialized.FindProperty("m_Settings.Source");
            if (source == null)
            {
                EditorGUILayout.HelpBox("이 URP 버전에서 SSAO 설정 이름을 못 찾았다. 렌더러 에셋 인스펙터에서 직접 바꿀 것.", MessageType.Warning);
                return;
            }

            int value = EditorGUILayout.Popup("방식", source.intValue, SsaoSourceLabels);
            if (value != source.intValue)
            {
                source.intValue = value;
                serialized.ApplyModifiedProperties(); // Undo 기록과 OnValidate(기능 다시 만들기)를 같이 해 준다
                Save(owner);
            }
        }

        /// <summary>URP 에셋이 쓰는 렌더러들에서 SSAO 기능을 찾는다. owner는 그 기능을 가진 렌더러(저장용).</summary>
        private static ScreenSpaceAmbientOcclusion FindSsao(UniversalRenderPipelineAsset asset, out ScriptableRendererData owner)
        {
            foreach (ScriptableRendererData data in asset.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                {
                    if (feature is not ScreenSpaceAmbientOcclusion ssao) continue;
                    owner = data;
                    return ssao;
                }
            }
            owner = null;
            return null;
        }

        // ───────────────────────── 5. 그림자 단계 ─────────────────────────

        /// <summary>
        /// 그림자 캐스케이드 수. 카메라 앞 그림자 범위(지금 50 m)를 몇 장의 그림자 지도로 나눌지 — 한 장마다 그 범위의 물체를 다시 그린다.
        /// 줄이면 그리기가 줄고, 대신 중간 거리 그림자가 살짝 흐려진다. 선택 항목이라 눈으로 보고 정한다.
        /// </summary>
        private void DrawCascades(UniversalRenderPipelineAsset asset)
        {
            Section("5. 그림자 단계 (선택)",
                    "카메라 앞 그림자 범위를 그림자 지도 몇 장으로 나눌지. 한 장마다 그 범위의 물체를 다시 그린다. " +
                    "2단계로 줄이면 중간 거리 그림자가 살짝 흐려진다 — 눈으로 비교하고 고른다.");

            int cascades = EditorGUILayout.IntPopup("단계", asset.shadowCascadeCount, CascadeLabels, CascadeValues);
            if (cascades != asset.shadowCascadeCount)
            {
                Undo.RecordObject(asset, "그림자 단계");
                asset.shadowCascadeCount = cascades;
                Save(asset);
            }
        }

        // ───────────────────────── 공용 ─────────────────────────

        /// <summary>지금 품질 단계가 쓰는 URP 에셋(없으면 기본 에셋). 이 프로젝트는 PC 단계 → PC_RPAsset.</summary>
        private static UniversalRenderPipelineAsset CurrentAsset() =>
            (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline)
                as UniversalRenderPipelineAsset;

        /// <summary>항목 제목과 설명(왜 무거운지·뭐가 바뀌는지).</summary>
        private void Section(string title, string description)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, _wrap);
        }

        /// <summary>에셋을 바로 디스크에 써 둔다 — 에디터가 꺼져도 바꾼 값이 남게.</summary>
        private static void Save(Object target)
        {
            if (target == null) return;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
    }
}
