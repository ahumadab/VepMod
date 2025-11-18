using UnityEngine;

namespace VepMod.Scripts.Enemies.Whispral;

public class EnemyWhispralAnim : MonoBehaviour
{
    public enum BreathingState
    {
        None = 0,
        Slow = 1,
        Medium = 2,
        Fast = 3,
        FastNoSound = 4
    }

    public enum FootstepState
    {
        None = 0,
        Standing = 1,
        TwoStep = 2,
        Moving = 3,
        Sprinting = 4,
        TimedSteps = 5
    }

    [Space] public Enemy enemy;

    public EnemyWhispral enemyWhispral;

    [Space] public ParticleSystem particleBreath;

    public ParticleSystem particleBreathFast;

    public ParticleSystem particleBreathConstant;

    [Space] public Transform transformFoot;

    public ParticleSystem particleFootstepShapeRight;

    public ParticleSystem particleFootstepShapeLeft;

    public ParticleSystem particleFootstepSmoke;

    [Space] public Sound soundBreatheIn;

    public Sound soundBreatheOut;

    [Space] public Sound soundBreatheInFast;

    public Sound soundBreatheOutFast;

    [Space] public Sound soundFootstep;

    public Sound soundFootstepSprint;

    [Space] public Sound soundStunStart;

    public Sound soundStunLoop;

    public Sound soundStunStop;

    [Space] public Sound soundJump;

    public Sound soundLand;

    [Space] public Sound soundPlayerPickup;

    public Sound soundPlayerRelease;

    public Sound soundPlayerMove;

    public Sound soundPlayerMoveStop;

    [Space] public Sound soundHurt;

    public Sound soundDeath;

    private bool breathingCurrent;

    private BreathingState breathingState;

    private float breathingTimer;

    private int footstepCurrent = 1;

    private Vector3 footstepPositionPrevious;

    private Vector3 footstepPositionPreviousLeft;

    private Vector3 footstepPositionPreviousRight;

    private FootstepState footstepState;

    private bool jumpStartImpulse = true;

    private bool jumpStopImpulse;

    internal Materials.MaterialTrigger material = new();

    private float movingTimer;

    private bool soundJumpImpulse;

    private bool soundLandImpulse;

    private bool soundPlayerMoveImpulse;

    private bool soundPlayerPickupImpulse;

    private bool soundPlayerReleaseImpulse;

    private float soundStunPauseTimer;

    private bool soundStunStartImpulse;

    private bool soundStunStopImpulse;

    private float stopStepTimer;

    private float timedStepsTimer;

    private void Update()
    {
        BreathingLogic();
        FootstepLogic();
        if (enemyWhispral.CurrentState == EnemyWhispral.State.Stun)
        {
            if (soundStunStartImpulse)
            {
                StopBreathing();
                soundStunStart.Play(particleBreath.transform.position);
                soundStunStartImpulse = false;
            }

            if (soundStunPauseTimer > 0f)
            {
                if (soundStunStopImpulse)
                {
                    soundStunStop.Play(particleBreath.transform.position);
                    soundStunStopImpulse = false;
                }

                soundStunLoop.PlayLoop(false, 2f, 5f);
                particleBreathConstant.Stop();
            }
            else
            {
                soundStunLoop.PlayLoop(true, 2f, 10f);
                particleBreathConstant.Play();
                soundStunStopImpulse = true;
            }
        }
        else
        {
            if (soundStunStopImpulse)
            {
                soundStunStop.Play(particleBreath.transform.position);
                soundStunStopImpulse = false;
            }

            soundStunLoop.PlayLoop(false, 2f, 5f);
            particleBreathConstant.Stop();
            soundStunStartImpulse = true;
        }

        if (soundStunPauseTimer > 0f)
        {
            soundStunPauseTimer -= Time.deltaTime;
        }

        if (enemy.Jump.jumping)
        {
            if (soundJumpImpulse)
            {
                particleBreath.Play();
                soundJump.Play(particleBreath.transform.position);
                StopBreathing();
                soundJumpImpulse = false;
            }

            soundLandImpulse = true;
        }
        else
        {
            if (soundLandImpulse)
            {
                particleBreathFast.Play();
                soundLand.Play(particleBreath.transform.position);
                StopBreathing();
                soundLandImpulse = false;
            }

            soundJumpImpulse = true;
        }

        if (enemyWhispral.CurrentState == EnemyWhispral.State.PrepareAttach)
        {
            if (soundPlayerPickupImpulse)
            {
                StopBreathing();
                particleBreath.Play();
                soundPlayerPickup.Play(particleBreath.transform.position);
                soundPlayerPickupImpulse = false;
            }
        }
        else
        {
            soundPlayerPickupImpulse = true;
        }

        if (enemyWhispral.CurrentState == EnemyWhispral.State.DetachWait)
        {
            if (soundPlayerReleaseImpulse)
            {
                StopBreathing();
                particleBreath.Play();
                soundPlayerRelease.Play(particleBreath.transform.position);
                soundPlayerReleaseImpulse = false;
            }
        }
        else
        {
            soundPlayerReleaseImpulse = true;
        }

        if (enemyWhispral.CurrentState == EnemyWhispral.State.Attached && !enemy.Jump.jumping)
        {
            soundPlayerMove.PlayLoop(true, 2f, 10f);
            soundPlayerMoveImpulse = true;
            return;
        }

        if (soundPlayerMoveImpulse)
        {
            soundPlayerMoveStop.Play(particleBreath.transform.position);
            soundPlayerMoveImpulse = false;
        }

        soundPlayerMove.PlayLoop(false, 2f, 10f);
    }

