# Changelog

# [2.0.0](https://github.com/ahumadab/VepMod/compare/1.1.0...2.0.0) (2026-10-22)


### Bug Fixes

* **hallucination:** say why a Hallucination of the local player has no map tool ([#67](https://github.com/ahumadab/VepMod/issues/67)) ([9ec6e62](https://github.com/ahumadab/VepMod/commit/9ec6e62d05a1536407969f85b9d117ecf3875d19))
* **hallucination:** strip cloned components dependents first ([#79](https://github.com/ahumadab/VepMod/issues/79)) ([3a7260b](https://github.com/ahumadab/VepMod/commit/3a7260b5f06691cf6519860cc139d6f715c81a4e))
* **hallucination:** the angry eyelids show at the eyeball's size ([#105](https://github.com/ahumadab/VepMod/issues/105)) ([8678a91](https://github.com/ahumadab/VepMod/commit/8678a9156294a39c7daf2fc7ca32e7bcaea53ba4))
* **whispral:** give each Mimic its own emitter so the next one cannot cut it off ([#40](https://github.com/ahumadab/VepMod/issues/40)) ([#47](https://github.com/ahumadab/VepMod/issues/47)) ([96ddb75](https://github.com/ahumadab/VepMod/commit/96ddb7511501b086bab842e00e0dbc3a9e5e6524))
* **whispral:** give the Haunted state one owner and the Attachment one exit ([#31](https://github.com/ahumadab/VepMod/issues/31)) ([4b02c1c](https://github.com/ahumadab/VepMod/commit/4b02c1cda7f636c2878fdfadb7830604283914eb)), closes [#14](https://github.com/ahumadab/VepMod/issues/14)
* **whispral:** hide everything that gives an Imitated Player away to the Victim ([#35](https://github.com/ahumadab/VepMod/issues/35)) ([#38](https://github.com/ahumadab/VepMod/issues/38)) ([906346e](https://github.com/ahumadab/VepMod/commit/906346eee94a62276f895d759755419ae48f6870))
* **whispral:** keep a dead Imitated Player's name on their Hallucination ([#28](https://github.com/ahumadab/VepMod/issues/28)) ([5429cee](https://github.com/ahumadab/VepMod/commit/5429cee30914212d2c23db7567e6dfed5331cb7a))
* **whispral:** merge Vanish and Hallucinations into one Imitations module ([#33](https://github.com/ahumadab/VepMod/issues/33)) ([d16179a](https://github.com/ahumadab/VepMod/commit/d16179a80ee0314c049c3154ec5ecbf80237492a))
* **whispral:** mute Imitated Players' footsteps by emitter, not by distance ([#36](https://github.com/ahumadab/VepMod/issues/36)) ([5227189](https://github.com/ahumadab/VepMod/commit/522718973d77531b712dd0ce82bffe99a1e0a3b7))


### Features

* **hallucination:** a Hallucination looks at whoever talks nearby ([#96](https://github.com/ahumadab/VepMod/issues/96)) ([e7447d2](https://github.com/ahumadab/VepMod/commit/e7447d261480139075d14b661ca0ba503f03709a))
* **hallucination:** a Hallucination sometimes chains two trips ([#99](https://github.com/ahumadab/VepMod/issues/99)) ([f45e337](https://github.com/ahumadab/VepMod/commit/f45e337acc30ec53f832c498167357795e030fff))
* **hallucination:** a Hallucination sometimes changes its mind on the way ([#101](https://github.com/ahumadab/VepMod/issues/101)) ([6538a04](https://github.com/ahumadab/VepMod/commit/6538a046249b21d80fa6d872a1c472744649ad51))
* **hallucination:** a lunging Hallucination speaks a Mimic that outlives it ([#111](https://github.com/ahumadab/VepMod/issues/111)) ([#120](https://github.com/ahumadab/VepMod/issues/120)) ([005acc9](https://github.com/ahumadab/VepMod/commit/005acc9c73bad478b763b2abaa2eb82d9b5dc3fa))
* **hallucination:** a Stalking Hallucination the Victim never looks at lunges and kills them ([#109](https://github.com/ahumadab/VepMod/issues/109)) ([#116](https://github.com/ahumadab/VepMod/issues/116)) ([2f5f063](https://github.com/ahumadab/VepMod/commit/2f5f063721b67b4581f34aef4af5c4b8ab9e309a))
* **hallucination:** a waiting Hallucination glances around and sometimes turns round ([#94](https://github.com/ahumadab/VepMod/issues/94)) ([a2ccf61](https://github.com/ahumadab/VepMod/commit/a2ccf61a9e082539e3e4584049b7cbe599acf15a))
* **hallucination:** ease starts and stops like a player and round the path's corners ([#92](https://github.com/ahumadab/VepMod/issues/92)) ([fbf0f5e](https://github.com/ahumadab/VepMod/commit/fbf0f5e5be0e1a0ea119be3956433a16499c396f))
* **hallucination:** Gaze with line of sight, shared by Stalking, the angry eyes and the nameplate ([#69](https://github.com/ahumadab/VepMod/issues/69)) ([b4547be](https://github.com/ahumadab/VepMod/commit/b4547beb8dd3d6f4b385e981656e08954c5b439c)), closes [#60](https://github.com/ahumadab/VepMod/issues/60)
* **hallucination:** one body pose module, the eyes follow the Victim while staring ([#71](https://github.com/ahumadab/VepMod/issues/71)) ([d6799b2](https://github.com/ahumadab/VepMod/commit/d6799b2d3bf4942ba73d8313a409960a7c67a2fe)), closes [#61](https://github.com/ahumadab/VepMod/issues/61) [#61](https://github.com/ahumadab/VepMod/issues/61)
* **hallucination:** only one Hallucination stalks a Victim at a time ([#108](https://github.com/ahumadab/VepMod/issues/108)) ([#114](https://github.com/ahumadab/VepMod/issues/114)) ([df17a70](https://github.com/ahumadab/VepMod/commit/df17a70d308262a4ad39f2abf32e69b6848484d4))
* **hallucination:** the eyes follow the head's look point, a Glance too ([#103](https://github.com/ahumadab/VepMod/issues/103)) ([b8c0993](https://github.com/ahumadab/VepMod/commit/b8c0993d8654ccaede3e4e30972b63fcc0d1ee38)), closes [#98](https://github.com/ahumadab/VepMod/issues/98)
* **hallucination:** the game's PlayerAvatarVisuals animates the Hallucination's body ([#89](https://github.com/ahumadab/VepMod/issues/89)) ([0dd8618](https://github.com/ahumadab/VepMod/commit/0dd8618e24d7cc7e86688a53d74f26d1a1295a01)), closes [#82](https://github.com/ahumadab/VepMod/issues/82)
* **hallucination:** the Lunge count resumes once the Victim has been back in sight a moment ([#125](https://github.com/ahumadab/VepMod/issues/125)) ([#127](https://github.com/ahumadab/VepMod/issues/127)) ([600ddcd](https://github.com/ahumadab/VepMod/commit/600ddcd5de7a426913df854fc944f6766cdc9862))
* **hallucination:** the Lunge count's start and resumes play the dead-teammate effect ([#115](https://github.com/ahumadab/VepMod/issues/115)) ([#124](https://github.com/ahumadab/VepMod/issues/124)) ([fcaec96](https://github.com/ahumadab/VepMod/commit/fcaec96a86cf22afda966b0a04ff6dff09734a7d))
* **hallucination:** the Lunge forces the Victim's camera onto the Hallucination's face ([#110](https://github.com/ahumadab/VepMod/issues/110)) ([#118](https://github.com/ahumadab/VepMod/issues/118)) ([d8ee39e](https://github.com/ahumadab/VepMod/commit/d8ee39e99d7bf90aedf08f49d271c3d51dc27def))
* **hallucination:** the Master's option decides whether Hallucinations can kill ([#112](https://github.com/ahumadab/VepMod/issues/112)) ([#122](https://github.com/ahumadab/VepMod/issues/122)) ([7aca7a4](https://github.com/ahumadab/VepMod/commit/7aca7a4a81bef184ee053b25f43ecd72e2efd651))
* **mimic:** Mimic played by the Hallucination, commanded from the Whispral to the Victim ([#44](https://github.com/ahumadab/VepMod/issues/44)) ([#56](https://github.com/ahumadab/VepMod/issues/56)) ([4a99f47](https://github.com/ahumadab/VepMod/commit/4a99f471375609f12507bb045fa717dfe6722a8e))
* **mimic:** the Victim's client picks a Hallucination that can speak ([#77](https://github.com/ahumadab/VepMod/issues/77)) ([71adb3d](https://github.com/ahumadab/VepMod/commit/71adb3dda21c2b947c66a277a15448c59055b053))
* **recorder:** Voice Samples from voice frames, validated off the voice thread ([#42](https://github.com/ahumadab/VepMod/issues/42)) ([#52](https://github.com/ahumadab/VepMod/issues/52)) ([87ffdf1](https://github.com/ahumadab/VepMod/commit/87ffdf19a1b85aed7cf199c565dbd7f675ad2c7e))
* **sharing:** Voice Sample sharing as a thin Photon adapter ([#43](https://github.com/ahumadab/VepMod/issues/43)) ([#54](https://github.com/ahumadab/VepMod/issues/54)) ([6f47ccb](https://github.com/ahumadab/VepMod/commit/6f47ccb9e4c70e9e7b9b821063460dfd75cb46de))
* **voice-bank:** one store per client, keyed by Steam ID, one WAV codec ([#41](https://github.com/ahumadab/VepMod/issues/41)) ([#50](https://github.com/ahumadab/VepMod/issues/50)) ([ac144c6](https://github.com/ahumadab/VepMod/commit/ac144c6e80c367030a22fd18fa3644f4b853ccf5))


### BREAKING CHANGES

* **mimic:** the Mimic command is now MimicRPC(voiceFilter) on the Whispral, sent to the Victim's client only, which picks the Hallucination that speaks. Every player needs the same version for Mimics to play.
* **sharing:** the Voice Sample sharing RPC is now ReceiveVoiceSampleChunk(chunk, chunkIndex, chunkCount), with no player identity or sample rate. Clients on an earlier version no longer share Voice Samples with this one.

# [1.1.0](https://github.com/ahumadab/VepMod/compare/1.0.5...1.1.0) (2026-06-22)


### Features

* **Audio:** WebRTC VAD speech filtering + mic sample-rate fix for Whispral mimic voices ([#5](https://github.com/ahumadab/VepMod/issues/5)) ([215fc5a](https://github.com/ahumadab/VepMod/commit/215fc5afc8aceafbf08ab06469eb3b661c3abb00))
* **droid:** Hallucination LostDroid — clone visuel live du joueur, pose CheckMap, et spawn debout ([#4](https://github.com/ahumadab/VepMod/issues/4)) ([999c5fc](https://github.com/ahumadab/VepMod/commit/999c5fcd36fcddb08830381e3d8ed657d04d1b41))
* **droid:** real gravity-driven falling + fall animation ([19d8d47](https://github.com/ahumadab/VepMod/commit/19d8d4750cb82b6d99a7d843664c3b9f79c4f771)), closes [#if](https://github.com/ahumadab/VepMod/issues/if)

## [1.0.5](https://github.com/ahumadab/VepMod/compare/1.0.4...1.0.5) (2026-03-14)


### Bug Fixes

* **filesystem:** Introduce robust cross-platform file system sanitizer ([06278c6](https://github.com/ahumadab/VepMod/commit/06278c60ef660c3b593426913f042a8037dd52bd))

## [1.0.4](https://github.com/ahumadab/VepMod/compare/1.0.3...1.0.4) (2026-01-31)


### Bug Fixes

* **Droid:**  Changing prefab dependencie & material shaders ([af1fe44](https://github.com/ahumadab/VepMod/commit/af1fe440dfda5608532cbd2b5e7d2d43fa8d4b5a))
* **Droid:** Loads AssetBundle from embedded resource ([48c49c4](https://github.com/ahumadab/VepMod/commit/48c49c464ca3e5aed239e88392f26839d220e067))

## [1.0.3](https://github.com/ahumadab/VepMod/compare/1.0.2...1.0.3) (2026-01-08)


### Bug Fixes

* **audio:** use WAV file sample rate instead of local config ([964f939](https://github.com/ahumadab/VepMod/commit/964f939a0a8b38515bbd611a6cc973183debaf02))
* **whispral:** Clean debuffs on player extraction ([5d57f49](https://github.com/ahumadab/VepMod/commit/5d57f495758e9e773636464633427de155014c5d))

# 1.0.2 (2026-01-05)

### Bug Fixes

* **Whispral:** Fixed Map Tool remaining visible on hidden players
* **Core:** Removing unnecessary dependency breaking mod
