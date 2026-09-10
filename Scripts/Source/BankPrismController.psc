ScriptName BankPrismController extends Quest
{Per-hold bank and merchant credit, driven by PrismaUI.}

; Accounts are per hold, indexed the same way as HoldCrimeFactions. The hold is
; resolved from the speaker's crime faction, which is the value Skyrim's own bounty
; system uses, so jurisdiction lines up with the game's.
GlobalVariable[] Property BankBalances Auto
GlobalVariable[] Property BankDebts Auto
GlobalVariable[] Property CreditDebts Auto
FormList Property HoldCrimeFactions Auto


; --- loans ---------------------------------------------------------------
GlobalVariable[] Property LoanDue Auto
GlobalVariable[] Property LoanPrincipal Auto
GlobalVariable[] Property CleanRepayments Auto
GlobalVariable[] Property AccruedDays Auto
Book[] Property DunningLetters Auto

GlobalVariable Property LoanTermDays Auto
GlobalVariable Property OverduePercent Auto

; --- standing ------------------------------------------------------------
; The tier the Dovahkiin has earned, 1..5, and what it entitles them to. The two
; limit tables are indexed by tier; the two per-hold tables remember the standing
; each hold's court has already recognised, so it never falls back.
GlobalVariable[] Property LoanLimits Auto
GlobalVariable[] Property CreditLimits Auto
GlobalVariable[] Property CreditTiers Auto
GlobalVariable[] Property CreditPaths Auto

; Bound at the same time as the four arrays above, so it can stand in for them as a
; readiness flag: an unbound array property throws the moment it is read, and cannot
; be guarded with a None test.
GlobalVariable Property CreditTierFlag Auto

Quest Property ThaneTracker Auto            ; FavorJarlsMakeFriends
Quest Property MQDragonRising Auto
Quest Property MQAlduinsBane Auto
Quest Property MQDragonslayer Auto
Quest Property CompanionsJoin Auto
Quest Property CompanionsCircleQuest Auto
Quest Property CivilWar Auto                ; carries CWScript
Faction Property CompanionsHarbinger Auto
Faction Property CollegeFaction Auto
Faction Property CWImperial Auto
Faction Property CWSons Auto

WICourierScript Property Courier Auto
GlobalVariable Property CreditSurcharge Auto

; Test shortcut. DirectX scan code; 12 is the main-row minus key. Set to 0 to disable.
GlobalVariable Property DebugHotkey Auto

MiscObject Property Gold001 Auto

; --- collateral ----------------------------------------------------------
; Per hold: 0 no pledge, 1 house pledged, 2 house seized. CollateralLtv is bound in the
; same generation as the array and stands in for it as the readiness flag.
GlobalVariable[] Property PropertyPledges Auto
GlobalVariable Property CollateralLtv Auto
GlobalVariable Property ForecloseDays Auto
Quest Property HousePurchase Auto            ; carries HousePurchaseScript and its QF fragment script
GlobalVariable Property LienReleaseFeePercent Auto
; --- guarantor ------------------------------------------------------------
; Per hold: 0 no pledge, 1 pledged, 2 claimed/defaulted. GuarantorCredit is bound
; in the same generation and stands in as the readiness flag.
GlobalVariable[] Property GuarantorPledges Auto
GlobalVariable Property GuarantorCredit Auto
Actor Property HousecarlWhiterun Auto
; Breezehome's front door, the one outside in Whiterun. Persistent, so it resolves from
; anywhere. Only this door is locked on seizure: the inner door stays as it is, so a
; player who gets inside some other way is never shut in.
ObjectReference Property BreezehomeFrontDoor Auto

; What a seizure changed on the door and took from the player, so returning the house
; puts back exactly that. Plain variables, not properties.
Bool LockoutApplied = False
Bool FrontDoorWasLocked = False
Int FrontDoorLockLevel = 0
Int KeysTaken = 0

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
    EnsureDunningRun()
    Trace("OnInit - quest is live in this save")
    ReportState()
EndEvent

; Every line this mod writes carries the same prefix, so one grep over
; Papyrus.0.log answers "did it even start" without reading anything else.
; Silence used to mean two different things - working, and never loaded - and
; telling them apart cost a game launch every time.
Function Trace(String asLine)
    Debug.Trace("BankPrism: " + asLine)
EndFunction

; Dumps what is bound and what is not. The property structure is what breaks when
; the plugin changes under an existing save, and it is invisible from inside the
; game, so it is written out where it can be read after the fact.
Function ReportState()
    Trace("state: running=" + IsRunning() + " stopped=" + IsStopped()         + " holdsReady=" + HoldsReady() + " loansReady=" + LoansReady()         + " standingReady=" + StandingReady() + " hold=" + CurrentHold)
    Trace("bindings: crimeFactions=" + (HoldCrimeFactions != None)         + " gold=" + (Gold001 != None) + " courier=" + (Courier != None)         + " thaneTracker=" + (ThaneTracker != None) + " civilWar=" + (CivilWar != None)         + " college=" + (CollegeFaction != None))
    If HoldsReady()
        Trace("accounts: " + BankBalances.Length + " holds bound")
        Trace("enabled holds: " + EnabledHoldList())
        Trace("collateral: ready=" + CollateralReady() + " owned=" + PropertyOwned(0) + " state=" + PropertyState(0) + " appraisal=" + PropertyAppraisal(0) + " credit=" + CollateralCredit(0) + " cellBound=" + (PropertyCell(0) != None) + " frontDoorBound=" + (BreezehomeFrontDoor != None) + " keyBound=" + (PropertyKey(0) != None) + " locked=" + LockoutApplied + " releaseFee=" + LienReleaseFee(0))
        Trace("guarantor: ready=" + GuarantorReady() + " appointed=" + GuarantorAppointed(0) + " alive=" + GuarantorAlive(0) + " state=" + GuarantorState(0) + " credit=" + GuarantorCredit(0) + " housecarlBound=" + (HousecarlWhiterun != None))
    Else
        Trace("accounts: NOT BOUND - this save predates the current property set;"              + " a new game is required")
    EndIf
EndFunction

