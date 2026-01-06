using System;
using UnityEngine;
using UnityEngine.AI;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Utilitaires statiques pour HallucinationDroid et ses composants.
/// </summary>
public static class DroidHelpers
{
    /// <summary>
    ///     Vérifie si l'état donné est un état de mouvement.
    /// </summary>
    public static bool IsMovementState(DroidController.StateId state)
    {
        return state is DroidController.StateId.Wander
            or DroidController.StateId.Sprint
            or DroidController.StateId.StalkApproach
            or DroidController.StateId.StalkStare
            or DroidController.StateId.StalkFlee;
    }

    /// <summary>
    ///     Recherche un enfant par nom dans la hiérarchie.
    /// </summary>
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

    /// <summary>
    ///     Recherche plusieurs enfants par nom dans la hiérarchie.
    /// </summary>
    public static void FindChildrenByNames(
        Transform root,
        bool includeInactive,
        params (string name, Action<Transform> onFound)[] searches)
    {
        if (root == null) return;

        var remaining = searches.Length;
        foreach (var child in root.GetComponentsInChildren<Transform>(includeInactive))
        {
            foreach (var (name, onFound) in searches)
            {
                if (child.name == name)
                {
                    onFound(child);
                    remaining--;
                    if (remaining == 0) return;
                }
            }
        }
    }

    /// <summary>
    ///     Valide une position sur le NavMesh.
    /// </summary>
    public static bool TryGetNavMeshPosition(Vector3 position, out Vector3 result, int areaMask, float maxDistance = 5f)
    {
        if (NavMesh.SamplePosition(position, out var hit, maxDistance, areaMask))
        {
            result = hit.position;
            return true;
        }

        result = position;
        return false;
    }

    /// <summary>
    ///     Récupère un clip d'animation par son nom.
    /// </summary>
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

    /// <summary>
    ///     Vérifie si le NavMeshAgent est valide et sur le NavMesh.
    /// </summary>
    public static bool IsNavAgentValid(NavMeshAgent agent)
    {
        return agent != null && agent.isOnNavMesh;
    }

    /// <summary>
    ///     Retourne la position du joueur local ou null si non disponible.
    /// </summary>
    public static Vector3? GetLocalPlayerPosition()
    {
        var player = PlayerAvatar.instance;
        return player != null ? player.transform.position : null;
    }
}