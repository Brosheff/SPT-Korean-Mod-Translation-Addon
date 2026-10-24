# Translation format — 현행 데이터 규격

> 1.7.16은 일부 채널 통합 규칙의 도입 이력입니다. **현재 문서가 적용되는 애드온 릴리스 버전이나 검증 결과가 아닙니다.** 작업별 문서 열람 순서는 `docs/AGENTS.md`를, 스키마 항목은 이 문서를 따릅니다.

작업 순서는 [번역작업안내서](번역작업안내서.md)를, 표시 동작은 현재 `src/LocaleDisplayRules.cs` 등을 먼저 확인한다. 이 파일이 현행 스키마 기준이다. [부모 패턴 분석](records/audits/PARENT_PATTERN_AUDIT_1.7.14.md)은 필요할 때만 읽는 과거 감사 기록이며 현행 규칙이 아니다.

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

### 원본 모드 버전 기록(작업 시 확인 의무 · 근거 확보 시 필수 기록)

`translations/*.json`과 `config-ui/*.json`의 최상위 `mod_version`은 원본 코드/배포 자료로 근거를 확보한 **모드 자체의 버전**을 문자열로 기록한다. 과거 최초 번역 시점을 복원할 근거가 없는 경우에는 당시 최초 버전이라고 주장하지 않는다. 버전 근거 및 미확인 항목은 `번역_원본모드_버전관리대장_20261023.md`를 따른다. `target_spt`는 게임 호환 버전이며 `mod_version`과 다르다. `4.1-dev`, `spt-4.1` 등의 SPT 브랜치명이나 테스트용 어셈블리 버전을 모드 버전으로 자동 채택하지 않는다. 버전이 불명확하면 필드를 생략하고 대장에 `미확인`으로 표시한다.

미확인 프로필에 대해 공식 배포 사이트의 `최신 버전`을 새로 기록할 때에는 **웹 조회 기준 버전**으로 대장과 근거 CSV에 출처·확인일을 남긴다. 최초 번역 당시 버전, 사용자가 보관한 원본 소스의 버전, 인게임 검증 버전과 같다고 보장하지 않는다. 사이트에서 버전을 확인했다는 이유로 기존 번역을 수정하거나 호환 검증을 통과 처리하지 않는다.

**신규 번역·번역 업데이트·F12 작업의 필수 처리 규칙:**

- 대상 `translations/*.json`과 `config-ui/*.json` **각 프로필**에서 실제 기준 원본의 모드 릴리스 버전을 먼저 조사한다. 동일 모드라도 일반 번역과 F12의 기준 원본이 다른 경우 각각 근거를 남긴다.
- 근거 있는 버전이면 최상위 `"mod_version": "x.y.z"`를 기입·갱신하고, **같은 작업에서** `docs/records/versioning/번역_원본모드_버전관리대장_20261023.md` 및 `docs/records/versioning/번역_원본모드_버전_근거목록_20261023.csv`도 갱신한다. 원본 ZIP/프로젝트/공식 사이트 등 출처 유형과 확인일을 남긴다.
- 소스 ZIP의 버전과 공식 사이트 최신 게시 버전은 다를 수 있다. `mod_version`은 관리용 **기록 버전**이며, 근거가 `공식 사이트 조회`뿐이라면 그 사실을 대장·근거 CSV에 그대로 명시한다. 과거 작업 원본/최초 번역 버전 또는 현재 게임 적용 검증 버전으로 해석하지 않는다.
- 버전을 확인하지 못하면 숫자를 만들어 넣지 않는다. 기존 값의 근거도 불분명하면 확인 완료로 표시하지 않고, 필드 미기록 및 `미확인` 사유를 대장·작업별 인계에 기록한다. 해당 항목은 **버전 확인 미완료**로 남긴다. 근거 확보 시 같은 작업에서 보완한다.
- 작업 종료 시 **변경 대상 전건**에 대해 `JSON↔버전 대장↔근거 CSV`의 프로필·버전·출처·확인일을 대조하고 `대상/일치/불일치/미확인`을 보고한다. 문서의 버전 번호 존재만으로 원본 일치·번역 적용·F12 호환이 검증됐다고 처리하지 않는다.

