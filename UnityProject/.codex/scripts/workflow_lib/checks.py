"""Offline structure checks and verification profiles."""

import importlib.util
from pathlib import Path
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from urllib.parse import unquote, urlsplit

from .core import WorkflowError, blocked, project_lock, require_success
from .unity import Unity


SKILLS = ("tengine-dev", "unity-cli", "luban-dev", "html-to-ugui")


def dependencies(*names):
    missing = [name for name in names if importlib.util.find_spec(name) is None]
    if missing:
        blocked("Missing optional dependencies: " + ", ".join(missing) + ". Install explicitly from the documented requirements.")


def structure(context):
    dependencies("yaml", "markdown_it")
    import yaml
    from markdown_it import MarkdownIt

    root = context.project / ".codex"
    errors = []
    discovered = sorted(path.parent.name for path in (root / "skills").glob("*/SKILL.md"))
    if discovered != sorted(SKILLS):
        errors.append(f"Expected exactly the maintained skills {SKILLS}, got {discovered}")
    for name in SKILLS:
        directory = root / "skills" / name
        text = (directory / "SKILL.md").read_text(encoding="utf-8-sig")
        sections = text.split("---", 2)
        try:
            metadata = yaml.safe_load(sections[1]) if len(sections) == 3 and not sections[0].strip() else None
            if not isinstance(metadata, dict) or metadata.get("name") != name or not metadata.get("description"):
                errors.append(f"{name}: missing or invalid frontmatter")
            ui = yaml.safe_load((directory / "agents" / "openai.yaml").read_text(encoding="utf-8"))
            interface = ui.get("interface", {})
            if "$" + name not in interface.get("default_prompt", ""):
                errors.append(f"{name}: default_prompt must name the skill")
            if not 25 <= len(interface.get("short_description", "")) <= 64:
                errors.append(f"{name}: short_description must be 25-64 characters")
        except (ValueError, OSError, yaml.YAMLError) as exc:
            errors.append(f"{name}: {exc}")
    docs = list((root / "skills").rglob("*.md")) + [
        context.project / "AGENTS.md", context.repo / "AGENTS.md",
        context.repo / "Books" / "AI-Development-Workflow.md",
    ]
    parser = MarkdownIt()
    banned = re.compile(r"/opsx:|\.claude/|mcp-tools\.md|mcp-visual\.md|manage_script|manage_scene|"
                        r"mcp__unity|unity-mcp-orchestrator|openspec/(?:specs|changes)|openspec (?:init|apply)")
    machine_path = re.compile(r"(?:[A-Za-z]:[\\/](?:Users|WorkSpace)[\\/]|/Users/[^/]+/)")
    docs.append(context.repo / "README.md")
    for path in docs:
        if not path.is_file():
            errors.append(f"Missing document: {path}")
            continue
        text = path.read_text(encoding="utf-8-sig")
        if path.name == "README.md":
            # Only this task's workflow section; unrelated legacy product links are out of scope.
            match = re.search(r"^## .*AI.*(?:\n.*)*", text, re.M)
            text = match[0].split("\n## ", 1)[0] if match else ""
            if not text or "AGENTS.md" not in text:
                errors.append("README workflow entry is missing")
        if banned.search(text):
            errors.append(f"Retired workflow dependency in {path}")
        if machine_path.search(text) or re.search(r"\bsk-[A-Za-z0-9_-]{20,}", text):
            errors.append(f"Machine-specific path or credential in {path}")
        for token in parser.parse(text):
            for child in token.children or []:
                if child.type not in ("link_open", "image"):
                    continue
                target = child.attrGet("href" if child.type == "link_open" else "src") or ""
                parts = urlsplit(target)
                if parts.scheme or target.startswith("#") or not parts.path:
                    continue
                destination = (path.parent / unquote(parts.path)).resolve()
                if not destination.exists():
                    errors.append(f"Broken reference: {path} -> {target}")
    for obsolete in ("AGENT.md", "openspec"):
        target = context.project / obsolete
        if target.is_file() or (target.is_dir() and any(path.is_file() for path in target.rglob("*"))):
            errors.append(f"Retired entry still exists: {obsolete}")
    # Behavior scenarios are evidence requirements, not regex-based model grading.
    from .core import read_json
    cases = read_json(root / "evals" / "scenarios.json")
    ids = [case["id"] for case in cases["cases"]]
    if len(ids) != len(set(ids)):
        errors.append("Duplicate behavior scenario ids")
    for case in cases["cases"]:
        if not case.get("checks") or any(skill not in SKILLS for skill in case["skills"]):
            errors.append(f"Invalid behavior scenario: {case['id']}")
    if errors:
        raise WorkflowError("\n".join(errors))
    return {"skills": discovered, "documents": len(docs), "behaviorScenarios": len(ids)}

