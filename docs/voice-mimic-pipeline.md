# Système de voix Whispral (Mimic) — Fonctionnement end-to-end

Documentation du pipeline complet : de la capture du micro d'un joueur jusqu'à la relecture de sa
voix par une hallucination (droid) pendant le debuff Whispral.

> Sous-systèmes détaillés ailleurs : le filtrage de parole (WebRTC VAD) est documenté dans
> [`vad-integration.md`](./vad-integration.md). Cette page couvre l'ensemble et y renvoie.

---

## 1. Vue d'ensemble

L'ennemi **Whispral** s'accroche à un joueur et le « debuff ». Pendant ce temps, des hallucinations
(des clones-droids des autres joueurs) apparaissent et **rejouent de vrais extraits de voix**
captés en jeu. Deux boucles indépendantes alimentent ça :

- **Boucle 1 — Capture & partage** : chaque client enregistre sa propre voix, la filtre (VAD),
  la sauvegarde en WAV et la partage aux autres clients.
- **Boucle 2 — Lecture** : le Master, quand un Whispral est accroché à un joueur, ordonne à ce
  joueur de jouer un extrait d'un autre joueur sur une hallucination.

```mermaid
flowchart TD
    mic["🎤 Micro du joueur"] -->|"flux audio brut"| patch["Capture des trames voix"]
    patch -->|"format réel du micro"| resolver["Détection du format audio"]
    resolver --> mimics["Gestion de la voix"]

    subgraph L1["Boucle 1 — Capture et partage (joueur local)"]
        mimics -->|"enregistre puis filtre la parole"| wav["Stockage des extraits"]
        mimics -->|"envoie aux autres joueurs"| net1(["Réseau Photon"])
    end

    net1 -->|"réception des extraits"| wavRemote["Stockage chez les autres joueurs"]

    subgraph L2["Boucle 2 — Lecture (pilotée par le Master)"]
        whispral["Ennemi accroché à un joueur"] -->|"ordonne une lecture"| net2(["Réseau Photon"])
        net2 -->|"commande reçue par le joueur ciblé"| droid["Hallucination du joueur source"]
        droid -->|"déclenche la voix"| play["Lecture spatialisée"]
        play --> wavRemote
    end
```

**Rôle clé : `WhispralMimics`** est attaché à *chaque* `PlayerAvatar` (via
`PlayerAvatarPatch`), mais :
- la **boucle 1 d'enregistrement** ne tourne que pour le joueur **local** (`PhotonView.IsMine`) ;
- tous les clients **reçoivent et stockent** les sons des autres (pour pouvoir les rejouer) ;
- la référence au mimics local est mise en cache dans `VepFinder.LocalMimics`.

---

## 2. Initialisation & cycle de vie

```mermaid
sequenceDiagram
    participant Game as Jeu REPO
    participant Patch as Hook de création du joueur
    participant Mimics as Gestion de la voix
    participant Finder as Annuaire local

    Game->>Patch: un joueur apparaît
    Patch->>Mimics: attache le composant de voix au joueur
    alt c'est le joueur local
        Patch->>Finder: mémorise la référence locale
    end
    Mimics->>Mimics: attend le chat vocal du jeu
    Note over Mimics: récupère le chat vocal,<br/>prépare le filtre de parole si activé
    alt joueur local
        Mimics->>Mimics: démarre la boucle d'enregistrement
    else joueur distant
        Note over Mimics: se contente de recevoir les voix des autres
    end
```

---

## 3. Résolution du format audio (correctif échantillonnage)

Photon expose **deux formats** pour une voix : le format **encodeur/transmission**
(`Info.SamplingRate` / `Info.Channels`, ex. 48000 mono) et le format **source/micro**
(`InputSamplingRate` / `InputChannels`, ex. 44100 stéréo). Les buffers qu'on capture sont au format
**source** — les étiqueter avec le format encodeur déforme la voix à la relecture (pitch/vitesse).

`VoiceFormatResolver` lit donc le format **source** (par réflexion défensive, cache par voix) :

