ScriptName BankPrismDialogueFragment Extends TopicInfo Hidden

; BEGIN FRAGMENT Fragment_0
Function Fragment_0(ObjectReference akSpeakerRef)
    Actor akSpeaker = akSpeakerRef as Actor
    ; The speaker decides which hold's accounts are opened.
    BankPrismQuest.OpenBankMenu(akSpeaker)
EndFunction
; END FRAGMENT

BankPrismController Property BankPrismQuest Auto
