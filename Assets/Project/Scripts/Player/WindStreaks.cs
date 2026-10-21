using UnityEngine;
using UnityEngine.Rendering;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-21)
    /// <summary>
    /// 스윙 바람 — 빠르게 날 때 캐릭터 주변을 스쳐 지나가는 옅은 바람 줄기(인섬니악 스파이더맨 웹스윙 느낌).
    ///
    /// <b>방식.</b> 손발에 꼬리를 다는 대신, 몸 <b>앞쪽 공중</b>에 바람 조각(파티클)을 뿌리고 그 자리에 <b>멈춰 두면</b>
    /// 플레이어가 그 사이를 빠르게 지나가며 바람이 몸 옆을 스쳐 뒤로 흘러가는 것처럼 보인다.
    /// 파티클은 월드 공간에 멈춰 있고, 렌더러의 <c>cameraVelocityScale</c>(카메라가 움직인 만큼 늘리기)로
    /// 움직이는 방향으로 길게 늘어난다 — 빠를수록 긴 줄기, 느리면 짧아진다. 유니티 내장 ParticleSystem만 쓴다.
    ///
    /// <b>화면을 가리지 않게.</b> 아주 옅게(가산 합성, 낮은 투명도), 짧게 살고(0.25초), 태어나고 사라질 때 서서히 흐려진다.
    /// 카메라 바로 앞에서 커지지 않도록 몸 주변 껍데기에서만 태어나고, 땅에 있으면 안 나온다.
    ///
    /// 텍스처: Kenney Particle Pack(CC0)의 trace(부드러운 바람결 줄무늬). 검은 바탕 흰 무늬라 가산 합성으로 쓴다.
    /// <see cref="GrappleGun"/>이 실행 중에 붙이고 텍스처를 넘겨준다.
    /// </summary>
    public class WindStreaks : MonoBehaviour
    {
        [Header("언제 나오나")]
        [Tooltip("이 속도(m/s)부터 바람이 나오기 시작한다.")]
        [Min(0f)][SerializeField] private float startSpeed = 20f;
        [Tooltip("이 속도(m/s)에서 바람이 가장 많다.")]
        [Min(1f)][SerializeField] private float fullSpeed = 40f;
        [Tooltip("가장 많을 때 초당 바람 조각 수.")]
        [Min(0f)][SerializeField] private float maxRate = 45f;

        [Header("어디서 나오나")]
        [Tooltip("바람이 태어나는 곳을 진행 방향 앞으로 이만큼(m) 당긴다. 앞에서 태어나 몸 옆을 지나 뒤로 흘러간다.")]
        [Min(0f)][SerializeField] private float spawnAhead = 4f;
        [Tooltip("몸 중심에서 바람이 태어나는 껍데기 반지름(m). 너무 작으면 몸에 겹치고, 크면 바람이 몸과 따로 논다.")]
        [Min(0.1f)][SerializeField] private float spawnRadius = 2.2f;

        [Header("모양")]
        [Tooltip("바람 조각 굵기(m). 늘어나는 길이는 속도가 정한다.")]
        [SerializeField] private Vector2 streakWidth = new Vector2(0.25f, 0.55f);
        [Tooltip("카메라가 움직인 만큼 늘이는 배율. 클수록 긴 줄기(30 m/s에서 0.06이면 약 1.8 m).")]
        [Min(0f)][SerializeField] private float stretchByCamera = 0.06f;
        [Tooltip("바람 조각 수명(초). 짧을수록 화면에 덜 남는다.")]
        [Min(0.05f)][SerializeField] private float lifetime = 0.25f;
        [Tooltip("바람 색과 최대 진하기(알파). 가산 합성이라 낮게 둬도 잘 보인다.")]
        [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.22f);

        private Rigidbody _body;
        private PlayerMovement _movement;
        private ParticleSystem _particles;
        private Transform _emitter;

        /// <summary>붙이는 쪽에서 텍스처를 넘긴다. 텍스처가 없으면 아무것도 만들지 않는다.</summary>
        public void Init(Texture texture)
        {
            _body = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            if (texture == null || _particles != null) return;
            _particles = CreateParticles(texture);
        }

        // 추가 생성 (2026-10-21)
        /// <summary>
        /// 한 번에 바람 조각을 확 뿌린다 — 슬링샷 발사처럼 "팡" 튀어 나가는 순간용.
        /// 속도로 나오는 평소 바람과 같은 자리(진행 방향 앞 껍데기)에서 태어나므로 모양이 같다.
        /// </summary>
        public void Burst(int count)
        {
            if (_particles == null || _body == null || count <= 0) return;
            Vector3 velocity = _body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > 0.1f)
                _emitter.position = _body.position + Vector3.up * 1f + velocity / speed * spawnAhead;
            _particles.Emit(count);
        }

        private void OnDisable()
        {
            if (_particles != null) _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// 속도에 맞춰 초당 개수를 정하고, 태어나는 자리를 진행 방향 앞으로 옮긴다.
        /// LateUpdate인 이유: 몸이 움직인 뒤 그 프레임 위치 기준으로 앞을 잡아야 한 프레임씩 뒤처지지 않는다.
        /// </summary>
        private void LateUpdate()
        {
            if (_particles == null || _body == null) return;

            Vector3 velocity = _body.linearVelocity;
            float speed = velocity.magnitude;
            bool grounded = _movement != null && _movement.IsGrounded;
            float amount = grounded ? 0f : Mathf.InverseLerp(startSpeed, fullSpeed, speed);

            var emission = _particles.emission;
            emission.rateOverTime = amount * maxRate;

            if (speed > 0.1f)
                _emitter.position = _body.position + Vector3.up * 1f + velocity / speed * spawnAhead;
        }

        /// <summary>
        /// 파티클 시스템을 코드로 만든다. 월드 공간에 멈춰 있는 바람 조각, 늘이기(Stretch) 렌더, 가산 합성.
        /// 부모(플레이어)를 따라가지 않도록 월드 공간 시뮬레이션이다 — 그래야 플레이어가 그 사이를 지나간다.
        /// </summary>
        private ParticleSystem CreateParticles(Texture texture)
        {
            var go = new GameObject("WindStreaks");
            go.transform.SetParent(transform, false);
            _emitter = go.transform;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = lifetime;
            main.startSpeed = 0f; // 멈춰 있는 바람 — 지나가는 건 플레이어다
            main.startSize = new ParticleSystem.MinMaxCurve(streakWidth.x, streakWidth.y);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = spawnRadius;
            shape.radiusThickness = 0.35f; // 껍데기 바깥쪽에서만 — 몸 한가운데엔 안 생긴다

            // 태어날 때·사라질 때 서서히(뚝 나타났다 사라지면 깜빡여 보인다).
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.3f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.cameraVelocityScale = stretchByCamera; // 카메라(=플레이어)가 빠를수록 길게
            renderer.velocityScale = 0f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = CreateMaterial(texture);

            ps.Play();
            return ps;
        }

        /// <summary>URP 파티클 Unlit, 가산 합성 — 검은 바탕 흰 무늬 텍스처(Kenney)를 그대로 쓴다.</summary>
        private static Material CreateMaterial(Texture texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var material = new Material(shader) { name = "WindStreaks (Additive)" };
            material.SetFloat("_Surface", 1f); // 반투명
            material.SetFloat("_Blend", 2f);   // 가산(Additive)
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
