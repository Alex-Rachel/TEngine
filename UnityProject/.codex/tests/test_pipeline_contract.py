"""Offline Pipeline contract tests. No Editor connection or project asset writes."""

from contextlib import nullcontext
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import workflow
from workflow_lib import checks, core
from workflow_lib import unity as unity_module
from workflow_lib.core import Run, WorkflowError, aggregate, read_json
from workflow_lib.unity import (
    MUTATIONS, Unity, canonical_command, command_catalog, command_names, decode_response,
    parse_instances, test_listing as select_tests, validate_parameters, validate_test_result,
)


def context(root):
    return SimpleNamespace(project=root, repo=root.parent, runs=root / "runs", cli=root / "unity.exe")


def instance(project, state="ready"):
    return {"instances": [{"projectPath": str(project), "state": state}], "count": 1}


def editor_status(project, **changes):
    return {"projectPath": str(project), "status": "ready", "compiling": False,
            "domainReloadInProgress": False, **changes}


def listing(name="Game.Tests.Inventory.Refresh", mode="EditMode"):
    return {"Tests": [{"FullName": name, "Mode": mode, "Assembly": "Game.Tests",
                       "Categories": ["Inventory"], "Explicit": False}]}


def results(name="Game.Tests.Inventory.Refresh", outcome="Passed"):
    counts = {"total": 1, "passed": 0, "failed": 0, "skipped": 0, "inconclusive": 0}
    counts[outcome.lower()] = 1
    return {"status": "completed", "summary": counts, "results": [{"FullName": name, "Status": outcome}]}


class ProtocolTests(unittest.TestCase):
    def test_plain_result_and_lean_envelope_have_same_value(self):
        payload = {"status": "up_to_date", "failed": False}
        self.assertEqual(decode_response(json.dumps(payload)), payload)
        self.assertEqual(decode_response(json.dumps({"success": True, "result": payload})), payload)

    def test_cli_and_http_envelopes_unwrap(self):
        payload = {"status": "completed", "failed": False}
        response = {"success": True, "data": {"success": True, "result": json.dumps(payload)}}
        self.assertEqual(decode_response(json.dumps(response)), payload)

    def test_test_acknowledgement_keeps_its_own_payload(self):
        payload = {"success": True, "result": "running", "Summary": {}, "Results": [],
                   "Mode": "EditMode", "StatusPath": "Temp/pipeline_test_status.json"}
        response = {"success": True, "result": payload}
        self.assertEqual(decode_response(json.dumps(response)), payload)

    def test_build_status_result_is_not_an_envelope(self):
        payload = {"status": "completed", "buildId": "build-1", "result": "Succeeded"}
        self.assertEqual(decode_response(json.dumps(payload)), payload)

    def test_ndjson_keeps_every_record(self):
        records = [{"path": "Assets/A.asset"}, {"path": "Assets/B.asset"}]
        output = "\n".join(json.dumps(item) for item in records) + "\n"
        self.assertEqual(decode_response(output), records)

    def test_empty_or_mixed_output_has_no_success_evidence(self):
        for output in ("", " \n", "progress\n{\"success\":true}", "{}\nnot-json"):
            with self.subTest(output=output), self.assertRaises(WorkflowError):
                decode_response(output)

    def test_explicit_null_is_preserved(self):
        self.assertIsNone(decode_response('{"success":true,"result":null}'))

    def test_business_failure_in_any_envelope_is_rejected(self):
        responses = ({"success": False}, {"error": "feature unavailable"},
                     {"success": True, "result": {"errors": ["compile error"]}},
                     {"status": "cancelled"})
        for response in responses:
            with self.subTest(response=response), self.assertRaises(WorkflowError):
                decode_response(json.dumps(response))

    def test_busy_is_a_blocker(self):
        with self.assertRaises(WorkflowError) as caught:
            decode_response('{"status":"busy","busyReason":"settling"}')
        self.assertEqual(caught.exception.status, "blocked")