공개 `note/번역지원모드목록.md`의 버전은 `build/build_supported_mods.py`가 이 필드에서 생성한다. 번역과 F12 버전이 다르면 각각 표시하며, 최신 버전 조회만으로 실제 호환을 검증한 것으로 해석하지 않는다. 버전 메타데이터를 수정한 뒤에는 지원 목록을 재생성하고 일치 여부를 검사한다.

이 필드는 번역 로딩·적용 조건을 변경하지 않는다. F12에서의 `mod_version`도 기록용이며 기존 F12 필드의 GUID, section/key 또는 저장값을 변경해서는 안 된다. 근거가 확인된 최신 모드 버전으로 이 메타데이터를 갱신하더라도 실제 번역 원문·동작 호환성이 검증됐다는 뜻은 아니다.

### 현재 설치된 모드 버전만 지원

애드온 1.0.4의 Aishi 2.0.4와 ISB Special Forces 2.0.1은 모드별 단일 번역 프로필을 사용한다. 이전 버전의 영어 원문만을 위한 별도 호환 프로필이나 검사 스크립트를 배포하지 않는다. **동일한 최신 원본 모드 안에서 서로 다른 코드 경로에 실제로 존재하는 영어 문자열**은 과거 버전 호환으로 취급하지 않는다. 각 문자열을 해당 최신 버전의 실제 출력 경로로 검증하고 관리한다.

`mod_id`는 기존 코드가 참조하는 식별자다. 파일명/모드 표시명이 비슷하다고 임의 변경하지 않는다. `mod_name`은 설명 끝의 출처 표시를 관리하는 유일한 값이다.

1.7.16부터 같은 모드의 UI·알림·로케일은 `translations/<모드명>.json` 하나의 channels로 관리한다. `Small UI -`, `UI Alerts -` 파일을 다시 만들지 않는다. config-ui와 native-locales는 별도 로더 규격이므로 이 JSON에 섞지 않는다. 현행 파일·프로필 ID·채널은 **실제 `translations/*.json`에서 확인**한다. 과거 통합 이력 문서 `MERGED_TRANSLATIONS_1.7.16.md`는 현재 제공되지 않으므로 이전 파일 대응 관계는 별도 근거 없이는 확정하지 않는다.

**원문 공급 경로 선행 검사:** `source`를 확인할 때 최신 모드의 JSON 외에 서버 등록 함수, 언어별 transformer·최종화, 부모/다른 모드 로케일 병합 및 실제 훅 전달 값을 추적한다. 동일 키의 복수 공급 영어가 있으면 `원본 경로·조건·적용 순서·정적 예상 최종값·실제 런타임 값(있다면)`을 따로 남긴다. **최신 JSON과 프로필이 일치한다는 사실만으로 로더 적용을 판정하지 않는다.** 실행값 미확보는 미확인으로 표시한다. 자세한 절차는 `번역작업안내서.md` 2-3절을 따른다.

`source`는 원문 일치 검사용이므로 trim하거나 대소문자를 바꾸지 않는다. `translation`은 한국어 본문 하나만 저장한다. **아이템 `Name`에는 `\n(영문 아이템명)`을 수동으로 붙이지 않는다.** 한영병기에서 원문을 자동 생성하므로 이전 데이터의 수동 영문 이름이 남으면 이름이 중복된다. `source`와 `translation`이 동일한 모델명/단축명은 번역 상태 보고에서 **원문 유지**로 분류한다. 이 두 조건은 전체 누적 프로필에서도 검사해야 한다.

 전체 원문, 원문 이름 머리말, 출처 모드명을 직접 합성하지 않는다. 원문 유지 모델명/탄약명도 가능하다.

## locale 표시 종류

