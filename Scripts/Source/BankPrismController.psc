ScriptName BankPrismController extends Quest
{Controller for the bank and merchant credit system driven by PrismaUI.}

Bool Property bMenuOpen = False Auto
MiscObject Property Gold001 Auto

GlobalVariable Property BankBalance Auto
GlobalVariable Property BankDebt Auto
GlobalVariable Property MerchantCreditDebt Auto
GlobalVariable Property MerchantCreditLimit Auto

; Test shortcut. DirectX scan code; 210 is Insert. Set to 0 to disable.
GlobalVariable Property DebugHotkey Auto

; Set while a credit barter is in flight, so OnMenuClose knows the BarterMenu it sees
; is ours and how much was lent. Hidden properties so they survive a save.
Bool Property bCreditBarterActive = False Auto Hidden
Int Property CreditLentAmount = 0 Auto Hidden
Int Property CreditGoldBefore = 0 Auto Hidden

Event OnInit()
    RegisterForModEvent("BankPrismAction", "OnBankPrismAction")
    RegisterDebugHotkey()
EndEvent

; A hotkey costs ten lines and no dependencies. An MCM would need MCM Helper plus a
; config json, and SKSE Menu Framework would need ImGui integration in the plugin -
; both far more machinery than opening the bank for a test is worth.
Function RegisterDebugHotkey()
    ; Not named 'key': Key is an existing Skyrim script type and the name is reserved.
    Int iKeyCode = 210
    If DebugHotkey
        iKeyCode = DebugHotkey.GetValueInt()
    EndIf
    If iKeyCode > 0
        RegisterForKey(iKeyCode)
    EndIf
EndFunction

Event OnKeyDown(Int aiKeyCode)
    If bMenuOpen || Utility.IsInMenuMode()
        Return
    EndIf
    OpenBankMenu()
EndEvent

Function OpenBankMenu()
    ; Re-register on every open. A registration made only in OnInit is lost when the
    ; script is recompiled or the quest is reset, and the UI would then accept clicks
    ; that never reach Papyrus - which looks exactly like the mod being broken.
    RegisterForModEvent("BankPrismAction", "OnBankPrismAction")
    RegisterDebugHotkey()
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

; Merchant credit works by lending the player gold, letting them use the ordinary
; vanilla barter window, and converting whatever they actually spent into debt when
; the window closes. Copying the merchant's stock into our own container would lose
; restocking, speech-perk pricing, the merchant's own gold, and selling back to them.
Function BeginCreditBarter(Actor akMerchant)
    If akMerchant == None || Gold001 == None || MerchantCreditDebt == None
        Debug.Notification("외상 거래를 사용할 수 없습니다.")
        Return
    EndIf
    If bCreditBarterActive
        Return
    EndIf

    Int limit = 1000
    If MerchantCreditLimit
        limit = MerchantCreditLimit.GetValueInt()
    EndIf

    Int available = limit - MerchantCreditDebt.GetValueInt()
    If available <= 0
        Debug.Notification("외상 한도를 모두 사용했습니다.")
        Return
    EndIf

    Actor player = Game.GetPlayer()
    CreditGoldBefore = player.GetItemCount(Gold001)
    CreditLentAmount = available
    bCreditBarterActive = True

    RegisterForMenu("BarterMenu")
    player.AddItem(Gold001, available, True)
    akMerchant.ShowBarterMenu()
EndFunction

Event OnMenuClose(String menuName)
    If menuName != "BarterMenu" || !bCreditBarterActive
        Return
    EndIf

    UnregisterForMenu("BarterMenu")
    bCreditBarterActive = False

    Actor player = Game.GetPlayer()
    Int goldAfter = player.GetItemCount(Gold001)
    Int lent = CreditLentAmount

    ; Credit is spent before the player's own gold. Anything of the loan still in the
    ; purse is taken back; the rest becomes debt. Selling to the merchant leaves the
    ; player richer than they started, and that income is theirs to keep.
    Int reclaim = goldAfter - CreditGoldBefore
    If reclaim < 0
        reclaim = 0
    ElseIf reclaim > lent
        reclaim = lent
    EndIf

    Int owed = (CreditGoldBefore + lent) - goldAfter
    If owed < 0
        owed = 0
    ElseIf owed > lent
        owed = lent
    EndIf

    If reclaim > 0
        player.RemoveItem(Gold001, reclaim, True)
    EndIf

    If owed > 0
        MerchantCreditDebt.SetValueInt(MerchantCreditDebt.GetValueInt() + owed)
        Debug.Notification(owed + " 골드를 외상으로 달았습니다.")
    Else
        Debug.Notification("외상으로 구매한 물건이 없습니다.")
    EndIf

    CreditLentAmount = 0
    CreditGoldBefore = 0
EndEvent

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
