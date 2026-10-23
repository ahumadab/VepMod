using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The Whispers of the local Victim, from <see cref="Begin" /> to <see cref="End" />: heard by them alone, timed here
///     (ADR-0010).
/// </summary>
public sealed class Whispers : MonoBehaviour
{
    private static Whispers? _local;

    private const float WhisperVolume = 0.35f;

    private static readonly VepLogger LOG = VepLogger.Create<Whispers>();

    private readonly WhisperSchedule _schedule = new(() => Random.value);

    // Sound.Play draws the AudioClip it names its object after and the one it plays apart: given one, both are the
    // Whisper sound picked here. It starts silent, the schedule's envelope then drives the volume. No Doppler: the
    // source circles the listener as the head turns.
    private readonly Sound _sound = new()
    {
        Sounds = new AudioClip[1],
        Volume = 0f,
        VolumeRandom = 0f,
        Pitch = 1f,
        PitchRandom = 0.1f,
        Doppler = 0f
    };

    private bool _haunted;
    private float _hauntedSince;
    private float? _lastEnd;
    private AudioLowPassLogic? _lowPass;
    private AudioSource? _source;
    private string _victimName = "";

    // The game deactivates a dead player's avatar (PlayerAvatar.PlayerDeath): on it, the release that death starts would
    // wait for the revive while the Whisper plays on. The Whispers live on an object of their own, kept across levels.
    public static Whispers Local
    {
        get
        {
            if (!_local)
            {
                var host = new GameObject(nameof(Whispers));
                DontDestroyOnLoad(host);
                _local = host.AddComponent<Whispers>();
            }

            return _local!;
        }
    }

    /// <summary>The local Whisper, or the silence before the next one, for the dev HUD.</summary>
    public static string Summary
    {
        get
        {
            if (!_local || (!_local!._haunted && !_local._schedule.IsPlaying)) return "not Haunted";

            var schedule = _local._schedule;
            return schedule.IsPlaying
                ? $"{SoundName(_local._sound)} from the {schedule.LastSide}, volume {schedule.Volume:F2}" +
                  (schedule.IsReleasing ? ", released" : "")
                : $"silence, next Whisper in {schedule.SilenceLeft:F1} s";
        }
    }

    private string WhisperName => $"Whisper {SoundName(_sound)} from the {_schedule.LastSide} of {_victimName}";

    private void Update()
    {
        if (HallucinationPrefabLoader.WhisperSounds.Length == 0) return;

        switch (_schedule.Update(Time.time, _haunted))
        {
            case WhisperSchedule.Step.Start:
                Play();
                break;
            case WhisperSchedule.Step.End:
                _lastEnd = Time.time;
                LOG.Debug($"{WhisperName} ended on its own, faded out over {_schedule.FadeLength:F2} s; silence of " +
                          $"{_schedule.SilenceLeft:F1} s drawn");
                break;
            case WhisperSchedule.Step.Release:
                LOG.Debug($"{WhisperName} released by the end of the Haunting, faded out over " +
                          $"{_schedule.FadeLength:F2} s");
                StopSource();
                break;
        }

        if (!_source) return;

        // The game's low-pass logic on the source sets its volume every frame from the one it read at the start: the
        // envelope goes through it, which keeps its muffling behind walls.
        var volume = WhisperVolume * _schedule.Volume;
        if (_lowPass) _lowPass!.Volume = volume;
        else _source!.volume = volume;
    }

    private void OnDestroy()
    {
        StopSource();
    }

    public void Begin(PlayerAvatar victim)
    {
        _victimName = victim.playerName;
        _haunted = true;
        _hauntedSince = Time.time;
        _lastEnd = null;
        if (HallucinationPrefabLoader.WhisperSounds.Length == 0)
        {
            LOG.Warning($"No Whisper sound in the bundle: {_victimName} hears no Whisper.");
        }
    }

    public void End()
    {
        _haunted = false;
    }

    private void Play()
    {
        var sounds = HallucinationPrefabLoader.WhisperSounds;
        _sound.Sounds[0] = sounds[Random.Range(0, sounds.Length)];
        var (x, y, z) = WhisperSchedule.Offset(_schedule.LastSide);
        var camera = Camera.main;
        // SoundFollowTransform keeps the source at that point in the camera's space: it follows the Victim's head.
        _source = camera
            ? _sound.Play(camera.transform, camera.transform.TransformPoint(new Vector3(x, y, z)))
            : null;
        if (!_source)
        {
            LOG.Warning($"{WhisperName} failed to play: no camera or no audio source.");
            _schedule.Started(0f);
            return;
        }

        _lowPass = _source!.GetComponent<AudioLowPassLogic>();
        // The game destroys the source once its Whisper sound has played at its pitch.
        _schedule.Started(_source.clip.length / _source.pitch);
        if (_lastEnd.HasValue)
        {
            LOG.Debug($"{WhisperName} starts, {Time.time - _hauntedSince:F1} s into the Haunting, " +
                      $"{Time.time - _lastEnd.Value:F1} s after the previous one ended");
        }
        else
        {
            LOG.Debug($"{WhisperName} starts, {Time.time - _hauntedSince:F1} s into the Haunting");
        }
    }

    private void StopSource()
    {
        if (_source) Destroy(_source!.gameObject);
        _source = null;
        _lowPass = null;
    }

    private static string SoundName(Sound sound)
    {
        return sound.Sounds[0] ? sound.Sounds[0].name : "none";
    }
}
