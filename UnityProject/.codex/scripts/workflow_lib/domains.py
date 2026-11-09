"""Scoped adapters for existing Luban and UI tools."""

import contextlib
import importlib.util
import io
import os
from pathlib import Path
import shutil
import sys

from .checks import build, dependencies
from .core import WorkflowError, blocked, consume_approval, file_hash, make_plan, require_success, within, validate_plan
from .unity import Unity


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def luban_validate(run, mode="lazyload"):
    dependencies("openpyxl")
    source = run.context.repo / "Configs" / "GameConfig" / "Datas"
    if not (source / "__tables__.xlsx").is_file():
        blocked(f"Missing table registry: {source / '__tables__.xlsx'}")
    module = load_module("workflow_luban_helper", run.context.project / ".codex/skills/luban-dev/scripts/luban_helper.py")
    captured = io.StringIO()
    with contextlib.redirect_stdout(captured):
        helper = module.LubanConfigHelper(str(source), str(run.path / "luban-cache"))
        result = helper.validate_all()
    if result["total"] == 0:
        blocked("No registered Luban tables; cannot validate an empty export.")
    # The legacy helper cannot fully parse multi-row Bean headers. The current generator is authoritative.
    script = export_script(run.context, mode)
    dll = run.context.repo / "Tools/Luban/Luban.dll"
    if not dll.is_file() or not script.is_file() or not shutil.which("dotnet"):
        blocked("Project Luban export script/tool/.NET missing; validation never installs or rebuilds tools.")
    env = {**os.environ, "AI_MODE": "1", "LUBAN_DLL": str(dll)}
    outputs = fresh_luban_export(run, script, env)
    return {"mode": mode, "generator": "passed", "outputs": outputs, "helper": result,
            "helperScope": "Advisory only; nested Bean headers and full type/reference semantics are checked by Luban."}


def export_script(context, mode):
    if mode not in ("lazyload", "standard"):
        raise WorkflowError("Unsupported export mode.")
    base = "gen_code_bin_to_project" + ("_lazyload" if mode == "lazyload" else "")
    return context.repo / "Configs" / "GameConfig" / (base + (".bat" if os.name == "nt" else ".sh"))


def luban_preview(run, mode):
    validation = luban_validate(run, mode)
    script = export_script(run.context, mode)
    if not script.is_file():
        blocked(f"Export script missing: {script}")
    return make_plan(
        run, "luban", "export", {"mode": mode}, high_risk=True,
        inputs=[script, *(run.context.repo / "Tools/Luban").glob("*.dll")],
        preview={"script": str(script), "mode": mode, "validation": validation,
                 "writes": ["Assets/GameScripts/HotFix/GameProto", "Assets/AssetRaw/Configs/bytes"],
                 "notes": "Uses the existing script and its actual exporter/template selection; lazyload does not imply sharding."},
    )

def export_manifest(assets):
    files = list((assets / "GameScripts/HotFix/GameProto/GameConfig").rglob("*.cs"))
    binaries = list((assets / "AssetRaw/Configs/bytes").rglob("*.bytes"))
    if not files or not binaries or any(path.stat().st_size == 0 for path in files + binaries):
        raise WorkflowError("Export produced missing/empty code or binary data.")
    files += [assets / "GameScripts/HotFix/GameProto" / name for name in ("ConfigSystem.cs", "ExternalTypeUtil.cs")]
    return {str(path.relative_to(assets)).replace("\\", "/"): file_hash(path) for path in files + binaries}


def validate_export(actual, expected):
    if actual != expected:
        missing = sorted(expected.keys() - actual.keys())
        stale = sorted(actual.keys() - expected.keys())
        different = sorted(key for key in actual.keys() & expected.keys() if actual[key] != expected[key])
        raise WorkflowError(f"Export differs from fresh isolated output. Missing: {missing}; stale: {stale}; changed: {different}")
    return {"verifiedFiles": len(expected)}


def snapshot_luban_tool(run, isolated):
    source = run.context.repo / "Tools/Luban"
    if not (source / "Luban.dll").is_file():
        blocked("Project Luban tool is missing; isolated validation never installs or rebuilds it.")
    # Export scripts resolve Tools/Luban from their repository, overriding LUBAN_DLL.
    # Copy the installed runtime, dependencies and templates into the isolated layout.
    destination = Path(isolated) / "Tools/Luban"
    shutil.copytree(source, destination)
    return destination


