---
name: verify
description: Run dotnet build followed by dotnet test for the RadioPlayer solution. Use this after making changes to confirm nothing is broken before committing. Reports build errors, warnings, and test failures.
---

Run the following commands from the solution root (`D:\Code\.NET\wpf-radio\RadioPlayer`):

1. `dotnet build` — compile the solution. Since `TreatWarningsAsErrors` is on, any warning is a failure. Report all errors and warnings.
2. If the build succeeds, run `dotnet test` — execute all xUnit tests. Report any failures with the test name and failure message.

Summarize the result:
- If both pass: "Build and tests passed — X test(s) passed."
- If build fails: list all errors. Stop; do not run tests.
- If tests fail: list failing test names and their assertion messages.

Do not suggest code changes unless the user asks — just report the results.
