using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 회색 박스 건물 한 채가 자기 자신에 대해 아는 것.
    ///
    /// 큐브를 실제 건물 프리팹으로 갈아끼우는 작업은 이 데이터만 있으면 성립한다.
    /// 교체 도구가 "격자 (2,3) 자리에 9층 20×20 건물"이라는 정보를 읽어
    /// 조건에 맞는 프리팹을 놓으면 되기 때문이다.
    /// 그래서 회색 박스 단계에서 이 컴포넌트를 붙여두는 것이 나중 비용을 없앤다.
    /// </summary>
    public class CityBuilding : MonoBehaviour
    {
        [Header("격자 좌표")]
        public int gridX;
        public int gridZ;

        [Header("치수")]
        public int floorCount;
        public float height;
        public Vector2 footprint;

        [Tooltip("얇은 벽 4장 + 옥상으로 만들어졌는지. 속 찬 큐브면 false.")]
        public bool isShell;

        /// <summary>
        /// 옥상 중앙. 앵커 후보의 기본값.
        ///
        /// 좌표를 로컬에서 만들어 <c>TransformPoint</c>로 넘긴다. 월드에서 직접 더하면
        /// 건물에 준 90° 회전이 반영되지 않아, 바닥이 정사각이 아닌 건물에서 앵커가
        /// 실제 옥상 밖으로 나간다.
        /// </summary>
        public Vector3 RoofCenter => transform.TransformPoint(new Vector3(0f, height, 0f));

        /// <summary>
        /// 옥상 네 모서리를 살짝 안으로 넣은 지점.
        /// 앵커 스코어링이 실제로 고르게 될 후보들이라 시각화 대상으로 삼는다.
        /// </summary>
        public Vector3[] RoofAnchorCandidates(float inset = 1f)
        {
            float hx = Mathf.Max(0f, footprint.x * 0.5f - inset);
            float hz = Mathf.Max(0f, footprint.y * 0.5f - inset);
            return new[]
            {
                transform.TransformPoint(new Vector3(0f, height, 0f)),
                transform.TransformPoint(new Vector3(+hx, height, +hz)),
                transform.TransformPoint(new Vector3(+hx, height, -hz)),
                transform.TransformPoint(new Vector3(-hx, height, +hz)),
                transform.TransformPoint(new Vector3(-hx, height, -hz)),
            };
        }
    }
}
