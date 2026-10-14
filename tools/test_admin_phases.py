#!/usr/bin/env python3
"""Guard the admin roadmap against accidental skipped or forgotten stages."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
free_policy = (root / "docs/ZERO-COST-POLICY.md").read_text(encoding="utf-8")
state = json.loads((root / "docs/admin-phases.json").read_text(encoding="utf-8"))
roadmap = (root / "docs/ADMIN-ROADMAP.md").read_text(encoding="utf-8")
assert state["schema"] == 1
assert len(state["phases"]) == 11, "Expected all 11 dashboard stages"
assert {"sequential", "owner_directed_waiver", "passing_ci",
        "authorization_review", "staging_acceptance", "rollback_documented",
        "production_verification", "issue_checklist_complete",
        "zero_cost_only"} <= set(state["rules"])
assert "USD 0.01344/hour" in free_policy and "not created" in free_policy, "Reject paid Supabase staging"
assert "ZERO-COST-POLICY.md" in roadmap, "All stages must link the free-only rule"
allowed = {"queued", "in_progress", "blocked", "completed", "waived"}
numbers = [f"{i:02d}" for i in range(11)]
assert [p["number"] for p in state["phases"]] == numbers
assert len({p["issue"] for p in state["phases"]}) == 11
assert all(p["status"] in allowed for p in state["phases"])
assert sum(p["status"] in ("in_progress", "blocked") for p in state["phases"]) <= 1

waiver_recorded = (
    "owner-directed" in roadmap.lower()
    and "acceptance incomplete" in roadmap.lower()
    and "deferred" in roadmap.lower()
    and "never completed" in roadmap.lower()
)
if any(p["status"] == "waived" for p in state["phases"]):
    assert "owner_directed_waiver" in state["rules"], "Waiver must be an explicit roadmap rule"
    assert waiver_recorded, "A waiver must be disclosed as incomplete with deferred gates"

for index, phase in enumerate(state["phases"]):
    assert f"/issues/{phase['issue']}" in roadmap, f"Missing issue for stage {phase['number']}"
    assert f"| {phase['number']} |" in roadmap, f"Missing roadmap row: {phase['number']}"
    if index == 0:
        continue
    previous = state["phases"][index - 1]["status"]
    if previous in {"queued", "in_progress", "blocked"}:
        assert phase["status"] == "queued", (
            f"Stage {phase['number']} started before unfinished stage {state['phases'][index - 1]['number']}"
        )
    elif previous == "waived":
        assert phase["status"] in {"queued", "in_progress", "blocked", "waived", "completed"}, (
            f"Stage {phase['number']} may proceed after a waived stage only by explicit owner direction"
        )
        if phase["status"] in {"in_progress", "blocked", "waived", "completed"}:
            assert waiver_recorded, "Owner-directed transition must record incomplete/deferred acceptance"

if state["phases"][0]["status"] == "completed":
    assert "Phase 00 is being worked on" not in roadmap, "Roadmap summary is stale"
print("PASS: all 11 admin stages are present and ordered; any owner waiver remains explicitly incomplete")
