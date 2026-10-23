# 번역-배포 검증 도구 사용법 (개발용)

`tools/verify_workflow.py`는 번역 JSON, 기존 C# 훅, 모드 원본을 수정하지 않고 **정적 오류를 감지**합니다. 기존 `validate_translations.py`, `verify_quartermaster_contracts.py`, `build_supported_mods.py`를 대체하지 않습니다.

## 자동 빌드 검사

`BUILD_DISTRIBUTION.ps1`은 아래 순서로 진행합니다.

1. `references/client`, `references/server`의 필수 참조 DLL 목록 확인(누락된 DLL이 있으면 빌드 중단). 문서 파일의 자동 이동·삭제는 수행하지 않음
2. `verify_workflow.py source` — 규격 외 루트 `extras`, 잘못된 F12 GUID, 중복 설정/카테고리, 누락된 F12 번역명·설명·선택값, 중복 `mod_id` 검사
3. 기존 번역/계약/지원 목록 검사
4. **새 번역 회차에서 후보 목록과 인계 기록을 전달한 경우** 두 파일 대조, 미연결 후보 차단
5. 클라이언트/서버 DLL 빌드
6. 설치용 데이터만 스테이징 (`translations/*.json`, `config-ui/*.json`, 기존 `native-locales`/`contract-locales`, 양측 DLL)
7. 배포 ZIP 생성 후 `verify_workflow.py distribution` — 원본과 번역 JSON의 바이트 일치, DLL 존재, 스테이징/ZIP 동일성, 무단 파일(노트·소스·`extras`) 차단

**빌드 스크립트는 번역·C#·모드 원본·개발 문서를 이동하거나 삭제하지 않습니다.** 배포 ZIP에는 지정된 런타임 JSON과 새로 빌드한 클라이언트·서버 DLL만 넣습니다. `config-ui/README.md` 같은 개발 문서는 포함하지 않습니다.

## 분석-구현 역대조(신규 번역 회차)

새 모드의 원본 소스에서 발견한 사용자 표시 후보를 `candidate_inventory.json`의 `candidate_ids` 목록으로 기록하고, 같은 ID를 `handoff.json`의 `targets`에 기록합니다. 두 파일은 **회차별 임시 작업 기록이며 소스/배포 ZIP에 넣지 않습니다.**

```json
{
  "candidate_ids": ["example:item-name", "example:f12-setting", "example:ui"]
}
```

```json
{
  "schema_version": 1,
  "targets": [
    {"id": "example:item-name", "kind": "locale", "status": "translated", "evidence": {"file": "translations/Example.json", "channel": "locale", "key": "ITEM_ID Name", "source": "Original name"}},
    {"id": "example:f12-setting", "kind": "f12_entry", "status": "translated", "evidence": {"file": "config-ui/example.guid.json", "section": "General", "key": "Enable", "source": "Enable"}},
    {"id": "example:ui", "kind": "hook", "status": "translated", "evidence": {"file": "src/SomeUiHooks.cs", "tokens": ["UIClass", "Original text"], "registration": {"file": "src/Plugin.cs", "tokens": ["SomeUiHooks.Enable"]}}}
  ]
}
```

위 값들은 가상 예시이며 실제 모드의 키로 대체해야 합니다. 검사는 `kind`별로 다음 증거를 요구합니다.

- `locale`: 정확한 `translations/*.json`의 채널, key, (제공된 경우) source, 번역 본문
- `channel_row`: 채널과 `field`(id/key/source), `selector` 및 번역 본문
- `f12_entry`, `f12_category`, `f12_value`: 설정 section/key, category source 또는 values source와 한국어 번역
- `native_locale`: 기존 `native-locales`의 파일 및 키
- `hook`: **애드온 내부** C# 훅 파일의 식별 토큰과 별도 등록 코드의 식별 토큰 (문자열 존재 확인만 가능)
- 아직 적용하지 않는 대상: `source_kept`, `non_target`, `blocked`와 구체적 `reason`. `blocked`는 `--require-complete`가 있을 때 릴리스 준비 검사를 실패시킵니다.

