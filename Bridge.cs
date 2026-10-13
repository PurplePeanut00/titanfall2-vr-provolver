// Titanfall 2 VR / ProVolver Bridge, 0.3.3. See LICENSE.
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
        public int Charge = -1;
    }

    public static class Events
    {
        // Accept only our client-script protocol, never the bHaptics stream.
        const string Header = @"^\[(\d{4}-\d{2}-\d{2})\] \[(\d{2}:\d{2}:\d{2})\] \[SCRIPT CL\] \[info\] \[PVBRIDGE/1\] ";
        static readonly Regex Pattern = new Regex(Header +
            @"shot=(pistol|rifle|shotgun|titan|titan_rockets) weapon=([a-zA-Z0-9_]+)\r?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex ReadyPattern = new Regex(Header + @"ready version=0\.3\.(?:0|2|3)\r?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static readonly Regex ChargePattern = new Regex(Header +
            @"charge=(100|[0-9]{1,2}) weapon=(mp_weapon_defender|mp_titanweapon_sniper)\r?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        public static Event ParseCharge(string line)
        {
            Match m = ChargePattern.Match(line);
            DateTime time;
            if (!Timestamp(m, out time)) return null;
            return new Event { Time = time, Name = "charge", Weapon = m.Groups[4].Value,
                Charge = Int32.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) };
        }

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
            // Include the whole final second: a 2.9 s coarse age may be only 1.9 s old.
            return age >= -1 && age < 3;
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
        public bool FullPower;
    }

    public sealed class Scheduler
    {
        readonly Queue<double> intervals = new Queue<double>();
        Pulse pending;
        double lastArrival = -10, lastOutput = -10, recovery = 0;
        string kind, weapon;
        public int Expired;
        public int Replaced;
        public void ClearPending() { pending = null; }
        public void Offer(Event e, int count, double now)
        {
            double dt = now - lastArrival;
            if (kind != e.Name || weapon != e.Weapon || count > 1 || dt > 0.3) intervals.Clear();
            else if (dt >= 0.025 && dt <= 0.3)
            {
                intervals.Enqueue(dt);
                while (intervals.Count > 6) intervals.Dequeue();
            }
            kind = e.Name;
            weapon = e.Weapon;
            lastArrival = now;
            // Heavy groups begin at 130 ms; other groups begin at 60 ms.
            // Actual shotguns always retain the full-power recovery budget.
            double period = e.Name == "recoil_shotgun_r" ? 0.130 : 0.060;
            bool fullPower = RecoilPower.IsShotgun(e.Weapon);
            if (fullPower) period = 0.130;
            else if (count > 1) period = 0.050; // Timing of batched shots is unknown.
            else if (intervals.Count >= 2)
            {
                double fastest = 1;
                foreach (double sample in intervals) fastest = Math.Min(fastest, sample);
                period = Math.Max(0.050, Math.Min(0.130, fastest - 0.008));
            }
            if (pending != null) Replaced++;
            pending = new Pulse { Source = e, Received = now, Recovery = period, Batch = count, FullPower = fullPower };
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

    // A charge sample is a short-lived state update, never a queue of effects.
    // Hardware requests last 100 ms. No infinite-duration rumble is used.
    public sealed class ChargeEnvelope
    {
        public const float Duration = 0.100f;
        int desired, sent;
        double received = -10, lastOutput = -10;
        public int Power { get { return sent; } }
        public void Offer(Event e, double now)
        {
            if (e.Charge < 0 || e.Charge > 100) return;
            desired = e.Charge == 0 ? 0 : 32 + (223 * e.Charge / 100);
            received = now;
        }
        public void Cancel() { desired = 0; }
        public int Take(double now)
        {
            // Stop refreshing after a missed source update (including pause/load).
            if (now - received > 0.180) desired = 0;
            if (desired == 0)
            {
                if (sent == 0) return -1;
                sent = 0;
                lastOutput = now;
                return 0;
            }
            // Up to 20 updates/second, including a refresh at full held charge.
            if (now - lastOutput < 0.050) return -1;
            lastOutput = now;
            sent = desired;
            return sent;
        }
    }

    public static class RecoilPower
    {
        // The provider's legacy "shotgun" group also includes snipers and launchers.
        // Only actual shotguns bypass adaptive SDK power.
        public static bool IsShotgun(string weapon)
        {
            return weapon == "mp_weapon_shotgun" || weapon == "mp_weapon_mastiff"
                || weapon == "mp_weapon_shotgun_pistol" || weapon == "mp_weapon_shotgun_doublebarrel";
        }
        public static byte Select(bool fullPower, double recovery, Func<float, byte> adaptive)
        {
            return fullPower ? (byte)255 : adaptive((float)recovery);
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
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RumbleFn(byte power, float seconds, int channel);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ActiveFn([MarshalAs(UnmanagedType.I1)] bool enabled);
        readonly IntPtr module;
        readonly InitFn init;
        readonly KickFn kick;
        readonly TempoFn tempo;
        readonly RumbleFn rumble;
        readonly ActiveFn active;
        public Driver(string dll)
        {
            module = LoadLibraryW(Path.GetFullPath(dll));
            if (module == IntPtr.Zero) throw new Exception("Cannot load ProTube DLL. Windows error " + Marshal.GetLastWin32Error());
            init = (InitFn)Bind("InitPistol", typeof(InitFn));
            kick = (KickFn)Bind("KickChannel", typeof(KickFn));
            tempo = (TempoFn)Bind("TempoToKickPower", typeof(TempoFn));
            rumble = (RumbleFn)Bind("RumbleChannel", typeof(RumbleFn));
            active = (ActiveFn)Bind("SetActive", typeof(ActiveFn));
            init(); // First paired ProTube device is assigned Pistol1 (channel 4).
        }
        Delegate Bind(string name, Type type)
        {
            IntPtr p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) throw new Exception("ProTube DLL export missing: " + name);
            return Marshal.GetDelegateForFunctionPointer(p, type);
        }
        public byte Kick(double recoverySeconds, bool fullPower)
        {
            // Explicit 255/255 for shotguns; retain SDK adaptation for other weapons.
            byte power = RecoilPower.Select(fullPower, recoverySeconds, delegate(float seconds) { return tempo(seconds); });
            if (power > 0) kick(power, 4);
            return power;
        }
        public void Rumble(byte power) { rumble(power, ChargeEnvelope.Duration, 4); }
        public void Stop()
        {
            try { rumble(0, 0.010f, 4); } finally { active(false); }
        }
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
                if (mode != "--test" && mode != "--bridge" && mode != "--monitor" && mode != "--charge-test") throw new Exception("Unknown mode.");
                audit = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge-session.txt"), false);
                audit.AutoFlush = false;
                Say("Titanfall 2 VR ProVolver Bridge 0.3.3 | Pistol1/channel 4 | full-power slow shots, adaptive rapid fire");
                Say("Experimental log bridge: timing and physical recoil need testing on your PC.");
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; stopping = true; };
                if (mode != "--monitor")
                {
                    string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ForceTubeVR_API_x64.dll");
                    if (!File.Exists(dll)) throw new Exception("ProTube DLL missing. Run GET_PROTUBE_DLL.cmd once, then restart START.cmd. See README.md for manual setup.");
                    driver = new Driver(dll);
                    Say("ProTube connection started. Turn on your paired ProVolver; close ProTube Companion first.");
                    Say("Standalone recoil mode.");
                }
                if (mode == "--test")
                {
                    Say("Wait for the ProVolver connection vibration, then press Enter to request ONE kick.");
                    Say("Press Q then Enter to cancel. If it has not connected, do not press Enter yet.");
                    string answer = Console.ReadLine();
                    if (!stopping && answer != null && answer.Trim().Length == 0)
                    {
                        Say("Requested one FULL-POWER kick at " + driver.Kick(0.130, true) + "/255. This does not confirm hardware receipt.");
                        Thread.Sleep(1000); // Give the SDK's async Bluetooth sender time before process exit.
                    }
                    return 0;
                }
                if (mode == "--charge-test")
                {
                    Say("Wait for connection, then Enter for a TWO-SECOND rising rumble (no recoil kick). Q then Enter cancels.");
                    string answer = Console.ReadLine();
                    if (!stopping && answer != null && answer.Trim().Length == 0)
                    {
                        Stopwatch testClock = Stopwatch.StartNew();
                        while (!stopping && testClock.Elapsed.TotalSeconds < 2.0)
                        {
                            byte power = (byte)Math.Min(255, 32 + (223 * testClock.Elapsed.TotalSeconds / 1.9));
                            driver.Rumble(power);
                            Thread.Sleep(50);
                        }
                        driver.Rumble(0);
                        Thread.Sleep(150);
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
                Say("Existing log entries are skipped. Launch VR normally if needed, then fire in the campaign. Ctrl+C stops.");
                timerSet = timeBeginPeriod(1) == 0;
                Stopwatch clock = Stopwatch.StartNew();
                double lastScan = -2, lastReport = 0, lastLoop = 0;
                Scheduler scheduler = new Scheduler();
                ChargeEnvelope charge = new ChargeEnvelope();
                int chargeSamples = 0, lastLoggedCharge = 0;
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
                        charge.Cancel();
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
                            charge.Cancel();
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
                                Say("Standalone game provider detected.");
                            }
                            Event chargeEvent = Events.ParseCharge(line);
                            if (chargeEvent != null)
                            {
                                if (Events.Fresh(chargeEvent, wallNow))
                                {
                                    charge.Offer(chargeEvent, clock.Elapsed.TotalSeconds);
                                    chargeSamples++;
                                }
                                continue;
                            }
                            Event e = Events.Parse(line);
                            if (e == null) continue;
                            seen++;
                            if (!Events.Fresh(e, wallNow))
                            {
                                discarded++;
                                audit.WriteLine(wallNow.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                                    " STALE SHOT | weapon=" + e.Weapon + " | ageSeconds=" +
                                    (wallNow - e.Time).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
                                continue;
                            }
                            charge.Cancel(); // A shot ends charging; never invent a shot from charge release.
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
                    int rumblePower = charge.Take(clock.Elapsed.TotalSeconds);
                    if (rumblePower >= 0)
                    {
                        if (driver != null) driver.Rumble((byte)rumblePower);
                        if (rumblePower != lastLoggedCharge)
                        {
                            audit.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                                (driver == null ? " CHARGE MONITOR " : " RUMBLE REQUEST ") + rumblePower + "/255");
                            lastLoggedCharge = rumblePower;
                        }
                    }
                    Pulse pulse = scheduler.Take(clock.Elapsed.TotalSeconds);
                    if (pulse != null)
                    {
                        double sdkStart = clock.Elapsed.TotalSeconds;
                        byte power = driver == null ? (byte)0 : driver.Kick(pulse.Recovery, pulse.FullPower);
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
                            " replaced=" + scheduler.Replaced + " readerPauses=" + stalls +
                            " chargeSamples=" + chargeSamples + " chargePower=" + charge.Power);
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
            Event rifle = Events.Parse(sample.Replace("shot=shotgun", "shot=rifle").Replace("mp_weapon_shotgun", "mp_weapon_r97"));
            string chargeLine = "[2026-10-12] [17:02:48] [SCRIPT CL] [info] [PVBRIDGE/1] charge=50 weapon=mp_titanweapon_sniper";
            Event half = Events.ParseCharge(chargeLine);
            Check(half != null && half.Charge == 50, "parse Plasma Railgun charge");
            Check(Events.ParseCharge(chargeLine.Replace("mp_titanweapon_sniper", "mp_weapon_defender")) != null, "parse Charge Rifle charge");
            Check(Events.Parse(chargeLine) == null, "charge update never becomes a recoil event");
            Check(Events.ParseCharge(chargeLine.Replace("charge=50", "charge=101")) == null, "reject excessive charge value");
            Check(Events.ParseCharge(chargeLine.Replace("charge=50", "charge=-1")) == null, "reject negative charge value");
            Check(Events.ParseCharge(chargeLine.Replace("mp_titanweapon_sniper", "mp_weapon_car")) == null, "ignore unrelated charge weapons");
            Check(Events.ParseCharge(chargeLine.Replace("SCRIPT CL", "SCRIPT SV")) == null, "ignore server charge messages");
            Check(Events.Ready(ready.Replace("0.3.0", "0.3.3"), e.Time), "recognize updated provider heartbeat");
            ChargeEnvelope envelope = new ChargeEnvelope();
            envelope.Offer(half, 0);
            Check(envelope.Take(0) == 143, "half charge gives intermediate rumble");
            Check(envelope.Take(0.020) == -1, "rumble refresh rate is limited");
            Event full = Events.ParseCharge(chargeLine.Replace("charge=50", "charge=100"));
            envelope.Offer(full, 0.050);
            Check(envelope.Take(0.051) == 255, "full charge requests maximum rumble");
            Check(envelope.Take(0.110) == 255, "held charge refreshes a finite pulse");
            Check(envelope.Take(0.240) == 0 && envelope.Take(0.241) == -1, "missing source updates stop rumble once");
            envelope.Offer(full, 1); envelope.Take(1);
            envelope.Offer(Events.ParseCharge(chargeLine.Replace("charge=50", "charge=0")), 1.010);
            Check(envelope.Take(1.010) == 0, "charge release stops immediately despite output rate limit");
            envelope.Offer(full, 2); envelope.Take(2); envelope.Cancel();
            Check(envelope.Take(2.001) == 0, "shot, weapon switch or log reset cancels rumble");
            envelope.Offer(half, 3); envelope.Offer(full, 3); envelope.Cancel();
            Check(envelope.Take(3) == -1, "batched ramp followed by cancel is not replayed");
            Check(ChargeEnvelope.Duration > 0 && ChargeEnvelope.Duration <= 0.1f, "all rumble pulses are finite and short");
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
            Check(Events.Fresh(e, e.Time.AddMilliseconds(2999)), "freshness includes coarse timestamp final second");
            Check(Events.Ready(ready.Replace("0.3.0", "0.3.3"), e.Time), "recognize fixed provider heartbeat");
            int adaptiveCalls = 0;
            Func<float, byte> adaptive = delegate(float seconds) { adaptiveCalls++; return 123; };
            Check(RecoilPower.Select(true, 0.050, adaptive) == 255 && adaptiveCalls == 0,
                "shotgun bypasses adaptive SDK and requests 255/255");
            Check(RecoilPower.Select(false, 0.060, adaptive) == 123 && adaptiveCalls == 1,
                "other weapons retain adaptive SDK power");
            foreach (string shotgun in new string[] { "mp_weapon_shotgun", "mp_weapon_mastiff",
                "mp_weapon_shotgun_pistol", "mp_weapon_shotgun_doublebarrel" })
            {
                Event shot = new Event { Name = e.Name, Weapon = shotgun, Time = e.Time };
                Scheduler shotgunSchedule = new Scheduler();
                shotgunSchedule.Offer(shot, 1, 0);
                Pulse first = shotgunSchedule.Take(0);
                Check(first != null && first.FullPower && first.Recovery == 0.130,
                    "first shotgun is full power: " + shotgun);
                shotgunSchedule.Offer(shot, 4, 0.2);
                Pulse batch = shotgunSchedule.Take(0.2);
                Check(batch != null && batch.FullPower && batch.Recovery == 0.130,
                    "batched shotgun stays full power: " + shotgun);
                shotgunSchedule.Offer(shot, 1, 0.4); shotgunSchedule.Take(0.4);
                shotgunSchedule.Offer(shot, 1, 0.6);
                Check(shotgunSchedule.Take(0.6).FullPower,
                    "cadence cannot reduce shotgun power: " + shotgun);
            }
            Check(!RecoilPower.IsShotgun("mp_weapon_defender") && !RecoilPower.IsShotgun("mp_weapon_epg")
                && !RecoilPower.IsShotgun("mp_weapon_sniper") && !RecoilPower.IsShotgun("mp_titanweapon_sniper"),
                "heavy-group charge and plasma weapons are not mistaken for shotguns");
            Scheduler cadence = new Scheduler();
            cadence.Offer(rifle, 1, 0); cadence.Take(0);
            cadence.Offer(rifle, 1, 0.2); cadence.Take(0.2);
            cadence.Offer(rifle, 1, 0.4); cadence.Take(0.4);
            Event newRifle = new Event { Name = rifle.Name, Weapon = "mp_weapon_g2", Time = e.Time };
            cadence.Offer(newRifle, 1, 0.6);
            Check(cadence.Take(0.6).Recovery == 0.060, "same-group weapon switch resets cadence");
            cadence.Offer(newRifle, 1, 0.8); cadence.Take(0.8);
            cadence.Offer(newRifle, 1, 1.0); cadence.Take(1.0);
            cadence.Offer(newRifle, 1, 2.0);
            Check(cadence.Take(2.0).Recovery == 0.060, "idle first shot uses fresh cadence");
            foreach (string chargedWeapon in new string[] { "mp_weapon_defender", "mp_titanweapon_sniper" })
            {
                ChargeEnvelope charged = new ChargeEnvelope();
                charged.Offer(new Event { Charge = 100, Weapon = chargedWeapon }, 0);
                Check(charged.Take(0) == 255, "first charge sample reaches full rumble: " + chargedWeapon);
                charged.Cancel();
                Scheduler discharge = new Scheduler();
                discharge.Offer(new Event { Name = "recoil_titan", Weapon = chargedWeapon }, 1, 0.01);
                Check(charged.Take(0.01) == 0 && discharge.Take(0.01) != null,
                    "charge stop does not swallow first discharge: " + chargedWeapon);
            }
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
