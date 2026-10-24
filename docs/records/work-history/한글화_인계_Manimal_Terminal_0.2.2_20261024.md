# Manimal Terminal 0.2.2 — 한글화 인계 (2026-10-24)

## 유지 조건

- **애드온 버전은 1.0.4 고정**. `SPT_Mod_Korean_Addon.csproj` 등의 버전값 변경 금지.
- `note/update.md`에 남아 있던 이전 업데이트 내용은 삭제·축약·재작성하지 않았습니다. 이번 모드 지원 사실만 추가했습니다.
- 이번 작업은 **기존 애드온을 이어서 수정한 전체 소스**이며 원본 Manimal Terminal 모드 파일을 수정하지 않았습니다. 바이너리 재빌드·게임 실행 검증은 별도 필요합니다.

## 제공된 근거와 모드 버전

- 원본: `ManimalTerminal-master.zip` (사용자 제공).
- SHA-256: `14a9a64f0ea6dbfd611658c330207be2b68139f28b8183337d910ab329d3d318`.
- `Directory.Build.props`의 `ModVersion=0.2.2`와 README의 `Version 0.2.2`를 대조했습니다.
- README에는 SPT 4.1을 대상으로 하며 **SPT 4.1.5 기준 컴파일**되었다고 적혀 있습니다. 애드온 대상인 **SPT 4.1.6 실제 실행 가능 여부는 미확인**입니다.
- 클라이언트 GUID `com.manimal.terminal`, 어셈블리 `ManimalTerminalClient` (`terminal-client/terminal-client.csproj`).

## 신규 번역 데이터

- `translations/Manimal Terminal.json`: **일반 로케일 171개** 및 **전용 UI 83개**, 게임 알림 **4개**.
  - 로케일: Prapor 중심 퀘스트 로케일 101개(퀘스트 14개), Lightkeeper 커스텀 대화 로케일 39개, 터미널 지도·배너 8개, 컨테이너 18개, 장비 캐비닛 3개, 업적 2개.
  - UI: 컷신 자막 4개 시퀀스(개별 표시 49회 중 인트로 공통 16문장 공유), 확성기 단독 자막, 에필로그 원본 JSON·버튼·통계·보상 라벨, 터미널 전용 펌프장 및 게이트 작업 문구 10개.
  - 알림: 장비 압수 1개, 전력 복구 및 고장 3개.
- `config-ui/com.manimal.terminal.json`: 원본 `terminal-client/Plugin.cs`에 있는 `Config.Bind(...)` **105개**의 한국어 이름·설명을 대응시켰습니다. 설정값·키·섹션을 변경하지 않았습니다.
- 원문의 key, GUID, 부품 ID, 구조화된 퀘스트 목표 ID, 컷신 식별자, 시스템 변수 등은 변경하지 않았습니다.

## 실제 출력 경로와 코드 변경

- `src/ManimalTerminalUiHooks.cs` 신규: `ManimalTerminalClient` 어셈블리의 이름과 매개변수 수가 검토된 전용 메서드 **10개에만** Harmony 훅을 설치합니다.
  - `TerminalSubtitles.DrawPanel`: 컷신·단독 확성기 자막이 공유하는 최종 그리기 메서드.
  - `TerminalEpilogueScreen.EpilogueRunner.LoadConfig`: 런타임의 에필로그 JSON 객체만 변경합니다. `kind`, `type`, `id`, 이미지·오디오 경로는 절대 변경하지 않으며 보상 종류 `Item/Dogtag`에 따른 UI 상태를 보존합니다.
  - `EpilogueRunner.MakeText`, `CloneShellButton`, `TryClonePrestigeRewardCard`, `BuildFallbackRewardCard`, `UpdateButtonVisibility`: 텍스트 생성·보상 카드·NEXT/OK 버튼의 실제 화면 레이블.
  - `Patch_PumpSwitchActions.Postfix`, `Patch_GateSwitchActions.Postfix`: 터미널 전용 상호작용 목록 생성 이후 `Name`만 번역합니다. 실행 델리게이트와 잠금 상태는 건드리지 않습니다.
  - `GateHoldSession.Update`: 자신이 표시할 진행 패널 문구만 번역합니다.