class CatalogTests(unittest.TestCase):
    def test_short_names_in_different_groups_remain_distinct(self):
        catalog = {"commands": [
            {"command": "test list", "name": "list", "group": "test", "schema": {"title": "test list"}},
            {"command": "package list", "name": "list", "group": "package", "schema": {}},
        ]}
        self.assertEqual(command_names(catalog), {"test list", "package list"})
        self.assertEqual(command_catalog(catalog)["test list"]["schema"]["title"], "test list")

    def test_group_and_name_form_a_command_if_full_name_is_absent(self):
        self.assertEqual(command_names([{"group": "scene", "name": "list_open"}]), {"scene list_open"})

    def test_empty_or_duplicate_catalog_cannot_establish_capabilities(self):
        for catalog in ({"commands": []}, {"commands": [{}]}, {"commands": [
                {"command": "test list"}, {"command": "test list"}]}):
            with self.subTest(catalog=catalog), self.assertRaises(WorkflowError):
                command_names(catalog)

    def test_only_project_commands_have_convenience_aliases(self):
        self.assertEqual(canonical_command("tengine_bake_ugui"), "global tengine_bake_ugui")
        self.assertEqual(canonical_command("tengine_validate_assets"), "global tengine_validate_assets")
        self.assertEqual(canonical_command("editor_status"), "editor_status")

    def test_kebab_case_and_project_underscores_are_preserved(self):
        parameters = {"filter-type": "assembly", "output_path": "Assets/UI.prefab", "legacy_text": False}
        self.assertEqual(validate_parameters(parameters), parameters)
        self.assertEqual(MUTATIONS["asset delete"]["dry_run"], "dry-run")
        self.assertEqual(MUTATIONS["global tengine_bake_ugui"]["dry_run"], "dry_run")

    def test_parameters_cannot_override_selection_or_output_contract(self):
        for key in ("project-path", "project_path", "Project-Path", "runtime-path", "fields",
                    "format", "confirm", "dry_run", "dry-run", "timeout", "--unsafe"):
            with self.subTest(key=key), self.assertRaises(WorkflowError):
                validate_parameters({key: "value"})


class ConnectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.unity = Unity(SimpleNamespace(context=context(self.root)))

    def test_exact_project_ready_instance_is_selected(self):
        selected = parse_instances(json.dumps(instance(self.root)), self.root)
        self.assertEqual(selected["projectPath"], str(self.root))

    def test_wrong_project_or_multiple_instances_are_blocked(self):
        multiple = instance(self.root)
        multiple["instances"].append(multiple["instances"][0])
        multiple["count"] = 2
        for response in (instance(self.root / "other"), multiple, {"instances": [], "count": 0}):
            with self.subTest(response=response), self.assertRaises(WorkflowError) as caught:
                parse_instances(json.dumps(response), self.root)
            self.assertEqual(caught.exception.status, "blocked")

    def test_busy_transport_is_observable_but_cannot_start_work(self):
        response = json.dumps(instance(self.root, "settling"))
        with self.assertRaises(WorkflowError):
            parse_instances(response, self.root)
        self.assertEqual(parse_instances(response, self.root, require_ready=False)["state"], "settling")

    def test_transport_ready_does_not_hide_editor_compilation(self):
        self.unity.raw = Mock(side_effect=[
            json.dumps(instance(self.root)), json.dumps(editor_status(self.root, compiling=True, status="compiling"))])
        with self.assertRaises(WorkflowError) as caught:
            self.unity.ready()
        self.assertEqual(caught.exception.status, "blocked")

    def test_domain_reload_and_wrong_editor_status_are_blocked(self):
        for status in (editor_status(self.root, domainReloadInProgress=True),
                       editor_status(self.root / "other"), editor_status(self.root, status="blocked_by_dialog")):
            self.unity.raw = Mock(side_effect=[json.dumps(instance(self.root)), json.dumps(status)])
            with self.subTest(status=status), self.assertRaises(WorkflowError):
                self.unity.ready()

    def test_status_query_and_describe_work_while_editor_is_busy(self):
        spec = {"command": "scene get_hierarchy", "name": "get_hierarchy", "group": "scene",
                "parameters": [{"name": "depth"}], "schema": {"type": "object"}}
        self.unity.raw = Mock(side_effect=[
            json.dumps(instance(self.root, "settling")), json.dumps({"commands": [spec]})])
        self.assertEqual(self.unity.describe("scene get_hierarchy"), spec)
        catalog_call = self.unity.raw.call_args_list[-1].args[0]
        self.assertEqual(catalog_call[-4:], ["pipeline", "list_commands", "--detail", "full"])
        self.assertFalse(any("editor" in call.args[0] for call in self.unity.raw.call_args_list))

    def test_query_rejects_unreviewed_mutations_before_any_call(self):
        self.unity.ready = Mock()
        self.unity.call = Mock()
        with self.assertRaises(WorkflowError):
            self.unity.query("script eval", {"code": "1+1"})
        self.unity.ready.assert_not_called()
        self.unity.call.assert_not_called()

    def test_audited_status_query_skips_idle_requirement(self):
        self.unity.ready = Mock()
        self.unity.capabilities = {"script recompile_status"}
        self.unity.call = Mock(return_value={"status": "compiling"})
        self.assertEqual(self.unity.query("script recompile_status", {})["status"], "compiling")
        self.unity.ready.assert_called_once_with(require_ready=False)


class InvocationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.context = context(self.root)
        self.context.cli.touch()
        self.execute = Mock(return_value=subprocess.CompletedProcess([], 0, '{"status":"ready"}', ""))
        self.unity = Unity(SimpleNamespace(context=self.context, execute=self.execute))

    def test_canonical_command_uses_split_argv_and_exact_parameter_names(self):
        self.unity.capabilities = {"test run"}
        self.unity.call("test run", {"filter-type": "assembly", "async-tests": True}, mutating=True)
        args = self.execute.call_args.args[0]
        position = args.index("test")
        self.assertEqual(args[position:position + 2], ["test", "run"])
        self.assertEqual(args[args.index("--filter-type") + 1], "assembly")
        self.assertEqual(args[args.index("--async-tests") + 1], "true")
        self.assertTrue(self.execute.call_args.kwargs["mutating"])

    def test_project_alias_is_sent_as_global_command(self):
        self.unity.capabilities = {"global tengine_validate_assets"}
        self.unity.call("tengine_validate_assets", {"paths": ["Assets/A.asset"]})
        args = self.execute.call_args.args[0]
        self.assertIn("global", args)
        self.assertEqual(json.loads(args[args.index("--paths") + 1]), ["Assets/A.asset"])

    def test_cli_timeout_does_not_replay_a_mutation(self):
        self.execute.return_value = subprocess.CompletedProcess([], 14, "", "Timed out")
        with self.assertRaises(WorkflowError) as caught:
            self.unity.raw(["command", "asset", "delete"], mutating=True)
        self.assertEqual(caught.exception.status, "blocked")
        self.assertIn("outcome is unknown", str(caught.exception))
        self.execute.assert_called_once()

    def test_unavailable_and_dialog_codes_are_blocked(self):
        for code in (17, 18):
            self.execute.return_value = subprocess.CompletedProcess([], code, "", "")
            with self.subTest(code=code), self.assertRaises(WorkflowError) as caught:
                self.unity.raw(["status"])
            self.assertEqual(caught.exception.status, "blocked")

    def test_runtime_selection_reports_cli_source_without_inventing_player_path(self):
        payload = {"UnityVersion": "2022.3.62f1", "Platform": "WindowsPlayer", "IsPlaying": True,
                   "BuildGuid": "player-build"}
        self.unity.raw = Mock(return_value=json.dumps(payload))
        value = workflow.runtime_status(self.unity, self.root / "player")
        self.assertEqual(value["selectionSource"], "CLI --runtime-path")
        self.assertEqual(value["runtime"], payload)
        self.assertEqual(self.unity.raw.call_args.args[0][-2:], ["runtime", "status"])

    def test_runtime_empty_or_editor_payload_cannot_pass(self):
        self.unity.raw = Mock(return_value='{"status":"ready"}')
        with self.assertRaises(WorkflowError):
            workflow.runtime_status(self.unity, self.root / "player")

    def test_describe_is_exposed_by_argument_parser(self):
        args = workflow.parser().parse_args(["unity", "describe", "scene get_hierarchy"])
        self.assertEqual((args.action, args.name), ("describe", "scene get_hierarchy"))


