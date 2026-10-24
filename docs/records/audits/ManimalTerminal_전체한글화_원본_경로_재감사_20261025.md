# Manimal Terminal 0.2.2 전체 한글화 재감사 — 2026-10-25

## 범위·증거

- 기준 애드온: 직전 `Manimal-Quest-Locale-Repair-Reviewed-Source` 전체 소스 (1.0.4).
- 기준 모드: 사용자가 제공한 `ManimalTerminal-master.zip` (원본 README 및 프로젝트 0.2.2).
- 추가 조사: 기존 사용자 제공 MOD SOUCES 안의 `WTT-CommonLib-3.0.6.zip`; `WTTCustomQuestService.ImportLocaleData`, `WTTCustomDialogueService.CreateCustomDialogues` 확인.
- 실제 게임의 DLL/서버 응답 스냅샷/인게임 전 항목 화면은 **미제공**, .NET SDK가 없어 DLL 컴파일 **미실시**.

## 네 갈래 이상의 표시 경로 전수 검사

| 경로 | 원본 데이터 확인 | 번역 프로필 현황 | 적용 방식/검증 한계 |
|---|---:|---:|---|
| 퀘스트 글로벌 로케일 | 101 | 정확한 키·원문 101 | WTT en fallback, 서버 locale + 기존 클라이언트 overlay; 런타임 미검증 |
| 업적 글로벌 로케일 | 2 | 2 | 서버 locale + 클라이언트 overlay; 런타임 미검증 |
| 보관함 글로벌 로케일 | 18 | 18 | 원본 TerminalMod 글로벌 transformer; 런타임 미검증 |
| 지도 이름·설명 | 8 | 8 | 원본 TerminalMod 글로벌 transformer; 런타임 미검증 |
| 신규 장비 캐비닛 이름·설명 | 3 | 3 | 원본 TerminalMod 글로벌 transformer; 런타임 미검증 |
| Lightkeeper 전용 대화 | 39 | 39 | **별도 `localization.en` 저장소**. 기존 글로벌 번역만으로 해결되지 않음 |
| 컷신 시간표 자막 | 고유 원문 33 | 33 | `TerminalSubtitles.DrawPanel` 전용 Harmony 훅; 런타임 미검증 |
| 독립 방송 | 1 | 1 | 같은 자막 훅; 런타임 미검증 |
| 에필로그 리소스 문자열 | 고유 17 | 17 | `EpilogueRunner.LoadConfig`, 텍스트 메서드 훅; 런타임 미검증 |
| 자체 UI/상호작용 포함 | small_ui 총 83 | 83 | 원본 10개 메서드 연결 검토. 이벤트/언어 재설정/폰트 미검증 |
| EFT 알림 | 4 | 4 | GUID 스택 범위의 알림 훅; 런타임 미검증 |
| F12 설정 | Config.Bind 105 | 105 | ConfigurationManager 전용 훅; 런타임 미검증 |

**글로벌 locale 프로필 전체 171개** = 퀘스트 101 + 업적 2 + 보관함 18 + 지도 8 + 장비 캐비닛 3 + 별도 대사 39. 원본 대비 대응하는 키 누락 또는 원문 불일치 0건. 다른 번역 프로필의 동일 locale 키 충돌 0건(현재 114개 프로필의 정적 대조).

`terminal_subtitles.json`은 원본 모드 전용 자막 전체가 아니라 원작 영어 언어 덤프입니다. 실제 컷신 표시는 `terminal_subtitle_timings.json`의 33개 고유 원문과 독립 방송을 기준으로 검증했습니다. 관련 없는 원작 일반 로케일 수백 개를 모드의 한글화 누락이라고 잘못 판정하지 않았습니다.

## 확인된 구조 결함 및 수정

1. 원본 WTT `ImportLocaleData`는 `en.json` fallback을 한국어를 포함한 모든 로케일에 공급합니다. 이전의 `seed_missing`은 키 자체가 없을 때만 작동하므로, **영어 값으로 채워진 글로벌 로케일 또는 로더 미호출**을 해결하는 보장이 아닙니다.
2. 원본 WTT `CreateCustomDialogues`는 Lightkeeper의 영어 대사를 `TemplateTable.Dialogue.Elements`에 등록합니다. 별도의 `localization.en` 객체이며 `LocaleTable.Global`로 넣지 않습니다. 따라서 `ClientLocaleModOverlay`의 39개 `locale` 항목은 해당 대사를 직접 변경하지 못합니다.
3. `server-src/SPT_Mod_Korean_Server/ManimalTerminalLocales.cs`를 추가했습니다. 원본의 Preload 등록이 끝난 다음, `LocaleTable.Global`의 **정확한 기존 영어 원문**만 서버 한국어로 바꾸고, 특정 두 대화 객체에만 별도 언어 사전을 추가합니다. 다른 모드의 로케일, 이미 존재하는 한국어 및 영어 문화는 보존합니다. 예상 동작이며 게임에서 확인된 사실이 아닙니다.
4. 글로벌, 대화, 클라이언트 자막·UI, 알림, F12를 별도 경로로 감사하는 `docs/tools/verify_manimal_full_scope.py`를 추가했습니다. **빌드 CMD와 `docs`의 분리는 유지**합니다.

## 부작용·예외 검토

- 원본 Manimal이 로드되지 않았거나 특정 키의 영어 원문이 바뀌면 서버 번역을 강제하지 않습니다. 다른 번역이 존재하면 유지합니다.
- `kr` 또는 `kr-en` 서버 글로벌 로케일이 존재하지 않는 설치 구성은 서버 변환기가 등록되지 않을 수 있습니다. 클라이언트 방식만 남으며 로그 경고를 통해 확인해야 합니다.
- WTT 대화 모델의 `Localization` 구조 또는 언어 선택 코드가 변경되면 전용 대사 번역은 여전히 동작하지 않을 수 있습니다. 모델 반사 접근 실패는 경고로 남기고 다른 서버 기능을 손상시키지 않습니다.
- 서버에서 한국어로 공급한 값은 기존 클라이언트의 정확한 `source` 일치 검사에 더 이상 해당하지 않으므로 이중 한영병기 합성을 피합니다. 언어 전환·캐시 갱신·글꼴의 한국어 렌더링은 실제 게임에서 확인해야 합니다.
- `ManimalTerminalUiHooks`는 10개 메서드를 함께 검사하므로 한 훅 대상 불일치 시 자체 UI 가족 전체가 미적용될 가능성이 있습니다. 현재 제공한 0.2.2 원본의 명칭·매개변수 수는 일치하나 **실제 설치 DLL 검증은 아님**. 후속 런타임 로그로 확인해야 합니다.

## 수행한 검증과 금지된 완료 판단

- 원본 코드·WTT 로더·JSON 양방향 대조 완료. Python 전수 감사 실행 결과는 별도 명령 출력으로 확인.
- 일반 번역 JSON·F12/소스·Quartermaster·문서 정적 검사 및 `docs` 의존성 검사를 별도로 실행.
- .NET SDK 및 게임 실행 환경 부재로 **서버 DLL 컴파일·Windows CMD 실제 실행·SPT 게임 한글화는 미검증**.
- **전체 한글화 완료/문제 해결 확정이라고 보고해서는 안 됩니다.** 서버의 `[Manimal Korean]` 로그에 실제 변환 개수가 표시되는지, 클라이언트 훅이 설치되는지, 각 표시 경로의 실제 한국어 출력을 확인해야 완료 판정이 가능합니다.