; A hotkey costs ten lines and no dependencies. An MCM would need MCM Helper plus a
; config json, and SKSE Menu Framework would need ImGui integration in the plugin.
Function RegisterDebugHotkey()
    ; Not named 'key': Key is an existing Skyrim script type and the name is reserved.
    Int iKeyCode = 12
    If DebugHotkey
        iKeyCode = DebugHotkey.GetValueInt()
    EndIf
    ; Key registrations live in the save, so changing the key without clearing the old
    ; one leaves both registered and the mod keeps answering on the key nobody expects.
    UnregisterForAllKeys()
    If iKeyCode > 0
        RegisterForKey(iKeyCode)
        ; Says which key is actually live. If an older save baked in the previous value
        ; of the global, the registered code will not match the one shipped in the
        ; plugin, and this line is the only place that difference is visible.
        Debug.Trace("BankPrism: 디버그 단축키 등록 스캔코드=" + iKeyCode)
    EndIf
EndFunction

Event OnKeyDown(Int aiKeyCode)
    If bMenuOpen || Utility.IsInMenuMode()
        Return
    EndIf
    ; The shortcut reopens whichever hold was last used, which may since have been
    ; switched off; fall back to the first hold still served.
    If !HoldIsEnabled(CurrentHold)
        CurrentHold = FirstEnabledHold()
    EndIf
    ; The shortcut doubles as the health check: it says out loud that the quest is
    ; alive and writes the full state to the log. If pressing it does nothing at
    ; all, the quest is not running and no amount of UI work will show a topic.
    Debug.Notification("BankPrism: 퀘스트 동작 중 (홀드 " + GetHoldName(CurrentHold) + ")")
    ReportState()
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
        Return "하얄마치"
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
    If !HoldIsValid(CurrentHold) || !LoansReady()
        Return 0
    EndIf
    Float dueAt = LoanDue[CurrentHold].GetValue()
    If dueAt <= 0.0
        Return 0
    EndIf
    Return Math.Floor(dueAt - Utility.GetCurrentGameTime())
EndFunction

; Reading an array property that was never bound throws "Cannot cast from None to
; ...[]" on the read itself, so it cannot be guarded by testing the array. A save made
; before a property existed never binds it, and every later access would throw. These
; two flags are ordinary object properties, which read back as None harmlessly, and
; they were introduced alongside the arrays they stand for - so if the flag is None,
; the arrays are unbound too and must not be touched at all.
Bool Function HoldsReady()
    Return HoldCrimeFactions != None
EndFunction

Bool Function LoansReady()
    Return LoanTermDays != None && OverduePercent != None
EndFunction

Bool Function HoldIsValid(Int aiHold)
    If aiHold < 0 || !HoldsReady()
        Return False
    EndIf
    Return aiHold < BankBalances.Length
EndFunction

; Holds the mod currently serves. The rest keep every account, letter and array slot;
; only the doors are shut - no topic is offered there, and these gates refuse whatever
; still arrives. Must name the same holds as enabledHolds in EspGenerator/Program.cs:
; the generator reads this function and fails the build if the two disagree.
Bool Function HoldIsEnabled(Int aiHold)
    Return aiHold == 0
EndFunction

Int Function FirstEnabledHold()
    Int i = 0
    While HoldIsValid(i)
        If HoldIsEnabled(i)
            Return i
        EndIf
        i += 1
    EndWhile
    Return -1
EndFunction

String Function EnabledHoldList()
    String names = ""
    Int i = 0
    While HoldIsValid(i)
        If HoldIsEnabled(i)
            If names != ""
                names += ", "
            EndIf
            names += GetHoldName(i)
        EndIf
        i += 1
    EndWhile
    Return names
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
    If !HoldIsEnabled(hold)
        Trace("bank refused: " + GetHoldName(hold) + " is switched off")
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
    EnsureDunningRun()
    bMenuOpen = True
    AccrueOverdue(CurrentHold)
    ForeclosePropertyIfDue(CurrentHold)
    ClaimGuarantorIfDue(CurrentHold)
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
        + ", \"creditLimit\":" + GetCreditLimit(CurrentHold) \
        + ", \"tier\":" + GetCreditTier(CurrentHold) \
        + ", \"paths\":" + CountHeldPaths(CurrentHold) \
        + ", \"pathList\":\"" + HeldPathList(CurrentHold) + "\"" \
        + ", \"overdueDays\":" + GetOverdueDays(CurrentHold) \
        + ", \"grade\":\"" + GetCreditGrade(CurrentHold) + "\"" \
        + ", \"loanDaysLeft\":" + GetLoanDaysLeft() \
        + ", \"hold\":\"" + GetHoldName(CurrentHold) + "\"" \
        + ", \"holdKey\":\"" + GetHoldKey(CurrentHold) + "\"" \
        + ", \"propertyOwned\":" + (PropertyOwned(CurrentHold) as Int) \
        + ", \"propertyState\":" + PropertyState(CurrentHold) \
        + ", \"propertyValue\":" + PropertyAppraisal(CurrentHold) \
        + ", \"collateralCredit\":" + CollateralCredit(CurrentHold) \
        + ", \"ltv\":" + GetLtvPercent() \
        + ", \"forecloseDays\":" + GetForecloseDays() \
        + ", \"propertyName\":\"" + PropertyName(CurrentHold) + "\"" \
        + ", \"lienReleaseFee\":" + LienReleaseFee(CurrentHold) \
        + ", \"lienFeePercent\":" + GetLienFeePercent() \
        + ", \"propertyLocked\":" + (LockoutApplied as Int) \
        + ", \"guarantorAppointed\":" + (GuarantorAppointed(CurrentHold) as Int) \
        + ", \"guarantorAlive\":" + (GuarantorAlive(CurrentHold) as Int) \
        + ", \"guarantorState\":" + GuarantorState(CurrentHold) \
        + ", \"guarantorCredit\":" + GuarantorCredit(CurrentHold) \
        + ", \"guarantorCreditAmount\":" + GuarantorCreditBase(CurrentHold) \
        + ", \"guarantorName\":\"" + GuarantorName(CurrentHold) + "\"" \
        + ", \"guarantorTitle\":\"" + GuarantorTitle(CurrentHold) + "\"" \
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
    If !HoldIsEnabled(hold)
        Trace("credit refused: " + GetHoldName(hold) + " is switched off")
        Debug.Notification("이 상인에게는 외상을 달 수 없습니다.")
        Return
    EndIf

    Int limit = GetCreditLimit(hold)

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
; What the Jarl's vault will advance, read off the standing tier rather than the
; main quest: the court lends on the strength of what it recognises, and four
; different lives - Dragonborn, Circle, College, war - can earn the same trust.
Int Function GetLoanLimit()
    Int limit = 0
    Int tier = GetCreditTier(CurrentHold)
    If StandingReady() && tier >= 1 && tier <= LoanLimits.Length
        limit = WithPathBonus(GetGlobalInt(LoanLimits[tier - 1]), CurrentHold)
    EndIf
    ; A pledged house is lent against at any standing: the court that will not trust a
    ; nameless sellsword on his word will still take his deed. A housecarl guarantee also
    ; adds to the ceiling.
    Return limit + CollateralCredit(CurrentHold) + GuarantorCredit(CurrentHold)
