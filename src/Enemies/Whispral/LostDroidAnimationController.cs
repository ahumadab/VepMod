#nullable disable
using UnityEngine;

namespace VepMod.Enemies.Whispral;

internal class LostDroidAnimationController : MonoBehaviour
{
    [Header("References")] public LostDroid Controller;

    public Animator animator;
    public Transform minigunFirePosition;
    public GameObject ExplosionPrefab;

    [Header("Particles")] public ParticleSystem[] Deathparticles;

    public ParticleSystem footstepParticles;
    public ParticleSystem goreExplosion;

    [Header("Sounds")] [SerializeField] private Sound mediumFootstepSounds;

    [SerializeField] private Sound mediumWoodFootstepSounds;
    [SerializeField] private Sound mediumStoneFootstepSounds;
    [SerializeField] private Sound smallFootstepSounds;
    [SerializeField] private Sound largeFootstepSounds;
    [SerializeField] private Sound transformSounds;
    [SerializeField] private Sound stingerSounds;
    [SerializeField] public Sound minigunAttackSounds;
    [SerializeField] public Sound minigunShortAttackSounds;
    [SerializeField] public Sound minigunAttackGlobalSounds;
    [SerializeField] public Sound minigunShortAttackGlobalSounds;
    [SerializeField] private Sound hurtSounds;
    [SerializeField] private Sound deathSounds;
    [SerializeField] private Sound whipSounds;
    [SerializeField] private Sound explodeSounds;

    private void Update()
    {
        animator.SetBool("isSprinting", Controller._isSprinting);
        animator.SetBool("isWalking", Controller._isWalking);
        animator.SetBool("isTurning", Controller._isTurning);
        animator.SetBool("stun", Controller._isStun);

        if (Controller._animSpeed != animator.speed)
        {
            animator.speed = Controller._animSpeed;
        }

        if (Controller._despawnImpulse)
        {
            Controller._despawnImpulse = false;
            animator.SetTrigger("despawn");
        }

        if (Controller._transformImpulse)
        {
            Controller._transformImpulse = false;
            animator.SetTrigger("transform");
        }

        if (Controller._attackImpulse)
        {
            Controller._attackImpulse = false;
            animator.SetTrigger("attack");
        }

        if (Controller._attackShortImpulse)
        {
            Controller._attackShortImpulse = false;
            animator.SetTrigger("attack2");
        }

        if (Controller._deathImpulse)
        {
            Controller._deathImpulse = false;
            animator.SetTrigger("death");
            deathSounds.Play(Controller._enemy.CenterTransform.position);
        }

        if (!Controller._swingImpulse)
        {
            return;
        }

        Controller._swingImpulse = false;
        animator.SetTrigger("swing");
    }

    public void Despawn()
    {
        Controller._enemy.EnemyParent.Despawn();
    }

    public void ExplodeMinigun()
    {
        Instantiate(ExplosionPrefab, minigunFirePosition);
        PlayExplode();
    }

    public void PlayAttackSmall()
    {
        stingerSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayDeathParticles()
    {
        PlayDeathsound();
        foreach (var deathparticle in Deathparticles)
        {
            deathparticle.Play();
        }
    }

    public void PlayDeathsound()
    {
        deathSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayExplode()
    {
        explodeSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayGorePartExplosion()
    {
        goreExplosion.Play();
    }

    public void PlayHurtSound()
    {
        hurtSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayLargeFootstep()
    {
        largeFootstepSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayMediumFootstep()
    {
        if (LevelGenerator.Instance.Level.NarrativeName == "McJannek Station")
        {
            mediumFootstepSounds.Play(Controller.Enemy.CenterTransform.position);
        }
        else if (LevelGenerator.Instance.Level.NarrativeName == "Headman Manor")
        {
            mediumWoodFootstepSounds.Play(Controller.Enemy.CenterTransform.position);
        }
        else if (LevelGenerator.Instance.Level.NarrativeName == "Swiftbroom Academy")
        {
            mediumStoneFootstepSounds.Play(Controller.Enemy.CenterTransform.position);
        }
        else
        {
            mediumStoneFootstepSounds.Play(Controller.Enemy.CenterTransform.position);
        }
    }

    public void PlayShortStinger()
    {
        minigunShortAttackSounds.Play(Controller.Enemy.CenterTransform.position);
        minigunShortAttackGlobalSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlaySmallFootstep()
    {
        smallFootstepSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayStinger()
    {
        stingerSounds.Play(Controller.Enemy.CenterTransform.position);
        minigunAttackSounds.Play(Controller.Enemy.CenterTransform.position);
        minigunAttackGlobalSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayTransform()
    {
        transformSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void PlayWhipSound()
    {
        whipSounds.Play(Controller.Enemy.CenterTransform.position);
    }

    public void SetDespawn()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            return;
        }

        Controller._enemy.EnemyParent.Despawn();
    }

    public void SetSpawn()
    {
        animator.Play("New State", -1, 0f);
        Controller._damageAmount = 0;
        Controller.Transformed = false;

        if (Controller.Terminator)
        {
            Controller._transformCount = 5f;
            Controller._transformCountMax = Random.Range(5f, 10f);
        }
        else
        {
            Controller._transformCount = 8f;
            Controller._transformCountMax = Random.Range(6f, 12f);
        }
    }

    public void ShakeCameraBig()
    {
        GameDirector.instance.CameraShake.ShakeDistance(6f, 5f, 15f, transform.position, 1.8f);
    }

    public void ShakeCameraFire()
    {
        GameDirector.instance.CameraShake.ShakeDistance(7f, 10f, 20f, transform.position, 8.64f);
        EnemyDirector.instance.SetInvestigate(transform.position, 40f);
    }

    public void ShakeCameraSmall()
    {
        GameDirector.instance.CameraShake.ShakeDistance(2f, 5f, 15f, transform.position, 0.8f);
    }

    public void ShakeCameraStep()
    {
        GameDirector.instance.CameraShake.ShakeDistance(3f, 5f, 15f, transform.position, 1f);
        footstepParticles.Play();
    }

    public void StartFiring()
    {
        Controller._fireBullets = true;
    }

    public void StartTurning()
    {
        Controller._isTurningToPlayer = true;
    }

    public void StopFiring()
    {
        Controller._fireBullets = false;
    }

    public void StopFiringSounds() { }

    public void StopTurning()
    {
        Controller._isTurningToPlayer = false;
    }
}