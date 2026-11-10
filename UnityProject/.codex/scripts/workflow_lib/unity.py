"""Known command policies and Unity Pipeline response validation."""

from __future__ import annotations

import json
from pathlib import Path
import re
import time

from .core import WorkflowError, blocked, consume_approval, digest, make_plan


PROJECT_ALIASES = {
    "tengine_bake_ugui": "global tengine_bake_ugui",
    "tengine_validate_assets": "global tengine_validate_assets",
}


def canonical_command(command):
    return PROJECT_ALIASES.get(command, command)


READ_ONLY = frozenset({
    "editor status", "scene list_open", "scene get_hierarchy", "build get_settings",
    "build.target list", "build list_profiles", "build list_platforms", "test list",
    "test status", "script recompile_status", "settings.player get", "settings.audio get",
    "settings.graphics get", "settings.input get", "settings.physics get", "settings.quality get",
    "settings.tags_layers get", "settings.time get", "console get", "console status",
    "diagnostics get_performance", "authoring get_root", "gameobject get_selection",
    "asset find", "gameobject find", "component get_properties", "component get_fields",
    "asset get_import_settings", "material get_properties", "material list_shaders",
    "material get_shader_properties", "animation.clip get", "animation.animator get_controller",
    "animation.timeline get", "lighting get_settings", "navmesh get_settings", "build status",
    "build.target switch_status", "package list", "package status", "lighting bake_status",
    "navmesh bake_status", "occlusion bake_status", "pipeline progress", "script.methodreload status",
    "diagnostics profiler_record_status", "diagnostics memory_snapshot_status",
    "global tengine_validate_assets",
})

# Explicit catalog, not a prefix heuristic. Only implemented return/poll contracts are admitted.
MUTATIONS = {
    "global tengine_bake_ugui": {"dry_run": "dry_run", "confirm": "confirm", "poll": None},
    "asset delete": {"dry_run": "dry-run", "confirm": "confirm", "poll": None},
    "settings.player set": {"dry_run": "dry-run", "confirm": "confirm", "poll": None},
    "build set_settings": {"dry_run": "dry-run", "confirm": "confirm", "poll": None},
    "build run": {"dry_run": "dry-run", "confirm": "confirm", "poll": "build status"},
}

RESERVED = {"project-path", "project_path", "runtime", "runtime-path", "runtime_path",
            "format", "json", "confirm", "dry_run", "dry-run", "non-interactive",
            "non_interactive", "timeout", "fields", "verbose", "omit-nulls", "omit_nulls",
            "port", "host", "token", "no-log-proxy", "no_log_proxy"}

STATUS_QUERIES = frozenset({
    "editor status", "test status", "script recompile_status", "build status",
    "build.target switch_status", "package status", "lighting bake_status", "navmesh bake_status",
    "occlusion bake_status", "console get", "console status", "pipeline progress",
    "script.methodreload status", "diagnostics profiler_record_status", "diagnostics memory_snapshot_status",
})


def validate_parameters(parameters):
    if not isinstance(parameters, dict):
        raise WorkflowError("Parameters must be a JSON object.")
    for key in parameters:
        if (not isinstance(key, str) or key.lower() in RESERVED
                or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]*", key)):
            raise WorkflowError(f"Reserved or invalid parameter: {key}")
    return parameters


def validate_mutation_parameters(command, parameters):
    validate_parameters(parameters)
    if command == "build run":
        for key in ("target", "output-path"):
            if not isinstance(parameters.get(key), str) or not parameters[key].strip():
                raise WorkflowError(f"Build preview requires an explicit nonempty {key}.")
        scenes = parameters.get("scenes")
        if (not isinstance(scenes, list) or not scenes
                or any(not isinstance(scene, str) or not scene.strip() for scene in scenes)):
            raise WorkflowError("Build preview requires explicit scenes as a nonempty array of scene paths.")
    return parameters