EndFunction

Int Function GetLoanTermDays()
    Int term = GetGlobalInt(LoanTermDays)
    If term <= 0
        term = 7
    EndIf
    Return term
EndFunction

; Whole days this hold's loan has been overdue; 0 while it is still in term.
Int Function GetOverdueDays(Int aiHold)
    If !HoldIsValid(aiHold) || !LoansReady()
        Return 0
    EndIf
    Float dueAt = LoanDue[aiHold].GetValue()
    If dueAt <= 0.0 || BankDebts[aiHold].GetValueInt() <= 0
        Return 0
    EndIf
    Int days = Math.Floor(Utility.GetCurrentGameTime() - dueAt)
    If days < 0
        days = 0
    EndIf
    Return days
EndFunction

; Charges once per day past the due date, so the ledger moves on the same rhythm as
; the letters - a weekly charge left the figure sitting still while a courier arrived
; every morning, which reads as broken. The charge is a share of the original sum,
; never of the running total, and it stops at three times what was borrowed, so the
; debt climbs in a straight line and then holds.
;
; The due date itself is never moved: days already charged are counted separately, so
; the panel can still say how long the loan has been overdue.
Function AccrueOverdue(Int aiHold)
    If !HoldIsValid(aiHold) || !LoansReady()
        Return
    EndIf

    GlobalVariable ledger = BankDebts[aiHold]
    Int principal = LoanPrincipal[aiHold].GetValueInt()
    If principal <= 0 || ledger.GetValueInt() <= 0
        Return
    EndIf

    Int overdue = GetOverdueDays(aiHold)
    Int charged = AccruedDays[aiHold].GetValueInt()
    Int owing = overdue - charged
    If owing <= 0
        Return
    EndIf

    Int perDay = (principal * GetGlobalInt(OverduePercent)) / 100
    If perDay < 1
        perDay = 1
    EndIf

    Int ceiling = principal * 3
    Int before = ledger.GetValueInt()
    Int after = before + (perDay * owing)
    If after > ceiling
        after = ceiling
    EndIf

    AccruedDays[aiHold].SetValueInt(overdue)

    If after > before
        ledger.SetValueInt(after)
        Debug.Notification(GetHoldName(aiHold) + " 채무 연체 " + overdue + "일. " + (after - before) + " 골드가 더해졌습니다.")
    EndIf
EndFunction

; ---------------------------------------------------------------------------
; Standing with the hold
; ---------------------------------------------------------------------------

; Standing is a rank earned by deed, not a score that drifts with repayment
; behaviour, and the court fixes it on the day it first recognises the player. So
; the tier is stored per hold and only ever climbs, and alongside it the path -
; which deed earned it - because that is what gives the rank its name.
;
; Every test below reads a vanilla record that was checked against Skyrim.esm; see
; the standing block in EspGenerator/Program.cs for why these and not the obvious
; ones (there is no Thane faction, and the Civil War mission counter is deprecated).
;
; Path codes are tier * 10 + the branch, in the order the view lists them.
Int Function EarnedPath(Int aiHold)
    Int i = 0
    While i < 4
        If PathHeld(50 + i, aiHold)
            Return 50 + i
        EndIf
        i += 1
    EndWhile
    i = 0
    While i < 4
        If PathHeld(40 + i, aiHold)
            Return 40 + i
        EndIf
        i += 1
    EndWhile
    If PathHeld(30, aiHold)
        Return 30
    ElseIf PathHeld(31, aiHold)
        Return 31
    ElseIf IsTier2()
        Return 20
    EndIf
    Return 10
EndFunction

; One route, asked about by code. Every route the Dovahkiin holds is worth
; something even after the rank is settled, so the same test has to answer both
; "what names this tier" and "how many ways over did they clear it".
Bool Function PathHeld(Int aiPath, Int aiHold)
    If aiPath == 50
        Return IsTier5MainQuest()
    ElseIf aiPath == 51
        Return IsTier5Companions()
    ElseIf aiPath == 52
        Return IsTier5College()
    ElseIf aiPath == 53
        Return IsTier5CivilWar()
    ElseIf aiPath == 40
        Return IsTier4MainQuest()
    ElseIf aiPath == 41
        Return IsTier4Companions()
    ElseIf aiPath == 42
        Return IsTier4College()
    ElseIf aiPath == 43
        Return IsTier4CivilWar()
    ElseIf aiPath == 30
        Return IsThaneOf(aiHold)
    ElseIf aiPath == 31
        Return MiscObjectivesDone() >= 30
    EndIf
    Return False
EndFunction

; How many of this tier's routes are held, never less than one. Only tiers 3 to 5
; are counted: those are the ones the view draws as separate routes, and a bonus
; the player cannot see itemised is a bonus they will think is a bug.
Int Function CountHeldPaths(Int aiHold)
    Int tier = GetCreditTier(aiHold)
    If tier < 3
        Return 1
    EndIf

    Int held = 0
    Int i = 0
    While i < 4
        If PathHeld(tier * 10 + i, aiHold)
            held += 1
        EndIf
        i += 1
    EndWhile
    If held < 1
        held = 1
    EndIf
    Return held
EndFunction

; The path codes held at this tier, comma separated, for the view to mark. Empty
; below tier 3, where the view lists requirements as prose rather than as routes.
String Function HeldPathList(Int aiHold)
    Int tier = GetCreditTier(aiHold)
    If tier < 3
        Return ""
    EndIf

    String list = ""
    Int i = 0
    While i < 4
        Int code = tier * 10 + i
        If PathHeld(code, aiHold)
            If list == ""
                list = "" + code
            Else
                list = list + "," + code
            EndIf
        EndIf
        i += 1
    EndWhile
    Return list
EndFunction

; Clearing a tier by more than one route raises what it is worth. Kept as a
; constant rather than a global on purpose: another global would mean another
; property on the quest, and that invalidates every existing save.
Int Function ExtraPathBonusPercent()
    Return 25
EndFunction