```mermaid
flowchart TD
    start["Détecter le format d'une voix"] --> cache{"format déjà connu<br/>pour cette voix ?"}
    cache -->|oui| ret["réutilise le format mémorisé"]
    cache -->|non| ch["nombre de canaux :<br/>priorité au format source du micro,<br/>sinon repli sur l'encodeur, sinon mono"]
    ch --> rate{"fréquence source<br/>connue et plausible ?"}
    rate -->|oui| use["utilise la fréquence source"]
    rate -->|non| derive{"durée d'une trame<br/>connue ?"}
    derive -->|oui| calc["calcule la fréquence à partir<br/>de la durée réelle de la trame"]
    derive -->|non| fb["repli sur la fréquence encodeur"]
    use --> clamp["borne à une plage plausible<br/>(8 à 48 kHz)"]
    calc --> clamp
    fb --> clamp
    clamp --> log["journal de diagnostic<br/>(une fois par voix)"]
    log --> store["mémorise et renvoie le format"]
```

Une fois le format source correct, **tout le reste devient cohérent** : le rate voyage avec le
fichier (header WAV) et le RPC, et la lecture réutilise ce même rate. Chaque client peut donc avoir
un micro différent (16 k / 44.1 k / 48 k) sans déformation.

---

## 4. Boucle 1 — Capture, validation, partage

### 4.1 Machine d'état de la capture (`ProcessVoiceData`)

Appelée à chaque frame voix par le patch Photon (sur le **thread voix**, pas le thread Unity).

```mermaid
stateDiagram-v2
    [*] --> Repos
    Repos --> EnAttente: la boucle 1 lance un enregistrement
    EnAttente --> Capture: le joueur se met à parler
    note right of Capture
        mixe la voix en mono à la fréquence du micro
        accumule jusqu'à 6 secondes
    end note
    Capture --> Capture: le joueur continue de parler
    Capture --> Finalisation: silence d'au moins une demi-seconde
    Capture --> Finalisation: mémoire pleine (6 secondes)
    Finalisation --> Repos: l'extrait est traité puis sauvegardé
```

### 4.2 Pipeline de finalisation (`FinalizeRecording`)

```mermaid
flowchart TD
    fin["Fin de la capture"] --> dur{"durée suffisante ?"}
    dur -->|non| drop1["rejeté : trop court"]
    dur -->|oui| trim["retire le silence de fin<br/>(pour l'analyse seulement ;<br/>l'extrait gardé reste complet)"]
    trim --> vaden{"filtre de parole activé ?"}
    vaden -->|non| save
    vaden -->|oui| vad{"assez de parole détectée ?<br/>(selon la sensibilité)"}
    vad -->|non| drop2["rejeté : pas assez de parole"]
    vad -->|oui| save["sauvegarde l'extrait"]
    save --> wav["fichier audio mono<br/>à la fréquence du micro"]
    save --> flag["marqué prêt à partager"]
```

> Détails VAD (sensibilité, seuils, fail-open, calibration) : voir
> [`vad-integration.md`](./vad-integration.md).

### 4.3 Partage réseau

```mermaid
sequenceDiagram
    participant Share as Boucle de partage (local)
    participant Mimics as Gestion de la voix (local)
    participant Net as Réseau Photon
    participant Remote as Autres joueurs
    participant Wav as Stockage des extraits

    loop à intervalle aléatoire
        Share->>Mimics: lance un enregistrement
        Note over Mimics: capture puis filtrage de la parole<br/>(voir 4.1 et 4.2)
        alt nouvel extrait valide
            Mimics->>Mimics: découpe l'extrait en petits morceaux
            loop chaque morceau, avec un léger délai
                Mimics->>Net: envoie un morceau
                Net->>Remote: morceau transmis
            end
            Remote->>Remote: réassemble une fois tous les morceaux reçus
            Remote->>Wav: enregistre l'extrait du joueur source
        end
    end
```

Le `WavFileManager` applique une **rotation** par joueur (max `ConfigSamplesPerPlayer`, supprime le
plus ancien).

---

## 5. Boucle 2 — Lecture sur l'hallucination

Pilotée par le **Master**. Quand `EnemyWhispral` est dans l'état `Attached` à un joueur, il envoie
périodiquement (toutes les `VoiceDelay.Min..Max` s) une commande de lecture.

```mermaid
sequenceDiagram
    participant Master as Ennemi accroché (Master)
    participant LMimics as Gestion de la voix (Master)
    participant Net as Réseau Photon
    participant Target as Joueur ciblé (debuffé)
    participant Debuff as Gestion des hallucinations
    participant Droid as Hallucination
    participant Play as Lecture spatialisée
    participant Wav as Stockage des extraits

    Master->>Master: choisit un autre joueur comme source
    Master->>LMimics: ordonne une lecture sur le joueur ciblé
    LMimics->>Net: diffuse la commande de lecture
    Net->>Target: commande reçue
    Note over Target: seul le joueur ciblé exécute la lecture
    Target->>Debuff: une hallucination du joueur source est-elle active ?
    Debuff->>Droid: retrouve l'hallucination du joueur source
    Droid->>Play: déclenche la voix
    Play->>Wav: prend un extrait au hasard du joueur source
    Play->>Play: prépare l'audio (voir 6)
    Play->>Droid: joue à la bonne fréquence, avec occlusion des murs
```

