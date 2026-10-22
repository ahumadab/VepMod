using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace HarmonyPatches.Tests;

// Harmony finds an original by name and binds a patch's parameters by name; a mismatch only shows once the game loads
// the plugin. This replays that binding against the game assembly the plugin compiles with.
public class HarmonyPatchesTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> PatchKinds = new() { "Prefix", "Postfix", "Finalizer", "Transpiler" };

    private static readonly HashSet<string> SpecialParameters = new()
    {
        "__instance", "__result", "__state", "__originalMethod", "__args", "__runOriginal", "__exception"
    };

    [Fact]
    public void Every_patch_binds_to_its_original()
    {
        var references = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "references.txt"));
        using var context = new MetadataLoadContext(new PathAssemblyResolver(references));
        var plugin = context.LoadFromAssemblyPath(references.Single(path => Path.GetFileName(path) == "VepMod.dll"));

        var patches = plugin.GetTypes().SelectMany(PatchesOf).ToList();
        Assert.NotEmpty(patches);
        var errors = patches.SelectMany(BindingErrors).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    private static IEnumerable<Patch> PatchesOf(Type patchClass)
    {
        var classTarget = Target.Of(patchClass.GetCustomAttributesData());
        foreach (var method in patchClass.GetMethods(All))
        {
            var attributes = method.GetCustomAttributesData();
            var kind = attributes.Select(a => a.AttributeType.Name.Replace("Harmony", ""))
                           .FirstOrDefault(PatchKinds.Contains)
                       ?? (PatchKinds.Contains(method.Name) ? method.Name : null);
            if (kind == null) continue;

            var target = classTarget.With(Target.Of(attributes));
            if (target.Type != null) yield return new Patch(method, kind, target);
        }
    }

    private static IEnumerable<string> BindingErrors(Patch patch)
    {
        var where = $"{patch.Method.DeclaringType!.Name}.{patch.Method.Name}";
        var original = FindOriginal(patch.Target, out var error);
        if (original == null)
        {
            yield return $"{where}: {error}";
            yield break;
        }

        if (patch.Kind == "Transpiler") yield break;

        var originalParameters = original.GetParameters();
        foreach (var parameter in patch.Method.GetParameters())
        {
            var name = parameter.Name!;
            var bound = name switch
            {
                "__instance" => !original.IsStatic,
                "__result" => original is MethodInfo { ReturnType.FullName: not "System.Void" },
                _ when SpecialParameters.Contains(name) => true,
                _ when name.StartsWith("___") => FindField(original.DeclaringType!, name[3..]),
                _ when name.StartsWith("__") && int.TryParse(name[2..], out var index) =>
                    index < originalParameters.Length,
                _ => originalParameters.Any(p => p.Name == name)
            };
            if (!bound) yield return $"{where}: parameter '{name}' matches nothing on {original}";
        }
    }

    // Harmony searches the declaring type first, then its base types, and refuses an ambiguous name.
    private static MethodBase? FindOriginal(Target target, out string error)
    {
        for (var type = target.Type; type != null; type = type.BaseType)
        {
            var candidates = target.Name == null
                ? type.GetConstructors(All).Cast<MethodBase>()
                : type.GetMethods(All).Where(m => m.Name == target.Name);
            if (target.ArgumentTypes != null)
            {
                candidates = candidates.Where(m =>
                    m.GetParameters().Select(p => p.ParameterType).SequenceEqual(target.ArgumentTypes));
            }

            var found = candidates.ToList();
            if (found.Count == 1)
            {
                error = "";
                return found[0];
            }

            if (found.Count > 1)
            {
                error = $"{target.Type!.Name}.{target.Name} is ambiguous: give its argument types";
                return null;
            }
        }

        error = $"{target.Type!.Name}.{target.Name} not found";
        return null;
    }

    private static bool FindField(Type type, string name)
    {
        for (var declaring = type; declaring != null; declaring = declaring.BaseType)
        {
            if (declaring.GetField(name, All) != null) return true;
        }

        return false;
    }

    private sealed record Patch(MethodInfo Method, string Kind, Target Target);

    private sealed record Target(Type? Type, string? Name, Type[]? ArgumentTypes)
    {
        public static Target Of(IEnumerable<CustomAttributeData> attributes)
        {
            Type? type = null;
            string? name = null;
            Type[]? argumentTypes = null;
            foreach (var attribute in attributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
            {
                foreach (var argument in attribute.ConstructorArguments)
                {
                    switch (argument.Value)
                    {
                        case Type value:
                            type = value;
                            break;
                        case string value:
                            name = value;
                            break;
                        case IReadOnlyCollection<CustomAttributeTypedArgument> values:
                            argumentTypes = values.Select(v => (Type)v.Value!).ToArray();
                            break;
                    }
                }
            }

            return new Target(type, name, argumentTypes);
        }

        public Target With(Target over)
        {
            return new Target(over.Type ?? Type, over.Name ?? Name, over.ArgumentTypes ?? ArgumentTypes);
        }
    }
}