; Applies the multiple-route bonus to a base ceiling.
Int Function WithPathBonus(Int aiBase, Int aiHold)
    Int extra = CountHeldPaths(aiHold) - 1
    If extra <= 0
        Return aiBase
    EndIf
    Return (aiBase * (100 + extra * ExtraPathBonusPercent())) / 100
EndFunction

Bool Function IsTier5MainQuest()
    Return MQDragonslayer != None && MQDragonslayer.IsCompleted()
EndFunction

Bool Function IsTier5Companions()
    Return CompanionsHarbinger != None && Game.GetPlayer().IsInFaction(CompanionsHarbinger)
EndFunction

Bool Function IsTier5College()
    Return CollegeRank() >= 6
EndFunction

Bool Function IsTier5CivilWar()
    Return CivilWarRank() >= 4
EndFunction

Bool Function IsTier4MainQuest()
    Return MQAlduinsBane != None && MQAlduinsBane.IsCompleted()
EndFunction

; C03 stage 25 is the line "I have ascended to the Circle which leads the
; Companions" - the record's own words, not an inference from the quest order.
Bool Function IsTier4Companions()
    Return CompanionsCircleQuest != None && CompanionsCircleQuest.GetStageDone(25)
EndFunction

Bool Function IsTier4College()
    Return CollegeRank() >= 4
EndFunction

Bool Function IsTier4CivilWar()
    Return CivilWarRank() >= 2
EndFunction

Bool Function IsTier2()
    If CollegeRank() >= 0
        Return True
    EndIf
    If CompanionsJoin != None && CompanionsJoin.IsCompleted()
        Return True
    EndIf
    If MiscObjectivesDone() >= 10
        Return True
    EndIf
    If MQDragonRising != None && MQDragonRising.IsRunning()
        Return True
    EndIf
    If MQDragonRising != None && MQDragonRising.IsCompleted()
        Return True
    EndIf
    Actor player = Game.GetPlayer()
    If CWImperial != None && player.IsInFaction(CWImperial)
        Return True
    EndIf
    If CWSons != None && player.IsInFaction(CWSons)
        Return True
    EndIf
    Return False
EndFunction

; -1 when the player is not enrolled at all; 0..6 Student..Arch-Mage otherwise.
Int Function CollegeRank()
    If CollegeFaction == None
        Return -1
    EndIf
    Return Game.GetPlayer().GetFactionRank(CollegeFaction)
EndFunction

; 1..4 once the player has been promoted; 0 before that. Vanilla keeps this on the
; CW quest's script, not in a faction rank table and not in CWCountMissionsDone,
; which its own comment marks as deprecated.
Int Function CivilWarRank()
    If CivilWar == None
        Return 0
    EndIf
    CWScript cw = CivilWar as CWScript
    If cw == None
        Return 0
    EndIf
    Return cw.PlayerRank
EndFunction

; Vanilla's own durable record of thanehood, per hold and per side of the war. The
; Favor25x quests stop the moment the Jarl names you, taking their stage data with
; them; these variables are set at the same moment and stay set.
Bool Function IsThaneOf(Int aiHold)
    If ThaneTracker == None
        Return False
    EndIf
    FavorJarlsMakeFriendsScript thane = ThaneTracker as FavorJarlsMakeFriendsScript
    If thane == None
        Return False
    EndIf

    If aiHold == 0
        Return thane.WhiterunImpGetOutofJail > 0 || thane.WhiterunSonsGetOutofJail > 0
    ElseIf aiHold == 1
        Return thane.HaafingarImpGetOutofJail > 0 || thane.HaafingarSonsGetOutofJail > 0
    ElseIf aiHold == 2
        Return thane.EastmarchImpGetOutofJail > 0 || thane.EastmarchSonsGetOutofJail > 0
    ElseIf aiHold == 3
        Return thane.RiftImpGetoutofJail > 0 || thane.RiftSonsGetOutofJail > 0
    ElseIf aiHold == 4
        Return thane.ReachImpGetOutofJail > 0 || thane.ReachSonsGetOutofJail > 0
    ElseIf aiHold == 5
        Return thane.FalkreathImpGetOutofJail > 0 || thane.FalkreathSonsGetOutofJail > 0
    ElseIf aiHold == 6
        Return thane.HjaalmarchImpGetOutofJail > 0 || thane.HjaalmarchSonsGetOutofJail > 0
    ElseIf aiHold == 7
        Return thane.PaleImpGetOutofJail > 0 || thane.PaleSonsGetOutofJail > 0
    ElseIf aiHold == 8
        Return thane.WinterholdImpGetOutofJail > 0 || thane.WinterholdSonsGetOutofJail > 0
    EndIf
    Return False
EndFunction

; The stat name is the one the game itself carries; it was read out of SkyrimSE.exe
; rather than remembered, because QueryStat answers 0 for a name that does not exist
; and would have failed silently.
Int Function MiscObjectivesDone()
    Return Game.QueryStat("Misc Objectives Completed")
EndFunction

Bool Function StandingReady()
    Return CreditTierFlag != None
EndFunction

Int Function PathTier(Int aiPath)
    Return aiPath / 10
EndFunction

; Reads the locked standing, promoting it first if the player has earned better.
; Demotion never happens: the view calls the rank permanent, and it is.
Int Function ResolveStanding(Int aiHold)
    If !HoldIsValid(aiHold) || !StandingReady()
        Return 10
    EndIf

    Int stored = CreditPaths[aiHold].GetValueInt()
    If stored < 10
        stored = 10
    EndIf

    Int earned = EarnedPath(aiHold)
    If PathTier(earned) > PathTier(stored)
        CreditPaths[aiHold].SetValue(earned as Float)
        CreditTiers[aiHold].SetValue(PathTier(earned) as Float)
        Return earned
    EndIf

    CreditTiers[aiHold].SetValue(PathTier(stored) as Float)
    Return stored
EndFunction

Int Function GetCreditTier(Int aiHold)
    Return PathTier(ResolveStanding(aiHold))
EndFunction

; The titles the view keys its portraits and steward lines off. Changing one here
; means changing the matching key in GRADE_DATA in BankView.html.
String Function GetCreditGrade(Int aiHold)
    Int path = ResolveStanding(aiHold)
    If path == 50
        Return "탐리엘의 구원자"
    ElseIf path == 51
        Return "컴패니언의 인도자"
    ElseIf path == 52
        Return "대학의 아크메이지"
    ElseIf path == 53
        Return "전쟁 영웅 장군"
    ElseIf path == 40
        Return "스카이림의 영웅"
    ElseIf path == 41
        Return "요르바스크의 전사"
    ElseIf path == 42
        Return "수석 마법학자"
    ElseIf path == 43
        Return "훈장 수훈 장교"
    ElseIf path == 30
        Return "영지의 종사"
    ElseIf path == 31
        Return "신뢰받는 해결사"
    ElseIf path == 20
        Return "정체모를 용병"
    EndIf
    Return "외지인"
