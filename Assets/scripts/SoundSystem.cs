using UnityEngine;

/// <summary>Plays the event sounds configured on the scene's SoundSystem object.</summary>
[DisallowMultipleComponent]
public sealed class SoundSystem : MonoBehaviour
{
    private const float SemitoneRatio = 1.0594631f;

    [SerializeField] private AudioSource chargeLaunch;
    [SerializeField] private AudioSource chargeImpact;
    [SerializeField] private AudioSource stoneShatter;
    [SerializeField] private AudioSource blackHoleShatter;
    [SerializeField] private AudioSource fragmentDestroy;
    [SerializeField] private AudioSource spacePress;

    private float chargeLaunchBasePitch;
    private float chargeImpactBasePitch;
    private float stoneShatterBasePitch;
    private float blackHoleShatterBasePitch;
    private float fragmentDestroyBasePitch;
    private float spacePressBasePitch;

    public static SoundSystem Instance { get; private set; }

    private void Awake()
    {
        ResolveSpacePress();
        chargeLaunchBasePitch = GetPitch(chargeLaunch);
        chargeImpactBasePitch = GetPitch(chargeImpact);
        stoneShatterBasePitch = GetPitch(stoneShatter);
        blackHoleShatterBasePitch = GetPitch(blackHoleShatter);
        fragmentDestroyBasePitch = GetPitch(fragmentDestroy);
        spacePressBasePitch = GetPitch(spacePress);
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;

        RestorePitch(chargeLaunch, chargeLaunchBasePitch);
        RestorePitch(chargeImpact, chargeImpactBasePitch);
        RestorePitch(stoneShatter, stoneShatterBasePitch);
        RestorePitch(blackHoleShatter, blackHoleShatterBasePitch);
        RestorePitch(fragmentDestroy, fragmentDestroyBasePitch);
        RestorePitch(spacePress, spacePressBasePitch);
    }

    public void PlayChargeLaunch()
    {
        PlayWithPitchStep(chargeLaunch, chargeLaunchBasePitch);
    }

    public void PlayChargeImpact()
    {
        PlayWithPitchStep(chargeImpact, chargeImpactBasePitch);
    }

    public void PlayStoneShatter()
    {
        PlayWithPitchStep(stoneShatter, stoneShatterBasePitch);
    }

    public void PlayBlackHoleShatter()
    {
        PlayWithPitchStep(blackHoleShatter, blackHoleShatterBasePitch);
    }

    public void PlayFragmentDestroy()
    {
        PlayWithPitchStep(fragmentDestroy, fragmentDestroyBasePitch);
    }

    public void PlaySpacePress()
    {
        ResolveSpacePress();
        PlayWithPitchStep(spacePress, spacePressBasePitch);
    }

    private void ResolveSpacePress()
    {
        if (spacePress != null)
            return;

        Transform soundFive = transform.Find("5");
        if (soundFive == null)
            return;

        spacePress = soundFive.GetComponent<AudioSource>();
        spacePressBasePitch = GetPitch(spacePress);
    }

    private static float GetPitch(AudioSource source)
    {
        return source != null ? source.pitch : 1f;
    }

    private static void RestorePitch(AudioSource source, float basePitch)
    {
        if (source != null)
            source.pitch = basePitch;
    }

    private static void PlayWithPitchStep(AudioSource source, float basePitch)
    {
        if (source == null)
            return;

        if (source.isPlaying)
        {
            // Raise the sound already playing; do not restart or overlap it.
            source.pitch = Mathf.Min(source.pitch * SemitoneRatio, 3f);
            return;
        }

        source.pitch = basePitch;
        source.Play();
    }
}
