using UnityEngine;

[ExecuteAlways]
public class DroneRecordSpotlight : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Light recordLight;          // Spot Light component
    [SerializeField] private Renderer coneRenderer;      // Visual cone mesh renderer

    [Header("Beam Settings")]
    [ColorUsage(true, true)]
    [SerializeField] private Color lightColor = new Color(1f, 0.847f, 0.239f); // #FFD83D
    [SerializeField] private float intensity = 4000f;
    [SerializeField] private float range = 6f;
    [SerializeField] private float spotAngle = 25f;
    [SerializeField] private float beamThickness = 1.2f; // scales cone x/y

    private bool isRecording;

    private void Reset()
    {
        recordLight = GetComponentInChildren<Light>();
        coneRenderer = GetComponentInChildren<Renderer>();
    }

    private void Awake()
    {
        ApplySettings();
        SetBeamActive(false);
    }

    private void OnValidate()
    {
        ApplySettings();
        if (!Application.isPlaying)
        {
            SetBeamActive(isRecording);
        }
    }

    private void ApplySettings()
    {
        if (recordLight != null)
        {
            recordLight.type = LightType.Spot;
            recordLight.color = lightColor;
            recordLight.intensity = intensity;
            recordLight.range = range;
            recordLight.spotAngle = spotAngle;
            recordLight.shadows = LightShadows.None; // safe default for Quest
        }

        if (coneRenderer != null)
        {
            var t = coneRenderer.transform;
            t.localScale = new Vector3(beamThickness, beamThickness, range);
        }
    }

    public void BeginRecording()
    {
        isRecording = true;
        SetBeamActive(true);
    }

    public void EndRecording()
    {
        isRecording = false;
        SetBeamActive(false);
    }

    private void SetBeamActive(bool active)
    {
        if (recordLight != null)
        {
            recordLight.enabled = active;
        }

        if (coneRenderer != null)
        {
            coneRenderer.enabled = active;
        }
    }
}