EndFunction

; What a general goods merchant in this hold will carry on the slate.
Int Function GetCreditLimit(Int aiHold)
    Int tier = GetCreditTier(aiHold)
    If !StandingReady() || tier < 1 || tier > CreditLimits.Length
        Return 1000
    EndIf
    Return WithPathBonus(GetGlobalInt(CreditLimits[tier - 1]), aiHold)
EndFunction

Bool Function IsOverdue(Int aiHold)
    If !HoldIsValid(aiHold) || !LoansReady()
        Return False
    EndIf
    Float dueAt = LoanDue[aiHold].GetValue()
    Return dueAt > 0.0 && BankDebts[aiHold].GetValueInt() > 0 && Utility.GetCurrentGameTime() >= dueAt
EndFunction

Bool Function AnyLoanOutstanding()
    If !HoldsReady()
        Return False
    EndIf
    Int i = 0
    While i < BankDebts.Length
        If HoldIsEnabled(i) && BankDebts[i].GetValueInt() > 0
            Return True
        EndIf
        i += 1
    EndWhile
    Return False
EndFunction

Function TakeLoan(Int aiAmount)
    If !LoansReady()
        Refresh("이 저장 파일에서는 대출을 취급할 수 없습니다. 새 회차가 필요합니다.")
        Return
    EndIf
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
    If LoansReady()
        AccruedDays[CurrentHold].SetValueInt(0)
    EndIf
    ledger.SetValueInt(owed)
    LoanDue[CurrentHold].SetValue(Utility.GetCurrentGameTime() + GetLoanTermDays())

    Game.GetPlayer().AddItem(Gold001, aiAmount, True)
    ScheduleDunningRun()

    RefreshTx(aiAmount + " 골드를 빌렸습니다. " + GetLoanTermDays() + "일 안에 " + owed + " 골드를 갚아야 합니다.", "borrow", aiAmount)
EndFunction

Function RepayLoan(Int aiAmount, Int aiPlayerGold)
    If !LoansReady()
        Refresh("이 저장 파일에서는 대출을 취급할 수 없습니다. 새 회차가 필요합니다.")
        Return
    EndIf
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
        ; Settled before it ever came due: that is what standing is built on.
        If LoansReady() && GetOverdueDays(CurrentHold) <= 0
            GlobalVariable record = CleanRepayments[CurrentHold]
            record.SetValueInt(record.GetValueInt() + 1)
        EndIf
        LoanDue[CurrentHold].SetValue(0.0)
        LoanPrincipal[CurrentHold].SetValueInt(0)
        If LoansReady()
            AccruedDays[CurrentHold].SetValueInt(0)
        EndIf
        Bool propRestored = RestorePropertyIfSeized(CurrentHold)
        Bool guarRestored = RestoreGuarantorIfClaimed(CurrentHold)
        If propRestored && guarRestored
            RefreshTx("대출을 모두 갚았습니다. " + PropertyName(CurrentHold) + " 압류와 " + GuarantorName(CurrentHold) + "의 연대보증 추심이 풀렸습니다.", "repay", paid)
        ElseIf propRestored
            RefreshTx("대출을 모두 갚았습니다. " + PropertyName(CurrentHold) + " 압류가 풀렸습니다. 근저당은 남아 있습니다.", "repay", paid)
        ElseIf guarRestored
            RefreshTx("대출을 모두 갚았습니다. " + GuarantorName(CurrentHold) + "의 연대보증 추심이 풀렸습니다. 보증은 유지됩니다.", "repay", paid)
        Else
            RefreshTx("대출을 모두 갚았습니다.", "repay", paid)
        EndIf
    Else
        RefreshTx(paid + " 골드를 갚았습니다. 남은 채무 " + (owed - paid) + " 골드.", "repay", paid)
    EndIf
EndFunction

; ---------------------------------------------------------------------------
; Property collateral
; ---------------------------------------------------------------------------

; Only Whiterun has a house mapped, Breezehome, because only Whiterun is served. Every
; fact about it comes from vanilla's HousePurchase quest: WhiterunHouseVar, which vanilla
; Hearthfire also reads as "owns Breezehome"; HPWhiterun, the price Proventus asks; and
; the stage-10 fragment script's WhiterunHouse, the interior cell whose owner the
; purchase sets to PlayerFaction.
Bool Function CollateralReady()
    Return CollateralLtv != None
EndFunction

String Function PropertyName(Int aiHold)
    If aiHold == 0
        Return "브리즈홈"
    EndIf
    Return ""
EndFunction

Bool Function PropertyOwned(Int aiHold)
    If aiHold != 0 || HousePurchase == None
        Return False
    EndIf
    HousePurchaseScript purchase = HousePurchase as HousePurchaseScript
    Return purchase != None && purchase.WhiterunHouseVar >= 1
EndFunction

Int Function PropertyAppraisal(Int aiHold)
    If aiHold != 0 || HousePurchase == None
        Return 0
    EndIf
    HousePurchaseScript purchase = HousePurchase as HousePurchaseScript
    If purchase == None || purchase.HPWhiterun == None
        Return 0
    EndIf
    Return purchase.HPWhiterun.GetValueInt()
EndFunction

Cell Function PropertyCell(Int aiHold)
    If aiHold != 0 || HousePurchase == None
        Return None
    EndIf
    QF_HousePurchase_000A7B33 fragments = HousePurchase as QF_HousePurchase_000A7B33
    If fragments == None
        Return None
    EndIf
    Return fragments.WhiterunHouse
EndFunction

Int Function PropertyState(Int aiHold)
    If !CollateralReady() || !HoldIsValid(aiHold)
        Return 0
    EndIf
    Return PropertyPledges[aiHold].GetValueInt()
EndFunction

Int Function GetLtvPercent()
    Int pct = 60
    If CollateralLtv
        pct = CollateralLtv.GetValueInt()
    EndIf
    If pct < 0
        pct = 0
    ElseIf pct > 100
        pct = 100
    EndIf
    Return pct
EndFunction

