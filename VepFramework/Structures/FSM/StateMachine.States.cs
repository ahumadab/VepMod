using UnityEngine;

namespace VepMod.VepFramework.Structures.FSM;

public partial class StateMachineBase<TMachine, TStateId>
{
    /// <summary>
    ///     Most basic State, inherit from this to start implementing your fsm
    /// </summary>
    public class StateBase
    {
        public TMachine Machine { get; set; }
        public virtual void OnStateEnter(TStateId previous) { }
        public virtual void OnStateExit(TStateId next) { }
        public virtual void OnStateUpdate() { }
    }

    //* States belows are archetypes you can use or inherit from in your fsm 

    /// <summary>
    ///     State with a duration mechanic, inherit this to save the trouble of handling a timer yourself
    /// </summary>
    public class StateBaseTimed : StateBase
    {
        public float TimeElapsed { get; protected set; }

        public override void OnStateEnter(TStateId previous)
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
    public class StateBaseTransition : StateBaseTimed
    {
        public float Duration;
        public TStateId NextState;

        public StateBaseTransition(float duration, TStateId nextState)
        {
            Duration = duration;
            NextState = nextState;
        }

        public override void OnStateEnter(TStateId previous)
        {
            base.OnStateEnter(previous);
            if (Duration <= 0)
            {
                Machine.NextStateStateId = NextState;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();
            if (TimeElapsed >= Duration)
            {
                Machine.NextStateStateId = NextState;
            }
        }
    }

    //* Feel Free to add new archétypes here !
}