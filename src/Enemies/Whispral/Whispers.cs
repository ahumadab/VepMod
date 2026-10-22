using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The Whispers of one Victim, on the Victim's client, from <see cref="Begin" /> to <see cref="End" />: heard by them
///     alone, timed here (ADR-0010).
/// </summary>
public sealed class Whispers : MonoBehaviour
{
    private const float WhisperVolume = 0.6f;

    private static readonly VepLogger LOG = VepLogger.Create<Whispers>();

    private readonly WhisperSchedule _schedule = new(() => Random.value);

    // Sound.Play draws the clip it names its object after and the one it plays apart: with one clip, both are the one
    // picked here. The schedule's envelope drives the volume. No Doppler: the source circles the listener as the head
    // turns.
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
    private AudioSource? _source;
    private PlayerAvatar _victim = null!;

    /// <summary>The local Whisper, or the silence before the next one, for the dev HUD.</summary>
    public static string Summary
    {
        get
        {
            var whispers = PlayerAvatar.instance ? PlayerAvatar.instance.GetComponent<Whispers>() : null;
            if (!whispers || (!whispers!._haunted && !whispers._schedule.IsPlaying)) return "not Haunted";

            var schedule = whispers._schedule;
            return schedule.IsPlaying
                ? $"{SoundName(whispers._sound)} from the {schedule.LastSide}, volume {schedule.Volume:F2}" +
                  (schedule.IsReleasing ? ", released" : "")
                : $"silence, next Whisper in {schedule.SilenceLeft:F1} s";
        }
    }

    private void Awake()
    {
        _victim = GetComponent<PlayerAvatar>();
    }

    private void Update()
    {
        switch (_schedule.Update(Time.time, _haunted))
        {
            case WhisperSchedule.Step.Start:
                Play();
                break;
            case WhisperSchedule.Step.End:
                _lastEnd = Time.time;
                LOG.Debug($"Whisper {SoundName(_sound)} from the {_schedule.LastSide} of {_victim.playerName} ended on " +
                          $"its own, faded out over {_schedule.FadeLength:F2} s; silence of " +
                          $"{_schedule.SilenceLeft:F1} s drawn");
                break;
            case WhisperSchedule.Step.Release:
                LOG.Debug($"Whisper {SoundName(_sound)} from the {_schedule.LastSide} of {_victim.playerName} released " +
                          $"by the end of the Haunting, faded out over {_schedule.FadeLength:F2} s");
                StopSource();
                break;
        }

        if (_source) _source!.volume = WhisperVolume * _schedule.Volume;
    }

    private void OnDestroy()
    {
        StopSource();
    }

    public void Begin()
    {
        _haunted = true;
        _hauntedSince = Time.time;
        _lastEnd = null;
        if (HallucinationPrefabLoader.WhisperSounds.Length == 0)
        {
            LOG.Warning($"No Whisper sound in the bundle: {_victim.playerName} hears no Whisper.");
        }
    }

    public void End()
    {
        _haunted = false;
    }

    private void Play()
    {
        var sounds = HallucinationPrefabLoader.WhisperSounds;
        var camera = Camera.main;
        if (sounds.Length == 0 || !camera)
        {
            _schedule.Started(0f);
            return;
        }

        _sound.Sounds[0] = sounds[Random.Range(0, sounds.Length)];
        var (x, y, z) = WhisperSchedule.Offset(_schedule.LastSide);
        var cameraTransform = camera.transform;
        // SoundFollowTransform keeps the source at that point in the camera's space: it follows the Victim's head.
        _source = _sound.Play(cameraTransform, cameraTransform.TransformPoint(new Vector3(x, y, z)));
        if (!_source)
        {
            LOG.Warning($"Whisper {SoundName(_sound)} failed to play for {_victim.playerName}.");
            _schedule.Started(0f);
            return;
        }

        // The game destroys the source once its clip has played at its pitch.
        _schedule.Started(_source!.clip.length / _source.pitch);
        var silence = _lastEnd.HasValue ? $", {Time.time - _lastEnd.Value:F1} s after the previous one ended" : "";
        LOG.Debug($"Whisper {SoundName(_sound)} starts from the {_schedule.LastSide} of {_victim.playerName}, " +
                  $"{Time.time - _hauntedSince:F1} s into the Haunting{silence}");
    }

    private void StopSource()
    {
        if (_source) Destroy(_source!.gameObject);
        _source = null;
    }

    private static string SoundName(Sound sound)
    {
        return sound.Sounds[0] ? sound.Sounds[0].name : "none";
    }
}
