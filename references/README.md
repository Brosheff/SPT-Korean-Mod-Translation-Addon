# 빌드용 참조 DLL 안내

이 폴더는 개발자에게만 필요한 **컴파일 참조 파일**용입니다. 게임 설치 경로를 입력하거나 검색하지 않습니다.

`BUILD_DISTRIBUTION.cmd`는 프로젝트 내부의 `references/client`와 `references/server`에 있는 DLL만 사용합니다.
다른 참조 저장소를 사용할 때에만 첫 번째 인수로 그 폴더를 지정하거나 `ADDON_REFERENCE_ROOT` 환경변수를 사용합니다.

**client/** — BepInEx.dll, 0Harmony.dll, UnityEngine.dll, UnityEngine.CoreModule.dll,
UnityEngine.IMGUIModule.dll, Unity.TextMeshPro.dll, UnityEngine.UI.dll, UnityEngine.UIModule.dll,
Newtonsoft.Json.dll, spt-common.dll. 계약 테스트 실행 시 Mono.Cecil.dll도 사용합니다.

**server/** — SPTarkov.Server.Core.dll, SPTarkov.Common.dll, SPTarkov.DI.dll,
SemanticVersioning.dll, 0Harmony.dll.

참조 DLL은 SPT 4.1.6에 호환되는 정당하게 확보한 사본이어야 합니다. 이 소스 ZIP에는 해당 제3자 DLL을 포함하지 않습니다.
이 파일들이 없으면 컴파일이 불가능하므로 필요한 파일명을 출력하고 중단합니다.
참조 DLL은 배포 ZIP에 포함되지 않습니다.