def validate_mutation_preview(command, value):
    if command == "build run" and (
            not isinstance(value, dict) or value.get("status") != "dry_run"
            or value.get("valid") is not True or value.get("validationErrors") != []):
        raise WorkflowError("Build dry-run validation did not pass: " + json.dumps(value, ensure_ascii=False))
    return value


def decode_response(text):
    """Read both protocol results/NDJSON and HTTP/CLI response envelopes."""
    if not isinstance(text, str) or not text.strip():
        raise WorkflowError("CLI returned no verifiable JSON result; inspect command evidence.")
    try:
        current = json.loads(text.lstrip("\ufeff"))
    except ValueError:
        try:
            records = [json.loads(line) for line in text.lstrip("\ufeff").splitlines() if line.strip()]
        except ValueError as exc:
            raise WorkflowError("CLI did not return JSON/NDJSON; inspect command evidence.") from exc
        if not records:
            raise WorkflowError("CLI returned no verifiable JSON result; inspect command evidence.")
        return [_unwrap_response(record) for record in records]
    return _unwrap_response(current)


def _unwrap_response(current):
    # CLI envelope -> Pipeline envelope -> JSON-string status. Never search arbitrary asset data.
    for _ in range(6):
        if isinstance(current, str):
            try:
                parsed = json.loads(current)
            except ValueError:
                return current
            current = parsed
            continue
        if not isinstance(current, dict):
            return current
        if current.get("success") is False or current.get("Success") is False:
            raise WorkflowError("Command/business failure: " + json.dumps(current, ensure_ascii=False))
        if current.get("status") == "busy":
            blocked("Operation is busy: " + json.dumps(current, ensure_ascii=False))
        if current.get("status") in ("error", "failed", "cancelled"):
            raise WorkflowError("Operation did not succeed: " + json.dumps(current, ensure_ascii=False))
        errors = current.get("errors", current.get("Errors"))
        if errors or current.get("error") or current.get("Error"):
            raise WorkflowError("Command returned errors: " + json.dumps(current, ensure_ascii=False))
        payload_keys = ("summary", "Summary", "statusPath", "StatusPath", "tests", "Tests", "results", "Results")
        if "data" in current and ("success" in current or "command" in current):
            current = current["data"]
        elif ("result" in current and ("success" in current or "command" in current or "executionTimeMs" in current)
              and not any(key in current for key in payload_keys)):
            current = current["result"]
        else:
            return current
    raise WorkflowError("Unexpectedly deep response envelope.")


def parse_instances(text, project, require_ready=True):
    data = decode_response(text)
    if not isinstance(data, dict) or not isinstance(data.get("instances"), list):
        blocked("Unknown CLI status schema; do not guess the selected project.")
    instances = data["instances"]
    if len(instances) != 1 or data.get("count", len(instances)) != 1:
        blocked(f"Expected exactly one project instance; received {len(instances)}.")
    instance = instances[0]
    actual = instance.get("projectPath")
    if not actual and isinstance(instance.get("project"), dict):
        actual = instance["project"].get("path")
    if not actual and isinstance(instance.get("project"), str):
        actual = instance["project"]
    if not actual or Path(actual).resolve() != Path(project).resolve():
        blocked("CLI instance project does not match the requested absolute project path.")
    state = instance.get("state", instance.get("status"))
    if isinstance(state, dict):
        state = state.get("status")
    if not state or (require_ready and state != "ready"):
        blocked(f"Editor is not ready ({state!r}); wait for compilation/import/testing to finish.")
    return instance


def command_catalog(value):
    if isinstance(value, dict):
        for key in ("commands", "tools"):
            if isinstance(value.get(key), list):
                value = value[key]
                break
        else:
            if "result" in value:
                return command_catalog(value["result"])
            blocked("Unrecognized command discovery schema; inspect the registered capabilities.")
    if not isinstance(value, list):
        blocked("Unrecognized command discovery schema; inspect the registered capabilities.")
    catalog = {}
    for item in value:
        if not isinstance(item, dict):
            blocked("Invalid command catalog entry.")
        name = item.get("command")
        if not name:
            short_name = item.get("name")
            group = item.get("group")
            name = f"{group} {short_name}" if group and short_name else short_name
        if not isinstance(name, str) or not name.strip():
            blocked("Command catalog entry lacks its command identity.")
        if name in catalog:
            blocked(f"Duplicate command identity in discovery: {name}")
        catalog[name] = item
    if not catalog:
        blocked("No commands registered in this project.")
    return catalog


