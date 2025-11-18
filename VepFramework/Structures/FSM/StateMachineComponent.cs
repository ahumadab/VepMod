using UnityEngine;

namespace VepMod.VepFramework.Structures.FSM;

//For Component with an embarqued FSM
public abstract class StateMachineComponent<TOwner, TStateId> : MonoBehaviour
    where TOwner : StateMachineComponent<TOwner, TStateId>
{
    protected StateMachine fsm;

    protected abstract TStateId DefaultState { get; }

    protected virtual void Awake()
    {
        fsm = new StateMachine(this as TOwner, DefaultState);
    }

    protected virtual void Update()
    {
        fsm?.Update();
    }

    protected class StateMachine : StateMachineBase<StateMachine, TStateId>
    {
        public StateMachine(TOwner owner, TStateId defaultKey) : base(defaultKey)
        {
            Owner = owner;
        }

        public TOwner Owner { get; }
    }
}