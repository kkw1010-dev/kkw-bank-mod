ScriptName BankPrismCreditFragment Extends TopicInfo Hidden

; BEGIN FRAGMENT Fragment_0
Function Fragment_0(ObjectReference akSpeakerRef)
    Actor akSpeaker = akSpeakerRef as Actor
    ; Proof the topic was offered and picked. Without it, "the line does not
    ; appear" and "the line appears but does nothing" look identical from the log.
    Debug.Trace("BankPrism: 외상 대화문 실행, 화자=" + akSpeaker)
    BankPrismQuest.BeginCreditBarter(akSpeaker)
EndFunction
; END FRAGMENT

BankPrismController Property BankPrismQuest Auto