검사 명령:

```text
python tools/verify_workflow.py handoff --manifest handoff.json --inventory candidate_inventory.json --require-complete
```

빌드 명령에는 기존 인자와 함께 `-HandoffManifest <경로> -CandidateInventory <경로>`를 전달할 수 있습니다. 둘 중 하나만 전달하면 빌드가 중단됩니다.

**중요:** 이 검사로 모드 원본의 모든 문자열을 새로 발견하지는 못합니다. 누락 검사 범위는 원본 분석에서 독립적으로 작성한 `candidate_ids`에 한정됩니다. 기록 없이 빌드하면 기존 구조 검사는 실행되지만 번역 후보 전수검사를 통과했다고 주장할 수 없습니다. 소스의 훅 문자열·등록 토큰 존재는 런타임 작동의 증거가 아닙니다.

## 독립 실행 / 오류 재현 시험

```text
python tools/verify_workflow.py source
python tools/verify_workflow.py documents
python tools/verify_workflow.py distribution --stage <스테이징 폴더> --zip <배포 ZIP>
```

실패 시 검사 메시지를 보고 원본 후보/프로필/훅을 다시 확인합니다. 통과하더라도 실제 SPT DLL 컴파일과 인게임 표시 확인은 별도입니다.

## 미동봉 테스트 현황과 검사 상태 기록

현재 제공된 **1.0.4 소스 ZIP에는 `tests/` 디렉터리가 없습니다.** 아래는 과거 지침에 등장하는 테스트 경로이며 이 소스만으로는 실행할 수 없습니다.

| 테스트 경로 | 현재 상태 | 조치 |
|---|---|---|
| `tests/LocaleDisplayContract` | 미동봉 | 테스트 원본이 실제 제공된 경우에만 C# 빌드·실행; 없으면 `미실시(원본 없음)` |
| `tests/ServerDisplayContract` | 미동봉 | 동일. 서버 메시지 정적 검사를 계약 테스트 성공으로 대체하지 않음 |
| `tests/CasinoWriteContract` | 미동봉 | 동일. 카지노 UI 출력 순서의 인게임 정상 여부는 별도로 검증 |
| `tests/WorkflowChecks` | 미동봉 | Python unittest를 실행한 것처럼 보고하지 않음 |

**현재 실행 가능:** `python tools/verify_workflow.py source`, `python tools/verify_workflow.py documents`, `python tools/validate_translations.py`, `python tools/verify_quartermaster_contracts.py`, `python tools/build_supported_mods.py`. 사용 전 각 명령의 인자와 현재 소스 내용을 확인한다. 이들은 계약 테스트나 게임 실행의 대체 증거가 아니다. 파일·경로 참조가 누락되면 `documents` 검사에서 오류를 내도록 관리한다.

## 응답 전 필수 문서 게이트

프로젝트에 관한 **매 응답 전에** `AGENTS.md`와 `docs/모드번역_작업절차.md` 0·0-A절,
`docs/응답전_필수확인.md`를 직접 읽는다. 번역 설명·오류 분석은 해당 로더 소스,
원본 모드, 최신 JSON, 게임 로그/스크린샷까지 교차 확인해야 한다.

```text
python tools/verify_workflow.py documents
```

`documents` 검사는 현행 문서의 존재·중앙 목록 등록·문서 링크·미동봉 테스트 안내 등 **정적 문서 정합성**을 확인한다. 실제로 답변마다 문서를 열어 읽었는지,
게임에서 번역이 표시됐는지를 자동 검증하지 못한다. 사용자가 게임에서 영어 아이템을 제시하면
정적 검사 통과 사실을 우선하지 말고, `VSK` vs `VKS`처럼 **현재 런타임 원문·키와 번역 프로필
`source`·상속 `match_mode`·동일 키 충돌을 확인**한다. 하나의 항목을 고쳤다는 사실만으로 전체
번역 적용 성공을 선언하지 않는다.
