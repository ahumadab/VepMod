# Reads a built asset bundle, which the Unity sources in Repo_04 may not match:
#   python tooling/bundle-dump.py                        lists the assets of Ressources/vepmod_prefabs
#   python tooling/bundle-dump.py --script <Script>      prints that script's serialized fields on every object holding it,
#                                                        references by name (EnemyWhispralAnim with --bundle Ressources/VepMod.repobundle)
#   --bundle <path>                                      reads another bundle
# Needs UnityPy: pip install -r tooling/requirements.txt
import argparse
import os

import UnityPy

DEFAULT_BUNDLE = os.path.join(os.path.dirname(__file__), "..", "Ressources", "vepmod_prefabs")


def object_name(obj):
    data = obj.read()
    if obj.type.name == "MonoBehaviour":
        holder = data.m_GameObject.read().m_Name if data.m_GameObject else "?"
        return f"{holder}/{script_name(data)}"
    return getattr(data, "m_Name", None) or obj.type.name


def script_name(data):
    return data.m_Script.read().m_ClassName if data.m_Script else "?"


def resolve(value, by_path_id):
    if isinstance(value, dict):
        if set(value) == {"m_FileID", "m_PathID"}:
            if value["m_PathID"] == 0:
                return None
            target = by_path_id.get(value["m_PathID"])
            return object_name(target) if target else f"<external {value['m_PathID']}>"
        return {key: resolve(item, by_path_id) for key, item in value.items()}
    if isinstance(value, list):
        return [resolve(item, by_path_id) for item in value]
    return value


def print_fields(fields, indent):
    for key, value in fields.items():
        if isinstance(value, dict):
            print(f"{indent}{key}:")
            print_fields(value, indent + "  ")
        else:
            print(f"{indent}{key}: {value}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", default=DEFAULT_BUNDLE)
    parser.add_argument("--script")
    args = parser.parse_args()

    env = UnityPy.load(args.bundle)
    by_path_id = {obj.path_id: obj for obj in env.objects}

    if not args.script:
        for path, obj in sorted(env.container.items()):
            print(f"{obj.type.name:<14} {path}")
        return

    found = False
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour" or script_name(obj.read()) != args.script:
            continue
        found = True
        print(object_name(obj))
        fields = {key: value for key, value in obj.read_typetree().items() if not key.startswith("m_")}
        print_fields(resolve(fields, by_path_id), "  ")
    if not found:
        print(f"No {args.script} in {args.bundle}.")


if __name__ == "__main__":
    main()
