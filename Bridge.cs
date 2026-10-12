// Titanfall 2 VR / ProVolver Bridge, 0.3.0. See LICENSE.
// C# 5 / .NET Framework 4.x; runs outside the game. Uses the bundled standalone campaign script provider.
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace TF2Provolver
{
    public sealed class Event
    {
        public DateTime Time;
        public string Name;
        public string Weapon;
    }

    public static class Events
    {
        // Accept only our client-script protocol, never the bHaptics stream.
        const string Header = @"^\[(\d{4}-\d{2}-\d{2})\] \[(\d{2}:\d{2}:\d{2})\] \[SCRIPT CL\] \[info\] \[PVBRIDGE/1\] ";
        static readonly Regex Pattern = new Regex(Header +
            @"shot=(pistol|rifle|shotgun|titan|titan_rockets) weapon=([a-zA-Z0-9_]+)\r?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex ReadyPattern = new Regex(Header + @"ready version=0\.3\.0\r?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static bool Timestamp(Match m, out DateTime time)
        {
            time = DateTime.MinValue;
            return m.Success && DateTime.TryParseExact(m.Groups[1].Value + " " + m.Groups[2].Value,
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }
        public static bool Ready(string line, DateTime now)
        {
            DateTime time;
            return Timestamp(ReadyPattern.Match(line), out time) && Fresh(new Event { Time = time }, now);
        }
        public static Event Parse(string line)
        {
            Match m = Pattern.Match(line);
            DateTime time;
            if (!Timestamp(m, out time)) return null;
            string group = m.Groups[3].Value;
            // Preserve the tested scheduler's internal group identifiers.
            string name = "recoil_" + group + (group.StartsWith("titan", StringComparison.Ordinal) ? "" : "_r");
            return new Event { Time = time, Name = name, Weapon = m.Groups[4].Value };
        }

        public static bool Fresh(Event e, DateTime now)
        {
            // Source timestamps have only one-second precision.
            double age = (now - e.Time).TotalSeconds;
            return age >= -1 && age <= 2;
        }
    }

    public sealed class Tail : IDisposable
    {
        readonly FileStream stream;
        readonly byte[] buffer = new byte[262144];
        string partial = "";
        bool skipFragment;
        public Tail(string path)
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            Reset();
        }
        public void Reset()
        {
            partial = "";
            long length = stream.Length;
            skipFragment = false;
            if (length > 0)
            {
                stream.Position = length - 1;
                skipFragment = stream.ReadByte() != 10;
            }
            stream.Position = length; // Never replay pre-existing content.
        }
        public string[] Read()
        {
            long pending = stream.Length - stream.Position;
            if (pending < 0 || pending > buffer.Length)
            {
                Reset(); // Truncation or a large backlog: discard rather than replay.
                return new string[0];
            }
            if (pending == 0) return new string[0];
            int n = stream.Read(buffer, 0, (int)pending);
            string text = partial + Encoding.UTF8.GetString(buffer, 0, n);
            int end = text.LastIndexOf('\n');
            if (end < 0)
            {
                partial = text.Length < 65536 ? text : "";
                if (text.Length >= 65536) skipFragment = true;
                return new string[0];
            }
            partial = text.Substring(end + 1);
            string complete = text.Substring(0, end);
            if (skipFragment)
            {
                int first = complete.IndexOf('\n');
                complete = first < 0 ? "" : complete.Substring(first + 1);
                skipFragment = false;
            }
            return complete.Split('\n');
        }
        public void Dispose() { stream.Dispose(); }
    }

    public sealed class Pulse
    {
        public Event Source;
        public double Received;
        public double Recovery;
        public int Batch;
    }

    public sealed class Scheduler
    {
        readonly Queue<double> intervals = new Queue<double>();
        Pulse pending;
        double lastArrival = -10, lastOutput = -10, recovery = 0;
        string kind;
        public int Expired;
        public int Replaced;
        public void ClearPending() { pending = null; }
        public void Offer(Event e, int count, double now)
        {
            double dt = now - lastArrival;
            if (kind != e.Name || count > 1) intervals.Clear();
            else if (dt >= 0.025 && dt <= 0.3)
            {
                intervals.Enqueue(dt);
                while (intervals.Count > 6) intervals.Dequeue();
            }
            kind = e.Name;
            lastArrival = now;
            // Heavy group starts at full. Rifles/SMGs/Titans start with a
            // 60 ms recovery budget because this log does not identify a gun.
            double period = e.Name == "recoil_shotgun_r" ? 0.130 : 0.060;
            if (count > 1) period = 0.050; // Timing of batched shots is unknown.
            else if (intervals.Count >= 2)
            {
                double fastest = 1;
                foreach (double sample in intervals) fastest = Math.Min(fastest, sample);
                period = Math.Max(0.050, Math.Min(0.130, fastest - 0.008));
            }
            if (pending != null) Replaced++;
            pending = new Pulse { Source = e, Received = now, Recovery = period, Batch = count };
        }
        public Pulse Take(double now)
        {
            if (pending == null) return null;
            if (now - pending.Received > 0.040)
            {
                pending = null;
                Expired++;
                return null;
            }
            // A slightly early event waits briefly instead of disappearing.
            // Recovery belongs to the PREVIOUS kick, not the new kick.
            if (now + 0.0000001 < lastOutput + recovery) return null;
            Pulse result = pending;
            pending = null;
            lastOutput = now;
            recovery = result.Recovery;
            return result;
        }
    }

    public sealed class Driver
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string filename);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InitFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void KickFn(byte power, int channel);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte TempoFn(float seconds);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ActiveFn([MarshalAs(UnmanagedType.I1)] bool enabled);
        readonly IntPtr module;
        readonly InitFn init;
        readonly KickFn kick;
        readonly TempoFn tempo;
        readonly ActiveFn active;
        public Driver(string dll)
        {
            module = LoadLibraryW(Path.GetFullPath(dll));
            if (module == IntPtr.Zero) throw new Exception("Cannot load ProTube DLL. Windows error " + Marshal.GetLastWin32Error());
            init = (InitFn)Bind("InitPistol", typeof(InitFn));
            kick = (KickFn)Bind("KickChannel", typeof(KickFn));
            tempo = (TempoFn)Bind("TempoToKickPower", typeof(TempoFn));
            active = (ActiveFn)Bind("SetActive", typeof(ActiveFn));
            init(); // First paired ProTube device is assigned Pistol1 (channel 4).
        }
        Delegate Bind(string name, Type type)
        {
            IntPtr p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) throw new Exception("ProTube DLL export missing: " + name);
            return Marshal.GetDelegateForFunctionPointer(p, type);
        }
        public byte Kick(double recoverySeconds)
        {
            // Full power is permitted; the SDK caps it for the chosen recovery.
            byte power = tempo((float)recoverySeconds);
            if (power > 0) kick(power, 4);
            return power;
        }
        public void Stop() { active(false); }
        // Do not FreeLibrary: the SDK owns background threads until process exit.
    }

    public static class Program
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
        static volatile bool stopping;
        static StreamWriter audit;
        public static string ChooseLogDirectory(string argument, string saved, string standard)
        {
            // An explicitly supplied path takes priority, even if it needs correction.
            if (!String.IsNullOrWhiteSpace(argument)) return argument.Trim().Trim('"');
            if (!String.IsNullOrWhiteSpace(saved) && Directory.Exists(saved.Trim())) return saved.Trim();
            return standard;
        }
        static void Say(string text)
        {
            Console.WriteLine(text);
            if (audit != null) audit.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + text);
        }
        static string Latest(string directory)
        {
            string result = null;
            DateTime stamp = DateTime.MinValue;
            foreach (string p in Directory.GetFiles(directory, "nslog*.txt"))
            {
                DateTime t = File.GetLastWriteTimeUtc(p);
                if (t > stamp) { result = p; stamp = t; }
            }
            return result;
        }
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") { SelfTest(); return 0; }
            Driver driver = null;
            Tail tail = null;
            Mutex mutex = null;
            bool acquired = false;
            bool timerSet = false;
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8)
                    throw new Exception("Use 64-bit Windows for the hardware bridge.");
                mutex = new Mutex(false, @"Local\TF2VR_Provolver_Test_01");
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new Exception("Another bridge/test window is already running. Close it first.");
                string mode = args.Length == 0 ? "--monitor" : args[0];
                if (mode != "--test" && mode != "--bridge" && mode != "--monitor") throw new Exception("Unknown mode.");
                audit = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge-session.txt"), false);
                audit.AutoFlush = false;
                Say("Titanfall 2 VR ProVolver Bridge 0.3.0 | Pistol1/channel 4 | full-power slow shots, adaptive rapid fire");
                Say("Experimental log bridge: timing and physical recoil need testing on your PC.");
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; stopping = true; };
                if (mode != "--monitor")
                {
                    string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ForceTubeVR_API_x64.dll");
                    if (!File.Exists(dll)) throw new Exception("ProTube DLL missing. Run GET_PROTUBE_DLL.cmd once, then restart START.cmd. See README.md for manual setup.");
                    driver = new Driver(dll);
                    Say("ProTube connection started. Turn on your paired ProVolver; close ProTube Companion first.");
                    Say("Install the bundled Titanfall2VR.ProVolver game mod. bHaptics is not required.");
                }
                if (mode == "--test")
                {
                    Say("Wait for the ProVolver connection vibration, then press Enter to request ONE kick.");
                    Say("Press Q then Enter to cancel. If it has not connected, do not press Enter yet.");
                    string answer = Console.ReadLine();
                    if (!stopping && answer != null && answer.Trim().Length == 0)
                    {
                        Say("Requested one FULL-POWER kick at " + driver.Kick(0.130) + "/255. This does not confirm hardware receipt.");
                        Thread.Sleep(1000); // Give the SDK's async Bluetooth sender time before process exit.
                    }
                    return 0;
                }
                string pathFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs-path.txt");
                string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Steam\steamapps\common\Titanfall2\TF2VR\logs");
                string saved = File.Exists(pathFile) ? File.ReadAllText(pathFile).Trim() : null;
                string directory = ChooseLogDirectory(args.Length > 1 ? args[1] : null, saved, standard);
                if (!Directory.Exists(directory))
                {
                    Say("Paste the full TF2VR\\logs folder path, then press Enter:");
                    directory = (Console.ReadLine() ?? "").Trim().Trim('"');
                }
                if (!Directory.Exists(directory)) throw new Exception("Log folder not found: " + directory);
                try { File.WriteAllText(pathFile, directory); }
                catch (IOException) { Say("Could not save log folder; pass it again next run."); }
                catch (UnauthorizedAccessException) { Say("Could not save log folder; pass it again next run."); }
                Say("Watching " + directory);
                Say(mode == "--monitor" ? "MONITOR ONLY: no ProTube DLL loaded, no kicks." : "LIVE BRIDGE: standalone pilot and Titan recoil enabled on your single ProVolver.");
                Say("Existing log entries are skipped. Fire a few separated shots in the campaign. Ctrl+C stops.");
                timerSet = timeBeginPeriod(1) == 0;
                Stopwatch clock = Stopwatch.StartNew();
                double lastScan = -2, lastReport = 0, lastLoop = 0;
                Scheduler scheduler = new Scheduler();
                string current = null;
                int seen = 0, sent = 0, discarded = 0, batched = 0, stalls = 0;
                bool providerSeen = false;
                while (!stopping)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    if (tail != null && now - lastLoop > 0.25)
                    {
                        tail.Reset(); // A delayed reader must not catch up with queued shots.
                        scheduler.ClearPending();
                        stalls++;
                        Say("Reader paused over 250 ms; skipped pending events.");
                    }
                    lastLoop = now;
                    if (now - lastScan >= 1)
                    {
                        lastScan = now;
                        string latest = Latest(directory);
                        if (latest != null && !String.Equals(latest, current, StringComparison.OrdinalIgnoreCase))
                        {
                            if (tail != null) tail.Dispose();
                            tail = new Tail(latest);
                            scheduler.ClearPending();
                            current = latest;
                            providerSeen = false;
                            Say("Attached at end of " + Path.GetFileName(current));
                        }
                    }
                    if (tail != null)
                    {
                        Event selected = null;
                        int count = 0;
                        DateTime wallNow = DateTime.Now;
                        foreach (string line in tail.Read())
                        {
                            if (!providerSeen && Events.Ready(line, wallNow))
                            {
                                providerSeen = true;
                                Say("Standalone game provider detected (no bHaptics dependency).");
                            }
                            Event e = Events.Parse(line);
                            if (e == null) continue;
                            seen++;
                            if (!Events.Fresh(e, wallNow)) { discarded++; continue; }
                            selected = e;
                            count++;
                        }
                        if (selected != null)
                        {
                            // A file flush can deliver many shots together. Keep at most one;
                            // original sub-second timing cannot be recovered from this log.
                            batched += Math.Max(0, count - 1);
                            scheduler.Offer(selected, count, clock.Elapsed.TotalSeconds);
                        }
                    }
                    Pulse pulse = scheduler.Take(clock.Elapsed.TotalSeconds);
                    if (pulse != null)
                    {
                        double sdkStart = clock.Elapsed.TotalSeconds;
                        byte power = driver == null ? (byte)0 : driver.Kick(pulse.Recovery);
                        double sdkMs = (clock.Elapsed.TotalSeconds - sdkStart) * 1000;
                        sent++;
                        audit.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                            (driver == null ? " EVENT " : " KICK REQUEST ") + pulse.Source.Name +
                            " | weapon=" + pulse.Source.Weapon + " | power=" + power + " | recoveryMs=" + (int)(pulse.Recovery * 1000) +
                            " | waitMs=" + (int)((clock.Elapsed.TotalSeconds - pulse.Received) * 1000) +
                            " | sdkMs=" + sdkMs.ToString("F1", CultureInfo.InvariantCulture) +
                            " | batch=" + pulse.Batch + " | source=" + pulse.Source.Time.ToString("HH:mm:ss"));
                    }
                    if (now - lastReport >= 1)
                    {
                        lastReport = now;
                        Say("Shots=" + seen + " outputs=" + sent + " grouped=" + batched +
                            " stale=" + discarded + " expired=" + scheduler.Expired +
                            " replaced=" + scheduler.Replaced + " readerPauses=" + stalls);
                        audit.Flush();
                    }
                    Thread.Sleep(2);
                }
                Say("Stopped. Matching events=" + seen + "; outputs=" + sent + "; stale=" + discarded +
                    "; grouped=" + batched + "; expired=" + scheduler.Expired + "; replaced=" + scheduler.Replaced + "; readerPauses=" + stalls);
                return 0;
            }
            catch (Exception e) { Say("ERROR: " + e.Message); return 1; }
            finally
            {
                if (tail != null) tail.Dispose();
                if (timerSet) timeEndPeriod(1);
                if (driver != null) { try { driver.Stop(); } catch { } }
                if (audit != null) { audit.Dispose(); audit = null; }
                if (mutex != null) { if (acquired) mutex.ReleaseMutex(); mutex.Dispose(); }
            }
        }

        static void Check(bool ok, string name)
        {
            if (!ok) throw new Exception("TEST FAILED: " + name);
            Console.WriteLine("PASS " + name);
        }
        public static void SelfTest()
        {
            string tempFolder = Path.GetTempPath();
            Check(ChooseLogDirectory("explicit-folder", tempFolder, "default-folder") == "explicit-folder", "explicit log folder wins");
            Check(ChooseLogDirectory(null, tempFolder, "default-folder") == tempFolder.Trim(), "reuse valid saved log folder");
            Check(ChooseLogDirectory(null, "nonexistent-tf2vr-folder-7a50", "default-folder") == "default-folder", "ignore missing saved log folder");
            string sample = "[2026-10-12] [17:02:48] [SCRIPT CL] [info] [PVBRIDGE/1] shot=shotgun weapon=mp_weapon_shotgun";
            Event e = Events.Parse(sample);
            Check(e != null && e.Name == "recoil_shotgun_r" && e.Weapon == "mp_weapon_shotgun", "standalone client protocol");
            Check(Events.Parse(sample + "\r") != null, "Windows CRLF log record");
            Check(Events.Parse(sample.Replace("shot=shotgun", "shot=damage")) == null, "ignore unsupported effects");
            Check(Events.Parse(sample.Replace("[SCRIPT CL]", "[SCRIPT SV]")) == null, "ignore server messages");
            Check(Events.Parse(sample.Replace("PVBRIDGE/1", "PVBRIDGE/2")) == null, "reject unknown protocol version");
            Check(Events.Parse(sample + " extra") == null, "reject malformed event suffix");
            Check(Events.Parse(sample.Replace("2026-10-12", "2026-99-99")) == null, "reject invalid timestamp");
            Check(Events.Parse("[2026-10-12] [17:02:48] [BHAPTICS] [info] [DEBUG] Play 'recoil_shotgun_r' (intensity 1.00)") == null,
                "ignore bHaptics even when it runs alongside provider");
            Check(Events.Parse(sample.Replace("shot=shotgun", "shot=titan")).Name == "recoil_titan", "Titan event");
            Check(Events.Parse(sample.Replace("shot=shotgun", "shot=titan_rockets")).Name == "recoil_titan_rockets", "Titan rocket event");
            string ready = "[2026-10-12] [17:02:48] [SCRIPT CL] [info] [PVBRIDGE/1] ready version=0.3.0";
            Check(Events.Ready(ready, e.Time) && Events.Parse(ready) == null, "heartbeat identifies provider without recoil");
            Check(!Events.Ready(ready, e.Time.AddSeconds(4)), "ignore stale heartbeat");
            Check(Events.Fresh(e, e.Time.AddMilliseconds(1300)), "second-resolution timestamp");
            Check(!Events.Fresh(e, e.Time.AddSeconds(3)), "drop stale shots");
            Check(!Events.Fresh(e, e.Time.AddSeconds(-3)), "drop future timestamp");
            Event rifle = Events.Parse(sample.Replace("shot=shotgun", "shot=rifle"));
            Scheduler rapid = new Scheduler();
            int rapidSent = 0;
            // 1000 RPM, with five milliseconds of alternating arrival jitter.
            int offered = 0;
            for (int ms = 0; ms < 1300; ms++)
            {
                int nextMs = offered * 60 + (offered % 2 == 1 ? -5 : 0);
                if (offered < 20 && ms == nextMs) { rapid.Offer(rifle, 1, ms / 1000.0); offered++; }
                if (rapid.Take(ms / 1000.0) != null) rapidSent++;
            }
            Check(rapidSent == 20 && rapid.Expired == 0, "1000 RPM with arrival jitter: 20 of 20 outputs");
            Scheduler heavy = new Scheduler();
            heavy.Offer(e, 1, 0);
            Check(Math.Abs(heavy.Take(0).Recovery - 0.130) < 0.000001, "full-power heavy-shot recovery");
            heavy.Offer(rifle, 1, 0.11);
            Check(heavy.Take(0.11) == null && heavy.Take(0.131) != null, "respect previous heavy kick when switching to rapid");
            Scheduler late = new Scheduler();
            late.Offer(rifle, 1, 0);
            Check(late.Take(0.050) == null && late.Expired == 1, "no output from expired pending pulse");
            Scheduler grouped = new Scheduler();
            grouped.Offer(rifle, 4, 0);
            Pulse groupedPulse = grouped.Take(0);
            Check(groupedPulse != null && grouped.Take(0.1) == null, "grouped log writes never create a catch-up burst");
            Scheduler slow = new Scheduler();
            slow.Offer(rifle, 1, 0); slow.Take(0);
            slow.Offer(rifle, 1, 0.2); slow.Take(0.2);
            slow.Offer(rifle, 1, 0.4);
            Check(Math.Abs(slow.Take(0.4).Recovery - 0.130) < 0.000001, "slow rifle shots reach full-power recovery");
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, sample + "\n", new UTF8Encoding(false));
                using (Tail t = new Tail(path))
                {
                    Check(t.Read().Length == 0, "skip historical log");
                    File.AppendAllText(path, sample.Substring(0, 40));
                    Check(t.Read().Length == 0, "hold partial line");
                    File.AppendAllText(path, sample.Substring(40) + "\n");
                    string[] lines = t.Read();
                    Check(lines.Length == 1 && Events.Parse(lines[0]) != null, "complete split write");
                    Check(t.Read().Length == 0, "no duplicate replay");
                    File.AppendAllText(path, sample + "\n" + sample + "\n");
                    Check(t.Read().Length == 2, "identify batched log writes");
                    File.WriteAllText(path, "reset\n");
                    Check(t.Read().Length == 0, "skip truncated log");
                    File.AppendAllText(path, sample + "\n");
                    Check(t.Read().Length == 1, "resume after truncation");
                }
                File.WriteAllText(path, "old partial");
                using (Tail t = new Tail(path))
                {
                    File.AppendAllText(path, " finish\n" + sample + "\n");
                    string[] lines = t.Read();
                    Check(lines.Length == 1 && Events.Parse(lines[0]) != null, "discard pre-attach partial line");
                    File.AppendAllText(path, new string('x', 300000));
                    Check(t.Read().Length == 0, "discard oversized backlog");
                }
            }
            finally { File.Delete(path); }
            Console.WriteLine("All parser and live-tail tests passed. Hardware is NOT tested.");
        }
    }
}
