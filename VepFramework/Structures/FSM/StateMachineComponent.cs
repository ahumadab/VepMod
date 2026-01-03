using UnityEngine;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8604 // Possible null reference argument.

namespace VepMod.VepFramework.Structures.FSM;

//For Component with an embarked FSM
public abstract class StateMachineComponent<TOwner, TStateId> : MonoBehaviour
    where TOwner : StateMachineComponent<TOwner, TStateId>
{
    protected StateMachine Fsm;

    protected abstract TStateId DefaultState { get; }

    protected virtual void Awake()
    {
        Fsm = new StateMachine(this as TOwner, DefaultState);
    }

    protected virtual void Update()
    {
        Fsm.Update();
    }

    protected sealed class StateMachine : StateMachineBase<StateMachine, TStateId>
    {
        public StateMachine(TOwner owner, TStateId defaultKey) : base(defaultKey)
        {
            Owner = owner;
        }

        public TOwner Owner { get; }
    }
}