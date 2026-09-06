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

        // His vendor faction. Conditioning on the faction rather than the actor survives
        // NPC overhauls that replace the record, and is the same mechanism a wider
        // merchant scope would use later.
        static readonly FormKey BelethorsGoodsFaction =
            new FormKey(new ModKey("Skyrim", ModType.Master), 0x09CAF5);

        // Gold001
        static readonly FormKey Gold001 =
            new FormKey(new ModKey("Skyrim", ModType.Master), 0x00000F);

        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "belethor")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var target = new FormKey(new ModKey("Skyrim", ModType.Master), 0x013BA1);
                var vendorFac = new FormKey(new ModKey("Skyrim", ModType.Master), 0x09CAF5);
                var quests = esm.Quests.ToDictionary(q => q.FormKey);

                int shown = 0;
                foreach (var d in esm.DialogTopics)
                {
                    bool hit = false;
                    foreach (var r in d.Responses)
                        foreach (var c in r.Conditions)
                        {
                            if (c.Data is IGetIsIDConditionDataGetter g &&
                                g.Object.Link.FormKeyNullable == target) hit = true;
                            if (c.Data is IGetInFactionConditionDataGetter f &&
                                f.Faction.Link.FormKeyNullable == vendorFac) hit = true;
                        }
                    if (!hit) continue;

                    var qk = d.Quest.FormKeyNullable;
                    quests.TryGetValue(qk ?? default, out var q);
                    var br = esm.DialogBranches.FirstOrDefault(b => b.FormKey == d.Branch.FormKeyNullable);

                    Console.WriteLine($"DIAL {d.FormKey.ID:X6} {d.EditorID}");
                    Console.WriteLine($"   prio={d.Priority} cat={d.Category} sub={d.Subtype} flags={d.TopicFlags}");
                    Console.WriteLine($"   branch={(br == null ? "(none)" : br.EditorID + " flags=" + br.Flags + " cat=" + br.Category)}");
                    Console.WriteLine($"   quest={(q == null ? "(none)" : q.EditorID + " prio=" + q.Priority + " flags=" + q.Flags + " type=" + q.Type)}");
                    if (++shown >= 8) break;
                }
                Console.WriteLine($"총 {shown}건 표시");
                return;
            }

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

            // Record FormIDs are pinned. Mutagen hands out the next free id in creation
            // order, so inserting a record used to shift every later one - which moves a
            // running quest out from under an existing save and silently kills its
            // dialogue. New records take a new id at the end; nothing above ever moves.
            const uint IdBankBalance        = 0x800;
            const uint IdBankDebt           = 0x801;
            const uint IdMerchantCreditDebt = 0x802;
            const uint IdQuest              = 0x803;
            const uint IdBankTopic          = 0x804;
            const uint IdBankBranch         = 0x805;
            const uint IdBankInfo           = 0x806;
            const uint IdCreditLimit        = 0x807;
            const uint IdCreditTopic        = 0x808;
            const uint IdCreditBranch       = 0x809;
            const uint IdCreditInfo         = 0x80A;
            const uint IdDebugHotkey        = 0x80B;

            FormKey Id(uint value) => new FormKey(mod.ModKey, value);

            // ---- 1. Global variables -------------------------------------------------
            // Global is abstract in Mutagen; GlobalFloat must be constructed directly.
            GlobalFloat NewGlobal(uint id, string edid, float value = 0f)
            {
                var g = new GlobalFloat(Id(id), SkyrimRelease.SkyrimSE)
                {
                    EditorID = edid,
                    Data = value
                };
                mod.Globals.Add(g);
                return g;
            }

            var bankBalance = NewGlobal(IdBankBalance, "BankBalance");
            var bankDebt = NewGlobal(IdBankDebt, "BankDebt");
            var merchantCreditDebt = NewGlobal(IdMerchantCreditDebt, "MerchantCreditDebt");
            var merchantCreditLimit = NewGlobal(IdCreditLimit, "MerchantCreditLimit", 1000f);
            // 210 = DirectX scan code for Insert; 0 disables the test shortcut.
            var debugHotkey = NewGlobal(IdDebugHotkey, "BankPrismDebugHotkey", 210f);

            // ---- 2. Controller quest -------------------------------------------------
            var bankQuest = new Quest(Id(IdQuest), SkyrimRelease.SkyrimSE) { EditorID = "BankPrismQuest" };
            mod.Quests.Add(bankQuest);
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
            controller.Properties.Add(new ScriptObjectProperty { Name = "DebugHotkey", Object = debugHotkey.ToLink<ISkyrimMajorRecordGetter>() });
            controller.Properties.Add(new ScriptObjectProperty { Name = "Gold001", Object = Gold001.ToLink<ISkyrimMajorRecordGetter>() });

            bankQuest.VirtualMachineAdapter = new QuestAdapter();
            bankQuest.VirtualMachineAdapter.Scripts.Add(controller);

            // ---- 3. Dialogue ---------------------------------------------------------
            // Every vanilla player topic belongs to a top-level DialogBranch. A topic
            // without one is never offered in the dialogue menu at all.
            void AddTopic(string id, uint topicId, uint branchId, uint infoId,
                          string prompt, string response, FormKey speaker, string fragment,
                          bool speakerIsFaction = false)
            {
                var topic = new DialogTopic(Id(topicId), SkyrimRelease.SkyrimSE);
                mod.DialogTopics.Add(topic);
                topic.EditorID = id + "Topic";
                topic.Quest.SetTo(bankQuest);
                topic.Name = prompt;
                topic.Priority = 50f;
                topic.Category = DialogTopic.CategoryEnum.Topic;
                topic.Subtype = DialogTopic.SubtypeEnum.Custom;
                topic.SubtypeName = new RecordType("CUST");

                var branch = new DialogBranch(Id(branchId), SkyrimRelease.SkyrimSE);
                mod.DialogBranches.Add(branch);
                branch.EditorID = id + "Branch";
                branch.Quest.SetTo(bankQuest);
                branch.Flags = DialogBranch.Flag.TopLevel;
                branch.Category = DialogBranch.CategoryType.Player;
                branch.StartingTopic.SetTo(topic);
                topic.Branch.SetTo(branch);

                var info = new DialogResponses(Id(infoId), SkyrimRelease.SkyrimSE)
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

                ConditionData speakerCondition;
                if (speakerIsFaction)
                {
                    var inFaction = new GetInFactionConditionData();
                    inFaction.Faction.Link.SetTo(speaker);
                    speakerCondition = inFaction;
                }
                else
                {
                    var isId = new GetIsIDConditionData();
                    isId.Object.Link.SetTo(speaker);
                    speakerCondition = isId;
                }

                info.Conditions.Add(new ConditionFloat
                {
                    CompareOperator = CompareOperator.EqualTo,
                    ComparisonValue = 1f,
                    Data = speakerCondition
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

            AddTopic("BankPrismBank", IdBankTopic, IdBankBranch, IdBankInfo,
                     "은행 업무를 보고 싶습니다.",
                     "물론입니다. 어떤 업무를 도와드릴까요?",
                     ProventusAvenicci, "BankPrismDialogueFragment");

            // Merchant credit is limited to Belethor while the mechanic is being tested.
            // Widening it later is a matter of calling AddTopic for more speakers, or
            // replacing the GetIsID condition with one OR clause per vendor faction.
            AddTopic("BankPrismCredit", IdCreditTopic, IdCreditBranch, IdCreditInfo,
                     "외상으로 거래하고 싶습니다.",
                     "장부에 달아 두지요. 갚는 것만 잊지 마시오.",
                     BelethorsGoodsFaction, "BankPrismCreditFragment", speakerIsFaction: true);

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
                    {
                        var gid = c.Data as IGetIsIDConditionDataGetter;
                        var gfa = c.Data as IGetInFactionConditionDataGetter;
                        var tgt = gid?.Object.Link.FormKeyNullable?.ToString()
                               ?? gfa?.Faction.Link.FormKeyNullable?.ToString() ?? "(NONE)";
                        Console.WriteLine($"      cond: {c.Data.GetType().Name} target={tgt} op={c.CompareOperator}");
                    }
                    var frag = r.VirtualMachineAdapter?.ScriptFragments;
                    Console.WriteLine($"      fragment file={frag?.FileName} onBegin={frag?.OnBegin?.ScriptName}.{frag?.OnBegin?.FragmentName}");
                }
            }
        }
    }
}