    public void Death()
    {
        particleBreathConstant.Stop();
        StopBreathing();
        StunPause();
        soundDeath.Play(particleBreath.transform.position);
    }

    public void Hurt()
    {
        StopBreathing();
        StunPause();
        soundHurt.Play(particleBreath.transform.position);
    }

    public void StopBreathing()
    {
        soundBreatheIn.Stop();
        soundBreatheInFast.Stop();
        soundBreatheOut.Stop();
        soundBreatheOutFast.Stop();
    }

    public void StunPause()
    {
        soundStunPauseTimer = 1f;
    }

    private void BreathingLogic()
    {
        if (enemy.Jump.jumping ||
            enemyWhispral.CurrentState == EnemyWhispral.State.Stun ||
            enemyWhispral.CurrentState == EnemyWhispral.State.Detach ||
            enemyWhispral.CurrentState == EnemyWhispral.State.DetachWait ||
            enemyWhispral.CurrentState == EnemyWhispral.State.PrepareAttach)
        {
            breathingState = BreathingState.None;
        }
        else if (enemyWhispral.CurrentState == EnemyWhispral.State.Attached)
        {
            breathingState = BreathingState.FastNoSound;
        }
        else if (enemyWhispral.CurrentState == EnemyWhispral.State.GoToPlayer ||
                 enemyWhispral.CurrentState == EnemyWhispral.State.Leave)
        {
            breathingState = BreathingState.Fast;
        }
        else if (enemyWhispral.CurrentState == EnemyWhispral.State.Roam ||
                 enemyWhispral.CurrentState == EnemyWhispral.State.Investigate)
        {
            breathingState = BreathingState.Medium;
        }
        else
        {
            breathingState = BreathingState.Slow;
        }

        if (breathingState == BreathingState.None)
        {
            soundBreatheIn.Stop();
            soundBreatheOut.Stop();
        }

        if (breathingTimer <= 0f)
        {
            if (breathingCurrent)
            {
                breathingCurrent = false;
                if (breathingState != BreathingState.FastNoSound)
                {
                    if (breathingState == BreathingState.Fast)
                    {
                        soundBreatheInFast.Play(particleBreath.transform.position);
                    }
                    else
                    {
                        soundBreatheIn.Play(particleBreath.transform.position);
                    }
                }
                else
                {
                    particleBreathFast.Play();
                }

                breathingTimer = 3f;
            }
            else
            {
                breathingCurrent = true;
                if (breathingState != BreathingState.FastNoSound)
                {
                    if (breathingState == BreathingState.Fast)
                    {
                        soundBreatheOutFast.Play(particleBreath.transform.position);
                    }
                    else
                    {
                        soundBreatheOut.Play(particleBreath.transform.position);
                    }

                    particleBreath.Play();
                }
                else
                {
                    particleBreathFast.Play();
                }

                breathingTimer = 4.5f;
            }
        }

        if (breathingState == BreathingState.Slow)
        {
            breathingTimer -= 1f * Time.deltaTime;
        }
        else if (breathingState == BreathingState.Medium)
        {
            breathingTimer -= 2f * Time.deltaTime;
        }
        else
        {
            breathingTimer -= 5f * Time.deltaTime;
        }
    }