def command_names(value):
    return set(command_catalog(value))


def validate_editor_status(value, project):
    if not isinstance(value, dict):
        blocked("Editor status lacks a verifiable state.")
    actual = value.get("projectPath")
    if not actual or Path(actual).resolve() != Path(project).resolve():
        blocked("Editor status project differs from the requested absolute project path.")
    if value.get("compiling") is not False or value.get("domainReloadInProgress") is not False:
        blocked("Editor compilation/import/domain reload is active or not verifiable.")
    if value.get("status") not in ("ready", "playing"):
        blocked(f"Editor cannot start a new operation ({value.get('status')!r}).")
    return value


def test_listing(value, mode, query, filter_type):
    if not isinstance(value, dict):
        raise WorkflowError("Invalid test listing.")
    tests = value.get("tests", value.get("Tests"))
    if not isinstance(tests, list):
        raise WorkflowError("Test listing lacks individual test identities.")
    expected_mode = {"editor": "EditMode", "playmode": "PlayMode"}[mode]
    names = []
    for test in tests:
        name = test.get("fullName", test.get("FullName", ""))
        test_mode = test.get("mode", test.get("Mode", ""))
        explicit = test.get("explicit", test.get("Explicit", False))
        categories = test.get("categories", test.get("Categories", [])) or []
        value = {"testName": name, "assembly": test.get("assembly", test.get("Assembly", "")),
                 "category": ""}[filter_type]
        matches = (any(query.lower() == category.lower() for category in categories) if filter_type == "category"
                   else query.lower() in value.lower())
        if not explicit and test_mode.lower() == expected_mode.lower() and matches:
            names.append(name)
    if not names:
        blocked(f"No runnable {mode} tests match {filter_type}={query!r}.")
    return set(names)


def validate_test_result(value, expected_names):
    if not isinstance(value, dict) or value.get("status") != "completed":
        raise WorkflowError("Tests have not reached a completed state.")
    summary = value.get("summary", {})
    fields = ("total", "passed", "failed", "skipped", "inconclusive")
    if any(type(summary.get(key)) is not int or summary[key] < 0 for key in fields):
        raise WorkflowError("Invalid test summary.")
    if summary["total"] == 0 or summary["passed"] == 0:
        raise WorkflowError("Zero executed/passed tests, or all tests skipped.")
    if summary["failed"] or summary["inconclusive"]:
        raise WorkflowError("Tests failed or were inconclusive.")
    if summary["total"] != sum(summary[key] for key in fields[1:]):
        raise WorkflowError("Test summary counts are inconsistent.")
    results = value.get("results", [])
    if not isinstance(results, list) or len(results) != summary["total"]:
        raise WorkflowError("Test result list/count mismatch.")
    actual_counts = {"passed": 0, "failed": 0, "skipped": 0, "inconclusive": 0}
    for item in results:
        status = str(item.get("status", item.get("Status", ""))).lower()
        if status not in actual_counts:
            raise WorkflowError("Unknown individual test outcome.")
        actual_counts[status] += 1
    if any(summary[key] != count for key, count in actual_counts.items()):
        raise WorkflowError("Individual test outcomes disagree with the summary.")
    names = {item.get("fullName", item.get("FullName", item.get("name", item.get("Name"))))
             for item in results if isinstance(item, dict)}
    if names != expected_names or summary["total"] != len(expected_names):
        raise WorkflowError("Test identities/count differ from the selected discovery set; stale or changed result.")
    return summary