Int Function GetForecloseDays()
    Int days = 7
    If ForecloseDays
        days = ForecloseDays.GetValueInt()
    EndIf
    If days < 1
        days = 1
    EndIf
    Return days
EndFunction

Key Function PropertyKey(Int aiHold)
    If aiHold != 0 || HousePurchase == None
        Return None
    EndIf
    QF_HousePurchase_000A7B33 fragments = HousePurchase as QF_HousePurchase_000A7B33
    If fragments == None
        Return None
    EndIf
    Return fragments.WhiterunHouseKey
EndFunction

Int Function GetLienFeePercent()
    Int pct = 20
    If LienReleaseFeePercent
        pct = LienReleaseFeePercent.GetValueInt()
    EndIf
    If pct < 0
        pct = 0
    ElseIf pct > 100
        pct = 100
    EndIf
    Return pct
EndFunction

; Releasing a lien is not free, but taking one is: like a real mortgage, the charge
; falls when the registration is cleared, not when it is made.
Int Function LienReleaseFee(Int aiHold)
    Return (PropertyAppraisal(aiHold) * GetLienFeePercent()) / 100
EndFunction

; What the pledge adds to the loan ceiling. Nothing once the house is seized.
Int Function CollateralCredit(Int aiHold)
    If PropertyState(aiHold) != 1 || !PropertyOwned(aiHold)
        Return 0
    EndIf
    Return (PropertyAppraisal(aiHold) * GetLtvPercent()) / 100
EndFunction

Function PledgeProperty()
    If !CollateralReady()
        Refresh("이 저장 파일에서는 담보를 취급할 수 없습니다. 새 회차가 필요합니다.")
        Return
    EndIf
    If !HoldIsValid(CurrentHold) || !HoldIsEnabled(CurrentHold)
        Refresh("담보를 취급할 수 없습니다.")
        Return
    EndIf
    If !PropertyOwned(CurrentHold)
        Refresh("근저당을 설정할 집이 없습니다.")
        Return
    EndIf
    Int current = PropertyState(CurrentHold)
    If current == 2
        Refresh("압류된 집에는 근저당을 설정할 수 없습니다.")
        Return
    ElseIf current == 1
        Refresh("이미 근저당이 설정되어 있습니다.")
        Return
    EndIf

    PropertyPledges[CurrentHold].SetValueInt(1)
    Int credit = CollateralCredit(CurrentHold)
    Trace("collateral: " + PropertyName(CurrentHold) + " pledged, appraisal=" + PropertyAppraisal(CurrentHold) + " credit=" + credit)
    RefreshTx(PropertyName(CurrentHold) + "에 근저당을 설정했습니다. 대출 한도가 " + credit + " 골드 늘었습니다. 해지할 때 수수료 " + LienReleaseFee(CurrentHold) + " 골드를 냅니다.", "pledge", credit)
EndFunction

Function ReleaseProperty(Int aiPlayerGold)
    If !CollateralReady() || !HoldIsValid(CurrentHold) || Gold001 == None
        Refresh("담보를 취급할 수 없습니다.")
        Return
    EndIf
    If PropertyState(CurrentHold) != 1
        Refresh("근저당이 설정된 집이 없습니다.")
        Return
    EndIf
    If BankDebts[CurrentHold].GetValueInt() > 0
        Refresh("대출을 모두 갚아야 근저당을 해지할 수 있습니다.")
        Return
    EndIf

    ; Paid the way a repayment is: purse first, then this hold's account.
    Int fee = LienReleaseFee(CurrentHold)
    GlobalVariable acct = BankBalances[CurrentHold]
    If aiPlayerGold + acct.GetValueInt() < fee
        Refresh("근저당 해지 수수료 " + fee + " 골드가 부족합니다.")
        Return
    EndIf
    Int fromWallet = aiPlayerGold
    If fromWallet > fee
        fromWallet = fee
    EndIf
    Int fromBank = fee - fromWallet
    If fromWallet > 0
        Game.GetPlayer().RemoveItem(Gold001, fromWallet, True)
    EndIf
    If fromBank > 0
        acct.SetValueInt(acct.GetValueInt() - fromBank)
    EndIf

    PropertyPledges[CurrentHold].SetValueInt(0)
    Trace("collateral: " + PropertyName(CurrentHold) + " lien released, fee=" + fee + " (wallet " + fromWallet + ", account " + fromBank + ")")
    RefreshTx(PropertyName(CurrentHold) + " 근저당을 해지했습니다. 해지 수수료 " + fee + " 골드를 냈습니다.", "release", fee)
EndFunction

; Seizure hands the house's cell to the hold - the same switch the purchase flips the
; other way. Nothing inside is moved or deleted, and repaying in full hands it back.
Function ForeclosePropertyIfDue(Int aiHold)
    Int current = PropertyState(aiHold)
    If current == 2
        ; A seizure that found the player inside locks the door once they have left.
        ApplyLockout(aiHold)
        Return
    EndIf
    If current != 1 || !LoansReady()
        Return
    EndIf
    If GetOverdueDays(aiHold) < GetForecloseDays()
        Return
    EndIf

    Cell house = PropertyCell(aiHold)
    Faction owner = None
    If HoldsReady()
        owner = HoldCrimeFactions.GetAt(aiHold) as Faction
    EndIf
    If house == None || owner == None
        Trace("collateral: cannot seize " + PropertyName(aiHold) + " - cellBound=" + (house != None) + " factionBound=" + (owner != None))
        Return
    EndIf

    house.SetFactionOwner(owner)
    PropertyPledges[aiHold].SetValueInt(2)
    Trace("collateral: " + PropertyName(aiHold) + " seized at " + GetOverdueDays(aiHold) + " days overdue, owner now " + owner)
    Debug.Notification(GetHoldName(aiHold) + " 행정관이 " + PropertyName(aiHold) + "을 압류했습니다.")
    ApplyLockout(aiHold)
EndFunction