- `src/SmallUiLiteralHooks.cs`: 기존 수명주기·어셈블리 지연 로딩 처리에 해당 모드만 연결합니다. 게임 전체 UI 훅은 추가하지 않았습니다.
- `src/DisplayHooks.cs`: 기존 알림 호출자 GUID → 번역 프로필 매핑에 `com.manimal.terminal`을 추가했습니다.
- `tools/build_supported_mods.py`: 기존 지원 목록 생성기의 GUID 매칭 정보에 새 모드를 추가했습니다. 이전 매핑 변경 없음.

## 업데이트 기록

- `note/update.md`: 이전 1.0.4 릴리스 업데이트를 그대로 두고 신규 모드 지원 한 줄만 추가.
- `note/번역지원모드목록.md`: 지원 목록 생성기로 갱신. 166개 → **167개** 항목.
- `docs/번역_원본모드_버전관리대장_20261023.md`: 일반 번역 및 F12 표에 각 1개 신규 행을 삽입하고 이번 작업 근거를 기록.
- `docs/번역_원본모드_버전_근거목록_20261023.csv`: 종전 228행을 보존하고 새 근거 2행만 추가.

## 확인 및 미확인 구분

- 확인: 원본 영문 키·문장과 한글 번역 JSON을 1:1 매핑, 퀘스트 101개, 대화 39개, 컷신 49개 표시 시점, F12 설정 105개, 항목 ID 충돌 검사, 1.0.4 버전 유지, 예전 업데이트 기록 보존.
- 실행한 검사: `tools/validate_translations.py`, `tools/verify_workflow.py source`, `tools/verify_workflow.py documents`, `tools/verify_quartermaster_contracts.py`, `tools/build_supported_mods.py`, ZIP 무결성 검사.
- **미확인**: .NET SDK 미설치로 C# 빌드 미실시, SPT 4.1.6 클라이언트/서버·BepInEx·WTT CommonLib 등 게임 환경 부재로 인게임 표시 미검증. 모드의 원본 SPT 4.1.5 참조와 애드온 4.1.6 호환성은 직접 검증 필요.
- 커스텀 등대지기 대화는 원본 WTT CommonLib의 등록 로직을 확인해 로케일 키를 추가했지만, SPT 서버에서 최종 글로벌 로케일이 노출되는 경로는 실행 검증하지 않았습니다.
- 모드의 개발자 진단 로그·디버그 자료 전체를 번역 대상으로 삼지 않았습니다. UI에 출력되는 실제 사용자 문구를 우선 번역했습니다.


## 2026-10-24 추가 검수 및 번역 오류 보정

- 검수 기준: 저장소 `AGENTS.md` → `모드번역_작업절차.md` 0·0-A → `응답전_필수확인.md`와 `번역작업안내서.md`, `TRANSLATION_FORMAT.md`, `prompts/translate_prompt.txt`. 원본 Manimal Terminal 0.2.2 퀘스트 정의·`DialogSide`·F12 `Config.Bind` 및 컷신·상호작용 소스와 대조. 프라퍼 말투는 제공된 `docs/prompts/vanilla_trader_dialogue_reference.json`의 해당 상인 대사 참고. 메카닉·등대지기의 현행 바닐라 대화 자료는 별도로 제공되지 않아 완전 일치 여부 미확인.
- `translations/Manimal Terminal.json`: 로케일 12행, 독립 UI 1행 번역 본문 교정. 플레이어 찾는 대상에 관한 원문 오역, `prove yourself`의 문법 오역, Prapor의 군 개입 묘사, 메카닉 말투, 제3자 안내문의 설명체, 등대지기 말투 혼재 및 엔딩의 비자발적 사건 연루 뉘앙스 등을 수정.
- `config-ui/com.manimal.terminal.json`: 설명 11건 원문 의미·조건·수치 복구. 봇 생성 보류 조건의 오역을 바로잡아 `공격 컷신이 끝날 때까지`로 수정하고, 누락된 조명·시간·AI 수·성능 수치 및 `보xel` 오탈자를 교정.
- **불변 조건:** 애드온 버전 1.0.4, 원본 모드 버전 0.2.2, 모든 프로필 `source`·키·ID·display_type·match_mode·GUID·F12 저장 항목·코드 훅 유지. 이번 교정은 기존 공개 노트에 개발 과정의 수정 항목으로 별도 누적하지 않음.
- **검증 제한:** JSON 및 정적 검사를 수행한 범위에서만 통과 판정. C# 빌드, 설치용 배포, 실제 게임 언어/화자 렌더링, 부모 4.1.6 메카닉·등대지기의 현행 말투 검증은 미실시. 문장 품질에 대한 정적 재검토는 추가 오역이 없다는 증명이 아님.
