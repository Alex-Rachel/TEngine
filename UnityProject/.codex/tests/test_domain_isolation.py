"""Check that existing repository-relative exporters stay inside a fresh run."""

from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from workflow_lib.core import WorkflowError
from workflow_lib.domains import fresh_luban_export, snapshot_luban_tool


class LubanIsolationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repo = self.root / "production"
        self.tool = self.repo / "Tools/Luban"
        self.tool.mkdir(parents=True)
        (self.tool / "Luban.dll").write_bytes(b"existing-tool")
        (self.tool / "Luban.deps.json").write_text("{}", encoding="utf-8")
        (self.tool / "Templates").mkdir()
        (self.tool / "Templates/tables.sbn").write_text("template", encoding="utf-8")
        self.run = SimpleNamespace(context=SimpleNamespace(repo=self.repo), path=self.root / "run \u4e2d\u6587")
        self.run.path.mkdir()

    def test_snapshot_includes_runtime_dependencies_and_templates(self):
        destination = snapshot_luban_tool(self.run, self.run.path / "isolated")
        self.assertEqual((destination / "Luban.dll").read_bytes(), b"existing-tool")
        self.assertTrue((destination / "Luban.deps.json").is_file())
        self.assertEqual((destination / "Templates/tables.sbn").read_text(), "template")
        (destination / "Luban.dll").write_bytes(b"isolated-change")
        self.assertEqual((self.tool / "Luban.dll").read_bytes(), b"existing-tool")

    def test_missing_installed_tool_blocks_without_execution(self):
        self.run.context.repo = self.root / "missing"
        with self.assertRaises(WorkflowError) as raised:
            snapshot_luban_tool(self.run, self.run.path / "isolated")
        self.assertEqual(raised.exception.status, "blocked")

    def test_export_uses_isolated_repository_relative_tool_and_outputs(self):
        config = self.repo / "Configs/GameConfig"
        config.mkdir(parents=True)
        script = config / "export.bat"
        script.write_text("repository-owned exporter", encoding="utf-8")
        sentinel = self.repo / "UnityProject/Assets/user.bytes"
        sentinel.parent.mkdir(parents=True)
        sentinel.write_bytes(b"preserve-user-data")

        def execute(arguments, cwd, timeout, env):
            isolated = cwd.parents[1]
            self.assertNotEqual(isolated.resolve(), self.repo.resolve())
            self.assertEqual((isolated / "Tools/Luban/Luban.dll").read_bytes(), b"existing-tool")
            self.assertEqual(arguments[-1], script.name)
            output = isolated / "UnityProject/Assets"
            code = output / "GameScripts/HotFix/GameProto"
            (code / "GameConfig/Tables.cs").write_text("generated", encoding="utf-8")
            (code / "ConfigSystem.cs").write_text("loader", encoding="utf-8")
            (code / "ExternalTypeUtil.cs").write_text("helper", encoding="utf-8")
            (output / "AssetRaw/Configs/bytes/rows.bytes").write_bytes(b"fresh-data")
            return subprocess.CompletedProcess(arguments, 0, "", "")

        self.run.execute = execute
        manifest = fresh_luban_export(self.run, script, {})
        self.assertEqual(len(manifest), 4)
        self.assertIn("AssetRaw/Configs/bytes/rows.bytes", manifest)
        self.assertEqual(sentinel.read_bytes(), b"preserve-user-data")
        self.assertFalse((self.repo / "UnityProject/Assets/GameScripts").exists())


if __name__ == "__main__":
    unittest.main()
