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
            if (args.Length > 0 && args[0] == "steward")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                var prov = new FormKey(new ModKey("Skyrim", ModType.Master), 0x013BBA);
                var p2 = esm.Npcs.First(n => n.FormKey == prov);
                Console.WriteLine("프로벤투스 소속 팩션:");
                foreach (var fr in p2.Factions)
                {
                    var f = esm.Factions.FirstOrDefault(x => x.FormKey == fr.Faction.FormKeyNullable);
                    int n = esm.Npcs.Count(x => x.Factions.Any(y => y.Faction.FormKeyNullable == f?.FormKey));
                    Console.WriteLine($"  {f?.FormKey.ID:X6} {f?.EditorID}  (소속 {n}명)");
                }
                Console.WriteLine();
                Console.WriteLine("Steward 이름이 들어간 팩션:");
                foreach (var f in esm.Factions)
                {
                    var e = f.EditorID ?? "";
                    if (e.IndexOf("Steward", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int n = esm.Npcs.Count(x => x.Factions.Any(y => y.Faction.FormKeyNullable == f.FormKey));
                    if (n > 0) Console.WriteLine($"  {f.FormKey.ID:X6} {f.EditorID}  (소속 {n}명)");
                }
                return;
            }

            if (args.Length > 0 && args[0] == "verify")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var cellOf = new Dictionary<FormKey, (string cell, bool interior)>();
                foreach (var cell in esm.EnumerateMajorRecords<ICellGetter>())
                {
                    bool interior = cell.Flags.HasFlag(Cell.Flag.IsInteriorCell);
                    var label = cell.EditorID ?? cell.FormKey.ToString();
                    foreach (var r in cell.Persistent) cellOf[r.FormKey] = (label, interior);
                    foreach (var r in cell.Temporary) cellOf[r.FormKey] = (label, interior);
                }

                var wanted = new (string name, string claimed)[]
                {
                    ("Belethor", "00013BA3"), ("Lucan", "0001347A"), ("Sayma", "000132A1"),
                    ("Bersi", "0001334E"), ("Lisbet", "000133A1"), ("RevynSadri", "0001412A"),
                    ("Birna", "0001338B"), ("Solaf", "00013653"), ("Lami", "000135E6"),
                };

                foreach (var (name, claimed) in wanted)
                {
                    var hits = esm.Npcs.Where(n =>
                        (n.EditorID ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (hits.Count == 0) { Console.WriteLine($"{name}: 찾지 못함 (주장 {claimed})"); continue; }
                    foreach (var n in hits.Take(3))
                    {
                        var vend = n.Factions
                            .Select(fr => esm.Factions.FirstOrDefault(x => x.FormKey == fr.Faction.FormKeyNullable))
                            .FirstOrDefault(x => x != null && x.Flags.HasFlag(Faction.FactionFlag.Vendor));
                        var crime = n.CrimeFaction.FormKeyNullable;
                        var crimeRec = esm.Factions.FirstOrDefault(x => x.FormKey == crime);
                        string shopCell = "(창고 없음)";
                        if (vend != null && vend.MerchantContainer.FormKeyNullable is FormKey mk
                            && cellOf.TryGetValue(mk, out var loc)) shopCell = loc.cell + (loc.interior ? " [실내]" : " [실외]");
                        bool ok = string.Equals(n.FormKey.ID.ToString("X8"), claimed, StringComparison.OrdinalIgnoreCase);
                        Console.WriteLine($"{n.EditorID,-20} 실제={n.FormKey.ID:X8} 주장={claimed} {(ok ? "일치" : "*** 불일치 ***")}");
                        Console.WriteLine($"    이름={n.Name?.String}  범죄팩션={crimeRec?.EditorID ?? "(없음)"}");
                        Console.WriteLine($"    판매팩션={vend?.EditorID ?? "(없음)"} [{vend?.FormKey.ID:X6}]  상점={shopCell}");
                    }
                }
                return;
            }

            if (args.Length > 0 && args[0] == "general")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var cellOf = new Dictionary<FormKey, (string cell, bool interior)>();
                foreach (var cell in esm.EnumerateMajorRecords<ICellGetter>())
                {
                    bool interior = cell.Flags.HasFlag(Cell.Flag.IsInteriorCell);
                    var label = cell.EditorID ?? cell.FormKey.ToString();
                    foreach (var r in cell.Persistent) cellOf[r.FormKey] = (label, interior);
                    foreach (var r in cell.Temporary) cellOf[r.FormKey] = (label, interior);
                }

                var belethorFac = new FormKey(new ModKey("Skyrim", ModType.Master), 0x09CAF5);
                var bel = esm.Factions.First(f => f.FormKey == belethorFac);
                var listKey = bel.VendorBuySellList.FormKeyNullable;
                var listRec = esm.FormLists.FirstOrDefault(l => l.FormKey == listKey);
                Console.WriteLine($"벨레쏘어 판매목록: {listKey} {listRec?.EditorID}");
                Console.WriteLine();
                Console.WriteLine("--- 같은 판매목록을 쓰는 판매 팩션 ---");

                foreach (var f in esm.Factions)
                {
                    if (!f.Flags.HasFlag(Faction.FactionFlag.Vendor)) continue;
                    if (f.VendorBuySellList.FormKeyNullable != listKey) continue;
                    var mc = f.MerchantContainer.FormKeyNullable;
                    var members = esm.Npcs.Where(n => n.Factions.Any(fr => fr.Faction.FormKeyNullable == f.FormKey)).ToList();
                    if (members.Count == 0) continue;
                    string cellLabel = "(창고 없음)";
                    bool interior = false;
                    if (mc != null && cellOf.TryGetValue(mc.Value, out var loc)) { cellLabel = loc.cell; interior = loc.interior; }
                    Console.WriteLine($"  {f.FormKey.ID:X6} {f.EditorID}");
                    Console.WriteLine($"      cell={cellLabel} interior={interior}");
                    foreach (var m in members)
                        Console.WriteLine($"      NPC {m.FormKey.ID:X6} {m.EditorID} / {m.Name?.String}");
                }
                return;
            }

            if (args.Length > 0 && args[0] == "shops")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                // Which cell each placed object sits in, so a merchant chest can be told
                // apart by whether its shop is an interior.
                var cellOf = new Dictionary<FormKey, (string cell, bool interior)>();
                foreach (var cell in esm.EnumerateMajorRecords<ICellGetter>())
                {
                    bool interior = cell.Flags.HasFlag(Cell.Flag.IsInteriorCell);
                    var label = cell.EditorID ?? cell.FormKey.ToString();
                    foreach (var r in cell.Persistent) cellOf[r.FormKey] = (label, interior);
                    foreach (var r in cell.Temporary) cellOf[r.FormKey] = (label, interior);
                }

                int inside = 0, outside = 0, unknown = 0, insideMembers = 0, outsideMembers = 0;
                var outsideList = new List<string>();
                foreach (var f in esm.Factions)
                {
                    if (!f.Flags.HasFlag(Faction.FactionFlag.Vendor)) continue;
                    var mc = f.MerchantContainer.FormKeyNullable;
                    if (mc == null) continue;
                    int members = esm.Npcs.Count(n => n.Factions.Any(fr => fr.Faction.FormKeyNullable == f.FormKey));
                    if (members == 0) continue;

                    if (!cellOf.TryGetValue(mc.Value, out var loc)) { unknown++; continue; }
                    if (loc.interior) { inside++; insideMembers += members; }
                    else { outside++; outsideMembers += members; outsideList.Add($"    {f.EditorID}  cell={loc.cell}  ({members}명)"); }
                }

                Console.WriteLine($"상점 창고가 실내(건물 안): {inside}개 팩션");
                Console.WriteLine($"상점 창고가 실외(노점/대상): {outside}개 팩션");
                Console.WriteLine($"창고 위치 확인 불가: {unknown}개 팩션");
                Console.WriteLine();
                Console.WriteLine($"실내 팩션 소속 NPC 합계: {insideMembers}명 / 실외 {outsideMembers}명");
                Console.WriteLine();
                Console.WriteLine("--- Mutagen 퍽 엔트리포인트 타입 ---");
                foreach (var t in typeof(Perk).Assembly.GetTypes())
                    if (t.Name.StartsWith("PerkEntryPoint") && !t.Name.Contains("Getter")
                        && !t.Name.Contains("Common") && !t.Name.Contains("Binary")
                        && !t.Name.Contains("Registration") && !t.Name.Contains("FieldIndex")
                        && !t.Name.Contains("Setter") && !t.Name.Contains("MixIn"))
                        Console.WriteLine("  " + t.Name);
                Console.WriteLine();
                Console.WriteLine("--- 조건 함수: GetGlobalValue / GetInFaction 존재 여부 ---");
                foreach (var nm in new[] { "GetGlobalValueConditionData", "GetInFactionConditionData", "GetItemCountConditionData" })
                    Console.WriteLine($"  {nm}: {(typeof(Perk).Assembly.GetType("Mutagen.Bethesda.Skyrim." + nm) != null ? "있음" : "없음")}");
                Console.WriteLine();
                Console.WriteLine("--- 실외로 분류된 것 (건물 없음) ---");
                foreach (var line in outsideList.Take(20)) Console.WriteLine(line);
                return;
            }

            if (args.Length > 0 && args[0] == "holds")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                Console.WriteLine("--- 범죄 팩션 (홀드 단위) ---");
                int n = 0;
                foreach (var f in esm.Factions)
                {
                    var e = f.EditorID ?? "";
                    if (e.IndexOf("Crime", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (f.CrimeValues == null) continue;
                    Console.WriteLine($"  {f.FormKey.ID:X6}  {e}");
                    n++;
                }
                Console.WriteLine($"  합계 {n}개");

                Console.WriteLine();
                Console.WriteLine("--- Mutagen 배열 프로퍼티 타입 확인 ---");
                foreach (var t in typeof(ScriptEntry).Assembly.GetTypes())
                    if (t.Name.StartsWith("ScriptObject") || t.Name.StartsWith("ScriptProperty"))
                        if (!t.Name.Contains("Getter") && !t.Name.Contains("Common") && !t.Name.Contains("Binary")
                            && !t.Name.Contains("Registration") && !t.Name.Contains("FieldIndex")
                            && !t.Name.Contains("Setter") && !t.Name.Contains("Mixin"))
                            Console.WriteLine("  " + t.Name);
                return;
            }

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
            const uint IdQuest        = 0x803;
            const uint IdBankTopic    = 0x804;
            const uint IdBankBranch   = 0x805;
            const uint IdBankInfo     = 0x806;
            const uint IdCreditLimit  = 0x807;
            const uint IdCreditTopic  = 0x808;
            const uint IdCreditBranch = 0x809;
            const uint IdCreditInfo   = 0x80A;
            const uint IdDebugHotkey  = 0x80B;
            const uint IdSurcharge    = 0x80C;
            const uint IdHoldList     = 0x80D;
            // 0x810 / 0x820 / 0x830 plus the hold index, for the three per-hold accounts.
            const uint IdBalanceBase  = 0x810;
            const uint IdBankDebtBase = 0x820;
            const uint IdCreditBase   = 0x830;

            FormKey Id(uint value) => new FormKey(mod.ModKey, value);
            FormKey Vanilla(uint value) => new FormKey(new ModKey("Skyrim", ModType.Master), value);

            // The nine holds, in the order the Papyrus side indexes them. This order is
            // part of the save format: reordering it moves every account to another hold.
            var holds = new (string name, uint crimeFaction)[]
            {
                ("Whiterun",   0x0267EA),
                ("Haafingar",  0x029DB0),
                ("Eastmarch",  0x0267E3),
                ("Rift",       0x02816B),
                ("Reach",      0x02816C),
                ("Falkreath",  0x028170),
                ("Hjaalmarch", 0x02816D),
                ("Pale",       0x02816E),
                ("Winterhold", 0x02816F),
            };

            // ---- 1. Globals ----------------------------------------------------------
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

            var balances = new List<GlobalFloat>();
            var bankDebts = new List<GlobalFloat>();
            var creditDebts = new List<GlobalFloat>();
            for (uint i = 0; i < holds.Length; i++)
            {
                var h = holds[i].name;
                balances.Add(NewGlobal(IdBalanceBase + i, "BankBalance" + h));
                bankDebts.Add(NewGlobal(IdBankDebtBase + i, "BankDebt" + h));
                creditDebts.Add(NewGlobal(IdCreditBase + i, "MerchantCreditDebt" + h));
            }

            var merchantCreditLimit = NewGlobal(IdCreditLimit, "MerchantCreditLimit", 1000f);
            // A single 20% markup applied once, when a credit purchase is written to the
            // ledger. Nothing accrues afterwards, so no timer or background script is
            // needed and the debt cannot run away on its own.
            var surcharge = NewGlobal(IdSurcharge, "MerchantCreditSurchargePercent", 20f);
            // 210 = DirectX scan code for Insert; 0 disables the test shortcut.
            var debugHotkey = NewGlobal(IdDebugHotkey, "BankPrismDebugHotkey", 210f);

            // Crime factions in hold order. Papyrus resolves the hold by asking the
            // speaker for its crime faction and finding it here - the same value Skyrim's
            // own bounty system uses, so jurisdiction matches the game's.
            var holdList = new FormList(Id(IdHoldList), SkyrimRelease.SkyrimSE)
            {
                EditorID = "BankPrismHoldCrimeFactions"
            };
            foreach (var h in holds) holdList.Items.Add(Vanilla(h.crimeFaction));
            mod.FormLists.Add(holdList);

            // ---- 2. Controller quest -------------------------------------------------
            var bankQuest = new Quest(Id(IdQuest), SkyrimRelease.SkyrimSE) { EditorID = "BankPrismQuest" };
            mod.Quests.Add(bankQuest);
            bankQuest.Name = "Bank Prism Quest";
            bankQuest.Flags |= Quest.Flag.StartGameEnabled;
            bankQuest.Priority = 50;
            bankQuest.Type = Quest.TypeEnum.Misc;

            var controller = new ScriptEntry
            {
                Name = "BankPrismController",
                Flags = ScriptEntry.Flag.Local
            };

            void ObjProp(string name, FormKey target) =>
                controller.Properties.Add(new ScriptObjectProperty
                {
                    Name = name,
                    Object = target.ToLink<ISkyrimMajorRecordGetter>()
                });

            void ListProp(string name, IEnumerable<GlobalFloat> items)
            {
                var list = new ScriptObjectListProperty { Name = name };
                foreach (var g in items)
                    list.Objects.Add(new ScriptObjectProperty { Object = g.ToLink<ISkyrimMajorRecordGetter>() });
                controller.Properties.Add(list);
            }

            ListProp("BankBalances", balances);
            ListProp("BankDebts", bankDebts);
            ListProp("CreditDebts", creditDebts);
            ObjProp("HoldCrimeFactions", holdList.FormKey);
            ObjProp("MerchantCreditLimit", merchantCreditLimit.FormKey);
            ObjProp("CreditSurcharge", surcharge.FormKey);
            ObjProp("DebugHotkey", debugHotkey.FormKey);
            ObjProp("Gold001", Gold001);

            bankQuest.VirtualMachineAdapter = new QuestAdapter();
            bankQuest.VirtualMachineAdapter.Scripts.Add(controller);

            // ---- 3. Dialogue ---------------------------------------------------------
            // Every vanilla player topic belongs to a top-level DialogBranch. A topic
            // without one is never offered in the dialogue menu at all.
            void AddTopic(string id, uint topicId, uint branchId, uint infoId,
                          string prompt, string response, FormKey[] factions, string fragment)
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

                // Conditioning on the faction rather than the actor survives NPC overhauls
                // that replace the record. Alternatives are OR'd: every clause but the last
                // carries the OR flag, which is how Skyrim groups them.
                for (int i = 0; i < factions.Length; i++)
                {
                    var inFaction = new GetInFactionConditionData();
                    inFaction.Faction.Link.SetTo(factions[i]);
                    info.Conditions.Add(new ConditionFloat
                    {
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 1f,
                        Data = inFaction,
                        Flags = i < factions.Length - 1 ? Condition.Flag.OR : default
                    });
                }

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

            // One faction covers every hold steward, including the wartime replacements.
            AddTopic("BankPrismBank", IdBankTopic, IdBankBranch, IdBankInfo,
                     "은행 업무를 보고 싶습니다.",
                     "물론입니다. 어떤 업무를 도와드릴까요?",
                     new[] { Vanilla(0x050922) }, "BankPrismDialogueFragment");

            // Standalone general stores with their own premises. Blacksmiths, alchemists,
            // innkeepers, market stalls and the Khajiit caravans are deliberately absent:
            // Eorlund extending credit to the Companions' Harbinger reads wrong. Faction
            // ids were read out of Skyrim.esm rather than taken on trust - of the nine NPC
            // ids supplied with this list, eight were wrong.
            var generalStores = new[]
            {
                Vanilla(0x09CAF5), // ServicesWhiterunBelethorsGoods    - Belethor
                Vanilla(0x05A665), // ServicesRiverwoodRiverwoodTrader  - Lucan Valerius
                Vanilla(0x0A6C02), // ServicesSolitudeBitsAndPieces     - Sayma
                Vanilla(0x0A31C5), // ServicesRiftenPawnedPrawn         - Bersi Honey-Hand
                Vanilla(0x094375), // ServicesMarkarthArnleifandSons    - Lisbet
                Vanilla(0x0A3F12), // ServicesWindhelmRevynSadri        - Revyn Sadri
                Vanilla(0x09DA62), // ServicesWinterholdBirna           - Birna
                Vanilla(0x0A6BFE), // ServicesFalkreathGrayPineGoods    - Solaf
                Vanilla(0x09DA5B), // ServicesMorthalLami               - Lami
            };

            AddTopic("BankPrismCredit", IdCreditTopic, IdCreditBranch, IdCreditInfo,
                     "외상으로 거래하고 싶습니다.",
                     "장부에 달아 두지요. 갚는 것만 잊지 마시오.",
                     generalStores, "BankPrismCreditFragment");


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
                        var lst = pr as IScriptObjectListPropertyGetter;
                        string shown;
                        if (lst != null)
                            shown = lst.Objects.Count + "개 [" +
                                string.Join(", ", lst.Objects.Select(o => o.Object.FormKeyNullable?.ID.ToString("X3") ?? "?")) + "]";
                        else
                            shown = obj?.Object.FormKeyNullable?.ToString() ?? "(not object)";
                        Console.WriteLine($"      {pr.Name} type={pr.GetType().Name} -> {shown}");
                    }
                }
            foreach (var fl in check.FormLists)
                Console.WriteLine($"  FLST {fl.FormKey.ID:X6} {fl.EditorID} items={fl.Items.Count} [" +
                    string.Join(", ", fl.Items.Select(i => i.FormKeyNullable?.ID.ToString("X6") ?? "?")) + "]");
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
                        Console.WriteLine($"      cond: {c.Data.GetType().Name} target={tgt} op={c.CompareOperator} flags={c.Flags}");
                    }
                    var frag = r.VirtualMachineAdapter?.ScriptFragments;
                    Console.WriteLine($"      fragment file={frag?.FileName} onBegin={frag?.OnBegin?.ScriptName}.{frag?.OnBegin?.FragmentName}");
                }
            }
        }
    }
}
