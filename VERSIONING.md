# Versioning

이 저장소는 `MAJOR.MINOR.PATCH` 형식을 사용합니다.

- 초기 버전: `0.1.0`
- 0.1 개발선의 버그 수정과 소규모 개선: `0.1.1`, `0.1.2`, `0.1.3` …
- 사용자 동작이나 데이터 의미가 달라지는 다음 개발선: `0.2.0`
- 안정판 호환성을 보장하기 시작하는 시점: `1.0.0`

Git tag와 GitHub Release에는 `v` 접두사를 붙입니다. 예: `v0.1.0`.

버전을 올릴 때 다음 항목을 함께 변경합니다.

1. `Directory.Build.props`의 `Version`, `VersionPrefix`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
2. `README.md`의 현재 버전
3. `CHANGELOG.md`
4. Git tag와 GitHub Release
