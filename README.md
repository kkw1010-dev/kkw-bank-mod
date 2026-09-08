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
1. 인게임에서 행정관(Steward) 등의 NPC와 대화하여 은행 UI 열기 호출.
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

## Building

The mod is produced entirely from the command line; the Creation Kit is never opened.

### 1. Plugin (`BankPrismUI.esp`)

```
cd EspGenerator && dotnet run
```

`EspGenerator` uses Mutagen to write the globals, the controller quest and the
steward dialogue, then reads the file back and prints what it wrote. Two things
are easy to get wrong here and are deliberately handled in code:

- `Global` is abstract in Mutagen, so `Globals.AddNew()` throws at runtime.
  `GlobalFloat` is constructed directly instead.
- Mutagen defaults to Windows-1252, which replaces every Hangul character with
  `?`. Strings are written as UTF-8, matching the Korean translation mods in
  this setup.

### 2. Papyrus scripts

```
"C:\TAKEALOOK\mods\Creation Kit\Root\Papyrus Compiler\PapyrusCompiler.exe" ^
  "C:\TAKEALOOK\BankPrismUI\Scripts\Source" -all ^
  -f="C:\TAKEALOOK\mods\Papyrus Compiler\source\scripts\TESV_Papyrus_Flags.flg" ^
  -i="C:\TAKEALOOK\BankPrismUI\Scripts\Source;C:\TAKEALOOK\BankPrismUI\Scripts\Import;C:\TAKEALOOK\mods\Skyrim Script Extender (SKSE64)\Scripts\Source;C:\TAKEALOOK\mods\Papyrus Compiler\source\scripts;C:\TAKEALOOK\BankPrismUI\Scripts\Vanilla" ^
  -o="C:\TAKEALOOK\BankPrismUI\Scripts"
```

**The SKSE source folder must come before `Scripts\Vanilla`.** The Creation Kit's
own copies of `Form.psc`, `Quest.psc` and friends have no SKSE additions, so if the
vanilla tree is searched first the compiler stops recognising `RegisterForModEvent`,
`RegisterForKey` and `RegisterForMenu`, and every script in this mod fails to build.

Papyrus sources **must be saved as UTF-8 with a BOM**. The compiler reads a
BOM-less UTF-8 file as the system ANSI codepage, which turns every Korean string
into unrelated CJK characters in the `.pex` while still reporting a successful
compile. Compiling the same string from three encodings gives:

| source encoding | compiles | Korean in `.pex` |
| --- | --- | --- |
| UTF-8, no BOM | fails, or silently mangles | broken |
| UTF-8 with BOM | yes | correct |
| cp949 | yes | correct |

`Scripts/Import` holds vanilla sources the project compiles against but does not
ship - currently `WICourierScript.psc`, needed to hand a letter to the courier.
It is not in the partial extraction under `mods/Papyrus Compiler`, so pull it out
of the Creation Kit archive and it stays out of git:

```
python -c "import zipfile,os; z=zipfile.ZipFile(r'C:\TAKEALOOK\mods\Creation Kit\Root\Data\Scripts.zip'); open(r'C:\TAKEALOOK\BankPrismUI\Scripts\Import\WICourierScript.psc','wb').write(z.read('Source/Scripts/WICourierScript.psc'))"
```

`Scripts/Vanilla` is the whole vanilla source tree, needed because the standing
system reads two vanilla quest scripts directly - `CWScript` for the player's Civil
War rank and `FavorJarlsMakeFriendsScript` for thanehood. Referencing either type
pulls in a dependency chain the partial extraction under `mods/Papyrus Compiler`
cannot close, so extract all of it. It is also gitignored, and compiling against
the full tree costs about a second:

```
python -c "import zipfile,os; z=zipfile.ZipFile(r'C:\TAKEALOOK\mods\Creation Kit\Root\Data\Scripts.zip'); d=r'C:\TAKEALOOK\BankPrismUI\Scripts\Vanilla'; os.makedirs(d,exist_ok=True); [open(os.path.join(d,n.split('/')[-1]),'wb').write(z.read(n)) for n in z.namelist() if n.lower().endswith('.psc')]"
```

Only `Scripts/Source` is compiled, so nothing in `Import` or `Vanilla` produces a
.pex that could overwrite the game's own.

### 3. SKSE plugin (`BankPrismNative.dll`)

Requires Visual Studio 2026 Build Tools and CommonLibSSE-NG. The library is
vendored at `SKSE_Source/extern/CommonLibSSE-NG` and kept out of git; copy it
from a project that already has it, or clone CommonLibSSE-NG there.

```
cd SKSE_Source
cmake -B build -G "Visual Studio 18 2026" -A x64
cmake --build build --config Release
```

The build copies the DLL into `SKSE/Plugins/` so the game can never load a stale
one from a previous build.

### 4. Deploy to Mod Organizer 2

```
powershell -File deploy.ps1
```

Copies the runtime files to `C:\TAKEALOOK\mods\BankPrismUI`. Enabling the mod
and the plugin in Mod Organizer 2 is done in its interface.

Writing there directly bypasses MO2, so the CRDW Auto Mode plugin has no reason
to rebuild its directory cache and the next launch runs in LOAD mode against a
stale index. That cache holds the file index for `Data\Scripts`, not file
contents, so overwriting an existing `.pex` is harmless - but a `.pex` that is
new or renamed would be absent from the index, and the engine would never learn
it exists. No error, no log line. The script therefore:

- deletes `data_scripts_.cache` **only** when the set of paths under `Scripts\`
  changed, since invalidating costs a full rebuild of that cache;
- otherwise reads the cache and fails if a deployed `.pex` is missing from it,
  so the gap between "deployed" and "the game can see it" closes here rather
  than in a play session;
- does neither, without failing, where CRDW is not installed.

Papyrus sources are not deployed - the game never reads them and they only widen
the index. Pass `-IncludeSources` to ship them anyway, which is what a public
release would conventionally do.
