# Incendiary Grenade 1.1.0 — 한글화 작업 인계 (2026-10-24)

## A. 작업 기준 및 버전 근거

- 최신 사용자 요청: **SPT Mod Korean Addon 버전은 1.0.4로 고정**하고 번역·대응 내역과 안내만 업데이트합니다. 1.0.5 변경 없음.
- 애드온 기준: `SPT-Korean-Mod-Translation-Addon-v1.0.4.zip`. 기존 추출 프로젝트 구조를 보존합니다.
- 원본: 사용자가 제공한 `Incendiary-Grenade-main.zip`; SHA-256 `36e21bce7383641fd675bc7910b53ed29f3d717e4663a3376430ffe41844f7ec`.
- 원본 코드: `Client/IncendiaryGrenade.csproj`, `Client/Plugin/IncendiaryPlugin.cs`, `Client/Plugin/IncendiaryConfig.cs`, `Server/Saga6Server.cs` 및 관련 클라이언트·서버 C# 코드. `csproj`/BepInPlugin 선언에서 모드 버전 **1.1.0** 확인. 별도의 사이트 최신 버전 조회·실게임 설치 버전 확인은 미실시.
- 기존 두 프로필의 `mod_version`: **1.0.2 → 1.1.0**. JSON의 기존 번역 본문과 키·원문/설정 본문은 유지했습니다.
- 대상: `translations/Incendiary Grenade.json`, `config-ui/com.sage.incendiarygrenade.json`.
- 현재 확인 문서: `AGENTS.md`, `docs/모드번역_작업절차.md`, `docs/응답전_필수확인.md`, `docs/번역작업안내서.md`, `docs/TRANSLATION_FORMAT.md`, `docs/prompts/translate_prompt.txt`, `docs/번역대상_인계기록_양식.md`, `docs/배포_업데이트노트_작성지침.md`, `tools/VERIFY_WORKFLOW.md`.

## B. 원본 표시 후보 및 공급 경로 대조

| 표시 대상 | 원본 공급원 | 기존 번역 / 신규 작업 | 연결 경로 / 검증 상태 |
|---|---|---|---|
| SAGA-6 소이 수류탄 이름·단축명·설명 | `Server/Saga6Server.cs`의 서버 아이템 로케일 | 기존 3개 문자열 유지; 1.1.0 원본 코드 영어 상수와 정적 대조 | 기존 `locale` 채널과 `ClientLocaleModOverlay` 연결 확인; 실행 서버 최종 문자열은 미확인 |
| 화염 범위 F12 설정 | `Client/Plugin/IncendiaryConfig.cs`의 BepInEx 설정 | 기존 이름·설명 번역 유지 | 기존 `config-ui` 표준 F12 번역 경로; 실제 UI 적용 미확인 |
| BigBrain 누락 시 시작 오류 MessageBox | `Client/Plugin/IncendiaryPlugin.cs`의 Win32 메시지 경로 | **보류**: 직접 Windows MessageBox이며 기존 일반 F12/locale 훅 대상 아님 | 임의 신규 전역 패치 미작성; 런타임 조건·후킹 지점 및 게임 부팅 순서 확인 필요 |
| 일반 로그·내부 상수/식별자 | 원본 여러 C# 파일 | 비대상: 게임 사용자 표시 번역으로 입증되지 않음 | 제외 사유 기록 |

동일 로케일 키의 서버 등록·후처리·다른 모드 병합이 만든 **실제 최종 서버 영어**를 수집하지 못했습니다. 정적 소스에 존재하는 원문을 실제 출력 검증으로 간주하지 않습니다. 기본 `match_mode`의 원문 비교 및 다른 모드 후보 선택은 애드온 소스·정적 검사 범위에서만 검토했습니다.

## C. 수정 범위, 회귀 및 문서

- 직접 수정: 두 프로필의 기록용 `mod_version` 메타데이터 1.1.0 설정. 기존 번역 데이터·원문·설정 `section/key`·GUID와 원본 모드 소스는 그대로 유지했습니다.
- 공통 문서 갱신: `docs/번역_원본모드_버전관리대장_20261023.md`, `docs/번역_원본모드_버전_근거목록_20261023.csv`(확인일/ZIP 해시 포함), `note/번역지원모드목록.md`, `note/update.md`.
- 불필요하여 유지: `docs/번역작업안내서.md`, 번역 Prompt/포맷, `note/README.md`, 애드온 1.0.4 버전 필드/프로젝트 설정.
- 전체 애드온 정적 검사: `python tools/verify_workflow.py source`, `python tools/validate_translations.py`, `python tools/verify_workflow.py documents`로 확인. 실제 SPT 또는 C# 빌드 테스트는 수행하지 못했습니다.
- 번역/F12 프로필 버전 기록 두 건 모두 JSON↔버전 대장↔근거 CSV와 일치하며 사용자 제공 원본 ZIP의 SHA-256과 비교했습니다.

## D. 미확인·미완료 / 다음 작업 기준

- **실제 원본 모드 게임 적용 및 SPT 서버의 raw locale/게임 화면 스크린샷 미확보**. 원본 1.1.0과 게임 버전의 런타임 호환 여부도 미확인.
- BigBrain 부재 경고의 OS MessageBox는 한글화 연결 **미완료**. 사용자 표시가 확인되면 시작 시점 동작을 선행 조사하고 애드온 내 좁은 경로만 검토해야 합니다.
- 번역 표시, 언어 전환, 빈 설치/기존 설치 덮어쓰기, 실제 DLL 빌드/게임 실행은 미검증.
- 전달 결과는 **수정 소스**이며 설치/배포용 빌드가 아닙니다. 신규 대응 정보가 `mod_version` 변경만으로 실제 설치 호환성을 증명하지 않습니다.
