using UnityEditor;
using UnityEngine;

namespace Momentum.Level.EditorTools
{
    /// <summary>
    /// 지면용 URP 머티리얼을 만든다.
    ///
    /// 손으로 만들지 않는 이유는 두 가지다.
    /// 첫째, 노멀맵은 임포터에서 <c>Normal map</c> 타입으로 바꿔줘야 하는데 이걸 빠뜨리면
    /// 셰이딩이 조용히 틀어진다. 둘째, 타일링을 1로 두면 300m 지면에 텍스처 한 장이 늘어나
    /// 뭉개진다. 둘 다 눈에 잘 안 띄는 실수라 코드로 못 박아둔다.
    /// </summary>
    public static class GroundMaterialTool
    {
        private const string TextureFolder =
            "Assets/Project/City/Downtown City MegaKit[Standard]/Textures";
        private const string OutputFolder = "Assets/Project/City/Materials";
        private const string MaterialPath = OutputFolder + "/M_Ground_Concrete.mat";

        /// <summary>텍스처 한 장이 덮을 실제 거리(m). 지면 크기와 무관하게 이 밀도를 유지한다.</summary>
        private const float MetersPerTile = 8f;

        /// <summary>지면 박스 한 변의 대략적인 길이. 타일링 횟수를 계산하는 기준.</summary>
        private const float AssumedGroundSpan = 324f;

        [MenuItem("Momentum/지면 머티리얼 만들기", false, 120)]
        private static void CreateGroundMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[GroundMaterialTool] URP Lit 셰이더를 찾을 수 없다. URP 프로젝트가 맞는지 확인할 것.");
                return;
            }

            var baseMap = LoadTexture("T_Concrete_BaseColor.png");
            var normalMap = LoadTexture("T_Concrete_Normal.png");
            if (baseMap == null) return;

            if (normalMap != null) EnsureNormalMapImport(normalMap);

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Project/City", "Materials");

            // 이미 있으면 새로 만들지 않고 갱신한다. 두 번 눌러도 사본이 생기지 않게.
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool isNew = material == null;
            if (isNew) material = new Material(shader);
            else material.shader = shader;

            float tiling = AssumedGroundSpan / MetersPerTile;
            var scale = new Vector2(tiling, tiling);

            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", scale);

            if (normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
                material.SetTextureScale("_BumpMap", scale);
                material.EnableKeyword("_NORMALMAP");
            }

            // ORM 텍스처는 쓰지 않는다.
            // URP Lit의 Metallic Gloss Map은 R에 metallic, A에 smoothness를 기대하는데
            // 이 에셋의 ORM은 (Occlusion, Roughness, Metallic)이라 채널이 어긋난다.
            // 거친 콘크리트 지면은 metallic 0 / 낮은 smoothness면 충분하므로 슬라이더로 대신한다.
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.12f);

            if (isNew) AssetDatabase.CreateAsset(material, MaterialPath);
            else EditorUtility.SetDirty(material);

            AssetDatabase.SaveAssets();
            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);

            Debug.Log(
                $"[GroundMaterialTool] {(isNew ? "생성" : "갱신")}: {MaterialPath}\n" +
                $"  타일링 {tiling:F0}× (약 {MetersPerTile:F0}m마다 반복)" +
                (normalMap == null ? "\n  노멀맵 없음" : string.Empty) +
                "\n  CityLayoutSettings의 groundMaterial에 넣을 것.", material);
        }

        private static Texture2D LoadTexture(string fileName)
        {
            string path = $"{TextureFolder}/{fileName}";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                Debug.LogError($"[GroundMaterialTool] 텍스처를 찾을 수 없다: {path}");
            return texture;
        }

        /// <summary>
        /// 노멀맵을 Normal map 타입으로 임포트되게 고친다.
        /// 기본(Default) 타입으로 두면 Unity가 경고만 내고 잘못된 법선으로 셰이딩한다.
        /// </summary>
        private static void EnsureNormalMapImport(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            if (importer.textureType == TextureImporterType.NormalMap) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
            Debug.Log($"[GroundMaterialTool] '{texture.name}'을 Normal map 타입으로 다시 임포트했다.");
        }
    }
}
