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
            if (args.Length > 0 && args[0] == "compare")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                int shown = 0;
                foreach (var d in esm.DialogTopics)
                {
                    if (d.Subtype != DialogTopic.SubtypeEnum.Custom) continue;
                    bool hasGetIsID = false;
                    foreach (var r in d.Responses)
                        foreach (var c in r.Conditions)
                            if (c.Data is IGetIsIDConditionDataGetter) hasGetIsID = true;
                    if (!hasGetIsID) continue;

                    Console.WriteLine($"=== VANILLA DIAL {d.FormKey} {d.EditorID}");
                    Console.WriteLine($"    Name       = {d.Name}");
                    Console.WriteLine($"    Priority   = {d.Priority}");
                    Console.WriteLine($"    Category   = {d.Category}");
                    Console.WriteLine($"    Subtype    = {d.Subtype} / {d.SubtypeName}");
                    Console.WriteLine($"    TopicFlags = {d.TopicFlags}");
                    Console.WriteLine($"    Branch     = {d.Branch.FormKeyNullable}");
                    Console.WriteLine($"    Quest      = {d.Quest.FormKeyNullable}");
                    foreach (var r in d.Responses)
                    {
                        Console.WriteLine($"    --- INFO {r.FormKey} {r.EditorID}");
                        Console.WriteLine($"        Flags        = {r.Flags}");
                        Console.WriteLine($"        DATA         = {r.DATA}");
                        Console.WriteLine($"        FavorLevel   = {r.FavorLevel}");
                        Console.WriteLine($"        Prompt       = {r.Prompt}");
                        Console.WriteLine($"        Speaker      = {r.Speaker.FormKeyNullable}");
                        Console.WriteLine($"        ResponseData = {r.ResponseData.FormKeyNullable}");
                        Console.WriteLine($"        TopicBackRef = {r.Topic.FormKeyNullable}");
                        foreach (var resp in r.Responses)
                            Console.WriteLine($"        resp[{resp.ResponseNumber}] emo={resp.Emotion}/{resp.EmotionValue} flags={resp.Flags}");
                    }
                    foreach (var q in esm.Quests)
                        if (q.FormKey == d.Quest.FormKeyNullable)
                            Console.WriteLine($"    QUEST {q.EditorID} flags={q.Flags} priority={q.Priority} type={q.Type}");

                    foreach (var b in esm.DialogBranches)
                        if (b.FormKey == d.Branch.FormKeyNullable)
                        {
                            Console.WriteLine($"    BRANCH {b.FormKey} {b.EditorID}");
                            Console.WriteLine($"        Quest         = {b.Quest.FormKeyNullable}");
                            Console.WriteLine($"        StartingTopic = {b.StartingTopic.FormKeyNullable}");
                            Console.WriteLine($"        Flags         = {b.Flags}");
                            Console.WriteLine($"        Category      = {b.Category}");
                        }

                    if (++shown >= 2) break;
                }
                return;
            }

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
            // Vanilla dialogue quests carry a real priority (40-70) and a type;
            // a priority of 0 leaves the topic ranked below everything else.
            bankQuest.Priority = 50;
            bankQuest.Type = Quest.TypeEnum.Misc;

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

            // Every vanilla player topic belongs to a top-level DialogBranch. Without one
            // the topic is never offered in the dialogue menu, which is why the steward
            // showed nothing at all.
            var branch = mod.DialogBranches.AddNew();
            branch.EditorID = "BankPrismBankBranch";
            branch.Quest.SetTo(bankQuest);
            branch.Flags = DialogBranch.Flag.TopLevel;
            branch.Category = DialogBranch.CategoryType.Player;
            branch.StartingTopic.SetTo(topic);
            topic.Branch.SetTo(branch);

            var info = new DialogResponses(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
            {
                EditorID = "BankPrismBankInfo",
                Prompt = "은행 업무를 보고 싶습니다."
            };

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
                Console.WriteLine($"  QUST {q.FormKey.ID:X6} {q.EditorID} scripts={q.VirtualMachineAdapter?.Scripts.Count ?? 0} flags={q.Flags} priority={q.Priority} type={q.Type}");
            foreach (var q in check.Quests)
                foreach (var sc in q.VirtualMachineAdapter?.Scripts ?? Enumerable.Empty<IScriptEntryGetter>())
                {
                    Console.WriteLine($"    script {sc.Name} props={sc.Properties.Count}");
                    foreach (var pr in sc.Properties)
                    {
                        var obj = pr as IScriptObjectPropertyGetter;
                        Console.WriteLine($"      {pr.Name} type={pr.GetType().Name} -> {obj?.Object.FormKeyNullable?.ToString() ?? "(not object)"}");
                    }
                }
            foreach (var b in check.DialogBranches)
                Console.WriteLine($"  DLBR {b.FormKey.ID:X6} {b.EditorID} quest={b.Quest.FormKeyNullable} start={b.StartingTopic.FormKeyNullable} flags={b.Flags} cat={b.Category}");
            foreach (var d in check.DialogTopics)
            {
                Console.WriteLine($"  DIAL {d.FormKey.ID:X6} {d.EditorID} quest={d.Quest.FormKey} cat={d.Category} sub={d.Subtype} branch={d.Branch.FormKeyNullable} prio={d.Priority} name=\"{d.Name}\"");
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