---

## 6. Traitement audio

### 6.1 À la capture (boucle 1)
- **Downmix → mono** par moyenne des canaux (`CopyVoiceDataToBuffer`).
- **VAD** (filtre principal de parole) — voir `vad-integration.md`.
- Sauvegarde **WAV PCM 16-bit mono** au **rate source**.

### 6.2 À la lecture (boucle 2) — `ProcessReceivedAudio`
```mermaid
flowchart TD
    raw["fichier audio reçu"] --> f2["décodage en échantillons"]
    f2 --> lp["filtre passe-bas<br/>(rend la voix feutrée)"]
    lp --> filt{"effet de voix demandé ?"}
    filt -->|oui| rnd["effet aléatoire :<br/>grave, aigu ou alien"]
    filt -->|non| fade
    rnd --> fade["fondu d'entrée/sortie<br/>+ marge de silence"]
    fade --> clip["extrait mono prêt à jouer"]
```

Le filtre voix aléatoire n'est appliqué que si `ConfigVoiceFilterEnabled` est activé **et** tiré au
sort (50 %). La lecture spatiale ajoute une occlusion (murs) via `AudioLowPassLogic`, comme les
vraies voix du jeu.

---

## 7. Configuration utilisateur (`BepInEx/config/com.vep.vepMod.cfg`)

| Section | Clé | Défaut | Rôle |
|---|---|---|---|
| General | `Volume` | 100 % | Volume des voix d'hallucination |
| Audio Sharing | `Share Min/Max Delay` | 10 / 30 s | Intervalle entre deux enregistrements partagés |
| Audio Sharing | `Samples Per Player` | 20 | Nb max de WAV stockés par joueur (rotation) |
| Voice Playback | `Voice Min/Max Delay` | 8 / 15 s | Intervalle entre lectures pendant le debuff |
| Voice Playback | `Voice Filter Enabled?` | false | Active les filtres voix aléatoires à la lecture |
| Audio Quality | `Min Duration` | 0.3 s | Garde-fou durée (pré-VAD) |
| Audio Quality | `VAD Enabled` | true | Active le filtrage de parole WebRTC |
| Audio Quality | `VAD Sensitivity` | Balanced | Permissive / Balanced / Strict (voir `vad-integration.md`) |
| Experimental | `Sampling Rate` | 48000 | Hint encodeur (le rate **source** réel est résolu automatiquement) |

> Variante de build **sans VAD** (`-p:VepModNoVad=true`) : retire la dépendance WebRTC et tout le
> code VAD ; la boucle 1 ne garde que le garde-fou de durée. Voir `vad-integration.md`.

---

## 8. Carte des fichiers

| Fichier | Rôle |
|---|---|
| `src/VepMod.cs` | Plugin BepInEx : config, Harmony, préchargement prefab |
| `src/Patchs/PlayerAvatarPatch.cs` | Attache `WhispralMimics` ; définit `VepFinder.LocalMimics` |
| `src/Patchs/LocalVoiceFramedPatch.cs` | Capte les frames micro → `ProcessVoiceData` |
| `src/Patchs/VoiceFormatResolver.cs` | Résout le format **source** (rate + canaux) |
| `src/Patchs/VepFinder.cs` | Cache global du `WhispralMimics` local |
| `src/Enemies/Whispral/WhispralMimics.cs` | Cœur : capture, VAD, partage (L1), réception, lecture (L2) |
| `src/Enemies/Whispral/WavFileManager.cs` | Stockage WAV par joueur + rotation |
| `src/Enemies/Whispral/AudioFilters.cs` | Conversions, passe-bas, pitch, alien, fade/padding |
| `src/VepFramework/Audio/VadAudioValidator.cs` | Filtre de parole WebRTC VAD |
| `src/Enemies/Whispral/EnemyWhispral.cs` | FSM de l'ennemi ; envoie les commandes de lecture (L2) |
| `src/Enemies/Whispral/DroidController.cs` | Hallucination ; `PlayVoice` → lecture sur le droid |
| `src/Dev/DroidDevTools.cs` | Outils dev (DEBUG) : spawn droid, F6 replay VAD, HUD |
```
