using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class WindSound : MonoBehaviour
{
    public AudioClip windClip;               
    public InteractiveBody flock;           

    [Header("Response")]
    public float quietAcceleration = 4f;     // At or below this average acceleration the wind is at its quietest
    public float fullAcceleration = 20f;     // At or above this the wind is at full strength
    public float responseSeconds = 0.3f;     // How quickly the wind rises and falls

    [Header("Sound")]
    [Range(0f, 1f)] public float minVolume = 0.05f;
    [Range(0f, 1f)] public float maxVolume = 0.8f;
    public float minPitch = 0.9f;            // Slightly lower when calm
    public float maxPitch = 1.2f;            // Slightly higher when strong

    AudioSource source;
    float intensity;                         // 0 = calm, 1 = full wind

    void Start()
    {
        if (flock == null) flock = FindObjectOfType<InteractiveBody>();
        source = GetComponent<AudioSource>();
        source.clip = windClip;
        source.loop = true;
        source.volume = minVolume;
        if (windClip != null) source.Play();
        else Debug.LogWarning("WindSound has no wind clip assigned.");
    }

    void Update()
    {
        // More acceleration = stronger wind, smoothed so it swells and fades instead of flickering.
        float acceleration = flock != null ? flock.AverageAcceleration : 0f;
        float target = Mathf.InverseLerp(quietAcceleration, fullAcceleration, acceleration);
        intensity = Mathf.Lerp(intensity, target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, responseSeconds)));

        source.volume = Mathf.Lerp(minVolume, maxVolume, intensity);
        source.pitch = Mathf.Lerp(minPitch, maxPitch, intensity);
    }
}