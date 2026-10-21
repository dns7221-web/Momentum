using UnityEngine;

namespace Momentum.Player
{
    // 추가 생성 (2026-10-21)
    /// <summary>
    /// 이동 소리 — 줄 발사·붙음·스윙 휙·놓기·슬링샷 당김/발사·바람·다이브·착지를 한 곳에서 낸다.
    ///
    /// <b>방식.</b> 다른 컴포넌트를 고치지 않고 <b>상태가 바뀌는 순간</b>을 여기서 지켜본다
    /// (줄이 붙었나, 스윙 바닥을 지났나, 슬링샷 충전이 시작됐나 …). 소리를 내는 쪽이 한 군데라
    /// 볼륨·음높이를 한 곳에서 맞출 수 있다. 줄 발사 소리는 <b>진짜 웹스윙이 시작될 때만</b> 낸다(슬링샷·포인트 런치·던지기의 발사 동작엔 안 남).
    ///
    /// <b>소리 두 갈래.</b> 짧은 효과음은 <c>AudioSource.PlayOneShot</c>(겹쳐도 끊기지 않음), 바람은 반복 재생하는
    /// 별도 AudioSource 하나의 볼륨·음높이를 속도로 움직인다. 같은 효과음이 반복돼 보이지 않게 음높이를 조금씩 흔든다.
    ///
    /// 에디터에서 이 컴포넌트를 붙이면(Reset) <c>Assets/Project/Audio</c>에서 이름이 맞는 클립을 자동으로 채운다.
    /// </summary>
    public class TraversalAudio : MonoBehaviour
    {
        [Header("효과음 (비우면 그 소리만 안 난다)")]
        [SerializeField] private AudioClip webFire = null;
        [SerializeField] private AudioClip webAttach = null;
        [SerializeField] private AudioClip swingWhoosh = null;
        [Tooltip("줄 놓기 소리. 비우면 swingWhoosh를 조금 높게 쓴다.")]
        [SerializeField] private AudioClip releaseWhoosh = null;
        [SerializeField] private AudioClip slingStretch = null;
        [SerializeField] private AudioClip slingLaunch = null;
        [SerializeField] private AudioClip landThud = null;

        [Header("바람 (반복)")]
        [SerializeField] private AudioClip windLoop = null;
        [Tooltip("다이브 중 바람. 비우면 windLoop을 높은 음으로 쓴다.")]
        [SerializeField] private AudioClip diveWind = null;
        [Tooltip("바람이 들리기 시작하는 속도 / 가장 큰 속도(m/s).")]
        [SerializeField] private Vector2 windSpeedRange = new Vector2(10f, 40f);
        [Range(0f, 1f)][SerializeField] private float windMaxVolume = 0.55f;
        [Tooltip("다이브 중 바람 음높이 배율(diveWind가 비었을 때).")]
        [Range(1f, 2f)][SerializeField] private float divePitch = 1.35f;

        [Header("세기")]
        [Range(0f, 1f)][SerializeField] private float masterVolume = 0.8f;
        [Tooltip("효과음 음높이를 이만큼 무작위로 흔든다 — 같은 소리가 반복돼 들리지 않게.")]
        [Range(0f, 0.3f)][SerializeField] private float pitchJitter = 0.08f;
        [Tooltip("스윙 바닥을 이 속도(m/s) 이상으로 지날 때만 휙 소리.")]
        [Min(0f)][SerializeField] private float swingWhooshMinSpeed = 14f;
        // 추가 생성 (2026-10-21) — 슬링샷은 휠로 한 칸 당길 때마다 짧게, 최대에서 한 번 세게, 그 뒤엔 조용히
        [Tooltip("슬링샷 한 칸 당길 때 sling_stretch를 앞에서부터 이만큼(초)만 재생한다.")]
        [Min(0.05f)][SerializeField] private float stretchNotchLength = 0.3f;
        [Tooltip("한 칸 소리 음높이 (충전 0일 때, 최대일 때). 당길수록 팽팽하게 올라간다.")]
        [SerializeField] private Vector2 stretchPitch = new Vector2(0.85f, 1.25f);
        [Tooltip("최대까지 당긴 순간 소리 길이(초)·음높이.")]
        [Min(0.05f)][SerializeField] private float stretchFullLength = 0.6f;
        [Range(0.5f, 2f)][SerializeField] private float stretchFullPitch = 1.35f;
        [Tooltip("이 낙하 속도(m/s) 이상으로 땅에 닿아야 착지 소리(착지 먼지와 같은 기준).")]
        [Min(0f)][SerializeField] private float landMinFallSpeed = 9f;

