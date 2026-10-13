using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

public sealed class DroidNameplate : MonoBehaviour
{
    private const float NameplateHeight = 1.9f;
    private const float MaxVisibleDistance = 8f;
    private const float FadeSpeed = 5f;
    private const float MinFontSize = 8f;
    private const float MaxFontSize = 20f;

    // Deliberately dimmer than a real teammate's nameplate, which peaks at 0.5.
    private const float MaxAlpha = 0.2f;

    private static readonly VepLogger LOG = VepLogger.Create<DroidNameplate>();

    private Transform _controllerTransform = null!;
    private WorldSpaceUIPlayerName? _nameplate;
    private PlayerAvatar _sourcePlayer = null!;

    public void Initialize(Transform controllerTransform, PlayerAvatar sourcePlayer)
    {
        _controllerTransform = controllerTransform;
        _sourcePlayer = sourcePlayer;

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

        var direction = worldPos - cameraPos;
        var isVisible = !Physics.Raycast(cameraPos, direction.normalized, distance,
            LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);

        var distanceAlpha = Mathf.Clamp01(1f - distance / MaxVisibleDistance);
        var targetAlpha = isVisible ? distanceAlpha * MaxAlpha : 0f;

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
        if (_sourcePlayer == null || WorldSpaceUIParent.instance == null) return;

        var prefab = WorldSpaceUIParent.instance.playerNamePrefab;
        if (prefab == null) return;

        var nameplateGO = Instantiate(prefab, WorldSpaceUIParent.instance.transform);
        _nameplate = nameplateGO.GetComponent<WorldSpaceUIPlayerName>();

        if (_nameplate != null)
        {
            // The game's Update fades the name of a dead player and of one the camera is not aiming at, and moves it
            // to the player, while this nameplate follows the Hallucination: only UpdateNameplate drives it.
            _nameplate.enabled = false;
            _nameplate.text.text = _sourcePlayer.playerName;
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
