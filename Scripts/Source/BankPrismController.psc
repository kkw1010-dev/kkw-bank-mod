ScriptName BankPrismController extends Quest
{Controller for Bank & Merchant Credit System Prisma UI}

Bool Property bMenuOpen = False Auto

; Global variables placeholder (Needs to be linked in CK)
GlobalVariable Property BankBalance Auto
GlobalVariable Property BankDebt Auto
GlobalVariable Property MerchantCreditDebt Auto

Event OnInit()
    RegisterForModEvent("BankPrismAction", "OnBankPrismAction")
EndEvent

Function OpenBankMenu()
    bMenuOpen = True
    BankPrismNative.OpenMenu()
    UpdateBankUI()
EndFunction

Function CloseBankMenu()
    bMenuOpen = False
    BankPrismNative.CloseMenu()
EndFunction

Function UpdateBankUI()
    Int balance = 0
    Int debt = 0
    Int creditDebt = 0
    
    If BankBalance
        balance = BankBalance.GetValueInt()
    EndIf
    If BankDebt
        debt = BankDebt.GetValueInt()
    EndIf
    If MerchantCreditDebt
        creditDebt = MerchantCreditDebt.GetValueInt()
    EndIf
    
    String jsonPayload = "{\"balance\":" + balance + ", \"debt\":" + debt + ", \"creditDebt\":" + creditDebt + "}"
    BankPrismNative.UpdateParams(jsonPayload)
EndFunction

Event OnBankPrismAction(String eventName, String strArg, Float numArg, Form sender)
    If strArg == "close"
        CloseBankMenu()
    ElseIf strArg == "payCredit"
        Debug.Notification("외상값을 상환합니다.")
        If MerchantCreditDebt
            MerchantCreditDebt.SetValueInt(0)
        EndIf
        UpdateBankUI()
    ElseIf strArg == "sellBond"
        Debug.Notification("채권을 판매합니다. 서드파티 모드를 연동합니다.")
        ; TODO: 연동할 서드파티 모드 관련 Global 변수 초기화 및 이벤트 호출 로직 추가
        ; 예시:
        ; GlobalVariable varBond = Game.GetFormFromFile(0x12345, "ThirdPartyMod.esp") as GlobalVariable
        ; If varBond
        ;     varBond.SetValue(0)
        ; EndIf
        ; SendModEvent("ThirdPartyBondSellEvent")
    EndIf
EndEvent
