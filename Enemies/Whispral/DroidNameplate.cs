using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Contrôleur du nameplate pour HallucinationDroid.
///     Gère la création, la position et la visibilité du nom au-dessus du droid.
/// </summary>
public sealed class DroidNameplate : MonoBehaviour
{
    private const float NameplateHeight = 1.9f;
    private const float MaxVisibleDistance = 8f;
    private const float FadeSpeed = 5f;
    private const float MinFontSize = 8f;
    private const float MaxFontSize = 20f;

    private static readonly VepLogger LOG = VepLogger.Create<DroidNameplate>(true);

    private Transform _controllerTransform = null!;
    private WorldSpaceUIPlayerName? _nameplate;
    private PlayerAvatar _sourcePlayer = null!;

    /// <summary>
    ///     Initialise le contrôleur de nameplate.
    /// </summary>
    public void Initialize(Transform controllerTransform, PlayerAvatar sourcePlayer)
    {
        _controllerTransform = controllerTransform;
        _sourcePlayer = sourcePlayer;

        CreateNameplate();
    }

    /// <summary>
    ///     Met à jour le nameplate (appelé dans LateUpdate).
    /// </summary>
    public void UpdateNameplate()
    {
        if (_nameplate == null || _controllerTransform == null || Camera.main == null) return;

        var worldPos = _controllerTransform.position + Vector3.up * NameplateHeight;
        var cameraPos = Camera.main.transform.position;
        var distance = Vector3.Distance(worldPos, cameraPos);

        // Vérifier si le droid est visible (pas de mur entre la caméra et le droid)
        var direction = worldPos - cameraPos;
        var isVisible = !Physics.Raycast(cameraPos, direction.normalized, distance,
            LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);

        // Alpha basé sur la distance (1 proche, 0 à MaxVisibleDistance+)
        var distanceAlpha = Mathf.Clamp01(1f - distance / MaxVisibleDistance);
        var targetAlpha = isVisible ? distanceAlpha : 0f;

        // Lerp smooth
        var currentColor = _nameplate.text.color;
        var newAlpha = Mathf.Lerp(currentColor.a, targetAlpha, Time.deltaTime * FadeSpeed);
        _nameplate.text.color = new Color(1f, 1f, 1f, newAlpha);

        // Mettre à jour la position
        var screenPos = SemiFunc.UIWorldToCanvasPosition(worldPos);
        var rect = _nameplate.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = screenPos;
        }

        // Ajuster la taille selon la distance
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
            // Assigner le SourcePlayer pour éviter la destruction automatique
            _nameplate.playerAvatar = _sourcePlayer;
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
