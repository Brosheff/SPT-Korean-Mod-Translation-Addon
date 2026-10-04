# SPT Mod Korean Addon — 1.0.0

SPT 4.1.6용 모드 번역 애드온입니다. 부모 모드 SPT Korean Project - Locale Patcher / Korean Patch Fix 2.1.1이 필요합니다. 부모의 한국어·한영병기 언어 설정을 사용합니다.

## 안내 문서

- [지원 모드 목록](docs/번역지원모드목록_목록만.md)
- [모드별 번역 범위](docs/번역지원모드목록.md)
- [번역 작업안내서](docs/번역작업안내서.md)
- [현재 JSON 및 표시 규격](docs/TRANSLATION_FORMAT.md)
- [최신 변경사항과 설치 주의점](docs/LOCALE_DISPLAY_1.0.0_KO.md)
- [모드별 파일 통합표](docs/MERGED_TRANSLATIONS_1.7.16.md)
- [부모 표시 패턴 분석](docs/PARENT_PATTERN_AUDIT_1.7.14.md)
- [퀘스트 패턴 분석](docs/QUEST_PATTERN_REAUDIT_20261003.md)

## 현재 표시 규칙

- 아이템 이름: 한국어는 번역 이름, 한영병기는 번역 이름 뒤 줄바꿈 괄호 원문.
- 아이템 설명: 두 한국어 설정 모두 [원문 이름] 머리말과 (모드명) 출처.
- 퀘스트 제목: 두 한국어 설정 모두 같은 줄 괄호 원문.
- 확인된 퀘스트 목표: 두 한국어 설정 모두 줄바꿈 괄호 원문.
- 퀘스트 설명·업적 설명: 두 한국어 설정 모두 끝에 줄바꿈 (모드명).
- 업적 이름: 한영병기에서만 같은 줄 괄호 원문.
- 일반 UI·상인·대사·메일에는 일괄 원문 병기나 출처를 추가하지 않습니다.

번역 본문만 JSON에 저장하고 자동 표시 장식은 코드가 생성합니다. 최신 세부 규격은 docs/TRANSLATION_FORMAT.md를 따릅니다.

## 폴더

- src: 클라이언트 C# 소스.
- docs: 번역 안내서·지원 모드 목록·표시 규칙 및 분석 문서.

- translations: 모드별 번역 JSON. 같은 모드의 UI·알림·로케일을 채널로 구분합니다.
- config-ui: F12 설정 번역.
- native-locales: 모드 자체 언어팩 연동 데이터.
- server-src: 서버 애드온 소스.
- tests / tools: 검증 및 유지보수 도구.
- _dist: 배포본.
- bin / obj: 빌드 결과와 캐시.

## 빌드 및 검증

BUILD_DISTRIBUTION.ps1에 SPTPath와 필요한 경우 PythonExe를 지정해 클라이언트·서버 DLL과 설치 ZIP을 만듭니다. 파일 배치는 docs/DISTRIBUTION_LAYOUT.txt를 참고합니다.

```powershell
python tools/validate_translations.py
python tools/build_supported_mods.py
.\BUILD_DISTRIBUTION.ps1 -SPTPath 'C:\spt 4.1'
```

코드 변경 시 tests/LocaleDisplayContract와 tests/ServerDisplayContract를 사용합니다. 전체 데이터 검사의 분류 manifest와 실제 서버 snapshot은 현재 데이터에 맞춰 준비해야 합니다. 과거 파일명이나 고정 행 수에 맞추려고 최신 번역을 되돌리지 않습니다. 구문 검증과 인게임 화면 검증은 구분해서 보고합니다.

## 설치

게임을 종료하고 기존 애드온 translations 폴더를 다른 이름으로 백업한 뒤 배포 ZIP의 BepInEx와 SPT_Runtime을 SPT 루트에 복사합니다. 통합 전 JSON이 남지 않도록 새 translations 폴더를 설치하고 DLL도 함께 갱신합니다. 게임 설정 키·저장 값·원본 모드 DLL은 번역 대상으로 변경하지 않습니다.

[상위 프로젝트 라이선스](LICENSE-upstream.md)를 유지합니다.
