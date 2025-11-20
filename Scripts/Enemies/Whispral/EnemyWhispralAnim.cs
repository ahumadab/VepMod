using UnityEngine;

namespace VepMod.Scripts.Enemies.Whispral;

public class EnemyWhispralAnim : MonoBehaviour
{
    [Space] [SerializeField] private Enemy enemy;

    [SerializeField] private EnemyWhispral enemyWhispral;

    [Space] [SerializeField] private Sound? soundHurt;
    [SerializeField] private Sound? soundDeath;

    [Space] public Sound? soundBreatheIn;

    public Sound? soundBreatheOut;
    private bool _breathingIn;

    private BreathingState _breathingState = BreathingState.None;
    private float _breathingTimer;
    private bool _soundPlayerReleaseImpulse;


    private void Update()
    {
        if (enemyWhispral.CurrentState == EnemyWhispral.State.DetachWait)
        {
            if (_soundPlayerReleaseImpulse)
            {
                StopBreathing();
                _soundPlayerReleaseImpulse = false;
            }
        }
        else
        {
            _soundPlayerReleaseImpulse = true;
        }

        if (enemyWhispral.CurrentState == EnemyWhispral.State.Attached && !enemy.Jump.jumping)
        {
            if (_breathingState != BreathingState.Slow)
            {
                _breathingState = BreathingState.Slow;
                _breathingTimer = 0f; // pour déclencher immédiatement le premier son
                _breathingIn = true; // on commence par l'inspiration (au choix)
            }

            if (_breathingTimer <= 0f)
            {
                if (_breathingIn)
                {
                    soundBreatheIn?.Play(enemy.transform.position);
                    _breathingTimer = 3f;
                }
                else
                {
                    soundBreatheOut?.Play(enemy.transform.position);
                    _breathingTimer = 4.5f;
                }

                // on alterne pour la prochaine fois
                _breathingIn = !_breathingIn;
            }
        }
        else
        {
            if (_breathingState != BreathingState.None)
            {
                StopBreathing();
            }
        }

        if (_breathingState == BreathingState.Slow)
        {
            _breathingTimer -= 1f * Time.deltaTime;
        }
    }

    public void Death()
    {
        StopBreathing();
        soundDeath?.Play(enemy.transform.position);
    }

    public void Hurt()
    {
        StopBreathing();
        soundHurt?.Play(enemy.transform.position);
    }

    public void StopBreathing()
    {
        _breathingState = BreathingState.None;
        _breathingTimer = 0f;
        _breathingIn = false;
    }

    private enum BreathingState
    {
        None,
        Slow
    }
}