using System.Collections.Generic;
using UnityEngine;

namespace VepMod.VepFramework.Structures.FSM;

/// <summary>
///     A finite state machine is a structure to make easier the use different states and behavior depending on a key
///     It could be visually represented with a flowchart, the key feature being that states know previous and next
///     key state
///     The Unity Animator is by definition a FSM for exemple
///     You can inherit this structure to add fields accessible from the states you'll define
/// </summary>
/// <typeparam name="TKey">
///     The type used to store States in a dictionary, an enum is usually a preferred choice but you do
///     you. Juste beware of boxing
/// </typeparam>
public partial class FiniteStateMachine<TFSM, TKey> where TFSM : FiniteStateMachine<TFSM, TKey>
{
    private readonly State Default = new();
    private readonly Dictionary<TKey, State> states = new();
    private State currentStateRunning;

    public FiniteStateMachine(TKey defaultKey)
    {
        CurrentStateKey = defaultKey;
        NextStateKey = defaultKey;
    }

    public TKey CurrentStateKey { get; private set; }

    public TKey NextStateKey { get; set; }

    public void AddState(TKey key, State state)
    {
        state.FSM = this as TFSM;
        if (states.ContainsKey(key))
        {
            Debug.LogWarning($"[FSM] Fsm already defines a state with key {key}, it will be overidden");
        }

        states[key] = state;
    }

    public State GetState(TKey key)
    {
        if (!states.TryGetValue(key, out var result))
        {
            return Default;
        }

        return result;
    }

    //* In a perfect world this call should be avoided to preserve encapsulation and type secrecy
    public T GetStateTyped<T>(TKey key) where T : State
    {
        return GetState(key) as T;
    }

    private void RefreshStatesIO()
    {
        var state = GetState(NextStateKey);
        while (state != currentStateRunning)
        {
            var next = NextStateKey;
            currentStateRunning?.OnStateExit(next);
            if (!states.ContainsKey(next))
            {
                Debug.LogWarning($"[FSM] Fsm does not define any state with key {next}, it will play default state");
            }

            currentStateRunning = GetState(next);
            currentStateRunning?.OnStateEnter(CurrentStateKey);
            CurrentStateKey = next;

            state = GetState(NextStateKey);
        }
    }

    public void Update()
    {
        RefreshStatesIO();
        currentStateRunning?.OnStateUpdate();
        RefreshStatesIO();
    }
}