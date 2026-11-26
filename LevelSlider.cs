using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

sealed class LevelSlider : UserControl
{
    sealed class PercentTrackBar : TrackBar
    {
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;
        const int TBM_GETTHUMBRECT = 0x0419, TBM_GETCHANNELRECT = 0x041A;
        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, out NativeRect rect);

        Rectangle TrackRectangle(int message)
        {
            NativeRect rect;
            SendMessage(Handle, message, IntPtr.Zero, out rect);
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
            {
                var click = new Point(unchecked((int)m.LParam.ToInt64()));
                var thumb = TrackRectangle(TBM_GETTHUMBRECT);
                if (!thumb.Contains(click))
                {
                    var channel = TrackRectangle(TBM_GETCHANNELRECT);
                    int travel = channel.Width - thumb.Width;
                    if (travel > 0)
                    {
                        int offset = Math.Max(0, Math.Min(travel, click.X - channel.Left - thumb.Width / 2));
                        Value = Minimum + (offset * (Maximum - Minimum) + travel / 2) / travel;

                        // Let the native control capture the mouse and drag the newly selected thumb.
                        thumb = TrackRectangle(TBM_GETTHUMBRECT);
                        int x = thumb.Left + thumb.Width / 2;
                        int y = thumb.Top + thumb.Height / 2;
                        m.LParam = (IntPtr)((y << 16) | (x & 0xffff));
                    }
                }
            }
            base.WndProc(ref m);
        }

        int wheelDelta;
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            wheelDelta += e.Delta;
            int notch = SystemInformation.MouseWheelScrollDelta;
            Value = Math.Max(Minimum, Math.Min(Maximum, Value + wheelDelta / notch));
            wheelDelta %= notch;
        }
    }

    readonly TrackBar track = new PercentTrackBar {
        Minimum = 0, Maximum = 10, TickFrequency = 1, SmallChange = 1, LargeChange = 1
    };
    readonly Label value = new Label { TextAlign = ContentAlignment.MiddleRight };
    int? confirmed;
    bool updating, mouseDown, keyDown;

    public event EventHandler Edited;
    public int? Pending { get; private set; }
    public bool Holding { get { return mouseDown || keyDown || track.Capture; } }

    public LevelSlider(string title)
    {
        Size = new Size(344, 42);
        var label = new Label { Text = title, TextAlign = ContentAlignment.MiddleLeft };
        label.SetBounds(0, 0, 42, 28);
        track.AccessibleName = title + " (%)";
        track.SetBounds(44, 0, 249, 42);
        value.SetBounds(294, 0, 50, 28);
        Controls.AddRange(new Control[] { label, track, value });
        track.ValueChanged += delegate {
            if (!updating) Preview();
        };
        track.MouseDown += delegate(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { mouseDown = true; Preview(); }
        };
        track.MouseUp += delegate(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { mouseDown = false; Preview(); }
        };
        track.MouseCaptureChanged += delegate {
            if (!track.Capture) { mouseDown = false; NotifyEdited(); }
        };
        track.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode >= Keys.Prior && e.KeyCode <= Keys.Down) { keyDown = true; NotifyEdited(); }
        };
        track.KeyUp += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode >= Keys.Prior && e.KeyCode <= Keys.Down) { keyDown = false; Preview(); }
        };
        track.LostFocus += delegate { keyDown = false; NotifyEdited(); };
        ShowLevel(null);
    }

    public void ShowLevel(int? percent)
    {
        confirmed = percent;
        Pending = null;
        updating = true;
        try { track.Value = (percent.GetValueOrDefault() + 5) / 10; }
        finally { updating = false; }
        value.Text = percent.HasValue ? percent.Value + "%" : "—";
    }

    public void StepPending(int direction)
    {
        track.Value = Math.Max(track.Minimum, Math.Min(track.Maximum, track.Value + direction));
    }

    void Preview()
    {
        int percent = track.Value * 10;
        Pending = percent == confirmed ? (int?)null : percent;
        value.Text = percent + "%";
        NotifyEdited();
    }

    void NotifyEdited()
    {
        if (Edited != null) Edited(this, EventArgs.Empty);
    }
}
