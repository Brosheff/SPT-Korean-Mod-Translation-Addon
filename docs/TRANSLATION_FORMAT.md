# Translation format — 1.7.16

작업 순서는 [번역작업안내서](번역작업안내서.md), 표시 근거는 [부모 패턴 분석](PARENT_PATTERN_AUDIT_1.7.14.md)을 먼저 읽는다. 이 파일이 현재 스키마 기준이며 1.7.13 설명보다 우선한다.

## 프로필

```json
{
  "schema_version": 1,
  "target_spt": "4.1.6",
  "mod_id": "stable.mod.id",
  "mod_name": "Displayed Mod Name",
  "channels": {
    "locale": [
      { "key": "item Name", "source": "Original name", "translation": "번역 이름", "match_mode": "item_decorated" },
      { "key": "item ShortName", "source": "Short", "translation": "단축명" },
      { "key": "item Description", "source": "Original description.", "translation": "번역 설명입니다." }
    ]
  }
}
```

`mod_id`는 기존 코드가 참조하는 식별자다. 파일명/모드 표시명이 비슷하다고 임의 변경하지 않는다. `mod_name`은 설명 끝의 출처 표시를 관리하는 유일한 값이다.

1.7.16부터 같은 모드의 UI·알림·로케일은 `translations/<모드명>.json` 하나의 channels로 관리한다. `Small UI -`, `UI Alerts -` 파일을 다시 만들지 않는다. config-ui와 native-locales는 별도 로더 규격이므로 이 JSON에 섞지 않는다. 통합 내역은 `MERGED_TRANSLATIONS_1.7.16.md`를 따른다.

`source`는 원문 일치 검사용이므로 trim하거나 대소문자를 바꾸지 않는다. `translation`은 한국어 본문 하나만 저장한다. 전체 원문, 원문 이름 머리말, 출처 모드명을 직접 합성하지 않는다. 원문 유지 모델명/탄약명도 가능하다.

## locale 표시 종류

1.7.15에서 목표 전용 `quest_objective`를 추가했다. 실제 퀘스트 conditions의 ID로 확인한 행에만 명시한다. `achievement_condition`은 한영병기 전용이므로 목표에 대신 쓰지 않는다. 근거: `QUEST_PATTERN_REAUDIT_20261003.md`.

| display_type | kr | kr-en |
|---|---|---|
| default | 번역 본문 | 번역 본문 |
| item_name | 번역 이름 | `번역 이름\n(원문 이름)` |
| item_description | `[원문 이름]\n번역 설명\n(mod_name)` | 왼쪽과 동일 |
| quest_title | `번역 제목 (원문 제목)` | 왼쪽과 동일 |
| quest_objective | `번역 목표\n(원문 목표)` | 왼쪽과 동일 |
| quest_description | `번역 설명\n(mod_name)` | 왼쪽과 동일 |
| achievement_title | 번역 제목 | `번역 제목 (원문 제목)` |
| achievement_description | `번역 설명\n(mod_name)` | 왼쪽과 동일 |
| achievement_condition | 번역 조건 | `번역 조건\n(원문 조건)` |

quest_description과 achievement_description은 1.7.16 사용자 요청으로 추가한 애드온 출처 규칙이다. 실제 정의 또는 확인된 제목 그룹의 설명 필드에 명시한다. 메시지·목표·상인 소개에 확대 적용하지 않는다. 이 두 설명에는 아이템의 `[원문 이름]` 머리말이나 영문 설명 본문을 붙이지 않는다. 한국어/한영병기 모두 출처를 표시하며 다른 언어는 원문이다.

그 외 언어는 원문이다. 동일한 번역/원문 이름은 반복하지 않는다. 의미가 있는 괄호·문단·태그는 유지한다. 설명에는 영어 설명 문장 전체를 붙이지 않는다.

`LocaleDisplayRules` 분류 순서:

1. 행에 `display_type`이 있으면 사용한다. 이 필드는 앞 행에서 상속되지 않는다.
2. FirstName/LastName/FullName/Nickname이 있는 상인 그룹은 default.
3. startedMessageText/successMessageText/acceptPlayerMessage/completePlayerMessage가 있는 퀘스트 그룹의 name/Name은 quest_title, 그 외는 default.
4. 남은 대문자 Name/Description은 각각 item_name/item_description.
5. 그 외는 default. 이름이라는 뜻의 key라는 이유만으로 퀘스트/아이템이라고 추정하지 않는다.

대문자 업적은 반드시 명시적으로 분류한다. 소문자 또는 bare ID 의류는 확인된 item_name/item_description을 명시한다. 단독 퀘스트 제목은 quest_title을 명시한다. parent_display_decisions.json은 실제 업적/조건 정의에서 확인한 예외 기록이며, 런타임에서는 행의 display_type을 읽는다.

