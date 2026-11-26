using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle(MonitorControl.AppName)]
[assembly: System.Reflection.AssemblyProduct(MonitorControl.AppName)]

class MonitorControl : Form
{
    internal const string AppName = "Monitor Control";
    const byte Brightness = 0x10;
    const byte SpeakerVolume = 0x62;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("dxva2.dll", SetLastError=true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr screen, out uint count);
    [DllImport("dxva2.dll", SetLastError=true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr screen, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError=true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, IntPtr type, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError=true)] static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

    readonly Panel panel = new Panel { Dock = DockStyle.Fill };
    readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8, 4, 8, 0) };
    readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox address = new TextBox();
    readonly Button pair = new Button { Text = "페어링" };
    readonly LevelSlider brightnessSlider = new LevelSlider("밝기");
    readonly LevelSlider volumeSlider = new LevelSlider("볼륨");
    readonly Timer sliderTimer = new Timer { Interval = 600 };
    SamsungMonitor samsung;
    bool Wifi { get { return mode.SelectedIndex == 1; } }
    SamsungMonitor Target { get { return Wifi && samsung != null && samsung.Host == address.Text.Trim() ? samsung : null; } }

    MonitorControl()
    {
        Text = AppName;
        Icon = new Icon(typeof(MonitorControl), "MonitorControl.ico");
        ClientSize = new Size(360, 246);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Controls.Add(panel);
        Controls.Add(status);
        mode.Items.AddRange(new object[] { "현재 창의 모니터 (DDC/CI)", "삼성 모니터 (Wi-Fi)" });
        mode.SetBounds(8, 8, 344, 24);
        var label = new Label { Text = "모니터 IP", TextAlign = ContentAlignment.MiddleLeft };
        label.SetBounds(8, 41, 65, 26);
        address.SetBounds(76, 44, 186, 24);
        pair.SetBounds(268, 40, 84, 29);
        panel.Controls.AddRange(new Control[] { mode, label, address, pair });
        Add("− 밝기 낮추기", 8, 80, Brightness, -1);
        Add("+ 밝기 높이기", 185, 80, Brightness, 1);
        Add("− 볼륨 낮추기", 8, 134, SpeakerVolume, -1);
        Add("+ 볼륨 높이기", 185, 134, SpeakerVolume, 1);
        brightnessSlider.Location = new Point(8, 191);
        volumeSlider.Location = new Point(8, 236);
        panel.Controls.AddRange(new Control[] { brightnessSlider, volumeSlider });
        brightnessSlider.Edited += delegate { ScheduleSliders(); };
        volumeSlider.Edited += delegate { ScheduleSliders(); };
        sliderTimer.Tick += async delegate {
            sliderTimer.Stop();
            var target = Target;
            if (target == null || brightnessSlider.Holding || volumeSlider.Holding) return;
            await Run("조절 중…", async delegate {
                string message = "";
                foreach (var slider in new[] { brightnessSlider, volumeSlider })
                {
                    if (IsDisposed || !slider.Pending.HasValue) continue;
                    bool volume = slider == volumeSlider;
                    int percent = slider.Pending.Value;
                    int actual = await UpdateLevel(slider, () => target.SetPercent(volume, percent));
                    message = target.Describe(volume, actual);
                }
                return message;
            });
        };

        mode.SelectedIndexChanged += async delegate {
            address.Enabled = pair.Enabled = label.Enabled = Wifi;
            brightnessSlider.Visible = volumeSlider.Visible = Wifi;
            ClientSize = new Size(ClientSize.Width, Wifi ? 346 : 246);
            await TargetChanged();
        };
        address.TextChanged += async delegate { await TargetChanged(); };
        Shown += async delegate { if (Target != null) await TargetChanged(); };
        pair.Click += async delegate {
            string host = address.Text.Trim();
            ClearSliders();
            await Run("모니터 화면에서 연결을 허용하세요.", async delegate {
                samsung = await Task.Run(delegate {
                    var paired = SamsungMonitor.Pair(host);
                    paired.Save();
                    return paired;
                });
                return await ReadSliders(samsung);
            });
        };
        mode.SelectedIndex = 0;
        try
        {
            samsung = SamsungMonitor.Load();
            if (samsung != null)
            {
                address.Text = samsung.Host;
                mode.SelectedIndex = 1;
            }
        }
        catch (Exception) { mode.SelectedIndex = 1; status.Text = "페어링 정보를 읽을 수 없습니다. 다시 페어링하세요."; }
    }
    void Add(string title, int left, int top, byte feature, int direction)
    {
        var button = new Button { Text = title };
        button.SetBounds(left, top, 167, 46);
        button.Click += async delegate {
            var target = Target;
            if (Wifi && target == null)
            {
                status.Text = "입력한 IP의 모니터와 먼저 페어링하세요.";
                return;
            }
            var window = Handle;
            bool volume = feature == SpeakerVolume;
            var slider = volume ? volumeSlider : brightnessSlider;
            if (target != null && slider.Pending.HasValue)
            {
                slider.StepPending(direction);
                return;
            }
            await Run("조절 중…", async delegate {
                if (target == null) return await Task.Run(() => Adjust(window, feature, direction));
                int actual = await UpdateLevel(slider, () => target.AdjustPercent(volume, direction));
                return target.Describe(volume, actual);
            });
        };
        panel.Controls.Add(button);
    }

    void ClearSliders()
    {
        sliderTimer.Stop();
        brightnessSlider.ShowLevel(null);
        volumeSlider.ShowLevel(null);
    }

    async Task TargetChanged()
    {
        ClearSliders();
        var target = Target;
        brightnessSlider.Enabled = volumeSlider.Enabled = target != null;
        status.Text = Wifi ? "모니터 IP를 입력하고 페어링하세요." : "조절할 모니터에 이 창을 놓으세요.";
        if (Visible && target != null) await Run("현재 값 읽는 중…", () => ReadSliders(target));
    }

    async Task<string> ReadSliders(SamsungMonitor target)
    {
        string message = "페어링된 모니터  " + target.Host;
        foreach (var slider in new[] { brightnessSlider, volumeSlider })
        {
            if (IsDisposed) break;
            slider.Enabled = true;
            bool volume = slider == volumeSlider;
            try { await UpdateLevel(slider, () => target.ReadPercent(volume)); }
            catch (Exception ex) { message = (volume ? "볼륨" : "밝기") + " 조회 실패 — " + ex.Message; }
        }
        return message;
    }

    async Task<int> UpdateLevel(LevelSlider slider, Func<int> operation)
    {
        try
        {
            int actual = await Task.Run(operation);
            if (!IsDisposed) slider.ShowLevel(actual);
            return actual;
        }
        catch
        {
            if (!IsDisposed) slider.ShowLevel(null);
            throw;
        }
    }

    void ScheduleSliders()
    {
        if (IsDisposed || Disposing) return;
        sliderTimer.Stop();
        if (panel.Enabled && Target != null && !brightnessSlider.Holding && !volumeSlider.Holding &&
            (brightnessSlider.Pending.HasValue || volumeSlider.Pending.HasValue))
            sliderTimer.Start();
    }

    async Task Run(string progress, Func<Task<string>> action)
    {
        sliderTimer.Stop();
        panel.Enabled = false;
        status.Text = progress;
        try
        {
            string message = await action();
            if (!IsDisposed) status.Text = message;
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = "모니터 제어 실패 — " + ex.Message; }
        finally
        {
            if (!IsDisposed) { panel.Enabled = true; ScheduleSliders(); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) sliderTimer.Dispose();
        base.Dispose(disposing);
    }

    public static string Adjust(IntPtr window, byte feature, int direction)
    {
        var screen = MonitorFromWindow(window, 2);
        uint count;
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(screen, out count)) throw new Win32Exception();
        if (count != 1) throw new InvalidOperationException("Windows에서 대상 화면을 하나로 식별할 수 없습니다.");
        var monitors = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(screen, count, monitors)) throw new Win32Exception();
        try
        {
            var monitor = monitors[0];
            uint current, maximum;
            string label = feature == SpeakerVolume ? "볼륨" : "밝기";
            if (!GetVCPFeatureAndVCPFeatureReply(monitor.Handle, feature, IntPtr.Zero, out current, out maximum))
                throw new Win32Exception();
            if (maximum == 0 || current > maximum) throw new InvalidOperationException(label + " 범위를 확인할 수 없습니다.");
            long step = Math.Max(1L, (long)Math.Round(maximum / 10.0));
            uint target = (uint)Math.Max(0L, Math.Min((long)maximum, current + direction * step));
            if (target != current && !SetVCPFeature(monitor.Handle, feature, target)) throw new Win32Exception();
            return monitor.Name + "   " + label + " " + Math.Round(100.0 * target / maximum) + "%";
        }
        finally { DestroyPhysicalMonitors(count, monitors); }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new MonitorControl());
    }
}
