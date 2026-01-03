using UnityEngine;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;

namespace VepMod.Enemies.Whispral;

public class EnemyWhispralAnim : StateMachineComponent<EnemyWhispralAnim, EnemyWhispralAnim.State>
{
    public enum State
    {
        NoBreathing,
        BreatheIn,
        BreatheOut
    }

    private static readonly VepLogger LOG = VepLogger.Create<EnemyWhispralAnim>();

    [Space] [SerializeField] private Enemy enemy = null!;
    [SerializeField] private EnemyWhispral enemyWhispral = null!;

    [Space] [SerializeField] private Sound soundHurt = null!;
    [SerializeField] private Sound soundDeath = null!;

    [Space] [SerializeField] private Sound soundBreatheIn = null!;
    [SerializeField] private Sound soundBreatheOut = null!;
    [SerializeField] private Vector2 breathingDurationInRange;
    [SerializeField] private Vector2 breathingDurationOutRange;

    [Space] [SerializeField] private GameObject visuals = null!;
    [SerializeField] private GameObject rigidBody = null!;
    protected override State DefaultState => State.NoBreathing;

    protected override void Awake()
    {
        base.Awake();

        Fsm.AddState(State.NoBreathing, new NoBreathing(this));
        Fsm.AddState(State.BreatheIn,
            new BreathingState(this, soundBreatheIn, breathingDurationInRange, State.BreatheOut));
        Fsm.AddState(State.BreatheOut,
            new BreathingState(this, soundBreatheOut, breathingDurationOutRange, State.BreatheIn));
    }

    protected override void Update()
    {
        var isAttachedAndNotJumping = enemyWhispral.CurrentState == EnemyWhispral.State.Attached && !enemy.Jump.jumping;

        if (isAttachedAndNotJumping)
        {
            var current = Fsm.CurrentStateStateId;
            if (current != State.BreatheIn && current != State.BreatheOut)
            {
                Fsm.NextStateStateId = State.BreatheIn;
            }
        }
        else
        {
            Fsm.NextStateStateId = State.NoBreathing;
        }

        base.Update();
    }

    public void Death()
    {
        StopBreathing();
        soundDeath.Play(enemy.transform.position);
    }

    public void Hurt()
    {
        StopBreathing();
        soundHurt.Play(enemy.transform.position);
    }


    public void Show(bool show)
    {
        visuals.gameObject.SetActive(show);
        rigidBody.gameObject.SetActive(show);
    }

    public void StopBreathing()
    {
        Fsm.NextStateStateId = State.NoBreathing;
    }

    #region States

    /// <summary>
    ///     État "ne fait rien de spécial" côté anim/sons, coupe la respiration.
    /// </summary>
    private sealed class NoBreathing(EnemyWhispralAnim owner) : StateMachineBase<StateMachine, State>.StateBase { }

    /// <summary>
    ///     État de respiration générique : joue un son au début, attend Duration, puis passe à NextState.
    ///     Utilisé pour BreatheIn et BreatheOut.
    /// </summary>
    private sealed class BreathingState(EnemyWhispralAnim owner, Sound sound, Vector2 durationRange, State nextState)
        : StateMachineBase<StateMachine, State>.StateBaseTransition(durationRange.x, nextState)
    {
        private const float MinDuration = 3f;
        private readonly State _nextState = nextState;
        private AudioSource? _audioSource;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            owner.Show(false);
            Duration = GetDuration();
            if (owner.enemyWhispral.playerTarget != null)
            {
                _audioSource = sound.Play(owner.enemyWhispral.playerTarget.transform.position);
            }
        }

        private float GetDuration()
        {
            var min = Mathf.Max(durationRange.x, MinDuration);
            if (durationRange.x < MinDuration)
            {
                LOG.Warning($"Adjusted min duration from {durationRange.x} to {MinDuration} (minimum allowed).");
            }

            var max = Mathf.Max(durationRange.y, min);
            if (durationRange.y < min)
            {
                LOG.Warning($"Adjusted max duration from {durationRange.y} to {min} (must be >= min).");
            }

            return Random.Range(min, max);
        }

        public override void OnStateExit(State next)
        {
            base.OnStateExit(next);
            if (_audioSource) _audioSource.Stop();
            if (next != _nextState) // Si on ne va pas à l’état suivant prévu, on coupe la respiration.
            {
                owner.Show(true);
            }
        }
    }

    #endregion
}