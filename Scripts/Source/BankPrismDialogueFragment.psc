; BEGIN FRAGMENT CODE - Do not edit anything between this and the end comment
; NEXT FRAGMENT INDEX 1
Scriptname BankPrismDialogueFragment Extends TopicInfo Hidden

; BEGIN FRAGMENT Fragment_0
Function Fragment_0(ObjectReference akSpeakerRef)
Actor akSpeaker = akSpeakerRef as Actor
; BEGIN CODE
    ; 은행 UI 열기
    BankPrismQuest.OpenBankMenu()
; END CODE
EndFunction
; END FRAGMENT

; END FRAGMENT CODE - Do not edit anything between this and the begin comment

BankPrismController Property BankPrismQuest Auto
