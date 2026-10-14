using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

public sealed class HallucinationNameplate : MonoBehaviour
{
    private const float NameplateHeight = 1.9f;
    private const float MaxVisibleDistance = 8f;
    private const float FadeSpeed = 25f;
    private const float MinFontSize = 8f;
    private const float MaxFontSize = 20f;

    // Hallucination names have always peaked here, dimmer than a real teammate's nameplate (0.5).
    private const float PeakAlpha = 0.2f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationNameplate>();

    private Transform _controllerTransform = null!;
    private HallucinationGaze _gaze = null!;
    private WorldSpaceUIPlayerName? _nameplate;
    private PlayerAvatar _imitatedPlayer = null!;
    private bool _wasVisible;

    internal void Initialize(Transform controllerTransform, PlayerAvatar imitatedPlayer, HallucinationGaze gaze)
    {
        _controllerTransform = controllerTransform;
        _imitatedPlayer = imitatedPlayer;
        _gaze = gaze;

        CreateNameplate();
    }

    /// <summary>
    ///     Called from LateUpdate.
    /// </summary>
    public void UpdateNameplate()
    {
        if (_nameplate == null || _controllerTransform == null || Camera.main == null) return;

        var worldPos = _controllerTransform.position + Vector3.up * NameplateHeight;
        var cameraPos = Camera.main.transform.position;
        var distance = Vector3.Distance(worldPos, cameraPos);

        var visiblePart = _gaze.VisiblePart;
        var isVisible = visiblePart != null;
        if (isVisible != _wasVisible)
        {
            LOG.Debug($"Nameplate of the Hallucination of {_imitatedPlayer.playerName} " +
                      $"{(isVisible ? $"shown, {visiblePart} visible" : "hidden")}");
            _wasVisible = isVisible;
        }

        var distanceAlpha = Mathf.Clamp01(1f - distance / MaxVisibleDistance);
        var targetAlpha = isVisible ? distanceAlpha * PeakAlpha : 0f;

        var currentColor = _nameplate.text.color;
        var newAlpha = Mathf.Lerp(currentColor.a, targetAlpha, Time.deltaTime * FadeSpeed);
        _nameplate.text.color = new Color(1f, 1f, 1f, newAlpha);

        var screenPos = SemiFunc.UIWorldToCanvasPosition(worldPos);
        var rect = _nameplate.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = screenPos;
        }

        _nameplate.text.fontSize = Mathf.Clamp(MaxFontSize - distance, MinFontSize, MaxFontSize);
    }

    private void CreateNameplate()
    {
        if (_imitatedPlayer == null || WorldSpaceUIParent.instance == null) return;

        var prefab = WorldSpaceUIParent.instance.playerNamePrefab;
        if (prefab == null) return;

        var nameplateGO = Instantiate(prefab, WorldSpaceUIParent.instance.transform);
        _nameplate = nameplateGO.GetComponent<WorldSpaceUIPlayerName>();

        if (_nameplate != null)
        {
            // The game only calls Show on the real player's nameplate, so its Update would fade this copy every frame,
            // alive or dead, and pin it to the Imitated Player instead of the Hallucination.
            _nameplate.enabled = false;
            _nameplate.text.text = _imitatedPlayer.playerName;
        }
    }

    private void OnDestroy()
    {
        if (_nameplate != null)
        {
            Destroy(_nameplate.gameObject);
        }
    }
}
