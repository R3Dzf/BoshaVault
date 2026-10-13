#!/usr/bin/env python3
"""Guard the admin roadmap against accidental skipped or forgotten stages."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
state = json.loads((root / "docs/admin-phases.json").read_text(encoding="utf-8"))
roadmap = (root / "docs/ADMIN-ROADMAP.md").read_text(encoding="utf-8")
assert state["schema"] == 1
assert len(state["phases"]) == 11, "Expected all 11 dashboard stages"
assert {"sequential", "passing_ci", "authorization_review", "staging_acceptance",
        "rollback_documented", "production_verification", "issue_checklist_complete"} <= set(state["rules"])
allowed = {"queued", "in_progress", "blocked", "completed"}
numbers = [f"{i:02d}" for i in range(11)]
assert [p["number"] for p in state["phases"]] == numbers
assert len({p["issue"] for p in state["phases"]}) == 11
assert all(p["status"] in allowed for p in state["phases"])
assert sum(p["status"] in ("in_progress", "blocked") for p in state["phases"]) <= 1
seen_unfinished = False
for phase in state["phases"]:
    assert f"/issues/{phase['issue']}" in roadmap, f"Missing issue for stage {phase['number']}"
    assert f"| {phase['number']} |" in roadmap, f"Missing roadmap row: {phase['number']}"
    if seen_unfinished:
        assert phase["status"] == "queued", f"Stage {phase['number']} started before an earlier stage closed"
    if phase["status"] != "completed":
        seen_unfinished = True
if state["phases"][0]["status"] == "completed":
    assert "Phase 00 is being worked on" not in roadmap, "Roadmap summary is stale"
print("PASS: all 11 admin stages are present and ordered, no phase was skipped")
