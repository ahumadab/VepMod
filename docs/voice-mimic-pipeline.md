# Système de voix Whispral (Mimic) — Fonctionnement end-to-end

Le parcours d'une voix : du micro d'un joueur jusqu'au Mimic d'une Hallucination chez la Victim. Les termes sont ceux
de [`GLOSSARY.md`](../GLOSSARY.md). Le filtrage de parole (WebRTC VAD) est détaillé dans
[`vad-integration.md`](./vad-integration.md).

---

## 1. Vue d'ensemble

Deux boucles indépendantes :

- **Boucle 1 — Capture et partage** : chaque client capture la voix de son joueur, garde les Captures qui contiennent
  de la parole comme Voice Samples, les range dans la Voice Bank du joueur et les partage aux autres clients.
- **Boucle 2 — Mimic** : quand un Whispral est accroché à une Victim, le Master ordonne régulièrement au client de la
  Victim de faire parler une Hallucination avec un Voice Sample de son Imitated Player.

```mermaid
flowchart TD
    mic["🎤 Micro du joueur"] -->|"trames voix (thread voix Photon)"| rec["Recorder"]

    subgraph L1["Boucle 1 — Capture et partage (client du joueur)"]
        rec -->|"Capture"| val["Validation : durée puis parole"]
        val -->|"Voice Sample"| bank["Voice Bank du joueur"]
        val -->|"Voice Sample"| net1(["Réseau Photon"])
    end

    net1 -->|"réception"| bankRemote["Voice Bank du joueur chez les autres clients"]

    subgraph L2["Boucle 2 — Mimic (piloté par le Master)"]
        whispral["Whispral accroché à la Victim"] -->|"ordonne un Mimic"| net2(["Réseau Photon"])
        net2 -->|"client de la Victim"| hallu["Hallucination de l'Imitated Player"]
        hallu -->|"Voice Sample au hasard"| play["Lecture spatialisée"]
        bankRemote --> play
    end
```

Le recorder, le filtre de parole et le store des Voice Banks existent une fois par client.

---

## 2. Le format audio vient des trames

Photon expose **deux formats** pour une voix : celui de l'**encodeur** (`Info.SamplingRate` / `Info.Channels`, ex.
48000 mono) et celui du **micro** (`InputSamplingRate` / `InputChannels`, ex. 44100 stéréo). Les trames poussées dans
`LocalVoiceFramed<short>.PushDataAsync` sont au format du **micro** : les étiqueter avec celui de l'encodeur déforme la
voix à la relecture (hauteur et vitesse). Selon la version de PhotonVoice, les propriétés `Input*` manquent ; la
fréquence se déduit alors de la durée d'une trame (`FrameDurationUs`).

La fréquence voyage ensuite avec le Voice Sample (en-tête WAV), et la lecture la réutilise : chaque client peut avoir un
micro différent (16 k / 44,1 k / 48 k) sans déformation.

---

## 3. Boucle 1 — Capture, validation, partage

### 3.1 Capture (thread voix Photon)

Le recorder ne fait qu'accumuler ; chaque Capture terminée passe au thread principal par une file thread-safe.

```mermaid
stateDiagram-v2
    [*] --> Repos
    Repos --> EnAttente: la boucle 1 demande une Capture
    EnAttente --> Capture: le joueur se met à parler
    note right of Capture
        moyenne des canaux en mono, à la fréquence du micro
    end note
    Capture --> Capture: le joueur continue de parler
    Capture --> Repos: une demi-seconde de silence, ou 6 secondes
```

### 3.2 Validation (tâche de fond)

Le thread principal vide la file ; la validation, l'encodage et la sauvegarde tournent dans une tâche de fond, puis le
Voice Sample accepté repasse au thread principal pour le partage.

```mermaid
flowchart TD
    fin["Capture terminée"] --> dur{"durée suffisante ?"}
    dur -->|non| drop1["rejetée : trop courte"]
    dur -->|oui| trim["retire le silence de fin<br/>(pour l'analyse seulement ;<br/>le Voice Sample le garde)"]
    trim --> vaden{"filtre de parole actif ?"}
    vaden -->|non| save
    vaden -->|oui| vad{"assez de parole ?<br/>(selon la sensibilité)"}
    vad -->|non| drop2["rejetée : pas assez de parole"]
    vad -->|oui| save["Voice Sample : Voice Bank du joueur,<br/>puis partage"]
```

### 3.3 Partage réseau

```mermaid
sequenceDiagram
    participant Local as Client du joueur
    participant Net as Réseau Photon
    participant Remote as Autres clients

    loop à intervalle aléatoire
        Local->>Local: demande une Capture (voir 3.1 et 3.2)
        alt Voice Sample accepté
            loop chaque morceau, avec un léger délai
                Local->>Net: envoie un morceau
                Net->>Remote: morceau transmis
            end
            Remote->>Remote: réassemble, puis range dans la Voice Bank du joueur
        end
    end
```

Les morceaux passent par l'avatar du joueur (`VoiceSampleSharing`) et ne portent ni nom, ni Steam ID, ni fréquence :
le récepteur reconnaît l'expéditeur au propriétaire de l'avatar, et la fréquence est dans l'en-tête WAV. Un premier
morceau recommence le transfert ; un joueur qui part emporte son avatar, et le transfert en cours avec lui.

Une Voice Bank est rangée sous le Steam ID du joueur, le pseudo en repli sans Steam (ADR-0006), et plafonnée : le plus
ancien Voice Sample part en premier.

---

## 4. Boucle 2 — Mimic

```mermaid
sequenceDiagram
    participant Master as Whispral accroché (Master)
    participant Net as Réseau Photon
    participant Victim as Client de la Victim
    participant Hallu as Hallucination

    loop tant que l'Attachment dure
        Master->>Master: choisit un Imitated Player
        Master->>Net: ordonne un Mimic
        Net->>Victim: commande reçue (les autres clients l'ignorent)
        Victim->>Hallu: retrouve l'Hallucination de l'Imitated Player
        Hallu->>Hallu: Voice Sample au hasard de sa Voice Bank, traité (voir 5)
        Hallu->>Hallu: lecture spatialisée, occlusion des murs
    end
```

---

## 5. Traitement à la lecture

```mermaid
flowchart TD
    raw["Voice Sample"] --> lp["filtre passe-bas<br/>(voix feutrée)"]
    lp --> filt{"effet de voix demandé ?"}
    filt -->|oui| rnd["effet au hasard :<br/>grave, aigu ou alien"]
    filt -->|non| fade
    rnd --> fade["fondu d'entrée et de sortie<br/>+ marge de silence"]
    fade --> out["prêt à jouer, mono"]
```

Le Master tire l'effet de voix au hasard, seulement si `Voice Filter Enabled?` est activé dans sa config. L'occlusion des murs passe par
`AudioLowPassLogic`, comme les vraies voix du jeu.

Les entrées de config sont décrites dans [`WIKI.md`](../WIKI.md).
