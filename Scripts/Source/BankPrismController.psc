ScriptName BankPrismController extends Quest
{Per-hold bank and merchant credit, driven by PrismaUI.}

; Accounts are per hold, indexed the same way as HoldCrimeFactions. The hold is
; resolved from the speaker's crime faction, which is the value Skyrim's own bounty
; system uses, so jurisdiction lines up with the game's.
GlobalVariable[] Property BankBalances Auto
GlobalVariable[] Property BankDebts Auto
GlobalVariable[] Property CreditDebts Auto
FormList Property HoldCrimeFactions Auto

GlobalVariable Property MerchantCreditLimit Auto
GlobalVariable Property CreditSurcharge Auto

; Test shortcut. DirectX scan code; 210 is Insert. Set to 0 to disable.
GlobalVariable Property DebugHotkey Auto

MiscObject Property Gold001 Auto

Bool Property bMenuOpen = False Auto
Int Property CurrentHold = 0 Auto Hidden

; Set while a credit barter is in flight, so OnMenuClose knows the BarterMenu it sees
; is ours, which hold it belongs to, and how much was lent.
Bool Property bCreditBarterActive = False Auto Hidden
Int Property CreditLentAmount = 0 Auto Hidden
Int Property CreditGoldBefore = 0 Auto Hidden
Int Property CreditHold = 0 Auto Hidden

Event OnInit()
    RegisterForModEvent("BankPrismAction", "OnBankPrismAction")
    RegisterDebugHotkey()
EndEvent

; A hotkey costs ten lines and no dependencies. An MCM would need MCM Helper plus a
; config json, and SKSE Menu Framework would need ImGui integration in the plugin.
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
    ; No speaker here, so the shortcut reopens whichever hold was last used.
    ShowBank()
EndEvent

; ---------------------------------------------------------------------------
; Holds
; ---------------------------------------------------------------------------

Int Function ResolveHold(Actor akSpeaker)
    If akSpeaker == None || HoldCrimeFactions == None
        Return -1
    EndIf
    Faction crime = akSpeaker.GetCrimeFaction()
    If crime == None
        Return -1
    EndIf
    Return HoldCrimeFactions.Find(crime)
EndFunction

String Function GetHoldName(Int aiHold)
    If aiHold == 0
        Return "화이트런"
    ElseIf aiHold == 1
        Return "하핑가"
    ElseIf aiHold == 2
        Return "이스트마치"
    ElseIf aiHold == 3
        Return "리프트"
    ElseIf aiHold == 4
        Return "리치"
    ElseIf aiHold == 5
        Return "팔크리스"
    ElseIf aiHold == 6
        Return "햘마치"
    ElseIf aiHold == 7
        Return "페일"
    ElseIf aiHold == 8
        Return "윈터홀드"
    EndIf
    Return "알 수 없는 지역"
EndFunction

Bool Function HoldIsValid(Int aiHold)
    Return aiHold >= 0 && BankBalances && aiHold < BankBalances.Length
EndFunction

Int Function GetGlobalInt(GlobalVariable akGlobal)
    If akGlobal
        Return akGlobal.GetValueInt()
    EndIf
    Return 0
EndFunction

; ---------------------------------------------------------------------------
; Menu
; ---------------------------------------------------------------------------

Function OpenBankMenu(Actor akSpeaker)
    Int hold = ResolveHold(akSpeaker)
    If !HoldIsValid(hold)
        Debug.Notification("이 곳에서는 은행 업무를 볼 수 없습니다.")
        Return
    EndIf
    CurrentHold = hold
    ShowBank()
EndFunction

Function ShowBank()
    ; Re-register on every open. A registration made only in OnInit is lost when the
    ; script is recompiled, and the UI would then accept clicks that never arrive.
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

    Int balance = 0
    Int debt = 0
    Int credit = 0
    If HoldIsValid(CurrentHold)
        balance = GetGlobalInt(BankBalances[CurrentHold])
        debt = GetGlobalInt(BankDebts[CurrentHold])
        credit = GetGlobalInt(CreditDebts[CurrentHold])
    EndIf

    String jsonPayload = "{\"wallet\":" + playerGold \
        + ", \"balance\":" + balance \
        + ", \"debt\":" + debt \
        + ", \"creditDebt\":" + credit \
        + ", \"hold\":\"" + GetHoldName(CurrentHold) + "\"" \
        + ", \"txType\":\"" + asTxType + "\"" \
        + ", \"txAmount\":" + aiTxAmount \
        + ", \"message\":\"" + asMessage + "\"}"

    BankPrismNative.UpdateParams(jsonPayload)
EndFunction

; ---------------------------------------------------------------------------
; Deposits and withdrawals
; ---------------------------------------------------------------------------

Function Deposit(Int aiAmount, Int aiPlayerGold)
    If !HoldIsValid(CurrentHold) || Gold001 == None
        Refresh("은행 계좌를 사용할 수 없습니다.")
    ElseIf aiAmount <= 0
        Refresh("금액을 확인해 주세요.")
    ElseIf aiPlayerGold < aiAmount
        Refresh("지갑에 골드가 부족합니다.")
    Else
        ; Take the gold only after every check has passed. Removing it first meant a
        ; failed check destroyed the player's gold without crediting the account.
        Game.GetPlayer().RemoveItem(Gold001, aiAmount, True)
        GlobalVariable acct = BankBalances[CurrentHold]
        acct.SetValueInt(acct.GetValueInt() + aiAmount)
        RefreshTx(aiAmount + " 골드를 입금했습니다.", "deposit", aiAmount)
    EndIf