1.7.15에서 목표 전용 `quest_objective`를 추가했다. 실제 퀘스트 conditions의 ID로 확인한 행에만 명시한다. `achievement_condition`은 한영병기 전용이므로 목표에 대신 쓰지 않는다. 과거 감사 문서 `QUEST_PATTERN_REAUDIT_20261003.md`는 현재 제공되지 않는다. 현행 표시 규칙은 `src/LocaleDisplayRules.cs`를 기준으로 확인하며, **신규 목표의 조건 ID/문구는 원본 모드 데이터로 검증**한다. 과거 감사 결과 자체를 재현했다고 주장하지 않는다.

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

## 실제 게임 원문과의 불일치 — 번역 적용 검증 주의

`source`는 모드 ZIP을 베껴 쓰는 참고 문자열만이 아니라 **실제 게임 로케일 값과 비교하는 런타임 조건**이다. 원본 ZIP과 `translations`의 일치가 곧 인게임 적용 성공은 아니다. 특히 다른 모드/서버의 로케일 병합 후 값, 플러그인 로딩 순서, 같은 키의 여러 원문 후보, 이전 행에서 이어지는 `match_mode`를 함께 확인한다.

**확인된 실패 사례:** 번역 프로필의 총구제퇴기 원문 `VSK 12.7x55 ST-130 muzzle brake`와 사용자 스크린샷의 실제 표시 `VKS 12.7x55 ST-130 muzzle brake`가 다르다. 이 경우 전체 문자열 비교가 실패하면 이름·설명이 영어로 남을 수 있다. 정확한 런타임 item key와 원문 값이 없는 상태에서 모델명 유사도·전역 치환으로 덮어쓰지 않는다. **한 건 실패가 나왔다면 같은 비교 규칙을 쓰는 모든 새·수정 행을 재감사**한다. 정적 검사와 인게임 확인 상태는 [`응답전_필수확인.md`](응답전_필수확인.md)에 따라 별도 기록한다.

### 검증된 런타임 원문 변형 (`locale.source_variants`)

**원본 모드 파일의 영어 문자열은 수정하지 않는다.** 번역 프로필의 `source`는 현행 지원 버전의 키·공급 경로·적용 조건을 확인한 경우에만 그 실제 원문에 맞춰 최소 변경할 수 있다. 실제로 계속 사용되는 현행 원문은 검증 없이 제거하지 않는다. 같은 실제 로케일 **키**에 대해 사용자가 촬영한 화면 또는 실제 서버 로케일 스냅샷에서 **동일한 의미의 다른 영어 원문**이 확인된 경우에만, 해당 `locale` 행에 문자열 배열 `source_variants`를 지정할 수 있다.

```json
{
  "key": "6ab051dcb07a9b85e5577d0c Name",
  "source": "VSK 12.7x55 ST-130 muzzle brake",
  "source_variants": ["VKS 12.7x55 ST-130 muzzle brake"],
  "translation": "VKS 12.7x55 ST-130 총구제퇴기",
  "match_mode": "item_decorated"
}
```

- `source`와 `source_variants`의 후보는 **해당 행의 키에서만** 원문 비교에 사용한다. 유사도·자동 오탈자 수정·다른 아이템으로의 확장·전역 `VSK→VKS` 치환은 하지 않는다.
- `source_variants`는 **빈 문자열 없이 서로 다른 값 1~8개**만 허용하며 `source` 자체를 중복하지 않는다. 변형이 입증되지 않았으면 필드를 만들지 않는다.
- `item_decorated` 행에서는 기존에 허용한 가격/탄약 수치 장식만 보존한다. 그 외 소스는 기존 `exact` 일치 조건을 그대로 따른다.
- 번역 이름의 한영병기는 **실제로 일치한 원문**을 사용한다. 설명의 `[원문 이름]`도 해당 이름 키에서 검증된 런타임 원문이면 최신 표시를 따른다.
- 이 필드는 애드온 C# 로더가 읽어야 작동한다. **기존 DLL에 JSON만 추가해서 해결되는 기능이 아니며 빌드가 필요**하다.
- 스크린샷이 전체 영어 설명을 읽을 수 있어도 **아이템 키는 표시되지 않을 수 있다**. 원본 모드 ID와 UI 위치로 추적한 추론을 실제 서버 키로 확인한 사실처럼 기록하지 않는다.
- 변형을 한 건 확인해도 나머지 1.0.3 작업의 **런타임 서버 원문 전수 검증이 끝난 것은 아니다**. `docs/tools/audit_runtime_coverage.py`에 서버 스냅샷을 넣어 별도 재감사한다.