    private void FootstepLogic()
    {
        if (movingTimer > 0f)
        {
            movingTimer -= Time.deltaTime;
        }

        if ((enemyWhispral.CurrentState == EnemyWhispral.State.Roam ||
             enemyWhispral.CurrentState == EnemyWhispral.State.Investigate ||
             enemyWhispral.CurrentState == EnemyWhispral.State.GoToPlayer ||
             enemyWhispral.CurrentState == EnemyWhispral.State.Attached ||
             enemyWhispral.CurrentState == EnemyWhispral.State.Leave) && enemy.Rigidbody.velocity.magnitude > 0.5f)
        {
            movingTimer = 0.25f;
        }

        if (enemyWhispral.CurrentState == EnemyWhispral.State.Stun || enemy.Jump.jumping)
        {
            footstepState = FootstepState.None;
        }
        else if (enemyWhispral.CurrentState == EnemyWhispral.State.StunEnd ||
                 enemyWhispral.CurrentState == EnemyWhispral.State.NoticePlayer)
        {
            footstepState = FootstepState.TimedSteps;
        }
        else if (movingTimer > 0f)
        {
            if (enemyWhispral.CurrentState == EnemyWhispral.State.GoToPlayer ||
                enemyWhispral.CurrentState == EnemyWhispral.State.Attached ||
                enemyWhispral.CurrentState == EnemyWhispral.State.Leave)
            {
                footstepState = FootstepState.Sprinting;
            }
            else
            {
                footstepState = FootstepState.Moving;
            }
        }
        else if (footstepState == FootstepState.Moving)
        {
            footstepState = FootstepState.TwoStep;
        }
        else if (footstepState != FootstepState.TwoStep)
        {
            footstepState = FootstepState.Standing;
        }

        if (enemy.Jump.jumping)
        {
            if (jumpStartImpulse)
            {
                jumpStopImpulse = true;
                jumpStartImpulse = false;
                FootstepSet();
                FootstepSet();
            }
        }
        else if (jumpStopImpulse)
        {
            jumpStopImpulse = false;
            jumpStartImpulse = true;
            FootstepSet();
            FootstepSet();
        }

        if ((footstepState == FootstepState.Moving || footstepState == FootstepState.Sprinting) &&
            Vector3.Distance(transformFoot.position, footstepPositionPrevious) > 1f)
        {
            FootstepSet();
        }

        if (footstepState == FootstepState.TimedSteps)
        {
            if (timedStepsTimer <= 0f)
            {
                timedStepsTimer = 0.25f;
                FootstepSet();
            }
            else
            {
                timedStepsTimer -= Time.deltaTime;
            }
        }
        else
        {
            timedStepsTimer = 0f;
        }

        if (footstepState == FootstepState.TwoStep)
        {
            if (stopStepTimer == -1f)
            {
                FootstepSet();
                stopStepTimer = 0.25f;
                return;
            }

            stopStepTimer -= Time.deltaTime;
            if (stopStepTimer <= 0f)
            {
                footstepState = FootstepState.Standing;
                FootstepSet();
                stopStepTimer = -1f;
            }
        }
        else
        {
            stopStepTimer = -1f;
        }
    }

    private void FootstepSet()
    {
        var vector = transformFoot.right * (-0.3f * footstepCurrent);
        var vector2 = Random.insideUnitSphere * 0.15f;
        vector2.y = 0f;
        if (Physics.Raycast(transformFoot.position + vector + vector2, Vector3.down * 2f, out var hitInfo, 3f,
                LayerMask.GetMask("Default")))
        {
            var particleSystem = particleFootstepShapeRight;
            var vector3 = footstepPositionPreviousRight;
            if (footstepCurrent == 1)
            {
                particleSystem = particleFootstepShapeLeft;
                vector3 = footstepPositionPreviousLeft;
            }

            if (Vector3.Distance(vector3, hitInfo.point) > 0.2f)
            {
                particleSystem.transform.position = hitInfo.point + Vector3.up * 0.02f;
                particleSystem.transform.eulerAngles = new Vector3(0f, transformFoot.eulerAngles.y, 0f);
                particleSystem.Play();
                particleFootstepSmoke.transform.position = particleSystem.transform.position;
                particleFootstepSmoke.transform.rotation = particleSystem.transform.rotation;
                particleFootstepSmoke.Play();
                Materials.Instance.Impulse(particleSystem.transform.position + Vector3.up * 0.5f, Vector3.down,
                    Materials.SoundType.Medium, true, true, material, Materials.HostType.Enemy);
                if (footstepState == FootstepState.Sprinting)
                {
                    soundFootstepSprint.Play(particleSystem.transform.position);
                }
                else
                {
                    soundFootstep.Play(particleSystem.transform.position);
                }

                if (footstepCurrent == 1)
                {
                    footstepPositionPreviousLeft = hitInfo.point;
                }
                else
                {
                    footstepPositionPreviousRight = hitInfo.point;
                }

                footstepCurrent *= -1;
            }
        }

        footstepPositionPrevious = transformFoot.position;
    }
}