class TestExecutionTests(unittest.TestCase):
    def setUp(self):
        self.unity = Unity(SimpleNamespace(context=None))
        self.unity.ready = Mock()
        self.unity.poll = Mock(return_value=results())
        self.ack = {"success": True, "result": "running", "Mode": "EditMode",
                    "Summary": {}, "Results": [], "StatusPath": "Temp/pipeline_test_status.json"}

    def test_selected_case_and_individual_outcome_establish_success(self):
        self.unity.call = Mock(side_effect=[listing(), {"status": "completed"}, self.ack])
        value = self.unity.tests("editor", "Game.Tests", "assembly", 30)
        self.assertEqual(value["summary"]["passed"], 1)
        self.assertEqual(self.unity.call.call_args.args[0], "test run")
        self.assertEqual(self.unity.call.call_args.args[1]["filter-type"], "assembly")
        self.unity.poll.assert_called_once_with("test status", {"completed"}, 30)

    def test_zero_discovered_cases_never_start_a_run(self):
        self.unity.call = Mock(return_value={"Tests": []})
        with self.assertRaises(WorkflowError) as caught:
            self.unity.tests("editor", "Game.Tests", "assembly", 30)
        self.assertEqual(caught.exception.status, "blocked")
        self.unity.call.assert_called_once_with("test list", {"mode": "editor"})

    def test_running_test_is_not_replaced(self):
        self.unity.call = Mock(side_effect=[listing(), {"status": "running"}])
        with self.assertRaises(WorkflowError):
            self.unity.tests("editor", "Game.Tests", "assembly", 30)
        self.assertEqual(self.unity.call.call_count, 2)
        self.unity.poll.assert_not_called()

    def test_missing_ack_does_not_accept_retained_completed_results(self):
        self.unity.call = Mock(side_effect=[listing(), results(), {"success": True}])
        with self.assertRaises(WorkflowError):
            self.unity.tests("editor", "Game.Tests", "assembly", 30)
        self.unity.poll.assert_not_called()

    def test_wrong_mode_ack_is_not_accepted(self):
        self.unity.call = Mock(side_effect=[listing(), results(), {**self.ack, "Mode": "PlayMode"}])
        with self.assertRaises(WorkflowError):
            self.unity.tests("editor", "Game.Tests", "assembly", 30)
        self.unity.poll.assert_not_called()

    def test_stale_or_changed_result_names_are_rejected(self):
        self.unity.call = Mock(side_effect=[listing(), results(), self.ack])
        self.unity.poll.return_value = results(name="Other.Tests.PreviousRun")
        with self.assertRaises(WorkflowError):
            self.unity.tests("editor", "Game.Tests", "assembly", 30)

    def test_zero_all_skipped_failure_or_inconclusive_cannot_pass(self):
        empty = {"status": "completed", "summary": {
            "total": 0, "passed": 0, "failed": 0, "skipped": 0, "inconclusive": 0}, "results": []}
        for value in (empty, results(outcome="Skipped"), results(outcome="Failed"), results(outcome="Inconclusive")):
            with self.subTest(value=value), self.assertRaises(WorkflowError):
                validate_test_result(value, {"Game.Tests.Inventory.Refresh"})

    def test_aggregate_counts_must_match_individual_results(self):
        value = results()
        value["results"][0]["Status"] = "Failed"
        with self.assertRaises(WorkflowError):
            validate_test_result(value, {"Game.Tests.Inventory.Refresh"})

    def test_explicit_cases_are_excluded_and_category_match_is_exact(self):
        value = listing()
        value["Tests"].append({**value["Tests"][0], "FullName": "Explicit.Case", "Explicit": True})
        self.assertEqual(select_tests(value, "editor", "Inventory", "category"),
                         {"Game.Tests.Inventory.Refresh"})
        with self.assertRaises(WorkflowError):
            select_tests(value, "editor", "Invent", "category")


