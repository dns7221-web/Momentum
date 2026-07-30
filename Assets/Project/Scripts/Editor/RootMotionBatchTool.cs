using System.Text;
using UnityEditor;
using UnityEngine;

namespace Momentum.Player.EditorTools
{
    /// <summary>
    /// 선택한 애니메이션 FBX의 루트 트랜스폼 설정을 "제자리 포즈"로 통일한다.
    ///
    /// Momentum은 위치와 회전을 전부 Rigidbody가 갖고 애니메이션은 포즈만 얹는다
    /// (README 설계). 그러면 Apply Root Motion이 꺼져 있으므로 <b>추출된 루트 모션은
    /// 버려진다</b> — 즉 Bake Into Pose를 끄는 것이 그 성분을 지우는 방법이 된다.
    ///
    /// 특히 Mixamo에서 받은 클립은 기본값이 <c>Based Upon = Center of Mass</c>인데,
    /// 이 설정은 루트의 수평 기준점을 매 프레임 무게중심에 맞춘다. Bake Into Pose가
    /// 함께 켜져 있으면 루트는 제자리에 고정되므로 무게중심이 흔들리는 만큼 몸통이
    /// 반대로 밀리고, 그 결과 <b>걸을 때 좌우로 흔들리는 증상</b>이 생긴다. 달릴 때
    /// 무게중심 진폭이 커서 더 심해지는 것이 이 원인의 특징이다.
    ///
    /// 수직 성분만 클립 종류에 따라 갈린다. 걷기·뛰기는 상하 바운스를 포즈에 남겨야
    /// 하고(없으면 발이 지면을 문지르는 미끄러짐이 되고 걷는 느낌이 죽는다),
    /// 점프는 버려야 한다(물리가 올리는 높이와 겹쳐 두 배로 뛰어오른다).
    /// 그래서 메뉴가 지상용·공중용 둘로 나뉘어 있다.
    /// </summary>
    public static class RootMotionBatchTool
    {
        private const string GroundMenu = "Momentum/제자리 포즈로 고정/지상 이동 (걷기·뛰기)";
        private const string AirMenu = "Momentum/제자리 포즈로 고정/점프·공중";

        /// <summary>
        /// 걷기·뛰기용. 상하 바운스만 포즈에 남긴다.
        /// </summary>
        [MenuItem(GroundMenu, false, 150)]
        private static void StripForGround() => StripRootMotion(keepVerticalBounce: true);

        /// <summary>
        /// 점프·낙하용. 수직 성분까지 버린다.
        ///
        /// 점프 클립에는 골반이 1m가량 솟는 움직임이 들어 있다. 그걸 포즈에 남기면
        /// 물리가 올리는 높이 위에 애니메이션이 올리는 높이가 겹쳐서 <b>두 배로 뛰어오르고
        /// 메시가 캡슐에서 떨어져 나간다.</b> 높이는 Rigidbody 하나만 갖는 게 맞다.
        /// </summary>
        [MenuItem(AirMenu, false, 151)]
        private static void StripForAir() => StripRootMotion(keepVerticalBounce: false);

        private static void StripRootMotion(bool keepVerticalBounce)
        {
            int files = 0, clips = 0;
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) ||
                    !path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

                // 인스펙터에서 클립을 한 번도 건드리지 않은 파일은 clipAnimations가 비어 있고
                // 임포터가 만든 기본 클립만 있다. 그 경우 기본값을 복사해 와야 수정할 대상이 생긴다.
                var list = importer.clipAnimations;
                if (list == null || list.Length == 0)
                    list = importer.defaultClipAnimations;
                if (list == null || list.Length == 0) continue;

                var log = new StringBuilder($"[RootMotionBatchTool] {obj.name}");
                foreach (var clip in list)
                {
                    log.Append($"\n  · {clip.name}: {Describe(clip)}");

                    // 회전: 추출해서 버린다 → 몸통 방향이 캡슐에 고정된다.
                    clip.lockRootRotation = false;
                    clip.keepOriginalOrientation = true;

                    // 수직: 걷기는 포즈에 남겨야 걸음마다 오르내리는 바운스가 살아 있고,
                    // 점프는 버려야 물리가 올리는 높이와 겹치지 않는다.
                    clip.lockRootHeightY = keepVerticalBounce;
                    clip.keepOriginalPositionY = true;
                    clip.heightFromFeet = false;

                    // 수평: 추출해서 버린다 → 전진 이동도, 좌우 흔들림도 함께 사라진다.
                    clip.lockRootPositionXZ = false;
                    clip.keepOriginalPositionXZ = true;

                    log.Append($"  →  {Describe(clip)}");
                    clips++;
                }

                importer.clipAnimations = list;
                importer.SaveAndReimport();
                files++;
                Debug.Log(log.ToString(), obj);
            }

            if (files == 0)
                Debug.LogWarning("[RootMotionBatchTool] 프로젝트 창에서 애니메이션 FBX를 선택하고 실행할 것.");
            else
                Debug.Log($"[RootMotionBatchTool] {files}개 파일 · {clips}개 클립 처리 완료. " +
                          "Loop Time·Loop Pose는 건드리지 않았다 — 점프 클립은 루프가 아니므로 " +
                          "여기서 일괄로 정할 값이 아니다.");
        }

        /// <summary>바꾸기 전후를 한 줄로 찍어 무엇이 잘못돼 있었는지 눈으로 보게 한다.</summary>
        private static string Describe(ModelImporterClipAnimation c) =>
            $"Rot[{(c.lockRootRotation ? "Bake" : "----")}/{(c.keepOriginalOrientation ? "Orig" : "Body")}] " +
            $"Y[{(c.lockRootHeightY ? "Bake" : "----")}/{(c.keepOriginalPositionY ? "Orig" : "CoM")}] " +
            $"XZ[{(c.lockRootPositionXZ ? "Bake" : "----")}/{(c.keepOriginalPositionXZ ? "Orig" : "CoM")}]";

        [MenuItem(GroundMenu, true)]
        [MenuItem(AirMenu, true)]
        private static bool Validate() => Selection.objects.Length > 0;
    }
}
