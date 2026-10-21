using UnityEditor;

namespace Momentum.EditorTools
{
    // 추가 생성 (2026-10-21)
    /// <summary>
    /// 공중 트릭 클립 임포트 설정 — <c>AnimationFold/Tricks</c> 폴더의 FBX를 들여올 때 자동으로 적용한다.
    ///
    /// <b>왜 필요한가.</b> 공중제비의 "도는 회전"과 "몸이 오르내리는 높이"는 Humanoid 클립에서 루트 모션으로 빠진다.
    /// 플레이어는 루트 모션을 직접 받는 OnAnimatorMove(PlayerRootMotionRelay)가 있어서, 그대로 두면 회전이
    /// 버려져 몸이 안 돌고 제자리에서 팔다리만 움직인다. 그래서 루트 회전·높이·수평 이동을 전부
    /// <b>포즈에 굽혀(Bake Into Pose)</b> 몸이 화면에서 실제로 돌게 하고, 실제 이동은 물리(Rigidbody)가 맡게 한다.
    /// 한 번 재생하는 동작이라 반복(Loop Time)도 끈다.
    /// </summary>
    public class TrickClipImport : AssetPostprocessor
    {
        private const string Folder = "/AnimationFold/Tricks/";

        private void OnPreprocessAnimation()
        {
            if (!assetPath.Replace('\\', '/').Contains(Folder)) return;
            if (!(assetImporter is ModelImporter importer)) return;

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null) return;

            foreach (var clip in clips)
            {
                clip.loopTime = false;
                clip.lockRootRotation = true;      // 회전을 포즈에 — 몸이 실제로 돈다
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;       // 높이를 포즈에 — 웅크렸다 펴는 모양 그대로
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;    // 수평 이동은 버린다 — 날아가는 건 물리 몫
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
