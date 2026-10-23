using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

public class EnemyWhispralAnim : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<EnemyWhispralAnim>();

    // The Whispral prefab in the asset bundle serializes these by name: a renamed field loses its value.
#pragma warning disable IDE1006
    [Space][SerializeField] private Enemy enemy;
    [SerializeField] private EnemyWhispral enemyWhispral;

    [Space][SerializeField] private Sound soundHurt;
    [SerializeField] private Sound soundDeath;

    [Space][SerializeField] private GameObject visuals;
    [SerializeField] private GameObject rigidBody;
#pragma warning restore IDE1006

    private bool _hidden;

    private void Update()
    {
        var state = enemyWhispral.CurrentState;
        SetHidden(state == EnemyWhispral.State.Attached && !enemy.Jump.jumping, state);
    }

    public void Death()
    {
        soundDeath.Play(enemy.transform.position);
    }

    public void Hurt()
    {
        soundHurt.Play(enemy.transform.position);
    }

    public void Despawn()
    {
        SetHidden(false, EnemyWhispral.State.Despawn);
    }

    // Without its Visuals and Rigidbody, the Whispral is invisible and intangible: thrown objects pass through, grabs miss.
    private void SetHidden(bool hidden, EnemyWhispral.State state)
    {
        if (hidden == _hidden) return;
        _hidden = hidden;
        visuals.SetActive(!hidden);
        rigidBody.SetActive(!hidden);
        LOG.Debug($"Whispral {enemy.PhotonView.ViewID} {(hidden ? "hidden" : "shown")} in state {state}.");
    }
}
