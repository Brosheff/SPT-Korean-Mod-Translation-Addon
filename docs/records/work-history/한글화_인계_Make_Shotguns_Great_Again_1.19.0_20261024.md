# Make Shotguns Great Again! 1.19.0 — 한글화 작업 인계 (2026-10-24)

## A. 작업 기준·버전 증거

- 최신 사용자 요청: **애드온 버전 1.0.4 유지**. 1.0.5로 올리지 않고 번역 소스·업데이트 안내를 갱신합니다.
- 애드온 기준: `SPT-Korean-Mod-Translation-Addon-v1.0.4.zip`.
- 사용자 제공 원본: `makeshotgunsgreatagain.zip`, SHA-256 `fbfd81e0e67562d5ed528e1840b94c670b51eb23c881fed7c9a8b64b91705179`.
- 원본 유형: 서버 `SPT_Runtime/user/mods/makeshotgunsgreatagain/db`의 JSON 데이터 및 `BepInEx/plugins/makeshotgunsgreatagain.dll`의 사전 컴파일된 클라이언트 모듈. 클라이언트 DLL의 FileVersion/ProductVersion은 **1.19.0**. 별도 소스 C# 및 완전한 IL/런타임 F12 생성 호출 결과는 제공되지 않았습니다. 공식 사이트 버전/실게임 설치 버전은 확인하지 않았습니다.
- 기존 번역/F12 `mod_version` **1.15.0 → 1.19.0**. 변경 프로필: `translations/Make Shotguns Great Again!.json`, `config-ui/com.vinihns.makeshotgunsgreatagain.json`.
- 기준 문서: `AGENTS.md`, `docs/모드번역_작업절차.md`, `docs/응답전_필수확인.md`, `docs/번역작업안내서.md`, `docs/TRANSLATION_FORMAT.md`, `docs/prompts/translate_prompt.txt`, `docs/번역대상_인계기록_양식.md`, `docs/배포_업데이트노트_작성지침.md`, `tools/VERIFY_WORKFLOW.md`.

## B. 사용자 표시 후보 전수 분류와 원본 추적

| ID/대상 | 원본 공급 파일 및 기준 | 이전/이번 수량 | 채널·정적 상태 | 실제 적용 |
|---|---|---|---|---|
| 아이템 80종 x Name/ShortName/Description | `db/CustomItems/*.json` 의 `locales.en` | 영어 후보 **240**, 현행 JSON 240개 대응 | `locale`/`ClientLocaleModOverlay` 연동 경로 확인; 원본 영어 **240/240** 일치 | 서버 최종 원문 미확인 |
| 퀘스트 6종의 비어 있지 않은 로케일 | `db/Quests/*.json`의 `locales.en` | 후보 **32**, 기존 번역 32개 포함해 현행 JSON **32/32** | `locale` 채널·정적 일치 | 서버 최종 원문 미확인 |
| 추가 커스텀 로케일 | `db/CustomLocales/en.json` | 비어 있지 않은 후보 **6**, 현행 JSON **6/6** | `locale` 채널·정적 일치 | 게임 런타임 확인 필요 |
| F12 설정 | 클라이언트 DLL의 UTF-16 설정명·설명 문자열 | 기존 **17** + 신규 **7** = **24**; `Debug` 분류 추가 | `config-ui`의 현행 **24/24** 표시명·설명은 DLL 문자열과 정확 일치 | 실제 `Config.Bind` section/key 매핑은 DLL 전용이라 미확인 |
| 기능 고장 관련 동적 알림 | DLL 내 `DisplayMessageNotification`/`DisplayWarningNotification` 및 관련 문자열 | 신규 **14**(템플릿/정확/부분 매치) | 기존 `notifications` 채널과 `DisplayHooks.cs`의 지정 플러그인 GUID 연결 추가 | 수정된 C# DLL 미빌드·인게임 미확인 |
| 로그/내부 식별자·저장값 | DLL 및 JSON | 사용자 표시 경로 미입증 문자열은 제외 | 비대상·기존 시스템 값 불변 | 해당 없음 |

영문 원본 **278개** = 아이템 240 + 퀘스트 32 + 추가 로케일 6. 이전 일반 로케일 번역 **137개**, 신규 **141개**(아이템 138 + 추가 커스텀 3), 현재 일반 로케일 **278개**. `source` 278/278은 **제공된 JSON 원본을 기준으로 한 정적 정확 일치**이며 실서버 locale의 확인 결과가 아닙니다. 이름·단축명 중 모델명/탄종 등 일부 문자열은 번역 규칙에 따라 원문 유지합니다.

**현재 버전 원문 불일치 1개 수정**: `6910e8d90b57259e9dd34f78 Description` 원본 영어가 기존 프로필의 `750mm`가 아닌 현행 JSON의 `725mm`로 변경되어 `source`의 수치를 교정하고 기존 한국어 설명의 `750mm`도 `725mm`로 함께 바로잡았습니다. 그 밖의 기존 한국어는 보존했습니다. 실제 게임이 수신하는 최종 영어 및 같은 키를 덮어쓰는 다른 모드 여부는 추가 확인 필요합니다.

