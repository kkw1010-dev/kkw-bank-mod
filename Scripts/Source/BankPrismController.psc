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

; --- loans ---------------------------------------------------------------
GlobalVariable[] Property LoanDue Auto
GlobalVariable[] Property LoanPrincipal Auto
Book[] Property DunningLetters Auto

GlobalVariable Property LoanTier1 Auto
GlobalVariable Property LoanTier2 Auto
GlobalVariable Property LoanTier3 Auto
GlobalVariable Property LoanTermDays Auto
GlobalVariable Property OverduePercent Auto

Quest Property MQWayOfTheVoice Auto
Quest Property MQBladeInTheDark Auto
Quest Property MQAlduinsBane Auto

WICourierScript Property Courier Auto
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

; ASCII key for the view: picks the hold's background art and avoids putting a
; non-ASCII filename in a URL.
String Function GetHoldKey(Int aiHold)
    If aiHold == 0
        Return "whiterun"
    ElseIf aiHold == 1
        Return "haafingar"
    ElseIf aiHold == 2
        Return "eastmarch"
    ElseIf aiHold == 3
        Return "rift"
    ElseIf aiHold == 4
        Return "reach"
    ElseIf aiHold == 5
        Return "falkreath"
    ElseIf aiHold == 6
        Return "hjaalmarch"
    ElseIf aiHold == 7
        Return "pale"
    ElseIf aiHold == 8
        Return "winterhold"
    EndIf
    Return "whiterun"
EndFunction

; Whole days until this hold's loan falls due; negative once it is overdue.
Int Function GetLoanDaysLeft()
    If !HoldIsValid(CurrentHold) || LoanDue == None
        Return 0
    EndIf
    Float dueAt = LoanDue[CurrentHold].GetValue()
    If dueAt <= 0.0
        Return 0
    EndIf
    Return Math.Floor(dueAt - Utility.GetCurrentGameTime())
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
    AccrueOverdue(CurrentHold)
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
        + ", \"loanLimit\":" + GetLoanLimit() \
        + ", \"loanDaysLeft\":" + GetLoanDaysLeft() \
        + ", \"hold\":\"" + GetHoldName(CurrentHold) + "\"" \
        + ", \"holdKey\":\"" + GetHoldKey(CurrentHold) + "\"" \
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
; Loans
; ---------------------------------------------------------------------------

; No lender fronts gold to an unproven sellsword - the trade is too close to a
; mercenary's for anyone's comfort. Standing is read off the main quest: nothing at
; all until the Greybeards acknowledge the player, and it climbs from there.
Int Function GetLoanLimit()
    Int limit = 0
    If MQWayOfTheVoice && MQWayOfTheVoice.IsCompleted()
        limit = GetGlobalInt(LoanTier1)
    EndIf
    If MQBladeInTheDark && MQBladeInTheDark.IsCompleted()
        limit = GetGlobalInt(LoanTier2)
    EndIf
    If MQAlduinsBane && MQAlduinsBane.IsCompleted()
        limit = GetGlobalInt(LoanTier3)
    EndIf
    Return limit
EndFunction

Int Function GetLoanTermDays()
    Int term = GetGlobalInt(LoanTermDays)
    If term <= 0
        term = 7
    EndIf
    Return term
EndFunction

; Charges every whole term that has passed since the loan came due, moving the due
; date forward as it goes so the same week is never billed twice. The charge is a
; share of the original sum, not of the running total, so the debt grows in a
; straight line instead of compounding away.
Function AccrueOverdue(Int aiHold)
    If !HoldIsValid(aiHold) || LoanDue == None || LoanPrincipal == None
        Return
    EndIf

    GlobalVariable due = LoanDue[aiHold]
    GlobalVariable ledger = BankDebts[aiHold]
    Float dueAt = due.GetValue()

    If dueAt <= 0.0 || ledger.GetValueInt() <= 0
        Return
    EndIf

    Int term = GetLoanTermDays()
    Int pct = GetGlobalInt(OverduePercent)
    Int principal = LoanPrincipal[aiHold].GetValueInt()
    Int charge = (principal * pct) / 100
    Float now = Utility.GetCurrentGameTime()
    Int weeks = 0

    While now >= dueAt + term && weeks < 52
        ledger.SetValueInt(ledger.GetValueInt() + charge)
        dueAt += term
        weeks += 1
    EndWhile

    If weeks > 0
        due.SetValue(dueAt)
        Debug.Notification(GetHoldName(aiHold) + " 채무가 연체되어 " + (charge * weeks) + " 골드가 더해졌습니다.")
    EndIf
EndFunction

Bool Function IsOverdue(Int aiHold)
    If !HoldIsValid(aiHold) || LoanDue == None
        Return False
    EndIf
    Float dueAt = LoanDue[aiHold].GetValue()
    Return dueAt > 0.0 && BankDebts[aiHold].GetValueInt() > 0 && Utility.GetCurrentGameTime() >= dueAt
