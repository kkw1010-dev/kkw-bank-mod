# BankPrismUI - Skyrim Banking & Merchant Credit Mod

## Overview (개요)
이 프로젝트는 스카이림(Skyrim) 내에서 동작하는 은행 및 상인 외상(마이너스 재화) 시스템을 구현한 모드의 뼈대입니다. 
기존의 Scaleform(Flash) UI 대신, 최신 웹 기술 기반의 **PrismaUI 프레임워크**를 사용하여 제작되었습니다.

이 레포지토리의 코드는 게임 내의 파피루스(Papyrus) 스크립트, SKSE를 통한 C++ 네이티브 플러그인, 그리고 HTML/JS 기반의 프론트엔드가 어떻게 상호작용하는지 보여줍니다.

## Architecture (아키텍처 구조)

본 모드는 크게 3가지 계층으로 구성되어 있습니다:

1. **Frontend (UI 계층)**
   - **경로:** `PrismaUI/views/BankPrism/BankView.html`
   - **역할:** 플레이어에게 보여지는 팝업 UI입니다. 잔고, 외상금, 채권 매각 등의 인터페이스를 제공하며 Flexbox와 Grid를 이용해 디자인되었습니다.
   - **통신:** 사용자가 버튼을 클릭하면 `window.chrome.webview.postMessage()`를 통해 C++ 백엔드로 신호를 보냅니다.

2. **Backend (C++ SKSE 계층)**
   - **경로:** `SKSE_Source/src/main.cpp`
   - **역할:** 파피루스와 UI 사이의 브릿지 역할을 합니다.
   - **주요 기능:** 
     - UI 포커스 시 게임을 일시정지(`bPauseGame = true`)하여 CTD 및 멀티스레드 문제를 원천 차단합니다.
     - UI에서 들어온 JS 메시지(`postMessage`)를 캡처하여 파피루스 ModEvent(`BankPrismAction`)로 변환하여 발송합니다.
     - 파피루스에서 넘겨준 데이터를 JSON 형태로 UI에 전달(`bankUpdateParams`)합니다.

3. **Game Logic (Papyrus 스크립트 계층)**
   - **경로:** `Scripts/Source/BankPrismController.psc`
   - **역할:** 실제 게임 내 글로벌 변수(Global Variable)를 통제하고 게임 이벤트를 처리합니다.
   - **주요 기능:**
     - 예금(`BankBalance`), 대출(`BankDebt`), 상인 외상금(`MerchantCreditDebt`)의 수치를 관리합니다.
     - C++에서 넘겨준 `BankPrismAction` 이벤트를 수신하여 외상값 상환, 채권 서드파티 모드 연동 등의 게임 로직을 실행합니다.

## Workflow (동작 흐름)
1. 인게임에서 청지기(Steward) 등의 NPC와 대화하여 은행 UI 열기 호출.
2. Papyrus가 C++의 `OpenMenu()` 함수 호출.
3. C++가 PrismaUI를 통해 `BankView.html`을 화면에 띄우고 게임을 일시정지(Pause)시킴.
4. UI에서 [상인 외상값 상환] 버튼을 클릭.
5. HTML JS가 `payCredit` 문자열을 C++로 전송.
6. C++가 이를 받아 Papyrus로 `BankPrismAction` ModEvent 발송.
7. Papyrus가 이벤트를 수신하고 `MerchantCreditDebt` 값을 0으로 변경 후 UI 새로고침.

## Repository Structure (폴더 구조)
```text
BankPrismUI/
├── PrismaUI/
│   └── views/
│       └── BankPrism/
│           └── BankView.html        # UI 렌더링 파일
├── Scripts/
│   └── Source/
│       └── BankPrismController.psc  # 게임 내 로직 컨트롤러
├── SKSE_Source/
│   └── src/
│       └── main.cpp                 # SKSE C++ 플러그인 메인
└── README.md                        # 본 문서
```