def fresh_luban_export(run, script, env):
    # An empty output tree proves freshness even when Luban intentionally avoids rewriting unchanged files.
    isolated = run.path / "fresh export"
    config = isolated / "Configs/GameConfig"
    shutil.copytree(script.parent, config, ignore=shutil.ignore_patterns(".git", "__pycache__", ".luban-cache"))
    assets = isolated / "UnityProject/Assets"
    (assets / "GameScripts/HotFix/GameProto/GameConfig").mkdir(parents=True)
    (assets / "AssetRaw/Configs/bytes").mkdir(parents=True)
    snapshot_luban_tool(run, isolated)
    args = ["cmd.exe", "/d", "/c", script.name] if os.name == "nt" else ["bash", script.name]
    require_success(run.execute(args, cwd=config, timeout=300, env=env), "Fresh isolated Luban export")
    return export_manifest(assets)


def luban_apply(run, plan, approval_file):
    if plan.get("kind") != "luban" or plan.get("command") != "export":
        blocked("Not a supported Luban export plan.")
    script = export_script(run.context, plan["parameters"]["mode"])
    if not (run.context.repo / "Tools/Luban/Luban.dll").is_file():
        blocked("Project Luban tool is missing; build/install it explicitly before export.")
    validate_plan(run.context, plan)
    validation = luban_validate(run, plan["parameters"]["mode"])
    env = os.environ.copy()
    env["AI_MODE"] = "1"
    env["PYTHONUTF8"] = "1"
    env["LUBAN_DLL"] = str(run.context.repo / "Tools/Luban/Luban.dll")
    expected = validation["outputs"]
    consume_approval(run, plan, approval_file)
    # Fixed, repository-owned batch filename, never an interpolated user shell program.
    args = ["cmd.exe", "/d", "/c", script.name] if os.name == "nt" else ["bash", str(script)]
    require_success(run.execute(args, cwd=script.parent, timeout=300, env=env, mutating=True), "Luban export")
    code = run.context.project / "Assets/GameScripts/HotFix/GameProto/GameConfig"
    data = run.context.project / "Assets/AssetRaw/Configs/bytes"
    cs_files, bytes_files = list(code.rglob("*.cs")), list(data.rglob("*.bytes"))
    verification = validate_export(export_manifest(run.context.project / "Assets"), expected)
    template = script.parent / "CustomTemplate/ConfigSystem.cs"
    generated = code.parent / "ConfigSystem.cs"
    if not generated.is_file() or file_hash(template) != file_hash(generated):
        raise WorkflowError("ConfigSystem copy did not match the source template.")
    return {"codeFiles": len(cs_files), "binaryFiles": len(bytes_files), "mode": plan["parameters"]["mode"],
            "freshness": verification}


def ui_bake(run, source, width, height):
    dependencies("playwright")
    source = Path(source).resolve()
    if not source.is_file():
        blocked(f"Missing HTML input: {source}")
    script = run.context.project / ".codex/skills/html-to-ugui/scripts/bake_html_to_json.py"
    output = run.path / "ui.json"
    args = [sys.executable, script, source, "-o", output, "-w", str(width), "-H", str(height),
            "--screenshot", run.path / "browser.png"]
    require_success(run.execute(args, timeout=90), "Browser UI bake")
    return {"json": str(output), "screenshot": str(run.path / "browser.png")}


def ui_preview(run, json_path, config, prefab, legacy_text=False):
    json_path = Path(json_path).resolve()
    config_file = within(run.context.project / config, run.context.project / "Assets")
    within(run.context.project / prefab, run.context.project / "Assets")
    # C# preflight resolves every image; bind external/local input bytes into the approval.
    unity = Unity(run)
    parameters = {"json_path": str(json_path), "config_path": config, "output_path": prefab, "legacy_text": legacy_text}
    unity.ready()
    preview = unity.call("tengine_bake_ugui", {**parameters, "dry_run": True})
    if not isinstance(preview, dict) or not isinstance(preview.get("inputFiles"), list):
        blocked("Baker preview did not provide its resolved input list.")
    inputs = {str(json_path), str(config_file), *preview["inputFiles"]}
    return make_plan(run, "unity", "tengine_bake_ugui", parameters, high_risk=True, inputs=inputs, preview=preview)
