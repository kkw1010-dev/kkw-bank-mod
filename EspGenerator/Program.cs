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

        // Belethor (Belethor's General Goods, Whiterun), verified against Skyrim.esm
        static readonly FormKey Belethor =
            new FormKey(new ModKey("Skyrim", ModType.Master), 0x013BA1);

        // Gold001
        static readonly FormKey Gold001 =
            new FormKey(new ModKey("Skyrim", ModType.Master), 0x00000F);

        static void Main(string[] args)
        {
            if (args.Length > 1 && args[0] == "npc")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                foreach (var npc in esm.Npcs)
                {
                    var edid = npc.EditorID ?? "";
                    var name = npc.Name?.String ?? "";
                    if (edid.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine($"NPC {npc.FormKey}  EDID={edid}  Name={name}");
                    foreach (var fr in npc.Factions)
                    {
                        var fk = fr.Faction.FormKeyNullable;
                        var fac = esm.Factions.FirstOrDefault(x => x.FormKey == fk);
                        if (fac != null && fac.Flags.HasFlag(Faction.FactionFlag.Vendor))
                            Console.WriteLine($"    vendor faction: {fac.FormKey} {fac.EditorID} container={fac.MerchantContainer.FormKeyNullable}");
                    }
                }
                return;
            }

            if (args.Length > 0 && args[0] == "vendors")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var vf = new Dictionary<FormKey, (string edid, int members, bool hasContainer)>();
                foreach (var f in esm.Factions)
                {
                    if (!f.Flags.HasFlag(Faction.FactionFlag.Vendor)) continue;
                    vf[f.FormKey] = (f.EditorID ?? "(no edid)", 0, f.MerchantContainer.FormKeyNullable != null);
                }

                foreach (var npc in esm.Npcs)
                    foreach (var rank in npc.Factions)
                    {
                        var fk = rank.Faction.FormKeyNullable;
                        if (fk != null && vf.TryGetValue(fk.Value, out var v))
                            vf[fk.Value] = (v.edid, v.members + 1, v.hasContainer);
                    }

                var rows = vf.Values.Where(v => v.members > 0).OrderByDescending(v => v.members).ToList();
                Console.WriteLine($"Vendor 플래그 팩션 총 {vf.Count}개 / 소속 NPC 있는 것 {rows.Count}개");
                Console.WriteLine($"소속 NPC 합계(중복 포함) {rows.Sum(r => r.members)}명");
                Console.WriteLine();
                Console.WriteLine("--- 인원 상위 15 ---");
                foreach (var r in rows.Take(15))
                    Console.WriteLine($"  {r.members,3}  {r.edid}  container={r.hasContainer}");
                Console.WriteLine();
                Console.WriteLine("--- 상점 컨테이너(MerchantContainer) 보유 여부 ---");
                Console.WriteLine($"  보유: {rows.Count(r => r.hasContainer)}개 팩션 / {rows.Where(r => r.hasContainer).Sum(r => r.members)}명");
                Console.WriteLine($"  없음: {rows.Count(r => !r.hasContainer)}개 팩션 / {rows.Where(r => !r.hasContainer).Sum(r => r.members)}명");
                foreach (var r in rows.Where(r => !r.hasContainer).OrderByDescending(r => r.members).Take(8))
                    Console.WriteLine($"    (제외 대상) {r.members,3}  {r.edid}");
                Console.WriteLine();

                Console.WriteLine("--- Job 계열 팩션 (조건식 후보) ---");
                foreach (var f in esm.Factions)
                {
                    var e = f.EditorID ?? "";
                    if (!e.StartsWith("Job", StringComparison.OrdinalIgnoreCase)) continue;
                    int n = esm.Npcs.Count(npc => npc.Factions.Any(fr => fr.Faction.FormKeyNullable == f.FormKey));
                    if (n > 0) Console.WriteLine($"  {n,3}  {e}  vendorFlag={f.Flags.HasFlag(Faction.FactionFlag.Vendor)}");
                }
                Console.WriteLine();

                Console.WriteLine("--- General/Goods 계열 ---");
                foreach (var r in rows.Where(r => r.edid.IndexOf("General", StringComparison.OrdinalIgnoreCase) >= 0
                                               || r.edid.IndexOf("Goods", StringComparison.OrdinalIgnoreCase) >= 0))
                    Console.WriteLine($"  {r.members,3}  {r.edid}");
                return;
            }

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
            GlobalFloat NewGlobal(string edid, float value = 0f)
            {
                var g = new GlobalFloat(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
                {
                    EditorID = edid,
                    Data = value
                };
                mod.Globals.Add(g);
                return g;
            }

            var bankBalance = NewGlobal("BankBalance");
            var bankDebt = NewGlobal("BankDebt");
            var merchantCreditDebt = NewGlobal("MerchantCreditDebt");
            var merchantCreditLimit = NewGlobal("MerchantCreditLimit", 1000f);

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
            controller.Properties.Add(new ScriptObjectProperty { Name = "MerchantCreditLimit", Object = merchantCreditLimit.ToLink<ISkyrimMajorRecordGetter>() });
            controller.Properties.Add(new ScriptObjectProperty { Name = "Gold001", Object = Gold001.ToLink<ISkyrimMajorRecordGetter>() });

            bankQuest.VirtualMachineAdapter = new QuestAdapter();
            bankQuest.VirtualMachineAdapter.Scripts.Add(controller);

            // ---- 3. Dialogue ---------------------------------------------------------
            // Every vanilla player topic belongs to a top-level DialogBranch. A topic
            // without one is never offered in the dialogue menu at all.
            void AddTopic(string id, string prompt, string response, FormKey speaker, string fragment)
            {
                var topic = mod.DialogTopics.AddNew();
                topic.EditorID = id + "Topic";
                topic.Quest.SetTo(bankQuest);
                topic.Name = prompt;
                topic.Priority = 50f;
                topic.Category = DialogTopic.CategoryEnum.Topic;
                topic.Subtype = DialogTopic.SubtypeEnum.Custom;
                topic.SubtypeName = new RecordType("CUST");

                var branch = mod.DialogBranches.AddNew();
                branch.EditorID = id + "Branch";
                branch.Quest.SetTo(bankQuest);
                branch.Flags = DialogBranch.Flag.TopLevel;
                branch.Category = DialogBranch.CategoryType.Player;
                branch.StartingTopic.SetTo(topic);
                topic.Branch.SetTo(branch);

                var info = new DialogResponses(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
                {
                    EditorID = id + "Info",
                    Prompt = prompt
                };

                info.Responses.Add(new DialogResponse
                {
                    Text = response,
                    ResponseNumber = 1,
                    Emotion = Emotion.Neutral
                });

                var isSpeaker = new GetIsIDConditionData();
                isSpeaker.Object.Link.SetTo(speaker);
                info.Conditions.Add(new ConditionFloat
                {
                    CompareOperator = CompareOperator.EqualTo,
                    ComparisonValue = 1f,
                    Data = isSpeaker
                });

                var entry = new ScriptEntry { Name = fragment, Flags = ScriptEntry.Flag.Local };
                entry.Properties.Add(new ScriptObjectProperty
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
                        FileName = fragment,
                        OnBegin = new ScriptFragment { ScriptName = fragment, FragmentName = "Fragment_0" }
                    }
                };
                adapter.Scripts.Add(entry);
                info.VirtualMachineAdapter = adapter;

                topic.Responses.Add(info);
            }

            AddTopic("BankPrismBank",
                     "은행 업무를 보고 싶습니다.",
                     "물론입니다. 어떤 업무를 도와드릴까요?",
                     ProventusAvenicci, "BankPrismDialogueFragment");

            // Merchant credit is limited to Belethor while the mechanic is being tested.
            // Widening it later is a matter of calling AddTopic for more speakers, or
            // replacing the GetIsID condition with one OR clause per vendor faction.
            AddTopic("BankPrismCredit",
                     "외상으로 거래하고 싶습니다.",
                     "장부에 달아 두지요. 갚는 것만 잊지 마시오.",
                     Belethor, "BankPrismCreditFragment");

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
