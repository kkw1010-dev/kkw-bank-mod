using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
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

        // Mutagen's rank title shape has moved between versions; read it reflectively
        // so a probe never fails to build over a cosmetic label.
        static string RankTitle(object rank)
        {
            foreach (var name in new[] { "Title", "MaleTitle", "FemaleTitle" })
            {
                var pi = rank.GetType().GetProperty(name);
                var v = pi?.GetValue(rank);
                if (v == null) continue;
                var male = v.GetType().GetProperty("Male")?.GetValue(v) ?? v;
                var str = male?.ToString();
                if (!string.IsNullOrWhiteSpace(str)) return "\"" + str + "\"";
            }
            return "";
        }

        // The comparison value lives on a different member per Mutagen version
        // (ComparisonValue / Float / Data.ComparisonValue); read it reflectively.
        static string CondValue(object cond)
        {
            foreach (var name in new[] { "ComparisonValue", "Float", "Value" })
            {
                var v = cond.GetType().GetProperty(name)?.GetValue(cond);
                if (v != null) return v.ToString() ?? "";
            }
            return "?";
        }

        static void Main(string[] args)
        {
            // Generic record search. Added because judging tier requirements from
            // memory is exactly the guessing this project keeps getting burned by:
            // "there is no Thane faction" was only settled by looking.
            //   dotnet run -- find <keyword> [qust|fact|glob|perk]
            if (args.Length > 1 && args[0] == "find")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                string needle = args[1];
                string only = args.Length > 2 ? args[2].ToLowerInvariant() : "";
                bool Want(string kind) => only.Length == 0 || only == kind;
                bool Hit(string? e) =>
                    e != null && e.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

                if (Want("qust"))
                {
                    Console.WriteLine($"=== QUST ~ \"{needle}\" ===");
                    foreach (var q in esm.Quests)
                        if (Hit(q.EditorID))
                            Console.WriteLine($"  {q.FormKey.ID:X6}  {q.EditorID,-34} \"{q.Name}\" stages={q.Stages.Count}");
                }

                if (Want("fact"))
                {
                    Console.WriteLine($"=== FACT ~ \"{needle}\" ===");
                    foreach (var f in esm.Factions)
                        if (Hit(f.EditorID))
                        {
                            Console.WriteLine($"  {f.FormKey.ID:X6}  {f.EditorID,-34} ranks={f.Ranks.Count}");
                            foreach (var r in f.Ranks)
                                Console.WriteLine($"        rank {r.Number,-3} {RankTitle(r)}");
                        }
                }

                if (Want("glob"))
                {
                    Console.WriteLine($"=== GLOB ~ \"{needle}\" ===");
                    foreach (var g in esm.Globals)
                        if (Hit(g.EditorID))
                        {
                            string val = g switch
                            {
                                IGlobalIntGetter gi => gi.Data?.ToString() ?? "(null)",
                                IGlobalShortGetter gs => gs.Data?.ToString() ?? "(null)",
                                IGlobalFloatGetter gf => gf.Data?.ToString() ?? "(null)",
                                _ => "(?)"
                            };
                            Console.WriteLine($"  {g.FormKey.ID:X6}  {g.EditorID,-34} = {val}");
                        }
                }

                if (Want("perk"))
                {
                    Console.WriteLine($"=== PERK ~ \"{needle}\" ===");
                    foreach (var pk in esm.Perks)
                        if (Hit(pk.EditorID))
                            Console.WriteLine($"  {pk.FormKey.ID:X6}  {pk.EditorID,-34} \"{pk.Name}\"");
                }
                return;
            }

            // Who actually tests this record? Answers "is the player really put in
            // this faction / is this global really the rank ladder" by finding the
            // vanilla conditions that read it, and on whom they run.
            //   dotnet run -- usage <formid-hex>
            if (args.Length > 1 && args[0] == "usage")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                uint id = Convert.ToUInt32(args[1], 16);
                var target = new FormKey(new ModKey("Skyrim", ModType.Master), id);

                // Condition data holds its target behind a couple of wrapper shapes
                // (ConditionParameter -> Link -> FormKeyNullable) that differ per
                // condition type, so walk the properties and pull out any FormKey.
                static IEnumerable<FormKey> Keys(object? o, int depth)
                {
                    if (o == null || depth > 3) yield break;
                    var t = o.GetType();
                    foreach (var pi in t.GetProperties())
                    {
                        if (pi.GetIndexParameters().Length > 0) continue;
                        object? v;
                        try { v = pi.GetValue(o); } catch { continue; }
                        if (v == null) continue;
                        if (v is FormKey fk) { yield return fk; continue; }
                        if (pi.PropertyType == typeof(FormKey?)) { yield return (FormKey)v; continue; }
                        var ns = v.GetType().Namespace ?? "";
                        if (!ns.StartsWith("Mutagen")) continue;
                        foreach (var k in Keys(v, depth + 1)) yield return k;
                    }
                }

                bool Refers(IConditionGetter c)
                {
                    foreach (var k in Keys(c.Data, 0))
                        if (k == target) return true;
                    return false;
                }

                int hits = 0;
                var byRunOn = new Dictionary<string, int>();
                foreach (var d in esm.DialogTopics)
                    foreach (var r in d.Responses)
                        foreach (var c in r.Conditions)
                        {
                            if (!Refers(c)) continue;
                            hits++;
                            string fn = c.Data.GetType().Name.Replace("ConditionData", "");
                            string runOn = c.Data.RunOnType.ToString();
                            string k = $"{fn} / RunOn={runOn} / {c.CompareOperator}";
                            byRunOn[k] = byRunOn.TryGetValue(k, out var n) ? n + 1 : 1;
                            if (hits <= 12)
                                Console.WriteLine($"  DIAL {d.FormKey.ID:X6} {d.EditorID,-34} INFO {r.FormKey.ID:X6}  {fn} RunOn={runOn} {c.CompareOperator}");
                        }

                Console.WriteLine();
                Console.WriteLine($"=== {id:X6} 를 읽는 바닐라 대화 조건: {hits}건 ===");
                foreach (var kv in byRunOn.OrderByDescending(x => x.Value))
                    Console.WriteLine($"  {kv.Value,5}x  {kv.Key}");
                if (hits == 0)
                    Console.WriteLine("  (대화 조건에서 쓰이지 않음 - 스크립트로만 다뤄질 수 있다)");
                return;
            }

            // Print a quest's stages, log entries and objectives, so "which stage
            // actually means the player joined the Circle" is read off the record
            // instead of recalled.
            //   dotnet run -- quest <formid-hex>
            // Which stage runs which script fragment, and every script property's bound
            // value. `quest` lists property names only; a console instruction such as
            // `setstage` or a Papyrus cast needs the stage number and the actual form.
            if (args.Length > 1 && args[0] == "qfrag")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                var id = Convert.ToUInt32(args[1], 16);
                var q = esm.Quests.FirstOrDefault(x => x.FormKey.ID == id);
                if (q == null) { Console.WriteLine("no such quest"); return; }
                Console.WriteLine($"QUST {q.FormKey.ID:X6} {q.EditorID}  event={q.Event}  flags={q.Flags}");
                foreach (var al in q.Aliases)
                    Console.WriteLine($"  alias {al.ID,3} {al.Name,-24} forced={al.ForcedReference.FormKeyNullable}");
                var vm = q.VirtualMachineAdapter;
                if (vm == null) { Console.WriteLine("  (no scripts)"); return; }
                var qcache = esm.ToImmutableLinkCache();
                string Named(FormKey? fk) =>
                    fk is FormKey k && qcache.TryResolve(k, out var rec) ? $"{k} {rec.EditorID}" : fk?.ToString() ?? "(none)";
                foreach (var f in vm.Fragments)
                    Console.WriteLine($"  stage {f.Stage,4} index {f.StageIndex} -> {f.ScriptName}.{f.FragmentName}");
                foreach (var st in q.Stages)
                    Console.WriteLine($"  stage record {st.Index} logEntries={st.LogEntries.Count}");
                foreach (var sc in vm.Scripts)
                {
                    Console.WriteLine($"  script {sc.Name}");
                    foreach (var pr in sc.Properties)
                    {
                        string v = pr switch
                        {
                            IScriptObjectPropertyGetter o => Named(o.Object.FormKeyNullable),
                            IScriptIntPropertyGetter i => i.Data.ToString(),
                            IScriptFloatPropertyGetter fl => fl.Data.ToString(),
                            IScriptBoolPropertyGetter b => b.Data.ToString(),
                            _ => pr.GetType().Name
                        };
                        Console.WriteLine($"    {pr.Name,-28} = {v}");
                    }
                }
                return;
            }

            // Every door inside a cell and every door that leads into it, with its lock and
            // key. Locking a house means locking the right references; this names them.
            if (args.Length > 1 && args[0] == "doors")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                var dcache = esm.ToImmutableLinkCache();
                var inside = new HashSet<FormKey>();
                var doorRefs = new List<(IPlacedObjectGetter rec, string where)>();
                foreach (var ctx in esm.EnumerateMajorRecordContexts<IPlacedObject, IPlacedObjectGetter>(dcache))
                {
                    if (!dcache.TryResolve<IDoorGetter>(ctx.Record.Base.FormKey, out _)) continue;
                    var parent = ctx.Parent;
                    while (parent != null && parent.Record is not ICellGetter) parent = parent.Parent;
                    var cellRec = parent?.Record as ICellGetter;
                    string where = cellRec?.EditorID ?? $"(exterior cell {cellRec?.FormKey.ID:X6})";
                    doorRefs.Add((ctx.Record, where));
                    if (string.Equals(cellRec?.EditorID, args[1], StringComparison.OrdinalIgnoreCase))
                        inside.Add(ctx.Record.FormKey);
                }
                foreach (var (rec, where) in doorRefs)
                {
                    var dest = rec.TeleportDestination?.Door.FormKeyNullable;
                    bool isInside = inside.Contains(rec.FormKey);
                    bool leadsIn = dest is FormKey dk && inside.Contains(dk);
                    if (!isInside && !leadsIn) continue;
                    Console.WriteLine($"  {(isInside ? "inside " : "leadsIn")} ref={rec.FormKey.ID:X8} cell={where} base={rec.Base.FormKey.ID:X6} " +
                                      $"dest={(dest is FormKey dd ? dd.ID.ToString("X8") : "-")} lockLevel={rec.Lock?.Level} key={rec.Lock?.Key.FormKeyNullable} persistent={rec.MajorRecordFlagsRaw & 0x400}");
                }
                return;
            }

            if (args.Length > 1 && args[0] == "quest")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                uint qid = Convert.ToUInt32(args[1], 16);
                var q = esm.Quests.FirstOrDefault(x => x.FormKey.ID == qid);
                if (q == null) { Console.WriteLine($"QUST {qid:X6} 없음"); return; }

                Console.WriteLine($"QUST {q.FormKey.ID:X6}  {q.EditorID}  \"{q.Name}\"");
                Console.WriteLine($"  flags={q.Flags} type={q.Type} priority={q.Priority}");

                Console.WriteLine("  --- 붙은 스크립트 ---");
                foreach (var sc in q.VirtualMachineAdapter?.Scripts ?? new List<IScriptEntryGetter>())
                {
                    Console.WriteLine($"    script {sc.Name} props={sc.Properties.Count}");
                    foreach (var pr in sc.Properties)
                        Console.WriteLine($"        {pr.Name} ({pr.GetType().Name.Replace("ScriptProperty", "")})");
                }

                Console.WriteLine("  --- 목표(Objectives) ---");
                foreach (var o in q.Objectives)
                    Console.WriteLine($"    obj {o.Index,-4} \"{o.DisplayText}\"");

                Console.WriteLine("  --- 단계(Stages) ---");
                foreach (var st in q.Stages.OrderBy(x => x.Index))
                {
                    string flags = st.Flags.ToString();
                    foreach (var le in st.LogEntries)
                    {
                        var txt = le.Entry?.String ?? "";
                        Console.WriteLine($"    stage {st.Index,-4} [{flags}] \"{txt}\"");
                    }
                    if (st.LogEntries.Count == 0)
                        Console.WriteLine($"    stage {st.Index,-4} [{flags}]");
                }
                return;
            }

            // Which quest carries a given script? Needed before you can cast a Quest
            // to a vanilla script type and read its properties.
            //   dotnet run -- script <ScriptName>
            if (args.Length > 1 && args[0] == "script")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                string want = args[1];
                foreach (var q in esm.Quests)
                {
                    var scripts = q.VirtualMachineAdapter?.Scripts;
                    if (scripts == null) continue;
                    foreach (var sc in scripts)
                    {
                        if (!string.Equals(sc.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
                        Console.WriteLine($"  QUST {q.FormKey.ID:X6}  {q.EditorID,-24} \"{q.Name}\"  props={sc.Properties.Count}");
                    }
                }
                return;
            }

            // Dump every condition on one dialogue INFO. Used to read vanilla's own
            // test for a piece of player state off the record rather than guessing it.
            //   dotnet run -- info <formid-hex>
            if (args.Length > 1 && args[0] == "info")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                uint iid = Convert.ToUInt32(args[1], 16);
                foreach (var d in esm.DialogTopics)
                    foreach (var r in d.Responses)
                    {
                        if (r.FormKey.ID != iid) continue;
                        Console.WriteLine($"INFO {r.FormKey.ID:X6} in DIAL {d.FormKey.ID:X6} {d.EditorID}");
                        foreach (var resp in r.Responses)
                            Console.WriteLine($"  응답: \"{resp.Text}\"");
                        foreach (var c in r.Conditions)
                        {
                            string fn = c.Data.GetType().Name.Replace("ConditionData", "");
                            var links = new List<string>();
                            foreach (var pi in c.Data.GetType().GetProperties())
                            {
                                if (pi.GetIndexParameters().Length > 0) continue;
                                object? v = null;
                                try { v = pi.GetValue(c.Data); } catch { }
                                if (v == null) continue;
                                var ns = v.GetType().Namespace ?? "";
                                if (!ns.StartsWith("Mutagen")) continue;
                                foreach (var pi2 in v.GetType().GetProperties())
                                {
                                    if (pi2.Name != "Link") continue;
                                    var link = pi2.GetValue(v);
                                    var fk = link?.GetType().GetProperty("FormKeyNullable")?.GetValue(link);
                                    if (fk is FormKey k)
                                        links.Add($"{pi.Name}={k.ID:X6}:{k.ModKey}");
                                }
                            }
                            string extra = links.Count > 0 ? "  [" + string.Join(", ", links) + "]" : "";
                            Console.WriteLine($"  cond {fn,-26} RunOn={c.Data.RunOnType,-12} {c.CompareOperator} {CondValue(c)}  flags={c.Flags}{extra}");
                        }
                        return;
                    }
                Console.WriteLine($"INFO {iid:X6} 없음");
                return;
            }

            if (args.Length > 0 && args[0] == "qflags")
            {
                Console.WriteLine("=== Mutagen Quest.Flag 값 ===");
                foreach (var name in Enum.GetNames(typeof(Quest.Flag)))
                    Console.WriteLine($"  0x{(int)Enum.Parse(typeof(Quest.Flag), name):X3}  {name}");

                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                Console.WriteLine();
                Console.WriteLine("=== 바닐라 대화 퀘스트의 플래그 ===");
                foreach (var q in esm.Quests)
                {
                    var e = q.EditorID ?? "";
                    if (e != "DialogueWhiterun" && e != "DialogueRiverwood" && e != "DialogueGeneric"
                        && e != "DialogueFavorGeneric" && e != "PerkInvestor") continue;
                    Console.WriteLine($"  {e,-22} flags=0x{(int)q.Flags:X3} ({q.Flags})  prio={q.Priority} type={q.Type} stages={q.Stages.Count} aliases={q.Aliases.Count}");
                }
                Console.WriteLine();
                Console.WriteLine("=== 우리 퀘스트 ===");
                using var ours = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/BankPrismUI/BankPrismUI.esp", SkyrimRelease.SkyrimSE);
                foreach (var q in ours.Quests)
                    Console.WriteLine($"  {q.EditorID,-22} flags=0x{(int)q.Flags:X3} ({q.Flags})  prio={q.Priority} type={q.Type} stages={q.Stages.Count} aliases={q.Aliases.Count}");
                return;
            }

            if (args.Length > 0 && args[0] == "strict")
            {
                // The overlay reader is lazy and will happily skip past a record the game
                // would choke on. Parsing the whole plugin eagerly forces every record to
                // be read, so a malformed one shows up here instead of as a silently
                // missing topic in game.
                var path = @"C:/TAKEALOOK/BankPrismUI/BankPrismUI.esp";
                var strict = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);

                Console.WriteLine("엄격 파싱 통과");
                Console.WriteLine($"  masters      = {string.Join(", ", strict.ModHeader.MasterReferences.Select(m => m.Master.FileName))}");
                Console.WriteLine($"  globals      = {strict.Globals.Count}");
                Console.WriteLine($"  quests       = {strict.Quests.Count}");
                Console.WriteLine($"  books        = {strict.Books.Count}");
                Console.WriteLine($"  formLists    = {strict.FormLists.Count}");
                Console.WriteLine($"  dialogTopics = {strict.DialogTopics.Count}");
                Console.WriteLine($"  dialogBranch = {strict.DialogBranches.Count}");

                int infos = strict.DialogTopics.Sum(d => d.Responses.Count);
                Console.WriteLine($"  infos        = {infos}");

                Console.WriteLine();
                Console.WriteLine("  헤더가 신고한 레코드 수와 실제 개수:");
                Console.WriteLine($"    HEDR.NumRecords = {strict.ModHeader.Stats.NumRecords}");
                int actual = strict.EnumerateMajorRecords().Count();
                Console.WriteLine($"    실제 메이저 레코드 = {actual}");
                Console.WriteLine($"    NextFormID = {strict.ModHeader.Stats.NextFormID:X}");

                uint maxUsed = strict.EnumerateMajorRecords().Max(r => r.FormKey.ID);
                Console.WriteLine($"    실제 최대 FormID = {maxUsed:X}");
                if (strict.ModHeader.Stats.NextFormID <= maxUsed)
                    Console.WriteLine("    *** NextFormID 가 사용 중인 ID 이하입니다 ***");
                return;
            }

            if (args.Length > 0 && args[0] == "stewards")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var stewardFaction = new FormKey(new ModKey("Skyrim", ModType.Master), 0x050922);
                var holdNames = new Dictionary<uint, string>
                {
                    { 0x0267EA, "Whiterun" }, { 0x029DB0, "Haafingar" }, { 0x0267E3, "Eastmarch" },
                    { 0x02816B, "Rift" },     { 0x02816C, "Reach" },     { 0x028170, "Falkreath" },
                    { 0x02816D, "Hjaalmarch" }, { 0x02816E, "Pale" },    { 0x02816F, "Winterhold" },
                };

                foreach (var npc in esm.Npcs)
                {
                    if (!npc.Factions.Any(fr => fr.Faction.FormKeyNullable == stewardFaction)) continue;
                    var crime = npc.CrimeFaction.FormKeyNullable;
                    string hold = "(홀드 아님)";
                    if (crime != null && holdNames.TryGetValue(crime.Value.ID, out var h)) hold = h;
                    Console.WriteLine($"  {hold,-11} {npc.Name?.String,-22} ({npc.EditorID})");
                }
                return;
            }

            if (args.Length > 0 && args[0] == "notes")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                // What a vanilla note actually carries, so ours can match it.
                int shown = 0;
                foreach (var b in esm.Books)
                {
                    var e = b.EditorID ?? "";
                    if (e.IndexOf("Note", StringComparison.OrdinalIgnoreCase) < 0 &&
                        e.IndexOf("Letter", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (b.Model == null) continue;

                    Console.WriteLine($"BOOK {b.FormKey.ID:X6} {e}");
                    Console.WriteLine($"    Name        = {b.Name}");
                    Console.WriteLine($"    Model       = {b.Model?.File}");
                    Console.WriteLine($"    InventoryArt= {b.InventoryArt.FormKeyNullable}");
                    Console.WriteLine($"    Flags       = {b.Flags}");
                    Console.WriteLine($"    Type        = {b.Type}");
                    Console.WriteLine($"    Value/Weight= {b.Value} / {b.Weight}");
                    Console.WriteLine($"    PickUpSound = {b.PickUpSound.FormKeyNullable}");
                    Console.WriteLine($"    PutDownSound= {b.PutDownSound.FormKeyNullable}");
                    Console.WriteLine($"    Keywords    = {b.Keywords?.Count ?? 0}");
                    if (++shown >= 3) break;
                }
                return;
            }

            if (args.Length > 0 && args[0] == "standing")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                Console.WriteLine("=== 메인 퀘스트 ===");
                foreach (var q in esm.Quests)
                {
                    var e = q.EditorID ?? "";
                    if (!e.StartsWith("MQ1") && !e.StartsWith("MQ2") && e != "MQ306") continue;
                    Console.WriteLine($"  {q.FormKey.ID:X6}  {e,-10}  \"{q.Name}\"  stages={q.Stages.Count}");
                }

                Console.WriteLine();
                Console.WriteLine("=== 세인(Thane) 관련 팩션 ===");
                foreach (var f in esm.Factions)
                {
                    var e = f.EditorID ?? "";
                    if (e.IndexOf("Thane", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine($"  {f.FormKey.ID:X6}  {e}");
                }

                Console.WriteLine();
                Console.WriteLine("=== 세인 임명 퀘스트(Favor) ===");
                int n = 0;
                foreach (var q in esm.Quests)
                {
                    var e = q.EditorID ?? "";
                    if (e.IndexOf("Thane", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine($"  {q.FormKey.ID:X6}  {e,-28} \"{q.Name}\" stages={q.Stages.Count}");
                    if (++n >= 15) break;
                }

                Console.WriteLine();
                Console.WriteLine("=== 배달부 퀘스트 확인 ===");
                var courier = new FormKey(new ModKey("Skyrim", ModType.Master), 0x039F82);
                var cq = esm.Quests.FirstOrDefault(q => q.FormKey == courier);
                Console.WriteLine($"  {courier} -> {cq?.EditorID ?? "(찾지 못함)"}");
                return;
            }

            if (args.Length > 0 && args[0] == "gmst")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var wanted = new[]
                {
                    "iDaysToRespawnVendor", "iHoursToRespawnCell", "iHoursToRespawnCellCleared",
                    "iCrimeGoldMurder", "iCrimeGoldAssault", "iCrimeGoldTrespass",
                    "iCrimeGoldPickpocket", "iCrimeGoldEscape", "fCrimeGoldSteal",
                    "iCrimeGoldWerewolf", "iCrimeAlarmLowRecDistance",
                };

                foreach (var g in esm.GameSettings)
                {
                    var e = g.EditorID ?? "";
                    if (!wanted.Contains(e)) continue;
                    string val = g switch
                    {
                        IGameSettingIntGetter gi => gi.Data?.ToString() ?? "(null)",
                        IGameSettingFloatGetter gf => gf.Data?.ToString() ?? "(null)",
                        IGameSettingStringGetter gs => gs.Data?.String ?? "(null)",
                        _ => "(?)"
                    };
                    Console.WriteLine($"  {e,-30} = {val}");
                }
                return;
            }

            if (args.Length > 0 && args[0] == "ordiff")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                void Dump(string tag, IConditionGetter c)
                {
                    var cf = c as IConditionFloatGetter;
                    var gf = c.Data as IGetInFactionConditionDataGetter;
                    Console.WriteLine($"    {tag} flags={c.Flags} op={c.CompareOperator} value={cf?.ComparisonValue}");
                    Console.WriteLine($"        data={c.Data.GetType().Name} runOn={c.Data.RunOnType} refr={c.Data.Reference.FormKeyNullable} idx={c.Data.RunOnTypeIndex}");
                    if (gf != null) Console.WriteLine($"        faction={gf.Faction.Link.FormKeyNullable}");
                }

                Console.WriteLine("=== 바닐라: OR 플래그를 쓰는 대화문 INFO ===");
                int shown = 0;
                foreach (var d in esm.DialogTopics)
                {
                    foreach (var r in d.Responses)
                    {
                        if (r.Conditions.Count < 2) continue;
                        if (!r.Conditions.Any(c => c.Flags.HasFlag(Condition.Flag.OR))) continue;
                        if (!r.Conditions.Any(c => c.Data is IGetInFactionConditionDataGetter)) continue;

                        Console.WriteLine($"  DIAL {d.FormKey.ID:X6} {d.EditorID} / INFO {r.FormKey.ID:X6} ({r.Conditions.Count} conditions)");
                        foreach (var c in r.Conditions) Dump("cond", c);
                        if (++shown >= 2) break;
                    }
                    if (shown >= 2) break;
                }
                if (shown == 0) Console.WriteLine("  (해당 사례 없음)");

                Console.WriteLine();
                Console.WriteLine("=== 우리 ESP ===");
                using var ours = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/BankPrismUI/BankPrismUI.esp", SkyrimRelease.SkyrimSE);
                foreach (var d in ours.DialogTopics)
                    foreach (var r in d.Responses)
                    {
                        Console.WriteLine($"  {d.EditorID} / {r.EditorID} ({r.Conditions.Count} conditions)");
                        foreach (var c in r.Conditions) Dump("cond", c);
                    }
                return;
            }

            if (args.Length > 0 && args[0] == "ranks")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                void Show(uint facId, string label, uint npcId, string npcLabel)
                {
                    var fk = new FormKey(new ModKey("Skyrim", ModType.Master), facId);
                    var nk = new FormKey(new ModKey("Skyrim", ModType.Master), npcId);
                    var npc = esm.Npcs.FirstOrDefault(n => n.FormKey == nk);
                    var f = esm.Factions.FirstOrDefault(x => x.FormKey == fk);
                    var rank = npc?.Factions.FirstOrDefault(fr => fr.Faction.FormKeyNullable == fk);
                    Console.WriteLine($"{label} [{facId:X6}] flags={f?.Flags}");
                    Console.WriteLine($"    {npcLabel} rank={(rank == null ? "(미소속)" : rank.Rank.ToString())}");
                    Console.WriteLine($"    faction ranks defined: {f?.Ranks.Count}");
                }

                Show(0x050922, "JobStewardFaction", 0x013BBA, "Proventus");
                Show(0x09CAF5, "ServicesWhiterunBelethorsGoods", 0x013BA1, "Belethor");
                Show(0x05A665, "ServicesRiverwoodRiverwoodTrader", 0x01347A, "Lucan");

                Console.WriteLine();
                Console.WriteLine("--- 바닐라가 판매 팩션을 조건으로 쓰는 사례 ---");
                var target = new FormKey(new ModKey("Skyrim", ModType.Master), 0x09CAF5);
                int found = 0;
                foreach (var d in esm.DialogTopics)
                    foreach (var r in d.Responses)
                        foreach (var c in r.Conditions)
                            if (c.Data is IGetInFactionConditionDataGetter g &&
                                g.Faction.Link.FormKeyNullable == target)
                            {
                                Console.WriteLine($"  DIAL {d.FormKey.ID:X6} {d.EditorID} / INFO {r.FormKey.ID:X6}");
                                found++;
                            }
                Console.WriteLine($"  벨레쏘어 판매팩션을 GetInFaction 조건으로 쓰는 바닐라 대화문: {found}건");
                return;
            }

            if (args.Length > 0 && args[0] == "credit")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);

                var stores = new (uint faction, string label)[]
                {
                    (0x09CAF5, "Belethor's General Goods"),
                    (0x05A665, "Riverwood Trader"),
                    (0x0A6C02, "Bits and Pieces"),
                    (0x0A31C5, "Pawned Prawn"),
                    (0x094375, "Arnleif and Sons"),
                    (0x0A3F12, "Sadri's Used Wares"),
                    (0x09DA62, "Birna's Oddments"),
                    (0x0A6BFE, "Gray Pine Goods"),
                    (0x09DA5B, "Thaumaturgist's Hut"),
                };

                foreach (var (fac, label) in stores)
                {
                    var key = new FormKey(new ModKey("Skyrim", ModType.Master), fac);
                    var f = esm.Factions.FirstOrDefault(x => x.FormKey == key);
                    Console.WriteLine($"{fac:X6}  {label}  ({f?.EditorID})");
                    foreach (var n in esm.Npcs.Where(n => n.Factions.Any(fr => fr.Faction.FormKeyNullable == key)))
                    {
                        var crime = esm.Factions.FirstOrDefault(x => x.FormKey == n.CrimeFaction.FormKeyNullable);
                        Console.WriteLine($"      {n.Name?.String}  ({n.EditorID})  hold={crime?.EditorID ?? "(없음)"}");
                    }
                }
                return;
            }

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

            // Every faction an NPC belongs to, with rank, and the crime faction the engine
            // hands back from GetCrimeFaction(). Membership and the assigned crime faction
            // are different fields, and a dialogue condition can only see membership - so
            // a hold-restricting condition has to be checked here, not assumed.
            if (args.Length > 1 && args[0] == "factions")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                var byKey = esm.Factions.ToDictionary(f => f.FormKey, f => f.EditorID ?? "");
                foreach (var npc in esm.Npcs)
                {
                    var edid = npc.EditorID ?? "";
                    var name = npc.Name?.String ?? "";
                    if (edid.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var crime = npc.CrimeFaction.FormKeyNullable;
                    var crimeName = crime is FormKey ck && byKey.TryGetValue(ck, out var cn) ? cn : "(none)";
                    Console.WriteLine($"NPC {npc.FormKey}  EDID={edid}  Name={name}  crimeFaction={crimeName}  template={npc.Template.FormKeyNullable}");
                    foreach (var fr in npc.Factions)
                    {
                        var fk = fr.Faction.FormKeyNullable;
                        var fn = fk is FormKey k && byKey.TryGetValue(k, out var n) ? n : fk?.ToString() ?? "?";
                        Console.WriteLine($"    member rank={fr.Rank,-3} {fn}");
                    }
                }
                return;
            }

            // Placed references of an NPC - the id `player.moveto` and `prid` take in the
            // console - with the cell they stand in. Console instructions should quote an
            // id read from the plugin, never one remembered.
            if (args.Length > 1 && args[0] == "refs")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                var cache = esm.ToImmutableLinkCache();
                var wantedNpcs = esm.Npcs
                    .Where(n => (n.EditorID ?? "").IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (n.Name?.String ?? "").IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToDictionary(n => n.FormKey, n => n.EditorID ?? "");
                foreach (var ctx in esm.EnumerateMajorRecordContexts<IPlacedNpc, IPlacedNpcGetter>(cache))
                {
                    var baseKey = ctx.Record.Base.FormKeyNullable;
                    if (baseKey is not FormKey bk || !wantedNpcs.TryGetValue(bk, out var npcEdid)) continue;
                    var parent = ctx.Parent;
                    while (parent != null && parent.Record is not ICellGetter) parent = parent.Parent;
                    var cell = parent?.Record as ICellGetter;
                    Console.WriteLine($"  {npcEdid,-24} ref={ctx.Record.FormKey.ID:X8}  cell={cell?.EditorID ?? "(exterior/none)"} {cell?.FormKey.ID:X6}");
                }
                return;
            }

            // NPCs that belong to every faction named (comma separated EditorIDs), so an AND
            // of GetInFaction conditions can be checked against who it would really reach.
            if (args.Length > 1 && args[0] == "members")
            {
                using var esm = SkyrimMod.CreateFromBinaryOverlay(
                    @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
                var wanted = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                var keys = wanted
                    .Select(w => esm.Factions.FirstOrDefault(f => string.Equals(f.EditorID, w, StringComparison.OrdinalIgnoreCase))?.FormKey)
                    .ToArray();
                for (int i = 0; i < wanted.Length; i++)
                    Console.WriteLine($"  faction {wanted[i]} -> {(keys[i]?.ToString() ?? "NOT FOUND")}");
                if (keys.Any(k => k == null)) return;
                var byKey = esm.Factions.ToDictionary(f => f.FormKey, f => f.EditorID ?? "");
                int count = 0;
                foreach (var npc in esm.Npcs)
                {
                    var mine = npc.Factions.Select(fr => fr.Faction.FormKeyNullable).ToHashSet();
                    if (!keys.All(k => mine.Contains(k))) continue;
                    var crime = npc.CrimeFaction.FormKeyNullable;
                    var crimeName = crime is FormKey ck && byKey.TryGetValue(ck, out var cn) ? cn : "(none)";
                    Console.WriteLine($"  {npc.FormKey.ID:X6} {npc.EditorID,-28} {npc.Name?.String,-24} crimeFaction={crimeName}");
                    count++;
                }
                Console.WriteLine($"  {count} NPC(s)");
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
            // 0x807 retired: the flat merchant credit ceiling, now one global per tier.
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
            const uint IdLoanDueBase  = 0x840;   // game time the loan falls due, 0 = none
            // 0x850-0x858 retired: one letter per hold, from before they were per day.
            const uint IdLetterBase   = 0x900;   // 9 holds x 7 days, 0x900-0x93E
            const uint IdPrincipalBase = 0x870;  // the sum the overdue charge is figured on
            const uint IdCleanRepayBase = 0x880; // loans settled without ever falling due
            const uint IdAccruedDayBase = 0x890; // overdue days already charged
            const uint IdLoanTermDays = 0x863;
            const uint IdOverduePct   = 0x864;
            // Loan ceiling per standing tier, 1..5. Tiers 1-3 keep the ids the old
            // main-quest tiers had; 4 and 5 take new ones, so nothing above moves.
            uint[] loanLimitIds = { 0x860, 0x861, 0x862, 0x865, 0x866 };
            const uint IdCreditLimitBase = 0x8A0;  // merchant credit ceiling per tier
            const uint IdCreditTierBase  = 0x8B0;  // per hold: highest tier recognised
            const uint IdCreditPathBase  = 0x8C0;  // per hold: the deed that earned it
            const uint IdPropertyPledgeBase = 0x8D0; // per hold: 0 none, 1 house pledged, 2 seized
            const uint IdCollateralLtv   = 0x8E0;  // share of the appraisal lent against, percent
            const uint IdForecloseDays   = 0x8E1;  // days overdue before a pledged house is seized
            const uint IdLienFeePct      = 0x8E2;  // fee to release a lien, percent of the appraisal

            FormKey Id(uint value) => new FormKey(mod.ModKey, value);
            FormKey Vanilla(uint value) => new FormKey(new ModKey("Skyrim", ModType.Master), value);

            // The nine holds, in the order the Papyrus side indexes them. This order is
            // part of the save format: reordering it moves every account to another hold.
            var holds = new (string name, string korean, uint crimeFaction)[]
            {
                ("Whiterun",   "화이트런",   0x0267EA),
                ("Haafingar",  "하핑가",     0x029DB0),
                ("Eastmarch",  "이스트마치", 0x0267E3),
                ("Rift",       "리프트",     0x02816B),
                ("Reach",      "리치",       0x02816C),
                ("Falkreath",  "팔크리스",   0x028170),
                ("Hjaalmarch", "하얄마치",     0x02816D),
                ("Pale",       "페일",       0x02816E),
                ("Winterhold", "윈터홀드",   0x02816F),
            };

            // Holds the mod currently serves. Only these are offered the steward and
            // general-store topics, and the Papyrus side refuses the rest in
            // HoldIsEnabled(). Everything else is still generated - globals, letters,
            // array slots - so switching a hold back on is its name here plus its index
            // there, with no change to the save format. The build checks both agree.
            var enabledHolds = new HashSet<string> { "Whiterun" };
            var enabledCrimeFactions = holds.Where(h => enabledHolds.Contains(h.name))
                                            .Select(h => Vanilla(h.crimeFaction)).ToArray();
            var enabledHoldIndexes = holds.Select((h, i) => (h, i))
                                          .Where(x => enabledHolds.Contains(x.h.name))
                                          .Select(x => x.i).ToArray();
            if (enabledCrimeFactions.Length != enabledHolds.Count)
                throw new InvalidOperationException("enabledHolds names a hold that is not in the hold table");

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

            var loanDue = new List<GlobalFloat>();
            var loanPrincipal = new List<GlobalFloat>();
            var cleanRepayments = new List<GlobalFloat>();
            var accruedDays = new List<GlobalFloat>();
            for (uint i = 0; i < holds.Length; i++)
            {
                loanDue.Add(NewGlobal(IdLoanDueBase + i, "BankLoanDue" + holds[i].name));
                loanPrincipal.Add(NewGlobal(IdPrincipalBase + i, "BankLoanPrincipal" + holds[i].name));
                cleanRepayments.Add(NewGlobal(IdCleanRepayBase + i, "BankLoansRepaidClean" + holds[i].name));
                accruedDays.Add(NewGlobal(IdAccruedDayBase + i, "BankLoanDaysCharged" + holds[i].name));
            }

            // No lender fronts gold to an unproven sellsword. What the Jarl's vault will
            // advance, and what a general goods merchant will carry on the slate, both
            // follow the standing tier the Dovahkiin has earned by deed. The ladder is
            // the one the view publishes on its 신용등급 tab; these globals are where the
            // numbers actually live, so tuning them moves both the rule and the display.
            float[] loanLimitValues   = { 0f, 0f, 5000f, 12000f, 25000f };
            float[] creditLimitValues = { 1000f, 1500f, 2000f, 3500f, 6000f };

            var loanLimits = new List<GlobalFloat>();
            var creditLimits = new List<GlobalFloat>();
            for (int t = 0; t < 5; t++)
            {
                loanLimits.Add(NewGlobal(loanLimitIds[t], $"BankLoanLimitTier{t + 1}", loanLimitValues[t]));
                creditLimits.Add(NewGlobal(IdCreditLimitBase + (uint)t, $"MerchantCreditLimitTier{t + 1}", creditLimitValues[t]));
            }

            // Standing is remembered per hold rather than recomputed, because the view
            // presents it as a rank that is fixed on the day it is first recognised:
            // the tier only ever climbs, and the title records which deed earned it.
            var creditTiers = new List<GlobalFloat>();
            var creditPaths = new List<GlobalFloat>();
            for (uint i = 0; i < holds.Length; i++)
            {
                creditTiers.Add(NewGlobal(IdCreditTierBase + i, "BankCreditTier" + holds[i].name));
                creditPaths.Add(NewGlobal(IdCreditPathBase + i, "BankCreditPath" + holds[i].name));
            }

            // Property collateral. Nothing about the house itself is stored here: whether
            // it is owned, what it is worth and which cell it is are all read from vanilla's
            // HousePurchase quest at run time.
            var propertyPledges = new List<GlobalFloat>();
            for (uint i = 0; i < holds.Length; i++)
                propertyPledges.Add(NewGlobal(IdPropertyPledgeBase + i, "BankPropertyPledge" + holds[i].name));
            var collateralLtv = NewGlobal(IdCollateralLtv, "BankCollateralLtvPercent", 60f);
            var forecloseDays = NewGlobal(IdForecloseDays, "BankForecloseOverdueDays", 7f);
            var lienFeePct = NewGlobal(IdLienFeePct, "BankLienReleaseFeePercent", 20f);

            var loanTermDays = NewGlobal(IdLoanTermDays, "BankLoanTermDays", 7f);
            // Charged per DAY overdue, on the original sum. A weekly charge left the
            // figure unchanged while letters arrived every morning, which read as broken;
            // daily keeps the two in step. Simple, not compound, and capped at three
            // times the principal, so it grows in a straight line and then stops.
            var overduePct = NewGlobal(IdOverduePct, "BankLoanOverduePercentPerDay", 3f);
            // A single 20% markup applied once, when a credit purchase is written to the
            // ledger. Nothing accrues afterwards, so no timer or background script is
            // needed and the debt cannot run away on its own.
            var surcharge = NewGlobal(IdSurcharge, "MerchantCreditSurchargePercent", 20f);
            // 210 = DirectX scan code for Insert; 0 disables the test shortcut.
            var debugHotkey = NewGlobal(IdDebugHotkey, "BankPrismDebugHotkey", 12f);

            // Each hold's dunning letter. Only Whiterun's is written so far; the others
            // fall back to a short generic text, so adding one later is a single case.
            //
            // The Jarl is named in the prose rather than looked up. Whiterun changes hands
            // over the civil war - Vignar Gray-Mane replaces Balgruuf if the Stormcloaks
            // take the hold - so this line goes stale in those saves. It is prose, and
            // swapping it is a text edit, which is the trade that was chosen here.
            // ---- 독촉장 문안 ---------------------------------------------------
            // 홀드 9곳 x 연체 1~7일차 = 63통. 문안을 채울 때 건드릴 곳은 아래 표뿐이고
            // 나머지 코드는 손댈 필요가 없다. 빈 칸은 그 날짜의 공용 문안으로 떨어지므로,
            // 채운 만큼만 반영되고 나머지는 그대로 돌아간다.
            //
            // 8일차부터는 별도 이벤트가 붙을 예정이라 7통에서 끊는다.
            //
            // 야를이나 행정관 이름을 문안에 쓸 때 주의: 내전으로 화이트런의 야를이,
            // 리치/하얄마치/팔크리스의 행정관이 바뀐다. 그 세이브에서는 글이 어긋난다.
            const int DunningDays = 7;

            var dunning = new Dictionary<string, string[]>
            {
                ["화이트런"] = new string[DunningDays]
                {
                    "화이트런 영지 행정관\n\n드래곤스리치 금고에서 알립니다. 귀하의 대출 상환 기한이 지났음을 서면으로 안내해 드립니다. 제국과 스톰클로크의 대치 속에서 화이트런의 전시 재정은 한 푼도 놀릴 수 없는 형편입니다. 가까운 시일 안에 드래곤스리치를 방문하시어 장부를 정산해 주시기 바랍니다.",
                    "화이트런 영지 행정관\n\n어제 보내드린 서신을 확인하셨는지요. 드래곤스리치의 장부는 매일 일몰 전 정산되며, 귀하의 연체 기록이 여전히 남아 있습니다. 스카이림의 중심지인 화이트런의 재정은 시국의 혼란으로 매우 긴박합니다. 오늘 중으로 행정관 집무실을 찾아주십시오.",
                    "화이트런 영지 행정관\n\n사흘째 상환이 지연되고 있습니다. 드래곤스리치 금고의 규정에 따라 일일 이자가 장부에 추가로 기록되기 시작했습니다. 불필요한 금화 지출이 늘어나기 전에, 드래곤스리치로 오셔서 대출금을 청산하시길 강력히 권고합니다.",
                    "화이트런 영지 행정관\n\n사흘이 넘도록 소식이 없으시군요. 영지의 경비대 유지비와 성벽 보수 예산이 귀하의 채무로 인해 묶여 있습니다. 야를께서도 영지 재정 보고를 받으시며 장부의 이름을 유심히 살피셨습니다. 더 이상 일을 지체하지 마십시오.",
                    "화이트런 영지 행정관\n\n닷새째입니다. 귀하의 연체는 이제 단순한 건망증으로 보기 어렵습니다. 야를의 공공 금융 시스템은 모험가의 개인 자금줄이 아닙니다. 야를의 인내심을 시험하지 마시고, 즉시 방문하여 정산해 주십시오.",
                    "화이트런 영지 행정관\n\n엿새째 상환이 이루어지지 않았습니다. 이 서신이 전달된 후에도 정산이 되지 않는다면, 영지 법률에 따라 법적 채무 불이행 절차가 착수될 것입니다. 영지의 명예를 생각하시어 오늘 안으로 방문하십시오.",
                    "화이트런 영지 행정관\n\n최후통첩입니다. 일주일의 기한이 완전히 만료되었습니다. 야를의 인내에도 끝이 있으며, 드래곤스리치 금고는 더 이상 대기하지 않습니다. 즉시 채무를 완납하지 않을 경우, 신용 강등 및 강력한 추심 조치가 집행될 것입니다."
                },
                ["하핑가"] = new string[DunningDays]
                {
                    "하핑가 영지 행정관\n\n솔리튜드 야를 금고에서 알립니다. 귀하의 대출 상환 기한이 지났음을 서면으로 안내해 드립니다. 제국의 수도이자 하핑가의 영지 재정 규정은 매우 엄격히 운용되고 있으니, 가급적 빠른 시일 내에 푸른 궁전을 방문하시어 장부를 정산해 주시길 바랍니다.",
                    "하핑가 영지 행정관\n\n어제 안내문에도 불구하고 푸른 궁전에 상환 기록이 접수되지 않았습니다. 하핑가의 금융 자산은 제국군 물자 조달과 영지 방위에 직결되어 있습니다. 귀하의 신용을 위해 조속한 정산을 요청합니다.",
                    "하핑가 영지 행정관\n\n사흘째 채무가 이행되지 않고 있습니다. 하핑가 야를 조례에 따라 일일 연체금이 계속 가산되고 있음을 알려드립니다. 솔리튜드의 신용 관리는 스카이림 전역에 영향을 미치니, 불이익을 받지 않도록 유의하십시오.",
                    "하핑가 영지 행정관\n\n나흘째 소식이 없으시군요. 솔리튜드 무역 항구와 금고 관리관들이 귀하의 채무건을 지목하고 있습니다. 제국의 법도 아래 야를의 자금을 무단으로 유용하는 행위는 엄중히 다루어집니다. 즉시 방문하십시오.",
                    "하핑가 영지 행정관\n\n닷새째입니다. 푸른 궁전의 재정 회의에서 귀하의 연체 내역이 공식 안건으로 상정되었습니다. 야를의 조정은 약속을 이행하지 않는 채무자에게 관대하지 않습니다. 오늘 중으로 청산하시기 바랍니다.",
                    "하핑가 영지 행정관\n\n엿새째입니다. 이 서신을 수신하는 즉시 솔리튜드 궁전의 행정관실로 오십시오. 영지 법원과 채권 보증관에게 문서가 이관되기 직전입니다. 귀하의 명예와 신용을 지킬 마지막 기회입니다.",
                    "하핑가 영지 행정관\n\n최후의 경고입니다. 일주일간의 유예 기간이 끝났습니다. 하핑가 야를의 인내심은 바닥났으며, 법정 채무 불이행에 따른 법적·행정적 제재 절차가 시작될 것입니다. 즉시 채무 전체를 완납하십시오."
                },
                ["이스트마치"] = new string[DunningDays]
                {
                    "이스트마치 영지 행정관\n\n왕의 궁전 금고에서 서신을 보냅니다. 영지 대출금의 상환 기한이 경과하였습니다. 전선의 추위와 전장의 번잡함 속에서 깜빡하셨으리라 믿습니다. 조속히 윈드헬름의 궁전을 찾아 장부를 정산해 주십시오.",
                    "이스트마치 영지 행정관\n\n어제 보낸 서신에 답이 없으시군요. 이스트마치의 금고는 스카이림의 자존심과 노드 형제들의 피땀으로 유지됩니다. 상환이 늦어질수록 영지의 군자금 운용에 차질이 생기니, 오늘 안으로 정산하시길 바랍니다.",
                    "이스트마치 영지 행정관\n\n사흘째 상환 소식이 없습니다. 윈드헬름 야를 규칙에 의해 연체 이자가 장부에 적히기 시작했습니다. 노드의 명예는 약속을 지키는 데서 나옵니다. 불필요한 금화가 더 나가기 전에 완납하십시오.",
                    "이스트마치 영지 행정관\n\n나흘이 지났습니다. 영지 전선의 병사들에게 물자를 공급해야 할 금전이 귀하의 채무로 인해 막혀 있습니다. 야를께서도 재정 보고를 받으시며 굳은 표정을 지으셨습니다. 일을 더 키우지 마십시오.",
                    "이스트마치 영지 행정관\n\n닷새째입니다. 진정한 스카이림의 자손이라면 약속한 금화를 제때 갚아야 하는 법입니다. 야를의 신용을 묵살하는 행위는 더 이상 용납되지 않습니다. 즉시 왕의 궁전으로 오십시오.",
                    "이스트마치 영지 행정관\n\n엿새째입니다. 내일이면 야를의 유예 기한이 완전히 끝납니다. 이 서신을 받는 즉시 궁전으로 오지 않는다면, 영지 강제 집행 절차가 시작될 것입니다. 전사의 명예를 더럽히지 마십시오.",
                    "이스트마치 영지 행정관\n\n최후의 경고문입니다. 일주일의 시간이 모두 흘렀습니다. 이스트마치의 인내심은 한계에 달했으며, 더 이상의 관용은 없습니다. 즉시 밀린 채무 전액을 상환하지 않을 시 강력한 법적 조치가 집행됩니다."
                },
                ["리프트"] = new string[DunningDays]
                {
                    "리프트 영지 행정관\n\n안개베일 저택 금고에서 알립니다. 영지 대출 상환 기한이 지났습니다. 리프튼의 거리가 다소 어수선하더라도 야를과의 금전 약속은 엄격히 지켜져야 합니다. 조속히 저택을 방문하여 정산해 주십시오.",
                    "리프트 영지 행정관\n\n어제 보낸 서신을 받으셨을 줄 압니다. 리프튼의 장부는 음지의 거래와 달라서 한번 어그러지면 바로 기록에 남습니다. 귀하의 신용을 위해 오늘 안으로 안개베일 저택을 찾아주시기 바랍니다.",
                    "리프트 영지 행정관\n\n사흘째 상환이 지연되고 있군요. 영지 규정에 따라 매일 이자가 가산되고 있습니다. 리프튼에서 빚을 지고 오래 비우는 것은 본인에게 전혀 이롭지 않습니다. 조속히 채무를 청산하십시오.",
                    "리프트 영지 행정관\n\n나흘째 소식이 없으십니다. 영지 재정관들과 야를의 조언자들이 귀하의 이름이 적힌 장부를 주시하고 있습니다. 리프튼의 금고 자금을 묶어두는 것은 위험한 일입니다. 즉시 방문하십시오.",
                    "리프트 영지 행정관\n\n닷새째입니다. 야를의 금융을 가볍게 여기는 태도는 묵과할 수 없습니다. 더 이상 회피하지 마시고 안개베일 저택으로 오셔서 밀린 채무를 완납하시기 바랍니다.",
                    "리프트 영지 행정관\n\n엿새째입니다. 내일이면 야를이 제공하는 마지막 유예 기한이 만료됩니다. 제3자 추심이나 영지 법률 조치가 내려지기 전에, 오늘 안으로 오셔서 사건을 마무리지으십시오.",
                    "리프트 영지 행정관\n\n최후통첩입니다. 일주일의 기한이 끝났습니다. 안개베일 저택은 더 이상 서신을 보내지 않을 것입니다. 즉시 채무를 전액 완납하지 않으면 영지 차원의 강제 집행 절차가 시작됩니다."
                },
                ["리치"] = new string[DunningDays]
                {
                    "리치 영지 행정관\n\n언더스톤 성채 금고에서 통지합니다. 귀하의 대출 상환 기한이 만료되었습니다. 마르카스의 돌벽처럼 야를의 금전 계약은 엄정하고 확고합니다. 조속히 궁전을 방문하여 대출금을 정산하십시오.",
                    "리치 영지 행정관\n\n어제 발송한 경고문에도 불구하고 상환이 이루어지지 않았습니다. 리치의 은광산과 영지 재정은 엄격히 관리되고 있습니다. 불이익을 받지 않도록 오늘 중으로 궁전 행정관실을 찾으십시오.",
                    "리치 영지 행정관\n\n사흘째 채무가 미납 상태입니다. 언더스톤 성채의 장부에 따라 일일 이자가 계속 누적되고 있습니다. 은화 한 닢도 허투루 쓰이지 않는 리치에서 채무 지연은 용납되지 않습니다.",
                    "리치 영지 행정관\n\n나흘이 지났습니다. 포스원과의 경계 및 영지 방위에 쓰여야 할 공금이 귀하의 채무로 인해 차질을 빚고 있습니다. 야를과 가문 대표들이 주시하고 있으니 즉시 방문하십시오.",
                    "리치 영지 행정관\n\n닷새째입니다. 마르카스의 돌처럼 단단한 야를의 인내심도 금이 가고 있습니다. 귀하의 채무 불이행은 야를의 공공 질서를 흔드는 행위입니다. 즉시 오셔서 상환하십시오.",
                    "리치 영지 행정관\n\n엿새째입니다. 내일이면 법적 유예 기한이 마감됩니다. 이 서신을 받는 즉시 언더스톤 성채로 오지 않는다면, 영지 차원의 채권 강제 회수 절차가 시작될 것입니다.",
                    "리치 영지 행정관\n\n최후통첩입니다. 일주일의 시간이 완전히 끝났습니다. 마르카스 야를의 인내는 마침표를 찍었습니다. 즉시 채무 전액을 청산하지 않을 시, 법적·행정적 제재가 즉각 집행됩니다."
                },
                ["팔크리스"] = new string[DunningDays]
                {
                    "팔크리스 영지 행정관\n\n팔크리스 야를 금고에서 알립니다. 대출 상환 기한이 지났습니다. 묘지와 숲으로 둘러싸인 조용한 영지이지만, 금전 약속만큼은 명확히 이행되어야 합니다. 조속히 방문하시어 정산해 주십시오.",
                    "팔크리스 영지 행정관\n\n어제 보낸 서신을 받으셨겠지요. 팔크리스의 장부는 작지만 엄격하게 정산됩니다. 귀하의 채무가 영지 소문으로 번지기 전에, 오늘 안으로 오셔서 정리하시길 권합니다.",
                    "팔크리스 영지 행정관\n\n사흘째 소식이 없군요. 영지 조례에 의해 연체금이 가산되고 있습니다. 죽은 자는 말이 없으나 산 자는 약속을 지켜야 하는 법입니다. 조속히 채무를 상환하십시오.",
                    "팔크리스 영지 행정관\n\n나흘째입니다. 영지의 소소한 재정이 귀하의 채무로 인해 경색되고 있습니다. 야를께서도 재정 보고를 받으시며 유심히 살피셨습니다. 더 이상 지체하지 마십시오.",
                    "팔크리스 영지 행정관\n\n닷새째입니다. 야를의 금고를 개인 주머니처럼 여기는 태도는 용납할 수 없습니다. 조용한 영지라고 해서 야를의 권위까지 가벼운 것은 아닙니다. 즉시 오셔서 정산하십시오.",
                    "팔크리스 영지 행정관\n\n엿새째입니다. 마지막 유예 기한이 다가오고 있습니다. 오늘 안으로 오셔서 정산하지 않으시면, 영지 법률에 따른 영구 신용 강등 및 채권 이관 조치가 시작됩니다.",
                    "팔크리스 영지 행정관\n\n최후의 서신입니다. 일주일의 기한이 모두 지났습니다. 팔크리스 야를은 더 이상 기다리지 않을 것입니다. 즉시 채무 전체를 완납하여 신상에 불이익이 없도록 하십시오."
                },
                ["하얄마치"] = new string[DunningDays]
                {
                    "하얄마치 영지 행정관\n\n하이문 홀 금고에서 알립니다. 대출 상환 기한이 경과하였습니다. 안개 낀 늪지대의 한적한 영지이지만 야를과의 약속은 엄연합니다. 조속히 모탈을 방문하시어 정산해 주십시오.",
                    "하얄마치 영지 행정관\n\n어제 서신을 보내드렸으나 답이 없으시군요. 하얄마치의 재정 규모는 크지 않아 단 한 명의 채무 지연도 영지 운용에 큰 영향을 줍니다. 오늘 중으로 오셔서 정산해 주십시오.",
                    "하얄마치 영지 행정관\n\n사흘째 상환이 지연되고 있습니다. 영지 규정상 매일 연체 이자가 추가로 가산되고 있음을 알려드립니다. 늪지대의 안개처럼 채무를 어물쩍 넘길 수는 없습니다.",
                    "하얄마치 영지 행정관\n\n나흘째 소식이 없으시군요. 영지 주민들을 위한 예산이 귀하의 채무로 인해 고갈되고 있습니다. 야를의 예리한 시선이 장부에 머물고 있으니 즉시 방문하십시오.",
                    "하얄마치 영지 행정관\n\n닷새째입니다. 하이문 홀의 관용을 악용하지 마십시오. 야를과의 약속을 어기는 것은 모탈 전체를 무시하는 것과 같습니다. 즉시 오셔서 채무를 청산하십시오.",
                    "하얄마치 영지 행정관\n\n엿새째입니다. 내일이면 야를이 마련한 유예 기한이 만료됩니다. 법적 제재와 신용 불이익 조치가 집행되기 전에, 오늘 안으로 하이문 홀을 찾으십시오.",
                    "하얄마치 영지 행정관\n\n최후통첩입니다. 일주일의 시간이 만료되었습니다. 하얄마치 야를의 관용은 끝났습니다. 즉시 채무 전체를 상환하지 않을 경우, 즉각적인 법적 조치가 집행될 것입니다."
                },
                ["페일"] = new string[DunningDays]
                {
                    "페일 영지 행정관\n\n화이트 홀 금고에서 서신을 전합니다. 귀하의 대출 상환 기한이 지났습니다. 던스타의 얼어붙은 포구와 광산의 노동자들을 위해서라도 영지 재정은 신속히 회수되어야 합니다. 방문하여 정산해 주십시오.",
                    "페일 영지 행정관\n\n어제 안내에도 불구하고 소식이 없으시군요. 페일 영지의 금고는 광산 수입과 무역에 의존하므로 정산 지연에 민감합니다. 오늘 안으로 화이트 홀을 방문해 주시기 바랍니다.",
                    "페일 영지 행정관\n\n사흘째 상환이 이행되지 않고 있습니다. 페일 야를 조례에 따라 일일 연체금이 가산되고 있습니다. 얼음처럼 차가운 이자가 쌓이기 전에 조속히 완납하시길 권합니다.",
                    "페일 영지 행정관\n\n나흘째 소식이 없군요. 영지의 경비대 유지와 포구 관리에 쓰일 자금이 귀하의 미납으로 묶여 있습니다. 야를의 인내심이 얇은 얼음판 같아지고 있으니 즉시 오십시오.",
                    "페일 영지 행정관\n\n닷새째입니다. 페일의 추위보다 더 차갑게 야를의 약속을 외면하시는군요. 더 이상의 지체는 야를에 대한 불경으로 간주됩니다. 즉시 화이트 홀로 오십시오.",
                    "페일 영지 행정관\n\n엿새째입니다. 내일이면 유예 기한이 끝나고 법적 제재 절차로 넘어가게 됩니다. 귀하의 신용과 명예를 위해, 오늘 중으로 방문하여 사건을 마무리하십시오.",
                    "페일 영지 행정관\n\n최후통첩입니다. 일주일의 유예 기한이 완전히 마감되었습니다. 화이트 홀 금고는 더 이상 서신을 보내지 않습니다. 즉시 채무를 전액 완납하지 않을 시 법적 강제 집행이 시작됩니다."
                },
                ["윈터홀드"] = new string[DunningDays]
                {
                    "윈터홀드 영지 행정관\n\n윈터홀드 야를 금고에서 알립니다. 대출 상환 기한이 지났습니다. 대붕괴 이후 영지의 재정이 매우 빈곤하여 금화 한 닢이 절실합니다. 조속히 야를의 처소를 찾아 정산해 주십시오.",
                    "윈터홀드 영지 행정관\n\n어제 보낸 서신을 확인하셨는지요. 윈터홀드의 황량한 폐허 속에서도 야를의 장부는 엄격히 유지됩니다. 야를 재정을 위해 오늘 중으로 정산해 주시길 부탁드립니다.",
                    "윈터홀드 영지 행정관\n\n사흘째 상환이 지연되고 있습니다. 영지 규정에 의해 이자가 계속 쌓이고 있습니다. 마법대학과의 갈등과 가난 속에서 야를의 금전을 미루는 것은 야를을 욕보이는 일입니다.",
                    "윈터홀드 영지 행정관\n\n나흘이 지났습니다. 영지 주민들을 겨우 부양하고 있는 야를의 금고가 귀하의 채무 때문에 비어 가고 있습니다. 야를께서 매우 분노하고 계시니 즉시 오십시오.",
                    "윈터홀드 영지 행정관\n\n닷새째입니다. 몰락한 영지라 하여 야를의 권위까지 몰락한 것은 아닙니다. 야를의 은혜를 배신하지 마시고, 즉시 방문하여 밀린 대출금을 완납하십시오.",
                    "윈터홀드 영지 행정관\n\n엿새째입니다. 내일이면 마지막 유예 기한이 끝납니다. 이 서신을 받는 즉시 야를의 처소로 오지 않으신다면, 영지 차원의 강제 회수 및 법적 조치가 시작될 것입니다.",
                    "윈터홀드 영지 행정관\n\n최후통첩입니다. 일주일의 시간이 완전히 흘렀습니다. 윈터홀드의 인내심은 바닥났습니다. 즉시 채무 전액을 완납하지 않을 경우, 즉각적인 법적·행정적 제재가 집행됩니다."
                },
            };

            // 아직 쓰이지 않은 칸을 메우는 공용 문안. 날짜가 갈수록 인내심이 줄어든다.
            string GenericDunning(string koreanHold, int day)
            {
                string opening;
                if (day <= 2)
                    opening = "야를께서 호의로 내어주신 돈이 정해진 날을 넘겼습니다. 잊으신 " +
                              "것이라 믿고, 우선 서신으로 알려 드립니다.";
                else if (day <= 5)
                    opening = "장부는 하루가 지날 때마다 불어나고 있습니다. 궁의 셈은 사람의 " +
                              "사정을 헤아리지 않으니, 늦을수록 갚으실 몫만 커집니다.";
                else
                    opening = "여러 날 서신을 보냈으나 답이 없었습니다. 나는 기다리는 일에 " +
                              "익숙하지 않고, 야를의 인내에도 끝이 있습니다.";

                return
                    koreanHold + " 야를궁의 장부에 귀하의 이름이 올라 있습니다. 연체 "
                    + day + "일째입니다.\n\n" +
                    opening + "\n\n" +
                    "야를의 호의를 배신하지 마십시오.\n\n" +
                    "궁으로 찾아와 장부를 정리하십시오.\n\n" +
                    "― " + koreanHold + " 야를의 행정관";
            }

            string DunningLetter(string koreanHold, int day)
            {
                if (dunning.TryGetValue(koreanHold, out var written)
                    && day >= 1 && day <= written.Length
                    && !string.IsNullOrWhiteSpace(written[day - 1]))
                {
                    return written[day - 1];
                }
                return GenericDunning(koreanHold, day);
            }
            // A letter per hold, handed to the vanilla courier when a loan falls overdue.
            // Papyrus indexes this list as hold * DunningDays + (overdue day - 1), so the
            // order here is part of the save format, the same way the hold order is.
            var letters = new List<Book>();
            for (uint i = 0; i < holds.Length; i++)
            {
                var hold = holds[i];
                for (int day = 1; day <= DunningDays; day++)
                {
                uint slot = i * (uint)DunningDays + (uint)(day - 1);
                var book = new Book(Id(IdLetterBase + slot), SkyrimRelease.SkyrimSE)
                {
                    EditorID = "BankPrismDunningLetter" + hold.name + "Day" + day,
                    Name = hold.korean + " 채무 독촉장",
                    Weight = 0f,
                    Value = 0,
                    // A book with no model crashes the game the moment BookMenu tries to
                    // draw it. These are the fields a vanilla note carries, copied from
                    // WERoad08CourierLetter (0x1065F5).
                    Type = Book.BookType.BookOrTome,
                    Model = new Model { File = "Clutter\\Books\\Note01.nif" },
                    // No leading [pagebreak]: it opened the note on a blank first page.
                    // One in the middle, because a note this long is cut off without it.
                    BookText = DunningLetter(hold.korean, day)
                };
                book.InventoryArt.SetTo(Vanilla(0x097788));  // the note's inventory static
                book.PickUpSound.SetTo(Vanilla(0x0C7A54));   // ITMBookUp
                mod.Books.Add(book);
                letters.Add(book);
                }
            }

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
            // Match what every vanilla dialogue quest carries. DialogueWhiterun,
            // DialogueGeneric and DialogueFavorGeneric are all flags 0x011, type None -
            // 0x011 being StartGameEnabled plus bit 0x010, which neither Mutagen nor
            // xEdit names but which no vanilla dialogue quest is without. Ours was 0x001
            // and type Misc, the only quest offering player topics shaped that way.
            bankQuest.Flags |= Quest.Flag.StartGameEnabled;
            bankQuest.Flags |= (Quest.Flag)0x010;
            bankQuest.Priority = 50;
            bankQuest.Type = Quest.TypeEnum.None;

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

            ScriptObjectListProperty BookListProp(string name, IEnumerable<Book> items)
            {
                var list = new ScriptObjectListProperty { Name = name };
                foreach (var b in items)
                    list.Objects.Add(new ScriptObjectProperty { Object = b.ToLink<ISkyrimMajorRecordGetter>() });
                return list;
            }

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
            ListProp("LoanDue", loanDue);
            ListProp("LoanPrincipal", loanPrincipal);
            ListProp("CleanRepayments", cleanRepayments);
            ListProp("AccruedDays", accruedDays);
            controller.Properties.Add(BookListProp("DunningLetters", letters));
            ListProp("LoanLimits", loanLimits);
            ListProp("CreditLimits", creditLimits);
            ListProp("CreditTiers", creditTiers);
            ListProp("CreditPaths", creditPaths);
            // A scalar bound at the same moment as the four standing arrays, so Papyrus
            // can test it instead of the arrays: an unbound array property throws when
            // read and cannot be guarded with a None test.
            ObjProp("CreditTierFlag", creditLimits[0].FormKey);
            ObjProp("HoldCrimeFactions", holdList.FormKey);
            ObjProp("CreditSurcharge", surcharge.FormKey);
            ObjProp("DebugHotkey", debugHotkey.FormKey);
            ObjProp("LoanTermDays", loanTermDays.FormKey);
            ObjProp("OverduePercent", overduePct.FormKey);
            ListProp("PropertyPledges", propertyPledges);
            // Bound in the same generation as PropertyPledges, so Papyrus tests this scalar
            // as the array's readiness flag instead of touching an unbound array.
            ObjProp("CollateralLtv", collateralLtv.FormKey);
            ObjProp("ForecloseDays", forecloseDays.FormKey);
            ObjProp("LienReleaseFeePercent", lienFeePct.FormKey);

            // ---- Standing: the records each tier is actually read from -----------------
            // Every id below was read out of Skyrim.esm with the probes in this file, not
            // recalled. The two that surprised us are worth keeping in view:
            //
            //  - There is no Thane faction, and the per-hold Favor25x quests stop once the
            //    Jarl names you, which drops their stage data. Vanilla's own durable record
            //    is FavorJarlsMakeFriends, whose script keeps <Hold>ImpGetOutofJail /
            //    <Hold>SonsGetOutofJail per hold - 0 until you are Thane, then 1. It keys
            //    off the Jarl's crime faction, the same handle this mod uses for holds.
            //  - Civil War rank is not a faction rank; CWImperialFaction and CWSonsFaction
            //    have no rank table. The global CWCountMissionsDone looks right but is
            //    marked DEPRECATED/OBSOLETE in vanilla CWScript. The live value is
            //    CWScript.PlayerRank on the CW quest, 1..4.
            ObjProp("ThaneTracker", Vanilla(0x087E24));      // FavorJarlsMakeFriends
            ObjProp("MQDragonRising", Vanilla(0x02610C));    // MQ104 Dragon Rising
            ObjProp("MQAlduinsBane", Vanilla(0x036193));     // MQ206 Alduin's Bane
            ObjProp("MQDragonslayer", Vanilla(0x046EF3));    // MQ306, Alduin in Sovngarde
            ObjProp("CompanionsJoin", Vanilla(0x04B2D9));    // C00 Take Up Arms
            ObjProp("CompanionsCircleQuest", Vanilla(0x01CEF4));  // C03, stage 25 = the Circle
            ObjProp("CompanionsHarbinger", Vanilla(0x1070DD));    // CompanionsHarbingerFaction
            ObjProp("CollegeFaction", Vanilla(0x01F259));    // ranks 0..6, Wizard 4, Arch-Mage 6
            ObjProp("CivilWar", Vanilla(0x019E53));          // CW, carries CWScript
            ObjProp("CWImperial", Vanilla(0x02BF9A));
            ObjProp("CWSons", Vanilla(0x02BF9B));
            ObjProp("Courier", Vanilla(0x039F82));           // WICourier
            // HousePurchase carries HousePurchaseScript (WhiterunHouseVar, HPWhiterun =
            // 0F728B, PlayerFaction) and the stage-10 fragment script whose WhiterunHouse
            // property is Breezehome's interior cell, 0165A8. Read with `qfrag 0A7B33`.
            ObjProp("HousePurchase", Vanilla(0x0A7B33));
            // Breezehome's front door in Whiterun: persistent, teleports to 000166A9 inside
            // WhiterunBreezehome. Read with `dotnet run -- doors WhiterunBreezehome`.
            ObjProp("BreezehomeFrontDoor", Vanilla(0x01A6F9));

            ObjProp("Gold001", Gold001);

            bankQuest.VirtualMachineAdapter = new QuestAdapter();
            bankQuest.VirtualMachineAdapter.Scripts.Add(controller);

            // ---- 3. Dialogue ---------------------------------------------------------
            // Every vanilla player topic belongs to a top-level DialogBranch. A topic
            // without one is never offered in the dialogue menu at all.
            void AddTopic(string id, uint topicId, uint branchId, uint infoId,
                          string prompt, string response, FormKey[] required, FormKey[] anyOf, string fragment)
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
                // that replace the record.
                //
                // Every member of an OR group carries the OR flag, the last one included.
                // Vanilla INFOs look like this, e.g. 000E3D: one plain condition followed
                // by four that all carry OR. Clearing the flag on the final clause instead
                // makes the engine read it as "(any of the rest) AND (that one)", which no
                // actor can satisfy, and the topic silently reaches nobody.
                void AddInFaction(FormKey faction, bool or)
                {
                    var inFaction = new GetInFactionConditionData();
                    inFaction.Faction.Link.SetTo(faction);
                    info.Conditions.Add(new ConditionFloat
                    {
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 1f,
                        Data = inFaction,
                        Flags = or ? Condition.Flag.OR : default
                    });
                }

                // Required clauses first, each ANDed, then the alternatives as a single
                // OR group - the same shape as vanilla 000E3D.
                foreach (var faction in required) AddInFaction(faction, false);
                var orGroup = anyOf.Length > 1;
                foreach (var faction in anyOf) AddInFaction(faction, orGroup);

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

            // JobStewardFaction covers every hold steward, wartime replacements included;
            // membership of an enabled hold's crime faction narrows that to the holds the
            // mod serves. `dotnet run -- members JobStewardFaction,CrimeFactionWhiterun`
            // says who that is: Proventus Avenicci and nobody else.
            AddTopic("BankPrismBank", IdBankTopic, IdBankBranch, IdBankInfo,
                     "은행 업무를 보고 싶습니다.",
                     "물론입니다. 어떤 업무를 도와드릴까요?",
                     new[] { Vanilla(0x050922) }, enabledCrimeFactions, "BankPrismDialogueFragment");

            // Standalone general stores with their own premises. Blacksmiths, alchemists,
            // innkeepers, market stalls and the Khajiit caravans are deliberately absent:
            // Eorlund extending credit to the Companions' Harbinger reads wrong. Faction
            // ids were read out of Skyrim.esm rather than taken on trust - of the nine NPC
            // ids supplied with this list, eight were wrong.
            var generalStores = new (string hold, FormKey faction)[]
            {
                ("Whiterun",   Vanilla(0x09CAF5)), // ServicesWhiterunBelethorsGoods    - Belethor
                ("Whiterun",   Vanilla(0x05A665)), // ServicesRiverwoodRiverwoodTrader  - Lucan Valerius
                ("Haafingar",  Vanilla(0x0A6C02)), // ServicesSolitudeBitsAndPieces     - Sayma
                ("Rift",       Vanilla(0x0A31C5)), // ServicesRiftenPawnedPrawn         - Bersi Honey-Hand
                ("Reach",      Vanilla(0x094375)), // ServicesMarkarthArnleifandSons    - Lisbet
                ("Eastmarch",  Vanilla(0x0A3F12)), // ServicesWindhelmRevynSadri        - Revyn Sadri
                ("Winterhold", Vanilla(0x09DA62)), // ServicesWinterholdBirna           - Birna
                ("Falkreath",  Vanilla(0x0A6BFE)), // ServicesFalkreathGrayPineGoods    - Solaf
                ("Hjaalmarch", Vanilla(0x09DA5B)), // ServicesMorthalLami               - Lami
            };

            // Hold tags as read by `dotnet run -- credit`, which prints each store's
            // staff with the crime faction the engine assigns them.
            var enabledStores = generalStores.Where(st => enabledHolds.Contains(st.hold))
                                             .Select(st => st.faction).ToArray();

            AddTopic("BankPrismCredit", IdCreditTopic, IdCreditBranch, IdCreditInfo,
                     "외상으로 거래하고 싶습니다.",
                     "장부에 달아 두지요. 갚는 것만 잊지 마시오.",
                     Array.Empty<FormKey>(), enabledStores, "BankPrismCreditFragment");


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
            foreach (var b in check.Books)
                Console.WriteLine($"  BOOK {b.FormKey.ID:X6} {b.EditorID} model={b.Model?.File?.ToString() ?? "(NONE - BookMenu will crash)"} art={b.InventoryArt.FormKeyNullable} type={b.Type}");
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

            AssertDialogueCanWork(check);
            AssertOnlyEnabledHoldsReached(check, enabledHolds,
                holds.Select(h => (h.name, Vanilla(h.crimeFaction))).ToArray(),
                enabledHoldIndexes, Path.GetDirectoryName(outputPath)!);
            AssertFontCanDrawIt(outputPath);
        }

        // The other eight holds are hidden by dialogue condition, and a condition that
        // reaches the wrong people fails as quietly as one that reaches nobody. So the
        // build works out from Skyrim.esm exactly which NPCs each topic is offered to,
        // prints them, and refuses the build if any belongs to a hold that is switched
        // off, or if a topic reaches no one. It also reads HoldIsEnabled() out of the
        // Papyrus source, because the two lists live in different languages and nothing
        // else would notice them drift apart.
        static void AssertOnlyEnabledHoldsReached(ISkyrimModGetter mod, HashSet<string> enabledHolds,
            (string name, FormKey crime)[] holds, int[] enabledIndexes, string repoRoot)
        {
            var problems = new List<string>();
            using var esm = SkyrimMod.CreateFromBinaryOverlay(
                @"C:/TAKEALOOK/Stock Game/Data/Skyrim.esm", SkyrimRelease.SkyrimSE);
            var crimeToHold = holds.ToDictionary(h => h.crime, h => h.name);

            Console.WriteLine();
            Console.WriteLine("--- 대화문 도달 범위 (Skyrim.esm 기준) ---");
            foreach (var topic in mod.DialogTopics)
            foreach (var info in topic.Responses)
            {
                // Plain clauses are each ANDed and OR-flagged clauses form one group,
                // which is how the engine read the OR-flag bug recorded in TASK.md.
                var plain = new List<FormKey>();
                var anyOf = new List<FormKey>();
                foreach (var c in info.Conditions)
                {
                    if (c.Data is not IGetInFactionConditionDataGetter g ||
                        g.Faction.Link.FormKeyNullable is not FormKey fk)
                    {
                        problems.Add($"{info.EditorID}: GetInFaction 이외의 조건이 있어 도달 범위를 계산할 수 없다");
                        continue;
                    }
                    if (c.Flags.HasFlag(Condition.Flag.OR)) anyOf.Add(fk); else plain.Add(fk);
                }

                var reached = new List<string>();
                foreach (var npc in esm.Npcs)
                {
                    var mine = npc.Factions.Select(fr => fr.Faction.FormKeyNullable).OfType<FormKey>().ToHashSet();
                    if (!plain.All(mine.Contains)) continue;
                    if (anyOf.Count > 0 && !anyOf.Any(mine.Contains)) continue;
                    var crime = npc.CrimeFaction.FormKeyNullable;
                    string hold = crime is FormKey ck && crimeToHold.TryGetValue(ck, out var hn) ? hn : "(홀드 없음)";
                    reached.Add($"{npc.EditorID}={hold}");
                    if (!enabledHolds.Contains(hold))
                        problems.Add($"{info.EditorID}: 꺼진 홀드의 NPC에게 뜬다 - {npc.EditorID} ({hold})");
                }
                Console.WriteLine($"  {info.EditorID}: {reached.Count}명 [{string.Join(", ", reached)}]");
                if (reached.Count == 0)
                    problems.Add($"{info.EditorID}: 아무에게도 뜨지 않는다");
            }

            var psc = Path.Combine(repoRoot, "Scripts", "Source", "BankPrismController.psc");
            var src = File.ReadAllText(psc);
            int start = src.IndexOf("Bool Function HoldIsEnabled(", StringComparison.Ordinal);
            int end = start < 0 ? -1 : src.IndexOf("EndFunction", start, StringComparison.Ordinal);
            if (start < 0 || end < 0)
            {
                problems.Add("BankPrismController.psc에 HoldIsEnabled()가 없다");
            }
            else
            {
                var body = src.Substring(start, end - start);
                var gated = System.Text.RegularExpressions.Regex.Matches(body, @"aiHold\s*==\s*(\d+)")
                    .Select(m => int.Parse(m.Groups[1].Value)).OrderBy(i => i).ToArray();
                var wanted = enabledIndexes.OrderBy(i => i).ToArray();
                Console.WriteLine($"  HoldIsEnabled(): [{string.Join(", ", gated)}]   enabledHolds: [{string.Join(", ", wanted)}]");
                if (!gated.SequenceEqual(wanted))
                    problems.Add($"Papyrus HoldIsEnabled() [{string.Join(", ", gated)}] 와 생성기 enabledHolds [{string.Join(", ", wanted)}] 가 다르다");
            }

            Console.WriteLine();
            if (problems.Count == 0)
            {
                Console.WriteLine("--- 홀드 제한 검사: 통과 ---");
                return;
            }
            Console.WriteLine("--- 홀드 제한 검사: 실패 ---");
            foreach (var pr in problems) Console.WriteLine("  " + pr);
            Environment.ExitCode = 1;
        }

        // Everything here is a condition under which the game shows no topic at all,
        // reports no error, and writes nothing to any log - which is why each one cost
        // a session to find. Reading them back off the written file and refusing to
        // call the build good is the only place they can be caught for free.
        static void AssertDialogueCanWork(ISkyrimModGetter mod)
        {
            var problems = new List<string>();

            var quest = mod.Quests.FirstOrDefault(q => q.EditorID == "BankPrismQuest");
            if (quest == null)
            {
                problems.Add("BankPrismQuest 자체가 없다");
            }
            else
            {
                // Vanilla dialogue quests are all flags 0x011, type None. Ours was
                // 0x001/Misc once: the quest ran, the script attached, nothing errored,
                // and no NPC ever offered a line.
                if (((int)quest.Flags & 0x011) != 0x011)
                    problems.Add($"퀘스트 플래그가 0x{(int)quest.Flags:X3} - 0x011 비트가 빠졌다 (대화문이 아무에게도 안 뜬다)");
                if (quest.Type != Quest.TypeEnum.None)
                    problems.Add($"퀘스트 종류가 {quest.Type} - None이어야 한다");
                if ((quest.VirtualMachineAdapter?.Scripts.Count ?? 0) == 0)
                    problems.Add("퀘스트에 컨트롤러 스크립트가 붙어 있지 않다");
            }

            foreach (var topic in mod.DialogTopics)
            {
                string id = topic.EditorID ?? topic.FormKey.ID.ToString("X6");

                // A player topic outside a top-level branch is never even a candidate
                // in the dialogue menu.
                var branchKey = topic.Branch.FormKeyNullable;
                if (branchKey == null)
                {
                    problems.Add($"{id}: 브랜치가 없다 (대화 메뉴에 후보로 오르지 않는다)");
                }
                else
                {
                    var branch = mod.DialogBranches.FirstOrDefault(b => b.FormKey == branchKey);
                    if (branch == null)
                        problems.Add($"{id}: 브랜치 {branchKey} 를 찾을 수 없다");
                    else if (!branch.Flags.HasValue || !branch.Flags.Value.HasFlag(DialogBranch.Flag.TopLevel))
                        problems.Add($"{id}: 브랜치가 TopLevel이 아니다");
                    else if (branch.StartingTopic.FormKeyNullable != topic.FormKey)
                        problems.Add($"{id}: 브랜치의 시작 토픽이 이 토픽이 아니다");
                }

                if (topic.Quest.FormKeyNullable != quest?.FormKey)
                    problems.Add($"{id}: 토픽이 컨트롤러 퀘스트에 매여 있지 않다");

                foreach (var info in topic.Responses)
                {
                    string iid = info.EditorID ?? info.FormKey.ID.ToString("X6");
                    if (info.Conditions.Count == 0)
                        problems.Add($"{iid}: 조건이 하나도 없다");

                    // An OR group whose last clause has no OR flag is read as
                    // "(one of the rest) AND (that one)", which nobody satisfies.
                    // Vanilla INFO 000E3D flags every clause, the last one included.
                    bool anyOr = info.Conditions.Any(c => c.Flags.HasFlag(Condition.Flag.OR));
                    if (anyOr && !info.Conditions[info.Conditions.Count - 1].Flags.HasFlag(Condition.Flag.OR))
                        problems.Add($"{iid}: OR 그룹의 마지막 조건에 OR 플래그가 없다");

                    if (info.Responses.Count == 0)
                        problems.Add($"{iid}: 응답이 없다");

                    var frag = info.VirtualMachineAdapter?.ScriptFragments;
                    if (frag?.OnBegin == null)
                        problems.Add($"{iid}: onBegin 프래그먼트가 붙어 있지 않다");
                }
            }

            Console.WriteLine();
            if (problems.Count == 0)
            {
                Console.WriteLine("--- 대화문 불변조건 검사: 통과 ---");
                return;
            }

            Console.WriteLine("--- 대화문 불변조건 검사: 실패 ---");
            foreach (var p in problems) Console.WriteLine("  " + p);
            Console.WriteLine();
            Console.WriteLine("이 상태로 배포하면 대화문이 조용히 사라진다. 배포하지 말 것.");
            Environment.ExitCode = 1;
        }

        // Text the game draws itself - letters, notifications, dialogue prompts - goes
        // through the Korean font this setup ships, which is a 2,350-syllable cut. A
        // character outside it is drawn as a question mark, with no error anywhere: the
        // Hjaalmarch letter arrived as "?마치" for days before anyone worked out why.
        // Reading the font's own cmap and checking every string we write is the only
        // place that can be caught without launching the game.
        static void AssertFontCanDrawIt(string espPath)
        {
            const string fontPath =
                @"C:\TAKEALOOK\mods\TAKEALOOK - Font Edit\backup\DNF - Optimised.ttf";
            if (!File.Exists(fontPath))
            {
                Console.WriteLine();
                Console.WriteLine("--- 폰트 검사: 건너뜀 (폰트를 찾지 못함) ---");
                return;
            }

            var glyphs = ReadCmap(fontPath);
            if (glyphs.Count == 0)
            {
                Console.WriteLine("--- 폰트 검사: 건너뜀 (cmap을 읽지 못함) ---");
                return;
            }

            // Only the text subrecords: decoding the whole file turns record bytes into
            // stray characters that look like real misses.
            var missing = new SortedDictionary<int, string>();
            foreach (var (edid, field, text) in ReadStrings(espPath))
            {
                foreach (var ch in text)
                {
                    if (ch < 0x80) continue;
                    if (glyphs.Contains(ch)) continue;
                    if (!missing.ContainsKey(ch)) missing[ch] = $"{edid} / {field}";
                }
            }

            // Papyrus notifications and refusals are drawn by the same font, and nothing
            // looked at them until now. String literals only; comments are skipped.
            var repoRoot = Path.GetDirectoryName(Path.GetFullPath(espPath))!;
            foreach (var psc in Directory.GetFiles(Path.Combine(repoRoot, "Scripts", "Source"), "*.psc"))
            {
                int lineNo = 0;
                foreach (var line in File.ReadLines(psc, Encoding.UTF8))
                {
                    lineNo++;
                    bool inQuote = false;
                    for (int k = 0; k < line.Length; k++)
                    {
                        char ch = line[k];
                        if (inQuote && ch == '\\') { k++; continue; }
                        if (ch == '"') { inQuote = !inQuote; continue; }
                        if (!inQuote && ch == ';') break;
                        if (!inQuote || ch < 0x80 || glyphs.Contains(ch)) continue;
                        if (!missing.ContainsKey(ch)) missing[ch] = $"{Path.GetFileName(psc)}:{lineNo}";
                    }
                }
            }

            Console.WriteLine();
            if (missing.Count == 0)
            {
                Console.WriteLine("--- 폰트 검사: 통과 (게임이 그릴 수 없는 글자 없음) ---");
                return;
            }

            Console.WriteLine("--- 폰트 검사: 실패 ---");
            foreach (var kv in missing)
                Console.WriteLine($"  U+{kv.Key:X4} 를 게임 폰트가 그리지 못한다  (처음 나온 곳: {kv.Value})");
            Console.WriteLine();
            Console.WriteLine("게임에서 물음표로 나온다. 흔한 글자로 바꿀 것.");
            Environment.ExitCode = 1;
        }

        // EDID / field name / text, for every string subrecord in the plugin.
        static IEnumerable<(string edid, string field, string text)> ReadStrings(string path)
        {
            var data = File.ReadAllBytes(path);
            int off = 0;
            string edid = "";
            while (off < data.Length - 24)
            {
                var type = Encoding.ASCII.GetString(data, off, 4);
                int size = BitConverter.ToInt32(data, off + 4);
                if (type == "GRUP") { off += 24; continue; }

                int p = off + 24, end = off + 24 + size;
                while (p < end - 6)
                {
                    var sub = Encoding.ASCII.GetString(data, p, 4);
                    int len = BitConverter.ToUInt16(data, p + 4);
                    if (sub == "EDID" || sub == "FULL" || sub == "DESC" || sub == "CNAM")
                    {
                        var raw = new byte[len];
                        Array.Copy(data, p + 6, raw, 0, len);
                        var txt = Encoding.UTF8.GetString(raw).TrimEnd(' ');
                        if (sub == "EDID") edid = txt;
                        else yield return (edid, sub, txt);
                    }
                    p += 6 + len;
                }
                off = end;
            }
        }

        // Format 4 cmap, which is what these fonts use.
        static HashSet<int> ReadCmap(string path)
        {
            var chars = new HashSet<int>();
            var d = File.ReadAllBytes(path);
            int Be16(int o) => (d[o] << 8) | d[o + 1];
            int Be32(int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

            int numTables = Be16(4), cmap = -1;
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + 16 * i;
                if (Encoding.ASCII.GetString(d, rec, 4) == "cmap") cmap = Be32(rec + 8);
            }
            if (cmap < 0) return chars;

            int n = Be16(cmap + 2), best = -1;
            for (int i = 0; i < n; i++)
            {
                int pid = Be16(cmap + 4 + 8 * i), eid = Be16(cmap + 6 + 8 * i);
                if ((pid == 3 && (eid == 1 || eid == 10)) || (pid == 0)) best = cmap + Be32(cmap + 8 + 8 * i);
            }
            if (best < 0 || Be16(best) != 4) return chars;

            int segX2 = Be16(best + 6), seg = segX2 / 2;
            int endo = best + 14, starto = endo + segX2 + 2, deltao = starto + segX2, rangeo = deltao + segX2;
            for (int s = 0; s < seg; s++)
            {
                int e = Be16(endo + 2 * s), st = Be16(starto + 2 * s);
                short delta = (short)Be16(deltao + 2 * s);
                int ro = Be16(rangeo + 2 * s);
                if (st == 0xFFFF) continue;
                for (int c = st; c <= Math.Min(e, 0xFFFE); c++)
                {
                    int g;
                    if (ro == 0) g = (c + delta) & 0xFFFF;
                    else
                    {
                        int gi = rangeo + 2 * s + ro + 2 * (c - st);
                        if (gi + 1 >= d.Length) continue;
                        g = Be16(gi);
                        if (g != 0) g = (g + delta) & 0xFFFF;
                    }
                    if (g != 0) chars.Add(c);
                }
            }
            return chars;
        }
    }
}
