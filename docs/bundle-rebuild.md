# Rebuilding `vepmod_prefabs`

Boris rebuilds the bundle in Unity (`../Repo_04`), in the AssetBundle Browser's Build tab, with the bundle
`vepmod_prefabs` selected and `Ressources/` as the output path. `build-dev.ps1` then embeds it in the DLL.

- Give the AssetBundle to each asset in its `.meta` (`assetBundleName: vepmod_prefabs`), never to its folder: a
  one-bundle build reads the bundle's name on its first asset (`AssetBundleBuildTab.cs:404` in the package cache), and
  an asset that belongs only through its folder carries none, so the build fails with "No AssetBundle has been set for
  this build".
- After the rebuild, `python tooling/bundle-dump.py --bundle Ressources/vepmod_prefabs` lists the new assets.
- A build that wrote nothing logs its error in the editor's log, `%LOCALAPPDATA%/Unity/Editor/Editor.log`.
