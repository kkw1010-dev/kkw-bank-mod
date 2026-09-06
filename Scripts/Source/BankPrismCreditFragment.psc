ScriptName BankPrismCreditFragment Extends TopicInfo Hidden

; BEGIN FRAGMENT Fragment_0
Function Fragment_0(ObjectReference akSpeakerRef)
    Actor akSpeaker = akSpeakerRef as Actor
    BankPrismQuest.BeginCreditBarter(akSpeaker)
EndFunction
; END FRAGMENT

BankPrismController Property BankPrismQuest Auto
