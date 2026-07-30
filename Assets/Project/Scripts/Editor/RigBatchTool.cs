using UnityEditor;
using UnityEngine;

namespace Momentum.Player.EditorTools
{
    /// <summary>
    /// 선택한 애니메이션 FBX들의 Rig를 한 번에 Humanoid로 바꾼다.
    ///
    /// 외부에서 받은 모션캡처·모듈러 애니메이션 팩은 자기 스켈레톤 이름으로만 되어 있고
    /// Rig 타입도 기본적으로 Humanoid가 아닌 경우가 많다. Humanoid로 바꿔야 뼈 이름이
    /// 달라도 우리 캐릭터(Security Officer)에 재타겟팅된다. 파일마다 인스펙터를 열어
    /// 반복하는 대신 여러 개를 한 번에 선택해서 처리한다.
    /// </summary>
    public static class RigBatchTool
    {
        private const string MenuPath = "Momentum/선택한 애니메이션 FBX를 Humanoid로 설정";

        [MenuItem(MenuPath, false, 140)]
        private static void SetHumanoid()
        {
            int changed = 0;
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) ||
                    !path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
                changed++;

                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
                string status = avatar != null && avatar.isValid && avatar.isHuman
                    ? "정상"
                    : "자동 매핑 실패 — Rig 탭의 Configure로 직접 확인할 것";
                Debug.Log($"[RigBatchTool] {obj.name} → Humanoid ({status})", obj);
            }

            if (changed == 0)
                Debug.LogWarning("[RigBatchTool] 프로젝트 창에서 FBX 파일을 선택하고 실행할 것.");
            else
                Debug.Log($"[RigBatchTool] {changed}개 처리 완료.");
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.objects.Length > 0;
    }
}
