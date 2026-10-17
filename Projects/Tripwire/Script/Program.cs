// Tripwire programmable block script.
// Only the part between "#region Script" and "#endregion // Script" is the in-game script:
// Deploy-Script.ps1 copies it to the game's local script folder. The class around it is only here
// so the script can be compile-checked against the game's assemblies (Script.csproj).

using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace TripwireScript
{
    partial class Program : MyGridProgram
    {
        #region Script
        // Tripwire 1.0.0
        // Logs who visits your grid and what happens to its blocks while you're away.
        // Settings: the [Tripwire] section of this block's Custom Data.
        // Log: below the "--- Tripwire log ---" line here, and on every block with the tag in its name.
        // Arguments: "seen" (check sensors now; put it on a sensor's action), "write" (save copies now), "clear".
        // Log format: see LOG FORMAT.md in the Tripwire project on GitHub.

        const string Version = "1.0.0";
        const string Section = "Tripwire";
        const string Marker = "--- Tripwire log ---";
        const string Prefix = "TW1|";
        const int MaxLogChars = 60000;
        const double VisitorTimeout = 30;
        const double BlockCooldown = 60;
        const double FlushSeconds = 10;
        const int ManyThreshold = 10;
        const int RefreshRuns = 600;

        // Settings
        string tag = "[Tripwire]";
        bool logEnemy = true, logNeutral = true, logFaction = false, logOwner = false;
        bool detectGrids = true, useTurrets = true, watchArmor = true;
        float sensorRange = -1;
        HashSet<string> ignoreNames = new HashSet<string>();
        double minDamage = 2;
        int logLength = 200, writeInterval = 10, maxCopies = 4, budget = 10000, screenLines = 30;
        double utcOffset = 0;
        string alarmTimer = "";
        string settingsText;
        string settingsError;

        // Blocks
        readonly List<IMySensorBlock> sensors = new List<IMySensorBlock>();
        readonly List<IMyLargeTurretBase> turrets = new List<IMyLargeTurretBase>();
        readonly List<IMyTerminalBlock> copies = new List<IMyTerminalBlock>();
        readonly List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
        IMyTimerBlock alarm;
        readonly List<IMyTerminalBlock> scratch = new List<IMyTerminalBlock>();
        readonly List<IMySensorBlock> newSensors = new List<IMySensorBlock>();
        readonly List<IMyLargeTurretBase> newTurrets = new List<IMyLargeTurretBase>();
        readonly List<IMyTerminalBlock> newCopies = new List<IMyTerminalBlock>();
        readonly List<IMyCubeGrid> newGrids = new List<IMyCubeGrid>();
        IMyTimerBlock newAlarm;
        int refreshCursor = -1, lastRefresh = -RefreshRuns, generation;

        // Watched blocks
        class Tracked
        {
            public IMyCubeGrid Grid;
            public Vector3I Pos;
            public IMyTerminalBlock Term;
            public string Name;
            public float Build, Integ;
            public long Owner;
            public DateTime LastEvent;
            public int Seen;
        }
        readonly List<Tracked> terminals = new List<Tracked>();
        readonly Dictionary<long, Tracked> terminalById = new Dictionary<long, Tracked>();
        readonly List<Tracked> armor = new List<Tracked>();
        readonly Dictionary<IMyCubeGrid, Dictionary<Vector3I, Tracked>> armorByGrid = new Dictionary<IMyCubeGrid, Dictionary<Vector3I, Tracked>>();
        int termCursor, armorCursor;
        int discGrid = -1;
        Vector3I discPos, discMin, discMax;
        bool armorReady;
        int termPasses, armorPasses;
        DateTime termPassStart, armorPassStart;
        double termPassSeconds, armorPassSeconds;

        // Visitors
        class Visitor
        {
            public int Id;
            public string Relation, Type, Name;
            public DateTime First, Last;
        }
        readonly Dictionary<long, Visitor> visitors = new Dictionary<long, Visitor>();
        readonly List<MyDetectedEntityInfo> detected = new List<MyDetectedEntityInfo>();
        readonly List<long> gone = new List<long>();
        readonly HashSet<long> ownGridIds = new HashSet<long>();
        int visitorCounter;

        // Pending block events, flushed every few seconds so a big attack becomes one summary line
        struct Pending
        {
            public string Kind, Grid, Block, Pos, Value;
            public float Ratio;
        }
        readonly List<Pending> pending = new List<Pending>();
        DateTime lastFlush;

        // Log
        readonly List<string> lines = new List<string>();
        int logChars;
        bool dirty, urgent;
        DateTime lastWrite = DateTime.MinValue;
        readonly StringBuilder sb = new StringBuilder();

        // Status
        int runs;
        DateTime now;
        double avgInstr, avgMs;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            now = DateTime.UtcNow;
            lastFlush = now;
            termPassStart = armorPassStart = now;
            ReadSettings(true);
            LoadLog();
            Add("START", "loaded " + Version);
            urgent = true;
        }

        public void Save()
        {
            sb.Clear();
            sb.Append("TWSTATE|").Append(visitorCounter).Append('\n');
            foreach (var l in lines) sb.Append(l).Append('\n');
            Storage = sb.ToString();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            now = DateTime.UtcNow;
            runs++;
            var arg = (argument ?? "").Trim().ToLowerInvariant();
            if (arg == "clear")
            {
                lines.Clear();
                logChars = 0;
                Add("START", "cleared");
                urgent = true;
            }
            else if (arg == "write") urgent = true;

            ReadSettings(false);
            if (refreshCursor >= 0 || runs - lastRefresh >= RefreshRuns) Refresh();
            ScanDetectors();
            ExpireVisitors();
            CheckTerminals();
            if (watchArmor)
            {
                CheckArmor();
                Discover();
            }
            Flush();
            if (dirty && (urgent || (now - lastWrite).TotalSeconds >= writeInterval)) WriteCopies();
            Status();
        }

        // ---------------------------------------------------------------- settings

        void ReadSettings(bool force)
        {
            var head = Head(Me.CustomData).Trim();
            if (!force && head == settingsText) return;
            settingsText = head;
            settingsError = null;
            var ini = new MyIni();
            MyIniParseResult result;
            if (!ini.TryParse(head, out result))
            {
                settingsError = "Custom Data line " + result.LineNo + ": " + result.Error;
                return;
            }
            var missing = !ini.ContainsSection(Section);
            tag = Get(ini, "Tag", "[Tripwire]");
            var rel = Get(ini, "LogRelations", "Enemies,Neutral").ToLowerInvariant();
            logEnemy = rel.Contains("enem");
            logNeutral = rel.Contains("neut");
            logFaction = rel.Contains("fact") || rel.Contains("friend");
            logOwner = rel.Contains("owner");
            detectGrids = Get(ini, "DetectGrids", "true").ToLowerInvariant() != "false";
            useTurrets = Get(ini, "UseTurrets", "true").ToLowerInvariant() != "false";
            watchArmor = Get(ini, "WatchArmor", "true").ToLowerInvariant() != "false";
            sensorRange = (float)Num(Get(ini, "SensorRange", ""), -1, -1, 50);
            ignoreNames.Clear();
            foreach (var n in Get(ini, "IgnoreNames", "").Split(','))
                if (n.Trim().Length > 0) ignoreNames.Add(n.Trim());
            minDamage = Num(Get(ini, "MinDamagePercent", "2"), 2, 0.1, 100);
            logLength = (int)Num(Get(ini, "LogLength", "200"), 200, 10, 1000);
            writeInterval = (int)Num(Get(ini, "WriteIntervalSeconds", "10"), 10, 2, 600);
            maxCopies = (int)Num(Get(ini, "MaxCopies", "4"), 4, 0, 20);
            budget = (int)Num(Get(ini, "InstructionBudget", "10000"), 10000, 2000, 40000);
            screenLines = (int)Num(Get(ini, "ScreenLines", "30"), 30, 5, 200);
            utcOffset = Num(Get(ini, "ScreenUtcOffset", "0"), 0, -14, 14);
            alarmTimer = Get(ini, "AlarmTimer", "");
            if (missing)
            {
                // First run: write the defaults so the player can see and change them.
                var data = Me.CustomData;
                var at = data.IndexOf(Marker);
                var rest = at >= 0 ? data.Substring(at) : "";
                var newHead = ini.ToString().TrimEnd();
                Me.CustomData = newHead + "\n\n" + rest;
                settingsText = Head(Me.CustomData).Trim();
            }
            lastRefresh = -RefreshRuns; // blocks may match a new tag or timer
            ApplySensorRange();
        }

        string Get(MyIni ini, string key, string def)
        {
            var v = ini.Get(Section, key);
            if (v.IsEmpty)
            {
                ini.Set(Section, key, def);
                return def;
            }
            return v.ToString(def);
        }

        static double Num(string s, double def, double min, double max)
        {
            double d;
            if (!double.TryParse(s, out d)) return def;
            return Math.Max(min, Math.Min(max, d));
        }

        static string Head(string data)
        {
            var at = data.IndexOf(Marker);
            return at >= 0 ? data.Substring(0, at) : data;
        }

        void ApplySensorRange()
        {
            if (sensorRange <= 0) return;
            foreach (var s in sensors)
            {
                if (s.Closed) continue;
                var r = Math.Min(sensorRange, s.MaxRange);
                // Only set what differs: every change is sent to the players nearby.
                if (s.LeftExtend != r) s.LeftExtend = r;
                if (s.RightExtend != r) s.RightExtend = r;
                if (s.TopExtend != r) s.TopExtend = r;
                if (s.BottomExtend != r) s.BottomExtend = r;
                if (s.FrontExtend != r) s.FrontExtend = r;
                if (s.BackExtend != r) s.BackExtend = r;
            }
        }

        // ---------------------------------------------------------------- block lists (spread over runs)

        void Refresh()
        {
            if (refreshCursor < 0)
            {
                scratch.Clear();
                GridTerminalSystem.GetBlocks(scratch);
                newSensors.Clear();
                newTurrets.Clear();
                newCopies.Clear();
                newGrids.Clear();
                newGrids.Add(Me.CubeGrid);
                newAlarm = null;
                generation++;
                refreshCursor = 0;
            }
            var limit = budget * 3 / 10;
            while (refreshCursor < scratch.Count && Runtime.CurrentInstructionCount < limit)
            {
                var b = scratch[refreshCursor++];
                if (b.Closed || !b.IsSameConstructAs(Me)) continue;
                if (!newGrids.Contains(b.CubeGrid)) newGrids.Add(b.CubeGrid);
                var sensor = b as IMySensorBlock;
                if (sensor != null) newSensors.Add(sensor);
                var turret = b as IMyLargeTurretBase;
                if (turret != null) newTurrets.Add(turret);
                if (b != Me && tag.Length > 0 && b.CustomName.Contains(tag) && newCopies.Count < maxCopies) newCopies.Add(b);
                if (alarmTimer.Length > 0 && newAlarm == null && b.CustomName == alarmTimer) newAlarm = b as IMyTimerBlock;
                Tracked t;
                if (terminalById.TryGetValue(b.EntityId, out t)) t.Seen = generation;
                else
                {
                    var slim = b.CubeGrid.GetCubeBlock(b.Position);
                    if (slim == null) continue;
                    t = new Tracked { Grid = b.CubeGrid, Pos = b.Position, Term = b, Name = b.CustomName, Owner = b.OwnerId, Seen = generation };
                    t.Build = slim.BuildIntegrity;
                    t.Integ = slim.BuildIntegrity - slim.CurrentDamage;
                    terminals.Add(t);
                    terminalById[b.EntityId] = t;
                }
            }
            if (refreshCursor < scratch.Count) return;

            // Done: swap in the new lists and drop blocks that left the construct (not destroyed: that's caught by the checks).
            sensors.Clear(); sensors.AddRange(newSensors);
            turrets.Clear(); turrets.AddRange(newTurrets);
            copies.Clear(); copies.AddRange(newCopies);
            grids.Clear(); grids.AddRange(newGrids);
            ownGridIds.Clear();
            foreach (var g in grids) ownGridIds.Add(g.EntityId);
            alarm = newAlarm;
            for (int i = terminals.Count - 1; i >= 0; i--)
            {
                var t = terminals[i];
                if (t.Seen != generation && !t.Term.Closed)
                {
                    terminalById.Remove(t.Term.EntityId);
                    terminals[i] = terminals[terminals.Count - 1];
                    terminals.RemoveAt(terminals.Count - 1);
                }
            }
            scratch.Clear();
            refreshCursor = -1;
            lastRefresh = runs;
            ApplySensorRange();
        }

        // ---------------------------------------------------------------- visitors

        void ScanDetectors()
        {
            foreach (var s in sensors)
            {
                if (s.Closed || !s.IsWorking) continue;
                detected.Clear();
                s.DetectedEntities(detected);
                foreach (var e in detected) Consider(e, s.CustomName);
            }
            if (!useTurrets) return;
            foreach (var t in turrets)
            {
                if (t.Closed || !t.IsWorking || !t.HasTarget) continue;
                Consider(t.GetTargetedEntity(), t.CustomName);
            }
        }

        void Consider(MyDetectedEntityInfo e, string source)
        {
            if (e.IsEmpty()) return;
            string type;
            switch (e.Type)
            {
                case MyDetectedEntityType.CharacterHuman: type = "Human"; break;
                case MyDetectedEntityType.CharacterOther: type = "NPC"; break;
                case MyDetectedEntityType.LargeGrid: type = "LargeGrid"; break;
                case MyDetectedEntityType.SmallGrid: type = "SmallGrid"; break;
                default: return;
            }
            var isGrid = type.EndsWith("Grid");
            if (isGrid && (!detectGrids || ownGridIds.Contains(e.EntityId))) return;
            string rel;
            switch (e.Relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies: if (!logEnemy) return; rel = "Enemy"; break;
                case MyRelationsBetweenPlayerAndBlock.Owner: if (!logOwner) return; rel = "Owner"; break;
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                case MyRelationsBetweenPlayerAndBlock.Friends: if (!logFaction) return; rel = "Faction"; break;
                default: if (!logNeutral) return; rel = "Neutral"; break;
            }
            // The game only names owners, faction members and friends; everyone else is just "Human" or "Large Grid".
            var name = Clean(e.Name);
            if (ignoreNames.Contains(name)) return;
            Visitor v;
            if (visitors.TryGetValue(e.EntityId, out v))
            {
                v.Last = now;
                return;
            }
            v = new Visitor { Id = ++visitorCounter, Relation = rel, Type = type, Name = name, First = now, Last = now };
            visitors[e.EntityId] = v;
            Add("SEEN", "V" + v.Id, rel, type, name, Clean(source));
            if (rel == "Enemy" || rel == "Neutral") urgent = true; // on record before anything gets damaged
            if (rel == "Enemy" && alarm != null && !alarm.Closed && alarm.IsWorking) alarm.Trigger();
        }

        void ExpireVisitors()
        {
            gone.Clear();
            foreach (var kv in visitors)
                if ((now - kv.Value.Last).TotalSeconds > VisitorTimeout) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                var v = visitors[id];
                visitors.Remove(id);
                var minutes = Math.Max(0, (int)Math.Round((v.Last - v.First).TotalMinutes));
                Add("LEFT", "V" + v.Id, v.Relation, v.Type, v.Name, minutes.ToString());
            }
        }

        string Present()
        {
            if (visitors.Count == 0) return "";
            var s = new StringBuilder();
            foreach (var v in visitors.Values)
            {
                if (s.Length > 0) s.Append(';');
                s.Append('V').Append(v.Id).Append(':').Append(v.Name);
            }
            return s.ToString();
        }

        // ---------------------------------------------------------------- block checks

        void CheckTerminals()
        {
            var limit = budget * 45 / 100;
            int done = 0;
            while (terminals.Count > 0 && done < terminals.Count && Runtime.CurrentInstructionCount < limit)
            {
                if (termCursor >= terminals.Count)
                {
                    termCursor = 0;
                    termPasses++;
                    termPassSeconds = (now - termPassStart).TotalSeconds;
                    termPassStart = now;
                }
                var t = terminals[termCursor];
                done++;
                var b = t.Term;
                var slim = b.Closed ? null : t.Grid.GetCubeBlock(t.Pos);
                if (slim == null || slim.FatBlock != b)
                {
                    Queue("LOST", t, null, 0);
                    terminalById.Remove(b.EntityId);
                    terminals[termCursor] = terminals[terminals.Count - 1];
                    terminals.RemoveAt(terminals.Count - 1);
                    continue;
                }
                t.Name = b.CustomName;
                if (t.Owner != 0 && b.OwnerId != t.Owner)
                {
                    Queue("OWNER", t, null, 0);
                }
                t.Owner = b.OwnerId;
                CheckIntegrity(t, slim);
                termCursor++;
            }
        }

        void CheckArmor()
        {
            var limit = budget * 70 / 100;
            int done = 0;
            while (armor.Count > 0 && done < armor.Count && Runtime.CurrentInstructionCount < limit)
            {
                if (armorCursor >= armor.Count)
                {
                    armorCursor = 0;
                    armorPasses++;
                    armorPassSeconds = (now - armorPassStart).TotalSeconds;
                    armorPassStart = now;
                }
                var t = armor[armorCursor];
                done++;
                var slim = t.Grid.Closed ? null : t.Grid.GetCubeBlock(t.Pos);
                if (slim == null || slim.Position != t.Pos || slim.FatBlock is IMyTerminalBlock)
                {
                    if (!t.Grid.Closed) Queue("LOST", t, null, 0);
                    Dictionary<Vector3I, Tracked> map;
                    if (armorByGrid.TryGetValue(t.Grid, out map)) map.Remove(t.Pos);
                    armor[armorCursor] = armor[armor.Count - 1];
                    armor.RemoveAt(armor.Count - 1);
                    continue;
                }
                CheckIntegrity(t, slim);
                armorCursor++;
            }
        }

        void CheckIntegrity(Tracked t, IMySlimBlock slim)
        {
            var build = slim.BuildIntegrity;
            var integ = build - slim.CurrentDamage;
            var threshold = (float)(minDamage / 100 * slim.MaxIntegrity);
            var max = Math.Max(slim.MaxIntegrity, 1);
            // Grinding lowers the build level; damage only lowers integrity.
            if (build < t.Build - threshold)
            {
                if ((now - t.LastEvent).TotalSeconds < BlockCooldown) return;
                Queue("GRIND", t, ((int)Math.Round(build / max * 100)).ToString(), build / max);
                t.Build = build;
                t.Integ = integ;
            }
            else if (integ < t.Integ - threshold)
            {
                if ((now - t.LastEvent).TotalSeconds < BlockCooldown) return;
                Queue("DMG", t, ((int)Math.Round(integ / max * 100)).ToString(), integ / max);
                t.Build = build;
                t.Integ = integ;
            }
            else
            {
                // Welding and repairs raise the baseline; small losses add up until they pass the threshold.
                if (build > t.Build) t.Build = build;
                if (integ > t.Integ) t.Integ = integ;
            }
        }

        void Discover()
        {
            if (grids.Count == 0) return;
            var limit = budget * 90 / 100;
            int cells = 0, maxCells = armorReady ? 100 : int.MaxValue;
            while (cells < maxCells && Runtime.CurrentInstructionCount < limit)
            {
                if (discGrid < 0 || discGrid >= grids.Count || discPos.X > discMax.X)
                {
                    discGrid++;
                    if (discGrid >= grids.Count)
                    {
                        discGrid = 0;
                        armorReady = true;
                        return;
                    }
                    var g0 = grids[discGrid];
                    if (g0.Closed) { discPos = new Vector3I(1, 0, 0); discMax = Vector3I.Zero; continue; }
                    discMin = g0.Min;
                    discMax = g0.Max;
                    discPos = discMin;
                }
                var g = grids[discGrid];
                if (g.Closed) { discPos.X = discMax.X + 1; continue; }
                var p = discPos;
                cells++;
                if (++discPos.Z > discMax.Z)
                {
                    discPos.Z = discMin.Z;
                    if (++discPos.Y > discMax.Y)
                    {
                        discPos.Y = discMin.Y;
                        discPos.X++;
                    }
                }
                var slim = g.GetCubeBlock(p);
                if (slim == null || slim.Position != p || slim.FatBlock is IMyTerminalBlock) continue;
                Dictionary<Vector3I, Tracked> map;
                if (!armorByGrid.TryGetValue(g, out map))
                {
                    map = new Dictionary<Vector3I, Tracked>();
                    armorByGrid[g] = map;
                }
                if (map.ContainsKey(p)) continue;
                var t = new Tracked { Grid = g, Pos = p, Name = slim.BlockDefinition.SubtypeName };
                t.Build = slim.BuildIntegrity;
                t.Integ = slim.BuildIntegrity - slim.CurrentDamage;
                map[p] = t;
                armor.Add(t);
            }
        }

        void Queue(string kind, Tracked t, string value, float ratio)
        {
            t.LastEvent = now;
            pending.Add(new Pending
            {
                Kind = kind,
                Grid = Clean(t.Grid.CustomName),
                Block = Clean(t.Name),
                Pos = t.Pos.X + "," + t.Pos.Y + "," + t.Pos.Z,
                Value = value ?? "",
                Ratio = value == null ? 0 : ratio
            });
        }

        void Flush()
        {
            if (pending.Count == 0 || (now - lastFlush).TotalSeconds < FlushSeconds) return;
            lastFlush = now;
            var present = Present();
            if (pending.Count <= ManyThreshold)
            {
                foreach (var p in pending) Add(p.Kind, p.Grid, p.Block, p.Pos, p.Value, present);
            }
            else
            {
                // One summary line per grid instead of a flood.
                var done = new HashSet<string>();
                foreach (var first in pending)
                {
                    if (!done.Add(first.Grid)) continue;
                    int dmg = 0, grind = 0, lost = 0, owner = 0;
                    float worst = 1;
                    foreach (var p in pending)
                    {
                        if (p.Grid != first.Grid) continue;
                        if (p.Kind == "DMG") dmg++;
                        else if (p.Kind == "GRIND") grind++;
                        else if (p.Kind == "LOST") lost++;
                        else owner++;
                        if (p.Kind == "LOST") worst = 0;
                        else if (p.Value.Length > 0 && p.Ratio < worst) worst = p.Ratio;
                    }
                    var summary = new StringBuilder();
                    if (dmg > 0) summary.Append(dmg).Append(" damaged");
                    if (grind > 0) summary.Append(summary.Length > 0 ? ", " : "").Append(grind).Append(" ground");
                    if (lost > 0) summary.Append(summary.Length > 0 ? ", " : "").Append(lost).Append(" lost");
                    if (owner > 0) summary.Append(summary.Length > 0 ? ", " : "").Append(owner).Append(" changed owner");
                    Add("MANY", first.Grid, summary.ToString(), "", ((int)Math.Round(worst * 100)).ToString(), present);
                }
            }
            pending.Clear();
        }

        // ---------------------------------------------------------------- log

        void Add(params string[] fields)
        {
            sb.Clear();
            sb.Append(Prefix).Append(now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'"));
            foreach (var f in fields) sb.Append('|').Append(f);
            var line = sb.ToString();
            lines.Add(line);
            logChars += line.Length + 1;
            while (lines.Count > logLength || (logChars > MaxLogChars && lines.Count > 1))
            {
                logChars -= lines[0].Length + 1;
                lines.RemoveAt(0);
            }
            dirty = true;
        }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', '/').Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        void LoadLog()
        {
            // Storage first; if it's empty (new PB, or rebuilt after it was ground), the newest surviving copy.
            string best = null, bestLast = "";
            if (!string.IsNullOrEmpty(Storage) && Storage.StartsWith("TWSTATE|")) best = Storage;
            else
            {
                Candidate(Me.CustomData, ref best, ref bestLast);
                scratch.Clear();
                if (tag.Length > 0) GridTerminalSystem.SearchBlocksOfName(tag, scratch);
                foreach (var b in scratch)
                    if (b.IsSameConstructAs(Me)) Candidate(b.CustomData, ref best, ref bestLast);
                scratch.Clear();
            }
            if (best == null) return;
            foreach (var raw in best.Split('\n'))
            {
                var l = raw.Trim();
                if (l.StartsWith("TWSTATE|"))
                {
                    int.TryParse(l.Substring(8), out visitorCounter);
                    continue;
                }
                if (!l.StartsWith(Prefix)) continue;
                lines.Add(l);
                logChars += l.Length + 1;
                // Visitor numbers continue where the recovered log left off.
                var f = l.Split('|');
                int id;
                if (f.Length > 3 && (f[2] == "SEEN" || f[2] == "LEFT") && f[3].StartsWith("V") && int.TryParse(f[3].Substring(1), out id))
                    visitorCounter = Math.Max(visitorCounter, id);
            }
            while (lines.Count > logLength) { logChars -= lines[0].Length + 1; lines.RemoveAt(0); }
        }

        static void Candidate(string data, ref string best, ref string bestLast)
        {
            var at = data.IndexOf(Marker);
            if (at < 0) return;
            var log = data.Substring(at);
            var end = log.LastIndexOf(Prefix);
            if (end < 0) return;
            var lastLine = log.Substring(end, Math.Min(24, log.Length - end));
            if (best == null || string.CompareOrdinal(lastLine, bestLast) > 0)
            {
                best = log;
                bestLast = lastLine;
            }
        }

        void WriteCopies()
        {
            dirty = false;
            urgent = false;
            lastWrite = now;
            sb.Clear();
            sb.Append(Marker).Append('\n');
            foreach (var l in lines) sb.Append(l).Append('\n');
            var log = sb.ToString();
            WriteData(Me, log);
            string screen = null;
            foreach (var b in copies)
            {
                if (b.Closed) continue;
                WriteData(b, log);
                var panel = b as IMyTextPanel;
                if (panel == null) continue;
                if (screen == null) screen = Screen();
                if (panel.ContentType != ContentType.TEXT_AND_IMAGE) panel.ContentType = ContentType.TEXT_AND_IMAGE;
                panel.WriteText(screen);
            }
        }

        static void WriteData(IMyTerminalBlock b, string log)
        {
            var data = b.CustomData;
            var head = Head(data).TrimEnd();
            b.CustomData = head.Length > 0 ? head + "\n\n" + log : log;
        }

        string Screen()
        {
            var s = new StringBuilder();
            s.Append("Tripwire - ").Append(Me.CubeGrid.CustomName).Append('\n');
            s.Append(utcOffset == 0 ? "Times in UTC" : "Times in UTC" + (utcOffset > 0 ? "+" : "") + utcOffset).Append(", newest first\n\n");
            int shown = 0;
            for (int i = lines.Count - 1; i >= 0 && shown < screenLines; i--, shown++)
            {
                var f = lines[i].Split('|');
                if (f.Length < 4) continue;
                var time = LocalTime(f[1]);
                s.Append(time).Append("  ");
                switch (f[2])
                {
                    case "START": s.Append("Script ").Append(f[3]); break;
                    case "SEEN": if (f.Length > 7) s.Append(f[3]).Append(' ').Append(f[6]).Append(" (").Append(f[4]).Append(") seen by ").Append(f[7]); break;
                    case "LEFT": if (f.Length > 7) s.Append(f[3]).Append(' ').Append(f[6]).Append(" left after ").Append(f[7]).Append(" min"); break;
                    case "DMG": if (f.Length > 7) s.Append(f[4]).Append(" damaged, ").Append(f[6]).Append('%').Append(Who(f[7])); break;
                    case "GRIND": if (f.Length > 7) s.Append(f[4]).Append(" ground, ").Append(f[6]).Append('%').Append(Who(f[7])); break;
                    case "LOST": if (f.Length > 7) s.Append(f[4]).Append(" lost").Append(Who(f[7])); break;
                    case "OWNER": if (f.Length > 7) s.Append(f[4]).Append(" changed owner").Append(Who(f[7])); break;
                    case "MANY": if (f.Length > 7) s.Append(f[3]).Append(": ").Append(f[4]).Append(Who(f[7])); break;
                    default: s.Append(f[2]); break;
                }
                s.Append('\n');
            }
            return s.ToString();
        }

        string LocalTime(string iso)
        {
            // "2026-10-15T21:04:33Z" -> "10-15 21:04" in the screen's time zone. Parsed by hand: server culture varies.
            int y, mo, d, h, mi;
            if (iso.Length < 16 || !int.TryParse(iso.Substring(0, 4), out y) || !int.TryParse(iso.Substring(5, 2), out mo)
                || !int.TryParse(iso.Substring(8, 2), out d) || !int.TryParse(iso.Substring(11, 2), out h) || !int.TryParse(iso.Substring(14, 2), out mi))
                return iso;
            var t = new DateTime(y, mo, d, h, mi, 0).AddHours(utcOffset);
            return t.Month.ToString("00") + "-" + t.Day.ToString("00") + " " + t.Hour.ToString("00") + ":" + t.Minute.ToString("00");
        }

        static string Who(string present)
        {
            return present.Length == 0 ? "" : " [" + present.Replace(":", " ").Replace(";", ", ") + "]";
        }

        // ---------------------------------------------------------------- status

        void Status()
        {
            avgInstr = avgInstr * 0.9 + Runtime.CurrentInstructionCount * 0.1;
            avgMs = avgMs * 0.9 + Runtime.LastRunTimeMs * 0.1;
            sb.Clear();
            sb.Append("Tripwire ").Append(Version).Append('\n');
            if (settingsError != null) sb.Append("SETTINGS NOT READ: ").Append(settingsError).Append('\n');
            sb.Append("Sensors ").Append(sensors.Count).Append(", turrets ").Append(useTurrets ? turrets.Count.ToString() : "off").Append(", copies ").Append(copies.Count).Append('\n');
            foreach (var s in sensors)
                if (!s.Closed && (!s.DetectPlayers || !(s.DetectEnemy || s.DetectNeutral)))
                {
                    sb.Append("! ").Append(s.CustomName).Append(": turn on Detect Players and Enemy/Neutral\n");
                    break;
                }
            sb.Append("Visitors now: ").Append(visitors.Count).Append('\n');
            sb.Append("Terminal blocks ").Append(terminals.Count);
            if (termPasses > 0) sb.Append(", pass ").Append((int)termPassSeconds).Append(" s");
            sb.Append('\n');
            if (watchArmor)
            {
                sb.Append("Armour blocks ").Append(armor.Count);
                if (!armorReady) sb.Append(" (still finding them)");
                else if (armorPasses > 0) sb.Append(", pass ").Append((int)armorPassSeconds).Append(" s");
                sb.Append('\n');
            }
            sb.Append("Log ").Append(lines.Count).Append('/').Append(logLength).Append(" lines\n");
            sb.Append("Instructions ~").Append((int)avgInstr).Append(", ").Append(avgMs.ToString("0.000")).Append(" ms per run\n");
            Echo(sb.ToString());
        }
        #endregion // Script
    }
}