설명의 원문 이름은 같은 프로필의 이름 행 source에서 찾는다. 같은 ID의 Name/name 또는 bare ID 구조를 지원한다. 다른 이름 키에 연결해야 하면 설명 행에 `"name_key": "실제 이름 로케일 키"`를 쓴다. 해당 프로필에 이름 번역 행이 없는 원문 유지 탄약 등은 부모 영어 사전 또는 서버의 원문 이름에서 찾는다. 원문 이름 미확인은 머리말을 생략하고 `missing_item_name_sources`에 기록한다. 모드 출처는 유지한다.

`translation_bilingual`은 현재 사용하지 않는다. 오프라인 검증은 잔존 값을 오류로 처리하며, 일반 UI/메일의 런타임은 구형 값이 남아 있어도 이를 선택하지 않는다. 자동 locale 종류의 비어 있지 않은 구형 override는 그룹 로드 오류이므로 DLL과 데이터를 함께 갱신한다.

## 원문 일치와 후보 처리

locale는 `exact` 또는 `item_decorated`를 사용한다. 첫 행에 명시하고, 이후 행은 이전 match_mode를 상속한다. 바뀌는 지점에 다시 명시한다. `item_decorated`는 기존의 가격 접두부/탄약 수치 장식을 보존하면서 본문 원문이 맞을 때만 적용한다.

프로필은 파일명 ordinal 순서로 읽는다. 런타임 키는 대소문자를 무시하되 원본의 별칭/다른 source 후보를 보존한다. 첫 번째 원문 일치 후보를 사용하고 다른 결과의 후보는 `candidate_conflicts`에 기록한다. 동일 키·동일 원문을 공유하는 모드의 소유권을 로케일만으로 확정하지 않는다.

## 다른 채널

- `locale_source_fallback`: 확인된 원문 값의 정확 일치. 부모가 이미 번역한 값을 덮어쓰지 않는다.
- `locale_additions`: 모드가 실제 조회하는 키만 추가한다. 이미 다른 번역이 들어 있으면 보존한다.
- `locale_clone_suffix`: 기존 부모 아이템에 접미부를 붙이는 경로. `key`는 자식 ID, `parent_key`는 부모 ID, source/translation은 이름 접미부, description_source/description_translation은 설명 추가 부분이다. 부모/자식 원문 관계가 맞을 때만 적용하며 두 언어 설명에 자식 이름 머리말과 mod_name을 표시한다.
- `small_ui` 등 일반 클라이언트 채널: 기존 hook의 profile ID/channel 연결이 있어야 한다. default 한국어만 표시한다.
- `npc_messages`: 직접 NPC 메일의 실제 trader_id를 행마다 명시한다. 기존 RealisticInsurance 전용 예약 범위는 해당 프로필에서만 허용한다.
- `server_messages`: 직접 USER/SYSTEM 메일. trader_id 없이 `exact`/`segment`만 사용한다.
- `reference_only`: 조사 기록이며 표시 경로가 아니다.

일반 텍스트 규칙은 id/source/translation과 명시적인 match_mode를 사용한다. `exact`, `template`, `segment`, `segment_template`, `regex_segment`를 지원한다. `{0}`, `{count}`를 원문과 일치하게 보존한다. regex_segment는 .NET 이름 캡처 `(?<count>...)`를 translation의 `{count}`로 참조한다. `exclusive_group`은 같은 패스에서 한 그룹의 중복 적용을 막는 기존 기능이다.

메일은 부모의 CurrentCulture를 클라이언트에서 동기화하고 서버 전송 시 번역한다. 미동기화/그 외 언어는 원문이다. 두 한국어 모드 모두 번역 본문을 쓰며 영어를 다시 합성하지 않는다.

## config-ui 및 native-locales

F12: schema_version/target_spt/plugin_guid/plugin_name, categories와 entries를 사용한다. entries의 section/key/source_display_name/source_description은 원본 식별과 검사용이다. translation_display_name/translation_description 및 values의 source/translation만 편집한다. 빈 section/key는 유효할 수 있다. 저장 옵션 값은 바꾸지 않는다. `_bilingual`은 작성하지 않는다.

PitFireTeam의 krx/kren은 기존 호환 별칭이다. 두 파일에 동일한 한국어 내용을 유지한다. 일반 UI 병기를 부활시키는 용도로 한쪽을 편집하지 않는다.

## 검증 도구

- `tools/validate_translations.py`: translations 프로필 검증. config-ui는 별도 검증한다.
- `tools/audit_runtime_coverage.py`: 실제 서버 원문 스냅샷과 전체 번역 키를 대조한다. 없는 키와 원문 불일치, 다른 모드의 한국어, 검토할 단축명을 구분한다. JSON 검증 통과만으로 실제 번역 적용을 단정하지 않는다.
- `tools/audit_parent_patterns.py`: 부모의 세 언어 전체 패턴 재분석.
- `tools/correct_parent_display.py`: 1.7.13 잔존 병기 정리용, 기본 dry-run. `--decisions tools/parent_display_decisions.json --report <path>` 필요. 일상 번역 추가마다 write로 실행하지 않는다.
- `tests/LocaleDisplayContract`: 실제 C# 로더/표시/F12와 부모 출력 비교.
- `tests/ServerDisplayContract`: 실제 서버 규칙과 메일 언어 검증.
