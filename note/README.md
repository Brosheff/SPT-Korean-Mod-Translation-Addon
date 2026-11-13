# SPT Mod Korean Addon v1.0.6

SPT 4.1.6용 모드 한국어 번역 애드온입니다. 아이템·퀘스트 등의 로케일과 모드별 F12 설정, 일부 UI·메시지를 한국어로 표시합니다. 지원 범위는 모드와 버전에 따라 다릅니다.

**필수 선행 모드: SPT Korean Project Locale Patcher**

https://sp-mod.com/mod/1629/spt-korean-project-locale-patcher

## 설치 및 업데이트

1. 게임과 서버를 종료합니다.
2. 배포 ZIP의 `BepInEx`, `SPT_Runtime` 폴더를 **SPT 설치 폴더**에 압축 해제합니다.
3. 기존 파일과 겹치면 **모두 덮어씁니다.**

기존 애드온이나 `translations` 폴더 전체를 삭제할 필요는 없습니다.

**이전에 제공된 1.0.4 구버전 호환 패치를 설치했다면**, 다음 두 파일은 덮어쓰기로 제거되지 않으므로 **개별 삭제**하십시오.

- `BepInEx/plugins/SPT_Mod_Korean_Addon/translations/ISB Aishi - 2.0.3 Compatibility.json`
- `BepInEx/plugins/SPT_Mod_Korean_Addon/translations/ISB Special Forces - 2.0.0 Compatibility.json`

현재 1.0.6 개발 소스의 번역 기준은 Aishi 2.0.7과 Special Forces 2.0.4이며, 위 구버전 호환 파일은 필요하지 않습니다. 실제 SPT 게임에서의 적용 검증은 별도로 필요합니다.

1.0.6부터 WristAccessories 번역 지원은 제외됩니다. 이전 1.0.4를 설치한 경우 다음 **애드온 파일만** 기존 SPT 폴더에서 별도로 삭제해야 합니다. 덮어쓰기로는 삭제되지 않습니다.

- `BepInEx/plugins/SPT_Mod_Korean_Addon/translations/WristAccessories.json`
- `BepInEx/plugins/SPT_Mod_Korean_Addon/config-ui/com.amos.wristaccessories.json`

**주의:** 이 설치 절차는 `BepInEx`와 `SPT_Runtime`이 포함된 빌드 완료 **설치용 ZIP** 기준입니다. `*-Source.zip`은 설치용 파일이 아닙니다. 이전에 제공된 구버전 호환 복구 ZIP은 적용하지 마십시오.

## 안내 문서

- [업데이트 내용](update.md)
- [번역 지원 모드 및 범위](번역지원모드목록.md)

설치 ZIP에는 실행에 필요한 파일만 포함됩니다. 이 `note` 폴더는 사용자 안내 및 배포 게시용 문서이며, 설치 경로에 추가로 복사할 필요는 없습니다.
