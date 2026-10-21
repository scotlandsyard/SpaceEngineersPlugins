// Radar Tech programmable block script.
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

namespace RadarTechScript
{
    partial class Program : MyGridProgram
    {
        #region Script
        // Radar Tech 1.0.0
        // A range finder: camera blocks measure the distance to whatever they point at (an asteroid, a
        // planet's surface, a ship or a station) and show it on screens, with closing speed and time to contact.
        // Setup: put [Radar] in the name of one or more camera blocks and of the screens to show it on
        // (text panels, or cockpits and other blocks with screens). The programmable block's own screen
        // always shows it too.
        // Arguments: ping (one reading), on, off, toggle (keep measuring), range <km>.
        // Settings: the [Radar] section of this block's Custom Data (written on first run).
        // Guide: README.md in the Radar Tech project on GitHub.

        const string Version = "1.0.0";
        const string Section = "Radar";

        // Settings
        string tag = "[Radar]";
        double rangeKm = 10;
        bool auto = false;
        int screenIndex = 0;

        readonly MyIni ini = new MyIni();
        readonly List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();
        readonly List<IMyTextSurface> screens = new List<IMyTextSurface>();
        IMyShipController controller;
        int nextCamera;
        int refreshTicks;
        string setupError = "";

        // The last reading
        bool hasReading;
        MyDetectedEntityInfo last;
        double lastDistance;
        double lastClosing;
        string lastCamera = "";
        DateTime lastAt;

        public Program()
        {
            LoadSettings();
            if (Storage == "on") auto = true;
            Refresh();
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Save() { Storage = auto ? "on" : ""; }

        public void Main(string argument, UpdateType updateSource)
        {
            string arg = (argument ?? "").Trim().ToLowerInvariant();
            if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0 && arg.Length > 0)
            {
                Command(arg);
            }
            if (++refreshTicks >= 60) { refreshTicks = 0; Refresh(); }
            if (auto) Measure(false);
            Draw();
        }

        void Command(string arg)
        {
            string[] words = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "ping": Refresh(); Measure(true); break;
                case "on": auto = true; break;
                case "off": auto = false; break;
                case "toggle": auto = !auto; break;
                case "range":
                    double km;
                    if (words.Length > 1 && double.TryParse(words[1], out km) && km > 0)
                    {
                        rangeKm = Math.Min(km, 1000);
                        SaveSettings();
                    }
                    break;
            }
        }

        void LoadSettings()
        {
            MyIniParseResult result;
            if (!ini.TryParse(Me.CustomData, out result)) { setupError = "Custom Data: " + result.ToString(); return; }
            tag = ini.Get(Section, "Tag").ToString(tag);
            rangeKm = ini.Get(Section, "RangeKm").ToDouble(rangeKm);
            screenIndex = ini.Get(Section, "CockpitScreen").ToInt32(screenIndex);
            if (!ini.ContainsSection(Section)) SaveSettings();
        }

        void SaveSettings()
        {
            ini.Set(Section, "Tag", tag);
            ini.Set(Section, "RangeKm", rangeKm);
            ini.Set(Section, "CockpitScreen", screenIndex);
            ini.SetSectionComment(Section, " Radar Tech " + Version + ". Tag: the name tag for cameras and screens. RangeKm: how far each reading reaches.\n CockpitScreen: which screen of a tagged cockpit or other block with several screens (0 is the first).");
            Me.CustomData = ini.ToString();
        }

