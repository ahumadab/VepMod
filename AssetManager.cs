namespace VepMod;

public sealed class AssetsManager
{
    public static AssetsManager Instance { get; } = new();

    private void AssetManager()
    {
    }

    public void RegisterAssets()
    {
        VepMod.Logger.LogDebug("Initializing VepMod...");
        RegisterValuables();
    }

    private void RegisterValuables()
    {
    }
}