class Unity:
    def __init__(self, run):
        self.run = run
        self.context = run.context
        self.capabilities = None
        self.catalog = None

    def raw(self, arguments, timeout=45, mutating=False):
        if not self.context.cli.is_file():
            blocked(f"Unity CLI is missing: {self.context.cli}")
        result = self.run.execute(
            [self.context.cli, *arguments, "--format", "json", "--non-interactive", "--no-log-proxy"],
            timeout=timeout, mutating=mutating,
        )
        if result.returncode:
            if result.returncode == 14:
                suffix = " Mutation outcome is unknown; inspect state, do not replay." if mutating else ""
                blocked("Unity CLI operation timed out; see command evidence." + suffix)
            # Connection errors are blockers, never a successful validation.
            if (result.returncode in (17, 18)
                    or any(code in result.stdout + result.stderr for code in
                           ("NO_INSTANCES", "UNREACHABLE", "CONNECTION", "ECONNREFUSED", "TIMEOUT",
                            "502 Bad Gateway", "503 Service Unavailable"))):
                blocked("Unity CLI connection unavailable; see command evidence.")
            raise WorkflowError(f"Unity CLI exited with {result.returncode}; see command evidence.")
        return result.stdout

    def ready(self, require_ready=True):
        instance = parse_instances(self.raw(["status", "--project-path", str(self.context.project)]),
                                   self.context.project, require_ready)
        if require_ready:
            status = decode_response(self.raw(
                ["command", "--project-path", str(self.context.project), "editor", "status"]))
            return {**instance, "editorStatus": validate_editor_status(status, self.context.project)}
        return instance

    def discover(self, require_ready=True):
        self.ready(require_ready)
        value = decode_response(self.raw(["command", "--project-path", str(self.context.project),
                                         "pipeline", "list_commands", "--detail", "full"]))
        self.catalog = command_catalog(value)
        self.capabilities = set(self.catalog)
        return sorted(self.capabilities)

    def describe(self, command):
        command = canonical_command(command)
        self.discover(require_ready=False)
        if command not in self.catalog:
            blocked(f"Command is not registered in this project: {command}")
        return self.catalog[command]

    def call(self, command, parameters=None, timeout=45, mutating=False):
        command = canonical_command(command)
        if self.capabilities is None:
            self.discover()
        if command not in self.capabilities:
            blocked(f"Command is not registered in this project: {command}")
        arguments = ["command", "--project-path", str(self.context.project),
                     "--timeout", str(timeout), *command.split()]
        for key, value in (parameters or {}).items():
            arguments.extend(["--" + key, json.dumps(value, ensure_ascii=False) if not isinstance(value, str) else value])
        return decode_response(self.raw(arguments, timeout=timeout + 5, mutating=mutating))

    def query(self, command, parameters):
        command = canonical_command(command)
        validate_parameters(parameters)
        if command not in READ_ONLY:
            blocked(f"Not an audited read-only command: {command}")
        status_query = command in STATUS_QUERIES
        self.ready(require_ready=not status_query)
        if self.capabilities is None:
            self.discover(require_ready=not status_query)
        return self.call(command, parameters)

    def poll(self, command, terminal, timeout=300, interval=1):
        deadline = time.monotonic() + timeout
        errors = 0
        while time.monotonic() < deadline:
            try:
                # Domain reload may reconnect to a restarted/other instance; rebind every observation.
                self.ready(require_ready=False)
                value = self.call(command, timeout=min(15, max(1, int(deadline - time.monotonic()))))
                errors = 0
            except WorkflowError as exc:
                if exc.status != "blocked":
                    raise
                errors += 1
                if errors > 5:
                    raise
                time.sleep(interval)
                continue
            if isinstance(value, dict) and value.get("status") in terminal:
                return value
            time.sleep(interval)
        blocked(f"Timed out polling {command}. Operation may still be active; do not replay it.")

    def recompile(self, timeout):
        self.ready()
        started = self.call("script recompile", mutating=True)
        if not isinstance(started, dict) or started.get("status") not in ("compiling", "up_to_date"):
            blocked("Compilation start not acknowledged; retained results cannot establish success.")
        try:
            value = self.poll("script recompile_status", {"completed", "up_to_date"}, timeout)
        except WorkflowError:
            # Import/asmdef errors can prevent compilation callbacks entirely.
            # Collect the read-only console without hiding the original incomplete operation.
            try:
                self.call("console get")
            except WorkflowError:
                pass
            raise
        if not isinstance(value.get("failed"), bool) or not isinstance(value.get("compilationFailed"), bool):
            blocked("Compilation result lacks the current failure-state evidence.")
        # A completed compile owns its errors. Unity's native flag may still describe the
        # preceding failed compile until the successful compile's domain reload finishes.
        native_failure = value.get("compilationFailed") and value.get("status") != "completed"
        if (value.get("success") is False or value.get("failed") or native_failure
                or value.get("hasErrors") or value.get("errorCount", 0)):
            raise WorkflowError("Editor compilation reported errors.")
        self.ready()
        self.discover()
        logs = self.call("console get")
        return {"compilation": value, "console": logs}

    def tests(self, mode, query, filter_type, timeout):
        self.ready()
        expected = test_listing(self.call("test list", {"mode": mode}), mode, query, filter_type)
        before = self.call("test status")
        if isinstance(before, dict) and before.get("status") == "running":
            blocked("Another test run is active.")
        self.ready()
        # The package synchronously clears prior status before acknowledging the start.
        # An unacknowledged start is ambiguous; never fall back to a retained completed result.
        started = self.call("test run", {"mode": mode, "filter": query, "filter-type": filter_type,
                                       "async-tests": True}, mutating=True)
        if not isinstance(started, dict) or started.get("result", started.get("Result")) != "running":
            blocked("Async test start was not acknowledged; do not accept the last retained result.")
        expected_mode = {"editor": "EditMode", "playmode": "PlayMode"}[mode]
        if started.get("mode", started.get("Mode")) != expected_mode:
            blocked("Async test acknowledgement differs from the requested test mode.")
        value = self.poll("test status", {"completed"}, timeout)
        summary = validate_test_result(value, expected)
        return {"mode": mode, "filter": query, "summary": summary, "results": value["results"]}

    def preview(self, command, parameters, inputs=()):
        command = canonical_command(command)
        validate_mutation_parameters(command, parameters)
        if command not in MUTATIONS:
            blocked(f"Mutation policy/return contract not implemented: {command}")
        self.ready()
        policy = MUTATIONS[command]
        preview = self.call(command, {**parameters, policy["dry_run"]: True})
        validate_mutation_preview(command, preview)
        return make_plan(self.run, "unity", command, parameters, high_risk=True, inputs=inputs, preview=preview)

    def apply(self, plan, approval_file):
        command = canonical_command(plan.get("command"))
        if plan.get("kind") != "unity" or command not in MUTATIONS:
            blocked("Unsupported Unity mutation plan.")
        validate_mutation_parameters(command, plan["parameters"])
        self.ready()
        self.discover()
        policy = MUTATIONS[command]
        if command not in self.capabilities:
            blocked(f"Missing capability: {command}")
        current_preview = self.call(command, {**plan["parameters"], policy["dry_run"]: True})
        validate_mutation_preview(command, current_preview)
        if digest(current_preview) != digest(plan.get("preview")):
            blocked("Editor preview changed since authorization; inspect the new state and authorize a fresh preview.")
        self.ready()
        consume_approval(self.run, plan, approval_file)
        result = self.call(command, {**plan["parameters"], policy["confirm"]: True,
                                     policy["dry_run"]: False}, mutating=True)
        poll = policy["poll"]
        if poll:
            if not isinstance(result, dict) or result.get("status") != "queued":
                raise WorkflowError("Build was not queued.")
            final = self.poll(poll, {"completed"}, 1800)
            if final.get("buildId") != result.get("buildId") or not final.get("buildId"):
                raise WorkflowError("Build result does not match the submitted operation.")
            if final.get("result") != "Succeeded":
                raise WorkflowError("Player build did not succeed.")
            return final
        return result