        void Refresh()
        {
            cameras.Clear();
            screens.Clear();
            controller = null;
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, b => b.IsSameConstructAs(Me));
            foreach (var b in blocks)
            {
                var cam = b as IMyCameraBlock;
                if (cam != null && b.CustomName.Contains(tag)) { cameras.Add(cam); cam.EnableRaycast = true; continue; }
                var sc = b as IMyShipController;
                if (sc != null && (controller == null || sc.IsUnderControl)) controller = sc;
                if (!b.CustomName.Contains(tag)) continue;
                var panel = b as IMyTextPanel;
                if (panel != null) { screens.Add(panel); continue; }
                var provider = b as IMyTextSurfaceProvider;
                if (provider != null && provider.SurfaceCount > 0)
                {
                    screens.Add(provider.GetSurface(Math.Max(0, Math.Min(screenIndex, provider.SurfaceCount - 1))));
                }
            }
            screens.Add(Me.GetSurface(0));
            foreach (var s in screens)
            {
                s.ContentType = ContentType.TEXT_AND_IMAGE;
                s.Font = "Monospace";
            }
            setupError = cameras.Count == 0 ? "No camera with " + tag + " in its name." : "";
        }

        /// <summary>One reading from the next camera that has charge for the whole range.</summary>
        void Measure(bool ping)
        {
            if (cameras.Count == 0) return;
            double range = rangeKm * 1000;
            for (int i = 0; i < cameras.Count; i++)
            {
                var cam = cameras[(nextCamera + i) % cameras.Count];
                if (!cam.IsFunctional || !cam.Enabled || !cam.CanScan(range)) continue;
                nextCamera = (nextCamera + i + 1) % cameras.Count;
                var info = cam.Raycast(range);
                last = info;
                hasReading = true;
                lastAt = DateTime.Now;
                lastCamera = cam.CustomName;
                lastDistance = 0;
                lastClosing = 0;
                if (!info.IsEmpty() && info.HitPosition.HasValue)
                {
                    Vector3D at = info.HitPosition.Value;
                    lastDistance = Vector3D.Distance(cam.GetPosition(), at);
                    Vector3D toward = Vector3D.Normalize(at - cam.GetPosition());
                    Vector3D mine = controller != null ? controller.GetShipVelocities().LinearVelocity : Vector3D.Zero;
                    lastClosing = Vector3D.Dot(mine - (Vector3D)info.Velocity, toward);
                }
                return;
            }
            if (ping) Echo("Cameras still charging: try again in a moment.");
        }

        string KindName(MyDetectedEntityType type)
        {
            switch (type)
            {
                case MyDetectedEntityType.Asteroid: return "Asteroid";
                case MyDetectedEntityType.Planet: return "Planet surface";
                case MyDetectedEntityType.LargeGrid: return "Large grid";
                case MyDetectedEntityType.SmallGrid: return "Small grid";
                case MyDetectedEntityType.CharacterHuman:
                case MyDetectedEntityType.CharacterOther: return "Character";
                case MyDetectedEntityType.Meteor: return "Meteor";
                case MyDetectedEntityType.FloatingObject: return "Object";
                default: return "Something";
            }
        }

        static string Dist(double m)
        {
            if (m < 1000) return m.ToString("0") + " m";
            return (m / 1000).ToString(m < 100000 ? "0.00" : "0") + " km";
        }

        static string Time(double s)
        {
            if (s >= 3600) return ((int)(s / 3600)) + "h " + ((int)(s % 3600 / 60)).ToString("00") + "m";
            return ((int)(s / 60)) + ":" + ((int)(s % 60)).ToString("00");
        }

        void Draw()
        {
            var sb = new StringBuilder();
            sb.Append("RADAR TECH  ").Append(auto ? "[AUTO]" : "[PING]").Append('\n');
            if (setupError.Length > 0)
            {
                sb.Append(setupError).Append('\n');
            }
            else if (!hasReading)
            {
                sb.Append("No reading yet.\nRun with \"ping\", or \"on\".\n");
            }
            else if (last.IsEmpty())
            {
                sb.Append("Nothing within ").Append(Dist(rangeKm * 1000)).Append('\n');
            }
            else
            {
                sb.Append(KindName(last.Type)).Append('\n');
                sb.Append(Dist(lastDistance)).Append('\n');
                if (lastClosing > 0.5) sb.Append("Closing ").Append(lastClosing.ToString("0")).Append(" m/s\n").Append(Time(lastDistance / lastClosing)).Append(" to contact\n");
                else if (lastClosing < -0.5) sb.Append("Opening ").Append((-lastClosing).ToString("0")).Append(" m/s\n");
                else sb.Append("Holding\n");
                Vector3D at = last.HitPosition.Value;
                sb.Append("GPS:Radar hit:").Append(at.X.ToString("0")).Append(':').Append(at.Y.ToString("0")).Append(':').Append(at.Z.ToString("0")).Append(":\n");
            }
            if (hasReading) sb.Append(((int)(DateTime.Now - lastAt).TotalSeconds)).Append(" s ago, ").Append(lastCamera).Append('\n');
            sb.Append("Range ").Append(Dist(rangeKm * 1000));
            if (cameras.Count > 0)
            {
                double charge = 0;
                foreach (var c in cameras) charge = Math.Max(charge, c.AvailableScanRange);
                sb.Append(", charge ").Append(Math.Min(100, charge / (rangeKm * 10)).ToString("0")).Append('%');
            }
            string text = sb.ToString();
            foreach (var s in screens) s.WriteText(text);
            Echo(text);
        }
        #endregion // Script
    }
}
