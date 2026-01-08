#nullable disable
using System.Collections;
using System.Threading.Tasks;

namespace VepMod.VepFramework.Extensions;

public static class TaskExtensions
{
    public static IEnumerator AsCoroutine(this Task task)
    {
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.Exception != null)
        {
            throw task.Exception;
        }
    }
}