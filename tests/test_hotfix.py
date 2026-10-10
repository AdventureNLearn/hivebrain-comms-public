"""Guards for the Build-tab opt-in watch, signer fail-closed, and docs hotfix."""

import ast
import os
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


def load_server_fn(*names):
    source = (ROOT / "server.py").read_text(encoding="utf-8")
    tree = ast.parse(source)
    body = []
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id.startswith("CHILD_ENV"):
                    body.append(node)
        if isinstance(node, ast.FunctionDef) and node.name in names:
            body.append(node)
    if not any(isinstance(node, ast.FunctionDef) and node.name == names[0] for node in body):
        raise AssertionError(names[0] + " is missing from server.py")
    module = ast.Module(body=body, type_ignores=[])
    namespace = {"os": os}
    exec(compile(module, "server.py", "exec"), namespace)
    return namespace


class BuildWatchTests(unittest.TestCase):
    def test_off_by_default(self):
        enabled = load_server_fn("build_watch_enabled")["build_watch_enabled"]
        self.assertFalse(enabled({}))
        self.assertFalse(enabled({"HBC_BUILD_WATCH": ""}))
        self.assertFalse(enabled({"HBC_BUILD_WATCH": "0"}))
        self.assertFalse(enabled({"HBC_BUILD_WATCH": "off"}))

    def test_opt_in_values(self):
        enabled = load_server_fn("build_watch_enabled")["build_watch_enabled"]
        for value in ("1", "on", "true", "yes", "ON", " True "):
            self.assertTrue(enabled({"HBC_BUILD_WATCH": value}), value)


class ChildEnvTests(unittest.TestCase):
    def test_drops_secrets_and_keeps_allowlist(self):
        fn = load_server_fn("auth_env_enabled", "child_env_allowlist")["child_env_allowlist"]
        source = {
            "GROK_HOME": "home",
            "PATH": "bin",
            "SYSTEMROOT": "C:\\Windows",
            "WINDIR": "C:\\Windows",
            "COMSPEC": "cmd.exe",
            "TEMP": "temp",
            "TMP": "tmp",
            "USERPROFILE": "profile",
            "LOCALAPPDATA": "local",
            "APPDATA": "roam",
            "GROK_API_KEY": "should-not-pass",
            "XAI_API_KEY": "should-not-pass",
            "OPENAI_API_KEY": "should-not-pass",
            "SECRET": "nope",
            "HBC_PASS_GROK_AUTH": "",
        }
        out = fn(source)
        self.assertEqual(out["GROK_HOME"], "home")
        self.assertEqual(out["PATH"], "bin")
        self.assertEqual(out["SYSTEMROOT"], "C:\\Windows")
        self.assertNotIn("GROK_API_KEY", out)
        self.assertNotIn("XAI_API_KEY", out)
        self.assertNotIn("OPENAI_API_KEY", out)
        self.assertNotIn("SECRET", out)
        self.assertNotIn("HBC_PASS_GROK_AUTH", out)

    def test_auth_opt_in_is_explicit(self):
        ns = load_server_fn("auth_env_enabled", "child_env_allowlist")
        source = {
            "PATH": "bin",
            "GROK_API_KEY": "k",
            "XAI_API_KEY": "x",
            "OPENAI_API_KEY": "o",
            "HBC_PASS_GROK_AUTH": "1",
        }
        out = ns["child_env_allowlist"](source)
        self.assertEqual(out["GROK_API_KEY"], "k")
        self.assertEqual(out["XAI_API_KEY"], "x")
        self.assertNotIn("OPENAI_API_KEY", out)
        self.assertFalse(ns["auth_env_enabled"]({}))
        self.assertTrue(ns["auth_env_enabled"]({"HBC_PASS_GROK_AUTH": "yes"}))

    def test_extra_overrides_without_copying_parent(self):
        fn = load_server_fn("auth_env_enabled", "child_env_allowlist")["child_env_allowlist"]
        out = fn({"PATH": "parent", "SECRET": "nope"}, {"PATH": "child", "GROK_HOME": "pane"})
        self.assertEqual(out, {"PATH": "child", "GROK_HOME": "pane"})


class SourceGuardTests(unittest.TestCase):
    def test_retired_handler_removed(self):
        text = (ROOT / "server.py").read_text(encoding="utf-8")
        self.assertNotIn("Handler_retired", text)
        self.assertNotIn("def _retired", text)

    def test_watch_uses_project_bus_not_home_handoff(self):
        server = (ROOT / "server.py").read_text(encoding="utf-8")
        form = (ROOT / "shell" / "Form1.cs").read_text(encoding="utf-8")
        self.assertIn("project's bus folder", server)
        self.assertIn("build_watch_enabled()", server)
        self.assertNotIn("handoff-bus watch", server)
        self.assertNotIn("BUS-PROTOCOL", server)
        self.assertNotIn(".grok", form)
        self.assertIn('Path.Combine(root, "bus")', form)

    def test_build_cwd_defaults_to_project_root(self):
        server = (ROOT / "server.py").read_text(encoding="utf-8")
        self.assertIn('os.environ.get("HBC_BUILD_CWD", str(ROOT))', server)
        self.assertNotIn("AOS", server)

    def test_child_env_is_allowlisted(self):
        server = (ROOT / "server.py").read_text(encoding="utf-8")
        self.assertNotIn("os.environ.copy()", server)
        self.assertIn("child_env_allowlist", server)
        self.assertIn("HBC_PASS_GROK_AUTH", server)

    def test_grok_starts_only_on_request(self):
        server = (ROOT / "server.py").read_text(encoding="utf-8")
        html = (ROOT / "build.html").read_text(encoding="utf-8")
        self.assertIn("start-grok", server)
        self.assertIn("write_build_grok", server)
        self.assertIn("Start grok", html)
        self.assertIn("This will run an agent with your user permissions.", html)
        self.assertIn("Default answer is No.", html)
        self.assertIn("Opened \" + id + \" as a plain shell.", html)
        self.assertNotIn('pty.write("grok\\r\\n")\n        PANES', server)

    def test_dead_build_shortcut_helpers_removed(self):
        form = (ROOT / "shell" / "Form1.cs").read_text(encoding="utf-8")
        self.assertNotIn("FocusOrStartBuild", form)
        self.assertNotIn("FindBuildLink", form)

    def test_signer_fails_closed(self):
        text = (ROOT / "tools" / "Sign-BusJob.ps1").read_text(encoding="utf-8")
        self.assertNotIn("enforceAt", text)
        self.assertNotIn("SPOOFED-WARN", text)
        self.assertNotIn("$warn", text)
        self.assertIn("exit 3", text)
        self.assertNotRegex(text, r"exit\s+\$\(if")

    def test_readme_risks_and_no_overclaim(self):
        text = (ROOT / "README.md").read_text(encoding="utf-8-sig")
        self.assertTrue(text.startswith("# HiveBrainComms\n\n## Risks / read first\n"))
        self.assertIn("off by default", text)
        self.assertIn("always exits 3", text)
        self.assertIn("-ExecutionPolicy Bypass", text)
        self.assertIn("default answer is **No**", text)
        self.assertIn("env allowlist", text)
        self.assertIn("HBC_PASS_GROK_AUTH=1", text)
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
