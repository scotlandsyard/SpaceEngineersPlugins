// Shipwright programmable block script.
// Only the part between "#region Script" and "#endregion // Script" is the in-game script:
// Deploy-Script.ps1 copies it to the game's local script folder. The class around it is only here
// so the script can be compile-checked against the game's assemblies (Script.csproj).

using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace ShipwrightScript
{
    partial class Program : MyGridProgram
    {
        #region Script
        // Shipwright 1.0.0
        // A ship factory: welds the projector's blueprint slice by slice with moving welders, releases the
        // ship, resets and builds the next one.
        // Settings: the [Shipwright] section of this block's Custom Data. Blocks are found by name tags,
        // such as [SW], [SW Weld], [SW Z].
        // Arguments: start [N], pause, resume, stop, skip, retry, release, park, test, refresh, reset.
        // Guide: README.md in the Shipwright project on GitHub.

        const string Version = "1.0.0";
        const string Section = "Shipwright";

        // States. Hold is a wait with a reason; it goes back to Ret when the reason is gone.
        const int Idle = 0, Project = 1, Weld = 2, Verify = 3, Release = 4, Clear = 5, Reset = 6, Hold = 7;
        static readonly string[] StateNames = { "Idle", "Project", "Weld", "Verify", "Release", "Clear", "Reset", "Hold" };

        // Settings
        string tag = "SW", layout = "Gantry", releaseMode = "Merge", stallAction = "wait", nanoMode = "auto", parkText = "";
        double clearDelay = 15, sliceStep = 2.5, planeStep = 2.5, pistonSpeed = 0.5, rotorRpm = 1, maxImpulse = 0;
        double minDwell = 2, idleTimeout = 6, stuckTime = 10, warnEvery = 300, nanoWait = 120, armLength = 2.5;
        double weldReach = 0, weldOffset = 0, retryEvery = 60;
        int scanBudget = 20000;
        bool chatDone = true;
        string rangeZ = "", rangeX = "", rangeY = "", rangeRotor = "-90..90", rangeRadius = "";

        // Blocks
        IMyProjector projector;
        IMyShipMergeBlock merge;
        IMyShipConnector dock;
        IMySensorBlock bay;
        IMyTimerBlock releaseTimer, doneTimer;
        IMyTerminalBlock chat;
        readonly List<IMyShipWelder> welders = new List<IMyShipWelder>();
        readonly List<IMyFunctionalBlock> nanobots = new List<IMyFunctionalBlock>();
        readonly List<IMyTextSurface> screens = new List<IMyTextSurface>();
        readonly List<IMyTerminalBlock> stores = new List<IMyTerminalBlock>();
        Axis az, a1, a2;
        bool arm;
        string setupError = "";

        // State (saved)
        int state = Idle, ret = Idle, rs, done, want, slice, wp, pass = 1;
        string why = "";
        bool test, paused, passProgress;
        List<bool> act = new List<bool>();

        // State (not saved: timers restart after a reload)
        double now, since, lastProg, lastMove, bestDist, lastCheck, lastWarn = -1e9, lastRefresh, avgMs;
        int blockedCount, lastRemaining = -1, verifyRuns;
        bool parking, travelSlice, warned, dirty = true;
        string lastWarnKey = "", warnText = "";
        readonly Dictionary<string, double> park = new Dictionary<string, double>();

        // Reach scan
        bool scanning, haveBase;
        int scW, scI, scSide, scBlocks, baseBlocks;
        double scInteg, baseInteg, reachSq;
        Vector3I scC0;
        Vector3D scCenter;
        readonly HashSet<Vector3I> seen = new HashSet<Vector3I>();
        readonly List<Vector3I> scIncomplete = new List<Vector3I>();
        readonly List<Vector3I> stuck = new List<Vector3I>();
        readonly Dictionary<string, int> needs = new Dictionary<string, int>();
        readonly Dictionary<string, int> have = new Dictionary<string, int>();
        readonly List<MyInventoryItem> items = new List<MyInventoryItem>();
        readonly StringBuilder sb = new StringBuilder();
        readonly MyIni ini = new MyIni();

        class Axis
        {
            public string Name;
            public readonly List<IMyPistonBase> Pistons = new List<IMyPistonBase>();
            public IMyMotorStator Rotor;
            public double A, B;
            public double Cur()
            {
                if (Rotor != null)
                {
                    double d = MathHelper.ToDegrees(Rotor.Angle) % 360;
                    if (d > 180) d -= 360; else if (d <= -180) d += 360;
                    return d;
                }
                double s = 0;
                foreach (var p in Pistons) s += p.CurrentPosition;
                return s;
            }
            public double Tol { get { return Rotor != null ? 0.5 : 0.05; } }
            public double Dist(double t) { double d = Math.Abs(Cur() - t); return Rotor != null ? d / 10 : d; }
            public bool At(double t) { return Math.Abs(Cur() - t) <= Tol; }
            public void MoveTo(double t, double speed, double rpm)
            {
                if (Rotor != null)
                {
                    double c = Cur();
                    if (Math.Abs(c - t) <= Tol) { Rotor.TargetVelocityRPM = 0; return; }
                    if (t > c) { Rotor.LowerLimitDeg = float.MinValue; Rotor.UpperLimitDeg = (float)t; Rotor.TargetVelocityRPM = (float)rpm; }
                    else { Rotor.UpperLimitDeg = float.MaxValue; Rotor.LowerLimitDeg = (float)t; Rotor.TargetVelocityRPM = (float)-rpm; }
                    return;
                }
                // Stacked pistons share the move evenly; each one stops itself at its limit.
                float each = (float)(t / Pistons.Count), v = (float)(speed / Pistons.Count);
                foreach (var p in Pistons)
                {
                    if (Math.Abs(p.CurrentPosition - each) <= 0.01) { p.Velocity = 0; continue; }
                    if (each > p.CurrentPosition) { p.MinLimit = p.LowestPosition; p.MaxLimit = each; p.Velocity = v; }
                    else { p.MaxLimit = p.HighestPosition; p.MinLimit = each; p.Velocity = -v; }
                }
            }
            public void Stop()
            {
                if (Rotor != null) Rotor.TargetVelocityRPM = 0;
                foreach (var p in Pistons) p.Velocity = 0;
            }
            public double Max()
            {
                double s = 0;
                foreach (var p in Pistons) s += p.HighestPosition;
                return s;
            }
        }

        public Program()
        {
            LoadSettings();
            Refresh();
            LoadState();
            SetFrequency();
        }

        public void Save() { Storage = StateText(); }

        public void Main(string argument, UpdateType updateSource)
        {
            now += Runtime.TimeSinceLastRun.TotalSeconds;
            avgMs = avgMs * 0.95 + Runtime.LastRunTimeMs * 0.05;
            if (!string.IsNullOrWhiteSpace(argument)) Command(argument.Trim());
            if ((updateSource & (UpdateType.Update10 | UpdateType.Update100)) != 0)
            {
                if (now - lastRefresh > 300 && !Moving()) Refresh();
                if (setupError.Length == 0 && !paused) Step();
            }
            SetFrequency();
            if (dirty) { Storage = StateText(); dirty = false; }
            Show();
        }

        // ---------- Commands ----------

        void Command(string arg)
        {
            var parts = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            int n;
            dirty = true;
            switch (cmd)
            {
                case "start":
                case "test":
                    if (state != Idle) { warnText = "Already running; use stop first."; return; }
                    LoadSettings(); Refresh();
                    if (setupError.Length > 0) return;
                    test = cmd == "test";
                    want = test ? 1 : (parts.Length > 1 && int.TryParse(parts[1], out n) && n > 0 ? n : 1);
                    done = 0; paused = false; parking = false;
                    Go(Project);
                    break;
                case "pause":
                    if (state == Idle) return;
                    paused = true; StopAll(); SetWelders(false); SetNanobots(false);
                    break;
                case "resume":
                    if (!paused) return;
                    paused = false;
                    if (state == Weld) { rs = 0; StartTravel(false); }
                    break;
                case "stop":
                    paused = false; test = false; StopAll(); SetWelders(false); SetNanobots(false);
                    Go(Idle);
                    if (park.Count > 0) parking = true;
                    break;
                case "skip":
                    if (state != Hold) return;
                    if (why == "Stuck" || why == "Comp" || why == "Nano") { ClearHold(); NextSlice(); }
                    else if (why == "Unplaced") { ClearHold(); Go(Release); }
                    break;
                case "retry":
                    if (state != Hold) return;
                    if (ret == Weld) { ClearHold(); Rerun(); } else { ClearHold(); Go(ret); }
                    break;
                case "release":
                    if (state == Idle || state == Release || state == Clear) return;
                    ClearHold(); StopAll(); SetWelders(false); Go(Release);
                    break;
                case "park":
                    if (state != Idle) { warnText = "Park only works while idle."; return; }
                    parking = park.Count > 0;
                    if (!parking) warnText = "No Park position in Custom Data.";
                    break;
                case "refresh":
                    LoadSettings(); Refresh();
                    break;
                case "reset":
                    StopAll(); SetWelders(false); SetNanobots(false);
                    state = Idle; why = ""; done = want = slice = wp = rs = 0; pass = 1; act.Clear();
                    paused = test = parking = false;
                    break;
                default:
                    warnText = "Unknown command: " + cmd;
                    break;
            }
        }

        // ---------- State machine ----------

        void Go(int s)
        {
            state = s; rs = 0; since = now; verifyRuns = 0; dirty = true;
            if (s == Project) { slice = 0; wp = 0; pass = 1; act.Clear(); passProgress = false; travelSlice = true; StartTravel(true); }
        }

        void Step()
        {
            if (state == Idle) { if (parking && MoveToPark()) { parking = false; StopAll(); } return; }
            switch (state)
            {
                case Project: StepProject(); break;
                case Weld: StepWeld(); break;
                case Verify: StepVerify(); break;
                case Release: StepRelease(); break;
                case Clear: StepClear(); break;
                case Reset: StepReset(); break;
                case Hold: StepHold(); break;
            }
        }

        void StepProject()
        {
            projector.Enabled = true;
            if (!projector.GetValueBool("KeepProjection")) projector.SetValueBool("KeepProjection", true);
            merge.Enabled = true;
            if (!projector.IsProjecting) { if (now - since > 5) Hold_("NoProj", Project); return; }
            if (projector.RemainingBlocks == 0)
            {
                if (merge.IsConnected) { Go(Verify); return; }    // a finished ship is still attached
                if (now - since > 10) Hold_("NoProj", Project);
                return;
            }
            // A part-built ship on the merge block would be in the head's way on its trip to slice 1.
            if (merge.IsConnected && projector.RemainingBlocks < projector.TotalBlocks) { Hold_("Partial", Project); return; }
            // Travel to the first stop with welders off, then start welding.
            if (!Travel()) return;
            SetWelders(!test);
            if (nanoMode == "always" && !test) SetNanobots(true);
            state = Weld; since = now; dirty = true;
            StartDwell();
        }

        void StepWeld()
        {
            if (!test && WeldersUnpowered()) { StopAll(); Hold_("Power", Weld); return; }
            if (rs == 0)
            {
                if (Travel()) StartDwell();
                return;
            }
            // Dwell: stay while something in reach is still being welded.
            double t = now - since;
            if (test) { if (t >= minDwell) Next(false, false); return; }
            if (!scanning) StartScan();
            if (!ScanStep()) return;
            scanning = false;
            int remaining = projector.RemainingBlocks;
            bool first = !haveBase;
            // First look: something unfinished here makes the stop worth a visit. After that, progress is
            // rising integrity, new blocks in reach, or the projector's count going down.
            bool progress = first ? scIncomplete.Count > 0
                : scInteg > baseInteg + 0.01 || scBlocks != baseBlocks || (lastRemaining >= 0 && remaining < lastRemaining);
            if (progress) { lastProg = now; MarkActive(true); if (!first) passProgress = true; }
            haveBase = true; baseInteg = scInteg; baseBlocks = scBlocks; lastRemaining = remaining;
            if (t < minDwell) return;
            double idle = now - lastProg;
            if (scIncomplete.Count == 0 && idle >= minDwell) { Next(false, progressHere); return; }
            if (scIncomplete.Count > 0 && idle >= idleTimeout)
            {
                foreach (var c in scIncomplete) if (!stuck.Contains(c) && stuck.Count < 50) stuck.Add(c);
                Next(true, progressHere);
            }
        }

        bool progressHere;

        void StartDwell()
        {
            rs = 1; since = now; lastProg = now; haveBase = false; scanning = false; progressHere = false; blockedCount = 0; dirty = true;
        }

        void MarkActive(bool on)
        {
            progressHere |= on;
            while (act.Count <= wp) act.Add(false);
            if (on) act[wp] = true;
        }

        // Leave this stop. In later passes, a stop with nothing to do drops out of the active list.
        void Next(bool stuckHere, bool prog)
        {
            while (act.Count <= wp) act.Add(false);
            if (stuckHere) act[wp] = true;
            else if (pass > 1 && !prog) act[wp] = false;
            int count = PlaneCount();
            int n = wp + 1;
            if (pass > 1) while (n < count && (n >= act.Count || !act[n])) n++;
            if (n < count) { wp = n; travelSlice = false; StartTravel(false); return; }
            EndPass();
        }

        void EndPass()
        {
            if (test) { NextSlice(); return; }
            if (passProgress && AnyActive()) { Rerun(); return; }
            if (stuck.Count == 0) { NextSlice(); return; }
            StallCheck();
        }

        void Rerun()
        {
            pass++; passProgress = false; stuck.Clear();
            wp = 0;
            while (wp < act.Count && !act[wp]) wp++;
            if (wp >= act.Count) { NextSlice(); return; }
            state = Weld; travelSlice = false; StartTravel(false);
        }

        bool AnyActive() { foreach (var a in act) if (a) return true; return false; }

        void NextSlice()
        {
            stuck.Clear(); act.Clear(); pass = 1; passProgress = false; wp = 0;
            slice++;
            if (slice >= SliceCount()) { SetNanobots(false); Go(test ? Idle : Verify); if (test) { SetWelders(false); test = false; if (park.Count > 0) parking = true; } return; }
            state = Weld; travelSlice = true; StartTravel(false);
        }

        // Stuck blocks after a pass with no progress: missing components, out of reach, or a limit.
        void StallCheck()
        {
            if (MissingComponents()) { Hold_("Comp", Weld); return; }
            if (nanobots.Count > 0 && nanoMode != "off") { SetNanobots(true); lastProg = now; Hold_("Nano", Weld); return; }
            Hold_("Stuck", Weld);
            if (stallAction == "skip") { Warn("Stuck", StuckText()); warned = false; ClearHold(); NextSlice(); }
        }

        void StepVerify()
        {
            SetWelders(false);
            if (++verifyRuns < 2) return;    // let the projector recount first
            if (projector.RemainingBlocks == 0 && projector.IsProjecting) { Go(Release); return; }
            if (nanobots.Count > 0 && nanoMode != "off") SetNanobots(true);
            Hold_("Unplaced", Verify);
        }

        void StepRelease()
        {
            switch (rs)
            {
                case 0:
                    SetWelders(false); SetNanobots(false);
                    if (park.Count > 0 && !MoveToPark()) return;
                    StopAll();
                    if (releaseTimer != null) releaseTimer.Trigger();
                    if (releaseMode == "connector" && dock != null) { dock.Connect(); rs = 1; } else rs = 2;
                    since = now; dirty = true;
                    return;
                case 1:
                    if (dock.Status == MyShipConnectorStatus.Connected) { rs = 2; since = now; dirty = true; return; }
                    dock.Connect();
                    if (now - since > 10) Hold_("Dock", Release);
                    return;
                case 2:
                    merge.Enabled = false; rs = 3; since = now; dirty = true;
                    return;
                case 3:
                    if (!merge.IsConnected) { state = Clear; rs = 0; since = now; dirty = true; }
                    return;
            }
        }

        void StepClear()
        {
            // Connector mode: the ship waits on the connector for a pilot.
            if (releaseMode == "connector" && dock != null && dock.Status == MyShipConnectorStatus.Connected) { since = now; return; }
            bool full = bay != null && bay.IsActive;
            if (full) { if (now - since > 300) Warn("Bay", "The bay isn't clear: the sensor still sees something."); return; }
            if (now - since >= clearDelay) Go(Reset);
        }

        void StepReset()
        {
            merge.Enabled = true;
            done++;
            if (doneTimer != null) doneTimer.Trigger();
            if (chatDone) Chat("Ship " + done + " of " + want + " is finished and released.");
            if (done < want) Go(Project);
            else { Go(Idle); if (park.Count > 0) parking = true; }
        }

        void Hold_(string reason, int back)
        {
            if (state != Hold) ret = back;
            state = Hold; why = reason; since = now; lastCheck = now; dirty = true;
            if (reason != "Nano" && reason != "Power") SetWelders(false);
            StopAll();
        }

        void ClearHold()
        {
            if (warned) Chat("OK again: " + WhyText() + " is solved, carrying on.");
            warned = false; lastWarnKey = ""; why = "";
        }

        void StepHold()
        {
            double t = now - since;
            switch (why)
            {
                case "Power":
                    Warn(why, "The welders are off or have no power.");
                    if (!WeldersUnpowered()) { ClearHold(); state = Weld; SetWelders(true); StartTravel(false); }
                    return;
                case "NoProj":
                    Warn(why, "The projector shows nothing to build. Load the blueprint and check it lines up with the merge block.");
                    if (projector.IsProjecting && projector.RemainingBlocks > 0) { ClearHold(); Go(Project); }
                    return;
                case "Comp":
                    Warn(why, "Waiting for components: " + NeedsText());
                    if (now - lastCheck >= 30) { lastCheck = now; if (!MissingComponents() || StuckDone()) { ClearHold(); state = Weld; SetWelders(true); Rerun(); } }
                    return;
                case "Nano":
                    if (now - lastCheck >= 10)
                    {
                        lastCheck = now;
                        double before = baseInteg;
                        if (StuckDone()) { if (nanoMode == "auto") SetNanobots(false); ClearHold(); state = Weld; SetWelders(true); Rerun(); return; }
                        if (baseInteg > before + 0.01) lastProg = now;
                    }
                    if (now - lastProg > nanoWait) { if (nanoMode == "auto") SetNanobots(false); why = "Stuck"; since = now; dirty = true; }
                    return;
                case "Stuck":
                    Warn(why, StuckText());
                    if (now - lastCheck >= 30) { lastCheck = now; if (StuckDone()) { ClearHold(); state = Weld; SetWelders(true); Rerun(); } }
                    return;
                case "Unplaced":
                    Warn(why, projector.RemainingBlocks + " projected blocks were never placed (out of reach or not attached yet): " + RemainingTypes() + ". Weld them by hand, then the factory carries on, or use skip to release anyway.");
                    if (projector.RemainingBlocks == 0) { if (nanoMode == "auto") SetNanobots(false); ClearHold(); Go(Release); }
                    return;
                case "Blocked":
                    Warn(why, "The welder head is blocked: " + (blockedAxis ?? "an axis") + " can't reach its target. It tries again every " + retryEvery + " s.");
                    if (t >= retryEvery) { ClearHold(); state = ret; if (ret == Weld) StartTravel(false); else rs = 0; since = now; }
                    return;
                case "Partial":
                    Warn(why, "A part-built ship is still on the merge block, in the welders' way. Use release to let it go, or grind it off.");
                    if (!merge.IsConnected) { ClearHold(); Go(Project); }
                    return;
                case "Dock":
                    Warn(why, "Release: the ship's connector didn't lock to [" + tag + " Dock].");
                    if (dock.Status == MyShipConnectorStatus.Connected || t >= retryEvery) { ClearHold(); state = Release; rs = 0; }
                    return;
            }
        }

        // ---------- Movement ----------

        string blockedAxis;

        void StartTravel(bool slice0)
        {
            rs = 0; since = now; lastMove = now; bestDist = double.MaxValue; dirty = true;
            if (slice0) travelSlice = true;
        }

        bool Travel()
        {
            double z = SliceValue(slice), x, y;
            PlanePoint(wp, out x, out y);
            bool at = MoveAxis(az, z);
            if (a1 != null) at &= MoveAxis(a1, x);
            if (a2 != null) at &= MoveAxis(a2, y);
            if (at) { lastRemaining = -1; return true; }
            double d = az.Dist(z) + (a1 != null ? a1.Dist(x) : 0) + (a2 != null ? a2.Dist(y) : 0);
            if (d < bestDist - 0.02) { bestDist = d; lastMove = now; }
            else if (now - lastMove > stuckTime)
            {
                StopAll();
                blockedAxis = !az.At(z) ? az.Name : a1 != null && !a1.At(x) ? a1.Name : a2 != null ? a2.Name : null;
                // A blocked stop inside a slice is skipped; a blocked slice move or three in a row is a hold.
                if (state == Weld && !travelSlice && ++blockedCount < 3)
                {
                    while (act.Count <= wp) act.Add(false);
                    act[wp] = false;
                    warnText = "Stop " + (wp + 1) + " blocked, skipped.";
                    Next(false, false);
                }
                else Hold_("Blocked", state);
            }
            return false;
        }

        bool MoveAxis(Axis a, double t)
        {
            if (a.At(t)) { a.Stop(); return true; }
            a.MoveTo(t, pistonSpeed, rotorRpm);
            return false;
        }

        bool MoveToPark()
        {
            bool at = true;
            double v;
            if (park.TryGetValue("Z", out v)) at &= MoveAxis(az, v);
            if (a1 != null && park.TryGetValue(a1.Name, out v)) at &= MoveAxis(a1, v);
            if (a2 != null && park.TryGetValue(a2.Name, out v)) at &= MoveAxis(a2, v);
            return at;
        }

        void StopAll()
        {
            if (az != null) az.Stop();
            if (a1 != null) a1.Stop();
            if (a2 != null) a2.Stop();
        }

        bool Moving()
        {
            if (paused) return false;
            return parking || state == Project || (state == Weld && rs == 0) || (state == Release && rs == 0 && park.Count > 0);
        }

        void SetFrequency()
        {
            Runtime.UpdateFrequency = setupError.Length > 0 ? UpdateFrequency.Update100
                : Moving() ? UpdateFrequency.Update10 : UpdateFrequency.Update100;
        }

        // ---------- Path ----------

        static int Steps(double a, double b, double step)
        {
            double len = Math.Abs(b - a);
            return len < 1e-6 ? 1 : (int)Math.Ceiling(len / step - 1e-6) + 1;
        }

        static double StepValue(double a, double b, double step, int i)
        {
            double len = Math.Abs(b - a);
            if (len < 1e-6) return a;
            return a + (b - a) * Math.Min(1, i * step / len);
        }

        int SliceCount() { return Steps(az.A, az.B, sliceStep); }
        double SliceValue(int s) { return StepValue(az.A, az.B, sliceStep, s); }

        int PlaneCount()
        {
            if (a1 == null) return 1;
            if (!arm) return Steps(a1.A, a1.B, planeStep) * (a2 != null ? Steps(a2.A, a2.B, planeStep) : 1);
            int total = 0, rings = a2 != null ? Steps(a2.A, a2.B, planeStep) : 1;
            for (int r = 0; r < rings; r++) total += ArcSteps(r);
            return total;
        }

        int ArcSteps(int ring)
        {
            double radius = armLength + (a2 != null ? StepValue(a2.A, a2.B, planeStep, ring) : 0);
            double arc = MathHelper.ToRadians(Math.Abs(a1.B - a1.A)) * Math.Max(radius, 0.1);
            return Steps(0, arc, planeStep);
        }

        // Stop k of the current slice: a zig-zag over X/Y, or arcs ring by ring on an arm.
        void PlanePoint(int k, out double x, out double y)
        {
            x = 0; y = 0;
            if (a1 == null) return;
            if (!arm)
            {
                int ny = a2 != null ? Steps(a2.A, a2.B, planeStep) : 1;
                int ix = k / ny, iy = k % ny;
                if (ix % 2 == 1) iy = ny - 1 - iy;
                x = StepValue(a1.A, a1.B, planeStep, ix);
                if (a2 != null) y = StepValue(a2.A, a2.B, planeStep, iy);
                return;
            }
            int rings = a2 != null ? Steps(a2.A, a2.B, planeStep) : 1;
            for (int r = 0; r < rings; r++)
            {
                int n = ArcSteps(r);
                if (k < n)
                {
                    int i = r % 2 == 1 ? n - 1 - k : k;
                    x = n == 1 ? a1.A : a1.A + (a1.B - a1.A) * i / (n - 1);
                    if (a2 != null) y = StepValue(a2.A, a2.B, planeStep, r);
                    return;
                }
                k -= n;
            }
        }

        // ---------- Reach scan (spread over runs) ----------

        void StartScan()
        {
            scanning = true; scW = 0; scI = -1; scBlocks = 0; scInteg = 0;
            seen.Clear(); scIncomplete.Clear();
        }

        // Reads the build grid's blocks inside each welder's reach. Returns false when it ran out of
        // instructions for this run and continues next run.
        bool ScanStep()
        {
            var grid = projector.CubeGrid;
            double half = grid.GridSize * 0.5;
            while (scW < welders.Count)
            {
                var w = welders[scW];
                if (scI < 0)
                {
                    if (!w.IsFunctional) { scW++; continue; }
                    bool large = w.CubeGrid.GridSizeEnum == MyCubeSize.Large;
                    double reach = weldReach > 0 ? weldReach : large ? 2.26 : 1.3;
                    Vector3I size = w.Max - w.Min + Vector3I.One;
                    Vector3I f = Base6Directions.GetIntVector(w.Orientation.Forward);
                    double halfLen = Math.Abs(size.X * f.X + size.Y * f.Y + size.Z * f.Z) * w.CubeGrid.GridSize * 0.5;
                    scCenter = w.GetPosition() + w.WorldMatrix.Forward * (halfLen + weldOffset);
                    int rc = (int)Math.Ceiling(reach / grid.GridSize);
                    scSide = 2 * rc + 1;
                    scC0 = grid.WorldToGridInteger(scCenter) - new Vector3I(rc, rc, rc);
                    reachSq = (reach + half) * (reach + half);
                    scI = 0;
                }
                int total = scSide * scSide * scSide;
                while (scI < total)
                {
                    if (Runtime.CurrentInstructionCount > scanBudget) return false;
                    int i = scI++;
                    var cell = scC0 + new Vector3I(i % scSide, i / scSide % scSide, i / (scSide * scSide));
                    if (Vector3D.DistanceSquared(grid.GridIntegerToWorld(cell), scCenter) > reachSq) continue;
                    var slim = grid.GetCubeBlock(cell);
                    if (slim == null || !seen.Add(slim.Position)) continue;
                    scBlocks++;
                    scInteg += slim.BuildIntegrity;
                    if (!slim.IsFullIntegrity) scIncomplete.Add(slim.Position);
                }
                scW++; scI = -1;
            }
            return true;
        }

        // True when every stuck block is now complete (welded by hand, Nanobot or late components).
        // Also leaves their total integrity in baseInteg for the Nanobot progress check.
        bool StuckDone()
        {
            var grid = projector.CubeGrid;
            bool all = true;
            baseInteg = 0;
            foreach (var c in stuck)
            {
                var slim = grid.GetCubeBlock(c);
                if (slim == null) continue;
                baseInteg += slim.BuildIntegrity;
                if (!slim.IsFullIntegrity) all = false;
            }
            return all;
        }

        // ---------- Components ----------

        bool MissingComponents()
        {
            needs.Clear(); have.Clear();
            var grid = projector.CubeGrid;
            foreach (var c in stuck)
            {
                var slim = grid.GetCubeBlock(c);
                if (slim != null) slim.GetMissingComponents(needs);
            }
            if (needs.Count == 0) return false;
            foreach (var b in stores)
            {
                if (b.Closed) continue;
                for (int i = 0; i < b.InventoryCount; i++)
                {
                    items.Clear();
                    b.GetInventory(i).GetItems(items);
                    foreach (var it in items)
                    {
                        if (it.Type.TypeId != "MyObjectBuilder_Component") continue;
                        int n;
                        have.TryGetValue(it.Type.SubtypeId, out n);
                        have[it.Type.SubtypeId] = n + it.Amount.ToIntSafe();
                    }
                }
            }
            bool missing = false;
            var keys = new List<string>(needs.Keys);
            foreach (var k in keys)
            {
                int h;
                have.TryGetValue(k, out h);
                if (h >= needs[k]) needs.Remove(k); else { needs[k] -= h; missing = true; }
            }
            return missing;
        }

        string NeedsText()
        {
            sb.Clear();
            foreach (var kv in needs) { if (sb.Length > 0) sb.Append(", "); sb.Append(kv.Key).Append(' ').Append(kv.Value); }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        string StuckText()
        {
            sb.Clear();
            sb.Append(stuck.Count).Append(" block(s) in slice ").Append(slice + 1).Append(" don't finish although the components are there (out of reach, or a block or PCU limit): ");
            var grid = projector.CubeGrid;
            int shown = 0;
            foreach (var c in stuck)
            {
                var slim = grid.GetCubeBlock(c);
                if (slim == null || slim.IsFullIntegrity) continue;
                if (shown++ == 5) { sb.Append(" ..."); break; }
                if (shown > 1) sb.Append(", ");
                sb.Append(slim.BlockDefinition.SubtypeName).Append(" at ").Append(c.X).Append(',').Append(c.Y).Append(',').Append(c.Z);
            }
            sb.Append(". Weld them by hand (the factory carries on by itself), or use skip.");
            return sb.ToString();
        }

        string RemainingTypes()
        {
            sb.Clear();
            int shown = 0;
            foreach (var kv in projector.RemainingBlocksPerType)
            {
                if (shown++ == 5) { sb.Append(" ..."); break; }
                if (shown > 1) sb.Append(", ");
                string name = kv.Key.ToString();
                int slash = name.LastIndexOf('/');
                sb.Append(slash >= 0 ? name.Substring(slash + 1) : name).Append(" x").Append(kv.Value);
            }
            return sb.ToString();
        }

        // ---------- Blocks ----------

        void SetWelders(bool on) { foreach (var w in welders) if (w.Enabled != on) w.Enabled = on; }

        void SetNanobots(bool on)
        {
            if (nanoMode == "off" && on) return;
            foreach (var b in nanobots) if (b.Enabled != on) b.Enabled = on;
        }

        bool WeldersUnpowered()
        {
            int working = 0, functional = 0;
            foreach (var w in welders) if (w.IsFunctional) { functional++; if (w.IsWorking) working++; }
            return functional == 0 || working == 0;
        }

        // The role is the text after the tag inside the brackets: "[SW Weld]" -> "Weld", "[SW]" -> "".
        string Role(string name)
        {
            string open = "[" + tag;
            int i = name.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int j = name.IndexOf(']', i);
            if (j < 0) return null;
            string rest = name.Substring(i + open.Length, j - i - open.Length);
            if (rest.Length > 0 && rest[0] != ' ') return null;
            return rest.Trim().ToLowerInvariant();
        }

        void Refresh()
        {
            lastRefresh = now;
            projector = null; merge = null; dock = null; bay = null; releaseTimer = doneTimer = null; chat = null;
            welders.Clear(); nanobots.Clear(); screens.Clear(); stores.Clear();
            az = new Axis { Name = "Z" };
            a1 = new Axis { Name = arm ? "Rotor" : "X" };
            a2 = new Axis { Name = arm ? "Radius" : "Y" };
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, b => b.IsSameConstructAs(Me));
            foreach (var b in blocks)
            {
                if (b.HasInventory) stores.Add(b);
                string role = Role(b.CustomName);
                if (role == null) continue;
                var piston = b as IMyPistonBase;
                if (piston != null)
                {
                    if (role == "z") az.Pistons.Add(piston);
                    else if (role == "x" && !arm) a1.Pistons.Add(piston);
                    else if (role == "y" && !arm) a2.Pistons.Add(piston);
                    else if (role == "radius" && arm) a2.Pistons.Add(piston);
                    if (maxImpulse > 0) { piston.SetValueFloat("MaxImpulseAxis", (float)maxImpulse); piston.SetValueFloat("MaxImpulseNonAxis", (float)maxImpulse); }
                    continue;
                }
                if (role == "rotor" && arm && b is IMyMotorStator) { a1.Rotor = (IMyMotorStator)b; continue; }
                if (role == "" && b is IMyProjector) projector = (IMyProjector)b;
                else if (role == "weld" && b is IMyShipWelder) welders.Add((IMyShipWelder)b);
                else if (role == "release" && b is IMyShipMergeBlock) merge = (IMyShipMergeBlock)b;
                else if (role == "dock" && b is IMyShipConnector) dock = (IMyShipConnector)b;
                else if (role == "bay" && b is IMySensorBlock) bay = (IMySensorBlock)b;
                else if (role == "timer release" && b is IMyTimerBlock) releaseTimer = (IMyTimerBlock)b;
                else if (role == "timer done" && b is IMyTimerBlock) doneTimer = (IMyTimerBlock)b;
                else if (role == "chat" && b is IMyBroadcastController) chat = b;
                else if (role == "nanobot" && b is IMyFunctionalBlock) nanobots.Add((IMyFunctionalBlock)b);
                else if (role == "lcd" && b is IMyTextSurfaceProvider)
                {
                    var p = (IMyTextSurfaceProvider)b;
                    if (p.SurfaceCount > 0) screens.Add(p.GetSurface(0));
                }
            }
            if (az.Pistons.Count == 0) az = null;
            if (a1.Pistons.Count == 0 && a1.Rotor == null) a1 = null;
            if (a2.Pistons.Count == 0) a2 = null;
            if (a1 == null && a2 != null) { a1 = a2; a2 = null; }

            sb.Clear();
            if (projector == null) sb.Append("No projector named [" + tag + "]. ");
            if (merge == null) sb.Append("No merge block named [" + tag + " Release]. ");
            if (welders.Count == 0) sb.Append("No welders named [" + tag + " Weld]. ");
            if (az == null) sb.Append("No pistons named [" + tag + " Z] (the slice axis). ");
            if (layout == "gantry" && a1 == null) sb.Append("Gantry needs pistons named [" + tag + " X] (and optionally [" + tag + " Y]). ");
            if (arm && (a1 == null || a1.Rotor == null)) sb.Append("Arm needs a rotor named [" + tag + " Rotor] and pistons named [" + tag + " Radius]. ");
            if (releaseMode == "connector" && dock == null) sb.Append("Release=Connector needs a connector named [" + tag + " Dock]. ");
            setupError = sb.ToString();
            if (setupError.Length > 0) { StopAll(); return; }
            Range(az, rangeZ);
            if (a1 != null) Range(a1, a1.Rotor != null ? rangeRotor : rangeX);
            if (a2 != null) Range(a2, arm ? rangeRadius : rangeY);
        }

        // "a..b" in metres (pistons, total over the stack) or degrees (rotor); empty = the full piston range.
        void Range(Axis a, string text)
        {
            a.A = 0; a.B = a.Rotor != null ? 180 : a.Max();
            var p = (text ?? "").Split(new[] { ".." }, StringSplitOptions.None);
            double x, y;
            if (p.Length == 2 && double.TryParse(p[0], out x) && double.TryParse(p[1], out y)) { a.A = x; a.B = y; }
        }

        // ---------- Chat and screens ----------

        void Warn(string key, string text)
        {
            warnText = text;
            if (key == lastWarnKey && (warnEvery <= 0 || now - lastWarn < warnEvery)) return;
            lastWarnKey = key; lastWarn = now; warned = true;
            Chat(text);
        }

        void Chat(string text)
        {
            if (chat == null || !chat.IsWorking) return;
            var msg = new StringBuilder(Section + ": " + text);
            chat.SetValue("Message0", msg);
            chat.ApplyAction("Transmit Message 1");
        }

        string WhyText()
        {
            switch (why)
            {
                case "Power": return "power";
                case "NoProj": return "the projection";
                case "Comp": return "components";
                case "Nano": return "Nanobot help";
                case "Stuck": return "stuck blocks";
                case "Unplaced": return "unplaced blocks";
                case "Blocked": return "the blocked head";
                case "Dock": return "the connector";
                case "Partial": return "the part-built ship";
                default: return "the problem";
            }
        }

        void Show()
        {
            sb.Clear();
            sb.Append("Shipwright ").Append(Version).Append(" - ").Append(CapAxis(layout)).Append(" / ").Append(CapAxis(releaseMode)).Append('\n');
            if (setupError.Length > 0) sb.Append("Setup: ").Append(setupError).Append('\n');
            else
            {
                sb.Append("State: ").Append(paused ? "Paused (" + StateNames[state] + ")" : StateNames[state]);
                if (test) sb.Append(" [test]");
                if (state == Hold) sb.Append(" - ").Append(WhyText());
                sb.Append('\n');
                if (state != Idle) sb.Append("Build ").Append(Math.Min(done + 1, want)).Append(" of ").Append(want).Append('\n');
                if (state == Weld || (state == Hold && ret == Weld))
                    sb.Append("Slice ").Append(slice + 1).Append('/').Append(SliceCount()).Append("  Stop ").Append(wp + 1).Append('/').Append(PlaneCount()).Append("  Pass ").Append(pass).Append('\n');
                if (projector != null && projector.IsProjecting) sb.Append("Projector: ").Append(projector.RemainingBlocks).Append(" of ").Append(projector.TotalBlocks).Append(" blocks left\n");
                if (state == Weld && rs == 1 && haveBase) sb.Append("In reach: ").Append(scIncomplete.Count).Append(" unfinished\n");
                if (warnText.Length > 0) sb.Append(warnText).Append('\n');
                if (chat == null) sb.Append("(no [" + tag + " Chat] broadcast controller: warnings only here)\n");
            }
            sb.Append("Run: ").Append(avgMs.ToString("0.000")).Append(" ms, ").Append(Runtime.UpdateFrequency == UpdateFrequency.Update10 ? "Update10" : "Update100");
            string text = sb.ToString();
            Echo(text);
            foreach (var s in screens) { s.ContentType = ContentType.TEXT_AND_IMAGE; s.WriteText(text); }
            var own = Me.GetSurface(0);
            own.ContentType = ContentType.TEXT_AND_IMAGE;
            own.WriteText(text);
        }

        // ---------- Settings and storage ----------

        void LoadSettings()
        {
            ini.Clear();
            ini.TryParse(Me.CustomData);
            if (!ini.ContainsSection(Section)) { WriteDefaults(); ini.TryParse(Me.CustomData); }
            tag = ini.Get(Section, "Tag").ToString(tag).Trim('[', ']', ' ');
            layout = ini.Get(Section, "Layout").ToString("Gantry").ToLowerInvariant();
            arm = layout == "arm";
            releaseMode = ini.Get(Section, "Release").ToString("Merge").ToLowerInvariant();
            clearDelay = ini.Get(Section, "ClearDelay").ToDouble(15);
            sliceStep = Math.Max(0.25, ini.Get(Section, "SliceStep").ToDouble(2.5));
            planeStep = Math.Max(0.25, ini.Get(Section, "PlaneStep").ToDouble(2.5));
            rangeZ = ini.Get(Section, "Z").ToString("");
            rangeX = ini.Get(Section, "X").ToString("");
            rangeY = ini.Get(Section, "Y").ToString("");
            rangeRotor = ini.Get(Section, "Rotor").ToString("-90..90");
            rangeRadius = ini.Get(Section, "Radius").ToString("");
            armLength = ini.Get(Section, "ArmLength").ToDouble(2.5);
            pistonSpeed = MathHelper.Clamp(ini.Get(Section, "PistonSpeed").ToDouble(0.5), 0.05, 5);
            rotorRpm = MathHelper.Clamp(ini.Get(Section, "RotorRPM").ToDouble(1), 0.1, 5);
            maxImpulse = ini.Get(Section, "MaxImpulse").ToDouble(0);
            minDwell = ini.Get(Section, "MinDwell").ToDouble(2);
            idleTimeout = ini.Get(Section, "IdleTimeout").ToDouble(6);
            stuckTime = ini.Get(Section, "BlockedAfter").ToDouble(10);
            retryEvery = ini.Get(Section, "RetryEvery").ToDouble(60);
            weldReach = ini.Get(Section, "WeldReach").ToDouble(0);
            weldOffset = ini.Get(Section, "WeldOffset").ToDouble(0);
            stallAction = ini.Get(Section, "StallAction").ToString("wait").ToLowerInvariant();
            warnEvery = ini.Get(Section, "WarnEvery").ToDouble(300);
            chatDone = ini.Get(Section, "ChatDone").ToBoolean(true);
            nanoMode = ini.Get(Section, "Nanobot").ToString("auto").ToLowerInvariant();
            nanoWait = ini.Get(Section, "NanobotWait").ToDouble(120);
            scanBudget = MathHelper.Clamp(ini.Get(Section, "ScanBudget").ToInt32(20000), 5000, 40000);
            parkText = ini.Get(Section, "Park").ToString("");
            park.Clear();
            foreach (var part in parkText.Split(','))
            {
                var kv = part.Split(':');
                double v;
                if (kv.Length == 2 && double.TryParse(kv[1].Trim(), out v)) park[CapAxis(kv[0].Trim())] = v;
            }
        }

        static string CapAxis(string s)
        {
            if (s.Length == 0) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        void WriteDefaults()
        {
            ini.Set(Section, "Tag", "SW");
            ini.Set(Section, "Layout", "Gantry");
            ini.SetComment(Section, "Layout", "Gantry, Arm, Wall or Pull");
            ini.Set(Section, "Release", "Merge");
            ini.SetComment(Section, "Release", "Merge (unmerge and let it go) or Connector (wait for a pilot)");
            ini.Set(Section, "ClearDelay", 15);
            ini.Set(Section, "SliceStep", 2.5);
            ini.Set(Section, "PlaneStep", 2.5);
            ini.Set(Section, "Z", "");
            ini.SetComment(Section, "Z", "Axis ranges as a..b (metres; degrees for Rotor). Empty = full piston range");
            ini.Set(Section, "X", "");
            ini.Set(Section, "Y", "");
            ini.Set(Section, "Rotor", "-90..90");
            ini.Set(Section, "Radius", "");
            ini.Set(Section, "ArmLength", 2.5);
            ini.Set(Section, "PistonSpeed", 0.5);
            ini.Set(Section, "RotorRPM", 1);
            ini.Set(Section, "MaxImpulse", 0);
            ini.Set(Section, "MinDwell", 2);
            ini.Set(Section, "IdleTimeout", 6);
            ini.Set(Section, "BlockedAfter", 10);
            ini.Set(Section, "RetryEvery", 60);
            ini.Set(Section, "WeldReach", 0);
            ini.Set(Section, "WeldOffset", 0);
            ini.Set(Section, "StallAction", "wait");
            ini.Set(Section, "WarnEvery", 300);
            ini.Set(Section, "ChatDone", true);
            ini.Set(Section, "Nanobot", "auto");
            ini.Set(Section, "NanobotWait", 120);
            ini.Set(Section, "ScanBudget", 20000);
            ini.Set(Section, "Park", "");
            ini.SetComment(Section, "Park", "Where the head goes before release, e.g. Z:10,X:0");
            Me.CustomData = ini.ToString();
        }

        string StateText()
        {
            sb.Clear();
            sb.Append("SW1;st=").Append(state).Append(";ret=").Append(ret).Append(";why=").Append(why)
              .Append(";rs=").Append(rs).Append(";done=").Append(done).Append(";want=").Append(want)
              .Append(";sl=").Append(slice).Append(";wp=").Append(wp).Append(";pass=").Append(pass)
              .Append(";prog=").Append(passProgress ? 1 : 0).Append(";test=").Append(test ? 1 : 0)
              .Append(";pause=").Append(paused ? 1 : 0).Append(";act=");
            for (int i = 0; i < act.Count; i += 4)
            {
                int v = 0;
                for (int j = 0; j < 4 && i + j < act.Count; j++) if (act[i + j]) v |= 1 << j;
                sb.Append("0123456789abcdef"[v]);
            }
            return sb.ToString();
        }

        void LoadState()
        {
            if (!Storage.StartsWith("SW1;")) return;
            foreach (var part in Storage.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                int n;
                int.TryParse(v, out n);
                switch (k)
                {
                    case "st": state = n; break;
                    case "ret": ret = n; break;
                    case "why": why = v; break;
                    case "rs": rs = n; break;
                    case "done": done = n; break;
                    case "want": want = n; break;
                    case "sl": slice = n; break;
                    case "wp": wp = n; break;
                    case "pass": pass = Math.Max(1, n); break;
                    case "prog": passProgress = n == 1; break;
                    case "test": test = n == 1; break;
                    case "pause": paused = n == 1; break;
                    case "act":
                        act.Clear();
                        foreach (char c in v)
                        {
                            int h = "0123456789abcdef".IndexOf(c);
                            for (int j = 0; j < 4; j++) act.Add(h >= 0 && (h & (1 << j)) != 0);
                        }
                        break;
                }
            }
            if (state < Idle || state > Hold) state = Idle;
            if (setupError.Length > 0) return;
            // After a reload: pick up where it was. Stuck-block lists aren't saved, so a hold about them
            // becomes a fresh pass over the slice, which finds them again.
            if (state == Hold && (why == "Comp" || why == "Nano" || why == "Stuck")) { state = Weld; why = ""; Rerun(); pass = Math.Max(1, pass - 1); }
            else if (state == Weld) { StartTravel(false); if (!test) SetWelders(true); }
            else if (state == Project) StartTravel(true);
            since = now;
        }
        #endregion // Script
    }
}
