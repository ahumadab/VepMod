using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

public sealed class DilatedPupils : MonoBehaviour
{
    private const float PupilSizeMultiplier = 3f;
    private const int Priority = 5;
    private const float SpringSpeedIn = 10f;
    private const float SpringDampIn = 0.5f;
    private const float SpringSpeedOut = 5f;
    private const float SpringDampOut = 0.3f;
    private const float RefreshInterval = 0.1f;
    private static readonly VepLogger LOG = VepLogger.Create<DilatedPupils>();

    private PlayerAvatar? _playerAvatar;
    private float _refreshTimer;

    public bool IsActive { get; private set; }

    private void Awake()
    {
        _playerAvatar = GetComponent<PlayerAvatar>();
    }

    private void Update()
    {
        if (!IsActive || _playerAvatar == null) return;

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f)
        {
            ApplyPupilEffect();
            _refreshTimer = RefreshInterval;
        }
    }

    public void Apply(bool dilated)
    {
        IsActive = dilated;
        LOG.Debug($"Dilated Pupils: {dilated}");

        if (dilated)
        {
            _refreshTimer = 0f;
            ApplyPupilEffect();
        }
    }

    private void ApplyPupilEffect()
    {
        if (_playerAvatar == null) return;

        _playerAvatar.OverridePupilSize(
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