def solution_inputs(context):
    projects = re.findall(r'Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"',
                          context.solution.read_text(encoding="utf-8-sig"))
    if not projects:
        blocked("Solution contains no C# projects.")
    sources = set()
    for name in projects:
        path = context.solution.parent / name.replace("\\", "/")
        if not path.is_file():
            blocked(f"Generated project missing: {name}")
        for item in ET.parse(path).getroot().iter():
            if item.tag.rsplit("}", 1)[-1] == "Compile" and item.get("Include"):
                sources.add((path.parent / item.get("Include").replace("\\", "/")).resolve())
    missing = [str(path.relative_to(context.project)) for path in (context.project / "Assets").rglob("*.cs")
               if path.resolve() not in sources]
    if missing:
        blocked("Sources absent from the generated solution; regenerate in Unity or obtain explicit platform "
                "validation for excluded sources: " + ", ".join(missing[:30]))
    return {"projects": len(projects), "sources": len(sources)}


def unit_tests(run):
    result = run.execute(
        [sys.executable, "-m", "unittest", "discover", "-s", run.context.project / ".codex" / "tests",
         "-p", "test_*.py", "-v"], timeout=180,
    )
    require_success(result, "Workflow unit tests")
    output = result.stdout + result.stderr
    match = re.search(r"Ran (\d+) tests?", output)
    if not match or int(match[1]) == 0 or re.search(r"OK \(skipped=", output):
        raise WorkflowError("Unit suite ran no tests or skipped coverage; inspect its output.")
    return {"tests": int(match[1]), "runner": "unittest"}


def build(run):
    if not shutil.which("dotnet"):
        blocked(".NET SDK is missing.")
    if not run.context.solution.is_file():
        blocked("UnityProject.sln is missing. Regenerate project files in this Unity Editor; do not synthesize a solution.")
    inputs = solution_inputs(run.context)
    result = run.execute(["dotnet", "build", run.context.solution, "--nologo", "-v:q", "-clp:ErrorsOnly"], timeout=300)
    require_success(result, "Full solution build")
    return {"solution": str(run.context.solution), "inputs": inputs, "output": result.stdout}


def doctor(run):
    context = run.context

    def tools():
        from .core import read_json
        package = read_json(context.project / "Packages" / "com.unity.pipeline" / "package.json")
        version = (context.project / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8")
        if not context.cli.is_file() or not context.solution.is_file():
            blocked("CLI or generated solution missing; see project instructions.")
        missing = [name for name in ("dotnet", "git") if not shutil.which(name)]
        if missing:
            blocked("Required tools missing: " + ", ".join(missing))
        result = run.execute([context.cli, "--version"], timeout=15)
        require_success(result, "CLI version")
        dotnet = require_success(run.execute(["dotnet", "--version"], timeout=15), ".NET version").strip()
        return {"python": sys.version, "dotnet": shutil.which("dotnet"), "git": shutil.which("git"),
                "unity": version.strip(), "pipeline": package["version"], "cli": result.stdout.strip(), "dotnetVersion": dotnet,
                "optionalDependencies": {name: importlib.util.find_spec(name) is not None
                                         for name in ("yaml", "markdown_it", "openpyxl", "playwright")}}

    run.step("environment", tools)
    run.step("editor-connection", Unity(run).ready)


def verify(run, profile, query, filter_type, timeout, assets):
    if profile in ("docs", "code", "full"):
        run.step("skill-and-document-structure", lambda: structure(run.context))
        run.step("workflow-unit-tests", lambda: unit_tests(run))
    if profile in ("code", "full"):
        run.step("solution-build", lambda: build(run))
    if profile == "full":
        from . import domains, regression
        run.step("browser-regression", lambda: regression.browser(run))
        run.step("luban-regression", lambda: regression.luban(run))
        run.step("project-config-validation", lambda: domains.luban_validate(run))
    if profile in ("unity", "full"):
        with project_lock(run.context):
            unity = Unity(run)
            connection = run.step("editor-capabilities", unity.discover)
            if connection["status"] != "passed":
                for name in ("editor-compilation", "editmode-tests", "playmode-tests"):
                    run.skip(name, "Requires the project Editor connection.", required=True)
                if assets:
                    run.skip("asset-validation", "Requires the project Editor connection.", required=True)
                return
            compilation = run.step("editor-compilation", lambda: unity.recompile(timeout))
            if compilation["status"] != "passed":
                for name in ("editmode-tests", "playmode-tests"):
                    run.skip(name, "Editor compilation did not pass.", required=True)
                if assets:
                    run.skip("asset-validation", "Editor compilation did not pass.", required=True)
                return
            if assets:
                run.step("asset-validation", lambda: unity.query("tengine_validate_assets", {"paths": assets}))
                if any(Path(asset).suffix.lower() == ".unity" for asset in assets):
                    run.skip("scene-contents-validation",
                             "tengine_validate_assets checks scene assets/importers, but does not inspect scene "
                             "contents. Scene serialization and missing-reference acceptance need separate evidence.",
                             required=True)
            else:
                run.skip("asset-validation", "No asset targets supplied; no claim about uninspected assets.")
            for mode in ("editor", "playmode"):
                run.step(mode + "-tests", lambda mode=mode: unity.tests(mode, query, filter_type, timeout))
