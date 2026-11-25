# Repository instructions

- Push only to repositories owned by the GitHub account `SeelesToy`. Verify every effective push URL before pushing. Do not push to another owner.
- The main repository is `SeelesToy/GameBaseFramework` and directly tracks `Client/`.
- `Config/` is a submodule of `SeelesToy/GameBaseFramework-Config`.
- `CDNFile/` is a submodule of `SeelesToy/GameBaseFramework-CDNFile`.
- Push a changed submodule first, then commit its updated pointer in the main repository. Use `git push --recurse-submodules=check` for the main repository.
- Keep Unity Assets and their .meta files, Packages, and ProjectSettings under version control. Keep generated caches and build output ignored.
- Keep required binary dependencies, including the GameFramework libraries and the Luban tools.
