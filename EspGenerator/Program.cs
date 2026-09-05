using System;
using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Strings.DI;
using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace EspGenerator
{
    class Program
    {
        // Proventus Avenicci (Whiterun steward), verified against Skyrim.esm
        static readonly FormKey ProventusAvenicci =
            new FormKey(new ModKey("Skyrim", ModType.Master), 0x013BBA);

        // Gold001
        static readonly FormKey Gold001 =
            new FormKey(new ModKey("Skyrim", ModType.Master), 0x00000F);

        static void Main(string[] args)
        {
            Console.WriteLine("Generating BankPrismUI.esp...");

            var mod = new SkyrimMod(ModKey.FromNameAndExtension("BankPrismUI.esp"), SkyrimRelease.SkyrimSE);

            // ---- 1. Global variables -------------------------------------------------
            // Global is abstract in Mutagen; GlobalFloat must be constructed directly.
            GlobalFloat NewGlobal(string edid)
            {
                var g = new GlobalFloat(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
                {
                    EditorID = edid,
                    Data = 0f
                };
                mod.Globals.Add(g);
                return g;
            }

            var bankBalance = NewGlobal("BankBalance");
            var bankDebt = NewGlobal("BankDebt");
            var merchantCreditDebt = NewGlobal("MerchantCreditDebt");

            // ---- 2. Controller quest -------------------------------------------------
            var bankQuest = mod.Quests.AddNew("BankPrismQuest");
            bankQuest.Name = "Bank Prism Quest";
            bankQuest.Flags |= Quest.Flag.StartGameEnabled;

            var controller = new ScriptEntry
            {
                Name = "BankPrismController",
                Flags = ScriptEntry.Flag.Local
            };
            controller.Properties.Add(new ScriptObjectProperty { Name = "BankBalance", Object = bankBalance.ToLink<ISkyrimMajorRecordGetter>() });
            controller.Properties.Add(new ScriptObjectProperty { Name = "BankDebt", Object = bankDebt.ToLink<ISkyrimMajorRecordGetter>() });
            controller.Properties.Add(new ScriptObjectProperty { Name = "MerchantCreditDebt", Object = merchantCreditDebt.ToLink<ISkyrimMajorRecordGetter>() });
            controller.Properties.Add(new ScriptObjectProperty { Name = "Gold001", Object = Gold001.ToLink<ISkyrimMajorRecordGetter>() });

            bankQuest.VirtualMachineAdapter = new QuestAdapter();
            bankQuest.VirtualMachineAdapter.Scripts.Add(controller);

            // ---- 3. Dialogue: player topic shown when talking to the steward ---------
            var topic = mod.DialogTopics.AddNew();
            topic.EditorID = "BankPrismBankTopic";
            topic.Quest.SetTo(bankQuest);
            topic.Name = "은행 업무를 보고 싶습니다.";
            topic.Priority = 50f;
            topic.Category = DialogTopic.CategoryEnum.Topic;
            topic.Subtype = DialogTopic.SubtypeEnum.Custom;
            topic.SubtypeName = new RecordType("CUST");

            var info = new DialogResponses(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
            {
                EditorID = "BankPrismBankInfo",
                Prompt = "은행 업무를 보고 싶습니다."
            };
            info.Topic.SetTo(topic);

            info.Responses.Add(new DialogResponse
            {
                Text = "물론입니다. 어떤 업무를 도와드릴까요?",
                ResponseNumber = 1,
                Emotion = Emotion.Neutral
            });

            // Condition: speaker is Proventus Avenicci
            var isProventus = new GetIsIDConditionData();
            isProventus.Object.Link.SetTo(ProventusAvenicci);
            info.Conditions.Add(new ConditionFloat
            {
                CompareOperator = CompareOperator.EqualTo,
                ComparisonValue = 1f,
                Data = isProventus
            });

            // Papyrus fragment that opens the bank UI
            var fragmentScript = new ScriptEntry
            {
                Name = "BankPrismDialogueFragment",
                Flags = ScriptEntry.Flag.Local
            };
            fragmentScript.Properties.Add(new ScriptObjectProperty
            {
                Name = "BankPrismQuest",
                Object = bankQuest.ToLink<ISkyrimMajorRecordGetter>()
            });

            var adapter = new DialogResponsesAdapter
            {
                Version = 5,
                ObjectFormat = 2,
                ScriptFragments = new ScriptFragments
                {
                    FileName = "BankPrismDialogueFragment",
                    OnBegin = new ScriptFragment
                    {
                        ScriptName = "BankPrismDialogueFragment",
                        FragmentName = "Fragment_0"
                    }
                }
            };
            adapter.Scripts.Add(fragmentScript);
            info.VirtualMachineAdapter = adapter;

            topic.Responses.Add(info);

            // ---- 4. Write ------------------------------------------------------------
            var outputPath = Path.Combine(
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..")),
                "BankPrismUI.esp");
            // Korean text must be UTF-8: this setup's Korean translation mods are UTF-8 encoded,
            // and Mutagen defaults to Windows-1252, which replaces Hangul with '?'.
            // Korean text must be UTF-8: this setup's Korean translation mods are UTF-8 encoded,
            // and Mutagen defaults to Windows-1252, which replaces Hangul with '?'.
            mod.WriteToBinary(outputPath, new BinaryWriteParameters
            {
                Encodings = new EncodingBundle(MutagenEncoding._utf8, MutagenEncoding._utf8)
            });
            Console.WriteLine($"Successfully generated: {outputPath}");

            // ---- 5. Verify by reading the file back ---------------------------------
            Console.WriteLine("--- verification (re-read from disk) ---");
            using var check = SkyrimMod.CreateFromBinaryOverlay(outputPath, SkyrimRelease.SkyrimSE);
            foreach (var m in check.ModHeader.MasterReferences)
                Console.WriteLine($"  master: {m.Master}");
            foreach (var g in check.Globals)
                Console.WriteLine($"  GLOB {g.FormKey.ID:X6} {g.EditorID}");
            foreach (var q in check.Quests)
                Console.WriteLine($"  QUST {q.FormKey.ID:X6} {q.EditorID} scripts={q.VirtualMachineAdapter?.Scripts.Count ?? 0} flags={q.Flags}");
            foreach (var d in check.DialogTopics)
            {
                Console.WriteLine($"  DIAL {d.FormKey.ID:X6} {d.EditorID} quest={d.Quest.FormKey} cat={d.Category} sub={d.Subtype} name=\"{d.Name}\"");
                foreach (var r in d.Responses)
                {
                    Console.WriteLine($"    INFO {r.FormKey.ID:X6} {r.EditorID} conditions={r.Conditions.Count} responses={r.Responses.Count}");
                    foreach (var c in r.Conditions)
                        Console.WriteLine($"      cond: {c.Data.GetType().Name}");
                    var frag = r.VirtualMachineAdapter?.ScriptFragments;
                    Console.WriteLine($"      fragment file={frag?.FileName} onBegin={frag?.OnBegin?.ScriptName}.{frag?.OnBegin?.FragmentName}");
                }
            }
        }
    }
}