class CompilationTests(unittest.TestCase):
    def setUp(self):
        self.unity = Unity(SimpleNamespace(context=None))
        self.unity.ready = Mock()
        self.unity.discover = Mock()
        self.unity.call = Mock(side_effect=[{"status": "compiling"}, {"entries": []}])
        self.unity.poll = Mock(return_value={
            "status": "completed", "failed": False, "errors": [], "compilationFailed": False})

    def test_compilation_needs_acknowledgement_and_current_failure_state(self):
        value = self.unity.recompile(30)
        self.assertFalse(value["compilation"]["failed"])
        self.assertEqual(self.unity.call.call_args_list[0].args[0], "script recompile")
        self.unity.poll.assert_called_once_with("script recompile_status", {"completed", "up_to_date"}, 30)

    def test_unknown_start_cannot_reuse_previous_success(self):
        self.unity.call = Mock(return_value={"status": "idle"})
        with self.assertRaises(WorkflowError):
            self.unity.recompile(30)
        self.unity.poll.assert_not_called()

    def test_completed_compile_does_not_inherit_previous_native_failure(self):
        self.unity.poll.return_value = {
            "status": "completed", "failed": False, "errors": [], "compilationFailed": True}
        value = self.unity.recompile(30)
        self.assertFalse(value["compilation"]["failed"])
        self.assertTrue(value["compilation"]["compilationFailed"])
        self.unity.ready.assert_called()

    def test_failed_compilation_and_missing_failure_fields_cannot_pass(self):
        for value in ({"status": "completed", "failed": True, "compilationFailed": False},
                      {"status": "completed"}, {"status": "up_to_date", "failed": False, "compilationFailed": True}):
            self.unity.call = Mock(return_value={"status": "compiling"})
            self.unity.poll = Mock(return_value=value)
            with self.subTest(value=value), self.assertRaises(WorkflowError):
                self.unity.recompile(30)


