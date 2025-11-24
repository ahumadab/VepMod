using UnityEngine;
using VepMod.VepFramework.Structures.FSM;

namespace VepMod.Scripts.Enemies.Whispral;

public class EnemyWhispralAnim : StateMachineComponent<EnemyWhispralAnim, EnemyWhispralAnim.State>
{
    public enum State
    {
        NoBreathing,
        BreatheIn,
        BreatheOut
    }

    [Space] [SerializeField] private Enemy enemy;
    [SerializeField] private EnemyWhispral enemyWhispral;

    [Space] [SerializeField] private Sound soundHurt;
    [SerializeField] private Sound soundDeath;

    [Space] [SerializeField] private Sound soundBreatheIn;
    [SerializeField] private Sound soundBreatheOut;
    [SerializeField] private Vector2 breathingDurationInRange;
    [SerializeField] private Vector2 breathingDurationOutRange;

    [Space] [SerializeField] private GameObject visuals;
    [SerializeField] private GameObject rigidBody;
    protected override State DefaultState => State.NoBreathing;

    protected override void Awake()
    {
        base.Awake();

        fsm.AddState(State.NoBreathing, new NoBreathing(this));
        fsm.AddState(State.BreatheIn,
            new BreathingState(this, soundBreatheIn, breathingDurationInRange, State.BreatheOut));
        fsm.AddState(State.BreatheOut,
            new BreathingState(this, soundBreatheOut, breathingDurationOutRange, State.BreatheIn));
    }

    protected override void Update()
    {
        var isAttachedAndNotJumping = enemyWhispral.CurrentState == EnemyWhispral.State.Attached && !enemy.Jump.jumping;

        if (isAttachedAndNotJumping)
        {
            var current = fsm.CurrentStateStateId;
            if (current != State.BreatheIn && current != State.BreatheOut)
            {
                fsm.NextStateStateId = State.BreatheIn;
            }
        }
        else
        {
            fsm.NextStateStateId = State.NoBreathing;
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
        fsm.NextStateStateId = State.NoBreathing;
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
        private readonly State _nextState = nextState;
        private AudioSource? _audioSource;

        public override void OnStateEnter(State previous)
        {
            base.OnStateEnter(previous);
            owner.Show(false);
            Duration = Random.Range(durationRange.x, durationRange.y);
            _audioSource = sound.Play(owner.enemyWhispral.playerTarget.transform.position);
        }

        public override void OnStateExit(State next)
        {
            base.OnStateExit(next);
            if (_audioSource != null && _audioSource) _audioSource.Stop();
            if (next != _nextState) // Si on ne va pas à l’état suivant prévu, on coupe la respiration.
            {
                owner.Show(true);
            }
        }
    }

    #endregion
}