동일 키 공급원 표본: 아이템 Name/ShortName/Description은 사용자 제공 모드 `db/CustomItems/*.json`의 `locales.en`에서, 퀘스트는 `db/Quests/*.json`의 `locales.en`에서, 기타는 `db/CustomLocales/en.json`에서 공급되는 **정적 후보**입니다. 키별 최종 서버 병합·후처리·부모/타 모드 덮어쓰기·실제 선택 순서는 서버 raw locale 및 게임 실행 자료가 없어 전건 **미확인**입니다. `match_mode` 상속과 기존 선택 로직은 변경하지 않았습니다. 근거 없는 `source_variants`는 생성하지 않았습니다.

## C. 구현 범위와 회귀 검사

- `translations/Make Shotguns Great Again!.json`: 신규 로케일 141행, 1개 원문·번역 수치 교정, 기능 고장 알림 14행, `mod_version` 1.19.0.
- `config-ui/com.vinihns.makeshotgunsgreatagain.json`: F12 설정 7행 신설, Debug 분류 1개, 기존 설정 한 항목의 변동된 설명 원문 업데이트, `mod_version` 1.19.0. 저장 `section/key`/선택값은 수정하지 않음.
- `src/DisplayHooks.cs`: `com.vinihns.makeshotgunsgreatagain`에만 `source3.make_shotguns_great_again` 알림 프로필을 연결해 기존 알림 표시 훅을 재사용. 다른 모드 알림의 플러그인 GUID 매핑·처리 코드는 보존.
- 기존 번역 **137행**의 키 보존, 한국어는 **136행 변경 없음 / 1행은 잘못된 수치(750mm→725mm) 교정**. 해당 1행의 원문도 함께 교정했습니다. 다른 모든 애드온 번역 프로필은 원본 ZIP과 바이너리 동일성 검사 대상에 포함했습니다.
- 공통 문서: `docs/번역_원본모드_버전관리대장_20261023.md`, `docs/번역_원본모드_버전_근거목록_20261023.csv`, `note/번역지원모드목록.md`, `note/update.md` 갱신.
- 변경 불필요: `docs/번역작업안내서.md`, 번역 Prompt/포맷, `note/README.md`, 애드온 프로젝트/서버 버전 선언은 모두 1.0.4 유지.

## D. 검사 결과 및 미검증 범위

| 검사 | 실제 결과 |
|---|---|
| 원본 JSON → 프로필 locale 후보 키와 영어 | **278/278 정적 일치**(아이템 240/240, 퀘스트 32/32, 기타 6/6) |
| DLL 원문 문자열 ↔ F12 | **24/24 표시명/설명 문자열 발견**; 실제 설정 속성의 section/key 연결은 미확인 |
| 이전 일반 번역 보존 | **137/137 키 유지**, **136/137 한국어 동일**; 1개 수치 원문·번역 함께 교정 |
| 버전 프로필↔대장↔근거 CSV↔제공 ZIP | **2/2 일치**(두 모드 합계 4/4), 2026-10-24 및 ZIP 해시 기재 |
| JSON·C# 구분자·문서 정적 검사 | `python tools/verify_workflow.py source`, `python tools/validate_translations.py`, `python tools/verify_workflow.py documents` 통과 |
| C# 실제 빌드 | **미실시**(`dotnet` 실행 파일 없음) |
| DLL 교체 후 알림/F12·한국어/영문/언어 전환 | **미확인**(실제 게임 미실행) |
| 신규/기존 설치용 배포 ZIP | **미작성**(소스 ZIP만 생성) |

## E. 남은 작업과 전달

- `DisplayHooks.cs` 추가 GUID 매핑을 **실제 BepInEx 애드온 DLL에 반영하려면 C# 빌드가 필요**합니다. 새 알림 14행의 실게임 출력/호출 스택 매칭은 아직 검증하지 않았습니다. 로그 전용 문구가 실화면에 나타나는지는 미확인입니다.
- 클라이언트 DLL 전용 설정의 `Config.Bind` 저장 section/key 및 설명 생성 경로는 디컴파일/실게임 대조가 필요합니다. DLL에서 문자열이 발견됐다는 근거만으로 설정 UI 적용을 판정하지 않습니다.
- 서버 최종 영어와 다른 모드의 병합 결과(278개), 원본 모드 로딩, 새 설치 및 기존 설치 덮어쓰기, 언어 전환, 실제 표시/인게임 녹화 모두 미확인입니다.
- 별도 배포 빌드 요청이 오면 당시 실제 최신 원본·인계 기록을 재검토하고 컴파일 및 설치 ZIP 실측 검사를 해야 합니다. 이번 결과물은 **전체 수정 소스**이며 애드온 버전은 **1.0.4**입니다.
