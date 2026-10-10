"""Guards for the Build-tab opt-in watch, signer fail-closed, and docs hotfix."""

import ast
import os
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


def load_build_watch_enabled():
    source = (ROOT / "server.py").read_text(encoding="utf-8")
    tree = ast.parse(source)
    for node in tree.body:
        if isinstance(node, ast.FunctionDef) and node.name == "build_watch_enabled":
            module = ast.Module(body=[node], type_ignores=[])
            namespace = {"os": os}
            exec(compile(module, "server.py", "exec"), namespace)
            return namespace["build_watch_enabled"]
    raise AssertionError("build_watch_enabled is missing from server.py")


class BuildWatchTests(unittest.TestCase):
    def test_off_by_default(self):
        enabled = load_build_watch_enabled()
        self.assertFalse(enabled({}))
        self.assertFalse(enabled({"HBC_BUILD_WATCH": ""}))
        self.assertFalse(enabled({"HBC_BUILD_WATCH": "0"}))
        self.assertFalse(enabled({"HBC_BUILD_WATCH": "off"}))

    def test_opt_in_values(self):
        enabled = load_build_watch_enabled()
        for value in ("1", "on", "true", "yes", "ON", " True "):
            self.assertTrue(enabled({"HBC_BUILD_WATCH": value}), value)


class SourceGuardTests(unittest.TestCase):
    def test_retired_handler_removed(self):
        text = (ROOT / "server.py").read_text(encoding="utf-8")
        self.assertNotIn("Handler_retired", text)
        self.assertNotIn("def _retired", text)

    def test_watch_uses_project_bus_not_home_handoff(self):
        server = (ROOT / "server.py").read_text(encoding="utf-8")
        form = (ROOT / "shell" / "Form1.cs").read_text(encoding="utf-8")
        self.assertIn("project's bus folder", server)
        self.assertIn("if build_watch_enabled():", server)
        self.assertNotIn("handoff-bus watch", server)
        self.assertNotIn(".grok", form)
        self.assertIn('Path.Combine(root, "bus")', form)

    def test_signer_fails_closed(self):
        text = (ROOT / "tools" / "Sign-BusJob.ps1").read_text(encoding="utf-8")
        self.assertNotIn("enforceAt", text)
        self.assertNotIn("SPOOFED-WARN", text)
        self.assertNotIn("$warn", text)
        self.assertIn("exit 3", text)
        self.assertNotRegex(text, r"exit\s+\$\(if")

    def test_readme_risks_and_no_overclaim(self):
        text = (ROOT / "README.md").read_text(encoding="utf-8")
        self.assertTrue(text.startswith("# HiveBrainComms\n\n## Risks / read first\n"))
        self.assertIn("off by default", text)
        self.assertIn("always exits 3", text)
        self.assertIn("-ExecutionPolicy Bypass", text)
        self.assertNotIn("a faked or edited job gets flagged", text)
        self.assertIn("not automatically flagged", text)

    def test_opsec_license_is_mit(self):
        text = (ROOT / "OPSEC.md").read_text(encoding="utf-8")
        self.assertIn("License of this project: MIT.", text)
        self.assertNotIn("TBD", text)


class SyntaxTests(unittest.TestCase):
    def test_server_parses(self):
        ast.parse((ROOT / "server.py").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
