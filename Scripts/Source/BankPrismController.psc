ScriptName BankPrismController extends Quest
{Controller for Bank & Merchant Credit System Prisma UI}

Bool Property bMenuOpen = False Auto
MiscObject Property Gold001 Auto ; 플레이어 골드 아이템 (CK에서 0x0000000F 연결)

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
    Int playerGold = Game.GetPlayer().GetItemCount(Gold001)
    
    If BankBalance
        balance = BankBalance.GetValueInt()
    EndIf
    If BankDebt
        debt = BankDebt.GetValueInt()
    EndIf
    If MerchantCreditDebt
        creditDebt = MerchantCreditDebt.GetValueInt()
    EndIf
    
    String jsonPayload = "{\"wallet\":" + playerGold + ", \"balance\":" + balance + ", \"debt\":" + debt + ", \"creditDebt\":" + creditDebt + "}"
    BankPrismNative.UpdateParams(jsonPayload)
EndFunction

Event OnBankPrismAction(String eventName, String strArg, Float numArg, Form sender)
    Int amount = Math.Floor(numArg)
    Int playerGold = Game.GetPlayer().GetItemCount(Gold001)

    If strArg == "close"
        CloseBankMenu()
        
    ElseIf strArg == "deposit"
        If amount > 0 && playerGold >= amount
            Game.GetPlayer().RemoveItem(Gold001, amount, True)
            If BankBalance
                BankBalance.SetValueInt(BankBalance.GetValueInt() + amount)
            EndIf
            Debug.Notification(amount + " 골드를 입금했습니다.")
            UpdateBankUI()
        Else
            Debug.Notification("입금할 골드가 부족합니다.")
        EndIf

    ElseIf strArg == "withdraw"
        If amount > 0 && BankBalance && BankBalance.GetValueInt() >= amount
            BankBalance.SetValueInt(BankBalance.GetValueInt() - amount)
            Game.GetPlayer().AddItem(Gold001, amount, True)
            Debug.Notification(amount + " 골드를 출금했습니다.")
            UpdateBankUI()
        Else
            Debug.Notification("은행 잔고가 부족합니다.")
        EndIf

    ElseIf strArg == "payCredit"
        If MerchantCreditDebt
            MerchantCreditDebt.SetValueInt(0)
            Debug.Notification("외상값을 상환합니다.")
        EndIf
        UpdateBankUI()
        
    ElseIf strArg == "sellBond"
        Debug.Notification("채권을 판매합니다. 서드파티 모드를 연동합니다.")
    EndIf
EndEvent
