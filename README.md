# Financial Hell - Skyrim Banking and Credit Overhaul

스카이림(Skyrim) 내에서 동작하는 영지별 은행 금융 및 상인 외상(신용 거래) 시스템 오버홀 모드입니다.  
기존의 구형 Scaleform(Flash) UI 대신, 최신 웹 기술 기반의 **PrismaUI 프레임워크**를 채택하여 고해상도, 고반응성의 웹뷰 기반 금융 인터페이스를 제공합니다.

---

## 📋 필수 선행 모드 (Requirements)

본 모드를 정상적으로 실행하기 위해 다음 선행 모드들이 반드시 설치되어 있어야 합니다:

1. **The Elder Scrolls V: Skyrim Special Edition / Anniversary Edition** (1.6.640+ / 1.6.1170+ 지원)
2. **[SKSE64 (Skyrim Script Extender 64)](https://skse.silverlock.org/)**
3. **[PrismaUI](https://www.nexusmods.com/skyrimspecialedition/mods/61713)** (필수)
   * HTML/CSS/JS 기반 Web UI 렌더링 프레임워크로 게임 일시정지와 뷰 레이어를 제어합니다.
4. **[Address Library for SKSE Plugins](https://www.nexusmods.com/skyrimspecialedition/mods/32444)**
5. **(권장) [Simple Follower Extension AE (SFEAE)](https://www.nexusmods.com/skyrimspecialedition/mods/58012)**
   * 화이트런 하우스칼(리디아) 연대보증 설정 시 다중 동료 자동 해산 및 복귀 관리가 완벽히 호환되도록 설계되어 있습니다. (바닐라 동료 시스템도 완벽 지원)

---

## 🎮 인게임 사용 방법 (How to Use)

### 1. 은행 업무 보기 (예금, 대출, 담보 설정, 상환)
* **화이트런 드래곤스리치**의 행정관 **프로벤투스 아베니치**에게 말을 걸고,  
  `"은행 업무를 보고 싶습니다."` 대화 선택지를 클릭합니다.
* 마우스 조작이 가능한 최신 웹 기반 뱅킹 창이 화면 중앙에 팝업되며, 게임은 안전하게 일시정지됩니다.

### 2. 상인 외상 거래하기 (Credit Barter)
* 화이트런 잡화상 **벨레소어**, **이솔다** 및 리버우드의 **카밀라 발레리우스**, **루칸 발레리우스**에게 말을 걸고,  
  `"외상으로 거래하고 싶습니다."` 대화 선택지를 클릭합니다.
* 외상 바터 창이 열리며 주머니에 골드가 없어도 신용 한도 내에서 물품을 구매할 수 있습니다. 외상값은 은행 창구에서 언제든지 일괄 상환할 수 있습니다.

### 3. 디버그 및 상태 점검 단축키
* 인게임 어디서나 키보드의 **`-` 키**(메인 숫자열의 마이너스 키, DirectX ScanCode 12)를 누르면 화면 좌상단에 현재 영지의 예금, 대출 잔액, 만기 일수, 담보 설정 여부, 리디아 팩션 랭크 및 동행 여부가 실시간 HUD 알림으로 출력됩니다.

---

## ✨ 핵심 기능 (Features)

### 1. 영지(Hold)별 독립 금고 및 장부 운영
* 스카이림 9대 영지는 각자의 경제와 법률을 독립적으로 운영합니다.
* 화이트런에서 진 빚은 화이트런 행정관이 관리하며, 각 영지마다 고유의 신용등급과 한도가 적용됩니다.

### 2. 예금(Deposit) 및 5단계 금고 비주얼
* 소지금을 안전하게 예치하거나 필요할 때 출금할 수 있습니다.
* 예금 탭에서는 플레이어의 총 예금액에 따라 금고실의 비주얼과 묘사가 5단계로 실시간 변화합니다:
  * **1단계 (0 ~ 1,000 G)**: 먼지 쌓인 낡은 궤짝
  * **2단계 (1,001 ~ 5,000 G)**: 철제 보강 금고
  * **3단계 (5,001 ~ 15,000 G)**: 궁정 전용 대형 보관함
  * **4단계 (15,001 ~ 30,000 G)**: 중앙 영지 귀중품 보관실
  * **5단계 (30,000 G 이상)**: 마법 비전 결계와 정예 경비병이 수호하는 황금 보물실

### 3. 5단계 신용등급 & 야를 발그루프 전용 대사
* 플레이어의 퀘스트 완료 내역, 영지 세인 임명, 팩션 가입 행적에 따라 영구적으로 인정받는 12가지 신용 칭호(1~5단계):
  * **1단계**: 외지인
  * **2단계**: 정체모를 용병
  * **3단계**: 영지의 종사 / 신뢰받는 해결사
  * **4단계**: 스카이림의 영웅 (드래곤 라이징 완료) / 요르바스크의 전사 / 수석 마법학자 / 훈장 수훈 장교
  * **5단계**: 탐리엘의 구원자 (알두인 토벌) / 컴패니언의 인도자 / 대학의 아크메이지 / 전쟁 영웅 장군
* **신용등급 탭**: 신용 단계에 따라 변화하는 **야를 발그루프(Jarl Balgruuf)의 1~5단계 전용 초상화 및 직속 평가 대사**가 출력됩니다.
* **대출 탭**: 장부를 엄격하게 관리하는 행정관 프로벤투스 아베니치의 1~5단계 리액션이 출력됩니다.

### 4. 대출 및 엄격한 연체(Overdue) 관리
* 신용등급과 담보 자산에 비례하여 대출을 즉시 실행할 수 있습니다.
* 기본 대출 만기는 14일이며, 만기 경과 시 일일 복리 연체료가 부과됩니다.
* 연체가 시작되면 배달부를 통해 매일 강도 높은 독촉장(Dunning Letter)이 날아옵니다.

### 5. 입체적 담보(Collateral) 시스템
* **인적 보증 (화이트런 하우스칼: 리디아 연대보증)**
  * 화이트런 세인으로 임명되면 리디아를 연대보증인으로 등록 가능 (대출 한도 +3,000 G).
  * **보증 등록 즉시 동행 중인 리디아는 자동으로 동료에서 해산되며 영입이 차단**됩니다 (SFEAE 및 바닐라 완벽 호환).
  * 대출이 연체되면 야간 수면 중 화이트런 경비대가 리디아를 드래곤스리치 감옥으로 강제 연행하여 구금합니다.
  * 대출 완납 시 리디아는 석방되며, 보증을 정식 해제하면 다시 동료로 영입할 수 있습니다.
* **부동산 담보 (브리즈홈 근저당)**
  * 브리즈홈 소유 시 근저당 설정 가능 (대출 한도 +3,000 G).
  * 연체 7일 경과 시 브리즈홈이 영지 법원에 의해 압류되며, 플레이어 소유의 열쇠가 회수되고 정문이 봉쇄됩니다.
  * 대출 완납 후 감정가의 20%에 해당하는 근저당 해지 수수료를 지불하면 소유권과 정문 열쇠가 완전히 복구됩니다.

### 6. 상인 외상(Merchant Credit) 시스템
* 잡화상과 외상 거래를 진행할 수 있으며, 신용등급에 따라 잡화상(벨레소어)의 5단계 비주얼과 대사가 변화합니다.
* 외상값은 은행 창구에서 언제든지 일괄 상환할 수 있습니다.

---

## 🏗️ 아키텍처 및 내부 구조 (Architecture)

1. **Frontend (PrismaUI / Web Layer)**
   * `PrismaUI/views/BankPrism/BankView.html`
   * Chrome WebView 기반의 고화질 HTML/CSS/JavaScript 인터페이스.
   * `window.chrome.webview.postMessage()`를 통해 C++ 네이티브 계층과 넌블로킹 통신.
2. **Native Bridge (SKSE C++ Layer)**
   * `SKSE_Source/src/main.cpp`
   * UI가 열릴 때 게임을 안전하게 일시정지(`bPauseGame = true`)하여 스레드 충돌 및 CTD 원천 방지.
   * 프론트엔드의 비동기 메시지를 캡처하여 파피루스 ModEvent(`BankPrismAction`)로 라우팅.
3. **Game Logic (Papyrus Layer)**
   * `Scripts/Source/BankPrismController.psc`
   * 잔고, 부채, 담보 상태, 신용 칭호 판정, 날짜 추적 및 독촉장 배달 통제.
4. **Plugin Layer (Mutagen C# Layer)**
   * `EspGenerator/Program.cs`
   * Creation Kit에 의존하지 않고 Mutagen 라이브러리를 통해 프로그래밍 방식으로 레코드, 글로벌 변수, 퀘스트 대화문, SEQ 파일을 100% 무결하게 컴파일.

---

## 🔨 빌드 및 개발자 가이드 (CLI Build Pipeline)

본 모드는 Creation Kit을 열지 않고 명령줄에서 100% 재현 가능한 빌드 파이프라인을 가집니다.

### 1. 플러그인 빌드 (`BankPrismUI.esp` & `SEQ`)
```powershell
cd EspGenerator
dotnet run
```
* Mutagen을 통해 글로벌 변수, 퀘스트, 다이얼로그, 불변조건 검사 및 `SEQ/BankPrismUI.seq`를 한 번에 생성합니다.

### 2. 파피루스 스크립트 컴파일 (`.psc` ➔ `.pex`)
> ⚠️ **주의사항**: Papyrus 소스는 반드시 **UTF-8 with BOM (`0xEF, 0xBB, 0xBF`)**으로 인코딩되어야 게임 내 한글이 깨지지 않습니다.

```bat
"C:\TAKEALOOK\mods\Creation Kit\Root\Papyrus Compiler\PapyrusCompiler.exe" ^
  "C:\TAKEALOOK\BankPrismUI\Scripts\Source" -all ^
  -f="C:\TAKEALOOK\mods\Papyrus Compiler\source\scripts\TESV_Papyrus_Flags.flg" ^
  -i="C:\TAKEALOOK\BankPrismUI\Scripts\Source;C:\TAKEALOOK\BankPrismUI\Scripts\Import;C:\TAKEALOOK\mods\Skyrim Script Extender (SKSE64)\Scripts\Source;C:\TAKEALOOK\mods\Papyrus Compiler\source\scripts;C:\TAKEALOOK\BankPrismUI\Scripts\Vanilla" ^
  -o="C:\TAKEALOOK\BankPrismUI\Scripts"
```

### 3. SKSE C++ 플러그인 빌드 (`BankPrismNative.dll`)
```bat
cd SKSE_Source
cmake -B build -G "Visual Studio 18 2026" -A x64
cmake --build build --config Release
```

### 4. Mod Organizer 2로 배포
```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1
```
* 컴파일된 런타임 파일(ESP, PEX, DLL, SEQ, Web Assets)들을 `C:\TAKEALOOK\mods\Financial Hell - Skyrim Banking and Credit Overhaul_dev build`로 완벽히 동기화합니다.
