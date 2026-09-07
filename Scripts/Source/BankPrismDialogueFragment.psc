ScriptName BankPrismDialogueFragment Extends TopicInfo Hidden

; BEGIN FRAGMENT Fragment_0
Function Fragment_0(ObjectReference akSpeakerRef)
    Actor akSpeaker = akSpeakerRef as Actor
    ; Proof the topic was offered and picked. Without it, "the line does not
    ; appear" and "the line appears but does nothing" look identical from the log.
    Debug.Trace("BankPrism: 은행 대화문 실행, 화자=" + akSpeaker)
    ; The speaker decides which hold's accounts are opened.
    BankPrismQuest.OpenBankMenu(akSpeaker)
EndFunction
; END FRAGMENT

BankPrismController Property BankPrismQuest Auto
