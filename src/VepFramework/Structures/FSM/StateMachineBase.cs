using System.Collections.Generic;
using UnityEngine;

namespace VepMod.VepFramework.Structures.FSM;

/// <summary>
///     A state machine is a structure to make easier the use different states and behavior depending on a key
///     It could be visually represented with a flowchart, the key feature being that states know previous and next
///     key state
///     The Unity Animator is by definition a FSM for exemple
///     You can inherit this structure to add fields accessible from the states you'll define
/// </summary>
/// <typeparam name="TStateId">
///     The type used to store States in a dictionary, an enum is usually a preferred choice but you do
///     you. Juste beware of boxing
/// </typeparam>
public partial class StateMachineBase<TMachine, TStateId> where TMachine : StateMachineBase<TMachine, TStateId>
{
    private readonly StateBase Default = new();
    private readonly Dictionary<TStateId, StateBase> states = new();
    private StateBase _currentStateBaseRunning;

    public StateMachineBase(TStateId defaultStateId)
    {
        CurrentStateStateId = defaultStateId;
        NextStateStateId = defaultStateId;
    }

    public TStateId CurrentStateStateId { get; private set; }

    public TStateId NextStateStateId { get; set; }

    public void AddState(TStateId stateId, StateBase stateBase)
    {
        stateBase.Machine = this as TMachine;
        if (states.ContainsKey(stateId))
        {
            Debug.LogWarning($"[FSM] Fsm already defines a state with key {stateId}, it will be overidden");
        }

        states[stateId] = stateBase;
    }

    public StateBase GetState(TStateId stateId)
    {
        if (!states.TryGetValue(stateId, out var result))
        {
            return Default;
        }

        return result;
    }

    //* In a perfect world this call should be avoided to preserve encapsulation and type secrecy
    public T GetStateTyped<T>(TStateId stateId) where T : StateBase
    {
        return GetState(stateId) as T;
    }

    private void RefreshStatesIO()
    {
        var state = GetState(NextStateStateId);
        while (state != _currentStateBaseRunning)
        {
            var next = NextStateStateId;
            _currentStateBaseRunning?.OnStateExit(next);
            if (!states.ContainsKey(next))
            {
                Debug.LogWarning($"[FSM] Fsm does not define any state with key {next}, it will play default state");
            }

            _currentStateBaseRunning = GetState(next);
            _currentStateBaseRunning?.OnStateEnter(CurrentStateStateId);
            CurrentStateStateId = next;

            state = GetState(NextStateStateId);
        }
    }

    public void Update()
    {
        RefreshStatesIO();
        _currentStateBaseRunning?.OnStateUpdate();
        RefreshStatesIO();
    }
}