**후보 관리의 경계:** 동일한 최신 모드 안에 영문 `en.json`과 코드 주입 값이 함께 존재하더라도, 코드를 읽어 발견했다는 이유만으로 `source_variants`를 생성하거나 새로운 구버전 `Compatibility.json`을 만들지 않는다. 어느 값이 특정 언어의 로더에 들어오는지는 후처리·덮어쓰기 분기로 검토하고, 이 절이 요구하는 실제 화면/서버 증거가 없으면 `런타임 미확인`으로 기록한다. 구버전 전용 영어는 현행 회귀 보존 대상으로 간주하지 않는다.

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

- `build/validate_translations.py`: translations 프로필 검증. config-ui는 별도 검증한다.
- `build/check_csharp_delimiters.py`: C# 소스 괄호 균형 사전 검사. `build/verify_release.py source`에서도 실행한다. 실제 C# 컴파일이 아님을 구분한다.
- `docs/tools/audit_runtime_coverage.py`: 실제 서버 원문 스냅샷과 전체 번역 키를 대조한다. 없는 키와 원문 불일치, 다른 모드의 한국어, 검토할 단축명을 구분한다. JSON 검증 통과만으로 실제 번역 적용을 단정하지 않는다.
- `docs/tools/audit_parent_patterns.py`: 부모의 세 언어 전체 패턴 재분석.
- `docs/tools/correct_parent_display.py`: 1.7.13 잔존 병기 정리용, 기본 dry-run. `--decisions docs/tools/parent_display_decisions.json --report <path>` 필요. 일상 번역 추가마다 write로 실행하지 않는다.
- `tests/LocaleDisplayContract`: 과거 C# 로더·표시/F12 계약 검사용. **현재 소스 ZIP에 미동봉**이며 원본 확보 전에는 실행할 수 없다.
- `tests/ServerDisplayContract`: 과거 서버·메일 계약 검사용. **현재 소스 ZIP에 미동봉**이며 원본 확보 전에는 실행할 수 없다.
- 세 계약 테스트(카지노 포함)의 현재 제공 상태와 대체 가능한 검사·미실시 보고 기준은 `docs/tools/VERIFY_WORKFLOW.md`를 참고한다. 정적 검사는 이 C# 계약 테스트의 통과 증거가 아니다.

### 서버가 영어만 등록하는 모드의 누락 키 (2026-10-25)

- 현행 원본에서 ID와 영어 원문이 확인된 로케일 프로필에 한해 개별 `locale` 행의 `"seed_missing": true`를 사용할 수 있다. **기본값은 `false`**이며, 이 속성이 없는 기존 행의 동작은 변경하지 않는다.
- 이 플래그는 원본 영어 전용 로케일 파일에서 키와 원문을 확인한 `locale` 행에만 적용한다. 한국어 모드에서 서버의 현재 한국어 원문 맵에 키가 없어도, 최종 병합 결과가 없거나 `source`와 정확히 일치하는 영어/키 표시인 경우에만 번역을 채운다. **이미 다른 로케일 값이 있다면 덮어쓰지 않는다.** `locale`의 `display_type` 규칙과 `kr`/`kr-en` 정책은 그대로 적용한다.
- 키의 출처와 실제 공급 모드를 확인한 경우에만 활성화하며, 키가 임의 생성되거나 모드가 다른 자체 데이터 경로로 화면을 그리는 경우에는 적용을 보장하지 않는다. 이 기능은 인게임 검증을 대신하지 않는다.
