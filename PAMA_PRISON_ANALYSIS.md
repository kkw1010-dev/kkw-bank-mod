# Pama Prison Alternative 검토 기록

검토일: 2026-09-11  
대상: `C:\Modding 2\File Mod Skyrim SE\mods\[SL+] Pama Prison Alternative\PamaPrisonAlternative.esm`

## 대화에서 확정된 범위

- 화이트런 금융 시스템의 연대보증인 리디아에 **실제 구류**를 붙일 가능성을 검토한다.
- 바닐라 감옥을 활용하는 것은 가능하되, 바닐라 감옥 퀘스트·팩션·별칭은 건드리지
  않는 것이 안전하다고 정리했다.
- Pama가 설치된 환경에서 그 모드의 수감 장면을 활용할 수 있는지 확인하되,
  BankPrism을 Pama의 필수 종속 모드로 만들지는 않는다.
- 이번 작업에서는 게임 데이터와 BankPrism ESP를 수정하지 않았다.

## EspGenerator 진단 결과

`dotnet run -- external <PamaPrisonAlternative.esm 경로>`로 직접 읽었다.

- 마스터: `Skyrim.esm`, `Update.esm`, `ZaZAnimationPack.esm`
- Pama에는 퀘스트 8개, 팩션 8개가 있다.
- 화이트런 수감 장소는 바닐라 셀 `WhiterunDragonsreachBasement` (`04A376`)이다.
- 해당 셀에 Pama의 퍼시스턴트 가구 참조 `0000CC26`이 있다.
  - 베이스: `pamaPA_FormerFollowerHoldingGibbet` (`00BBF2`), Furniture
  - Pama 퀘스트 `PamaPA_FormerFollowerDetention` (`00BBEE`)의
    `Whiterun_HoldingDevice` 별칭도 이 참조를 강제 지정한다.
- 이 퀘스트에는 7개 홀드별 구금 장치와 구금 대상 별칭이 있고,
  `pamaPA_FollowerDetentionSystem` 스크립트가 팔로워 별칭, 범죄 팩션,
  인벤토리 상자, 바닐라 팔로워 별칭을 함께 소유한다.
- Pama의 플레이어 수감 퀘스트도 바닐라 감옥 퀘스트 `10D9F4`와 그 별칭을 참조한다.

따라서 Pama는 단순한 "감옥 장소 모드"가 아니라 팔로워·장비·바닐라 수감 흐름을
관리하는 독립 시스템이다.

## 채택할 연동 경계

**허용:** Pama가 로드된 경우에만 가구 참조 `0000CC26`을 리디아 이동 목적지의
위치 앵커로 선택한다. Papyrus의 바닐라 API `Game.GetFormFromFile()`로 런타임에
가져오므로 BankPrism ESP에 Pama 마스터를 추가하지 않는다.

**금지:** `PamaPA_FormerFollowerDetention` 퀘스트 시작, Detainee/HoldingDevice
별칭 채우기, Pama 인벤토리 상자 사용, Pama의 팔로워·범죄 팩션 상태 변경.

**폴백:** Pama가 없거나 가구 참조를 얻지 못하면 BankPrism 자체의 드래곤스리치
지하감옥 마커를 사용한다. Pama/ZaZ가 없는 BankPrism 사용자도 그대로 동작해야 한다.

## 리디아 구류의 안전한 상태 전이

1. 연체 7일에 기존 보증 상태를 "구상권 청구"로 바꾼다. 전투 중이거나 동행 중인
   리디아를 자동 순간이동시키지 않는다.
2. 플레이어가 동행을 해제한 뒤 "리디아 인도"를 선택하면, 위의 목적지로 이동시키고
   `SetRestrained(true)`, `SetDontMove(true)`만 적용한다.
3. 전액 상환하면 `SetRestrained(false)`, `SetDontMove(false)`,
   `MoveToPackageLocation()`, `EvaluatePackage()`로 복귀시킨다.

가구의 결박 애니메이션을 실제로 재생하는 일은 Pama가 자신의 퀘스트·별칭과 함께
처리할 수 있으므로, 위 안전선의 인게임 검증 뒤 별도 과제로 둔다.

## 로드오더 및 검증 상태

- `C:\Modding 2\File Mod Skyrim SE\profiles\kkw`와 `TuLED(SL)`에서는
  `PamaPrisonAlternative.esm`이 활성화돼 있다.
- 같은 두 프로필의 `plugins.txt`에는 BankPrism이 아직 배포·활성화돼 있지 않다.
  따라서 두 모드를 함께 둔 실제 게임 검증은 아직 할 수 없다.
- EspGenerator의 외부 플러그인 진단 명령은 빌드 성공(경고·오류 0)했다.
- 인게임 검증은 수행하지 않았다.
