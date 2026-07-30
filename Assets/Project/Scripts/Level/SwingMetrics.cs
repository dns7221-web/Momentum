using UnityEngine;

namespace Momentum.Level
{
    /// <summary>
    /// 스윙 물리의 이론값 계산. 순수 함수만 둔다.
    ///
    /// 존재 이유는 하나다 — 레벨 메트릭을 취향으로 고르지 않고 물리에서 역산하기 위해서.
    /// 로프 길이가 정해지면 진자 주기가 정해지고, 주기가 정해지면 한 스윙의 이동 거리가
    /// 정해지고, 그것이 곧 필요한 건물 간격이다. 이 파일이 그 사슬을 코드로 옮긴 것이다.
    /// </summary>
    public static class SwingMetrics
    {
        /// <summary>지구 중력 크기. 중력 배율의 기준값.</summary>
        public const float StandardGravity = 9.81f;

        /// <summary>진자 반주기 — 한쪽 끝에서 반대쪽 끝까지. 스윙 한 번의 체감 길이.</summary>
        /// <remarks>T = 2π√(L/g) 의 절반. 소진폭 근사이므로 45°를 넘으면 실제보다 짧게 나온다.</remarks>
        public static float HalfPeriod(float ropeLength, float gravity)
        {
            if (ropeLength <= 0f || gravity <= 0f) return 0f;
            return Mathf.PI * Mathf.Sqrt(ropeLength / gravity);
        }

        /// <summary>진자 한 번의 수평 이동 거리. d = 2·L·sin θ</summary>
        /// <remarks>
        /// 앵커 간격의 상한이다. 건물 간격이 이 값을 넘으면 스윙이 이어지지 않고 추락한다.
        /// </remarks>
        public static float HorizontalReach(float ropeLength, float amplitudeDeg)
        {
            return 2f * ropeLength * Mathf.Sin(amplitudeDeg * Mathf.Deg2Rad);
        }

        /// <summary>진자 최저점 속도. v = √(2gL(1−cos θ))</summary>
        /// <remarks>
        /// 에너지 보존에서 나온다. 터널링 판정과 고정 타임스텝 결정의 근거값.
        /// </remarks>
        public static float BottomSpeed(float ropeLength, float amplitudeDeg, float gravity)
        {
            float drop = ropeLength * (1f - Mathf.Cos(amplitudeDeg * Mathf.Deg2Rad));
            return Mathf.Sqrt(2f * gravity * drop);
        }

        /// <summary>
        /// 앵커가 머리 위에 있으려면 필요한 최소 건물 높이.
        /// 로프 길이에 플레이어가 지면에 닿지 않을 여유를 더한 값.
        /// </summary>
        public static float MinBuildingHeight(float ropeLength, float groundClearance)
        {
            return ropeLength + groundClearance;
        }

        /// <summary>
        /// 물리에서 역산한 권장 도로 폭.
        /// 앵커 간 거리(격자 피치)가 수평 이동 거리와 같아지는 지점을 목표로 한다.
        /// </summary>
        public static float RecommendedStreetWidth(float ropeLength, float amplitudeDeg, float footprint)
        {
            return HorizontalReach(ropeLength, amplitudeDeg) - footprint;
        }

        /// <summary>한 물리 스텝에 이동하는 거리. 이 값이 벽 두께를 넘으면 터널링이 일어난다.</summary>
        public static float DistancePerStep(float speed, float fixedTimestep)
        {
            return speed * fixedTimestep;
        }

        /// <summary>벽을 통과하지 않기 위한 고정 타임스텝 상한.</summary>
        /// <remarks>스텝당 이동이 벽 두께의 절반을 넘지 않도록 잡는다.</remarks>
        public static float MaxSafeFixedTimestep(float speed, float wallThickness)
        {
            if (speed <= 0f) return 0.02f;
            return wallThickness * 0.5f / speed;
        }

        /// <summary>
        /// 최저점에서 로프에 걸리는 장력을 중력 대비 배수로. T/mg = 1 + v²/(gL)
        /// </summary>
        /// <remarks>로프 끊김 판정과 앵커 파괴 연출의 기준. 값이 3을 넘으면 꽤 과격한 스윙이다.</remarks>
        public static float TensionRatio(float ropeLength, float speed, float gravity)
        {
            if (ropeLength <= 0f || gravity <= 0f) return 0f;
            return 1f + (speed * speed) / (gravity * ropeLength);
        }

        /// <summary>
        /// 로프를 감았을 때의 속도 증가. 각운동량 보존 L = m·v·r 에서 v₂ = v₁·r₁/r₂
        /// </summary>
        public static float SpeedAfterReeling(float speed, float fromRadius, float toRadius)
        {
            if (toRadius <= 0f) return speed;
            return speed * (fromRadius / toRadius);
        }

        /// <summary>인스펙터 표시용으로 한 번에 계산한 묶음.</summary>
        public readonly struct Budget
        {
            public readonly float Gravity;
            public readonly float HalfPeriod;
            public readonly float Reach;
            public readonly float BottomSpeed;
            public readonly float MinHeight;
            public readonly float RecommendedStreet;
            public readonly float StepDistance;
            public readonly float SafeTimestep;
            public readonly float TensionRatio;

            public Budget(float ropeLength, float amplitudeDeg, float gravityScale, float footprint,
                          float groundClearance, float fixedTimestep, float wallThickness)
            {
                Gravity = StandardGravity * gravityScale;
                HalfPeriod = SwingMetrics.HalfPeriod(ropeLength, Gravity);
                Reach = HorizontalReach(ropeLength, amplitudeDeg);
                BottomSpeed = SwingMetrics.BottomSpeed(ropeLength, amplitudeDeg, Gravity);
                MinHeight = MinBuildingHeight(ropeLength, groundClearance);
                RecommendedStreet = RecommendedStreetWidth(ropeLength, amplitudeDeg, footprint);
                StepDistance = DistancePerStep(BottomSpeed, fixedTimestep);
                SafeTimestep = MaxSafeFixedTimestep(BottomSpeed, wallThickness);
                TensionRatio = SwingMetrics.TensionRatio(ropeLength, BottomSpeed, Gravity);
            }
        }
    }
}
