using UnityEngine;
using UnityEngine.AI;

namespace VepMod.Enemies.Whispral;

public static class DroidHelpers
{
    public static bool IsMovementState(DroidController.StateId state)
    {
        return state is DroidController.StateId.Wander
            or DroidController.StateId.Sprint
            or DroidController.StateId.StalkApproach
            or DroidController.StateId.StalkStare
            or DroidController.StateId.StalkFlee;
    }

    public static Transform? FindChildByName(Transform? root, string name, bool includeInactive = true)
    {
        if (root == null) return null;

        foreach (var child in root.GetComponentsInChildren<Transform>(includeInactive))
        {
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    public static AnimationClip? GetAnimationClip(Animator animator, string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return null;

        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == clipName)
            {
                return clip;
            }
        }

        return null;
    }

    public static bool IsNavAgentValid(NavMeshAgent agent)
    {
        return agent != null && agent.isOnNavMesh;
    }

    public static Vector3? GetLocalPlayerPosition()
    {
        var player = PlayerAvatar.instance;
        return player != null ? player.transform.position : null;
    }
}
