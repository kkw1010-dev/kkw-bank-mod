{
  Generate BankPrismUI.esp automatically via xEdit.
  How to use:
  1. Open SSEEdit. Select only Skyrim.esm and Update.esm.
  2. Right-click anywhere in the left pane -> Apply Script...
  3. Load this script (GenerateBankUI.pas) and click OK.
  4. It will create BankPrismUI.esp with the Globals, Quest, and Script attached.
  5. Close xEdit to save.
}
unit GenerateBankUI;

var
  newFile: IInterface;
  globGroup, questGroup: IInterface;

function Process(e: IInterface): integer;
var
  recBalance, recDebt, recCredit, recQuest: IInterface;
  vmad, scripts, script, props, prop: IInterface;
begin
  // Only run once
  if Signature(e) <> 'TES4' then exit;

  AddMessage('Generating BankPrismUI.esp...');

  // Create new file
  newFile := AddNewFile('BankPrismUI.esp');
  if not Assigned(newFile) then begin
    AddMessage('Failed to create file');
    Result := 1;
    exit;
  end;

  // 1. Create Globals
  globGroup := Add(newFile, 'GLOB', True);
  
  recBalance := Add(globGroup, 'GLOB', True);
  SetElementEditValues(recBalance, 'EDID', 'BankBalance');
  SetElementNativeValues(recBalance, 'FLTV', 0.0);

  recDebt := Add(globGroup, 'GLOB', True);
  SetElementEditValues(recDebt, 'EDID', 'BankDebt');
  SetElementNativeValues(recDebt, 'FLTV', 0.0);

  recCredit := Add(globGroup, 'GLOB', True);
  SetElementEditValues(recCredit, 'EDID', 'MerchantCreditDebt');
  SetElementNativeValues(recCredit, 'FLTV', 0.0);

  // 2. Create Quest
  questGroup := Add(newFile, 'QUST', True);
  recQuest := Add(questGroup, 'QUST', True);
  SetElementEditValues(recQuest, 'EDID', 'BankPrismQuest');
  SetElementEditValues(recQuest, 'FULL', 'Bank Prism Quest');
  // Flag 0x11 = Start Game Enabled (1) | Run Once (10)
  SetElementNativeValues(recQuest, 'DNAM\Flags', $11);

  // 3. Add Script to Quest (VMAD)
  vmad := Add(recQuest, 'VMAD', True);
  SetElementNativeValues(vmad, 'Version', 5);
  SetElementNativeValues(vmad, 'Object Format', 2);
  
  scripts := ElementByPath(vmad, 'Scripts');
  script := ElementAssign(scripts, HighInteger, nil, False);
  SetElementEditValues(script, 'scriptName', 'BankPrismController');
  
  props := ElementByPath(script, 'Properties');

  prop := ElementAssign(props, HighInteger, nil, False);
  SetElementEditValues(prop, 'propertyName', 'BankBalance');
  SetElementEditValues(prop, 'Type', 'Object');
  SetElementNativeValues(prop, 'Value\Object Union\Object v2\FormID', GetLoadOrderFormID(recBalance));

  prop := ElementAssign(props, HighInteger, nil, False);
  SetElementEditValues(prop, 'propertyName', 'BankDebt');
  SetElementEditValues(prop, 'Type', 'Object');
  SetElementNativeValues(prop, 'Value\Object Union\Object v2\FormID', GetLoadOrderFormID(recDebt));

  prop := ElementAssign(props, HighInteger, nil, False);
  SetElementEditValues(prop, 'propertyName', 'MerchantCreditDebt');
  SetElementEditValues(prop, 'Type', 'Object');
  SetElementNativeValues(prop, 'Value\Object Union\Object v2\FormID', GetLoadOrderFormID(recCredit));

  prop := ElementAssign(props, HighInteger, nil, False);
  SetElementEditValues(prop, 'propertyName', 'Gold001');
  SetElementEditValues(prop, 'Type', 'Object');
  SetElementNativeValues(prop, 'Value\Object Union\Object v2\FormID', $0000000F);

  AddMessage('BankPrismUI.esp generation complete. Please save and exit.');
  Result := 1; // Stop processing further records
end;

end.
