# 03-validation: Validate the complete upgraded solution

Verify the full solution build and run the unit and integration test suites on .NET 10. Audit the resolved dependency graph for vulnerabilities and remaining actionable framework/package conflicts, and check tracked files for stale .NET 8 build or deployment instructions.

Document external service limitations explicitly rather than treating unexecuted tests as passing. Record the final validation evidence and any deferred recommendations without expanding the scope to unrelated modernization.

**Done when**: Full solution build is warning-free, test outcomes and environmental limitations are recorded, package audit is reviewed, and no unintended old runtime targets remain.

## Research and Validation Scope
- Prior upgrade task produced a warning-free .NET 10 solution build and 320 passing unit tests.
- Full integration suite was attempted: 12 passed and all 374 failures were classified from TRX as DockerUnavailableException, with no non-Docker failures. Docker must be started for meaningful container-backed validation.
- Review all nine package lockfiles and run per-project vulnerability audit because solution-level package listing stops at the JavaScript project.
- Verify locked-mode restore, fresh full-solution build, current framework/deployment references, documentation checks, and git diff whitespace.
- Preserve historical performance baseline entries. No application changes planned unless validation finds a concrete problem.