class BuildPreviewTests(unittest.TestCase):
    def setUp(self):
        self.unity = Unity(SimpleNamespace(context=None))
        self.unity.ready = Mock()
        self.unity.discover = Mock()
        self.unity.capabilities = {"build run"}
        self.valid_preview = {"status": "dry_run", "valid": True, "validationErrors": []}
        self.parameters = {"target": "StandaloneWindows64", "output-path": "Builds/Fixture/Game.exe",
                           "scenes": ["Assets/Scenes/Main.unity"]}

    def invalid_previews(self):
        return (
            {"status": "dry_run", "valid": False, "validationErrors": [{"field": "target", "message": "Unsupported"}]},
            {"status": "dry_run", "valid": False, "validationErrors": []},
            {"status": "dry_run", "valid": True, "validationErrors": [{"field": "scenes", "message": "Missing"}]},
            {"status": "dry_run", "valid": 1, "validationErrors": []},
            {"status": "dry_run", "validationErrors": []},
            {"status": "dry_run", "valid": True},
            {"status": "queued", "valid": True, "validationErrors": []},
            None,
        )

    def test_invalid_build_preview_cannot_create_an_approval_plan(self):
        for preview in self.invalid_previews():
            self.unity.call = Mock(return_value=preview)
            with self.subTest(preview=preview), patch.object(unity_module, "make_plan") as make_plan:
                with self.assertRaises(WorkflowError):
                    self.unity.preview("build run", self.parameters)
                make_plan.assert_not_called()
                self.unity.call.assert_called_once_with(
                    "build run", {**self.parameters, "dry-run": True})

    def test_valid_build_preview_creates_a_bound_plan(self):
        self.unity.call = Mock(return_value=self.valid_preview)
        with patch.object(unity_module, "make_plan", return_value={"plan": "fixture-preview.json"}) as make_plan:
            value = self.unity.preview("build run", self.parameters)
        self.assertEqual(value, {"plan": "fixture-preview.json"})
        self.assertEqual(make_plan.call_args.args[3], self.parameters)
        self.assertEqual(make_plan.call_args.kwargs["preview"], self.valid_preview)
        self.assertTrue(make_plan.call_args.kwargs["high_risk"])

    def test_apply_rejects_invalid_revalidation_before_consuming_approval(self):
        for preview in self.invalid_previews():
            plan = {"kind": "unity", "command": "build run", "parameters": self.parameters, "preview": preview}
            self.unity.call = Mock(return_value=preview)
            with self.subTest(preview=preview), patch.object(unity_module, "consume_approval") as consume:
                with self.assertRaises(WorkflowError):
                    self.unity.apply(plan, "fixture-approval.json")
                consume.assert_not_called()
                self.unity.call.assert_called_once_with("build run", {**self.parameters, "dry-run": True})

    def incomplete_scopes(self):
        scopes = [{key: value for key, value in self.parameters.items() if key != missing}
                  for missing in self.parameters]
        scopes.extend({**self.parameters, key: value} for key, value in (
            ("target", ""), ("target", None), ("output-path", " "),
            ("scenes", []), ("scenes", None), ("scenes", "Assets/Scenes/Main.unity"),
            ("scenes", [""]), ("scenes", [None])))
        return scopes

    def test_preview_requires_explicit_build_scope_before_contacting_editor(self):
        for parameters in self.incomplete_scopes():
            self.unity.call = Mock(return_value=self.valid_preview)
            with self.subTest(parameters=parameters), patch.object(unity_module, "make_plan") as make_plan:
                with self.assertRaises(WorkflowError):
                    self.unity.preview("build run", parameters)
                self.unity.call.assert_not_called()
                self.unity.ready.assert_not_called()
                make_plan.assert_not_called()

    def test_apply_rejects_incomplete_build_scope_without_consuming_approval(self):
        for parameters in self.incomplete_scopes():
            plan = {"kind": "unity", "command": "build run", "parameters": parameters,
                    "preview": self.valid_preview}
            self.unity.call = Mock(return_value=self.valid_preview)
            with self.subTest(parameters=parameters), patch.object(unity_module, "consume_approval") as consume:
                with self.assertRaises(WorkflowError):
                    self.unity.apply(plan, "fixture-approval.json")
                self.unity.call.assert_not_called()
                self.unity.ready.assert_not_called()
                consume.assert_not_called()

    def test_valid_apply_revalidates_then_consumes_approval_and_checks_build_identity(self):
        plan = {"kind": "unity", "command": "build run", "parameters": self.parameters,
                "preview": self.valid_preview}
        events = []

        def call(command, parameters, **options):
            events.append("write" if options.get("mutating") else "preview")
            if options.get("mutating"):
                self.assertEqual(events, ["preview", "approval", "write"])
                self.assertEqual(parameters, {**self.parameters, "confirm": True, "dry-run": False})
                return {"status": "queued", "buildId": "fixture-build"}
            return self.valid_preview

        self.unity.call = Mock(side_effect=call)
        self.unity.poll = Mock(return_value={
            "status": "completed", "buildId": "fixture-build", "result": "Succeeded"})
        with patch.object(unity_module, "consume_approval", side_effect=lambda *args: events.append("approval")):
            value = self.unity.apply(plan, "fixture-approval.json")
        self.assertEqual(value["result"], "Succeeded")
        self.unity.poll.assert_called_once_with("build status", {"completed"}, 1800)


class EvidenceAndApprovalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.context = context(Path(self.temp.name))

    def test_approval_is_bound_to_preview_and_consumed_once(self):
        with patch.object(core, "workspace_fingerprint", return_value="test-worktree"):
            run = Run(self.context, "isolated approval fixture")
            preview = core.make_plan(run, "unity", "asset delete", {"path": "Assets/Fixture.asset"}, high_risk=True)
            plan = read_json(preview["plan"])
            approval = core.approve(run, preview["plan"], "test-reviewer", "isolated test confirmation", high_risk=True)
            core.consume_approval(run, plan, approval["approval"])
            with self.assertRaises(WorkflowError) as caught:
                core.consume_approval(run, plan, approval["approval"])
            self.assertEqual(caught.exception.status, "blocked")

    def test_worktree_change_invalidates_preview(self):
        with patch.object(core, "workspace_fingerprint", return_value="before"):
            run = Run(self.context, "isolated approval fixture")
            preview = core.make_plan(run, "unity", "asset delete", {"path": "Assets/Fixture.asset"})
            plan = read_json(preview["plan"])
        with patch.object(core, "workspace_fingerprint", return_value="after"), self.assertRaises(WorkflowError):
            core.validate_plan(self.context, plan)

    def test_json_markdown_and_returned_result_do_not_publish_secrets(self):
        run = Run(self.context, "diagnostic Bearer command-secret")
        run.data["steps"] = [
            {"name": "result", "status": "passed", "result": {"evalToken": "result-secret"}},
            {"name": "failure", "status": "failed",
             "message": 'Command/business failure: {"evalToken":"message-secret"}'},
        ]
        self.assertEqual(run.finish(), 1)
        for filename in ("report.json", "report.md"):
            text = (run.path / filename).read_text(encoding="utf-8")
            for secret in ("command-secret", "result-secret", "message-secret"):
                self.assertNotIn(secret, text)
            self.assertIn("[REDACTED]", text)

    def test_interrupted_report_remains_blocked(self):
        run = Run(self.context, "interrupted fixture")
        run.data["steps"] = [{"name": "one", "status": "passed"}]
        self.assertEqual(core.render_report(run.path, run.data), "blocked")

    def test_scene_asset_metadata_cannot_establish_scene_contents_acceptance(self):
        run = Run(self.context, "scene validation fixture")
        unity = Mock()
        unity.discover.return_value = ["global tengine_validate_assets"]
        unity.recompile.return_value = {"compilation": "fixture-passed"}
        unity.query.return_value = {"assets": "fixture-passed"}
        unity.tests.return_value = {"tests": "fixture-passed"}
        with patch.object(checks, "Unity", return_value=unity), patch.object(checks, "project_lock", return_value=nullcontext()):
            checks.verify(run, "unity", "Game.Tests", "assembly", 30, ["Assets/Scenes/Main.unity"])
        self.assertEqual(aggregate(run.data["steps"]), "blocked")
        scene_step = next(step for step in run.data["steps"] if step["name"] == "scene-contents-validation")
        self.assertEqual(scene_step["status"], "skipped")
        self.assertTrue(scene_step["required"])

    def test_prefab_validation_does_not_claim_scene_coverage(self):
        run = Run(self.context, "prefab validation fixture")
        unity = Mock()
        unity.discover.return_value = ["global tengine_validate_assets"]
        unity.recompile.return_value = {"compilation": "fixture-passed"}
        unity.query.return_value = {"assets": "fixture-passed"}
        unity.tests.return_value = {"tests": "fixture-passed"}
        with patch.object(checks, "Unity", return_value=unity), patch.object(checks, "project_lock", return_value=nullcontext()):
            checks.verify(run, "unity", "Game.Tests", "assembly", 30, ["Assets/UI/Panel.prefab"])
        self.assertEqual(aggregate(run.data["steps"]), "passed")
        self.assertFalse(any(step["name"] == "scene-contents-validation" for step in run.data["steps"]))


if __name__ == "__main__":
    unittest.main()