        private AudioSource _oneShot;
        private AudioSource _wind;
        private AudioSource _stretch; // 추가 생성 (2026-10-21) — 칸마다 앞부분만 끊어 재생하려고 따로
        private float _stretchStopAt;
        private float _lastCharge;
        private Rigidbody _body;
        private PlayerMovement _movement;
        private GrappleGun _gun;
        private SwingConstraint _swing;
        private GrappleZip _zip;
        private WebSlingshot _sling;
        private SwingDive _dive;

        private bool _wasAttached, _wasSwinging, _wasZipping, _wasCharging, _wasLaunching, _wasGrounded = true;
        private float _lastBottomSide;
        private float _fallSpeed;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _movement = GetComponent<PlayerMovement>();
            _gun = GetComponent<GrappleGun>();
            _swing = GetComponent<SwingConstraint>();
            _zip = GetComponent<GrappleZip>();
            _sling = GetComponent<WebSlingshot>();

            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 0f; // 3인칭 플레이어 소리라 2D로 — 카메라 거리와 상관없이 같은 크기

            _wind = gameObject.AddComponent<AudioSource>();
            _wind.playOnAwake = false;
            _wind.loop = true;
            _wind.spatialBlend = 0f;
            _wind.volume = 0f;

            _stretch = gameObject.AddComponent<AudioSource>();
            _stretch.playOnAwake = false;
            _stretch.spatialBlend = 0f;
        }


        /// <summary>매 프레임 상태 변화를 보고 소리를 낸다. 바람은 속도로 볼륨을 맞춘다.</summary>
        private void Update()
        {
            if (_body == null) return;
            if (_dive == null) _dive = GetComponent<SwingDive>(); // GrappleGun이 실행 중에 붙인다

            Vector3 velocity = _body.linearVelocity;
            float speed = velocity.magnitude;
            bool grounded = _movement != null && _movement.IsGrounded;

            // 줄 붙음
            bool attached = _gun != null && _gun.IsAttached;
            if (attached && !_wasAttached) Play(webAttach, 0.9f);
            _wasAttached = attached;

            // 스윙 바닥 지나기 — 몸이 지지점 바로 아래를 지나는 순간(지지점 기준 앞뒤가 바뀜) "휙"
            bool swinging = _swing != null && _swing.RopeLength > 0f;
            // 수정 (2026-10-21) — 줄 발사 소리는 웹스윙이 실제로 시작되는 순간에만
            if (swinging && !_wasSwinging) Play(webFire, 0.8f);
            if (swinging)
            {
                Vector3 fromPivot = _body.position - _swing.SwingPivot;
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
                float side = Mathf.Sign(Vector3.Dot(Vector3.ProjectOnPlane(fromPivot, Vector3.up), horizontalVelocity));
                if (_wasSwinging && side > 0f && _lastBottomSide < 0f && speed >= swingWhooshMinSpeed)
                    Play(swingWhoosh, Mathf.InverseLerp(swingWhooshMinSpeed, 35f, speed) * 0.5f + 0.5f);
                _lastBottomSide = side;
            }
            // 줄 놓기 (공중에서)
            if (!swinging && _wasSwinging && !grounded)
                Play(releaseWhoosh != null ? releaseWhoosh : swingWhoosh, 0.7f, releaseWhoosh != null ? 1f : 1.15f);
            _wasSwinging = swinging;

            // 포인트 런치·웹 집 시작 — 줄을 쏘는 소리
            bool zipping = _zip != null && _zip.IsZipping;
            _wasZipping = zipping;

            // 슬링샷 당김 · 발사
            // 수정 (2026-10-21) — 당긴 칸마다 소리, 최대에서 한 번, 그 뒤 더 당겨도(이미 최대) 조용
            bool charging = _sling != null && _sling.IsCharging;
            float charge = charging ? _sling.Charge01 : 0f;
            if (charging && _wasCharging && charge > _lastCharge + 0.001f)
            {
                if (charge >= 0.999f) PlayStretch(stretchFullLength, stretchFullPitch, 1f);
                else PlayStretch(stretchNotchLength, Mathf.Lerp(stretchPitch.x, stretchPitch.y, charge), 0.7f);
            }
            if (!charging && _stretch.isPlaying) _stretch.Stop();
            _lastCharge = charge;
            _wasCharging = charging;
            if (_stretch.isPlaying && Time.time >= _stretchStopAt) _stretch.Stop();
            bool launching = _sling != null && _sling.IsLaunching;
            if (launching && !_wasLaunching) Play(slingLaunch, 1f);
            _wasLaunching = launching;

            // 착지 — 직전 공중 프레임의 낙하 속도로 판단(닿는 순간엔 이미 0)
            if (!grounded) _fallSpeed = Mathf.Max(0f, -velocity.y);
            else if (!_wasGrounded && _fallSpeed >= landMinFallSpeed)
                Play(landThud, Mathf.InverseLerp(landMinFallSpeed, 30f, _fallSpeed) * 0.5f + 0.5f);
            _wasGrounded = grounded;

            UpdateWind(speed, grounded);
        }

