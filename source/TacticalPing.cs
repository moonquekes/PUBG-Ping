// Independent desktop learning runtime. Bundled images/audio are extracted locally from PUBG.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TacticalPing
{
    internal sealed class Signal
    {
        internal string Id, Label, Texture, WorldTexture, SoundKind;
        internal Color Color;
        internal Signal(SignalSpec spec)
        {
            Id = spec.Id; Label = spec.Label; Texture = spec.Texture; WorldTexture = spec.WorldTexture;
            SoundKind = spec.SoundKind; Color = (Color)ColorConverter.ConvertFromString(spec.Color);
        }
    }

    [DataContract] internal sealed class SignalSpec
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Label { get; set; }
        [DataMember] public string Texture { get; set; }
        [DataMember] public string WorldTexture { get; set; }
        [DataMember] public string SoundKind { get; set; }
        [DataMember] public string Color { get; set; }
        [DataMember] public string PageTitle { get; set; }
        [DataMember] public int Page { get; set; }
        [DataMember] public int Slot { get; set; }
    }
    internal static class Catalog
    {
        internal static readonly string[] PageNames;
        internal static readonly Signal[][] Pages;
        static Catalog()
        {
            string root = Path.Combine(App.BaseDir, "assets", "game");
            string file = Path.Combine(root, "catalog.json");
            if (!File.Exists(file)) throw new FileNotFoundException("缺少本地素材目录。请保留整个文件夹，不要单独移动 EXE。", file);
            SignalSpec[] specs;
            using (var stream = File.OpenRead(file)) specs = (SignalSpec[])new DataContractJsonSerializer(typeof(SignalSpec[])).ReadObject(stream);
            if (specs == null || specs.Length != 24 || specs.Select(s => s.Id).Distinct().Count() != 24)
                throw new InvalidDataException("素材目录必须包含 24 个不同信号。");
            Pages = new Signal[3][]; PageNames = new string[3];
            for (int page = 0; page < 3; page++)
            {
                var items = specs.Where(s => s.Page == page).OrderBy(s => s.Slot).ToArray();
                if (items.Length != 8 || !items.Select(s => s.Slot).SequenceEqual(Enumerable.Range(0, 8)))
                    throw new InvalidDataException("原始轮盘槽位数据无效。");
                foreach (var item in items)
                {
                    foreach (string sprite in new[] { item.Texture, item.WorldTexture }.Where(s => !string.IsNullOrEmpty(s)))
                    {
                        string path = Logic.CustomPath(root, sprite);
                        if (path == null || !File.Exists(path)) throw new FileNotFoundException("缺少原始图标：" + sprite);
                    }
                    if (!new[] { "normal", "warning", "notify", "important" }.Contains(item.SoundKind)) throw new InvalidDataException("音效分类无效。");
                }
                Pages[page] = items.Select(s => new Signal(s)).ToArray(); PageNames[page] = items[0].PageTitle;
            }
        }
        internal static IEnumerable<Signal> All { get { return Pages.SelectMany(p => p); } }
        internal static Signal Find(string id) { return All.FirstOrDefault(s => s.Id == id); }
    }

    [DataContract] public sealed class AssetOverride
    {
        [DataMember] public string Id = "";
        [DataMember] public string Image = "";
        [DataMember] public string Sound = "";
    }
    [DataContract] public sealed class Settings
    {
        [DataMember] public int Trigger = 119; // F8; middle mouse is 4.
        [DataMember] public bool Enabled = true;
        [DataMember] public bool Sound = true;
        [DataMember] public bool AvoidFullscreen = true;
        [DataMember] public double Radius = 182;
        [DataMember] public double Duration = 2.8;
        [DataMember] public int LastPage = 2; // Emoji page. The wheel opens here until the user changes it.
        [DataMember] public string OnClose = "ask"; // ask, tray, or exit.
        [DataMember] public List<AssetOverride> Assets = new List<AssetOverride>();
        internal void Normalize()
        {
            if (Trigger != 4 && (Trigger < 117 || Trigger > 123)) Trigger = 119;
            Radius = Logic.Clamp(Radius, 145, 220, 182);
            Duration = Logic.Clamp(Duration, 1, 5, 2.8);
            LastPage = LastPage >= 0 && LastPage < 3 ? LastPage : 2;
            if (OnClose != "tray" && OnClose != "exit") OnClose = "ask";
            if (Assets == null) Assets = new List<AssetOverride>();
            Assets = Assets.Where(a => a != null && Catalog.Find(a.Id) != null)
                .GroupBy(a => a.Id).Select(g => g.First()).Take(24).ToList();
            foreach (AssetOverride a in Assets)
            {
                if (a.Image != a.Id + ".png") a.Image = "";
                if (a.Sound != a.Id + ".wav") a.Sound = "";
            }
        }
    }

    internal static class Logic
    {
        internal static double Clamp(double n, double lo, double hi, double fallback)
        { return double.IsNaN(n) || double.IsInfinity(n) ? fallback : Math.Max(lo, Math.Min(hi, n)); }
        internal static int Pick(double x, double y)
        {
            if (x * x + y * y < 34 * 34) return -1;
            double angle = (Math.Atan2(y, x) * 180 / Math.PI + 112.5 + 360) % 360;
            return (int)(angle / 45) % 8;
        }
        internal static Point Fit(Point p, double width, double height, double radius)
        {
            return new Point(Math.Max(radius + 18, Math.Min(width - radius - 18, p.X)),
                Math.Max(radius + 44, Math.Min(height - radius - 70, p.Y)));
        }
        internal static string CustomPath(string root, string filename)
        {
            if (string.IsNullOrEmpty(filename) || filename != Path.GetFileName(filename)) return null;
            string full = Path.GetFullPath(Path.Combine(root, filename));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }

    internal static class Native
    {
        internal delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData
        { internal POINT Pt; internal uint Mouse, Flags, Time; internal UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int he, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        internal static POINT Cursor() { POINT p; GetCursorPos(out p); return p; }
        internal static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
    }

    internal static class Art
    {
        internal static readonly Color Amber = (Color)ColorConverter.ConvertFromString("#EDC05B");
        internal static Brush Brush(string c) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)); }
        internal static Brush Brush(Color c, double alpha)
        { return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Max(0, Math.Min(1, alpha))), c.R, c.G, c.B)); }
        internal static void Text(DrawingContext d, string text, Point p, double size, Brush color, bool center, bool bold)
        {
            var ft = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
                    bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, color, 1.0);
            d.DrawText(ft, center ? new Point(p.X - ft.Width / 2, p.Y) : p);
        }
        internal static Geometry Sector(double inner, double outer, double a0, double a1)
        {
            Func<double, double, Point> polar = (r, a) => new Point(r * Math.Cos(a * Math.PI / 180), r * Math.Sin(a * Math.PI / 180));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(polar(inner, a0), true, true);
                c.LineTo(polar(outer, a0), true, false);
                c.ArcTo(polar(outer, a1), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(polar(inner, a1), true, false);
                c.ArcTo(polar(inner, a0), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze(); return g;
        }
    }

    internal sealed class AssetLibrary
    {
        internal readonly string Root;
        internal readonly string GameRoot = Path.Combine(App.BaseDir, "assets", "game");
        private readonly Settings settings;
        private readonly Random variants = new Random();
        private readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>();
        private readonly Dictionary<string, byte[]> clips = new Dictionary<string, byte[]>();
        private ClipPlayer speaker;
        internal AssetLibrary(string root, Settings s) { Root = root; settings = s; }
        internal BitmapSource Image(Signal signal, bool world = false)
        {
            string cacheKey = signal.Id + (world ? ":world" : ":wheel");
            BitmapSource cached;
            if (images.TryGetValue(cacheKey, out cached)) return cached;
            var item = settings.Assets.FirstOrDefault(a => a.Id == signal.Id);
            string file = item == null ? null : Logic.CustomPath(Root, item.Image);
            if (file == null || !File.Exists(file))
                file = Logic.CustomPath(GameRoot, world && !string.IsNullOrEmpty(signal.WorldTexture) ? signal.WorldTexture : signal.Texture);
            BitmapSource bitmap = null;
            if (file != null && File.Exists(file))
            {
                try
                {
                    var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad;
                    b.UriSource = new Uri(file); b.EndInit(); b.Freeze(); bitmap = b;
                }
                catch (Exception e) { App.Log(e); }
            }
            images[cacheKey] = bitmap; return bitmap;
        }
        internal void Draw(DrawingContext d, Signal s, Point p, double size, Brush ink, bool world = false)
        {
            BitmapSource b = Image(s, world);
            if (b == null) Art.Text(d, "素材缺失", p, 9, Brushes.Red, true, false);
            else
            {
                double scale = size / Math.Max(b.PixelWidth, b.PixelHeight);
                double w = b.PixelWidth * scale, h = b.PixelHeight * scale;
                d.DrawImage(b, new Rect(p.X - w / 2, p.Y - h / 2, w, h));
            }
        }
        internal void Clear()
        {
            images.Clear(); clips.Clear();
            if (speaker != null) { speaker.Dispose(); speaker = null; }
        }
        internal void Play(Signal signal)
        {
            if (!settings.Sound) return;
            try
            {
                var item = settings.Assets.FirstOrDefault(a => a.Id == signal.Id);
                string file = item == null ? null : Logic.CustomPath(Root, item.Sound);
                if (file == null || !File.Exists(file))
                    file = Path.Combine(GameRoot, "sounds", signal.SoundKind + "_" + variants.Next(1, signal.SoundKind == "notify" ? 7 : 4) + ".wav");
                if (!File.Exists(file)) throw new FileNotFoundException("缺少原版信号音效", file);
                byte[] pcm;
                if (!clips.TryGetValue(file, out pcm))
                {
                    pcm = ClipPlayer.ToDevicePcm(File.ReadAllBytes(file));
                    clips[file] = pcm;
                }
                if (speaker == null) speaker = new ClipPlayer();
                if (!speaker.Submit(pcm)) throw new InvalidOperationException("音频设备没有播出来。Win32/MMSYS 错误：" + speaker.LastError);
            }
            catch (Exception e) { App.Log(e); }
        }
        internal static double WaveDuration(byte[] bytes)
        {
            if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
                throw new InvalidOperationException("这不是有效的 WAV 文件。");
            int rate = 0, align = 0, data = 0;
            for (int offset = 12; offset + 8 <= bytes.Length; )
            {
                string tag = Encoding.ASCII.GetString(bytes, offset, 4);
                uint length = BitConverter.ToUInt32(bytes, offset + 4);
                if (length > int.MaxValue || (long)offset + 8 + length > bytes.Length) throw new InvalidOperationException("WAV 文件不完整。");
                if (tag == "fmt ")
                {
                    if (length < 16) throw new InvalidOperationException("WAV 格式信息不完整。");
                    int format = BitConverter.ToUInt16(bytes, offset + 8);
                    int channels = BitConverter.ToUInt16(bytes, offset + 10);
                    rate = BitConverter.ToInt32(bytes, offset + 12);
                    align = BitConverter.ToUInt16(bytes, offset + 20);
                    int bits = BitConverter.ToUInt16(bytes, offset + 22);
                    if (format != 1 || channels < 1 || channels > 2 || rate < 8000 || rate > 96000 ||
                        (bits != 8 && bits != 16) || align != channels * bits / 8)
                        throw new InvalidOperationException("请使用 8/16 位 PCM WAV（单声道或双声道）。");
                }
                if (tag == "data") data += (int)length;
                offset += 8 + (int)length + (int)(length % 2);
            }
            if (rate == 0 || align == 0 || data == 0) throw new InvalidOperationException("WAV 缺少有效音频数据。");
            return data / (double)rate / align;
        }
    }

    internal sealed class ClipPlayer : IDisposable
    {
        private const int Rate = 44100;
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            internal ushort Format, Channels;
            internal uint Samples, Average;
            internal ushort Block, Bits, Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            internal IntPtr Data;
            internal uint Length, Recorded;
            internal IntPtr User;
            internal uint Flags, Loops;
            internal IntPtr Next, Reserved;
        }
        private delegate void WaveOutProc(IntPtr device, uint message, IntPtr instance, IntPtr param1, IntPtr param2);
        [DllImport("winmm.dll", EntryPoint = "waveOutOpen")] private static extern int OpenDevice(out IntPtr device, IntPtr id, ref WAVEFORMATEX format, WaveOutProc proc, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr device);
        private readonly WaveOutProc proc;
        private IntPtr device, header;
        private GCHandle pin;
        private bool prepared;
        internal bool Ready;
        internal int LastError;
        internal ClipPlayer()
        {
            proc = delegate { };
            var format = new WAVEFORMATEX { Format = 1, Channels = 1, Samples = Rate, Average = Rate * 2, Block = 2, Bits = 16 };
            LastError = OpenDevice(out device, new IntPtr(-1), ref format, proc, IntPtr.Zero, 0x00030000);
            Ready = LastError == 0 && device != IntPtr.Zero;
            if (Ready) header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WAVEHDR)));
        }
        internal bool Submit(byte[] pcm)
        {
            if (!Ready || pcm == null || pcm.Length < 2) return false;
            byte[] owned = new byte[pcm.Length & ~1];
            Buffer.BlockCopy(pcm, 0, owned, 0, owned.Length);
            waveOutReset(device);
            ReleaseBuffer();
            pin = GCHandle.Alloc(owned, GCHandleType.Pinned);
            var hdr = new WAVEHDR { Data = pin.AddrOfPinnedObject(), Length = (uint)owned.Length };
            Marshal.StructureToPtr(hdr, header, false);
            int headerSize = Marshal.SizeOf(typeof(WAVEHDR));
            LastError = waveOutPrepareHeader(device, header, headerSize);
            prepared = LastError == 0;
            if (!prepared) { ReleaseBuffer(); return false; }
            LastError = waveOutWrite(device, header, headerSize);
            if (LastError != 0) { ReleaseBuffer(); return false; }
            return true;
        }
        private void ReleaseBuffer()
        {
            if (prepared && device != IntPtr.Zero)
            {
                waveOutUnprepareHeader(device, header, Marshal.SizeOf(typeof(WAVEHDR)));
                prepared = false;
            }
            if (pin.IsAllocated) pin.Free();
        }
        public void Dispose()
        {
            if (device != IntPtr.Zero) waveOutReset(device);
            ReleaseBuffer();
            if (device != IntPtr.Zero) { waveOutClose(device); device = IntPtr.Zero; }
            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
            Ready = false;
        }
        internal static byte[] ToDevicePcm(byte[] bytes)
        {
            int rate = 0, channels = 0, bits = 0, dataAt = 0, dataBytes = 0;
            if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
                throw new InvalidOperationException("这不是有效的 WAV 文件。");
            for (int offset = 12; offset + 8 <= bytes.Length; )
            {
                string tag = Encoding.ASCII.GetString(bytes, offset, 4);
                int length = BitConverter.ToInt32(bytes, offset + 4);
                if (length < 0 || offset + 8 + length > bytes.Length) throw new InvalidOperationException("WAV 文件不完整。");
                if (tag == "fmt ")
                {
                    channels = BitConverter.ToUInt16(bytes, offset + 10);
                    rate = BitConverter.ToInt32(bytes, offset + 12);
                    bits = BitConverter.ToUInt16(bytes, offset + 22);
                }
                if (tag == "data") { dataAt = offset + 8; dataBytes = length; }
                offset += 8 + length + (length & 1);
            }
            int width = bits / 8;
            if ((bits != 8 && bits != 16) || channels < 1 || channels > 2 || rate < 8000 || width == 0 || dataBytes < width * channels)
                throw new InvalidOperationException("音频设备只接受 8/16 位 PCM。");
            int frames = dataBytes / (width * channels);
            var mono = new short[frames];
            for (int i = 0; i < frames; i++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = dataAt + (i * channels + c) * width;
                    sum += bits == 8 ? (bytes[at] - 128) << 8 : BitConverter.ToInt16(bytes, at);
                }
                mono[i] = (short)(sum / channels);
            }
            int outFrames = Math.Max(1, (int)Math.Round(frames * (double)Rate / rate));
            var dst = new byte[outFrames * 2];
            for (int i = 0; i < outFrames; i++)
            {
                double pos = i * (double)rate / Rate;
                int left = Math.Min((int)pos, frames - 1);
                int right = Math.Min(left + 1, frames - 1);
                int sample = (int)Math.Round(mono[left] + (mono[right] - mono[left]) * (pos - left));
                if (sample > 32767) sample = 32767;
                if (sample < -32768) sample = -32768;
                dst[i * 2] = (byte)sample;
                dst[i * 2 + 1] = (byte)(sample >> 8);
            }
            return dst;
        }
    }

    internal sealed class WheelState
    {
        internal Point Center;
        internal int Page, Selected = -1;
        internal double Radius;
    }
    internal sealed class PingState
    {
        internal Signal Signal;
        internal Point At;
        internal DateTime Start;
        internal double Duration;
    }
    internal static class Painter
    {
        internal static void Wheel(DrawingContext d, WheelState wheel, AssetLibrary assets)
        {
            double scale = wheel.Radius / 190;
            d.PushTransform(new TranslateTransform(wheel.Center.X, wheel.Center.Y));
            d.PushTransform(new ScaleTransform(scale, scale));
            d.DrawEllipse(Art.Brush("#12000000"), null, new Point(0, 4), 204, 204);
            for (int i = 0; i < 8; i++)
            {
                bool selected = wheel.Selected == i;
                var fill = selected ? Art.Brush("#EE6A572D") : Art.Brush("#ED1B2027");
                var edge = new Pen(selected ? Art.Brush("#FFF0CB80") : Art.Brush("#A04C5662"), 1);
                d.DrawGeometry(fill, edge, Art.Sector(72, 190, -112.5 + 45 * i + 0.5, -67.5 + 45 * i - 0.5));
                double angle = (-90 + 45 * i) * Math.PI / 180;
                Point p = new Point(137 * Math.Cos(angle), 137 * Math.Sin(angle));
                Brush ink = selected ? Art.Brush("#20242B") : Art.Brush("#E5E9ED");
                Signal s = Catalog.Pages[wheel.Page][i];
                assets.Draw(d, s, new Point(p.X, p.Y - 10), selected ? 64 : 56, ink);
                Art.Text(d, s.Label, new Point(p.X, p.Y + 22), 11.5, Brushes.WhiteSmoke, true, selected);
            }
            d.DrawEllipse(Art.Brush("#F31A1F26"), new Pen(Art.Brush("#68747D"), 1.2), new Point(), 69, 69);
            if (wheel.Selected >= 0)
            {
                Signal current = Catalog.Pages[wheel.Page][wheel.Selected];
                assets.Draw(d, current, new Point(0, -22), 44, Art.Brush(Art.Amber, 1));
                Art.Text(d, current.Label, new Point(0, 4), 14, Brushes.WhiteSmoke, true, true);
                Art.Text(d, "松开发送", new Point(0, 30), 10, Art.Brush("#8A96A4"), true, false);
            }
            else
            {
                Art.Text(d, Catalog.PageNames[wheel.Page], new Point(0, -20), 15, Art.Brush(Art.Amber, 1), true, true);
                Art.Text(d, "移动鼠标选择", new Point(0, 10), 10.5, Art.Brush("#A3ADBA"), true, false);
                Art.Text(d, "ESC 取消", new Point(0, 29), 9.5, Art.Brush("#737F8F"), true, false);
            }
            Art.Text(d, "PUBG PING   /   " + (wheel.Page + 1).ToString("00"), new Point(0, -226), 10.5, Art.Brush(Art.Amber, 1), true, true);
            var legend = new StringBuilder();
            for (int p = 0; p < Catalog.PageNames.Length; p++)
            {
                if (p > 0) legend.Append("     /     ");
                legend.Append(p == wheel.Page ? "【" + Catalog.PageNames[p] + "】" : Catalog.PageNames[p]);
            }
            Art.Text(d, legend.ToString(), new Point(0, 210), 11, Brushes.WhiteSmoke, true, false);
            Art.Text(d, "滚轮 · ← → · Tab 切页       右键 / Esc 取消", new Point(0, 233), 10, Art.Brush("#A3ADBA"), true, false);
            d.Pop(); d.Pop();
        }
        internal static void Ping(DrawingContext d, PingState ping, double elapsed, AssetLibrary assets)
        {
            double end = ping.Duration;
            if (elapsed < 0 || elapsed > end) return;
            double opacity = elapsed < 0.09 ? elapsed / 0.09 : Math.Min(1, (end - elapsed) / 0.35);
            d.PushOpacity(Math.Max(0, opacity));
            d.PushTransform(new TranslateTransform(ping.At.X, ping.At.Y));
            for (int i = 0; i < 3; i++)
            {
                double t = elapsed - i * 0.20;
                if (t >= 0 && t < 0.65)
                {
                    double r = 10 + t / 0.65 * 50;
                    d.DrawEllipse(null, new Pen(Art.Brush(ping.Signal.Color, (1 - t / 0.65) * .72), 2), new Point(), r, r * .32);
                }
            }
            double pop = elapsed < .24 ? .45 + .65 * Math.Sin(Math.Min(1, elapsed / .24) * Math.PI / 2) : 1.0;
            double bob = Math.Sin(elapsed * 5.3) * 2.5;
            d.PushTransform(new TranslateTransform(0, -37 + bob));
            d.PushTransform(new ScaleTransform(pop, pop));
            Brush plate = string.IsNullOrEmpty(ping.Signal.WorldTexture) ? Art.Brush("#E51B2027") : Art.Brush(ping.Signal.Color, .85);
            d.DrawRoundedRectangle(plate, new Pen(Art.Brush(ping.Signal.Color, .95), 1.5), new Rect(-29, -29, 58, 58), 5, 5);
            assets.Draw(d, ping.Signal, new Point(), 42, Art.Brush(ping.Signal.Color, 1), true);
            d.Pop(); d.Pop();
            d.DrawLine(new Pen(Art.Brush(ping.Signal.Color, .8), 1.5), new Point(0, -10), new Point(0, 2));
            d.DrawEllipse(Art.Brush(ping.Signal.Color, 1), null, new Point(), 3, 3);
            Art.Text(d, ping.Signal.Label, new Point(0, 16), 13, Brushes.White, true, true);
            d.Pop(); d.Pop();
        }
    }

    internal sealed class OverlayCanvas : FrameworkElement
    {
        internal WheelState Wheel;
        internal readonly List<PingState> Pings = new List<PingState>();
        internal readonly AssetLibrary Assets;
        internal int Frames;
        internal OverlayCanvas(AssetLibrary assets) { Assets = assets; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext d)
        {
            base.OnRender(d);
            Frames++;
            DateTime now = DateTime.UtcNow;
            foreach (PingState p in Pings) Painter.Ping(d, p, (now - p.Start).TotalSeconds, Assets);
            if (Wheel != null) Painter.Wheel(d, Wheel, Assets);
        }
    }
    internal sealed class OverlayWindow : Window
    {
        internal readonly OverlayCanvas Canvas;
        internal readonly Forms.Screen Screen;
        internal double Dpi = 1;
        private HwndSource source;
        internal OverlayWindow(Forms.Screen screen, AssetLibrary assets)
        {
            Screen = screen; Canvas = new OverlayCanvas(assets);
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false;
            ResizeMode = ResizeMode.NoResize; Content = Canvas;
            Title = "PUBG PING overlay";
            Width = screen.Bounds.Width; Height = screen.Bounds.Height;
            SourceInitialized += delegate {
                IntPtr h = new WindowInteropHelper(this).Handle;
                int style = Native.GetWindowLong(h, -20);
                Native.SetWindowLongPtr(h, -20, new IntPtr(style | 0x20 | 0x80 | 0x80000 | 0x08000000));
                source = HwndSource.FromHwnd(h); source.AddHook(Messages);
                Place();
            };
            Loaded += delegate { Place(); };
        }
        private IntPtr Messages(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x21) { handled = true; return new IntPtr(3); }
            if (msg == 0x84) { handled = true; return new IntPtr(-1); }
            return IntPtr.Zero;
        }
        private void Place()
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            var b = Screen.Bounds;
            Native.SetWindowPos(h, new IntPtr(-1), b.Left, b.Top, b.Width, b.Height, 0x0010);
            try { Dpi = Native.GetDpiForWindow(h) / 96.0; } catch (EntryPointNotFoundException) { Dpi = 1; }
            if (Dpi <= 0) Dpi = 1;
        }
        internal Point Local(Native.POINT p)
        { return new Point((p.X - Screen.Bounds.Left) / Dpi, (p.Y - Screen.Bounds.Top) / Dpi); }
    }

    internal sealed class Controller : IDisposable
    {
        internal readonly Settings Config;
        internal readonly AssetLibrary Assets;
        private readonly List<OverlayWindow> overlays = new List<OverlayWindow>();
        private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);
        private readonly HashSet<int> swallowedKeys = new HashSet<int>();
        private readonly HashSet<int> passedTriggerKeys = new HashSet<int>();
        private readonly HashSet<int> physicalKeys = new HashSet<int>();
        private bool physicalMiddle;
        private Native.HookProc keyProc, mouseProc;
        private IntPtr keyHook, mouseHook;
        private HwndSource sink;
        private Forms.NotifyIcon tray;
        private Forms.ToolStripMenuItem pauseItem;
        private bool captured, cancelled, swallowRightUp, quitting, closePromptOpen;
        private DateTime openedAt, foregroundChecked;
        private IntPtr checkedForeground;
        private bool checkedBlocked;
        private int capturedTrigger;
        private int generation, activeGeneration;
        private Native.POINT origin;
        private OverlayWindow active;
        private Window settingsWindow;
        internal Action StateChanged;
        internal bool HotkeyRegistered;
        internal string PauseShortcut = "未注册";
        internal bool SimulatedInput, SimulationBlocked, SimulationModifiers, LastReleaseCommits;
        internal bool GestureCaptured { get { return captured; } }
        internal int TotalFrames { get { return overlays.Sum(w => w.Canvas.Frames); } }
        internal Controller(Settings config)
        {
            Config = config; Assets = new AssetLibrary(Path.Combine(App.BaseDir, "assets", "custom"), config);
            timer.Interval = TimeSpan.FromMilliseconds(16); timer.Tick += Tick;
        }
        internal void Start()
        {
            var p = new HwndSourceParameters("PUBG-Ping-Control") { Width = 0, Height = 0, WindowStyle = 0 };
            sink = new HwndSource(p); sink.AddHook(Messages);
            HotkeyRegistered = Native.RegisterHotKey(sink.Handle, 1, 0x4003, 0x50);
            if (HotkeyRegistered) PauseShortcut = "Ctrl+Alt+P";
            else
            {
                HotkeyRegistered = Native.RegisterHotKey(sink.Handle, 1, 0x4003, 0x7B);
                if (HotkeyRegistered) PauseShortcut = "Ctrl+Alt+F12";
            }
            keyProc = KeyHook; mouseProc = MouseHook;
            if (!InstallHooks(true))
                throw new InvalidOperationException("无法安装桌面输入监听。Win32 错误：" + Marshal.GetLastWin32Error());
            tray = new Forms.NotifyIcon(); tray.Icon = Brand.ShellIcon() ?? System.Drawing.SystemIcons.Application;
            tray.Text = "PUBG PING";
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("设置与预览", null, delegate { Queue(ShowSettings); });
            pauseItem = new Forms.ToolStripMenuItem(); pauseItem.Click += delegate { Queue(Toggle); }; menu.Items.Add(pauseItem);
            menu.Items.Add("退出", null, delegate { Queue(Exit); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Queue(ShowSettings); }; tray.Visible = true;
            UpdateTray();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            Queue(delegate { Cancel(); foreach (var w in overlays) w.Close(); overlays.Clear(); active = null; });
        }
        private void Queue(Action action)
        {
            if (!quitting && !SimulatedInput) Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, action);
        }
        private IntPtr Messages(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 && w.ToInt32() == 1) { Toggle(); handled = true; }
            if (msg == 0x8001) { ShowSettings(); handled = true; }
            return IntPtr.Zero;
        }
        private bool BlockedForeground()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow(); if (h == IntPtr.Zero) return false;
                DateTime now = DateTime.UtcNow;
                if (h == checkedForeground && (now - foregroundChecked).TotalSeconds < .20) return checkedBlocked;
                checkedForeground = h; foregroundChecked = now; checkedBlocked = true;
                checkedBlocked = InspectForeground(h); return checkedBlocked;
            }
            catch { return true; } // If inspection fails, leave input alone.
        }
        private bool InspectForeground(IntPtr h)
        {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if ((int)pid == Process.GetCurrentProcess().Id) return false;
                using (var process = Process.GetProcessById((int)pid))
                {
                    string n = process.ProcessName;
                    if (n.StartsWith("TslGame", StringComparison.OrdinalIgnoreCase) || n.Equals("ExecPubg", StringComparison.OrdinalIgnoreCase)) return true;
                    if (!Config.AvoidFullscreen || n.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
                }
                Native.RECT r; if (!Native.GetWindowRect(h, out r)) return false;
                var bounds = Forms.Screen.FromHandle(h).Bounds;
                return r.Left <= bounds.Left && r.Top <= bounds.Top && r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
        }
        private bool BeginCapture(int trigger)
        {
            if (captured) return false;
            if (!Config.Enabled || (SimulatedInput ? SimulationBlocked : BlockedForeground())) return false;
            bool modifiers = SimulatedInput ? SimulationModifiers :
                Native.Down(0x10) || Native.Down(0x11) || Native.Down(0x12) || Native.Down(0x5B) || Native.Down(0x5C);
            if (modifiers) return false;
            capturedTrigger = trigger; captured = true; cancelled = false;
            origin = SimulatedInput ? new Native.POINT() : Native.Cursor(); openedAt = DateTime.UtcNow;
            int id = ++generation;
            Queue(delegate { if (generation == id) Open(id); }); return true;
        }
        private void ReleaseCapture()
        {
            if (!captured) return;
            var point = SimulatedInput ? new Native.POINT() : Native.Cursor(); bool commit = !cancelled;
            Native.POINT start = origin; int id = generation;
            LastReleaseCommits = commit;
            captured = false; Queue(delegate { End(point, start, commit, id); InstallHooks(false); });
        }
        // Windows updates GetAsyncKeyState only for keys that pass the low-level hook.
        // The trigger is swallowed, so that API stays "up" and used to close the wheel on the next tick.
        private bool TriggerHeld()
        {
            return capturedTrigger == 4 ? physicalMiddle : physicalKeys.Contains(capturedTrigger);
        }
        internal bool TriggerPhysicallyHeld { get { return TriggerHeld(); } }
        internal void PollGesture()
        {
            if (captured && !TriggerHeld()) ReleaseCapture();
        }
        private bool InstallHooks(bool required)
        {
            if (keyProc == null || mouseProc == null) return false;
            if (!required && captured) return true;
            IntPtr module = Native.GetModuleHandle(null);
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            keyHook = Native.SetWindowsHookEx(13, keyProc, module, 0);
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, module, 0);
            if (keyHook != IntPtr.Zero && mouseHook != IntPtr.Zero) return true;
            if (required) return false;
            App.Log(new InvalidOperationException("桌面输入监听中断，错误 " + Marshal.GetLastWin32Error()));
            return false;
        }
        private void QueueCancel()
        {
            int id = generation; cancelled = true;
            Queue(delegate { if (generation == id) Cancel(); });
        }
        private void QueuePage(int step)
        {
            int id = generation;
            Queue(delegate { if (activeGeneration == id) ChangePage(step); });
        }
        // These two routing methods are shared by the native callbacks and in-process tests.
        // The tests do not inject keyboard/mouse events into Windows or other applications.
        internal bool RouteKey(int msg, int vk, bool injected)
        {
            if (injected) return false;
            bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
            if (down) physicalKeys.Add(vk);
            if (up) physicalKeys.Remove(vk);
            if (up) passedTriggerKeys.Remove(vk);
            if (up && captured && vk == capturedTrigger) { ReleaseCapture(); return true; }
            if (Config.Trigger != 4 && vk == Config.Trigger)
            {
                if (down && captured && vk == capturedTrigger) return true;
                if (down && !passedTriggerKeys.Contains(vk))
                {
                    if (BeginCapture(vk)) return true;
                    passedTriggerKeys.Add(vk);
                }
            }
            if (up && swallowedKeys.Remove(vk)) return true;
            if (down && swallowedKeys.Contains(vk)) return true;
            if (down && captured && !cancelled && (vk == 0x1B || vk == 0x25 || vk == 0x27 || vk == 0x09))
            {
                swallowedKeys.Add(vk);
                if (vk == 0x1B) QueueCancel(); else QueuePage(vk == 0x25 ? -1 : 1);
                return true;
            }
            return false;
        }
        internal bool RouteMouse(int msg, int delta, bool injected)
        {
            if (injected) return false;
            if (msg == 0x207) physicalMiddle = true;
            if (msg == 0x208) physicalMiddle = false;
            if (msg == 0x208 && captured && capturedTrigger == 4) { ReleaseCapture(); return true; }
            if (Config.Trigger == 4 && msg == 0x207 && ((captured && capturedTrigger == 4) || BeginCapture(4))) return true;
            if (msg == 0x205 && swallowRightUp) { swallowRightUp = false; return true; }
            if (captured && !cancelled)
            {
                if (msg == 0x204) { swallowRightUp = true; QueueCancel(); return true; }
                if (msg == 0x20A) { QueuePage(delta > 0 ? -1 : 1); return true; }
            }
            return false;
        }
        private IntPtr KeyHook(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0 && RouteKey(w.ToInt32(), Marshal.ReadInt32(l), (Marshal.ReadInt32(l, 8) & 0x10) != 0)) return new IntPtr(1);
            }
            catch (Exception e) { App.Log(e); }
            return Native.CallNextHookEx(keyHook, code, w, l);
        }
        private IntPtr MouseHook(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0 && RouteMouse(w.ToInt32(), (short)((uint)Marshal.ReadInt32(l, 8) >> 16), (Marshal.ReadInt32(l, 12) & 1) != 0)) return new IntPtr(1);
            }
            catch (Exception e) { App.Log(e); }
            return Native.CallNextHookEx(mouseHook, code, w, l);
        }
        internal void Toggle()
        { Config.Enabled = !Config.Enabled; Cancel(); Save(); UpdateTray(); if (StateChanged != null) StateChanged(); }
        internal void Save() { App.Save(Config); }
        private void UpdateTray() { if (pauseItem != null) pauseItem.Text = (Config.Enabled ? "暂停  " : "恢复  ") + PauseShortcut; }
        private OverlayWindow OverlayAt(Native.POINT p)
        {
            Forms.Screen screen = Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
            var window = overlays.FirstOrDefault(o => o.Screen.DeviceName == screen.DeviceName);
            if (window == null) { window = new OverlayWindow(screen, Assets); overlays.Add(window); }
            if (!window.IsVisible) window.Show(); return window;
        }
        private void Open(int id)
        {
            if (!captured || cancelled) return;
            active = OverlayAt(origin);
            activeGeneration = id;
            double width = active.Screen.Bounds.Width / active.Dpi, height = active.Screen.Bounds.Height / active.Dpi;
            double radius = Math.Min(Config.Radius, Math.Max(105, Math.Min(width / 2 - 25, height / 2 - 70)));
            active.Canvas.Wheel = new WheelState { Center = Logic.Fit(active.Local(origin), width, height, radius), Page = Config.LastPage, Radius = radius };
            timer.Start(); active.Canvas.InvalidateVisual();
        }
        private void ChangePage(int step)
        {
            if (active == null || active.Canvas.Wheel == null) return;
            Config.LastPage = (active.Canvas.Wheel.Page + step + 3) % 3;
            active.Canvas.Wheel.Page = Config.LastPage; active.Canvas.InvalidateVisual();
        }
        private void End(Native.POINT point, Native.POINT start, bool commit, int id)
        {
            if (id != activeGeneration) return;
            if (active != null && active.Canvas.Wheel != null)
            {
                var wheel = active.Canvas.Wheel;
                int chosen = Logic.Pick((point.X - start.X) / active.Dpi, (point.Y - start.Y) / active.Dpi);
                if (commit && chosen >= 0) AddPing(active, Catalog.Pages[wheel.Page][chosen], active.Local(start));
                active.Canvas.Wheel = null; active.Canvas.InvalidateVisual();
            }
            active = null; Save();
        }
        internal void Cancel()
        {
            cancelled = true;
            foreach (var window in overlays) { window.Canvas.Wheel = null; window.Canvas.InvalidateVisual(); }
            active = null;
        }
        private void AddPing(OverlayWindow window, Signal signal, Point at)
        {
            if (window.Canvas.Pings.Count >= 24) window.Canvas.Pings.RemoveAt(0);
            window.Canvas.Pings.Add(new PingState { Signal = signal, At = at, Start = DateTime.UtcNow, Duration = Config.Duration });
            Assets.Play(signal); timer.Start(); window.Canvas.InvalidateVisual();
        }
        internal void Preview(Signal signal)
        {
            var p = Native.Cursor(); var window = OverlayAt(p); AddPing(window, signal, window.Local(p));
        }
        internal void ShowSmokeWheel()
        {
            var p = Native.Cursor(); var window = OverlayAt(p);
            window.Canvas.Wheel = new WheelState {
                Center = Logic.Fit(window.Local(p), window.Screen.Bounds.Width / window.Dpi,
                    window.Screen.Bounds.Height / window.Dpi, Config.Radius),
                Page = Config.LastPage, Selected = 1, Radius = Config.Radius
            };
            timer.Start(); window.Canvas.InvalidateVisual();
        }
        internal string OverlayDiagnostics()
        {
            var lines = new List<string>();
            foreach (var window in overlays)
            {
                IntPtr h = new WindowInteropHelper(window).Handle;
                int style = Native.GetWindowLong(h, -20);
                lines.Add("Overlay style: click-through=" + ((style & 0x20) != 0) +
                    "; no-activate=" + ((style & 0x08000000) != 0) + "; tool-window=" + ((style & 0x80) != 0));
                lines.Add("DPI scale=" + window.Dpi.ToString(CultureInfo.InvariantCulture) +
                    "; canvas=" + window.Canvas.ActualWidth.ToString(CultureInfo.InvariantCulture) + "x" + window.Canvas.ActualHeight.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join("\r\n", lines.ToArray()) + "\r\n";
        }
        private void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (captured && !TriggerHeld()) ReleaseCapture();
            if (captured && (now - openedAt).TotalSeconds > 10) { cancelled = true; ReleaseCapture(); }
            if (captured && active != null && active.Canvas.Wheel != null && BlockedForeground())
            { cancelled = true; ReleaseCapture(); }
            else if (active != null && active.Canvas.Wheel != null)
            {
                var p = Native.Cursor();
                active.Canvas.Wheel.Selected = Logic.Pick((p.X - origin.X) / active.Dpi, (p.Y - origin.Y) / active.Dpi);
            }
            bool any = captured;
            foreach (var window in overlays)
            {
                window.Canvas.Pings.RemoveAll(p => (now - p.Start).TotalSeconds > p.Duration);
                if (window.Canvas.Pings.Count > 0 || window.Canvas.Wheel != null) { any = true; window.Canvas.InvalidateVisual(); }
                else if (window.IsVisible) window.Hide();
            }
            if (!any) timer.Stop();
        }
        internal void ShowSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new Window { Title = "PUBG PING", Width = 900, Height = 850, Icon = Brand.WindowIcon(),
                    MinWidth = 760, MinHeight = 650, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Art.Brush("#11151B"), Foreground = Brushes.WhiteSmoke };
                settingsWindow.Content = SettingsUi.Build(this);
                settingsWindow.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) {
                    if (quitting) return;
                    e.Cancel = true;
                    settingsWindow.Dispatcher.BeginInvoke(new Action(FinishClose));
                };
            }
            settingsWindow.Show(); if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Activate();
        }
        private void FinishClose()
        {
            if (quitting || settingsWindow == null || closePromptOpen) return;
            bool ask = Config.OnClose != "tray" && Config.OnClose != "exit";
            if (ask) closePromptOpen = true;
            string choice;
            try { choice = ask ? PromptClose() : Config.OnClose; }
            finally { closePromptOpen = false; }
            if (choice == "exit") Exit();
            else if (choice == "tray") settingsWindow.Hide();
        }
        private string PromptClose()
        {
            var dialog = new Window {
                Title = "PUBG PING", Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = settingsWindow, ShowInTaskbar = false,
                Background = Art.Brush("#11151B"), Foreground = Brushes.WhiteSmoke, FontFamily = new FontFamily("Microsoft YaHei UI"),
                Icon = Brand.WindowIcon()
            };
            var body = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            body.Children.Add(new TextBlock { Text = "关闭 PUBG PING", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.WhiteSmoke });
            body.Children.Add(new TextBlock { Text = "最小化到托盘后仍可按住热键发送。完全退出会卸掉监听。", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Art.Brush("#A5B0BE") });
            var remember = new CheckBox { Content = "不再询问", Margin = new Thickness(0, 14, 0, 16), Foreground = Brushes.WhiteSmoke };
            body.Children.Add(remember);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            string choice = null;
            Action<string> pick = delegate(string value) {
                choice = value;
                if (remember.IsChecked == true) { Config.OnClose = value; Save(); }
                dialog.DialogResult = true;
            };
            row.Children.Add(CloseButton("最小化到托盘", delegate { pick("tray"); }, true));
            row.Children.Add(CloseButton("完全退出", delegate { pick("exit"); }, false));
            body.Children.Add(row);
            dialog.Content = body;
            return dialog.ShowDialog() == true ? choice : null;
        }
        private static Button CloseButton(string text, Action action, bool accent)
        {
            var button = new Button {
                Content = text, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7), BorderThickness = new Thickness(0),
                Background = Art.Brush(accent ? "#DCC073" : "#2A313C"), Foreground = Art.Brush(accent ? "#1A1E25" : "#F1F3F5"),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += delegate { action(); };
            return button;
        }
        internal void Exit()
        {
            quitting = true;
            if (Application.Current != null) Application.Current.Shutdown();
        }
        internal void OpenGameAssets()
        {
            string dir = Assets.GameRoot;
            if (!Directory.Exists(dir))
            {
                MessageBox.Show("没有找到素材文件夹：\n" + dir + "\n\n它在程序旁边的 assets\\game，不是这台电脑的 PUBG 安装目录。", "PUBG PING");
                return;
            }
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }
        public void Dispose()
        {
            quitting = true; timer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (sink != null) { Native.UnregisterHotKey(sink.Handle, 1); sink.Dispose(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            foreach (var window in overlays) window.Close(); overlays.Clear();
            if (settingsWindow != null) settingsWindow.Close();
            Assets.Clear(); Save();
        }
    }

    internal static class SettingsUi
    {
        private static TextBlock Text(string text, double size, string color)
        { return new TextBlock { Text = text, FontSize = size, Foreground = Art.Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) }; }
        private static Border Card(UIElement child)
        { return new Border { Background = Art.Brush("#1B212A"), CornerRadius = new CornerRadius(12), Padding = new Thickness(22, 17, 22, 17), Margin = new Thickness(0, 0, 0, 13), Child = child }; }
        private static Button Button(string text, Action action)
        {
            var b = new Button { Content = text, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 6, 10, 2),
                Background = Art.Brush("#DCC073"), Foreground = Art.Brush("#1A1E25"), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            b.Click += delegate { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "未能完成操作"); App.Log(e); } }; return b;
        }
        internal static FrameworkElement Build(Controller app)
        {
            var outer = new StackPanel { Margin = new Thickness(30, 24, 30, 24), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch };
            outer.Children.Add(Text("非官方桌面信号", 10, "#DCC073"));
            var title = Text("PUBG PING", 31, "#F1F3F5"); title.FontWeight = FontWeights.Bold; outer.Children.Add(title);
            outer.Children.Add(Text("本机提取的原始图标和提示音", 12, "#A5B0BE"));
            outer.Children.Add(new Border { Height = 17 });
            var status = new StackPanel();
            var state = Text("", 18, "#DCC073"); status.Children.Add(state);
            var instructions = Text("", 13, "#E6E9ED");
            Action refresh = delegate {
                state.Text = app.Config.Enabled ? "●  桌面信号已启用" : "○  已暂停，普通输入不受影响";
                string key = app.Config.Trigger == 4 ? "鼠标中键" : "F" + (app.Config.Trigger - 111);
                instructions.Text = "按住 " + key + " → 移动鼠标 → 松开发送；在中心松开则取消。默认打开「" + Catalog.PageNames[Math.Max(0, Math.Min(2, app.Config.LastPage))] + "」。";
            };
            app.StateChanged = refresh; refresh();
            status.Children.Add(instructions);
            status.Children.Add(Text("按住期间：滚轮 / ← → / Tab 切页；Esc 或右键取消。", 12, "#A5B0BE"));
            status.Children.Add(Button("暂停 / 恢复    " + (app.HotkeyRegistered ? app.PauseShortcut : "可使用托盘菜单"), app.Toggle));
            if (!app.HotkeyRegistered) status.Children.Add(Text("暂停快捷键未注册或正在预览模式；仍可使用此按钮和托盘菜单。", 11, "#DCC073"));
            outer.Children.Add(Card(status));
            var options = new StackPanel(); options.Children.Add(Text("交互与显示", 16, "#F1F3F5"));
            var row = new WrapPanel { Margin = new Thickness(0, 5, 0, 7) };
            row.Children.Add(new TextBlock { Text = "触发方式", Width = 94, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.WhiteSmoke });
            var trigger = new ComboBox { Width = 210, Margin = new Thickness(0, 0, 18, 0) };
            int[] keys = { 119, 117, 118, 120, 121, 122, 123, 4 };
            string[] names = { "F8（默认）", "F6", "F7", "F9", "F10", "F11", "F12", "鼠标中键（占用中键）" };
            foreach (string name in names) trigger.Items.Add(name);
            trigger.SelectedIndex = Array.IndexOf(keys, app.Config.Trigger);
            trigger.SelectionChanged += delegate { app.Cancel(); app.Config.Trigger = keys[trigger.SelectedIndex]; app.Save(); refresh(); };
            row.Children.Add(trigger);
            var sound = new CheckBox { Content = "播放提示音", IsChecked = app.Config.Sound, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.WhiteSmoke };
            sound.Click += delegate { app.Config.Sound = sound.IsChecked == true; app.Save(); }; row.Children.Add(sound); options.Children.Add(row);
            var avoid = new CheckBox { Content = "全屏应用中暂停触发（推荐）", IsChecked = app.Config.AvoidFullscreen, Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 6, 0, 7) };
            avoid.Click += delegate { app.Config.AvoidFullscreen = avoid.IsChecked == true; app.Save(); }; options.Children.Add(avoid);
            options.Children.Add(Text("前台为 PUBG 时始终不触发；只检查窗口信息，不读取游戏内存。", 11, "#8995A5"));
            var sizeRow = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            sizeRow.Children.Add(new TextBlock { Text = "轮盘大小", Width = 94, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center });
            var size = new Slider { Minimum = 145, Maximum = 220, Value = app.Config.Radius, Width = 190, Margin = new Thickness(0, 2, 15, 0) };
            size.ValueChanged += delegate { app.Config.Radius = Math.Round(size.Value); app.Save(); }; sizeRow.Children.Add(size);
            sizeRow.Children.Add(new TextBlock { Text = "信号时长", Width = 80, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center });
            var duration = new Slider { Minimum = 1, Maximum = 5, Value = app.Config.Duration, Width = 150 };
            duration.ValueChanged += delegate { app.Config.Duration = Math.Round(duration.Value, 1); app.Save(); }; sizeRow.Children.Add(duration); options.Children.Add(sizeRow);
            outer.Children.Add(Card(options));
            var library = new StackPanel(); library.Children.Add(Text("信号预览与本地素材", 16, "#F1F3F5"));
            library.Children.Add(Text("3 页 / 24 槽：顺序、图标、中文名称、音效类别均从本机游戏数据表解析。", 11, "#A5B0BE"));
            var selection = new WrapPanel { Margin = new Thickness(0, 8, 0, 5) };
            var page = new ComboBox { Width = 165, Margin = new Thickness(0, 0, 12, 0), ItemsSource = Catalog.PageNames };
            var signal = new ComboBox { Width = 195 };
            // Signal fields are intentionally projected: WPF binding resolves properties, not fields.
            Action fill = delegate {
                signal.Items.Clear(); foreach (var s in Catalog.Pages[Math.Max(0, page.SelectedIndex)]) signal.Items.Add(s.Label); signal.SelectedIndex = 0;
            };
            page.SelectionChanged += delegate {
                fill();
                if (page.SelectedIndex >= 0 && page.SelectedIndex != app.Config.LastPage)
                { app.Config.LastPage = page.SelectedIndex; app.Save(); refresh(); }
            }; page.SelectedIndex = app.Config.LastPage;
            selection.Children.Add(page); selection.Children.Add(signal); library.Children.Add(selection);
            Func<Signal> chosen = () => Catalog.Pages[Math.Max(0, page.SelectedIndex)][Math.Max(0, signal.SelectedIndex)];
            var buttons = new WrapPanel();
            buttons.Children.Add(Button("预览信号", delegate { app.Preview(chosen()); }));
            buttons.Children.Add(Button("导入图片…", delegate { ImportImage(app, chosen()); }));
            buttons.Children.Add(Button("导入 WAV…", delegate { ImportSound(app, chosen()); }));
            buttons.Children.Add(Button("恢复此信号", delegate { Reset(app, chosen()); })); library.Children.Add(buttons);
            library.Children.Add(Text("图片：PNG / JPG / BMP；声音：8/16 位 PCM WAV，最长 5 秒 / 2 MB。仅导入你有权使用的素材。", 10.5, "#8995A5"));
            outer.Children.Add(Card(library));
            var note = new StackPanel();
            note.Children.Add(Text("独立桌面工具，不是 PUBG 插件", 14, "#DCC073"));
            note.Children.Add(Text("运行时不读取游戏、不修改文件、不注入进程、不联网、不自动开机启动。建议游戏前完全退出；未做反作弊认证。", 11.5, "#A5B0BE"));
            note.Children.Add(Text("点窗口关闭时可选最小化到托盘或完全退出，并可勾选不再询问。托盘里的「退出」会直接结束程序。", 11.5, "#A5B0BE"));
            note.Children.Add(Text("「打开原始素材」打开的是这个程序旁边的 assets\\game，不是 PUBG 的安装目录。", 11.5, "#A5B0BE"));
            var bottom = new WrapPanel();
            bottom.Children.Add(Button("打开原始素材", app.OpenGameAssets));
            bottom.Children.Add(Button("完全退出", app.Exit)); note.Children.Add(bottom);
            outer.Children.Add(Card(note));
            return new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Art.Brush("#11151B"), FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12 };
        }
        private static AssetOverride Entry(Controller app, Signal signal)
        {
            var item = app.Config.Assets.FirstOrDefault(a => a.Id == signal.Id);
            if (item == null) { item = new AssetOverride { Id = signal.Id }; app.Config.Assets.Add(item); }
            Directory.CreateDirectory(app.Assets.Root); return item;
        }
        private static void ImportImage(Controller app, Signal signal)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp", Title = "选择你有权使用的图片" };
            if (dialog.ShowDialog() != true) return;
            if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024) throw new InvalidOperationException("图片不能超过 10 MB。");
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(dialog.FileName); bitmap.EndInit();
            if (bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096) throw new InvalidOperationException("图片最长边不能超过 4096 像素。");
            var item = Entry(app, signal); string path = Logic.CustomPath(app.Assets.Root, signal.Id + ".png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            item.Image = signal.Id + ".png"; app.Assets.Clear(); app.Save(); app.Preview(signal);
        }
        private static void ImportSound(Controller app, Signal signal)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PCM WAV 音频|*.wav", Title = "选择你有权使用的短提示音" };
            if (dialog.ShowDialog() != true) return;
            if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new InvalidOperationException("音频不能超过 2 MB。");
            byte[] bytes = File.ReadAllBytes(dialog.FileName);
            if (AssetLibrary.WaveDuration(bytes) > 5) throw new InvalidOperationException("提示音不能超过 5 秒。");
            using (var player = new SoundPlayer(new MemoryStream(bytes))) player.Load();
            var item = Entry(app, signal); app.Assets.Clear();
            File.WriteAllBytes(Logic.CustomPath(app.Assets.Root, signal.Id + ".wav"), bytes);
            item.Sound = signal.Id + ".wav"; app.Save(); app.Preview(signal);
        }
        private static void Reset(Controller app, Signal signal)
        {
            // Only remove the reference; preserve user-imported files on disk.
            app.Config.Assets.RemoveAll(a => a.Id == signal.Id); app.Assets.Clear(); app.Save(); app.Preview(signal);
        }
    }

    internal sealed class PreviewView : FrameworkElement
    {
        private readonly AssetLibrary assets;
        internal PreviewView(AssetLibrary library) { assets = library; }
        protected override void OnRender(DrawingContext d)
        {
            d.DrawRectangle(Art.Brush("#11151B"), null, new Rect(0, 0, 1280, 860));
            for (int x = 0; x < 1280; x += 40) d.DrawLine(new Pen(Art.Brush("#17202A"), 1), new Point(x, 0), new Point(x, 860));
            for (int y = 0; y < 860; y += 40) d.DrawLine(new Pen(Art.Brush("#17202A"), 1), new Point(0, y), new Point(1280, y));
            Art.Text(d, "非官方桌面信号", new Point(48, 32), 12, Art.Brush(Art.Amber, 1), false, true);
            Art.Text(d, "PUBG PING", new Point(48, 59), 34, Brushes.WhiteSmoke, false, true);
            Art.Text(d, "本机原始图标和提示音 · 24 个槽位", new Point(48, 111), 14, Art.Brush("#A5B0BE"), false, false);
            double[] cx = { 230, 640, 1050 };
            for (int page = 0; page < 3; page++)
            {
                d.DrawRoundedRectangle(Art.Brush("#651B212A"), new Pen(Art.Brush("#303A47"), 1), new Rect(cx[page] - 193, 179, 386, 493), 14, 14);
                var wheel = new WheelState { Center = new Point(cx[page], 405), Page = page, Selected = page == 0 ? 1 : page == 1 ? 2 : 5, Radius = 169 };
                Painter.Wheel(d, wheel, assets);
                var signal = Catalog.Pages[page][wheel.Selected];
                Painter.Ping(d, new PingState { Signal = signal, At = new Point(cx[page], 757), Duration = 2.8 }, .46, assets);
            }
            Art.Text(d, "按住 F8 / 可选中键     →     移动选择     →     松开发送", new Point(640, 817), 14, Brushes.WhiteSmoke, true, false);
        }
    }

    internal static class Tests
    {
        private static int count;
        private static void Check(bool value, string name)
        { if (!value) throw new Exception("TEST FAILED: " + name); count++; }
        internal static void Render(FrameworkElement view, int width, int height, string file)
        {
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); target.Render(view);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target));
            using (var stream = File.Create(file)) png.Save(stream);
        }
        internal static string Run()
        {
            count = 0;
            Check(Catalog.Pages.Length == 3 && Catalog.Pages.All(p => p.Length == 8), "three pages, eight slots");
            Check(Catalog.All.Select(s => s.Id).Distinct().Count() == 24, "unique signal IDs");
            Check(Logic.Pick(0, 0) == -1 && Logic.Pick(33, 0) == -1, "center cancellation");
            for (int i = 0; i < 8; i++)
            {
                double angle = (-90 + i * 45) * Math.PI / 180;
                Check(Logic.Pick(Math.Cos(angle) * 100, Math.Sin(angle) * 100) == i, "direction " + i);
            }
            Check(Logic.Pick(34, 0) == 2, "dead-zone boundary");
            Check(Logic.CustomPath(App.BaseDir, "../escape.png") == null, "reject parent traversal");
            Check(Logic.CustomPath(App.BaseDir, "C:\\escape.png") == null, "reject absolute path");
            Check(Logic.CustomPath(App.BaseDir, "enemy.png") != null, "accept local basename");
            var settings = new Settings { Trigger = 1, Radius = double.NaN, Duration = 900, LastPage = -1 };
            settings.Assets.Add(new AssetOverride { Id = "Combat_Slot1", Image = "../foo.png", Sound = "C:\\foo.wav" });
            settings.OnClose = "nope";
            settings.Normalize();
            Check(settings.Trigger == 119 && settings.Radius == 182 && settings.Duration == 5 && settings.LastPage == 2 && settings.OnClose == "ask", "normalize invalid settings");
            Check(new Settings().LastPage == 2 && Catalog.PageNames[2] == "表情符号", "emoji page is the default wheel");
            Check(settings.Assets[0].Image == "" && settings.Assets[0].Sound == "", "sanitize imported filenames");
            Check(Logic.Clamp(double.PositiveInfinity, 1, 5, 2.8) == 2.8, "reject infinity");
            using (var input = new Controller(new Settings()) { SimulatedInput = true })
            {
                Check(!input.RouteKey(0x100, 65, false) && !input.RouteKey(0x101, 65, false), "ordinary key passes through");
                Check(!input.RouteMouse(0x201, 0, false) && !input.RouteMouse(0x202, 0, false), "ordinary left click passes through");
                Check(!input.RouteMouse(0x20A, 120, false), "scroll passes through when idle");
                Check(!input.RouteKey(0x100, 119, true) && !input.GestureCaptured, "injected key ignored");
                Check(input.RouteKey(0x100, 119, false) && input.GestureCaptured && input.TriggerPhysicallyHeld, "trigger begins gesture");
                input.PollGesture();
                Check(input.GestureCaptured && input.TriggerPhysicallyHeld, "swallowed trigger stays open without GetAsyncKeyState");
                Check(input.RouteKey(0x100, 119, false), "trigger repeat consumed");
                Check(input.RouteKey(0x100, 0x25, false), "page key down consumed");
                Check(input.RouteKey(0x101, 119, false) && !input.GestureCaptured && input.LastReleaseCommits, "trigger up closes gesture");
                Check(input.RouteKey(0x101, 0x25, false), "page key up paired after trigger release");
                Check(!input.RouteKey(0x101, 119, false), "extra trigger up passes through");
                input.Config.Enabled = false;
                Check(!input.RouteKey(0x100, 119, false), "paused trigger passes through");
                input.Config.Enabled = true;
                Check(!input.RouteKey(0x100, 119, false), "held pass-through key never captured midway");
                Check(!input.RouteKey(0x101, 119, false), "pass-through key release preserved");
                Check(input.RouteKey(0x100, 119, false), "fresh trigger captured after resume");
                Check(input.RouteKey(0x100, 0x1B, false) && input.RouteKey(0x101, 0x1B, false), "escape down/up paired");
                Check(input.RouteKey(0x101, 119, false) && !input.LastReleaseCommits, "cancelled gesture never commits");
                input.SimulationBlocked = true;
                Check(!input.RouteKey(0x100, 119, false) && !input.GestureCaptured, "game/fullscreen foreground bypass");
                input.RouteKey(0x101, 119, false); input.SimulationBlocked = false; input.SimulationModifiers = true;
                Check(!input.RouteKey(0x100, 119, false), "modifier shortcut passes through");
                input.RouteKey(0x101, 119, false); input.SimulationModifiers = false;
                Check(input.RouteKey(0x100, 119, false), "trigger captured before rebind");
                input.Config.Trigger = 4;
                Check(input.RouteKey(0x101, 119, false), "release remains paired after rebind");
                Check(!input.RouteMouse(0x207, 0, true) && !input.GestureCaptured, "injected middle button ignored");
                Check(input.RouteMouse(0x207, 0, false) && input.GestureCaptured && input.TriggerPhysicallyHeld, "middle button begins gesture");
                input.PollGesture();
                Check(input.GestureCaptured, "swallowed middle button stays open");
                Check(input.RouteMouse(0x20A, -120, false), "wheel paging consumed only during gesture");
                Check(input.RouteMouse(0x204, 0, false) && input.RouteMouse(0x205, 0, false), "right cancel click is paired");
                Check(input.RouteMouse(0x208, 0, false) && !input.GestureCaptured && !input.LastReleaseCommits, "cancelled middle release paired");
                Check(!input.RouteMouse(0x204, 0, false) && !input.RouteMouse(0x205, 0, false), "ordinary right click preserved");
            }
            using (var assets = new Controller(new Settings()))
            {
                foreach (Signal signal in Catalog.All)
                {
                    var image = assets.Assets.Image(signal);
                    Check(image != null && image.PixelWidth == 240, "original wheel texture " + signal.Id);
                    Check(assets.Assets.Image(signal, true) != null, "original placed texture " + signal.Id);
                }
                string[] waves = Directory.GetFiles(Path.Combine(App.BaseDir, "assets", "game", "sounds"), "*.wav");
                Check(waves.Length == 15, "fifteen original Wwise source variants");
                foreach (string file in waves)
                {
                    byte[] wav = File.ReadAllBytes(file);
                    double duration = AssetLibrary.WaveDuration(wav);
                    Check(duration > .02 && duration < 2, "source wave length " + Path.GetFileName(file));
                    byte[] device = ClipPlayer.ToDevicePcm(wav);
                    int expect = (int)Math.Round(duration * 44100) * 2;
                    Check(device.Length % 2 == 0 && Math.Abs(device.Length - expect) <= 4, "device buffer " + Path.GetFileName(file));
                    using (var player = new SoundPlayer(file)) player.Load();
                    Check(true, "source WAV decoded " + Path.GetFileName(file));
                }
                using (var clip = new ClipPlayer())
                {
                    Check(clip.Ready, "wave device opened");
                    int played = 0;
                    for (int i = 0; i < 8; i++) if (clip.Submit(new byte[882])) played++;
                    Check(played == 8, "rapid silent buffers are not dropped");
                }
            }
            using (var controller = new Controller(new Settings()))
            {
                Render(new PreviewView(controller.Assets), 1280, 860, Path.Combine(App.BaseDir, "preview.png"));
                Check(File.Exists(Path.Combine(App.BaseDir, "preview.png")), "render all icons / wheel / pings");
                Render(SettingsUi.Build(controller), 900, 850, Path.Combine(App.BaseDir, "settings-preview.png"));
                Check(File.Exists(Path.Combine(App.BaseDir, "settings-preview.png")), "render settings");
            }
            return "PASS: " + count + " checks\r\n" +
                "Rendering: preview.png and settings-preview.png\r\n" +
                "Audio: 15 WAVs converted for the output device; eight rapid buffers accepted. Audibility not human-verified.\r\n" +
                "Input routing: keyboard/mouse pairs, repeat, cancel, rebind, modifiers, pause, game bypass, injected-event rejection.\r\n" +
                "Input tests call app methods in-process; no Windows input is injected.\r\n" +
                "Not covered: physical input gestures, mixed-DPI monitors, anti-cheat compatibility.\r\n";
        }
    }

    internal static class Brand
    {
        internal static System.Windows.Media.ImageSource WindowIcon()
        {
            using (var icon = ShellIcon())
            {
                if (icon == null) return null;
                var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
        }
        internal static System.Drawing.Icon ShellIcon()
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
    }

    internal sealed class App : Application
    {
        internal static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private Controller controller;
        private Mutex mutex;
        private bool ownsMutex;
        internal static bool Testing;
        internal static void Log(Exception e)
        {
            try { Directory.CreateDirectory(Path.Combine(BaseDir, "data")); File.AppendAllText(Path.Combine(BaseDir, "data", "error.log"), DateTime.Now.ToString("s") + " " + e.GetType().Name + ": " + e.Message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        internal static void Save(Settings settings)
        {
            if (Testing) return;
            try
            {
                string folder = Path.Combine(BaseDir, "data"); Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "settings.json"), temp = file + ".tmp";
                using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
            catch (Exception e) { Log(e); }
        }
        private static Settings Load()
        {
            var settings = new Settings(); string file = Path.Combine(BaseDir, "data", "settings.json");
            if (File.Exists(file)) try { using (var stream = File.OpenRead(file)) settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream); } catch (Exception e) { Log(e); }
            if (settings == null) settings = new Settings(); settings.Normalize(); return settings;
        }
        [STAThread] public static int Main(string[] args)
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if (args.Contains("--self-test"))
                {
                    Testing = true;
                    string report = Tests.Run(); File.WriteAllText(Path.Combine(BaseDir, "test-report.txt"), report, Encoding.UTF8); return 0;
                }
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                    Log(e.Exception); MessageBox.Show(e.Exception.Message, "PUBG PING"); app.Shutdown(1); e.Handled = true;
                };
                app.Startup += delegate {
                    app.mutex = new Mutex(true, "Local\\PUBGPing_20261015", out app.ownsMutex);
                    if (!app.ownsMutex)
                    {
                        Native.PostMessage(Native.FindWindow(null, "PUBG-Ping-Control"), 0x8001, IntPtr.Zero, IntPtr.Zero);
                        app.Shutdown(); return;
                    }
                    app.controller = new Controller(Load()); app.controller.Start();
                    if (args.Contains("--smoke-test"))
                    {
                        IntPtr foreground = Native.GetForegroundWindow();
                        app.controller.Preview(Catalog.Pages[0][0]);
                        app.controller.ShowSmokeWheel();
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                        timer.Tick += delegate {
                            timer.Stop();
                            File.WriteAllText(Path.Combine(BaseDir, "smoke-report.txt"),
                                "Hooks installed: yes\r\nDesktop wheel and ping rendered: yes\r\n" +
                                "Foreground unchanged: " + (foreground == Native.GetForegroundWindow()) + "\r\n" +
                                "Pause hotkey registered: " + app.controller.HotkeyRegistered + "\r\n" +
                                "Pause shortcut: " + app.controller.PauseShortcut + "\r\n" +
                                "Overlay render calls: " + app.controller.TotalFrames + "\r\n" +
                                app.controller.OverlayDiagnostics() +
                                "Sound requested; human audibility not verified.\r\n", Encoding.UTF8);
                            app.Shutdown();
                        }; timer.Start();
                    }
                    else app.controller.ShowSettings();
                };
                app.Exit += delegate {
                    if (app.controller != null) app.controller.Dispose();
                    if (app.mutex != null) { if (app.ownsMutex) app.mutex.ReleaseMutex(); app.mutex.Dispose(); }
                };
                return app.Run();
            }
            catch (Exception e)
            {
                Log(e); if (!Testing) MessageBox.Show(e.Message, "PUBG PING");
                try { File.WriteAllText(Path.Combine(BaseDir, "test-failure.txt"), e.ToString(), Encoding.UTF8); } catch { }
                return 1;
            }
        }
    }
}