; Keeps the player out of a seized house: the front door is locked as requiring a key,
; and the key is taken. Vanilla then says what it always says at a locked door. The
; cell's ownership has already moved, so a way in that skips the door still finds
; nothing that belongs to the player.
Function ApplyLockout(Int aiHold)
    If aiHold != 0 || LockoutApplied || PropertyState(aiHold) != 2
        Return
    EndIf
    Actor player = Game.GetPlayer()
    Cell house = PropertyCell(aiHold)
    If house != None && player.GetParentCell() == house
        Trace("collateral: player is inside " + PropertyName(aiHold) + ", the door is locked once they leave")
        Return
    EndIf
    If BreezehomeFrontDoor == None
        Trace("collateral: cannot lock " + PropertyName(aiHold) + " - front door not bound")
        Return
    EndIf

    FrontDoorWasLocked = BreezehomeFrontDoor.IsLocked()
    FrontDoorLockLevel = BreezehomeFrontDoor.GetLockLevel()
    BreezehomeFrontDoor.SetLockLevel(255)
    BreezehomeFrontDoor.Lock(True)

    KeysTaken = 0
    Key houseKey = PropertyKey(aiHold)
    If houseKey != None
        KeysTaken = player.GetItemCount(houseKey)
        If KeysTaken > 0
            player.RemoveItem(houseKey, KeysTaken, True)
        EndIf
    EndIf

    LockoutApplied = True
    Trace("collateral: " + PropertyName(aiHold) + " front door locked (was locked=" + FrontDoorWasLocked + " level=" + FrontDoorLockLevel + "), keys taken=" + KeysTaken)
    Debug.Notification(PropertyName(aiHold) + " 문이 잠기고 열쇠를 회수당했습니다.")
EndFunction

Function LiftLockout(Int aiHold)
    If aiHold != 0 || !LockoutApplied
        Return
    EndIf
    If BreezehomeFrontDoor != None
        BreezehomeFrontDoor.SetLockLevel(FrontDoorLockLevel)
        BreezehomeFrontDoor.Lock(FrontDoorWasLocked)
    EndIf
    Key houseKey = PropertyKey(aiHold)
    If houseKey != None && KeysTaken > 0
        Game.GetPlayer().AddItem(houseKey, KeysTaken, True)
    EndIf
    Trace("collateral: " + PropertyName(aiHold) + " front door restored (locked=" + FrontDoorWasLocked + " level=" + FrontDoorLockLevel + "), keys returned=" + KeysTaken)
    KeysTaken = 0
    LockoutApplied = False
EndFunction

Bool Function RestorePropertyIfSeized(Int aiHold)
    If PropertyState(aiHold) != 2
        Return False
    EndIf
    LiftLockout(aiHold)
    Cell house = PropertyCell(aiHold)
    HousePurchaseScript purchase = HousePurchase as HousePurchaseScript
    If house != None && purchase != None && purchase.PlayerFaction != None
        house.SetFactionOwner(purchase.PlayerFaction)
    Else
        Trace("collateral: could not hand the cell back - cellBound=" + (house != None) + " purchaseScript=" + (purchase != None))
    EndIf
    ; The loan is paid, not the lien: the house comes back still pledged, and only a
    ; release - which costs its fee - clears the registration.
    PropertyPledges[aiHold].SetValueInt(1)
    Trace("collateral: " + PropertyName(aiHold) + " returned to the player, lien still registered")
    Return True
EndFunction

; ---------------------------------------------------------------------------
; Joint surety / Guarantor
; ---------------------------------------------------------------------------

Bool Function GuarantorReady()
    Return GuarantorCredit != None
EndFunction

String Function GuarantorName(Int aiHold)
    If aiHold == 0
        Return "리디아"
    EndIf
    Return ""
EndFunction

String Function GuarantorTitle(Int aiHold)
    If aiHold == 0
        Return "화이트런 하우스칼"
    EndIf
    Return ""
EndFunction

Bool Function GuarantorAppointed(Int aiHold)
    If aiHold == 0
        Return IsThaneOf(0) && HousecarlWhiterun != None
    EndIf
    Return False
EndFunction

Bool Function GuarantorAlive(Int aiHold)
    If aiHold == 0 && HousecarlWhiterun != None
        Return !HousecarlWhiterun.IsDead()
    EndIf
    Return False
EndFunction

Int Function GuarantorState(Int aiHold)
    If !GuarantorReady() || !HoldIsValid(aiHold)
        Return 0
    EndIf
    Return GuarantorPledges[aiHold].GetValueInt()
EndFunction

Int Function GuarantorCreditBase(Int aiHold)
    If aiHold == 0 && GuarantorCredit != None
        Return GuarantorCredit.GetValueInt()
    EndIf
    Return 0
EndFunction

; What the guarantor adds to the loan ceiling.
Int Function GuarantorCredit(Int aiHold)
    If !GuarantorReady() || !HoldIsValid(aiHold)
        Return 0
    EndIf
    If GuarantorState(aiHold) != 1 || !GuarantorAppointed(aiHold) || !GuarantorAlive(aiHold)
        Return 0
    EndIf
    Return GuarantorCreditBase(aiHold)
EndFunction

Function PledgeGuarantor()
    If !GuarantorReady()
        Refresh("이 저장 파일에서는 보증을 취급할 수 없습니다. 새 회차가 필요합니다.")
        Return
    EndIf
    If !HoldIsValid(CurrentHold) || !HoldIsEnabled(CurrentHold)
        Refresh("보증을 취급할 수 없습니다.")
        Return
    EndIf
    If !GuarantorAppointed(CurrentHold)
        Refresh("연대보증을 설 하우스칼이 없습니다.")
        Return
    EndIf
    If !GuarantorAlive(CurrentHold)
        Refresh("보증인이 사망하여 연대보증을 세울 수 없습니다.")
        Return
    EndIf
    Int current = GuarantorState(CurrentHold)
    If current == 2
        Refresh("구상권이 집행된 보증인은 다시 보증을 설 수 없습니다.")
        Return
    ElseIf current == 1
        Refresh("이미 연대보증이 설정되어 있습니다.")
        Return
    EndIf

    GuarantorPledges[CurrentHold].SetValueInt(1)
    Int credit = GuarantorCredit(CurrentHold)
    Trace("guarantor: " + GuarantorName(CurrentHold) + " pledged, credit=" + credit)
    Refresh(GuarantorName(CurrentHold) + "을 연대보증인으로 등록했습니다. 대출 한도가 " + credit + " 골드 늘었습니다.")
EndFunction

Function ReleaseGuarantor()
    If !GuarantorReady() || !HoldIsValid(CurrentHold)
        Refresh("보증을 취급할 수 없습니다.")
        Return
    EndIf
    If GuarantorState(CurrentHold) != 1
        Refresh("설정된 연대보증이 없습니다.")
        Return
    EndIf
    If BankDebts[CurrentHold].GetValueInt() > 0
        Refresh("대출을 모두 갚아야 연대보증을 해제할 수 있습니다.")
        Return
    EndIf

    GuarantorPledges[CurrentHold].SetValueInt(0)
    Trace("guarantor: " + GuarantorName(CurrentHold) + " released")
    Refresh(GuarantorName(CurrentHold) + "의 연대보증을 해제했습니다.")
