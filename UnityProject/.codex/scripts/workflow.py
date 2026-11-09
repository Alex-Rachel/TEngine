#!/usr/bin/env python3
"""Project-local development/CI entrypoint. No automatic setup or publication."""

import argparse
from pathlib import Path
import sys

from workflow_lib import checks, domains, regression
from workflow_lib.core import (
    Context, Run, WorkflowError, approve, blocked, project_lock, read_json, redact, render_report,
)
from workflow_lib.unity import Unity, canonical_command


def parser():
    root = argparse.ArgumentParser(description=__doc__)
    root.add_argument("--project", type=Path, help="Explicit Unity project root (default: this script's project).")
    commands = root.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor", help="Read-only environment and project connection preflight.")
    commands.add_parser("check", help="Validate instructions, links, and deterministic unit tests.")
    verify = commands.add_parser("verify", help="Run the selected validation profile.")
    verify.add_argument("--profile", choices=("docs", "code", "unity", "full"), required=True)
    verify.add_argument("--test-filter", default="TEngine.Workflow", help="Explicit test selection, never all third-party tests.")
    verify.add_argument("--filter-type", choices=("assembly", "testName", "category"), default="assembly")
    verify.add_argument("--timeout", type=int, default=300)
    verify.add_argument("--asset", action="append", default=[], help="Explicit asset path to validate; repeat as needed.")
    approval = commands.add_parser("approve", help="Record a human confirmation after reviewing a preview.")
    approval.add_argument("--plan", required=True, type=Path)
    approval.add_argument("--by", required=True)
    approval.add_argument("--reason", required=True, help="Reference to the actual user/CI approval.")
    approval.add_argument("--high-risk", action="store_true")
    report = commands.add_parser("report", help="Render only already-recorded evidence.")
    report.add_argument("--run", required=True, type=Path)
    unity = commands.add_parser("unity", help="Audited project-bound Unity operations.")
    sub = unity.add_subparsers(dest="action", required=True)
    sub.add_parser("list")
    describe = sub.add_parser("describe", help="Read the exact registered command parameters and schema.")
    describe.add_argument("name", help="Canonical command name, e.g. 'scene get_hierarchy'.")
    for action in ("query", "preview"):
        command = sub.add_parser(action)
        command.add_argument("name")
        command.add_argument("--params-file", type=Path)
    apply = sub.add_parser("apply")
    apply.add_argument("--plan", type=Path, required=True)
    apply.add_argument("--approval", type=Path, required=True)
    runtime = sub.add_parser("runtime-status", help="Read a specifically located Development Player.")
    runtime.add_argument("--runtime-path", type=Path, required=True)
    luban = commands.add_parser("luban", help="Validate or explicitly authorize the existing client export.")
    sub = luban.add_subparsers(dest="action", required=True)
    sub.add_parser("validate")
    sub.add_parser("test", help="Run both real exporters against isolated regression fixtures.")
    preview = sub.add_parser("preview")
    preview.add_argument("--mode", choices=("lazyload", "standard"), default="lazyload")
    apply = sub.add_parser("apply")
    apply.add_argument("--plan", type=Path, required=True)
    apply.add_argument("--approval", type=Path, required=True)
    ui = commands.add_parser("ui", help="Bake local HTML; preview or apply a Unity prefab operation.")
    sub = ui.add_subparsers(dest="action", required=True)
    sub.add_parser("test", help="Run isolated browser image/font/viewport regressions.")
    bake = sub.add_parser("bake")
    bake.add_argument("input", type=Path)
    bake.add_argument("--width", type=int, default=1920)
    bake.add_argument("--height", type=int, default=1080)
    preview = sub.add_parser("preview")
    preview.add_argument("--json", type=Path, required=True)
    preview.add_argument("--config", required=True)
    preview.add_argument("--prefab", required=True)
    preview.add_argument("--legacy-text", action="store_true", help="Explicit UGUI Text, not an automatic TMP fallback.")
    apply = sub.add_parser("apply")
    apply.add_argument("--plan", type=Path, required=True)
    apply.add_argument("--approval", type=Path, required=True)
    return root