EndFunction

Bool Function AnyLoanOutstanding()
    Int i = 0
    While i < BankDebts.Length
        If BankDebts[i].GetValueInt() > 0
            Return True
        EndIf
        i += 1
    EndWhile
    Return False
EndFunction

Function TakeLoan(Int aiAmount)
    If !HoldIsValid(CurrentHold) || Gold001 == None
        Refresh("대출을 취급할 수 없습니다.")
        Return
    EndIf

    GlobalVariable ledger = BankDebts[CurrentHold]
    If ledger.GetValueInt() > 0
        Refresh("이미 갚지 않은 대출이 있습니다.")
        Return
    EndIf

    Int limit = GetLoanLimit()
    If limit <= 0
        Refresh("이름 없는 칼잡이에게 내어줄 돈은 없다고 합니다.")
        Return
    EndIf
    If aiAmount <= 0
        Refresh("금액을 선택해 주세요.")
        Return
    EndIf
    If aiAmount > limit
        Refresh("대출 한도는 " + limit + " 골드입니다.")
        Return
    EndIf

    ; One markup, applied once at signing. Nothing accrues until the term runs out.
    Int pct = GetSurchargePercent()
    Int owed = aiAmount + ((aiAmount * pct) / 100)

    LoanPrincipal[CurrentHold].SetValueInt(aiAmount)
    ledger.SetValueInt(owed)
    LoanDue[CurrentHold].SetValue(Utility.GetCurrentGameTime() + GetLoanTermDays())

    Game.GetPlayer().AddItem(Gold001, aiAmount, True)
    ScheduleDunningRun()

    RefreshTx(aiAmount + " 골드를 빌렸습니다. " + GetLoanTermDays() + "일 안에 " + owed + " 골드를 갚아야 합니다.", "borrow", aiAmount)
EndFunction

Function RepayLoan(Int aiAmount, Int aiPlayerGold)
    If !HoldIsValid(CurrentHold) || Gold001 == None
        Refresh("대출을 취급할 수 없습니다.")
        Return
    EndIf

    GlobalVariable ledger = BankDebts[CurrentHold]
    Int owed = ledger.GetValueInt()
    If owed <= 0
        Refresh("갚을 대출이 없습니다.")
        Return
    EndIf

    Int pay = aiAmount
    If pay <= 0 || pay > owed
        pay = owed
    EndIf

    Int fromWallet = aiPlayerGold
    If fromWallet > pay
        fromWallet = pay
    EndIf

    GlobalVariable acct = BankBalances[CurrentHold]
    Int fromBank = acct.GetValueInt()
    If fromBank > pay - fromWallet
        fromBank = pay - fromWallet
    EndIf

    Int paid = fromWallet + fromBank
    If paid <= 0
        Refresh("갚을 골드가 없습니다.")
        Return
    EndIf

    If fromWallet > 0
        Game.GetPlayer().RemoveItem(Gold001, fromWallet, True)
    EndIf
    If fromBank > 0
        acct.SetValueInt(acct.GetValueInt() - fromBank)
    EndIf
    ledger.SetValueInt(owed - paid)

    If owed - paid <= 0
        LoanDue[CurrentHold].SetValue(0.0)
        LoanPrincipal[CurrentHold].SetValueInt(0)
        RefreshTx("대출을 모두 갚았습니다.", "repay", paid)
    Else
        RefreshTx(paid + " 골드를 갚았습니다. 남은 채무 " + (owed - paid) + " 골드.", "repay", paid)
    EndIf
EndFunction

; ---------------------------------------------------------------------------
; Dunning letters
; ---------------------------------------------------------------------------

; The vanilla courier carries them: a letter goes into its container and it finds the
; player in the next town. A daily check runs only while a loan is outstanding and
; stops itself once the ledger is clear.
Function ScheduleDunningRun()
    RegisterForSingleUpdateGameTime(1.0)
EndFunction

Function SendDunningLetter(Int aiHold)
    If Courier == None || DunningLetters == None || aiHold >= DunningLetters.Length
        Return
    EndIf
    Courier.addItemToContainer(DunningLetters[aiHold], 1)
EndFunction

Event OnUpdateGameTime()
    Int i = 0
    While i < BankDebts.Length
        AccrueOverdue(i)
        If IsOverdue(i)
            SendDunningLetter(i)
        EndIf
        i += 1
    EndWhile

    If AnyLoanOutstanding()
        ScheduleDunningRun()
    EndIf
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
    ElseIf strArg == "borrow"
        TakeLoan(amount)
    ElseIf strArg == "repay"
        RepayLoan(amount, playerGold)
    ElseIf strArg == "payCredit"
        PayMerchantCredit(playerGold)
    ElseIf strArg == "sellBond"
        Refresh("채권 매각은 서드파티 연동 후 사용할 수 있습니다.")
    EndIf
EndEvent
