"""Real tool regressions, isolated under the evidence directory, never production outputs."""

import base64
import os
from pathlib import Path
import shutil

from .core import WorkflowError, blocked, file_hash, require_success, write_json
from .domains import dependencies, export_script, load_module, snapshot_luban_tool


def browser(run):
    dependencies("playwright")
    module = load_module("workflow_html_baker", run.context.project / ".codex/skills/html-to-ugui/scripts/bake_html_to_json.py")
    root = run.path / "browser fixture \u4e2d\u6587"
    root.mkdir()
    (root / "pixel.png").write_bytes(base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="))
    fonts = list((run.context.project / "Assets").rglob("*.ttf"))
    if not fonts:
        font_root = Path(os.environ["WINDIR"]) / "Fonts" if os.name == "nt" and "WINDIR" in os.environ else Path("/usr/share/fonts")
        fonts = sorted(path for path in font_root.rglob("*") if path.suffix.lower() in (".ttf", ".otf"))
    if not fonts:
        blocked("No local font available for the browser regression.")
    shutil.copyfile(fonts[0], root / "fixture.ttf")
    markup = """<style>@font-face{font-family:Fixture;src:url('fixture.ttf')}</style>
<div data-u-type="div" data-u-name="Root" data-u-layout="stretch"
style="position:relative;width:100%;height:100%;background:#e0e4e8">
 <div data-u-type="image" data-u-name="Icon" data-u-src="pixel.png"
 style="width:96px;height:96px;background-color:#23846e"></div>
 <span data-u-type="text" data-u-name="Label"
 style="display:block;width:200px;height:40px;font-family:Fixture">Workflow</span>
 <input data-u-type="slider" data-u-name="Volume" data-u-value="0"
 style="width:180px;height:30px" type="range">
 <div style="position:absolute;top:180px;left:20px;width:200px;height:100px">
   <div data-u-type="div" data-u-name="A" style="width:50px;height:30px"></div>
   <div data-u-type="div" data-u-name="B" style="width:50px;height:30px"></div>
 </div>
</div>"""
    source = root / "input.html"
    source.write_text(markup, encoding="utf-8")
    cases = []
    for name, width, height in (("pc", 1920, 1080), ("mobile", 1080, 1920), ("pad", 2048, 1536)):
        screenshot = root / (name + ".png")
        result = module.bake_html_to_json(markup, width, height, str(source), str(screenshot))
        if result["width"] != width or result["height"] != height or result["children"][2]["value"] != 0:
            raise WorkflowError("Browser dimensions or zero-value roundtrip failed.")
        if not screenshot.is_file() or screenshot.stat().st_size < 1000:
            raise WorkflowError("Browser screenshot is empty.")
        repeated = module.bake_html_to_json(markup, width, height, str(source))
        if result != repeated:
            raise WorkflowError("Repeated browser bake is nondeterministic.")
        write_json(root / (name + ".json"), result)
        cases.append({"viewport": name, "size": [width, height], "screenshot": str(screenshot)})
    for bad_markup in (markup.replace("pixel.png", "missing.png"), markup.replace("fixture.ttf", "missing.ttf")):
        try:
            module.bake_html_to_json(bad_markup, source_path=str(source))
        except ValueError:
            continue
        raise WorkflowError("Missing required browser asset was accepted.")
    return {"viewports": cases, "font": fonts[0].name, "missingImageAndFontRejected": True,
            "scope": "Browser layout only; Unity/device visuals require separate evidence."}


def luban(run):
    dll = run.context.repo / "Tools/Luban/Luban.dll"
    if not dll.is_file() or not shutil.which("dotnet"):
        blocked("Luban/.NET missing; regression never installs or rebuilds tools.")
    fixture = run.context.project / ".codex/skills/luban-dev/examples/regression"
    results = []
    for mode in ("standard", "lazyload"):
        repo = run.path / ("luban fixture \u4e2d\u6587 " + mode)
        config = repo / "Configs/GameConfig"
        shutil.copytree(fixture, config)
        shutil.copytree(run.context.repo / "Configs/GameConfig/CustomTemplate", config / "CustomTemplate")
        script = export_script(run.context, mode)
        shutil.copyfile(script, config / script.name)
        output = repo / "UnityProject/Assets"
        (output / "GameScripts/HotFix/GameProto/GameConfig").mkdir(parents=True)
        (output / "AssetRaw/Configs/bytes").mkdir(parents=True)
        snapshot_luban_tool(run, repo)
        env = {**os.environ, "AI_MODE": "1", "LUBAN_DLL": str(dll)}
        args = ["cmd.exe", "/d", "/c", script.name] if os.name == "nt" else ["bash", script.name]
        require_success(run.execute(args, cwd=config, env=env, timeout=180), f"Isolated {mode} export")
        binaries = sorted((output / "AssetRaw/Configs/bytes").glob("*.bytes"))
        names = {path.stem.lower() for path in binaries}
        # Both current scripts select bin. Partition tags in the fixture do not
        # establish a sharded exporter or partition-aware runtime API.
        expected = {"tbnormal", "tbrange", "tbcount", "tbfield"}
        if names != expected or any(path.stat().st_size == 0 for path in binaries):
            raise WorkflowError(f"{mode} unexpected binary outputs: {sorted(names)}")
        code = output / "GameScripts/HotFix/GameProto"
        for filename in ("ConfigSystem.cs", "ExternalTypeUtil.cs"):
            if file_hash(code / filename) != file_hash(config / "CustomTemplate" / filename):
                raise WorkflowError("Template copy mismatch: " + filename)
        tables = code / "GameConfig/Tables.cs"
        if not tables.is_file():
            raise WorkflowError("Tables.cs missing.")
        if mode == "lazyload" and "SetDefaultLoader" not in tables.read_text(encoding="utf-8-sig"):
            raise WorkflowError("Table-lazy-loading API missing from generated Tables.")
        results.append({"mode": mode, "binaries": sorted(names), "output": str(output),
                        "scope": "Standard binary export and table-level lazy loading; sharding is not implemented."})
    return results