def dispatch(run, args):
    if args.command == "doctor":
        checks.doctor(run)
    elif args.command == "check":
        run.step("skill-and-document-structure", lambda: checks.structure(run.context))
        run.step("workflow-unit-tests", lambda: checks.unit_tests(run))
    elif args.command == "verify":
        if args.timeout < 1 or not args.test_filter.strip():
            raise WorkflowError("Timeout and test filter must be nonempty/positive.")
        checks.verify(run, args.profile, args.test_filter, args.filter_type, args.timeout, args.asset)
    elif args.command == "approve":
        run.step("approval-record", lambda: approve(run, args.plan, args.by, args.reason, args.high_risk))
    elif args.command == "unity":
        unity = Unity(run)
        with project_lock(run.context):
            if args.action == "list":
                run.step("registered-commands", lambda: unity.discover(require_ready=False))
            elif args.action == "describe":
                run.step("command-schema", lambda: unity.describe(args.name))
            elif args.action == "runtime-status":
                run.step("runtime-status", lambda: runtime_status(unity, args.runtime_path))
            elif args.action == "apply":
                run.step("unity-mutation", lambda: unity.apply(read_json(args.plan), args.approval))
            else:
                params = read_json(args.params_file) if args.params_file else {}
                if args.action == "preview" and canonical_command(args.name) == "global tengine_bake_ugui":
                    run.step("unity-preview", lambda: domains.ui_preview(
                        run, params["json_path"], params["config_path"], params["output_path"], params.get("legacy_text", False)))
                else:
                    method = unity.query if args.action == "query" else unity.preview
                    run.step("unity-" + args.action, lambda: method(args.name, params))
    elif args.command == "luban":
        with project_lock(run.context):
            if args.action == "validate":
                run.step("luban-validation", lambda: domains.luban_validate(run))
            elif args.action == "test":
                run.step("luban-regression", lambda: regression.luban(run))
            elif args.action == "preview":
                run.step("luban-preview", lambda: domains.luban_preview(run, args.mode))
            else:
                result = run.step("luban-export", lambda: domains.luban_apply(run, read_json(args.plan), args.approval))
                if result["status"] == "passed":
                    run.step("solution-build", lambda: checks.build(run))
                    run.step("editor-compilation", lambda: Unity(run).recompile(300))
    elif args.command == "ui":
        if args.action == "test":
            run.step("browser-regression", lambda: regression.browser(run))
        elif args.action == "bake":
            run.step("browser-bake", lambda: domains.ui_bake(run, args.input, args.width, args.height))
        else:
            with project_lock(run.context):
                if args.action == "preview":
                    run.step("prefab-preview", lambda: domains.ui_preview(run, args.json, args.config, args.prefab, args.legacy_text))
                else:
                    run.step("prefab-bake", lambda: Unity(run).apply(read_json(args.plan), args.approval))


def runtime_status(unity, path):
    from workflow_lib.unity import decode_response
    path = path.resolve()
    value = decode_response(unity.raw(["command", "--runtime-path", str(path), "runtime", "status"]))
    if not isinstance(value, dict):
        blocked("Runtime status has no verifiable Player identity.")
    version = value.get("unityVersion", value.get("UnityVersion"))
    platform = value.get("platform", value.get("Platform"))
    playing = value.get("isPlaying", value.get("IsPlaying"))
    if not version or not platform or not isinstance(playing, bool):
        blocked("Runtime result does not match the Player status contract.")
    return {"selectionSource": "CLI --runtime-path", "requestedRuntimePath": str(path),
            "runtime": value}


def main(argv=None):
    args = parser().parse_args(argv)
    if sys.version_info < (3, 11):
        print("Python 3.11 or newer is required.", file=sys.stderr)
        return 2
    if args.command == "report":
        try:
            status = render_report(args.run)
            print(f"{status}: {args.run / 'report.md'}")
            return {"passed": 0, "failed": 1, "blocked": 2}[status]
        except (OSError, WorkflowError, KeyError) as exc:
            print(str(exc), file=sys.stderr)
            return 2
    try:
        context = Context(args.project)
    except WorkflowError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    run = Run(context, " ".join(argv if argv is not None else sys.argv[1:]))
    try:
        dispatch(run, args)
    except WorkflowError as exc:
        run.data["steps"].append({"name": "dispatch", "status": exc.status, "message": str(exc), "required": True})
    except KeyboardInterrupt:
        run.data["steps"].append({"name": "interrupted", "status": "blocked", "message": "Inspect in-flight operation before retrying.", "required": True})
    except Exception as exc:
        run.data["steps"].append({"name": "unexpected-error", "status": "failed", "message": f"{type(exc).__name__}: {exc}", "required": True})
    code = run.finish()
    print(f"{run.data['status']}: {run.path / 'report.md'}")
    for step in redact(run.data)["steps"]:
        print(f"  {step['name']}: {step['status']}")
        if step.get("message"):
            print("    " + step["message"])
        elif step.get("result") and args.command not in ("verify", "check", "doctor"):
            print("    " + str(step["result"]))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
