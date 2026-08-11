# Unity EditMode test bridge

This folder is the Unity Test Framework entry point for the pure C# engine.
The repository does not yet contain an installed Unity project, so CI-equivalent
coverage currently runs from `src/Engine/Tests/` with `dotnet test`. Once the
approved Unity shell is added, copy or package `src/Engine/` and this assembly
definition into the Unity project, then run the same contract checks in EditMode.
