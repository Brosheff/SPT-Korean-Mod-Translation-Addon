# GPT 선택 검사 도구 안내

`docs/tools/`은 GPT가 작업을 조사·기록·감사할 때 사용하는 도구만 보관합니다.
**이 폴더와 `docs/` 전체는 빌드에 필수가 아니며, 빌드 스크립트는 여기 파일을 읽지 않습니다.**

## 일반 빌드와 배포

프로젝트 루트의 `BUILD_DISTRIBUTION.cmd`가 `BUILD_DISTRIBUTION.ps1`을 호출합니다.
독립된 `build/` 폴더에는 필수 정적 검사·번역 검증·지원 목록 생성·배포 무결성 검사가 있습니다.

```text
python build/verify_release.py source
python build/validate_translations.py
python build/verify_quartermaster_contracts.py
python build/build_supported_mods.py
python build/verify_release.py distribution --stage <스테이징 폴더> --zip <배포 ZIP>
```

빌드 도중 NuGet은 온라인 패키지 소스를 사용하지 않도록 운영체제 임시 위치에 설정을 생성하고 종료 시 정리합니다. `docs`에 NuGet 설정이 없어도 됩니다.
빌드 도구는 현행 소스·JSON·참조 DLL·배포 대상만 검사하고, 과거 `Upload/`·작업 기록·기타 백업을 검사하지 않습니다.

빌드 사전 검사(`build/verify_release.py source`)는 C# 전체 소스에서 프로젝트가 참조하지 않는 `EFT.*` 게임 타입의 직접 사용도 감지합니다. 게임 타입을 다뤄야 하는 모드별 훅은 실제 원본 메서드와 매개변수를 조사하고 범위가 제한된 리플렉션/Harmony 인자 주입을 사용합니다. **이 사전 검사는 C# 실제 컴파일을 대체하지 않습니다.**

## GPT 작업 전용 검사 (일반 빌드와 무관)

```text
python docs/tools/verify_workflow.py documents
python docs/tools/verify_workflow.py handoff --manifest <매니페스트.json> --inventory <후보목록.json> --require-complete
```

- `documents`: 현행 매뉴얼, 경로, 링크, 저장 위치 검사
- `handoff`: 독립적으로 작성한 후보 목록과 번역·코드 연결 증거 대조
- 과거 기록/참고 파일과 GPT 조사 도구는 `docs/records/`, `docs/reference-data/`, `docs/tools/`에서 관리합니다.
- 프로젝트 내부 감사 보고서는 `docs/records/` 안에 저장합니다.
- `tests/LocaleDisplayContract`, `tests/ServerDisplayContract`, `tests/CasinoWriteContract`, `tests/WorkflowChecks`는 현재 ZIP에 없습니다. 이 테스트를 실행했다고 보고하면 안 됩니다.

**실게임 표시 확인과 C# 컴파일은 별도 검증 단계입니다.** 정적 검사 통과만으로 게임 적용 완료라고 보고하지 않습니다.

## 기존 소스 폴더에 덮어쓴 경우

ZIP 덮어쓰기는 이전 `docs/tools/`의 빌드 파일을 삭제하지 않습니다. 일반 빌드는 이에 영향을 받지 않지만 GPT 작업 자료를 정리하려면 별도로 다음 명령을 실행합니다.

```text
python docs/tools/migrate_docs_records.py
python docs/tools/verify_workflow.py documents
```

기존 `NuGet.Offline.Config`와 구형 빌드용 검사 프로그램은 삭제하지 않고 `docs/records/legacy-imports/old-build-tools/`로 보관합니다. **빌드 CMD가 이 이관 작업을 자동 실행하지는 않습니다.**
