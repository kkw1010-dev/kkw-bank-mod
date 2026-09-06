ScriptName BankPrismController extends Quest
{Controller for the bank and merchant credit system driven by PrismaUI.}

Bool Property bMenuOpen = False Auto
MiscObject Property Gold001 Auto

GlobalVariable Property BankBalance Auto
GlobalVariable Property BankDebt Auto
GlobalVariable Property MerchantCreditDebt Auto

Event OnInit()
    RegisterForModEvent("BankPrismAction", "OnBankPrismAction")
EndEvent

Function OpenBankMenu()
    ; Re-register on every open. A registration made only in OnInit is lost when the
    ; script is recompiled or the quest is reset, and the UI would then accept clicks
    ; that never reach Papyrus - which looks exactly like the mod being broken.
    RegisterForModEvent("BankPrismAction", "OnBankPrismAction")
    bMenuOpen = True
    BankPrismNative.OpenMenu()
    Refresh("")
EndFunction

Function CloseBankMenu()
    bMenuOpen = False
    BankPrismNative.CloseMenu()
EndFunction

Int Function GetGlobalInt(GlobalVariable akGlobal)
    If akGlobal
        Return akGlobal.GetValueInt()
    EndIf
    Return 0
EndFunction

; Pushes the current figures to the view, optionally with a status line.
Function Refresh(String asMessage)
    RefreshTx(asMessage, "", 0)
EndFunction

; As Refresh, but also reports a completed transaction so the view can log it.
; txType is empty for anything that did not move gold.
Function RefreshTx(String asMessage, String asTxType, Int aiTxAmount)
    Int playerGold = 0
    If Gold001
        playerGold = Game.GetPlayer().GetItemCount(Gold001)
    EndIf

    String jsonPayload = "{\"wallet\":" + playerGold         + ", \"balance\":" + GetGlobalInt(BankBalance)         + ", \"debt\":" + GetGlobalInt(BankDebt)         + ", \"creditDebt\":" + GetGlobalInt(MerchantCreditDebt)         + ", \"txType\":\"" + asTxType + "\""         + ", \"txAmount\":" + aiTxAmount         + ", \"message\":\"" + asMessage + "\"}"

    BankPrismNative.UpdateParams(jsonPayload)
EndFunction

Function UpdateBankUI()
    Refresh("")
EndFunction

Function Deposit(Int aiAmount, Int aiPlayerGold)
    If BankBalance == None || Gold001 == None
        Refresh("은행 계좌를 사용할 수 없습니다.")
    ElseIf aiAmount <= 0
        Refresh("금액을 확인해 주세요.")
    ElseIf aiPlayerGold < aiAmount
        Refresh("지갑에 골드가 부족합니다.")
    Else
        ; Take the gold only after every check has passed. Removing it first meant a
        ; failed check destroyed the player's gold without crediting the account.
        Game.GetPlayer().RemoveItem(Gold001, aiAmount, True)
        BankBalance.SetValueInt(BankBalance.GetValueInt() + aiAmount)
        RefreshTx(aiAmount + " 골드를 입금했습니다.", "deposit", aiAmount)
    EndIf
EndFunction

Function Withdraw(Int aiAmount)
    If BankBalance == None || Gold001 == None
        Refresh("은행 계좌를 사용할 수 없습니다.")
    ElseIf aiAmount <= 0
        Refresh("금액을 확인해 주세요.")
    ElseIf BankBalance.GetValueInt() < aiAmount
        Refresh("예금 잔고가 부족합니다.")
    Else
        BankBalance.SetValueInt(BankBalance.GetValueInt() - aiAmount)
        Game.GetPlayer().AddItem(Gold001, aiAmount, True)
        RefreshTx(aiAmount + " 골드를 출금했습니다.", "withdraw", aiAmount)
    EndIf
EndFunction

; Provisional: settles as much merchant credit as the player can actually pay,
; wallet first and then the account. The final rules wait on the credit design.
Function PayMerchantCredit(Int aiPlayerGold)
    If MerchantCreditDebt == None
        Refresh("외상 정보를 사용할 수 없습니다.")
        Return
    EndIf

    Int owed = MerchantCreditDebt.GetValueInt()
    If owed <= 0
        Refresh("상환할 외상금이 없습니다.")
        Return
    EndIf

    Int fromWallet = aiPlayerGold
    If fromWallet > owed
        fromWallet = owed
    EndIf

    Int remaining = owed - fromWallet
    Int fromBank = GetGlobalInt(BankBalance)
    If fromBank > remaining
        fromBank = remaining
    EndIf

    Int paid = fromWallet + fromBank
    If paid <= 0
        Refresh("상환할 골드가 없습니다.")
        Return
    EndIf

    If fromWallet > 0
        Game.GetPlayer().RemoveItem(Gold001, fromWallet, True)
    EndIf
    If fromBank > 0
        BankBalance.SetValueInt(BankBalance.GetValueInt() - fromBank)
    EndIf
    MerchantCreditDebt.SetValueInt(owed - paid)

    If owed - paid > 0
        RefreshTx(paid + " 골드를 상환했습니다. 남은 외상금 " + (owed - paid) + " 골드.", "payCredit", paid)
    Else
        RefreshTx("외상금을 모두 상환했습니다.", "payCredit", paid)
    EndIf
EndFunction

Event OnBankPrismAction(String eventName, String strArg, Float numArg, Form sender)
    Int amount = Math.Floor(numArg)
    Int playerGold = 0
    If Gold001
        playerGold = Game.GetPlayer().GetItemCount(Gold001)
    EndIf

    If strArg == "close"
        CloseBankMenu()
    ElseIf strArg == "deposit"
        Deposit(amount, playerGold)
    ElseIf strArg == "withdraw"
        Withdraw(amount)
    ElseIf strArg == "payCredit"
        PayMerchantCredit(playerGold)
    ElseIf strArg == "sellBond"
        Refresh("채권 매각은 서드파티 연동 후 사용할 수 있습니다.")
    EndIf
EndEvent
