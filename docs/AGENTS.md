# SPT Mod Korean Addon — 작업 지침 진입점

> **모든 작업·질문에 적용:** 해당 세션에서 실제 제공된 최신 소스 파일을 확인하고, 문서를 직접 읽으십시오. 과거 대화에서 읽었다는 기억이나 작업 기록의 완료 판정으로 대체하지 않습니다.

## 문서 읽기 순서와 역할 — 단일 진입점

**매 턴마다** 현재 소스의 `docs/AGENTS.md` → [`docs/모드번역_작업절차.md`](모드번역_작업절차.md) 0·0-A절 → [`docs/응답전_필수확인.md`](응답전_필수확인.md)를 읽습니다. 사용자의 요청에 해당하는 전문 지침은 다음 표에서 추가로 확인합니다.

| 담당 | 유일한 기준 문서 |
|---|---|
| 작업 순서·완료 조건 | [`docs/모드번역_작업절차.md`](모드번역_작업절차.md) |
| 오류 증거·실패 판정 | [`docs/응답전_필수확인.md`](응답전_필수확인.md) |
| 로더·번역 구현·검사 방법 | [`docs/번역작업안내서.md`](번역작업안내서.md) |
| JSON 스키마·`mod_version`·`match_mode` | [`docs/TRANSLATION_FORMAT.md`](TRANSLATION_FORMAT.md) |
| 문체·고유명사·화자 | [`docs/prompts/translate_prompt.txt`](prompts/translate_prompt.txt) |
| 인계 기록 작성 **양식** | [`docs/번역대상_인계기록_양식.md`](번역대상_인계기록_양식.md) |
| 공개 업데이트 노트·설치 안내 | [`docs/배포_업데이트노트_작성지침.md`](배포_업데이트노트_작성지침.md) |
| 배포 폴더 규격 | [`docs/DISTRIBUTION_LAYOUT.txt`](DISTRIBUTION_LAYOUT.txt) |
| 실행 가능 검사·미동봉 테스트 | [`docs/tools/VERIFY_WORKFLOW.md`](tools/VERIFY_WORKFLOW.md) |

필요한 경우 관련 원본 코드·번역 JSON·사용자가 제공한 최신 런타임 증거를 직접 대조합니다. 자료가 부족하면 `미확인`으로 표시하며 정적 검사 통과를 인게임 적용 확인으로 표현하지 않습니다. 오류 하나가 확인되면 같은 공통 원인에 영향을 받는 모든 프로필을 조사하고, 확인된 원인 범위만 수정합니다. 세부 검사 조건은 위 전문 문서에서 확인합니다.

## GPT 작업 자료의 유일한 보관 위치 — 강제 규칙

- **`docs/`가 GPT 작업 자료의 단일 관리 루트**입니다. GPT가 생성하거나 사용하는 내부 작업 지시·기술 매뉴얼·번역 프롬프트·조사 증거·검증 도구·작업 기록·수정 내역·참고 자료는 반드시 `docs/` 아래에 저장합니다. **프로젝트 다른 위치에 GPT 작업용 기록·검증 자료·보고서·백업 사본을 생성하지 않습니다.**
- `docs/` 바로 아래에는 **`AGENTS.md`를 포함한 현행 작업 매뉴얼·공통 양식과 `records/`, `reference-data/`, `tools/`, `prompts/` 폴더**만 둡니다. 매뉴얼을 가장한 작업 기록도 금지합니다.
- `docs/records/`: 작업·수정 내역, 차수별 인계, 런타임 증거, 감사·회귀 결과, 버전 관리 대장, 과거 `patches`를 보관합니다. 이력은 현행 지시가 아닙니다. 새 작업 기록은 `docs/records/work-history/`, 감사 자료는 `docs/records/audits/`, 버전 근거는 `docs/records/versioning/`에 저장합니다.
- `docs/reference-data/`: GPT가 참조하는 원본/대사·고정 참조 자료를 보관합니다.
- `docs/tools/`: GPT만 선택적으로 사용하는 조사·문서·인계·이관 도구만 보관합니다. 빌드 필수 검사·NuGet·배포 검사는 루트 `build/`에 둡니다. 이후 새 검사 도구도 여기에 작성합니다.
- `docs/prompts/`: 현재 사용 중인 번역 지침 등 GPT 프롬프트를 보관합니다.
- `note/`는 **일반 사용자에게 공개하는 문서만** 저장하며 작업 기록이 아닙니다. 번역·실행 코드 및 데이터(`src/`, `translations/`, `config-ui/` 등)와 프로젝트 빌드 스크립트는 기존 위치를 유지합니다. **GPT 지침 진입점은 오직 `docs/AGENTS.md`에 두며 프로젝트 최상위에 별도 `AGENTS.md`를 만들지 않습니다.**
- GPT가 만들어 낸 보고서·CSV·JSON 증거를 프로젝트 루트, `note/`, 코드/데이터 폴더, `_dist/` 등에 남기는 것을 금지합니다. `_dist/`에는 최종 설치 배포 산출물만 허용됩니다. 일시적인 작업용 파일은 저장소 밖 임시 공간에서 처리하고 프로젝트 결과물에는 포함하지 않습니다.

**빌드 차단과 문서 정리는 별개입니다.** `BUILD_DISTRIBUTION.cmd`는 현행 빌드 입력만 검사합니다. `Upload/`, 과거 릴리스·백업·외부 원본 등은 GPT 자료 위치 검사와 빌드 차단 대상이 아닙니다. GPT 저장 위치 규칙은 명시적으로 `python docs/tools/verify_workflow.py documents`를 실행할 때 현행 `docs/`와 정해진 루트 폴더만 대상으로 검사합니다. 자세한 범위는 [`docs/tools/VERIFY_WORKFLOW.md`](tools/VERIFY_WORKFLOW.md)를 따릅니다.

**기존 소스 ZIP에 덮어쓰기만 하면 과거 최상위 `tools/`, `records/`, `reference-data/`와 `docs/patches/`가 남을 수 있습니다.** `python docs/tools/migrate_docs_records.py`로 보존 이관한 뒤 `python docs/tools/verify_workflow.py documents`로 작업 자료를 검사하고, 별도로 `python build/verify_release.py source`로 현재 빌드 입력을 확인합니다. 이관 도구는 기존 파일을 조용히 폐기하지 않으며, 동일 이름의 서로 다른 내용은 `docs/records/legacy-imports/`에 보존합니다.

**사용자의 최신 지시가 최우선**입니다. 조사/검증 도구를 실행했다는 사실만으로 인게임 적용 성공을 주장하지 않습니다.

**독립성 강제:** `docs/`가 없어도 `BUILD_DISTRIBUTION.cmd`의 검사·컴파일·배포가 동작해야 합니다. 현재 빌드 도구는 `build/`에만 둡니다.
