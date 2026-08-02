# T01 — Solution scaffold, Directory.*.props, .slnx

**Layer:** build · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

Create the `.slnx` solution with the nine `src/` projects of [[../sad|SAD]] §5, plus the five
test projects — including `JobHunter.TestKit`, which holds `FakeClock`, `SequentialIdGenerator` and
`TestDatabase` shared by every other test project. Author `Directory.Build.props` (net10.0, nullable, implicit usings,
`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest-recommended`) and
`Directory.Packages.props` (central package management, transitive pinning on). Add `global.json`
pinning the SDK feature band so an SDK patch cannot introduce a new analyzer that reddens CI.

## Done when

- `dotnet build JobHunter.slnx` succeeds with zero warnings.
- No project declares a package version inline — all versions live in `Directory.Packages.props`.
- Project references match the dependency direction in [[../sad|SAD]] §5.
- `tests/Directory.Build.props` imports the root props and sets the Coverlet threshold to 90 (line, branch).
- `.gitignore` covers `.env`, `*.tfvars` (except `.example`), kubeconfigs and `appsettings.Production.json`.

## Out of scope

- Any source file beyond empty placeholders.
- CI workflow (T13).

## Links

[[../sad]] §2, §5 · [[../../../engineering/coding-standards]] §1