EndFunction

Function ClaimGuarantorIfDue(Int aiHold)
    Int current = GuarantorState(aiHold)
    If current != 1 || !LoansReady()
        Return
    EndIf
    If GetOverdueDays(aiHold) < GetForecloseDays()
        Return
    EndIf
    GuarantorPledges[aiHold].SetValueInt(2)
    Trace("guarantor: " + GuarantorName(aiHold) + " claimed at " + GetOverdueDays(aiHold) + " days overdue")
    Debug.Notification(GetHoldName(aiHold) + " 행정관이 연대보증인 " + GuarantorName(aiHold) + "에게 구상권을 청구했습니다.")
EndFunction

Bool Function RestoreGuarantorIfClaimed(Int aiHold)
    If GuarantorState(aiHold) != 2
        Return False
    EndIf
    GuarantorPledges[aiHold].SetValueInt(1)
    Trace("guarantor: " + GuarantorName(aiHold) + " claim lifted upon full repayment")
    Return True
EndFunction

; ---------------------------------------------------------------------------
; Dunning letters
; ---------------------------------------------------------------------------

; The vanilla courier carries them: a letter goes into its container and it finds the
; player in the next town. A daily check runs only while a loan is outstanding and
; stops itself once the ledger is clear.
; Game time at which the pending dunning check is due. A plain script variable, not a
; property, so it changes nothing in the save format; a save that predates it reads
; 0.0, which EnsureDunningRun takes to mean no check is pending.
Float NextDunningCheckAt = 0.0

; The interval is in game HOURS - Form.psc: "in afInterval hours of game time". It was
; 1.0, so an overdue loan put a letter in the courier's bag every game hour while the
; trace beside it claimed a day. Seven letters, one per day overdue, need one check a
; day.
Function ScheduleDunningRun()
    RegisterForSingleUpdateGameTime(24.0)
    NextDunningCheckAt = Utility.GetCurrentGameTime() + 1.0
    Trace("dunning: armed, next check in 24 game hours")
EndFunction

; Re-arms the daily check when a loan is outstanding and no check is pending. A
; game-time registration does not survive the script being replaced, and Borrow() was
; once the only place that made one, so a rebuild between sessions silenced the
; courier for the rest of the playthrough. But a single-update registration also
; replaces whatever was pending, so re-arming on every bank visit would push the check
; back a day each time, and a player who opens the bank daily would never get a
; letter. Re-arm only when the check that should already have run has not.
Function EnsureDunningRun()
    If !AnyLoanOutstanding()
        Return
    EndIf
    If Utility.GetCurrentGameTime() > NextDunningCheckAt + (2.0 / 24.0)
        ScheduleDunningRun()
    EndIf
EndFunction

; Seven letters per hold, one per day overdue, so the court's patience visibly
; runs out instead of the same page arriving every morning. Past the seventh day
; the last one repeats: what happens after that is a separate piece of work.
Int Function DunningDays()
    Return 7
EndFunction

Function SendDunningLetter(Int aiHold)
    If Courier == None || !LoansReady() || aiHold < 0
        Trace("dunning: not sent - courier=" + (Courier != None) + " loansReady=" + LoansReady() + " hold=" + aiHold)
        Return
    EndIf

    Int day = GetOverdueDays(aiHold)
    If day < 1
        day = 1
    ElseIf day > DunningDays()
        day = DunningDays()
    EndIf

    Int slot = aiHold * DunningDays() + (day - 1)
    If slot < 0 || slot >= DunningLetters.Length
        ; An older save still holds the one-letter-per-hold array. Nothing to send
        ; that would not be the wrong letter, so say why rather than fail silently.
        Trace("dunning: slot " + slot + " out of range (" + DunningLetters.Length             + ") - this save predates the per-day letters")
        Return
    EndIf

    Courier.addItemToContainer(DunningLetters[slot], 1)
    Trace("dunning: " + GetHoldName(aiHold) + " day " + day + " letter (slot " + slot + ") handed to the courier")
EndFunction

Event OnUpdateGameTime()
    ; Re-arm before anything can return. This used to leave without rescheduling,
    ; and one unlucky tick then ended the daily run for the rest of the game with
    ; nothing written down to say it had happened.
    If !HoldsReady() || !LoansReady()
        Trace("dunning: tick skipped - holdsReady=" + HoldsReady() + " loansReady=" + LoansReady())
        ScheduleDunningRun()
        Return
    EndIf

    Int overdueHolds = 0
    Int i = 0
    While i < BankDebts.Length
        ; A switched-off hold is frozen, not forgiven: its ledger is left exactly as it
        ; is, but it neither accrues nor sends letters the player could not answer.
        If HoldIsEnabled(i)
            AccrueOverdue(i)
            ForeclosePropertyIfDue(i)
            ClaimGuarantorIfDue(i)
            ; Letter N is written for day N overdue - day three's says 사흘째 - so none
            ; goes out on the due date itself. Before, day one's letter arrived twice:
            ; once at zero days overdue, clamped up to one, and again at one.
            If IsOverdue(i) && GetOverdueDays(i) >= 1
                SendDunningLetter(i)
                overdueHolds += 1
            EndIf
        EndIf
        i += 1
    EndWhile

    Bool stillOwing = AnyLoanOutstanding()
    Trace("dunning: tick done, overdue holds=" + overdueHolds + " outstanding=" + stillOwing)
    If stillOwing
        ScheduleDunningRun()
    Else
        Trace("dunning: ledger clear, chain stops here")
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
    ElseIf strArg == "pledgeProperty"
        PledgeProperty()
    ElseIf strArg == "releaseProperty"
        ReleaseProperty(playerGold)
    ElseIf strArg == "pledgeGuarantor"
        PledgeGuarantor()
    ElseIf strArg == "releaseGuarantor"
        ReleaseGuarantor()
    ElseIf strArg == "sellBond"
        Refresh("채권 매각은 서드파티 연동 후 사용할 수 있습니다.")
    ElseIf StringUtil.Find(strArg, "diag:") == 0
        ; The view telling us how large a surface PrismaUI actually gave it. There
        ; is no way to ask from this side, and the answer decides whether blurry
        ; text is a stretched surface or something in the page.
        Trace("view surface " + StringUtil.Substring(strArg, 5))
    EndIf
EndEvent
