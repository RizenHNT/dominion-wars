# Java / C# black-box comparison

`run_dual.py` is a comparison harness, not a rule implementation. It invokes
two commands once per scenario with the same `--seed`, `--scenario`, and
`--json` arguments, then compares canonical JSON snapshots while ignoring
event IDs.

## Prerequisites

- Python 3.10 or newer (`python` or `python3` on PATH).
- A Java runner and a C# runner that both implement the documented arguments
  and emit exactly one JSON snapshot to stdout.
- Scenario JSON files accepted by both runners.

Example from the repository root:

```powershell
python scripts/java_compare/run_dual.py `
  --java-cmd "java -cp build/classes com.dominionwars.test.JsonRunner" `
  --cs-cmd "dotnet run --project tools/JsonRunner" `
  --seed 42 `
  --scenarios scenarios/smoke.json
```

The current Java `TestMain` is a human-readable regression runner and does
not expose this JSON protocol. The harness therefore cannot be claimed as
executed until compatible runners and a Python interpreter are available.