        /// <summary>바람 반복 소리 — 공중에서 빠를수록 크게, 다이브 중엔 다이브 바람(또는 높은 음).</summary>
        private void UpdateWind(float speed, bool grounded)
        {
            bool diving = _dive != null && _dive.IsDiving;
            AudioClip clip = diving && diveWind != null ? diveWind : windLoop;
            if (clip == null) return;
            if (_wind.clip != clip)
            {
                _wind.clip = clip;
                _wind.time = Random.Range(0f, clip.length); // 같은 지점부터 시작해 반복이 들리지 않게
                _wind.Play();
            }
            else if (!_wind.isPlaying) _wind.Play();

            float target = grounded ? 0f : Mathf.InverseLerp(windSpeedRange.x, windSpeedRange.y, speed) * windMaxVolume * masterVolume;
            _wind.volume = Mathf.MoveTowards(_wind.volume, target, 2f * Time.deltaTime);
            float pitch = (diving && diveWind == null ? divePitch : 1f) + Mathf.InverseLerp(windSpeedRange.x, windSpeedRange.y, speed) * 0.15f;
            _wind.pitch = Mathf.MoveTowards(_wind.pitch, pitch, Time.deltaTime);
        }

        // 추가 생성 (2026-10-21)
        /// <summary>sling_stretch를 처음부터 <paramref name="length"/>초만 재생한다(다음 칸이 오면 처음부터 다시).</summary>
        private void PlayStretch(float length, float pitch, float volume)
        {
            if (slingStretch == null) return;
            _stretch.clip = slingStretch;
            _stretch.pitch = pitch;
            _stretch.volume = volume * masterVolume;
            _stretch.time = 0f;
            _stretch.Play();
            _stretchStopAt = Time.time + length / Mathf.Max(0.1f, pitch);
        }

        /// <summary>효과음 한 번. 음높이를 조금 흔들어 같은 소리가 반복돼 들리지 않게 한다.</summary>
        private void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null) return;
            _oneShot.pitch = pitch + Random.Range(-pitchJitter, pitchJitter);
            _oneShot.PlayOneShot(clip, volume * masterVolume);
        }

#if UNITY_EDITOR
        /// <summary>에디터에서 컴포넌트를 붙이면 Assets/Project/Audio에서 이름이 같은 클립을 찾아 채운다.</summary>
        private void Reset()
        {
            webFire = Find("web_fire"); webAttach = Find("web_attach");
            swingWhoosh = Find("swing_whoosh"); releaseWhoosh = Find("release_whoosh");
            slingStretch = Find("sling_stretch"); slingLaunch = Find("sling_launch");
            landThud = Find("land_thud"); windLoop = Find("wind_loop"); diveWind = Find("dive_wind");
        }

        private static AudioClip Find(string name)
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(name + " t:AudioClip", new[] { "Assets/Project/Audio" }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return null;
        }
#endif
    }
}