EndFunction

Function Withdraw(Int aiAmount)
    If !HoldIsValid(CurrentHold) || Gold001 == None
        Refresh("은행 계좌를 사용할 수 없습니다.")
        Return
    EndIf

    GlobalVariable acct = BankBalances[CurrentHold]
    If aiAmount <= 0
        Refresh("금액을 확인해 주세요.")
    ElseIf acct.GetValueInt() < aiAmount
        Refresh("예금 잔고가 부족합니다.")
    Else
        acct.SetValueInt(acct.GetValueInt() - aiAmount)
        Game.GetPlayer().AddItem(Gold001, aiAmount, True)
        RefreshTx(aiAmount + " 골드를 출금했습니다.", "withdraw", aiAmount)
    EndIf
EndFunction

; Settles as much of this hold's merchant credit as the player can pay, wallet first
; and then the account.
Function PayMerchantCredit(Int aiPlayerGold)
    If !HoldIsValid(CurrentHold)
        Refresh("외상 정보를 사용할 수 없습니다.")
        Return
    EndIf

    GlobalVariable ledger = CreditDebts[CurrentHold]
    Int owed = ledger.GetValueInt()
    If owed <= 0
        Refresh("상환할 외상금이 없습니다.")
        Return
    EndIf

    Int fromWallet = aiPlayerGold
    If fromWallet > owed
        fromWallet = owed
    EndIf

    Int remaining = owed - fromWallet
    GlobalVariable acct = BankBalances[CurrentHold]
    Int fromBank = acct.GetValueInt()
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
        acct.SetValueInt(acct.GetValueInt() - fromBank)
    EndIf
    ledger.SetValueInt(owed - paid)

    If owed - paid > 0
        RefreshTx(paid + " 골드를 상환했습니다. 남은 외상금 " + (owed - paid) + " 골드.", "payCredit", paid)
    Else
        RefreshTx("외상금을 모두 상환했습니다.", "payCredit", paid)
    EndIf
EndFunction

; ---------------------------------------------------------------------------
; Merchant credit
; ---------------------------------------------------------------------------

; Credit lends the player gold, lets them use the ordinary vanilla barter window, and
; converts whatever they actually spent into debt when the window closes. Copying the
; merchant's stock into our own container would lose restocking, speech-perk pricing,
; the merchant's own gold, and selling back to them.
Function BeginCreditBarter(Actor akMerchant)
    If akMerchant == None || Gold001 == None
        Debug.Notification("외상 거래를 사용할 수 없습니다.")
        Return
    EndIf
    If bCreditBarterActive
        Return
    EndIf

    Int hold = ResolveHold(akMerchant)
    If !HoldIsValid(hold)
        Debug.Notification("이 상인에게는 외상을 달 수 없습니다.")
        Return
    EndIf

    Int limit = 1000
    If MerchantCreditLimit
        limit = MerchantCreditLimit.GetValueInt()
    EndIf

    Int headroom = limit - CreditDebts[hold].GetValueInt()
    If headroom <= 0
        Debug.Notification(GetHoldName(hold) + " 외상 한도를 모두 사용했습니다.")
        Return
    EndIf

    ; The limit caps the debt, not the spend. Since the ledger records the purchase
    ; with the markup added, lend only what still fits under the limit afterwards.
    Int lend = (headroom * 100) / (100 + GetSurchargePercent())
    If lend <= 0
        Debug.Notification(GetHoldName(hold) + " 외상 한도가 거의 남지 않았습니다.")
        Return
    EndIf

    Actor player = Game.GetPlayer()
    CreditGoldBefore = player.GetItemCount(Gold001)
    CreditLentAmount = lend
    CreditHold = hold
    bCreditBarterActive = True

    RegisterForMenu("BarterMenu")
    player.AddItem(Gold001, lend, True)
    akMerchant.ShowBarterMenu()
EndFunction

Int Function GetSurchargePercent()
    If CreditSurcharge
        Return CreditSurcharge.GetValueInt()
    EndIf
    Return 20
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

    Int spent = (CreditGoldBefore + lent) - goldAfter
    If spent < 0
        spent = 0
    ElseIf spent > lent
        spent = lent
    EndIf

    If reclaim > 0
        player.RemoveItem(Gold001, reclaim, True)
    EndIf

    If spent > 0
        ; One markup, applied once, at the moment the purchase is written to the ledger.
        Int pct = GetSurchargePercent()
        Int charged = spent + ((spent * pct) / 100)
        GlobalVariable ledger = CreditDebts[CreditHold]
        ledger.SetValueInt(ledger.GetValueInt() + charged)
        Debug.Notification(GetHoldName(CreditHold) + " 외상 " + charged + " 골드 (" + pct + "% 가산).")
    Else
        Debug.Notification("외상으로 구매한 물건이 없습니다.")
    EndIf

    CreditLentAmount = 0
    CreditGoldBefore = 0
EndEvent

; ---------------------------------------------------------------------------
; UI events
; ---------------------------------------------------------------------------

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
