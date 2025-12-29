using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

public sealed class DilatedPupilsDebuff : MonoBehaviour
{
    private const float PupilSizeMultiplier = 3f;
    private const int Priority = 5;
    private const float SpringSpeedIn = 10f;
    private const float SpringDampIn = 0.5f;
    private const float SpringSpeedOut = 5f;
    private const float SpringDampOut = 0.3f;
    private const float RefreshInterval = 0.1f;
    private static readonly VepLogger LOG = VepLogger.Create<DilatedPupilsDebuff>();

    private PlayerAvatar? playerAvatar;
    private float refreshTimer;

    public bool IsActive { get; private set; }

    private void Awake()
    {
        playerAvatar = GetComponent<PlayerAvatar>();
    }

    private void Update()
    {
        if (!IsActive || playerAvatar == null) return;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            ApplyPupilEffect();
            refreshTimer = RefreshInterval;
        }
    }

    public void ApplyDebuff(bool dilated)
    {
        IsActive = dilated;
        LOG.Debug($"Applying dilated pupils debuff: {dilated}");

        if (dilated)
        {
            refreshTimer = 0f;
            ApplyPupilEffect();
        }
    }

    private void ApplyPupilEffect()
    {
        if (playerAvatar == null) return;

        playerAvatar.OverridePupilSize(
            PupilSizeMultiplier,
            Priority,
            SpringSpeedIn,
            SpringDampIn,
            SpringSpeedOut,
            SpringDampOut,
            RefreshInterval + 0.05f
        );
    }
}