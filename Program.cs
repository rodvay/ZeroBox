using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;

namespace ZeroBox
{
    internal sealed class LaunchConfig
    {
        public bool DemoMode;
        public string ForceLang;
        public bool StartMinimized;

        public static LaunchConfig Parse(string[] args)
        {
            LaunchConfig c = new LaunchConfig();
            if (args == null) return c;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim();
                if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) c.DemoMode = true;
                else if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) c.StartMinimized = true;
                else if (a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) c.ForceLang = a.Substring(7).Trim().ToLowerInvariant();
            }
            return c;
        }
    }

    internal static class UiTheme
    {
        public static readonly Color Bg = Color.FromArgb(11, 15, 22);
        public static readonly Color Surface = Color.FromArgb(17, 23, 33);
        public static readonly Color SurfaceAlt = Color.FromArgb(22, 30, 44);
        public static readonly Color Hover = Color.FromArgb(28, 39, 57);
        public static readonly Color Border = Color.FromArgb(36, 48, 68);
        public static readonly Color BorderLight = Color.FromArgb(58, 75, 104);
        public static readonly Color Text = Color.FromArgb(241, 245, 249);
        public static readonly Color Muted = Color.FromArgb(148, 163, 184);
        public static readonly Color Subtle = Color.FromArgb(100, 116, 139);
        public static readonly Color Cyan = Color.FromArgb(56, 189, 248);
        public static readonly Color Emerald = Color.FromArgb(52, 211, 153);
        public static readonly Color Amber = Color.FromArgb(251, 191, 36);
        public static readonly Color Violet = Color.FromArgb(167, 139, 250);
        public static readonly Color Coral = Color.FromArgb(248, 113, 113);

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, RectangleF r, float rad, Color fill)
        {
            using (GraphicsPath p = RoundedRect(r, rad))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        public static void DrawRounded(Graphics g, RectangleF r, float rad, Color stroke, float width)
        {
            using (GraphicsPath p = RoundedRect(r, rad))
            using (Pen pen = new Pen(stroke, width))
                g.DrawPath(pen, p);
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppVersion = "1.0.0";

        private readonly LaunchConfig _launch;
        private readonly ZeroBoxSettings _cfg;
        private readonly ModuleManager _mgr;

        private string _lang = "en";
        private string _filter = "all"; // all, installed, updates, available
        private bool _checkingUpdates;
        private bool _startupCached;
        private bool _allowRealClose;

        private float _scale = 1.0f;
        private string _hoverId = "";
        private readonly Dictionary<string, Rectangle> _hitRects = new Dictionary<string, Rectangle>();
        private readonly Dictionary<string, Rectangle> _cardRects = new Dictionary<string, Rectangle>();

        private string _toastText = "";
        private DateTime _toastUntil = DateTime.MinValue;
        private System.Windows.Forms.Timer _uiTimer;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ContextMenuStrip _cardMenu;
        private ToolModule _ctxMod;

        // Tracked GDI Fonts (zero leaks)
        private Font _fTitle;
        private Font _fVer;
        private Font _fSub;
        private Font _fCardTitle;
        private Font _fBody;
        private Font _fSmall;
        private Font _fPill;
        private Font _fMono;

        public MainForm(LaunchConfig launch)
        {
            _launch = launch ?? new LaunchConfig();
            _cfg = ZeroBoxSettings.Load();
            if (!string.IsNullOrEmpty(_launch.ForceLang) && (_launch.ForceLang == "en" || _launch.ForceLang == "tr" || _launch.ForceLang == "zh"))
                _lang = _launch.ForceLang;
            else
                _lang = _cfg.Language;

            _mgr = new ModuleManager(_cfg.ResolveModulesRoot(), _launch.DemoMode);
            _startupCached = WinNative.IsStartupEnabled();

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Text = "ZeroBox v" + AppVersion;
            BackColor = UiTheme.Bg;
            ForeColor = UiTheme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _scale = ComputeScale();
            RebuildFonts();
            ClientSize = new Size(S(960), S(640));
            MinimumSize = new Size(S(880), S(590));

            InitTray();
            _cardMenu = new ContextMenuStrip();

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 500;
            _uiTimer.Tick += delegate
            {
                if (!string.IsNullOrEmpty(_toastText) && DateTime.UtcNow >= _toastUntil)
                {
                    _toastText = "";
                    Invalidate();
                }
            };
            _uiTimer.Start();
        }

        private int S(float v) { return (int)Math.Round(v * _scale); }

        private float ComputeScale()
        {
            try
            {
                if (IsHandleCreated)
                {
                    uint d = WinNative.GetDpiForWindow(Handle);
                    if (d >= 72 && d <= 480) return d / 96f;
                }
            }
            catch { }
            try
            {
                using (Graphics g = CreateGraphics())
                    if (g.DpiX >= 72f) return g.DpiX / 96f;
            }
            catch { }
            return 1.0f;
        }

        private void RebuildFonts()
        {
            string fam = Loc.FontFamily(_lang);
            float px = (96f / 72f) * _scale;
            Font oTitle = _fTitle, oVer = _fVer, oSub = _fSub, oCard = _fCardTitle;
            Font oBody = _fBody, oSmall = _fSmall, oPill = _fPill, oMono = _fMono;

            _fTitle = new Font("Segoe UI", 14.2f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fVer = new Font("Consolas", 8.0f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fSub = new Font(fam, 8.1f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fCardTitle = new Font("Segoe UI", 11.2f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBody = new Font(fam, 8.5f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font(fam, 7.6f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fPill = new Font(fam, 8.1f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fMono = new Font("Consolas", 8.0f * px, FontStyle.Bold, GraphicsUnit.Pixel);

            if (oTitle != null) oTitle.Dispose();
            if (oVer != null) oVer.Dispose();
            if (oSub != null) oSub.Dispose();
            if (oCard != null) oCard.Dispose();
            if (oBody != null) oBody.Dispose();
            if (oSmall != null) oSmall.Dispose();
            if (oPill != null) oPill.Dispose();
            if (oMono != null) oMono.Dispose();
        }

        private void DisposeFonts()
        {
            if (_fTitle != null) { _fTitle.Dispose(); _fTitle = null; }
            if (_fVer != null) { _fVer.Dispose(); _fVer = null; }
            if (_fSub != null) { _fSub.Dispose(); _fSub = null; }
            if (_fCardTitle != null) { _fCardTitle.Dispose(); _fCardTitle = null; }
            if (_fBody != null) { _fBody.Dispose(); _fBody = null; }
            if (_fSmall != null) { _fSmall.Dispose(); _fSmall = null; }
            if (_fPill != null) { _fPill.Dispose(); _fPill = null; }
            if (_fMono != null) { _fMono.Dispose(); _fMono = null; }
        }

        private void InitTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = Icon ?? SystemIcons.Application;
            _trayIcon.Text = "ZeroBox v" + AppVersion;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = !_launch.DemoMode;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_trayMenu == null) return;
            _trayMenu.Items.Clear();
            _trayMenu.Items.Add(Loc.T(_lang, "Open ZeroBox", "ZeroBox'ı Aç", "打开 ZeroBox"), null, delegate { RestoreFromTray(); });
            _trayMenu.Items.Add(new ToolStripSeparator());

            int installedAdded = 0;
            for (int i = 0; i < _mgr.Modules.Count; i++)
            {
                ToolModule mod = _mgr.Modules[i];
                if (mod.State == ModuleState.Installed || mod.State == ModuleState.UpdateAvailable)
                {
                    ToolModule captured = mod;
                    string lbl = "▶  " + captured.Name + (string.IsNullOrEmpty(captured.InstalledVersion) ? "" : " (" + captured.InstalledVersion + ")");
                    _trayMenu.Items.Add(lbl, null, delegate { HandleLaunch(captured, false); });
                    installedAdded++;
                }
            }
            if (installedAdded == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem(Loc.T(_lang, "(No modules installed yet)", "(Henüz kurulu modül yok)", "（尚未安装任何模块）"));
                empty.Enabled = false;
                _trayMenu.Items.Add(empty);
            }

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Loc.T(_lang, "Check All Updates", "Güncellemeleri Denetle", "检查全部更新"), null, delegate { StartCheckAllUpdates(); });
            _trayMenu.Items.Add(Loc.T(_lang, "Exit ZeroBox", "ZeroBox'tan Çık", "退出 ZeroBox"), null, delegate { ExitAppCleanly(); });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scale = ComputeScale();
            RebuildFonts();
            MinimumSize = new Size(S(880), S(590));
            WinNative.ApplyDarkTitleBar(Handle, UiTheme.Bg, UiTheme.Text);
            WinNative.AllowWakeMessage(Handle);
            if (!_launch.DemoMode) StartCheckAllUpdates();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_launch.StartMinimized && !_launch.DemoMode)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WinNative.WM_DPICHANGED)
            {
                int newDpi = m.WParam.ToInt32() & 0xFFFF;
                if (newDpi >= 72 && newDpi <= 480)
                {
                    _scale = newDpi / 96f;
                    RebuildFonts();
                    MinimumSize = new Size(S(880), S(590));
                    Invalidate();
                }
            }
            else if (WinNative.WM_SHOW_EXISTING != 0 && (uint)m.Msg == WinNative.WM_SHOW_EXISTING)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { StartCheckAllUpdates(); e.Handled = true; }
            else if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                int idx = (int)(e.KeyCode - Keys.D1);
                if (idx >= 0 && idx < _mgr.Modules.Count)
                {
                    ToolModule m = _mgr.Modules[idx];
                    if (m.State == ModuleState.Installed || m.State == ModuleState.UpdateAvailable)
                        HandleLaunch(m, false);
                }
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowRealClose && !_launch.DemoMode && _cfg.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_uiTimer != null) { _uiTimer.Stop(); _uiTimer.Dispose(); _uiTimer = null; }
                if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
                if (_trayMenu != null) { _trayMenu.Dispose(); _trayMenu = null; }
                if (_cardMenu != null) { _cardMenu.Dispose(); _cardMenu = null; }
            }
            base.Dispose(disposing);
            if (disposing) DisposeFonts();
        }

        private void ShowToast(string msg)
        {
            _toastText = msg ?? "";
            _toastUntil = DateTime.UtcNow.AddSeconds(4.5);
            Invalidate();
        }

        private void SafeUi(Action act)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || Disposing) return;
                    act();
                });
            }
            catch { }
        }

        private void SwitchLanguage(string lang)
        {
            if (_lang == lang) return;
            _lang = lang;
            _cfg.Language = lang;
            if (!_launch.DemoMode) _cfg.Save();
            RebuildFonts();
            RebuildTrayMenu();
            Invalidate();
        }

        private void StartCheckAllUpdates()
        {
            if (_checkingUpdates) return;
            if (_launch.DemoMode)
            {
                ShowToast(Loc.T(_lang, "Demo: All 6 GitHub Releases checked (1 update available).",
                    "Demo: 6 GitHub sürümü denetlendi (1 güncelleme hazır).",
                    "演示：已检测全部 6 个 GitHub 版本（发现 1 个可更新模块）。"));
                return;
            }
            _checkingUpdates = true;
            ShowToast(Loc.T(_lang, "Checking GitHub Releases for all 6 modules...",
                "6 modül için GitHub sürümleri denetleniyor...",
                "正在向 GitHub Releases 查询 6 个模块的最新版本..."));

            bool useMirror = _cfg.UseMirror;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int okCnt = 0;
                int updCnt = 0;
                for (int i = 0; i < _mgr.Modules.Count; i++)
                {
                    ToolModule m = _mgr.Modules[i];
                    string err;
                    if (_mgr.FetchLatestMetadata(m, useMirror, out err)) okCnt++;
                    if (m.State == ModuleState.UpdateAvailable) updCnt++;
                    SafeUi(delegate { Invalidate(); });
                }
                SafeUi(delegate
                {
                    _checkingUpdates = false;
                    RebuildTrayMenu();
                    if (okCnt == 0)
                        ShowToast(Loc.T(_lang, "Could not reach GitHub. Try toggling GitHub Mirror in the toolbar.",
                            "GitHub'a ulaşılamadı. Üst çubuktan Ayna (Mirror) seçeneğini deneyin.",
                            "无法连接 GitHub，可尝试开启工具栏中的「加速镜像」开关。"));
                    else if (updCnt > 0)
                        ShowToast(Loc.T(_lang,
                            Loc.F("Checked {0}/6 modules · {1} update(s) ready.", okCnt, updCnt),
                            Loc.F("{0}/6 modül denetlendi · {1} güncelleme hazır.", okCnt, updCnt),
                            Loc.F("已检测 {0}/6 个模块 · 发现 {1} 个可用更新。", okCnt, updCnt)));
                    else
                        ShowToast(Loc.T(_lang,
                            Loc.F("Checked {0}/6 modules · Everything is up to date.", okCnt),
                            Loc.F("{0}/6 modül denetlendi · Tümü güncel.", okCnt),
                            Loc.F("已检测 {0}/6 个模块 · 全部为最新版本。", okCnt)));
                });
            });
        }

        private void StartInstallOrUpdate(ToolModule m)
        {
            if (m == null || m.State == ModuleState.Downloading) return;
            m.State = ModuleState.Downloading;
            m.ProgressPct = 2;
            m.ErrorText = "";
            Invalidate();

            bool useMirror = _cfg.UseMirror;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                bool ok = _mgr.DownloadAndInstallModule(m, useMirror,
                    delegate(int pct)
                    {
                        m.ProgressPct = pct;
                        SafeUi(delegate { Invalidate(); });
                    }, out err);

                SafeUi(delegate
                {
                    if (ok)
                    {
                        RebuildTrayMenu();
                        ShowToast(Loc.T(_lang,
                            Loc.F("{0} {1} installed & SHA-256 verified.", m.Name, m.InstalledVersion),
                            Loc.F("{0} {1} kuruldu ve SHA-256 doğrulandı.", m.Name, m.InstalledVersion),
                            Loc.F("{0} {1} 安装完成并已通过 SHA-256 校验。", m.Name, m.InstalledVersion)));
                    }
                    else
                    {
                        _mgr.ScanLocalInstallations();
                        if (err == "RUNNING")
                        {
                            ShowToast(Loc.T(_lang,
                                Loc.F("Please close {0} before updating it.", m.Name),
                                Loc.F("Güncellemeden önce lütfen {0} uygulamasını kapatın.", m.Name),
                                Loc.F("更新前请先关闭正在运行的 {0}。", m.Name)));
                        }
                        else
                        {
                            m.ErrorText = err;
                            ShowToast(Loc.T(_lang,
                                Loc.F("{0} download failed: {1}", m.Name, err),
                                Loc.F("{0} indirilemedi: {1}", m.Name, err),
                                Loc.F("{0} 下载失败：{1}", m.Name, err)));
                        }
                    }
                    Invalidate();
                });
            });
        }

        private void HandleLaunch(ToolModule m, bool asAdmin)
        {
            if (m == null) return;
            if (_launch.DemoMode)
            {
                ShowToast(Loc.T(_lang, "Demo: Launching " + m.Name + (asAdmin ? " (Admin)" : ""),
                    "Demo: " + m.Name + " başlatılıyor" + (asAdmin ? " (Yönetici)" : ""),
                    "演示：正在启动 " + m.Name + (asAdmin ? "（管理员）" : "")));
                return;
            }
            string err;
            if (_mgr.LaunchModule(m, asAdmin, out err))
                ShowToast(Loc.T(_lang, "Launched " + m.Name, m.Name + " başlatıldı.", "已启动 " + m.Name));
            else
                ShowToast(Loc.T(_lang, "Launch failed: " + err, "Başlatılamadı: " + err, "启动失败：" + err));
        }

        private void HandleRemove(ToolModule m)
        {
            if (m == null) return;
            string msg = Loc.T(_lang,
                Loc.F("Remove module \"{0}\" from ZeroBox?", m.Name),
                Loc.F("\"{0}\" modülünü ZeroBox'tan kaldırmak istiyor musunuz?", m.Name),
                Loc.F("确定要从 ZeroBox 移除模块「{0}」吗？", m.Name));
            if (MessageBox.Show(this, msg, "ZeroBox", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string err;
            if (_mgr.RemoveModule(m, out err))
            {
                RebuildTrayMenu();
                ShowToast(Loc.T(_lang, "Removed " + m.Name, m.Name + " kaldırıldı.", "已移除 " + m.Name));
                Invalidate();
            }
            else if (err == "RUNNING")
            {
                ShowToast(Loc.T(_lang,
                    Loc.F("Please close {0} before removing it.", m.Name),
                    Loc.F("Kaldırmadan önce lütfen {0} uygulamasını kapatın.", m.Name),
                    Loc.F("移除前请先关闭正在运行的 {0}。", m.Name)));
            }
            else
            {
                ShowToast(Loc.T(_lang, "Remove failed: " + err, "Kaldırılamadı: " + err, "移除失败：" + err));
            }
        }

        private void ShowCardContextMenu(ToolModule m, Point screenPt)
        {
            if (m == null || _cardMenu == null) return;
            _ctxMod = m;
            _cardMenu.Items.Clear();
            bool inst = (m.State == ModuleState.Installed || m.State == ModuleState.UpdateAvailable);
            if (inst)
            {
                _cardMenu.Items.Add(Loc.T(_lang, "Launch " + m.Name, m.Name + " Başlat", "启动 " + m.Name), null, delegate { HandleLaunch(_ctxMod, false); });
                _cardMenu.Items.Add(Loc.T(_lang, "Launch as Administrator", "Yönetici Olarak Başlat", "以管理员身份启动"), null, delegate { HandleLaunch(_ctxMod, true); });
                _cardMenu.Items.Add(Loc.T(_lang, "Open Module Folder", "Modül Klasörünü Aç", "打开模块目录"), null, delegate { WinNative.OpenFolderSafe(_mgr.GetModuleDir(_ctxMod)); });
                _cardMenu.Items.Add(new ToolStripSeparator());
                _cardMenu.Items.Add(Loc.T(_lang, "Reinstall / Update", "Yeniden Kur / Güncelle", "重新安装 / 更新"), null, delegate { StartInstallOrUpdate(_ctxMod); });
                _cardMenu.Items.Add(Loc.T(_lang, "Remove Module", "Modülü Kaldır", "移除模块"), null, delegate { HandleRemove(_ctxMod); });
            }
            else
            {
                _cardMenu.Items.Add(Loc.T(_lang, "Install " + m.Name, m.Name + " Kur", "安装 " + m.Name), null, delegate { StartInstallOrUpdate(_ctxMod); });
            }
            _cardMenu.Items.Add(new ToolStripSeparator());
            _cardMenu.Items.Add(Loc.T(_lang, "Open GitHub Repository ↗", "GitHub Deposunu Aç ↗", "打开 GitHub 项目主页 ↗"), null, delegate { WinNative.OpenUrlSafe(_ctxMod.RepoUrl); });
            _cardMenu.Show(screenPt);
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ExitAppCleanly()
        {
            _allowRealClose = true;
            Close();
        }

        private int MeasurePillW(Graphics g, string text, Font f, int padX)
        {
            return (int)Math.Ceiling(g.MeasureString(text, f).Width) + S(padX * 2);
        }

        private List<ToolModule> GetFilteredModules()
        {
            List<ToolModule> list = new List<ToolModule>();
            for (int i = 0; i < _mgr.Modules.Count; i++)
            {
                ToolModule m = _mgr.Modules[i];
                if (_filter == "installed" && m.State != ModuleState.Installed && m.State != ModuleState.UpdateAvailable) continue;
                if (_filter == "updates" && m.State != ModuleState.UpdateAvailable) continue;
                if (_filter == "available" && m.State != ModuleState.NotInstalled) continue;
                list.Add(m);
            }
            return list;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.Bg);
            _hitRects.Clear();
            _cardRects.Clear();

            int pad = S(16);
            int right = ClientSize.Width - pad;

            int installedCnt = 0, updatesCnt = 0, availCnt = 0;
            long installedBytes = 0;
            for (int i = 0; i < _mgr.Modules.Count; i++)
            {
                ToolModule m = _mgr.Modules[i];
                if (m.State == ModuleState.Installed) { installedCnt++; installedBytes += m.InstalledBytes; }
                else if (m.State == ModuleState.UpdateAvailable) { installedCnt++; updatesCnt++; installedBytes += m.InstalledBytes; }
                else if (m.State == ModuleState.NotInstalled) availCnt++;
            }

            using (StringFormat sfNear = new StringFormat(StringFormatFlags.NoWrap))
            using (StringFormat sfCenter = new StringFormat(StringFormatFlags.NoWrap))
            using (StringFormat sfFar = new StringFormat(StringFormatFlags.NoWrap))
            using (StringFormat sfWrap = new StringFormat())
            {
                sfNear.LineAlignment = StringAlignment.Center;
                sfNear.Trimming = StringTrimming.EllipsisCharacter;
                sfCenter.Alignment = StringAlignment.Center;
                sfCenter.LineAlignment = StringAlignment.Center;
                sfFar.Alignment = StringAlignment.Far;
                sfFar.LineAlignment = StringAlignment.Center;
                sfWrap.Trimming = StringTrimming.EllipsisWord;

                // ── 1. HEADER BAR ──
                int hdrH = S(56);
                using (SolidBrush bHdr = new SolidBrush(UiTheme.Surface))
                using (Pen pB = new Pen(UiTheme.Border, 1f))
                {
                    g.FillRectangle(bHdr, 0, 0, ClientSize.Width, hdrH);
                    g.DrawLine(pB, 0, hdrH - 1, ClientSize.Width, hdrH - 1);
                }

                // 4-tile ZeroBox emblem
                RectangleF icoRc = new RectangleF(pad, S(13), S(30), S(30));
                UiTheme.FillRounded(g, icoRc, S(8), Color.FromArgb(18, 30, 48));
                UiTheme.DrawRounded(g, icoRc, S(8), UiTheme.Cyan, 1.3f);
                DrawZeroBoxMark(g, icoRc);

                int titleX = pad + S(38);
                SizeF tSz = g.MeasureString("ZeroBox", _fTitle);
                using (SolidBrush bt = new SolidBrush(UiTheme.Text))
                    g.DrawString("ZeroBox", _fTitle, bt, new RectangleF(titleX, S(6), tSz.Width + S(8), S(26)), sfNear);

                string verTxt = "v" + AppVersion;
                float vw = g.MeasureString(verTxt, _fVer).Width + S(10);
                RectangleF verRc = new RectangleF(titleX + tSz.Width + S(4), S(11), vw, S(17));
                UiTheme.FillRounded(g, verRc, S(5), Color.FromArgb(28, UiTheme.Cyan));
                using (SolidBrush bv = new SolidBrush(UiTheme.Cyan))
                    g.DrawString(verTxt, _fVer, bv, verRc, sfCenter);

                // Header right controls FIRST so subtitle clamps safely
                int rx = right;
                int btnH = S(28);
                int btnY = (hdrH - btnH) / 2;

                int lW1 = S(32), lW2 = S(32), lW3 = S(40);
                int langW = lW1 + lW2 + lW3;
                Rectangle langOuter = new Rectangle(rx - langW, btnY, langW, btnH);
                UiTheme.FillRounded(g, langOuter, S(7), UiTheme.SurfaceAlt);
                UiTheme.DrawRounded(g, langOuter, S(7), UiTheme.Border, 1f);
                DrawLangSeg(g, new Rectangle(langOuter.X, btnY, lW1, btnH), "EN", "en", sfCenter);
                DrawLangSeg(g, new Rectangle(langOuter.X + lW1, btnY, lW2, btnH), "TR", "tr", sfCenter);
                DrawLangSeg(g, new Rectangle(langOuter.X + lW1 + lW2, btnY, lW3, btnH), "中文", "zh", sfCenter);
                rx = langOuter.Left - S(8);

                string chkTxt = _checkingUpdates
                    ? Loc.T(_lang, "Checking...", "Denetleniyor...", "检测中...")
                    : Loc.T(_lang, "Check Updates", "Güncelleme Denetle", "检查更新");
                int chkW = MeasurePillW(g, chkTxt, _fPill, 10);
                Rectangle chkRc = new Rectangle(rx - chkW, btnY, chkW, btnH);
                DrawPill(g, "hdr_check", chkRc, chkTxt, UiTheme.Cyan, _checkingUpdates, sfCenter);
                rx = chkRc.Left - S(8);

                if (updatesCnt > 0)
                {
                    string uaTxt = Loc.T(_lang, "Update All (" + updatesCnt + ")", "Tümünü Güncelle (" + updatesCnt + ")", "全部更新 (" + updatesCnt + ")");
                    int uaW = MeasurePillW(g, uaTxt, _fPill, 10);
                    Rectangle uaRc = new Rectangle(rx - uaW, btnY, uaW, btnH);
                    DrawPill(g, "hdr_update_all", uaRc, uaTxt, UiTheme.Amber, true, sfCenter);
                    rx = uaRc.Left - S(8);
                }

                string subTxt = Loc.T(_lang, "Modular Native Windows Toolbox · On-Demand Plugins",
                    "Modüler Yerel Windows Araç Kutusu · İsteğe Bağlı Eklentiler",
                    "原生零臃肿 Windows 模块化工具箱 · 按需安装与统一更新");
                int subMaxW = Math.Max(S(80), rx - titleX - S(6));
                using (SolidBrush bs = new SolidBrush(UiTheme.Muted))
                    g.DrawString(subTxt, _fSub, bs, new RectangleF(titleX, S(31), subMaxW, S(18)), sfNear);

                // ── 2. FILTER & MIRROR STRIP ──
                int stripY = hdrH + S(12);
                int stripH = S(30);
                int fx = pad;

                string[,] flts = new string[,]
                {
                    { "all", Loc.T(_lang, "All (" + _mgr.Modules.Count + ")", "Tümü (" + _mgr.Modules.Count + ")", "全部 (" + _mgr.Modules.Count + ")") },
                    { "installed", Loc.T(_lang, "Installed (" + installedCnt + ")", "Kurulu (" + installedCnt + ")", "已安装 (" + installedCnt + ")") },
                    { "updates", Loc.T(_lang, "Updates (" + updatesCnt + ")", "Güncelleme (" + updatesCnt + ")", "可更新 (" + updatesCnt + ")") },
                    { "available", Loc.T(_lang, "Available (" + availCnt + ")", "Kullanılabilir (" + availCnt + ")", "未安装 (" + availCnt + ")") }
                };
                for (int i = 0; i < flts.GetLength(0); i++)
                {
                    string fid = flts[i, 0];
                    string flbl = flts[i, 1];
                    int pw = MeasurePillW(g, flbl, _fPill, 9);
                    Rectangle pr = new Rectangle(fx, stripY, pw, stripH);
                    Color acc = (fid == "updates" && updatesCnt > 0) ? UiTheme.Amber : UiTheme.Cyan;
                    DrawPill(g, "flt_" + fid, pr, flbl, acc, _filter == fid, sfCenter);
                    fx = pr.Right + S(6);
                }

                // Right side of strip: GitHub Mirror toggle + Total Installed Footprint badge
                int srx = right;
                string mirTxt = _cfg.UseMirror
                    ? Loc.T(_lang, "Mirror: Proxy ON", "Ayna: Proxy AÇIK", "加速镜像：开")
                    : Loc.T(_lang, "Source: GitHub", "Kaynak: GitHub", "源：GitHub 直连");
                int mirW = MeasurePillW(g, mirTxt, _fPill, 9);
                Rectangle mirRc = new Rectangle(srx - mirW, stripY, mirW, stripH);
                DrawPill(g, "strip_mirror", mirRc, mirTxt, _cfg.UseMirror ? UiTheme.Violet : UiTheme.Muted, _cfg.UseMirror, sfCenter);
                srx = mirRc.Left - S(10);

                string footStat = Loc.T(_lang,
                    Loc.F("Disk: {0} · SHA-256 Verified", ModuleManager.FormatBytes(installedBytes)),
                    Loc.F("Disk: {0} · SHA-256 Doğrulamalı", ModuleManager.FormatBytes(installedBytes)),
                    Loc.F("已占空间：{0} · SHA-256 校验", ModuleManager.FormatBytes(installedBytes)));
                int statAvail = srx - fx - S(8);
                if (statAvail > S(110))
                {
                    using (SolidBrush bfs = new SolidBrush(UiTheme.Subtle))
                        g.DrawString(footStat, _fSmall, bfs, new RectangleF(fx + S(4), stripY, statAvail, stripH), sfFar);
                }

                // ── 3. 2x3 MODULAR PLUGIN CARD GRID ──
                int gridTop = stripY + stripH + S(12);
                int footerH = S(44);
                int gridH = Math.Max(S(260), ClientSize.Height - gridTop - footerH - S(10));
                int gridW = ClientSize.Width - pad * 2;
                int gapX = S(12);
                int gapY = S(12);

                List<ToolModule> visible = GetFilteredModules();
                if (visible.Count == 0)
                {
                    Rectangle emptyRc = new Rectangle(pad, gridTop, gridW, gridH);
                    UiTheme.FillRounded(g, emptyRc, S(10), UiTheme.Surface);
                    UiTheme.DrawRounded(g, emptyRc, S(10), UiTheme.Border, 1f);
                    using (SolidBrush be = new SolidBrush(UiTheme.Muted))
                        g.DrawString(Loc.T(_lang, "No modules match this filter.", "Bu filtrede modül yok.", "当前筛选下暂无模块。"),
                            _fBody, be, emptyRc, sfCenter);
                }
                else
                {
                    int cols = 2;
                    int rows = 3;
                    int cardW = (gridW - gapX * (cols - 1)) / cols;
                    int cardH = (gridH - gapY * (rows - 1)) / rows;

                    for (int i = 0; i < visible.Count; i++)
                    {
                        int r = i / cols;
                        int c = i % cols;
                        if (r >= rows) break;
                        Rectangle cardRc = new Rectangle(pad + c * (cardW + gapX), gridTop + r * (cardH + gapY), cardW, cardH);
                        PaintModuleCard(g, cardRc, visible[i], sfNear, sfCenter, sfFar, sfWrap);
                    }
                }

                // ── 4. FOOTER BAR ──
                PaintFooter(g, pad, ClientSize.Height - footerH, gridW, footerH, sfNear, sfCenter);
            }
        }

        private void PaintModuleCard(Graphics g, Rectangle rc, ToolModule m, StringFormat sfNear, StringFormat sfCenter, StringFormat sfFar, StringFormat sfWrap)
        {
            _cardRects[m.Id] = rc;
            bool isInst = (m.State == ModuleState.Installed || m.State == ModuleState.UpdateAvailable);
            Color bdCol = m.State == ModuleState.UpdateAvailable ? UiTheme.Amber
                : (m.State == ModuleState.Downloading ? UiTheme.Cyan
                : (isInst ? Color.FromArgb(90, m.Accent) : UiTheme.Border));

            UiTheme.FillRounded(g, rc, S(10), UiTheme.Surface);
            UiTheme.DrawRounded(g, rc, S(10), bdCol, (m.State == ModuleState.UpdateAvailable || m.State == ModuleState.Downloading) ? 1.4f : 1f);

            // Top left accent stripe
            UiTheme.FillRounded(g, new RectangleF(rc.X + S(1), rc.Y + S(12), S(3.5f), rc.Height - S(24)), S(2), m.Accent);

            int ix = rc.X + S(16);
            int topY = rc.Y + S(12);

            // Module unique vector icon box
            RectangleF mIco = new RectangleF(ix, topY, S(34), S(34));
            UiTheme.FillRounded(g, mIco, S(8), Color.FromArgb(24, m.Accent));
            UiTheme.DrawRounded(g, mIco, S(8), Color.FromArgb(130, m.Accent), 1.2f);
            DrawModuleGlyph(g, mIco, m.IconKind, m.Accent);

            // Status pill on top right
            string stTxt;
            Color stCol;
            if (m.State == ModuleState.Downloading)
            {
                stTxt = Loc.T(_lang, "Downloading " + m.ProgressPct + "%", "İndiriliyor %" + m.ProgressPct, "下载中 " + m.ProgressPct + "%");
                stCol = UiTheme.Cyan;
            }
            else if (m.State == ModuleState.UpdateAvailable)
            {
                stTxt = (string.IsNullOrEmpty(m.InstalledVersion) ? "" : m.InstalledVersion + " → ") + m.LatestVersion;
                stCol = UiTheme.Amber;
            }
            else if (m.State == ModuleState.Installed)
            {
                stTxt = Loc.T(_lang, "Installed ", "Kurulu ", "已安装 ") + m.InstalledVersion;
                stCol = UiTheme.Emerald;
            }
            else
            {
                stTxt = !string.IsNullOrEmpty(m.LatestVersion) ? m.LatestVersion : m.FallbackTag;
                stCol = UiTheme.Muted;
            }

            int stW = MeasurePillW(g, stTxt, _fSmall, 8);
            RectangleF stRc = new RectangleF(rc.Right - stW - S(12), topY + S(2), stW, S(20));
            UiTheme.FillRounded(g, stRc, S(5), Color.FromArgb(28, stCol));
            UiTheme.DrawRounded(g, stRc, S(5), Color.FromArgb(100, stCol), 1f);
            using (SolidBrush bst = new SolidBrush(stCol))
                g.DrawString(stTxt, _fSmall, bst, stRc, sfCenter);

            // Name & Category beside icon
            int nx = ix + S(42);
            int nameMaxW = Math.Max(S(90), (int)stRc.Left - nx - S(6));
            using (SolidBrush bn = new SolidBrush(UiTheme.Text))
                g.DrawString(m.Name, _fCardTitle, bn, new RectangleF(nx, topY - S(1), nameMaxW, S(20)), sfNear);

            string catMeta = m.Category(_lang) + "  ·  " + (m.InstalledBytes > 0 ? ModuleManager.FormatBytes(m.InstalledBytes) : m.ApproxSize);
            using (SolidBrush bc = new SolidBrush(UiTheme.Subtle))
                g.DrawString(catMeta, _fSmall, bc, new RectangleF(nx, topY + S(19), nameMaxW, S(16)), sfNear);

            // Description (2 lines cleanly wrapped)
            int descY = topY + S(42);
            int descH = Math.Max(S(32), rc.Bottom - S(46) - descY);
            using (SolidBrush bd = new SolidBrush(UiTheme.Muted))
                g.DrawString(m.Description(_lang), _fBody, bd, new RectangleF(ix, descY, rc.Width - S(28), descH), sfWrap);

            // Bottom Action Bar OR Progress Bar
            int actH = S(26);
            int actY = rc.Bottom - actH - S(10);

            if (m.State == ModuleState.Downloading)
            {
                Rectangle pTrack = new Rectangle(ix, actY + S(8), rc.Width - S(28), S(8));
                UiTheme.FillRounded(g, pTrack, S(4), UiTheme.SurfaceAlt);
                int fw = Math.Max(S(8), (int)Math.Round((m.ProgressPct / 100.0) * pTrack.Width));
                UiTheme.FillRounded(g, new Rectangle(pTrack.X, pTrack.Y, fw, pTrack.Height), S(4), UiTheme.Cyan);
            }
            else
            {
                // Right-aligned Repo link pill
                string repoTxt = "GitHub ↗";
                int repoW = MeasurePillW(g, repoTxt, _fPill, 8);
                Rectangle repoRc = new Rectangle(rc.Right - repoW - S(12), actY, repoW, actH);
                DrawActionBtn(g, "repo_" + m.Id, repoRc, repoTxt, UiTheme.Muted, false, sfCenter);

                int bx = ix;
                if (m.State == ModuleState.UpdateAvailable)
                {
                    string uTxt = Loc.T(_lang, "Update", "Güncelle", "立即更新");
                    int uw = MeasurePillW(g, uTxt, _fPill, 11);
                    Rectangle ur = new Rectangle(bx, actY, uw, actH);
                    DrawActionBtn(g, "inst_" + m.Id, ur, uTxt, UiTheme.Amber, true, sfCenter);
                    bx = ur.Right + S(6);

                    string lTxt = Loc.T(_lang, "Launch", "Aç", "启动");
                    int lw = MeasurePillW(g, lTxt, _fPill, 10);
                    Rectangle lr = new Rectangle(bx, actY, lw, actH);
                    DrawActionBtn(g, "run_" + m.Id, lr, lTxt, m.Accent, false, sfCenter);
                    bx = lr.Right + S(6);

                    string rTxt = Loc.T(_lang, "Remove", "Kaldır", "移除");
                    int rw = MeasurePillW(g, rTxt, _fPill, 9);
                    if (bx + rw < repoRc.Left - S(4))
                        DrawActionBtn(g, "rem_" + m.Id, new Rectangle(bx, actY, rw, actH), rTxt, UiTheme.Coral, false, sfCenter);
                }
                else if (m.State == ModuleState.Installed)
                {
                    string lTxt = Loc.T(_lang, "Launch", "Başlat", "启动");
                    int lw = MeasurePillW(g, lTxt, _fPill, 12);
                    Rectangle lr = new Rectangle(bx, actY, lw, actH);
                    DrawActionBtn(g, "run_" + m.Id, lr, lTxt, m.Accent, true, sfCenter);
                    bx = lr.Right + S(6);

                    string rTxt = Loc.T(_lang, "Remove", "Kaldır", "移除");
                    int rw = MeasurePillW(g, rTxt, _fPill, 10);
                    if (bx + rw < repoRc.Left - S(4))
                        DrawActionBtn(g, "rem_" + m.Id, new Rectangle(bx, actY, rw, actH), rTxt, UiTheme.Coral, false, sfCenter);
                }
                else
                {
                    string iTxt = Loc.T(_lang, "Install Module", "Modülü Kur", "一键安装");
                    int iw = MeasurePillW(g, iTxt, _fPill, 12);
                    Rectangle ir = new Rectangle(bx, actY, iw, actH);
                    DrawActionBtn(g, "inst_" + m.Id, ir, iTxt, m.Accent, true, sfCenter);
                }
            }
        }

        private void PaintFooter(Graphics g, int x, int y, int w, int h, StringFormat sfNear, StringFormat sfCenter)
        {
            using (Pen pTop = new Pen(UiTheme.Border, 1f))
                g.DrawLine(pTop, 0, y, ClientSize.Width, y);

            int rx = x + w;
            int by = y + S(9);
            int bh = S(26);

            string trayTxt = Loc.T(_lang, "Tray", "Tepsi", "托盘") + ": " +
                (_cfg.MinimizeToTray ? Loc.T(_lang, "ON", "AÇIK", "开") : Loc.T(_lang, "OFF", "KAPALI", "关"));
            int trayW = MeasurePillW(g, trayTxt, _fPill, 9);
            Rectangle trayRc = new Rectangle(rx - trayW, by, trayW, bh);
            DrawPill(g, "ft_tray", trayRc, trayTxt, _cfg.MinimizeToTray ? UiTheme.Emerald : UiTheme.Muted, _cfg.MinimizeToTray, sfCenter);
            rx = trayRc.Left - S(6);

            string startTxt = Loc.T(_lang, "Startup", "Başlangıç", "自启") + ": " +
                (_startupCached ? Loc.T(_lang, "ON", "AÇIK", "开") : Loc.T(_lang, "OFF", "KAPALI", "关"));
            int startW = MeasurePillW(g, startTxt, _fPill, 9);
            Rectangle startRc = new Rectangle(rx - startW, by, startW, bh);
            DrawPill(g, "ft_startup", startRc, startTxt, _startupCached ? UiTheme.Cyan : UiTheme.Muted, _startupCached, sfCenter);
            rx = startRc.Left - S(6);

            string dirTxt = Loc.T(_lang, "Modules Dir", "Modül Klasörü", "模块目录");
            int dirW = MeasurePillW(g, dirTxt, _fPill, 9);
            Rectangle dirRc = new Rectangle(rx - dirW, by, dirW, bh);
            DrawPill(g, "ft_dir", dirRc, dirTxt, UiTheme.Violet, false, sfCenter);
            rx = dirRc.Left - S(10);

            bool showToast = !string.IsNullOrEmpty(_toastText) && DateTime.UtcNow < _toastUntil;
            string leftTxt = showToast ? _toastText
                : Loc.T(_lang,
                    "Right-click any card for Run as Admin · Downloads are SHA-256 verified against GitHub Releases",
                    "Yönetici çalıştırma için karta sağ tıklayın · Tüm indirmeler GitHub SHA-256 doğrulamalıdır",
                    "右键卡片可管理员启动 · 所有模块直连 GitHub Releases 并进行 SHA-256 校验");
            using (SolidBrush bf = new SolidBrush(showToast ? UiTheme.Emerald : UiTheme.Muted))
                g.DrawString(leftTxt, _fSmall, bf, new RectangleF(x, y, Math.Max(S(100), rx - x), h), sfNear);
        }

        private void DrawZeroBoxMark(Graphics g, RectangleF rc)
        {
            float u = rc.Width * 0.25f;
            float gap = rc.Width * 0.09f;
            float sx = rc.X + (rc.Width - (u * 2 + gap)) / 2f;
            float sy = rc.Y + (rc.Height - (u * 2 + gap)) / 2f;
            Color[] cols = new Color[] { UiTheme.Cyan, UiTheme.Emerald, UiTheme.Violet, UiTheme.Amber };
            int idx = 0;
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    using (SolidBrush b = new SolidBrush(cols[idx++]))
                        g.FillRectangle(b, sx + c * (u + gap), sy + r * (u + gap), u, u);
                }
        }

        private void DrawModuleGlyph(Graphics g, RectangleF rc, string kind, Color col)
        {
            float cx = rc.X + rc.Width / 2f;
            float cy = rc.Y + rc.Height / 2f;
            using (Pen p = new Pen(col, 1.7f))
            using (SolidBrush b = new SolidBrush(col))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                if (kind == "uptime")
                {
                    float r = rc.Width * 0.30f;
                    g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
                    g.DrawLine(p, cx, cy, cx, cy - r * 0.55f);
                    g.DrawLine(p, cx, cy, cx + r * 0.48f, cy + r * 0.2f);
                }
                else if (kind == "sweep")
                {
                    float r = rc.Width * 0.28f;
                    g.DrawLine(p, cx, cy - r, cx, cy + r);
                    g.DrawLine(p, cx - r, cy, cx + r, cy);
                    g.DrawLine(p, cx - r * 0.6f, cy - r * 0.6f, cx + r * 0.6f, cy + r * 0.6f);
                    g.DrawLine(p, cx + r * 0.6f, cy - r * 0.6f, cx - r * 0.6f, cy + r * 0.6f);
                }
                else if (kind == "forge")
                {
                    float r = rc.Width * 0.28f;
                    PointF[] pts = new PointF[]
                    {
                        new PointF(cx, cy - r), new PointF(cx + r, cy - r * 0.45f),
                        new PointF(cx + r * 0.78f, cy + r * 0.55f), new PointF(cx, cy + r),
                        new PointF(cx - r * 0.78f, cy + r * 0.55f), new PointF(cx - r, cy - r * 0.45f)
                    };
                    g.DrawPolygon(p, pts);
                }
                else if (kind == "pulse")
                {
                    float r = rc.Width * 0.32f;
                    PointF[] pts = new PointF[]
                    {
                        new PointF(cx - r, cy), new PointF(cx - r * 0.45f, cy),
                        new PointF(cx - r * 0.18f, cy - r * 0.75f), new PointF(cx + r * 0.18f, cy + r * 0.7f),
                        new PointF(cx + r * 0.48f, cy), new PointF(cx + r, cy)
                    };
                    g.DrawLines(p, pts);
                }
                else if (kind == "port")
                {
                    float r1 = rc.Width * 0.30f;
                    float r2 = rc.Width * 0.16f;
                    g.DrawEllipse(p, cx - r1, cy - r1, r1 * 2, r1 * 2);
                    g.DrawEllipse(p, cx - r2, cy - r2, r2 * 2, r2 * 2);
                    g.FillEllipse(b, cx - 2f, cy - 2f, 4f, 4f);
                }
                else // lumi
                {
                    float rC = rc.Width * 0.14f, rIn = rc.Width * 0.22f, rOut = rc.Width * 0.32f;
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        g.DrawLine(p, (float)(cx + Math.Cos(a) * rIn), (float)(cy + Math.Sin(a) * rIn),
                                      (float)(cx + Math.Cos(a) * rOut), (float)(cy + Math.Sin(a) * rOut));
                    }
                    g.FillEllipse(b, cx - rC, cy - rC, rC * 2, rC * 2);
                }
            }
        }

        private void DrawLangSeg(Graphics g, Rectangle rc, string label, string code, StringFormat sfCenter)
        {
            string id = "lang_" + code;
            _hitRects[id] = rc;
            bool active = string.Equals(_lang, code, StringComparison.OrdinalIgnoreCase);
            bool hov = _hoverId == id;
            if (active)
                UiTheme.FillRounded(g, new Rectangle(rc.X + 2, rc.Y + 2, rc.Width - 4, rc.Height - 4), S(5), UiTheme.Cyan);
            else if (hov)
                UiTheme.FillRounded(g, new Rectangle(rc.X + 2, rc.Y + 2, rc.Width - 4, rc.Height - 4), S(5), UiTheme.Hover);
            using (SolidBrush b = new SolidBrush(active ? Color.FromArgb(12, 18, 28) : (hov ? UiTheme.Text : UiTheme.Muted)))
                g.DrawString(label, _fPill, b, rc, sfCenter);
        }

        private void DrawPill(Graphics g, string id, Rectangle rc, string text, Color accent, bool active, StringFormat sfCenter)
        {
            _hitRects[id] = rc;
            bool hov = _hoverId == id;
            Color fill = active ? Color.FromArgb(34, accent) : (hov ? UiTheme.Hover : UiTheme.SurfaceAlt);
            Color stroke = active ? accent : (hov ? UiTheme.BorderLight : UiTheme.Border);
            Color fg = active ? accent : (hov ? UiTheme.Text : UiTheme.Muted);
            UiTheme.FillRounded(g, rc, S(7), fill);
            UiTheme.DrawRounded(g, rc, S(7), stroke, 1f);
            using (SolidBrush bt = new SolidBrush(fg))
                g.DrawString(text, _fPill, bt, rc, sfCenter);
        }

        private void DrawActionBtn(Graphics g, string id, Rectangle rc, string text, Color accent, bool primary, StringFormat sfCenter)
        {
            _hitRects[id] = rc;
            bool hov = _hoverId == id;
            Color fill, stroke, fg;
            if (primary)
            {
                fill = hov ? Color.FromArgb(230, accent) : Color.FromArgb(195, accent);
                stroke = accent;
                fg = Color.FromArgb(12, 18, 28);
            }
            else
            {
                fill = hov ? Color.FromArgb(46, accent) : Color.FromArgb(18, accent);
                stroke = hov ? accent : Color.FromArgb(105, accent);
                fg = hov ? UiTheme.Text : accent;
            }
            UiTheme.FillRounded(g, rc, S(6), fill);
            UiTheme.DrawRounded(g, rc, S(6), stroke, 1f);
            using (SolidBrush bt = new SolidBrush(fg))
                g.DrawString(text, _fPill, bt, rc, sfCenter);
        }

        private string HitTest(Point pt)
        {
            foreach (KeyValuePair<string, Rectangle> kv in _hitRects)
                if (kv.Value.Contains(pt)) return kv.Key;
            return "";
        }

        private ToolModule FindModuleById(string id)
        {
            for (int i = 0; i < _mgr.Modules.Count; i++)
                if (_mgr.Modules[i].Id == id) return _mgr.Modules[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitTest(e.Location);
            if (h != _hoverId)
            {
                _hoverId = h;
                Cursor = string.IsNullOrEmpty(h) ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!string.IsNullOrEmpty(_hoverId))
            {
                _hoverId = "";
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                foreach (KeyValuePair<string, Rectangle> kv in _cardRects)
                {
                    if (kv.Value.Contains(e.Location))
                    {
                        ShowCardContextMenu(FindModuleById(kv.Key), PointToScreen(e.Location));
                        return;
                    }
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            string id = HitTest(e.Location);
            if (string.IsNullOrEmpty(id)) return;

            if (id == "lang_en") SwitchLanguage("en");
            else if (id == "lang_tr") SwitchLanguage("tr");
            else if (id == "lang_zh") SwitchLanguage("zh");
            else if (id == "hdr_check") StartCheckAllUpdates();
            else if (id == "hdr_update_all")
            {
                for (int i = 0; i < _mgr.Modules.Count; i++)
                    if (_mgr.Modules[i].State == ModuleState.UpdateAvailable)
                        StartInstallOrUpdate(_mgr.Modules[i]);
            }
            else if (id.StartsWith("flt_")) { _filter = id.Substring(4); Invalidate(); }
            else if (id == "strip_mirror")
            {
                _cfg.UseMirror = !_cfg.UseMirror;
                if (!_launch.DemoMode) _cfg.Save();
                ShowToast(_cfg.UseMirror
                    ? Loc.T(_lang, "GitHub Mirror enabled (gh-proxy.org).", "GitHub Aynası açıldı (gh-proxy.org).", "已开启 GitHub 加速镜像 (gh-proxy.org)。")
                    : Loc.T(_lang, "Switched to Direct GitHub.", "Doğrudan GitHub bağlantısına geçildi.", "已切换为 GitHub 直连。"));
            }
            else if (id.StartsWith("inst_")) StartInstallOrUpdate(FindModuleById(id.Substring(5)));
            else if (id.StartsWith("run_")) HandleLaunch(FindModuleById(id.Substring(4)), false);
            else if (id.StartsWith("rem_")) HandleRemove(FindModuleById(id.Substring(4)));
            else if (id.StartsWith("repo_"))
            {
                ToolModule m = FindModuleById(id.Substring(5));
                if (m != null) WinNative.OpenUrlSafe(m.RepoUrl);
            }
            else if (id == "ft_dir") WinNative.OpenFolderSafe(_mgr.ModulesRoot);
            else if (id == "ft_tray")
            {
                _cfg.MinimizeToTray = !_cfg.MinimizeToTray;
                if (!_launch.DemoMode) _cfg.Save();
                Invalidate();
            }
            else if (id == "ft_startup")
            {
                if (_launch.DemoMode) { _startupCached = !_startupCached; Invalidate(); return; }
                bool next = !_startupCached;
                if (WinNative.SetStartupEnabled(next))
                {
                    _startupCached = next;
                    ShowToast(next
                        ? Loc.T(_lang, "Added ZeroBox to Windows startup (minimized).", "ZeroBox Windows başlangıcına eklendi.", "已开启 ZeroBox 开机自启（托盘静默）。")
                        : Loc.T(_lang, "Removed ZeroBox from Windows startup.", "ZeroBox başlangıçtan kaldırıldı.", "已关闭开机自启。"));
                }
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            WinNative.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LaunchConfig launch = LaunchConfig.Parse(args);
            Mutex mutex = null;
            bool ownsMutex = false;

            if (!launch.DemoMode)
            {
                try
                {
                    mutex = new Mutex(true, @"Local\ZeroBox_SingleInstance_v1", out ownsMutex);
                    if (!ownsMutex)
                    {
                        if (WinNative.WM_SHOW_EXISTING != 0)
                            WinNative.PostMessage(WinNative.HWND_BROADCAST, WinNative.WM_SHOW_EXISTING, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                }
                catch { }
            }

            try
            {
                Application.Run(new MainForm(launch));
            }
            finally
            {
                if (mutex != null)
                {
                    if (ownsMutex) { try { mutex.ReleaseMutex(); } catch { } }
                    mutex.Dispose();
                }
            }
        }
    }
}
