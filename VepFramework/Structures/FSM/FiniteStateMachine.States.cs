using UnityEngine;

namespace VepMod.VepFramework.Structures.FSM;

public partial class FiniteStateMachine<TFSM, TKey>
{
    /// <summary>
    ///     Most basic State, inherit from this to start implementing your fsm
    /// </summary>
    public class State
    {
        public TFSM FSM { get; set; }
        public virtual void OnStateEnter(TKey previous) { }
        public virtual void OnStateExit(TKey next) { }
        public virtual void OnStateUpdate() { }
    }

    //* States belows are archetypes you can use or inherit from in your fsm 

    /// <summary>
    ///     State with a duration mechanic, inherit this to save the trouble of handling a timer yourself
    /// </summary>
    public class StateTimed : State
    {
        public float TimeElapsed { get; protected set; }

        public override void OnStateEnter(TKey previous)
        {
            base.OnStateEnter(previous);
            TimeElapsed = 0;
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();
            TimeElapsed += Time.deltaTime;
        }
    }

    /// <summary>
    ///     A timed state where you change states at the end of a duration
    /// </summary>
    public class StateTransition : StateTimed
    {
        public float Duration;
        public TKey NextState;

        public StateTransition(float duration, TKey nextState)
        {
            Duration = duration;
            NextState = nextState;
        }

        public override void OnStateEnter(TKey previous)
        {
            base.OnStateEnter(previous);
            if (Duration <= 0)
            {
                FSM.NextStateKey = NextState;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();
            if (TimeElapsed >= Duration)
            {
                FSM.NextStateKey = NextState;
            }
        }
    }

    //* Feel Free to add new archétypes here !
}