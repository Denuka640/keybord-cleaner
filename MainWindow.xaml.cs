using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace KeyShield
{
    public partial class MainWindow : Window
    {
        // ══════════════════════════════════════════════════════════════════
        //  LOW-LEVEL KEYBOARD HOOK  (WH_KEYBOARD_LL)
        //
        //  Uses SetWindowsHookEx with WH_KEYBOARD_LL — a global, low-level
        //  Windows hook that intercepts all keyboard events BEFORE they
        //  reach any application or system handler.
        //
        //  Returning 1 (non-zero) from the hook procedure SUPPRESSES the
        //  key event entirely, including:
        //    ✓ All regular keys
        //    ✓ Windows key (VK_LWIN / VK_RWIN)
        //    ✓ Alt+Tab, Alt+F4, PrintScreen, etc.
        //
        //  Note: Ctrl+Alt+Del is the Windows Secure Attention Sequence
        //  and CANNOT be blocked by any user-mode application.
        // ══════════════════════════════════════════════════════════════════

        private const int WH_KEYBOARD_LL = 13;
        private const int HC_ACTION      = 0;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        // Native message pump
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hWnd; public uint Msg;
            public IntPtr wParam; public IntPtr lParam;
            public uint Time; public System.Drawing.Point Pt;
        }

        [DllImport("user32.dll")]
        private static extern int  GetMessage(ref NativeMessage msg, IntPtr hWnd, uint wMsgMin, uint wMsgMax);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage msg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage msg);
        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_QUIT = 0x0012;

        // ── Hook state ─────────────────────────────────────────────────
        private IntPtr               _hookHandle  = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc    = null!;   // keep ref — prevents GC collection
        private Thread?              _hookThread;
        private volatile bool        _isLocked    = false;
        private int                  _blockedCount = 0;

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= HC_ACTION && _isLocked)
            {
                int count = Interlocked.Increment(ref _blockedCount);

                // Marshal UI update back to the WPF Dispatcher — non-blocking
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    (Action)(() => OnKeyBlocked(count)));

                return (IntPtr)1; // ← non-zero = suppress the key event
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void StartHookThread()
        {
            _hookThread = new Thread(() =>
            {
                _hookProc   = HookCallback;                       // store ref before passing
                _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL,
                                  _hookProc, GetModuleHandle(null), 0);

                if (_hookHandle == IntPtr.Zero)
                {
                    Dispatcher.BeginInvoke(() =>
                        System.Windows.MessageBox.Show(
                            "Could not install keyboard hook.\n" +
                            "The keyboard blocker may not work correctly.",
                            "KeyShield — Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                // Keep this thread alive with a Windows message pump
                // (required for low-level hooks to receive callbacks)
                var msg = new NativeMessage();
                while (GetMessage(ref msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (_hookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
            })
            {
                IsBackground = true,
                Name         = "KeyShield-HookPump",
                Priority     = ThreadPriority.AboveNormal  // ensures low-latency hook callbacks
            };
            _hookThread.Start();
        }

        private void StopHookThread()
        {
            if (_hookThread is { IsAlive: true })
                PostThreadMessage((uint)_hookThread.ManagedThreadId,
                    WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        // ══════════════════════════════════════════════════════════════════
        //  SYSTEM TRAY
        // ══════════════════════════════════════════════════════════════════

        private NotifyIcon?  _tray;
        private ContextMenuStrip? _trayMenu;

        private void InitSystemTray()
        {
            // Build a 16×16 shield icon programmatically
            var bmp = new Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(new SolidBrush(System.Drawing.Color.FromArgb(124, 58, 237)), 0, 0, 15, 15);
                g.DrawString("K", new Font("Segoe UI", 8, System.Drawing.FontStyle.Bold),
                    System.Drawing.Brushes.White, new PointF(2, 1));
            }

            _trayMenu = new ContextMenuStrip();
            _trayMenu.BackColor   = System.Drawing.Color.FromArgb(16, 16, 30);
            _trayMenu.ForeColor   = System.Drawing.Color.FromArgb(241, 245, 249);
            _trayMenu.Font        = new Font("Segoe UI", 9);
            _trayMenu.RenderMode  = ToolStripRenderMode.System;

            var showItem = new ToolStripMenuItem("Show KeyShield");
            showItem.Click += (_, _) => ShowFromTray();

            var lockItem = new ToolStripMenuItem("Lock Keyboard");
            lockItem.Click += (_, _) =>
            {
                ShowFromTray();
                if (!_isLocked) LockBtn_Click(this, new RoutedEventArgs());
            };

            var unlockItem = new ToolStripMenuItem("Unlock Keyboard");
            unlockItem.Click += (_, _) =>
            {
                ShowFromTray();
                if (_isLocked) UnlockBtn_Click(this, new RoutedEventArgs());
            };

            var sep  = new ToolStripSeparator();

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) =>
            {
                _isLocked = false;
                System.Windows.Application.Current.Shutdown();
            };

            _trayMenu.Items.AddRange([showItem, sep, lockItem, unlockItem,
                new ToolStripSeparator(), exitItem]);

            _tray = new NotifyIcon
            {
                Icon             = System.Drawing.Icon.FromHandle(bmp.GetHicon()),
                Text             = "KeyShield — Keyboard Cleaner",
                ContextMenuStrip = _trayMenu,
                Visible          = true
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void UpdateTrayTip()
        {
            if (_tray != null)
                _tray.Text = _isLocked
                    ? $"KeyShield — LOCKED ({_blockedCount} blocked)"
                    : "KeyShield — Ready";
        }

        // ══════════════════════════════════════════════════════════════════
        //  ANIMATION ENGINE  (60fps via DispatcherTimer)
        // ══════════════════════════════════════════════════════════════════

        private readonly DispatcherTimer _animTimer = new();
        private readonly DispatcherTimer _sessionTimer = new();
        private double _phase   = 0;
        private int    _elapsed = 0;

        private void StartAnimations()
        {
            _animTimer.Interval = TimeSpan.FromMilliseconds(16);
            _animTimer.Tick    += AnimTick;
            _animTimer.Start();
        }

        private void AnimTick(object? s, EventArgs e)
        {
            _phase += 0.038;
            double sin = Math.Sin(_phase);
            double abs = Math.Abs(sin);

            if (!_isLocked)
            {
                IdleFloat.Y         = sin * 5.5;
                IdleGlow.BlurRadius = 18 + abs * 18;
                IdleGlow.Opacity    = 0.45 + abs * 0.3;
            }
            else
            {
                LockFloat.Y         = sin * 6.5;
                LockGlow.BlurRadius = 22 + abs * 22;
                LockGlow.Opacity    = 0.45 + abs * 0.4;

                // Pulse the status dot glow
                DotGlow.BlurRadius = 4 + abs * 10;
            }
        }

        private void StartSessionTimer()
        {
            _elapsed = 0;
            _sessionTimer.Interval = TimeSpan.FromSeconds(1);
            _sessionTimer.Tick    += (_, _) =>
            {
                _elapsed++;
                int m = _elapsed / 60, s = _elapsed % 60;
                TimerLabel.Text = $"{m:D2}:{s:D2}";
                UpdateTrayTip();
            };
            _sessionTimer.Start();
        }

        private void StopSessionTimer()
        {
            _sessionTimer.Stop();
            _elapsed = 0;
        }

        // ══════════════════════════════════════════════════════════════════
        //  COUNTER & PROGRESS UPDATE
        // ══════════════════════════════════════════════════════════════════

        private void OnKeyBlocked(int count)
        {
            CounterLabel.Text = count.ToString();

            // Scale-bump animation on the counter
            var scaleX = new DoubleAnimationUsingKeyFrames();
            var scaleY = new DoubleAnimationUsingKeyFrames();
            var kf1    = new LinearDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55)));
            var kf2    = new LinearDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)));
            scaleX.KeyFrames.Add(kf1); scaleX.KeyFrames.Add(kf2);
            scaleY.KeyFrames.Add(new LinearDoubleKeyFrame(1.18, kf1.KeyTime));
            scaleY.KeyFrames.Add(new LinearDoubleKeyFrame(1.00, kf2.KeyTime));
            CounterScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            CounterScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

            // Progress bar fill animation
            double parentWidth = ((System.Windows.Controls.Border)ProgressFill.Parent).ActualWidth;
            double target      = Math.Min(count / 100.0, 1.0) * parentWidth;
            var    widthAnim   = new DoubleAnimation(target, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, widthAnim);
        }

        // ══════════════════════════════════════════════════════════════════
        //  LOCK / UNLOCK
        // ══════════════════════════════════════════════════════════════════

        private void LockBtn_Click(object sender, RoutedEventArgs e)
        {
            _blockedCount = 0;
            _isLocked     = true;

            // Reset display
            CounterLabel.Text  = "0";
            TimerLabel.Text    = "00:00";
            ProgressFill.Width = 0;

            FadeSwitch(IdlePanel, LockedPanel);

            // Status → LOCKED
            StatusLabel.Text = "LOCKED";
            AnimColor(DotColor, Colors.Lime,  System.Windows.Media.Color.FromRgb(0xF4, 0x3F, 0x5E));
            AnimColor(DotGlow,  Colors.Green, System.Windows.Media.Color.FromRgb(0xF4, 0x3F, 0x5E));

            StartSessionTimer();
            UpdateTrayTip();

            // Show balloon notification from tray
            _tray?.ShowBalloonTip(2000, "KeyShield",
                "⌨️ Keyboard locked — clean away!", ToolTipIcon.Info);
        }

        private void UnlockBtn_Click(object sender, RoutedEventArgs e)
        {
            _isLocked = false;

            StopSessionTimer();
            FadeSwitch(LockedPanel, IdlePanel);

            // Status → READY
            StatusLabel.Text = "READY";
            AnimColor(DotColor, System.Windows.Media.Color.FromRgb(0xF4, 0x3F, 0x5E), Colors.Lime);
            AnimColor(DotGlow,  System.Windows.Media.Color.FromRgb(0xF4, 0x3F, 0x5E), Colors.Green);

            UpdateTrayTip();

            _tray?.ShowBalloonTip(2000, "KeyShield",
                $"✅ Unlocked! {_blockedCount} key press{(_blockedCount == 1 ? "" : "es")} blocked.",
                ToolTipIcon.Info);
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static void FadeSwitch(UIElement hide, UIElement show)
        {
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var easeIn  = new CubicEase { EasingMode = EasingMode.EaseIn };

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = easeIn };
            fadeOut.Completed += (_, _) =>
            {
                hide.Visibility = Visibility.Collapsed;
                hide.Opacity    = 1;
                show.Opacity    = 0;
                show.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                { EasingFunction = easeOut };
                show.BeginAnimation(OpacityProperty, fadeIn);
            };
            hide.BeginAnimation(OpacityProperty, fadeOut);
        }

        private static void AnimColor(SolidColorBrush brush, System.Windows.Media.Color from, System.Windows.Media.Color to) =>
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(from, to, TimeSpan.FromMilliseconds(380)));

        private static void AnimColor(DropShadowEffect glow, System.Windows.Media.Color from, System.Windows.Media.Color to) =>
            glow.BeginAnimation(DropShadowEffect.ColorProperty,
                new ColorAnimation(from, to, TimeSpan.FromMilliseconds(380)));

        // ══════════════════════════════════════════════════════════════════
        //  WINDOW LIFECYCLE
        // ══════════════════════════════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();
            StartHookThread();
            InitSystemTray();
            StartAnimations();
        }

        protected override void OnClosed(EventArgs e)
        {
            _isLocked = false;
            _animTimer.Stop();
            _sessionTimer.Stop();
            StopHookThread();
            _tray?.Dispose();
            base.OnClosed(e);
        }

        // Minimize to tray instead of taskbar
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _tray?.ShowBalloonTip(1500, "KeyShield",
                    "Running in the system tray.", ToolTipIcon.Info);
            }
        }

        // Title bar drag
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isLocked = false;
            Close();
        }

        private void MinBtn_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void TrayBtn_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            _tray?.ShowBalloonTip(1500, "KeyShield",
                "Minimized to system tray. Double-click icon to reopen.", ToolTipIcon.Info);
        }
    }
}