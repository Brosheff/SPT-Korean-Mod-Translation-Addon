"""GPT-only Manimal Terminal 0.2.2 translation source coverage audit.

Requires the user's extracted original ManimalTerminal source. Not a game test,
not required for the normal CMD build, and never modifies original mod files.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[2]


def load(path: pathlib.Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def nested_strings(value, exclusions=frozenset()):
    if isinstance(value, dict):
        for k, v in value.items():
            if k not in exclusions:
                yield from nested_strings(v, exclusions)
    elif isinstance(value, list):
        for v in value:
            yield from nested_strings(v, exclusions)
    elif isinstance(value, str):
        yield value


def audit(original: pathlib.Path):
    original = original.resolve()
    if (original / "ManimalTerminal-master" / "terminal-client").exists():
        original /= "ManimalTerminal-master"
    assert (original / "terminal-client" / "Plugin.cs").is_file(), "Original Manimal source directory required"
    profile = load(ROOT / "translations" / "Manimal Terminal.json")
    assert (profile["mod_id"], profile["mod_version"], profile["target_spt"]) == (
        "com.manimal.terminal", "0.2.2", "4.1.6"
    )
    ch = profile["channels"]
    assert set(ch) == {"locale", "small_ui", "notifications"}
    keyed = {r["key"]: r for r in ch["locale"]}
    assert len(keyed) == len(ch["locale"]), "duplicate locale key"
    ui = {r["source"] for r in ch["small_ui"]}
    notif = {r["source"] for r in ch["notifications"]}
    assert len(ui) == len(ch["small_ui"]), "duplicate UI source"
    assert len(notif) == len(ch["notifications"]), "duplicate notification source"

    providers = {}
    def add(category, key, english):
        assert key not in providers, f"repeated key: {key}"
        providers[key] = (category, english)
    for category, f in (
        ("quest", "terminal-server/db/CustomQuests/prapor/Locales/en.json"),
        ("achievement", "terminal-server/db/CustomAchievements/Locales/en.json"),
        ("container", "terminal-server/db/containerLocales.json"),
    ):
        for k, v in load(original / f).items():
            add(category, k, v)
    dialogues = load(original / "terminal-server/db/CustomDialogues/TheTicketLightkeeperDialogue.json")
    assert len(dialogues) == 2
    for d in dialogues:
        for k, v in d["localization"]["en"].items():
            add("dialogue", k, v)
    server_mod = (original / "terminal-server/TerminalMod.cs").read_text(encoding="utf-8-sig")
    banner = re.search(r'TerminalBannerId\s*=\s*"([a-z0-9]+)"', server_mod)
    assert banner, "Cannot resolve banner ID"
    map_ids = ["5704e5a4d2720bb45b8b4567", "65cc8f81a9aac3e77d0cfd3e", "6925a2c38bdebd9e2302692e", banner.group(1)]
    blurb = re.search(r'private const string TerminalBlurb\s*=\s*((?:\s*"[^"]*"\s*\+?)+);', server_mod)
    assert blurb, "Cannot resolve map blurb literal"
    text = "".join(re.findall(r'"([^"]*)"', blurb.group(1)))
    for id_ in map_ids:
        add("map", id_ + " Name", "Terminal")
        add("map", id_ + " Description", text)
    cabinet = "68a4c0ffee0000000000cab1"
    for k, v in (
        ("Name", "Equipment Cabinet"),
        ("ShortName", "EqCabinet"),
        ("Description", "A tall port-authority equipment cabinet. Whatever the terminal crews locked away is still inside."),
    ):
        add("cabinet", cabinet + " " + k, v)

    assert len(providers) == len(keyed), f"provider/profile key totals differ: {len(providers)}/{len(keyed)}"
    for key, (category, english) in providers.items():
        assert key in keyed, f"Missing {category} key: {key}"
        row = keyed[key]
        assert row["source"] == english, f"Source mismatch for {category} key: {key}"
        assert row.get("translation"), f"Empty translation for {category} key: {key}"
    assert set(providers) == set(keyed), "Orphaned locale translations"
    cats = Counter(category for category, _ in providers.values())
    assert cats == {"quest":101,"achievement":2,"container":18,"dialogue":39,"map":8,"cabinet":3}, cats

    timings = load(original / "terminal-client/plugin-data/terminal_subtitle_timings.json")
    timed = {entry["text"] for scene in timings["cutscenes"].values() for entry in scene}
    assert timed <= ui, f"Missing cutscene lines: {timed - ui}"
    standalone = re.search(r'TerminalSubtitles.ShowStandalone\(\s*"([^"]+)"',
        (original / "terminal-client/TerminalFinalExit.cs").read_text(encoding="utf-8-sig"))
    assert standalone and standalone.group(1) in ui, "Missing standalone loudspeaker subtitle"
    epilogue = load(original / "terminal-client/plugin-data/epilogue/terminal_epilogue.json")
    epicopy = set(nested_strings(epilogue, {"_comment", "kind", "type", "id", "image", "icon", "sprite", "path", "music", "background", "medal", "achievement_id", "action"}))
    assert epicopy <= ui, f"Missing epilogue copy: {epicopy - ui}"
    config = load(ROOT / "config-ui/com.manimal.terminal.json")
    assert config["plugin_guid"] == "com.manimal.terminal"
    config_keys = [(r["section"], r["key"]) for r in config["entries"]]
    original_binds = re.findall(r'Config.Bind\("([^"]+)",\s*"([^"]+)"',
        (original / "terminal-client/Plugin.cs").read_text(encoding="utf-8-sig"))
    assert len(config_keys) == len(set(config_keys)) and set(config_keys) == set(original_binds), "F12 config binding mismatch"
    source_csharp = "\n".join(p.read_text(encoding="utf-8-sig") for p in
        (original / "terminal-client").glob("*.cs"))
    for text in notif:
        assert text in source_csharp, f"Unused or changed notification: {text}"
    assert '"com.manimal.terminal"' in (ROOT / "src/DisplayHooks.cs").read_text(encoding="utf-8-sig")
    assert "ManimalTerminalUiHooks.Enable" in (ROOT / "src/SmallUiLiteralHooks.cs").read_text(encoding="utf-8-sig")
    server = (ROOT / "server-src/SPT_Mod_Korean_Server/ManimalTerminalLocales.cs").read_text(encoding="utf-8-sig")
    assert "localeTable.Global" in server and "templateTable.Dialogue.Elements" in server
    assert "string.Equals(current, row.Value.Source, StringComparison.Ordinal)" in server
    assert "OnLoadOrder.Preload + 90001" in server
    assert "languages[culture] = localized" in server
    # Regression expectations, independently derived from the source contract:
    for key, (_, english) in providers.items():
        assert keyed[key]["source"] == english
        assert keyed[key]["translation"] != ""
    print("OK: original Manimal 0.2.2 provider key/source matches, no orphaned translations")
    print("  "+", ".join(f"{k}={v}" for k,v in sorted(cats.items())))
    print(f"OK: all {len(timed)} distinct timed subtitle sources and standalone loudspeaker covered")
    print(f"OK: all {len(epicopy)} distinct epilogue config strings covered")
    print(f"OK: all {len(config_keys)} F12 bindings covered; {len(notif)} notifications sourced")
    print("OK: server global/dialogue channels registered in source; actual DLL compilation/gameplay UNVERIFIED")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--original", required=True, type=pathlib.Path, help="ManimalTerminal-master extracted source")
    args = parser.parse_args()
    audit(args.original)
