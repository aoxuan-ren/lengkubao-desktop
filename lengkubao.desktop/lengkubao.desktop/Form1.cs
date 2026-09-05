using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO.Compression;
using lengkubao.desktop.Sync;

namespace lengkubao.desktop
{
    public partial class Form1 : Form
    {
        // —— 电脑端主界面主题（与管理系统主色 #004c54 一致）——
        private static readonly Color UiPrimaryDark = Color.FromArgb(0, 76, 84);
        private static readonly Color UiPrimary = Color.FromArgb(0, 90, 100);
        private static readonly Color UiSurface = Color.FromArgb(242, 246, 249);
        private static readonly Color UiCard = Color.White;
        private static readonly Color UiMuted = Color.FromArgb(92, 107, 122);
        private static readonly Color UiBorder = Color.FromArgb(206, 216, 227);
        private static readonly Color UiSection = Color.FromArgb(247, 249, 251);
        private static readonly Color UiSuccess = Color.FromArgb(45, 125, 82);
        private static readonly Color UiDanger = Color.FromArgb(169, 68, 66);
        private static readonly Color UiSecondaryFill = Color.FromArgb(236, 241, 245);
        private static readonly Color UiSecondaryHover = Color.FromArgb(224, 231, 239);
        private static readonly Color UiListOnline = Color.FromArgb(37, 110, 85);
        private static readonly Color UiListOffline = Color.FromArgb(150, 158, 168);
        private static readonly Color UiCodeBox = Color.FromArgb(18, 66, 82);
        private static readonly Color UiStatusStopped = Color.FromArgb(108, 117, 125);
        private static readonly Color UiCoreSecondaryBg = Color.FromArgb(248, 249, 251);
        private static readonly Color UiHeaderGradientBottom = Color.FromArgb(0, 58, 66);
        private static readonly Color UiHeaderAccentLine = Color.FromArgb(0, 140, 155);
        private static readonly Color UiHeaderGhostBorder = Color.FromArgb(90, 255, 255, 255);
        private static readonly Color UiHeaderGhostFill = Color.FromArgb(36, 255, 255, 255);
        /// <summary>顶栏控件实色填充（ComboBox 等不支持半透明 BackColor）。</summary>
        private static readonly Color UiHeaderControlFill = Color.FromArgb(36, 112, 120);
        private static readonly Color UiHeaderInkMuted = Color.FromArgb(185, 215, 220);
        private static readonly Color UiHeaderInkSubtle = Color.FromArgb(175, 205, 212);
        private static readonly Color UiHeaderInkUser = Color.FromArgb(210, 228, 232);
        private static readonly Color UiHeaderPairingPillFill = Color.FromArgb(42, 255, 255, 255);
        private static readonly Color UiQrCardBorder = Color.FromArgb(220, 228, 235);
        private static readonly Color UiHeaderConnectionOnline = Color.FromArgb(130, 220, 165);
        private const int HeaderConsoleHeight = 108;
        private const int HeaderBtnHeight = 30;
        private const int HeaderBtnWidth = 58;
        private const int HeaderFiscalComboWidth = 82;
        private const int HeaderZoneGap = 12;
        private const int HeaderPadH = 20;
        private const int HeaderRightPad = 16;
        private const int HeaderMinWidthForFiscal = 1020;
        private const int HeaderMinWidthForUser = 1100;

        private static readonly Dictionary<long, Image> CoreFunctionIconCache = new Dictionary<long, Image>();
        private TableLayoutPanel coreFunctionsTable;
        private GroupBox commonToolsGroup;

        private static FontFamily ResolveUiFontFamily()
        {
            foreach (var name in new[] { "Microsoft YaHei UI", "Segoe UI", "微软雅黑" })
            {
                try
                {
                    var ff = new FontFamily(name);
                    if (ff.IsStyleAvailable(FontStyle.Regular))
                        return ff;
                }
                catch (ArgumentException) { }
            }
            return FontFamily.GenericSansSerif;
        }

        private static readonly FontFamily UiFontFamily = ResolveUiFontFamily();

        private static Font UiFont(float sizeInPoints, FontStyle style = FontStyle.Regular) =>
            new Font(UiFontFamily, sizeInPoints, style, GraphicsUnit.Point);

        private static Image GetCoreFunctionIcon(int tag, int size = 56)
        {
            long key = ((long)tag << 16) | (uint)size;
            lock (CoreFunctionIconCache)
            {
                if (CoreFunctionIconCache.TryGetValue(key, out Image existing))
                    return existing;
                Image img = BuildCoreFunctionIcon(tag, size);
                CoreFunctionIconCache[key] = img;
                return img;
            }
        }

        private static Image BuildCoreFunctionIcon(int tag, int s)
        {
            var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                int m = Math.Max(2, s / 14);
                var rect = new Rectangle(m, m, s - 2 * m, s - 2 * m);
                switch (tag)
                {
                    case 0: DrawIconSearchDoc(g, rect); break;
                    case 1: DrawIconInbound(g, rect); break;
                    case 2: DrawIconCart(g, rect); break;
                    case 3: DrawIconBars(g, rect); break;
                    case 4: DrawIconBox(g, rect); break;
                    case 5: DrawIconBoxSearch(g, rect); break;
                    case 6: DrawIconReconcile(g, rect); break;
                    case 100: DrawIconBars(g, rect); break;
                    case 101: DrawIconBackup(g, rect); break;
                    case 102: DrawIconRestore(g, rect); break;
                    case 103: DrawIconYearEnd(g, rect); break;
                    case 104: DrawIconHistorical(g, rect); break;
                    case 105: DrawIconPresale(g, rect); break;
                    case 106: DrawIconLedger(g, rect); break;
                    default: DrawIconGear(g, rect); break;
                }
            }
            return bmp;
        }

        private static void FillRoundRect(Graphics g, Brush b, Rectangle r, int radius)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(b, path);
            }
        }

        private static void DrawIconSearchDoc(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(52, 120, 220)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                g.DrawRectangle(pen, r.X + r.Width * 0.22f, r.Y + r.Height * 0.2f, r.Width * 0.36f, r.Height * 0.42f);
                g.DrawArc(pen, r.X + r.Width * 0.48f, r.Y + r.Height * 0.48f, r.Width * 0.38f, r.Height * 0.38f, 40, 260);
            }
        }

        private static void DrawIconInbound(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(39, 174, 96)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2.5f, r.Width / 14f)))
            {
                int cx = r.X + r.Width / 2;
                int y1 = r.Y + r.Height * 3 / 10;
                int y2 = r.Y + r.Height * 7 / 10;
                g.DrawLine(pen, cx, y1, cx, y2);
                g.DrawLine(pen, cx - r.Width / 5, y1 + r.Height / 8, cx, y1);
                g.DrawLine(pen, cx + r.Width / 5, y1 + r.Height / 8, cx, y1);
            }
        }

        private static void DrawIconCart(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(230, 126, 34)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                g.DrawLine(pen, r.X + r.Width * 0.22f, r.Y + r.Height * 0.35f, r.X + r.Width * 0.78f, r.Y + r.Height * 0.35f);
                g.DrawLine(pen, r.X + r.Width * 0.28f, r.Y + r.Height * 0.35f, r.X + r.Width * 0.35f, r.Y + r.Height * 0.72f);
                g.DrawLine(pen, r.X + r.Width * 0.72f, r.Y + r.Height * 0.35f, r.X + r.Width * 0.65f, r.Y + r.Height * 0.72f);
                g.DrawLine(pen, r.X + r.Width * 0.3f, r.Y + r.Height * 0.72f, r.X + r.Width * 0.7f, r.Y + r.Height * 0.72f);
            }
        }

        private static void DrawIconBars(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(41, 128, 185)))
                FillRoundRect(g, br, r, rad);
            using (var b = new SolidBrush(Color.White))
            {
                int w = r.Width / 6;
                int baseY = r.Bottom - r.Height / 5;
                g.FillRectangle(b, r.X + r.Width / 5, baseY - r.Height / 3, w, r.Height / 3);
                g.FillRectangle(b, r.X + r.Width * 2 / 5 - w / 4, baseY - r.Height / 2, w, r.Height / 2);
                g.FillRectangle(b, r.X + r.Width * 3 / 5 - w / 2, baseY - r.Height * 2 / 5, w, r.Height * 2 / 5);
            }
        }

        private static void DrawIconBox(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(155, 89, 182)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                var box = new Rectangle(r.X + r.Width / 5, r.Y + r.Height / 4, r.Width * 3 / 5, r.Height * 1 / 2);
                g.DrawRectangle(pen, box);
                g.DrawLine(pen, box.Left, box.Top + box.Height / 2, box.Right, box.Top + box.Height / 2);
            }
        }

        private static void DrawIconBoxSearch(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(22, 160, 133)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                var box = new Rectangle(r.X + r.Width / 6, r.Y + r.Height / 5, r.Width * 2 / 5, r.Height * 2 / 5);
                g.DrawRectangle(pen, box);
                g.DrawArc(pen, r.X + r.Width * 0.45f, r.Y + r.Height * 0.42f, r.Width * 0.42f, r.Height * 0.42f, 20, 280);
            }
        }

        private static void DrawIconReconcile(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(241, 196, 15)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                g.DrawEllipse(pen, r.X + r.Width * 0.22f, r.Y + r.Height * 0.35f, r.Width * 0.22f, r.Height * 0.22f);
                g.DrawEllipse(pen, r.X + r.Width * 0.56f, r.Y + r.Height * 0.35f, r.Width * 0.22f, r.Height * 0.22f);
                g.DrawLine(pen, r.X + r.Width * 0.33f, r.Y + r.Height * 0.62f, r.X + r.Width * 0.67f, r.Y + r.Height * 0.62f);
            }
        }

        private static void DrawIconGear(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(127, 140, 141)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 18f)))
            {
                int cx = r.X + r.Width / 2;
                int cy = r.Y + r.Height / 2;
                int rr = Math.Min(r.Width, r.Height) / 4;
                g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * Math.PI / 3;
                    float x1 = cx + (float)(Math.Cos(ang) * rr * 0.5);
                    float y1 = cy + (float)(Math.Sin(ang) * rr * 0.5);
                    float x2 = cx + (float)(Math.Cos(ang) * rr * 1.15);
                    float y2 = cy + (float)(Math.Sin(ang) * rr * 1.15);
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
        }

        private static void DrawIconBackup(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(52, 152, 219)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                var tray = new Rectangle(r.X + r.Width / 5, r.Y + r.Height * 2 / 5, r.Width * 3 / 5, r.Height * 2 / 5);
                g.DrawRectangle(pen, tray);
                g.DrawLine(pen, r.X + r.Width / 2, r.Y + r.Height / 5, r.X + r.Width / 2, r.Y + r.Height * 2 / 5);
                g.DrawLine(pen, r.X + r.Width / 2 - r.Width / 8, r.Y + r.Height / 4, r.X + r.Width / 2, r.Y + r.Height / 5);
                g.DrawLine(pen, r.X + r.Width / 2 + r.Width / 8, r.Y + r.Height / 4, r.X + r.Width / 2, r.Y + r.Height / 5);
            }
        }

        private static void DrawIconRestore(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(39, 174, 96)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                g.DrawArc(pen, r.X + r.Width / 5, r.Y + r.Height / 5, r.Width * 3 / 5, r.Height * 3 / 5, 45, 270);
                g.DrawLine(pen, r.X + r.Width / 5, r.Y + r.Height / 3, r.X + r.Width / 5, r.Y + r.Height / 5);
                g.DrawLine(pen, r.X + r.Width / 5, r.Y + r.Height / 5, r.X + r.Width / 3, r.Y + r.Height / 5);
            }
        }

        private static void DrawIconYearEnd(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(192, 57, 43)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                var cal = new Rectangle(r.X + r.Width / 5, r.Y + r.Height / 5, r.Width * 3 / 5, r.Height * 3 / 5);
                g.DrawRectangle(pen, cal);
                g.DrawLine(pen, cal.Left, cal.Top + cal.Height / 4, cal.Right, cal.Top + cal.Height / 4);
                g.DrawLine(pen, cal.Left + cal.Width / 3, cal.Top, cal.Left + cal.Width / 3, cal.Top + cal.Height / 6);
                g.DrawLine(pen, cal.Right - cal.Width / 3, cal.Top, cal.Right - cal.Width / 3, cal.Top + cal.Height / 6);
            }
        }

        private static void DrawIconHistorical(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(142, 68, 173)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                var book = new Rectangle(r.X + r.Width / 4, r.Y + r.Height / 5, r.Width / 2, r.Height * 3 / 5);
                g.DrawRectangle(pen, book);
                g.DrawLine(pen, book.Left + book.Width / 2, book.Top, book.Left + book.Width / 2, book.Bottom);
                g.DrawLine(pen, r.X + r.Width / 4, r.Y + r.Height * 4 / 5, r.X + r.Width * 3 / 4, r.Y + r.Height * 4 / 5);
            }
        }

        private static void DrawIconPresale(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(180, 83, 9)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                var tag = new Rectangle(r.X + r.Width / 4, r.Y + r.Height / 5, r.Width / 2, r.Height / 2);
                g.DrawRectangle(pen, tag);
                g.DrawLine(pen, tag.Left + tag.Width / 2, tag.Top, tag.Left + tag.Width / 4, tag.Top - r.Height / 8);
                g.DrawLine(pen, r.X + r.Width / 4, r.Y + r.Height * 3 / 4, r.X + r.Width * 3 / 4, r.Y + r.Height * 3 / 4);
            }
        }

        private static void DrawIconLedger(Graphics g, Rectangle r)
        {
            int rad = Math.Max(4, r.Width / 5);
            using (var br = new SolidBrush(Color.FromArgb(4, 120, 87)))
                FillRoundRect(g, br, r, rad);
            using (var pen = new Pen(Color.White, Math.Max(2f, r.Width / 16f)))
            {
                g.DrawLine(pen, r.X + r.Width / 5, r.Y + r.Height / 2, r.X + r.Width * 4 / 5, r.Y + r.Height / 2);
                g.DrawLine(pen, r.X + r.Width * 2 / 5, r.Y + r.Height / 3, r.X + r.Width / 5, r.Y + r.Height / 2);
                g.DrawLine(pen, r.X + r.Width * 2 / 5, r.Y + r.Height / 3, r.X + r.Width * 2 / 5, r.Y + r.Height * 2 / 3);
                g.DrawLine(pen, r.X + r.Width * 3 / 5, r.Y + r.Height * 2 / 3, r.X + r.Width * 4 / 5, r.Y + r.Height / 2);
                g.DrawLine(pen, r.X + r.Width * 3 / 5, r.Y + r.Height * 2 / 3, r.X + r.Width * 3 / 5, r.Y + r.Height / 3);
            }
        }

        private static bool IsPrimaryFunctionSlot(int tag) =>
            tag == 1 || tag == 2 || tag == 4 || tag == 7;

        private const int NavTagToolStatistics = 100;
        private const int NavTagToolBackup = 101;
        private const int NavTagToolRestore = 102;
        private const int NavTagToolYearEnd = 103;
        private const int NavTagToolHistorical = 104;
        private const int NavTagToolPresale = 105;
        private const int NavTagToolLedger = 106;

        private Timer refreshTimer;
        private Panel headerPanel;
        private Panel panelQrHost;
        private Panel panelPairingPill;
        private Panel leftNavPanel;
        private Label headerUserLabel;
        private Label headerSubtitleLabel;
        /// <summary>当前打开的功能模块外壳窗体（最大化工作区，与主界面分离）。</summary>
        private Form activeWorkspaceHost;
        /// <summary>当前选中的核心功能 Tag（0–7），-1 表示工作台欢迎页。</summary>
        private int selectedNavFunctionTag = -1;
        private readonly Dictionary<int, Button> navFunctionButtons = new Dictionary<int, Button>();

        // 同步服务器相关字段
        private AutoSyncServer syncServer;
        private bool isSyncServerRunning = false;
        private bool skipBackupReminderOnClose = false;

        private System.Threading.EventWaitHandle showWindowEvent;
        private System.Threading.Thread instanceListenerThread;
        private volatile bool stopInstanceListener;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // 配对码相关控件
        private TextBox txtPairingCode;
        private Button btnCopyPairingCode;
        private Button btnPairingMore;
        private ContextMenuStrip pairingMoreMenu;
        private ToolTip headerToolTip;
        private Label lblHeaderConnectionStatus;
        private Label lblSyncClientsEmpty;
        private ContextMenuStrip syncClientsMenu;
        private int pendingConfigDeltaCount;
        private Label lblPairingCodeDisplay;
        private PictureBox picPairingQr;
        private Label lblQrPairingHint;

        // 年份切换相关控件
        private Label lblFiscalYearTitle;
        private Panel panelFiscalYearHost;
        private ComboBox cboFiscalYear;
        private Button btnSaveFiscalYear;
        private Button btnNewFiscalYear;
        private bool suppressFiscalYearComboChange;

        public Form1()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            try
            {
                Console.WriteLine("=== 冷库宝管理系统启动 ===");

                // 初始化界面
                InitializeCustomComponents();

                // 启动定时刷新
                StartAutoRefresh();

                Console.WriteLine("=== 系统启动完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动失败: {ex}");
                MessageBox.Show($"程序启动失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 初始化自定义组件
        private void InitializeCustomComponents()
        {
            // 设置窗体基本属性
            this.Text = "冷库宝管理系统";
            this.Size = new Size(1200, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UiSurface;
            this.Font = UiFont(9f);

            // 隐藏旧按钮
            HideOldButtons();

            // 创建顶部标题栏
            CreateHeaderPanel();

            // 左侧：核心功能网格 + 常用工具 + 内容宿主（与参考界面一致）
            CreateLeftColumnWithDashboardAndContent();

            // 创建右侧同步管理器面板
            CreateSidePanel();

            // 配对码在顶栏中部
            CreateHeaderPairingControls();
            LoadFiscalYearCombo();

            // 初始化同步服务器
            InitializeSyncServer();

            this.Shown += Form1_Shown;

            this.MinimumSize = new Size(960, 600);
            this.Resize += Form1_Resize;
            ApplyResponsiveLayout();

            if (mainMenuStrip != null)
            {
                mainMenuStrip.Visible = false;
                mainMenuStrip.Height = 0;
            }
            this.MainMenuStrip = null;
            this.Padding = Padding.Empty;
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            ApplyResponsiveLayout();
        }

        /// <summary>
        /// 主界面随窗口缩放：顶栏；下方为左侧（核心功能 + 工具 + 内容）与右侧同步管理器。
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (headerPanel == null || leftNavPanel == null || sidePanel == null) return;

            const int margin = 10;
            const int gap = 10;
            const int headerH = HeaderConsoleHeight;
            const int dashboardMinW = 400;
            const int sideMinW = 280;

            int h = ClientSize.Height;
            int innerW = Math.Max(400, ClientSize.Width - 2 * margin);

            headerPanel.SetBounds(0, 0, ClientSize.Width, headerH);
            headerPanel.BringToFront();
            int y = headerH + gap;

            int restH = Math.Max(200, h - y - margin);
            int sideW = Math.Max(sideMinW, (int)(innerW * 0.34f));
            int leftW = Math.Max(dashboardMinW, innerW - sideW - gap);
            if (leftW + sideW + gap > innerW)
                leftW = Math.Max(320, innerW - sideW - gap);

            leftNavPanel.SetBounds(margin, y, leftW, restH);
            sidePanel.SetBounds(margin + leftW + gap, y, sideW, restH);

            LayoutHeaderFull();
            LayoutLeftDashboardHeights();
            LayoutSideInternals();
        }

        /// <summary>控制台顶栏：左品牌+财年 | 右配对区（二维码→状态→配对码→操作）| 用户名。</summary>
        private void LayoutHeaderFull()
        {
            if (headerPanel == null) return;

            int W = headerPanel.ClientSize.Width;
            int H = headerPanel.ClientSize.Height;
            int btnH = HeaderBtnHeight;
            int btnW = HeaderBtnWidth;
            int yMid = (H - btnH) / 2;
            var pairingButtons = new[] { btnCopyPairingCode, btnPairingMore };

            Control title = headerPanel.Controls.Find("headerTitleLabel", false).FirstOrDefault();
            if (title != null)
            {
                title.AutoSize = true;
                title.Font = UiFont(17f, FontStyle.Bold);
                title.ForeColor = Color.White;
                title.Location = new Point(HeaderPadH, 14);
            }

            if (headerSubtitleLabel != null)
            {
                headerSubtitleLabel.AutoSize = true;
                headerSubtitleLabel.Font = UiFont(9f);
                headerSubtitleLabel.ForeColor = UiHeaderInkSubtle;
                headerSubtitleLabel.Location = new Point(HeaderPadH, title != null ? title.Bottom + 2 : 40);
            }

            int brandRight = HeaderPadH + Math.Max(title?.Width ?? 0, headerSubtitleLabel?.Width ?? 0);
            bool showFiscal = W >= HeaderMinWidthForFiscal;
            bool showUser = W >= HeaderMinWidthForUser;

            if (headerUserLabel != null)
            {
                headerUserLabel.Visible = showUser;
                headerUserLabel.AutoSize = true;
                headerUserLabel.Font = UiFont(9.5f);
                headerUserLabel.ForeColor = UiHeaderInkUser;
                headerUserLabel.Text = Environment.UserName;
            }

            if (lblFiscalYearTitle != null)
            {
                lblFiscalYearTitle.Visible = showFiscal;
                lblFiscalYearTitle.AutoSize = true;
                lblFiscalYearTitle.Font = UiFont(9f);
                lblFiscalYearTitle.ForeColor = UiHeaderInkMuted;
                lblFiscalYearTitle.Text = "财年";
            }
            if (panelFiscalYearHost != null)
                panelFiscalYearHost.Visible = showFiscal;
            if (cboFiscalYear != null)
            {
                cboFiscalYear.Visible = showFiscal;
                cboFiscalYear.Font = UiFont(10f);
                cboFiscalYear.Width = HeaderFiscalComboWidth - 2;
                cboFiscalYear.Height = btnH - 2;
            }
            if (btnSaveFiscalYear != null)
            {
                btnSaveFiscalYear.Visible = showFiscal;
                btnSaveFiscalYear.Text = "保存";
                btnSaveFiscalYear.Font = UiFont(9.5f);
                btnSaveFiscalYear.Size = new Size(54, btnH);
            }
            if (btnNewFiscalYear != null)
            {
                btnNewFiscalYear.Visible = showFiscal;
                btnNewFiscalYear.Text = "新建";
                btnNewFiscalYear.Font = UiFont(9.5f);
                btnNewFiscalYear.Size = new Size(54, btnH);
            }

            foreach (Button b in pairingButtons)
            {
                if (b == null) continue;
                b.Font = UiFont(10f);
                int w = b == btnPairingMore ? 64 : btnW;
                b.Size = new Size(w, btnH);
            }

            Label pairingTitle = panelPairingPill.Controls.Find("pairingTitle", false).FirstOrDefault() as Label;
            if (pairingTitle == null || lblPairingCodeDisplay == null || panelQrHost == null || panelPairingPill == null)
                return;

            pairingTitle.AutoSize = true;
            pairingTitle.Font = UiFont(9f);
            pairingTitle.ForeColor = UiHeaderInkMuted;
            pairingTitle.Text = "配对码";

            float codePt = Math.Min(28f, Math.Max(22f, H * 0.26f));
            lblPairingCodeDisplay.AutoSize = true;
            lblPairingCodeDisplay.Font = new Font(UiFontFamily, codePt, FontStyle.Bold, GraphicsUnit.Point);
            lblPairingCodeDisplay.ForeColor = Color.White;

            const int pillPadH = 12;
            const int pillPadV = 6;
            pairingTitle.Location = new Point(pillPadH, pillPadV);
            lblPairingCodeDisplay.Location = new Point(pillPadH, pairingTitle.Bottom - 1);
            int pillW = Math.Max(124, Math.Max(pairingTitle.Width, lblPairingCodeDisplay.Width) + pillPadH * 2);
            int pillH = lblPairingCodeDisplay.Bottom + pillPadV;

            // —— 左侧：品牌 + 财年（同类归组）——
            int fiscalRight = brandRight;
            if (showFiscal && lblFiscalYearTitle != null && panelFiscalYearHost != null && cboFiscalYear != null)
            {
                int fx = brandRight + HeaderZoneGap;
                lblFiscalYearTitle.Location = new Point(fx, (H - lblFiscalYearTitle.Height) / 2);
                fx = lblFiscalYearTitle.Right + 8;
                panelFiscalYearHost.SetBounds(fx, yMid, HeaderFiscalComboWidth, btnH);
                cboFiscalYear.SetBounds(fx + 1, yMid + 1, HeaderFiscalComboWidth - 2, btnH - 2);
                fx = panelFiscalYearHost.Right + 6;
                if (btnSaveFiscalYear != null)
                {
                    btnSaveFiscalYear.Location = new Point(fx, yMid);
                    fx = btnSaveFiscalYear.Right + 4;
                }
                if (btnNewFiscalYear != null)
                {
                    btnNewFiscalYear.Location = new Point(fx, yMid);
                    fx = btnNewFiscalYear.Right;
                }
                fiscalRight = fx;
            }
            else
            {
                fiscalRight = brandRight;
            }

            int leftBound = fiscalRight + HeaderZoneGap;

            // —— 右侧：用户名 ——
            int xRight = W - HeaderRightPad;
            if (showUser && headerUserLabel != null)
            {
                headerUserLabel.Location = new Point(xRight - headerUserLabel.Width, (H - headerUserLabel.Height) / 2);
                xRight = headerUserLabel.Left - 12;
            }

            // —— 配对区宽度（二维码在配对码前）——
            if (lblHeaderConnectionStatus != null)
            {
                lblHeaderConnectionStatus.Visible = true;
                lblHeaderConnectionStatus.AutoSize = true;
                lblHeaderConnectionStatus.Font = UiFont(9.5f);
            }

            int statusW = lblHeaderConnectionStatus?.Width ?? 0;
            int buttonsW = pairingButtons.Where(b => b != null).Sum(b => b.Width) + Math.Max(0, pairingButtons.Count(b => b != null) - 1) * 6;
            int pairingGap = 10;

            int qrPad = 6;
            int qrSize = Math.Min(76, Math.Max(56, H - 24));
            int qrHostW = qrSize + qrPad * 2;
            int qrHostH = qrSize + qrPad * 2;

            int pairingClusterW = qrHostW + pairingGap + statusW + pairingGap + pillW + pairingGap + buttonsW;
            int pairingLeft = xRight - pairingClusterW;

            if (pairingLeft < leftBound)
            {
                int deficit = leftBound - pairingLeft;
                qrSize = Math.Max(56, qrSize - deficit);
                qrHostW = qrSize + qrPad * 2;
                qrHostH = qrSize + qrPad * 2;
                pairingClusterW = qrHostW + pairingGap + statusW + pairingGap + pillW + pairingGap + buttonsW;
                pairingLeft = xRight - pairingClusterW;
            }

            if (pairingLeft < leftBound)
                pairingLeft = leftBound;

            // —— 配对区从左到右：二维码 → 状态 → 配对码 → 复制/配对▾ ——
            int x = pairingLeft;
            int qrY = Math.Max(4, (H - qrHostH) / 2);
            panelQrHost.SetBounds(x, qrY, qrHostW, qrHostH);
            if (picPairingQr != null)
            {
                picPairingQr.Size = new Size(qrSize, qrSize);
                picPairingQr.BorderStyle = BorderStyle.None;
                picPairingQr.Location = new Point(qrPad, qrPad);
            }
            if (lblQrPairingHint != null)
                lblQrPairingHint.Visible = false;

            x = panelQrHost.Right + pairingGap;

            if (lblHeaderConnectionStatus != null)
            {
                lblHeaderConnectionStatus.Location = new Point(x, (H - lblHeaderConnectionStatus.Height) / 2);
                x = lblHeaderConnectionStatus.Right + pairingGap;
            }

            panelPairingPill.SetBounds(x, (H - pillH) / 2, pillW, pillH);
            x = panelPairingPill.Right + pairingGap;

            foreach (Button b in pairingButtons)
            {
                if (b == null) continue;
                b.SetBounds(x, yMid, b.Width, btnH);
                x = b.Right + 6;
            }

            if (title != null) title.BringToFront();
            if (headerSubtitleLabel != null) headerSubtitleLabel.BringToFront();
            if (lblFiscalYearTitle != null && lblFiscalYearTitle.Visible) lblFiscalYearTitle.BringToFront();
            if (panelFiscalYearHost != null && panelFiscalYearHost.Visible) panelFiscalYearHost.BringToFront();
            if (cboFiscalYear != null && cboFiscalYear.Visible) cboFiscalYear.BringToFront();
            if (btnSaveFiscalYear != null && btnSaveFiscalYear.Visible) btnSaveFiscalYear.BringToFront();
            if (btnNewFiscalYear != null && btnNewFiscalYear.Visible) btnNewFiscalYear.BringToFront();
            panelQrHost.BringToFront();
            if (picPairingQr != null) picPairingQr.BringToFront();
            if (lblHeaderConnectionStatus != null) lblHeaderConnectionStatus.BringToFront();
            panelPairingPill.BringToFront();
            foreach (Button b in pairingButtons)
            {
                if (b != null) b.BringToFront();
            }
            if (headerUserLabel != null && headerUserLabel.Visible)
                headerUserLabel.BringToFront();
        }

        private void RefreshHeaderConnectionStatus(int? onlineCount = null)
        {
            if (lblHeaderConnectionStatus == null || lblHeaderConnectionStatus.IsDisposed) return;

            void Apply()
            {
                int count = onlineCount ?? (syncServer?.GetConnectedOnlineDeviceCount() ?? 0);
                if (!isSyncServerRunning || syncServer == null)
                {
                    lblHeaderConnectionStatus.Text = "○ 服务未启动";
                    lblHeaderConnectionStatus.ForeColor = UiHeaderInkMuted;
                }
                else if (count > 0)
                {
                    lblHeaderConnectionStatus.Text = count == 1
                        ? "● 已连接 1 台"
                        : $"● 已连接 {count} 台";
                    lblHeaderConnectionStatus.ForeColor = UiHeaderConnectionOnline;
                }
                else
                {
                    lblHeaderConnectionStatus.Text = "○ 未连接";
                    lblHeaderConnectionStatus.ForeColor = UiHeaderInkMuted;
                }

                if (headerPanel != null && !headerPanel.IsDisposed)
                    LayoutHeaderFull();
            }

            try
            {
                if (lblHeaderConnectionStatus.InvokeRequired)
                    lblHeaderConnectionStatus.BeginInvoke(new Action(Apply));
                else
                    Apply();
            }
            catch { }
        }

        private void SetupHeaderTooltips()
        {
            if (headerToolTip == null) return;
            if (btnCopyPairingCode != null)
                headerToolTip.SetToolTip(btnCopyPairingCode, "复制配对码到剪贴板");
            if (btnPairingMore != null)
                headerToolTip.SetToolTip(btnPairingMore, "更改 / 随机 / 广播配对码");
            if (panelQrHost != null)
                headerToolTip.SetToolTip(panelQrHost, "手持端扫描此码连接");
            else if (picPairingQr != null)
                headerToolTip.SetToolTip(picPairingQr, "手持端扫描此码连接");
            if (panelFiscalYearHost != null)
                headerToolTip.SetToolTip(panelFiscalYearHost, "切换当前财年");
        }

        private void BtnPairingMore_Click(object sender, EventArgs e)
        {
            if (pairingMoreMenu == null || btnPairingMore == null) return;
            pairingMoreMenu.Show(btnPairingMore, new Point(0, btnPairingMore.Height));
        }

        private void RefreshSyncPortDisplay()
        {
            if (lblPortInfo == null || lblPortInfo.IsDisposed || syncServer == null) return;
            lblPortInfo.Text = $"端口: {syncServer.GetTcpPort()}";
        }

        private void RefreshSyncClientsEmptyState(int deviceCount)
        {
            if (lblSyncClientsEmpty == null || lblSyncClientsEmpty.IsDisposed) return;
            if (deviceCount > 0)
            {
                lblSyncClientsEmpty.Visible = false;
                return;
            }

            lblSyncClientsEmpty.Visible = true;
            lblSyncClientsEmpty.Text = isSyncServerRunning
                ? "暂无连接设备 · 请手持端扫描顶栏二维码"
                : "请先点击「启动服务」";
        }

        private void UpdatePendingSyncUiState(int count)
        {
            pendingConfigDeltaCount = count;
            bool hasOnline = isSyncServerRunning && syncServer != null && syncServer.GetConnectedOnlineDeviceCount() > 0;

            if (lblPendingConfigSummary != null && !lblPendingConfigSummary.IsDisposed)
            {
                if (count <= 0)
                    lblPendingConfigSummary.Text = "暂无待同步项";
                else if (!isSyncServerRunning || syncServer == null)
                    lblPendingConfigSummary.Text = $"共 {count} 条待同步 · 请先点击「启动服务」";
                else if (!hasOnline)
                    lblPendingConfigSummary.Text = $"共 {count} 条待同步 · 需手持端扫码连接";
                else
                    lblPendingConfigSummary.Text = $"共 {count} 条待同步（客户/库位/经手人/商品/包装）";
            }

            if (btnSyncPendingConfig != null && !btnSyncPendingConfig.IsDisposed)
                btnSyncPendingConfig.Enabled = count > 0 && hasOnline;
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!(sender is Panel panel)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = panel.ClientRectangle;
            using (var brush = new LinearGradientBrush(
                rect,
                UiPrimaryDark,
                UiHeaderGradientBottom,
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, rect);
            }
            using (var pen = new Pen(UiHeaderAccentLine, 2f))
                g.DrawLine(pen, 0, rect.Height - 1, rect.Width, rect.Height - 1);
        }

        private void QrHostPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!(sender is Panel panel)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using (var brush = new SolidBrush(Color.White))
                FillRoundRect(g, brush, rect, 8);
            using (var pen = new Pen(UiQrCardBorder))
                DrawRoundRect(g, pen, rect, 8);
        }

        private void PairingPillPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!(sender is Panel panel)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using (var brush = new SolidBrush(UiHeaderPairingPillFill))
                FillRoundRect(g, brush, rect, 8);
            using (var pen = new Pen(UiHeaderGhostBorder))
                DrawRoundRect(g, pen, rect, 8);
        }

        private void FiscalYearHost_Paint(object sender, PaintEventArgs e)
        {
            if (!(sender is Panel panel)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using (var brush = new SolidBrush(UiHeaderControlFill))
                FillRoundRect(g, brush, rect, 6);
            using (var pen = new Pen(UiHeaderGhostBorder))
                DrawRoundRect(g, pen, rect, 6);
        }

        private void StyleHeaderFiscalCombo(ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = UiHeaderControlFill;
            combo.ForeColor = Color.White;
            combo.Font = UiFont(10f);
            combo.IntegralHeight = false;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 26;
            combo.DrawItem -= CboFiscalYear_DrawItem;
            combo.DrawItem += CboFiscalYear_DrawItem;
        }

        private void CboFiscalYear_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? Color.FromArgb(58, 255, 255, 255) : UiHeaderGradientBottom;
            Color fg = Color.White;

            using (var brush = new SolidBrush(bg))
                e.Graphics.FillRectangle(brush, e.Bounds);

            string text = cboFiscalYear?.Items[e.Index]?.ToString() ?? "";
            TextRenderer.DrawText(
                e.Graphics,
                text,
                cboFiscalYear?.Font ?? UiFont(10f),
                e.Bounds,
                fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        private static void DrawRoundRect(Graphics g, Pen pen, Rectangle r, int radius)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }

        private static void StyleHeaderGhostButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = UiHeaderGhostFill;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = UiHeaderGhostBorder;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 255, 255, 255);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(72, 255, 255, 255);
            b.Cursor = Cursors.Hand;
            b.Size = new Size(HeaderBtnWidth, HeaderBtnHeight);
            b.TabStop = true;
            AttachHeaderButtonFocusChrome(b);
        }

        private static void AttachHeaderButtonFocusChrome(Button b)
        {
            if (b.Tag as string == "header-chrome") return;
            b.Tag = "header-chrome";
            b.GotFocus += (s, e) =>
            {
                var btn = (Button)s;
                btn.FlatAppearance.BorderColor = Color.FromArgb(160, 255, 255, 255);
                btn.FlatAppearance.BorderSize = 2;
            };
            b.LostFocus += (s, e) =>
            {
                var btn = (Button)s;
                btn.FlatAppearance.BorderColor = UiHeaderGhostBorder;
                btn.FlatAppearance.BorderSize = 1;
            };
        }

        private void LayoutLeftDashboardHeights()
        {
            // 常用工具区固定高度（Dock Bottom），核心功能区 Fill 占满剩余空间，无需再按比例计算。
        }

        private void LayoutSideInternals()
        {
            if (sidePanel == null) return;

            int W = sidePanel.ClientSize.Width;
            int H = sidePanel.ClientSize.Height;
            int pad = 10;
            int innerW = Math.Max(40, W - 2 * pad);

            int y = pad;
            Control sideTitle = sidePanel.Controls.Find("sideTitle", false).FirstOrDefault();
            if (sideTitle != null)
            {
                sideTitle.SetBounds(pad, y, innerW, 32);
                y = sideTitle.Bottom + 8;
            }

            Panel status = sidePanel.Controls.Find("statusPanel", false).FirstOrDefault() as Panel;
            if (status != null)
            {
                int sh = 76;
                status.SetBounds(pad, y, innerW, sh);
                y = status.Bottom + 8;
            }

            Panel ctrl = sidePanel.Controls.Find("syncControlPanel", false).FirstOrDefault() as Panel;
            if (ctrl != null)
            {
                int ch = 96;
                ctrl.SetBounds(pad, y, innerW, ch);
                y = ctrl.Bottom + 8;
                var ordered = ctrl.Controls.OfType<Button>().OrderBy(b => b.TabIndex).ToList();
                int nb = ordered.Count;
                int gap = 10;
                int inner = ctrl.ClientSize.Width - 24;
                int bh = 32;
                int btnTop = 8;
                if (nb > 0)
                {
                    int bw = Math.Max(72, (inner - (nb - 1) * gap) / nb);
                    for (int i = 0; i < nb; i++)
                        ordered[i].SetBounds(12 + i * (bw + gap), btnTop, bw, bh);
                }
                var autoStartChk = ctrl.Controls.Find("chkAutoStartSync", false).FirstOrDefault() as CheckBox;
                if (autoStartChk != null)
                {
                    int chkTop = btnTop + bh + 6;
                    autoStartChk.SetBounds(12, chkTop, innerW - 24, 22);
                }
            }

            Panel sp = sidePanel.Controls.Find("syncProgressPanel", false).FirstOrDefault() as Panel;
            if (sp != null)
            {
                int sph = 56;
                sp.SetBounds(pad, y, innerW, sph);
                y = sp.Bottom + 8;
            }

            Panel gc = sidePanel.Controls.Find("groupClients", false).FirstOrDefault() as Panel;
            Panel gpc = sidePanel.Controls.Find("groupPendingConfig", false).FirstOrDefault() as Panel;
            if (gc != null && gpc != null)
            {
                int remain = Math.Max(160, H - y - pad);
                int clientsH = Math.Max(88, (int)(remain * 0.38f));
                int pendingH = Math.Max(120, remain - clientsH - 8);
                gc.SetBounds(pad, y, innerW, clientsH);
                gpc.SetBounds(pad, gc.Bottom + 8, innerW, pendingH);
                sidePanel.AutoScrollMinSize = new Size(0, gpc.Bottom + pad);
            }
            else if (gc != null)
            {
                int gh = Math.Max(120, H - y - pad);
                gc.SetBounds(pad, y, innerW, gh);
                sidePanel.AutoScrollMinSize = new Size(0, gc.Bottom + pad);
            }

            if (gc != null && lvSyncClients != null)
            {
                int listTop = gc.Controls.OfType<Label>().FirstOrDefault(l => l != lblSyncClientsEmpty)?.Bottom ?? 24;
                lvSyncClients.SetBounds(8, listTop + 4, Math.Max(60, gc.ClientSize.Width - 16), Math.Max(48, gc.ClientSize.Height - listTop - 12));
                if (lblSyncClientsEmpty != null)
                {
                    lblSyncClientsEmpty.SetBounds(8, listTop + 8, Math.Max(60, gc.ClientSize.Width - 16),
                        Math.Max(40, gc.ClientSize.Height - listTop - 16));
                    lblSyncClientsEmpty.BringToFront();
                }
                int cw = lvSyncClients.ClientSize.Width - 24;
                if (lvSyncClients.Columns.Count >= 5 && cw > 40)
                {
                    lvSyncClients.Columns[0].Width = Math.Max(50, (int)(cw * 0.22f));
                    lvSyncClients.Columns[1].Width = Math.Max(50, (int)(cw * 0.22f));
                    lvSyncClients.Columns[2].Width = Math.Max(44, (int)(cw * 0.14f));
                    lvSyncClients.Columns[3].Width = Math.Max(56, (int)(cw * 0.22f));
                    lvSyncClients.Columns[4].Width = Math.Max(44, cw - lvSyncClients.Columns[0].Width - lvSyncClients.Columns[1].Width - lvSyncClients.Columns[2].Width - lvSyncClients.Columns[3].Width);
                }
                else if (lvSyncClients.Columns.Count >= 4 && cw > 40)
                {
                    lvSyncClients.Columns[0].Width = Math.Max(60, (int)(cw * 0.26f));
                    lvSyncClients.Columns[1].Width = Math.Max(50, (int)(cw * 0.34f));
                    lvSyncClients.Columns[2].Width = Math.Max(50, (int)(cw * 0.20f));
                    lvSyncClients.Columns[3].Width = Math.Max(40, cw - lvSyncClients.Columns[0].Width - lvSyncClients.Columns[1].Width - lvSyncClients.Columns[2].Width);
                }
            }

            if (gpc != null && lvPendingConfig != null && btnSyncPendingConfig != null)
            {
                int titleH = gpc.Controls.OfType<Label>().FirstOrDefault(l => l != lblPendingConfigSummary)?.Bottom ?? 24;
                int summaryTop = titleH + 2;
                if (lblPendingConfigSummary != null)
                    lblPendingConfigSummary.SetBounds(8, summaryTop, Math.Max(60, gpc.ClientSize.Width - 16), 34);
                int btnH = 32;
                btnSyncPendingConfig.SetBounds(8, gpc.ClientSize.Height - btnH - 8, Math.Max(60, gpc.ClientSize.Width - 16), btnH);
                int listTop = summaryTop + 38;
                lvPendingConfig.SetBounds(8, listTop, Math.Max(60, gpc.ClientSize.Width - 16),
                    Math.Max(48, btnSyncPendingConfig.Top - listTop - 6));
                int pcw = lvPendingConfig.ClientSize.Width - 24;
                if (lvPendingConfig.Columns.Count >= 4 && pcw > 40)
                {
                    lvPendingConfig.Columns[0].Width = Math.Max(44, (int)(pcw * 0.18f));
                    lvPendingConfig.Columns[1].Width = Math.Max(40, (int)(pcw * 0.14f));
                    lvPendingConfig.Columns[2].Width = Math.Max(60, (int)(pcw * 0.46f));
                    lvPendingConfig.Columns[3].Width = Math.Max(44, pcw - lvPendingConfig.Columns[0].Width - lvPendingConfig.Columns[1].Width - lvPendingConfig.Columns[2].Width);
                }
            }
        }

        // 隐藏旧按钮的方法
        private void HideOldButtons()
        {
            for (int i = 1; i <= 8; i++)
            {
                string buttonName = "button" + i;
                Control[] foundControls = this.Controls.Find(buttonName, true);

                if (foundControls.Length > 0)
                {
                    Control oldButton = foundControls[0];
                    oldButton.Visible = false;
                    oldButton.Enabled = false;
                }
            }

            // 隐藏"快速测试"按钮
            string[] quickTestButtons = {
                "btnQuickTest", "btnQuickTest_Click", "button9", "button10",
                "btnTest", "快速测试", "btnQuickTestButton"
            };

            foreach (string btnName in quickTestButtons)
            {
                Control[] found = this.Controls.Find(btnName, true);
                if (found.Length > 0)
                {
                    Console.WriteLine($"找到'快速测试'按钮: {btnName}");
                    found[0].Visible = false;
                    found[0].Enabled = false;
                }
            }

            // 隐藏"系统诊断"按钮
            string[] diagnosticButtons = {
                "btnDiagnostic", "btnDiagnostic_Click", "btnSystemDiagnostic",
                "button11", "button12", "btnDiagnose", "系统诊断"
            };

            foreach (string btnName in diagnosticButtons)
            {
                Control[] found = this.Controls.Find(btnName, true);
                if (found.Length > 0)
                {
                    Console.WriteLine($"找到'系统诊断'按钮: {btnName}");
                    found[0].Visible = false;
                    found[0].Enabled = false;
                }
            }
        }

        // 创建顶部控制台标题栏（贴顶全宽）
        private void CreateHeaderPanel()
        {
            headerPanel = new Panel();
            headerPanel.Size = new Size(this.ClientSize.Width, HeaderConsoleHeight);
            headerPanel.Location = new Point(0, 0);
            headerPanel.BackColor = UiPrimaryDark;
            headerPanel.Paint += HeaderPanel_Paint;

            Label titleLabel = new Label();
            titleLabel.Name = "headerTitleLabel";
            titleLabel.Text = "冷库宝";
            titleLabel.Font = UiFont(17f, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Location = new Point(20, 14);
            headerPanel.Controls.Add(titleLabel);

            headerSubtitleLabel = new Label();
            headerSubtitleLabel.Name = "headerSubtitleLabel";
            headerSubtitleLabel.Text = "管理系统";
            headerSubtitleLabel.Font = UiFont(9f);
            headerSubtitleLabel.ForeColor = UiHeaderInkSubtle;
            headerSubtitleLabel.AutoSize = true;
            headerSubtitleLabel.BackColor = Color.Transparent;
            headerSubtitleLabel.Location = new Point(20, 38);
            headerPanel.Controls.Add(headerSubtitleLabel);

            headerUserLabel = new Label();
            headerUserLabel.Text = Environment.UserName;
            headerUserLabel.Font = UiFont(9f);
            headerUserLabel.ForeColor = UiHeaderInkUser;
            headerUserLabel.AutoSize = true;
            headerUserLabel.BackColor = Color.Transparent;
            headerPanel.Controls.Add(headerUserLabel);

            this.Controls.Add(headerPanel);
        }

        private void CreateLeftColumnWithDashboardAndContent()
        {
            leftNavPanel = new Panel();
            leftNavPanel.Size = new Size(700, 550);
            leftNavPanel.Location = new Point(10, 90);
            leftNavPanel.BackColor = UiSurface;
            leftNavPanel.BorderStyle = BorderStyle.None;
            leftNavPanel.AutoScroll = true;

            commonToolsGroup = new GroupBox();
            commonToolsGroup.Name = "commonToolsGroup";
            commonToolsGroup.Text = "常用工具";
            commonToolsGroup.Font = UiFont(10f, FontStyle.Bold);
            commonToolsGroup.ForeColor = UiPrimaryDark;
            commonToolsGroup.BackColor = UiCard;
            const int queryToolsRowHeight = 150;
            const int maintToolsRowHeight = 150;
            commonToolsGroup.Dock = DockStyle.Bottom;
            commonToolsGroup.Height = queryToolsRowHeight + maintToolsRowHeight + 48;
            commonToolsGroup.Padding = new Padding(12, 8, 12, 10);

            var toolsTable = new TableLayoutPanel();
            toolsTable.Name = "commonToolsTable";
            toolsTable.Dock = DockStyle.Fill;
            toolsTable.ColumnCount = 1;
            toolsTable.RowCount = 2;
            toolsTable.Padding = new Padding(2);
            toolsTable.BackColor = UiCard;
            toolsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, queryToolsRowHeight));
            toolsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, maintToolsRowHeight));

            var queryToolsTable = new TableLayoutPanel();
            queryToolsTable.Name = "queryToolsTable";
            queryToolsTable.Dock = DockStyle.Fill;
            queryToolsTable.ColumnCount = 3;
            queryToolsTable.RowCount = 1;
            queryToolsTable.Padding = new Padding(0);
            queryToolsTable.BackColor = UiCard;
            for (int i = 0; i < 3; i++)
                queryToolsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            queryToolsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var maintToolsTable = new TableLayoutPanel();
            maintToolsTable.Name = "maintToolsTable";
            maintToolsTable.Dock = DockStyle.Fill;
            maintToolsTable.ColumnCount = 2;
            maintToolsTable.RowCount = 1;
            maintToolsTable.Padding = new Padding(0, 4, 0, 0);
            maintToolsTable.BackColor = UiCard;
            for (int i = 0; i < 2; i++)
                maintToolsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            maintToolsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Button btnStats = CreateDashboardToolButton("多维库存统计", darkStyle: true, asCard: true, iconTag: NavTagToolStatistics, iconSize: 40);
            Button btnPresaleQuery = CreateDashboardToolButton("销预售查询", darkStyle: true, asCard: true, iconTag: NavTagToolPresale, iconSize: 40);
            Button btnLedgerQuery = CreateDashboardToolButton("收支流水", darkStyle: true, asCard: true, iconTag: NavTagToolLedger, iconSize: 40);
            Button btnBackup = CreateDashboardToolButton("数据备份", darkStyle: false, asCard: true, iconTag: NavTagToolBackup, iconSize: 32);
            Button btnRestore = CreateDashboardToolButton("恢复数据", darkStyle: false, asCard: true, iconTag: NavTagToolRestore, iconSize: 32);

            btnRestore.Tag = NavTagToolRestore;
            btnRestore.Click += BtnRestore_Click;
            btnBackup.Tag = NavTagToolBackup;
            btnBackup.Click += BtnDataBackup_Click;
            btnStats.Tag = NavTagToolStatistics;
            btnStats.Click += btnStatistics_Click;
            btnPresaleQuery.Tag = NavTagToolPresale;
            btnPresaleQuery.Click += BtnPresaleQuery_Click;
            btnLedgerQuery.Tag = NavTagToolLedger;
            btnLedgerQuery.Click += BtnLedgerQuery_Click;

            // 第一行：多维库存统计 | 销预售查询 | 收支流水
            queryToolsTable.Controls.Add(btnStats, 0, 0);
            queryToolsTable.Controls.Add(btnPresaleQuery, 1, 0);
            queryToolsTable.Controls.Add(btnLedgerQuery, 2, 0);
            // 第二行：数据备份 | 恢复数据
            maintToolsTable.Controls.Add(btnBackup, 0, 0);
            maintToolsTable.Controls.Add(btnRestore, 1, 0);

            toolsTable.Controls.Add(queryToolsTable, 0, 0);
            toolsTable.Controls.Add(maintToolsTable, 0, 1);

            commonToolsGroup.Controls.Add(toolsTable);

            Panel coreDashboardHost = new Panel();
            coreDashboardHost.Name = "coreDashboardHost";
            coreDashboardHost.Dock = DockStyle.Fill;
            coreDashboardHost.BackColor = UiSurface;

            Label lblCore = new Label();
            lblCore.Name = "navSectionCore";
            lblCore.Text = "核心功能";
            lblCore.Font = UiFont(10f, FontStyle.Bold);
            lblCore.ForeColor = UiPrimaryDark;
            lblCore.Dock = DockStyle.Top;
            lblCore.Height = 24;
            lblCore.TextAlign = ContentAlignment.MiddleLeft;
            lblCore.Padding = new Padding(4, 4, 0, 0);

            coreFunctionsTable = new TableLayoutPanel();
            coreFunctionsTable.Dock = DockStyle.Fill;
            coreFunctionsTable.ColumnCount = 4;
            coreFunctionsTable.RowCount = 2;
            coreFunctionsTable.Padding = new Padding(4, 4, 4, 4);  // 增加内边距
            coreFunctionsTable.Margin = new Padding(0);
            coreFunctionsTable.BackColor = UiSurface;
            for (int i = 0; i < 4; i++)
                coreFunctionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            // 设置行样式，让两行等高
            for (int i = 0; i < 2; i++)
                coreFunctionsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            string[] buttonTexts = {
                "入库查询", "入库开单", "客户报账", "报账查询",
                "包装记账", "包装查询", "客户对账", "系统设置"
            };
            int[] row0 = { 1, 2, 4, 7 };
            int[] row1 = { 0, 3, 5, 6 };
            for (int c = 0; c < 4; c++)
            {
                int tag = row0[c];
                Button btn = CreateDashboardFunctionButton(buttonTexts[tag], tag, true);
                navFunctionButtons[tag] = btn;
                coreFunctionsTable.Controls.Add(btn, c, 0);
            }

            for (int c = 0; c < 4; c++)
            {
                int tag = row1[c];
                Button btn = CreateDashboardFunctionButton(buttonTexts[tag], tag, false);
                navFunctionButtons[tag] = btn;
                coreFunctionsTable.Controls.Add(btn, c, 1);
            }

            coreDashboardHost.Controls.Add(coreFunctionsTable);
            coreDashboardHost.Controls.Add(lblCore);

            leftNavPanel.Controls.Add(coreDashboardHost);
            leftNavPanel.Controls.Add(commonToolsGroup);

            this.Controls.Add(leftNavPanel);
            ApplyNavFunctionSelectionStyles();
        }

        private Button CreateDashboardFunctionButton(string text, int tag, bool primaryStyle)
        {
            Button btn = new Button();
            btn.Tag = tag;
            btn.Text = text;
            btn.Image = GetCoreFunctionIcon(tag);
            btn.ImageAlign = ContentAlignment.MiddleCenter;
            btn.TextAlign = ContentAlignment.BottomCenter;
            btn.TextImageRelation = TextImageRelation.ImageAboveText;

            // 增大字体
            btn.Font = primaryStyle ? UiFont(10.5f, FontStyle.Bold) : UiFont(10f, FontStyle.Bold);

            btn.FlatStyle = FlatStyle.Flat;
            btn.Cursor = Cursors.Hand;
            btn.Margin = new Padding(6);  // 增加外边距
            btn.Dock = DockStyle.Fill;
            btn.Padding = new Padding(4, 8, 4, 10);  // 增加内边距，让按钮内容更饱满
            btn.UseVisualStyleBackColor = false;

            if (primaryStyle)
            {
                btn.BackColor = UiPrimaryDark;
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderSize = 0;
            }
            else
            {
                btn.BackColor = UiCoreSecondaryBg;
                btn.ForeColor = UiPrimaryDark;
                btn.FlatAppearance.BorderColor = UiBorder;
                btn.FlatAppearance.BorderSize = 1;
            }

            btn.Click += (s, e) =>
            {
                switch (tag)
                {
                    case 0: button1_Click(s, e); break;
                    case 1: button2_Click(s, e); break;
                    case 2: button3_Click(s, e); break;
                    case 3: button4_Click(s, e); break;
                    case 4: button5_Click(s, e); break;
                    case 5: button6_Click(s, e); break;
                    case 6: button7_Click(s, e); break;
                    case 7: button8_Click(s, e); break;
                }
            };

            btn.MouseEnter += (s, e) =>
            {
                var b = (Button)s;
                if (selectedNavFunctionTag == tag) return;
                if (IsPrimaryFunctionSlot(tag))
                {
                    Color cur = b.BackColor;
                    b.BackColor = Color.FromArgb(
                        Math.Min(cur.R + 18, 255),
                        Math.Min(cur.G + 22, 255),
                        Math.Min(cur.B + 24, 255));
                    b.FlatAppearance.BorderSize = 0;
                }
                else
                {
                    b.BackColor = UiSecondaryHover;
                    b.FlatAppearance.BorderColor = UiPrimary;
                    b.FlatAppearance.BorderSize = 1;
                }
            };
            btn.MouseLeave += (s, e) => ApplyNavFunctionSelectionStyles();

            return btn;
        }

        private Button CreateDashboardToolButton(string text, bool darkStyle, bool asCard = false, int iconTag = -1, bool compact = false, int iconSize = 0)
        {
            Button btn = new Button();
            btn.Text = text;
            int resolvedIconSize = iconSize > 0 ? iconSize : (compact ? 22 : 34);
            bool useCompactStyle = resolvedIconSize < 28;
            btn.Font = asCard
                ? UiFont(useCompactStyle ? 8.5f : 9f, FontStyle.Bold)
                : UiFont(10f, FontStyle.Bold);
            btn.FlatStyle = FlatStyle.Flat;
            btn.Cursor = Cursors.Hand;
            btn.UseVisualStyleBackColor = false;

            if (asCard)
            {
                btn.Dock = DockStyle.Fill;
                btn.Margin = useCompactStyle ? new Padding(3) : new Padding(5);
                if (iconTag >= 0)
                {
                    btn.Image = GetCoreFunctionIcon(iconTag, resolvedIconSize);
                    btn.ImageAlign = ContentAlignment.MiddleCenter;
                    btn.TextAlign = ContentAlignment.BottomCenter;
                    btn.TextImageRelation = TextImageRelation.ImageAboveText;
                    btn.Padding = useCompactStyle ? new Padding(1, 0, 1, 2) : new Padding(2, 4, 2, 6);
                }
                else
                {
                    btn.TextAlign = ContentAlignment.MiddleCenter;
                    btn.Padding = compact ? new Padding(2, 4, 2, 4) : new Padding(4, 6, 4, 6);
                }
            }
            else
            {
                btn.TextAlign = ContentAlignment.MiddleCenter;
                btn.Margin = new Padding(0, 8, 0, 0);
                btn.Dock = DockStyle.Top;
                btn.Height = 48;
            }

            ApplyDashboardToolButtonStyle(btn, darkStyle, asCard);
            return btn;
        }

        private void ApplyDashboardToolButtonStyle(Button btn, bool darkStyle, bool asCard)
        {
            btn.MouseEnter -= DashboardToolButton_MouseEnter;
            btn.MouseLeave -= DashboardToolButton_MouseLeave;
            btn.AccessibleDescription = darkStyle ? "dark" : "light";

            if (darkStyle)
            {
                btn.BackColor = UiPrimaryDark;
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderSize = 0;
            }
            else if (asCard)
            {
                btn.BackColor = UiCoreSecondaryBg;
                btn.ForeColor = UiPrimaryDark;
                btn.FlatAppearance.BorderColor = UiBorder;
                btn.FlatAppearance.BorderSize = 1;
            }
            else
            {
                Color baseGray = Color.FromArgb(224, 228, 232);
                btn.BackColor = baseGray;
                btn.ForeColor = UiPrimaryDark;
                btn.FlatAppearance.BorderColor = UiBorder;
                btn.FlatAppearance.BorderSize = 1;
            }

            btn.MouseEnter += DashboardToolButton_MouseEnter;
            btn.MouseLeave += DashboardToolButton_MouseLeave;
        }

        private void DashboardToolButton_MouseEnter(object sender, EventArgs e)
        {
            if (!(sender is Button btn)) return;
            bool darkStyle = btn.AccessibleDescription == "dark";
            if (darkStyle)
            {
                btn.BackColor = Color.FromArgb(0, 95, 105);
                btn.FlatAppearance.BorderSize = 0;
            }
            else
            {
                btn.BackColor = UiSecondaryHover;
                btn.FlatAppearance.BorderColor = UiPrimary;
                btn.FlatAppearance.BorderSize = 1;
            }
        }

        private void DashboardToolButton_MouseLeave(object sender, EventArgs e)
        {
            if (!(sender is Button btn)) return;
            bool darkStyle = btn.AccessibleDescription == "dark";
            if (darkStyle)
            {
                btn.BackColor = UiPrimaryDark;
                btn.FlatAppearance.BorderSize = 0;
            }
            else
            {
                btn.BackColor = UiCoreSecondaryBg;
                btn.FlatAppearance.BorderColor = UiBorder;
                btn.FlatAppearance.BorderSize = 1;
            }
        }

        private Panel CreateSideCardPanel(string title, bool showTitle, out Label titleLabel)
        {
            var card = new Panel();
            card.BackColor = UiCard;
            card.Padding = new Padding(12, showTitle ? 6 : 10, 12, 10);
            card.Paint += SideCardPanel_Paint;

            titleLabel = null;
            if (showTitle && !string.IsNullOrEmpty(title))
            {
                titleLabel = new Label();
                titleLabel.Text = title;
                titleLabel.Font = UiFont(9f, FontStyle.Bold);
                titleLabel.ForeColor = UiPrimaryDark;
                titleLabel.Dock = DockStyle.Top;
                titleLabel.Height = 22;
                titleLabel.TextAlign = ContentAlignment.MiddleLeft;
                card.Controls.Add(titleLabel);
            }

            return card;
        }

        private void SideCardPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!(sender is Panel panel)) return;
            var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using (var pen = new Pen(UiBorder))
                e.Graphics.DrawRectangle(pen, rect);
        }

        private void ApplyNavFunctionSelectionStyles()
        {
            foreach (var kv in navFunctionButtons)
            {
                int tag = kv.Key;
                Button btn = kv.Value;
                if (btn == null || btn.IsDisposed) continue;
                bool primaryStyle = IsPrimaryFunctionSlot(tag);
                bool sel = selectedNavFunctionTag == tag;
                if (sel)
                {
                    btn.BackColor = Color.FromArgb(0, 95, 105);
                    btn.ForeColor = Color.White;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(255, 200, 90);
                    btn.FlatAppearance.BorderSize = 2;
                }
                else if (primaryStyle)
                {
                    btn.BackColor = UiPrimaryDark;
                    btn.ForeColor = Color.White;
                    btn.FlatAppearance.BorderSize = 0;
                }
                else
                {
                    btn.BackColor = UiCoreSecondaryBg;
                    btn.ForeColor = UiPrimaryDark;
                    btn.FlatAppearance.BorderColor = UiBorder;
                    btn.FlatAppearance.BorderSize = 1;
                }
            }
        }

        private void CloseActiveWorkspaceWindow()
        {
            if (activeWorkspaceHost == null || activeWorkspaceHost.IsDisposed) return;
            try
            {
                activeWorkspaceHost.Close();
            }
            catch { }
        }

        private void WorkspaceHost_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (!ReferenceEquals(sender, activeWorkspaceHost)) return;
            activeWorkspaceHost = null;
            if (!IsDisposed && !Disposing)
            {
                selectedNavFunctionTag = -1;
                ApplyNavFunctionSelectionStyles();
            }
        }

        private static void ApplyEmbeddedShell(Form module)
        {
            module.TopLevel = false;
            module.FormBorderStyle = FormBorderStyle.None;
            module.Dock = DockStyle.Fill;
            module.AutoScroll = true;
            module.ShowInTaskbar = false;
            module.MaximizeBox = true;
            module.MinimizeBox = false;
            module.ControlBox = true;
            module.MaximumSize = new Size(0, 0);
            module.MinimumSize = new Size(100, 80);
            // 非顶层窗体若保持 Maximized，在父容器内易出现客户区为 0、界面全白
            module.WindowState = FormWindowState.Normal;
        }

        /// <summary>在独立窗体中打开功能模块。<paramref name="maximizeWorkspace"/> 为 false 时居中普通窗口（用于开单类界面）。</summary>
        private void ShowModuleInHost(Form module, int navTagForHighlight, string titleOverride = null, bool maximizeWorkspace = true)
        {
            if (module == null) return;

            CloseActiveWorkspaceWindow();

            selectedNavFunctionTag = navTagForHighlight;
            ApplyNavFunctionSelectionStyles();

            string title = string.IsNullOrEmpty(titleOverride) ? module.Text : titleOverride;
            var host = new Form
            {
                Text = string.IsNullOrEmpty(title) ? "工作区" : title,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = true,
            };
            if (Icon != null)
                host.Icon = Icon;

            Screen sc = Screen.FromControl(this);
            if (maximizeWorkspace)
            {
                host.MinimumSize = new Size(640, 480);
                host.Bounds = sc.WorkingArea;
                host.WindowState = FormWindowState.Maximized;
            }
            else
            {
                host.WindowState = FormWindowState.Normal;
                host.MinimumSize = new Size(800, 560);
                int ww = Math.Min(1200, Math.Max(920, (int)(sc.WorkingArea.Width * 0.72)));
                int wh = Math.Min(860, Math.Max(640, (int)(sc.WorkingArea.Height * 0.78)));
                host.Size = new Size(ww, wh);
                host.Location = new Point(
                    sc.WorkingArea.Left + Math.Max(0, (sc.WorkingArea.Width - ww) / 2),
                    sc.WorkingArea.Top + Math.Max(0, (sc.WorkingArea.Height - wh) / 2));
            }

            ApplyEmbeddedShell(module);
            host.Controls.Add(module);

            FormClosedEventHandler onModuleClosed = null;
            onModuleClosed = (s, ev) =>
            {
                module.FormClosed -= onModuleClosed;
                if (!host.IsDisposed && !host.Disposing)
                {
                    try { host.Close(); }
                    catch { }
                }
            };
            module.FormClosed += onModuleClosed;

            host.FormClosed += WorkspaceHost_FormClosed;
            host.Shown += (s, ev) =>
            {
                if (module.IsDisposed) return;
                try
                {
                    module.PerformLayout();
                    module.Invalidate(true);
                }
                catch { }
            };
            activeWorkspaceHost = host;
            module.Show();
            host.Show();
        }

        /// <summary>控制台顶栏控件（布局由 LayoutHeaderFull 统一计算）。</summary>
        private void CreateHeaderPairingControls()
        {
            if (headerPanel == null) return;

            panelQrHost = new Panel();
            panelQrHost.Name = "panelQrHost";
            panelQrHost.BackColor = Color.Transparent;
            panelQrHost.Paint += QrHostPanel_Paint;
            headerPanel.Controls.Add(panelQrHost);

            picPairingQr = PairingQrControlHelper.CreateQrPictureBox(92);
            picPairingQr.Name = "picPairingQr";
            picPairingQr.BorderStyle = BorderStyle.None;
            picPairingQr.BackColor = Color.White;
            panelQrHost.Controls.Add(picPairingQr);

            lblQrPairingHint = new Label();
            lblQrPairingHint.Name = "lblQrPairingHint";
            lblQrPairingHint.Text = "扫码连接手持端";
            lblQrPairingHint.Visible = false;
            lblQrPairingHint.AutoSize = true;
            lblQrPairingHint.ForeColor = UiMuted;
            lblQrPairingHint.BackColor = Color.Transparent;
            panelQrHost.Controls.Add(lblQrPairingHint);

            panelPairingPill = new Panel();
            panelPairingPill.Name = "panelPairingPill";
            panelPairingPill.BackColor = Color.Transparent;
            panelPairingPill.Paint += PairingPillPanel_Paint;
            headerPanel.Controls.Add(panelPairingPill);

            Label lblTitle = new Label();
            lblTitle.Name = "pairingTitle";
            lblTitle.Text = "配对码";
            lblTitle.AutoSize = true;
            lblTitle.BackColor = Color.Transparent;
            lblTitle.ForeColor = UiHeaderInkMuted;
            panelPairingPill.Controls.Add(lblTitle);

            lblPairingCodeDisplay = new Label();
            lblPairingCodeDisplay.Text = "正在加载...";
            lblPairingCodeDisplay.AutoSize = true;
            lblPairingCodeDisplay.TextAlign = ContentAlignment.MiddleLeft;
            lblPairingCodeDisplay.BackColor = Color.Transparent;
            lblPairingCodeDisplay.ForeColor = Color.White;
            panelPairingPill.Controls.Add(lblPairingCodeDisplay);

            lblHeaderConnectionStatus = new Label();
            lblHeaderConnectionStatus.Name = "lblHeaderConnectionStatus";
            lblHeaderConnectionStatus.Text = "○ 服务未启动";
            lblHeaderConnectionStatus.AutoSize = true;
            lblHeaderConnectionStatus.BackColor = Color.Transparent;
            lblHeaderConnectionStatus.ForeColor = UiHeaderInkMuted;
            headerPanel.Controls.Add(lblHeaderConnectionStatus);

            btnCopyPairingCode = new Button();
            btnCopyPairingCode.Text = "复制";
            StyleHeaderGhostButton(btnCopyPairingCode);
            btnCopyPairingCode.Click += BtnCopyPairingCode_Click;
            headerPanel.Controls.Add(btnCopyPairingCode);

            pairingMoreMenu = new ContextMenuStrip();
            pairingMoreMenu.Font = UiFont(9.5f);
            pairingMoreMenu.Items.Add("更改配对码", null, BtnChangePairingCode_Click);
            pairingMoreMenu.Items.Add("随机生成", null, BtnRandomPairing_Click);
            pairingMoreMenu.Items.Add("广播配对码", null, BtnBroadcastPairing_Click);
            pairingMoreMenu.Items.Add(new ToolStripSeparator());
            pairingMoreMenu.Items.Add("重置增量同步状态", null, BtnResetDeltaSync_Click);

            btnPairingMore = new Button();
            btnPairingMore.Text = "配对 ▾";
            StyleHeaderGhostButton(btnPairingMore);
            btnPairingMore.Click += BtnPairingMore_Click;
            headerPanel.Controls.Add(btnPairingMore);

            headerToolTip = new ToolTip
            {
                AutoPopDelay = 4000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };
            SetupHeaderTooltips();

            lblFiscalYearTitle = new Label();
            lblFiscalYearTitle.Name = "fiscalYearTitle";
            lblFiscalYearTitle.Text = "财年";
            lblFiscalYearTitle.AutoSize = true;
            lblFiscalYearTitle.ForeColor = UiHeaderInkMuted;
            lblFiscalYearTitle.BackColor = Color.Transparent;
            headerPanel.Controls.Add(lblFiscalYearTitle);

            panelFiscalYearHost = new Panel();
            panelFiscalYearHost.Name = "panelFiscalYearHost";
            panelFiscalYearHost.BackColor = UiHeaderControlFill;
            panelFiscalYearHost.Paint += FiscalYearHost_Paint;
            headerPanel.Controls.Add(panelFiscalYearHost);

            cboFiscalYear = new ComboBox();
            cboFiscalYear.Name = "cboFiscalYear";
            cboFiscalYear.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleHeaderFiscalCombo(cboFiscalYear);
            cboFiscalYear.SelectedIndexChanged += CboFiscalYear_SelectedIndexChanged;
            headerPanel.Controls.Add(cboFiscalYear);

            btnSaveFiscalYear = new Button();
            btnSaveFiscalYear.Text = "保存";
            StyleHeaderGhostButton(btnSaveFiscalYear);
            btnSaveFiscalYear.Click += BtnSaveFiscalYear_Click;
            headerPanel.Controls.Add(btnSaveFiscalYear);

            btnNewFiscalYear = new Button();
            btnNewFiscalYear.Text = "新建";
            StyleHeaderGhostButton(btnNewFiscalYear);
            btnNewFiscalYear.Click += BtnNewFiscalYear_Click;
            headerPanel.Controls.Add(btnNewFiscalYear);
        }

        private void LoadFiscalYearCombo()
        {
            if (cboFiscalYear == null) return;

            suppressFiscalYearComboChange = true;
            try
            {
                cboFiscalYear.Items.Clear();
                foreach (int year in FiscalYearService.ListAvailableYears())
                    cboFiscalYear.Items.Add(year);

                int active = FiscalYearService.ActiveYear;
                object match = cboFiscalYear.Items.Cast<object>().FirstOrDefault(x => (int)x == active);
                if (match != null)
                    cboFiscalYear.SelectedItem = match;
                else if (cboFiscalYear.Items.Count > 0)
                    cboFiscalYear.SelectedIndex = 0;
            }
            finally
            {
                suppressFiscalYearComboChange = false;
            }
        }

        private void CboFiscalYear_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressFiscalYearComboChange || cboFiscalYear?.SelectedItem == null)
                return;

            int year = (int)cboFiscalYear.SelectedItem;
            if (year == FiscalYearService.ActiveYear)
                return;

            SwitchFiscalYear(year);
        }

        private void SwitchFiscalYear(int year)
        {
            if (year == FiscalYearService.ActiveYear)
                return;

            DialogResult confirm = MessageBox.Show(
                $"切换后将只显示 {year} 年的数据，其他年份的数据暂时不可见（可随时切回）。\n\n确定切换到 {year} 年吗？",
                "切换年份",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                LoadFiscalYearCombo();
                return;
            }

            bool wasSyncRunning = isSyncServerRunning;
            try
            {
                if (wasSyncRunning)
                {
                    syncServer?.Stop();
                    isSyncServerRunning = false;
                    UpdateSyncUIState(false);
                }

                CloseActiveWorkspaceWindow();
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                DatabaseHelper.Reset();
                FiscalYearService.SwitchToYear(year);
                DatabaseHelper.Reset();
                syncServer?.RestartDatabaseMonitor();

                if (wasSyncRunning)
                {
                    syncServer?.Start();
                    isSyncServerRunning = true;
                    UpdateSyncUIState(true);
                }

                LoadFiscalYearCombo();
                MessageBox.Show($"已切换到 {year} 年。", "切换完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoadFiscalYearCombo();
                MessageBox.Show($"切换年份失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                if (wasSyncRunning && !isSyncServerRunning)
                {
                    try
                    {
                        syncServer?.Start();
                        isSyncServerRunning = true;
                        UpdateSyncUIState(true);
                    }
                    catch { }
                }
            }
        }

        private void BtnSaveFiscalYear_Click(object sender, EventArgs e)
        {
            try
            {
                int year = FiscalYearService.ActiveYear;
                string target = FiscalYearService.GetDbPathForYear(year);
                if (File.Exists(target))
                {
                    DialogResult confirm = MessageBox.Show(
                        $"将覆盖 {year} 年已保存的数据库文件。\n\n确定继续吗？",
                        "保存年份",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (confirm != DialogResult.Yes)
                        return;
                }

                DatabaseHelper.Reset();
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                FiscalYearService.SaveCurrentYear(true);

                LoadFiscalYearCombo();
                MessageBox.Show($"{year} 年数据已保存。", "保存完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存年份失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNewFiscalYear_Click(object sender, EventArgs e)
        {
            int defaultYear = DateTime.Now.Year;
            if (FiscalYearService.ListAvailableYears().Contains(defaultYear))
                defaultYear = FiscalYearService.ListAvailableYears().DefaultIfEmpty(DateTime.Now.Year).Max() + 1;

            int newYear;
            using (var form = new Form())
            {
                form.Text = "新建年份";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new Size(320, 140);

                var lbl = new Label
                {
                    Text = "请输入要新建的年份（将复制基础资料，业务单据为空）：",
                    Location = new Point(12, 12),
                    Size = new Size(296, 36)
                };
                var num = new NumericUpDown
                {
                    Minimum = 2000,
                    Maximum = 2100,
                    Value = Math.Max(2000, Math.Min(2100, defaultYear)),
                    Location = new Point(12, 54),
                    Size = new Size(100, 23)
                };
                var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(140, 96), Size = new Size(80, 28) };
                var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(228, 96), Size = new Size(80, 28) };
                form.Controls.AddRange(new Control[] { lbl, num, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog(this) != DialogResult.OK)
                    return;
                newYear = (int)num.Value;
            }

            if (FiscalYearService.ListAvailableYears().Contains(newYear))
            {
                MessageBox.Show($"年份 {newYear} 已存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool wasSyncRunning = isSyncServerRunning;
            int sourceYear = FiscalYearService.ActiveYear;
            try
            {
                if (wasSyncRunning)
                {
                    syncServer?.Stop();
                    isSyncServerRunning = false;
                    UpdateSyncUIState(false);
                }

                CloseActiveWorkspaceWindow();
                DatabaseHelper.Reset();
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                FiscalYearService.CreateNewYear(newYear);
                DatabaseHelper.Reset();
                syncServer?.RestartDatabaseMonitor();

                if (wasSyncRunning)
                {
                    syncServer?.Start();
                    isSyncServerRunning = true;
                    UpdateSyncUIState(true);
                }

                LoadFiscalYearCombo();
                MessageBox.Show($"已新建并切换到 {newYear} 年。\n基础资料已从 {sourceYear} 年复制，业务单据为空。",
                    "新建完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoadFiscalYearCombo();
                MessageBox.Show($"新建年份失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                if (wasSyncRunning && !isSyncServerRunning)
                {
                    try
                    {
                        syncServer?.Start();
                        isSyncServerRunning = true;
                        UpdateSyncUIState(true);
                    }
                    catch { }
                }
            }
        }

        // 更改配对码
        private void BtnChangePairingCode_Click(object sender, EventArgs e)
        {
            if (syncServer == null) return;

            // 显示设置对话框
            syncServer.ShowPairingCodeSettings(this);

            // 更新显示
            if (lblPairingCodeDisplay != null)
            {
                lblPairingCodeDisplay.Text = syncServer.GetPairingCode();
            }

            // 如果服务正在运行，提示重启
            if (isSyncServerRunning)
            {
                MessageBox.Show("配对码已更新，将自动重启广播服务。\n已连接的设备需要重新配对。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // 复制配对码
        private void BtnCopyPairingCode_Click(object sender, EventArgs e)
        {
            if (syncServer == null) return;

            string code = syncServer.GetPairingCode();
            Clipboard.SetText(code);

            // 显示临时提示
            ToolTip tip = new ToolTip();
            tip.Show("已复制到剪贴板", btnCopyPairingCode, 0, -30, 2000);
        }

        // 随机生成配对码
        private void BtnRandomPairing_Click(object sender, EventArgs e)
        {
            if (syncServer == null) return;

            if (MessageBox.Show(
                    "随机生成将更换配对码，已连接的手持端需重新扫码配对。\n是否继续？",
                    "确认随机配对码",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            // 生成6位随机数
            Random rand = new Random();
            string newCode = rand.Next(100000, 999999).ToString();

            // 保存
            syncServer.SavePairingCode(newCode);

            // 更新显示
            if (lblPairingCodeDisplay != null)
            {
                lblPairingCodeDisplay.Text = syncServer.GetPairingCode();
            }

            // 如果服务正在运行，提示重启
            if (isSyncServerRunning)
            {
                MessageBox.Show("配对码已更新，将自动重启广播服务。\n已连接的设备需要重新配对。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // 广播配对码
        private void BtnBroadcastPairing_Click(object sender, EventArgs e)
        {
            if (syncServer == null) return;

            syncServer.BroadcastPairingCode();

            ToolTip tip = new ToolTip();
            Control anchor = btnPairingMore ?? (Control)sender;
            tip.Show("配对码已广播", anchor, 0, -30, 2000);
        }

        // 重置增量同步状态（清空 change_log，不影响业务数据库）
        private void BtnResetDeltaSync_Click(object sender, EventArgs e)
        {
            if (syncServer == null)
            {
                MessageBox.Show("同步模块未初始化。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    "将清空 PC 端增量变更日志（sync_delta_state.json 中的 change_log）。\n\n" +
                    "业务数据库不受影响；已对齐的手持端需重新全量同步。\n" +
                    "新装/重装手持端将不再收到历史测试增量。\n\n是否继续？",
                    "确认重置增量同步状态",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                syncServer.ResetDeltaSyncState();
                MessageBox.Show(
                    "增量同步状态已重置。\n重装或新连的手持端将仅通过全量快照对齐。",
                    "完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重置失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 创建右侧同步管理器面板
        private void CreateSidePanel()
        {
            // 如果 sidePanel 为 null，才创建新的
            if (sidePanel == null)
            {
                sidePanel = new Panel();
                this.Controls.Add(sidePanel);
            }

            // 调整面板大小和位置，增加高度和间距
            sidePanel.Size = new Size(400, 575);
            sidePanel.Location = new Point(770, 140); // 保持Y坐标140不变
            sidePanel.BackColor = UiSurface;
            sidePanel.BorderStyle = BorderStyle.None;
            sidePanel.AutoScroll = true;

            // 清空现有控件，重新添加
            sidePanel.Controls.Clear();

            // 同步管理器标题
            Label sideTitle = new Label();
            sideTitle.Name = "sideTitle";
            sideTitle.Text = "数据同步管理器";
            sideTitle.Font = UiFont(14f, FontStyle.Bold);
            sideTitle.ForeColor = UiPrimaryDark;
            sideTitle.Size = new Size(200, 40);
            sideTitle.Location = new Point(20, 10); // Y坐标从10增加到15，增加顶部间距
            sidePanel.Controls.Add(sideTitle);

            // 服务状态卡片
            Panel statusPanel = CreateSideCardPanel(null, false, out _);
            statusPanel.Name = "statusPanel";

            lblServiceStatus = new Label();
            lblServiceStatus.Name = "lblServiceStatus";
            lblServiceStatus.Text = "同步服务 · 未启动";
            lblServiceStatus.Font = UiFont(12f, FontStyle.Bold);
            lblServiceStatus.ForeColor = UiStatusStopped;
            lblServiceStatus.Dock = DockStyle.Top;
            lblServiceStatus.Height = 28;
            statusPanel.Controls.Add(lblServiceStatus);

            var statusMetaPanel = new Panel();
            statusMetaPanel.Dock = DockStyle.Top;
            statusMetaPanel.Height = 24;
            statusMetaPanel.BackColor = Color.Transparent;
            statusMetaPanel.Padding = new Padding(0, 4, 0, 0);

            lblPortInfo = new Label();
            lblPortInfo.Name = "lblPortInfo";
            lblPortInfo.Text = "端口: 8080";
            lblPortInfo.Font = UiFont(9f);
            lblPortInfo.ForeColor = UiMuted;
            lblPortInfo.AutoSize = false;
            lblPortInfo.Dock = DockStyle.Left;
            lblPortInfo.Width = 120;
            statusMetaPanel.Controls.Add(lblPortInfo);

            lblOnlineCount = new Label();
            lblOnlineCount.Name = "lblOnlineCount";
            lblOnlineCount.Text = "在线设备: 0 台";
            lblOnlineCount.Font = UiFont(9f, FontStyle.Bold);
            lblOnlineCount.ForeColor = UiSuccess;
            lblOnlineCount.AutoSize = false;
            lblOnlineCount.Dock = DockStyle.Fill;
            lblOnlineCount.TextAlign = ContentAlignment.MiddleLeft;
            statusMetaPanel.Controls.Add(lblOnlineCount);

            statusPanel.Controls.Add(statusMetaPanel);
            sidePanel.Controls.Add(statusPanel);

            // 控制按钮卡片
            Panel controlPanel = CreateSideCardPanel(null, false, out _);
            controlPanel.Name = "syncControlPanel";
            controlPanel.Padding = new Padding(12, 12, 12, 12);

            btnStartSync = new Button();
            btnStartSync.Name = "btnStartSync";
            btnStartSync.Text = "启动服务";
            btnStartSync.Font = UiFont(9f, FontStyle.Bold);
            btnStartSync.BackColor = UiSuccess;
            btnStartSync.ForeColor = Color.White;
            btnStartSync.FlatStyle = FlatStyle.Flat;
            btnStartSync.FlatAppearance.BorderSize = 0;
            btnStartSync.Cursor = Cursors.Hand;
            btnStartSync.TabIndex = 0;
            btnStartSync.Click += BtnStartSync_Click;
            controlPanel.Controls.Add(btnStartSync);

            btnStopSync = new Button();
            btnStopSync.Name = "btnStopSync";
            btnStopSync.Text = "停止服务";
            btnStopSync.Font = UiFont(9f, FontStyle.Bold);
            btnStopSync.BackColor = UiDanger;
            btnStopSync.ForeColor = Color.White;
            btnStopSync.FlatStyle = FlatStyle.Flat;
            btnStopSync.FlatAppearance.BorderSize = 0;
            btnStopSync.Cursor = Cursors.Hand;
            btnStopSync.Enabled = false;
            btnStopSync.TabIndex = 1;
            btnStopSync.Click += BtnStopSync_Click;
            controlPanel.Controls.Add(btnStopSync);

            chkAutoStartSync = new CheckBox();
            chkAutoStartSync.Name = "chkAutoStartSync";
            chkAutoStartSync.Text = "打开软件时自动启动同步";
            chkAutoStartSync.Font = UiFont(8.5f);
            chkAutoStartSync.ForeColor = UiMuted;
            chkAutoStartSync.AutoSize = true;
            chkAutoStartSync.Checked = true;
            chkAutoStartSync.CheckedChanged += ChkAutoStartSync_CheckedChanged;
            controlPanel.Controls.Add(chkAutoStartSync);

            sidePanel.Controls.Add(controlPanel);

            // 同步进度卡片
            Panel syncProgressPanel = CreateSideCardPanel("同步进程", true, out _);
            syncProgressPanel.Name = "syncProgressPanel";

            lblSyncActivity = new Label();
            lblSyncActivity.Name = "lblSyncActivity";
            lblSyncActivity.Text = "同步空闲";
            lblSyncActivity.Font = UiFont(8.5f);
            lblSyncActivity.ForeColor = UiMuted;
            lblSyncActivity.AutoSize = false;
            lblSyncActivity.Dock = DockStyle.Top;
            lblSyncActivity.Height = 24;
            syncProgressPanel.Controls.Add(lblSyncActivity);

            pbSyncProgress = new ProgressBar();
            pbSyncProgress.Name = "pbSyncProgress";
            pbSyncProgress.Minimum = 0;
            pbSyncProgress.Maximum = 100;
            pbSyncProgress.Value = 0;
            pbSyncProgress.Dock = DockStyle.Top;
            pbSyncProgress.Height = 14;
            pbSyncProgress.Margin = new Padding(0, 6, 0, 0);
            pbSyncProgress.Visible = false;
            syncProgressPanel.Controls.Add(pbSyncProgress);

            sidePanel.Controls.Add(syncProgressPanel);

            // 在线设备卡片
            Panel groupClients = CreateSideCardPanel("已连接设备", true, out _);
            groupClients.Name = "groupClients";

            lvSyncClients = new ListView();
            lvSyncClients.Name = "lvSyncClients";
            lvSyncClients.View = View.Details;
            lvSyncClients.FullRowSelect = true;
            lvSyncClients.GridLines = true;
            lvSyncClients.Dock = DockStyle.None;
            lvSyncClients.Columns.Add("设备名称", 100);
            lvSyncClients.Columns.Add("IP", 90);
            lvSyncClients.Columns.Add("连接", 56);
            lvSyncClients.Columns.Add("配置同步", 72);
            lvSyncClients.Columns.Add("最后连接", 64);
            lvSyncClients.Font = UiFont(9f);
            lvSyncClients.BackColor = UiCard;
            lvSyncClients.BorderStyle = BorderStyle.None;
            syncClientsMenu = new ContextMenuStrip();
            syncClientsMenu.Items.Add("删除设备", null, SyncClientsMenu_DeleteClick);
            lvSyncClients.ContextMenuStrip = syncClientsMenu;
            lvSyncClients.MouseUp += LvSyncClients_MouseUp;
            groupClients.Controls.Add(lvSyncClients);

            lblSyncClientsEmpty = new Label();
            lblSyncClientsEmpty.Name = "lblSyncClientsEmpty";
            lblSyncClientsEmpty.Text = "请先启动同步服务";
            lblSyncClientsEmpty.Font = UiFont(8.5f);
            lblSyncClientsEmpty.ForeColor = UiMuted;
            lblSyncClientsEmpty.TextAlign = ContentAlignment.MiddleCenter;
            lblSyncClientsEmpty.AutoSize = false;
            lblSyncClientsEmpty.Visible = true;
            groupClients.Controls.Add(lblSyncClientsEmpty);

            sidePanel.Controls.Add(groupClients);

            // 待同步基础配置卡片
            Panel groupPendingConfig = CreateSideCardPanel("待同步基础配置", true, out _);
            groupPendingConfig.Name = "groupPendingConfig";

            lblPendingConfigSummary = new Label();
            lblPendingConfigSummary.Name = "lblPendingConfigSummary";
            lblPendingConfigSummary.Text = "暂无待同步项";
            lblPendingConfigSummary.Font = UiFont(8.5f);
            lblPendingConfigSummary.ForeColor = UiMuted;
            lblPendingConfigSummary.AutoSize = false;
            lblPendingConfigSummary.Height = 34;
            groupPendingConfig.Controls.Add(lblPendingConfigSummary);

            lvPendingConfig = new ListView();
            lvPendingConfig.Name = "lvPendingConfig";
            lvPendingConfig.View = View.Details;
            lvPendingConfig.FullRowSelect = true;
            lvPendingConfig.GridLines = true;
            lvPendingConfig.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lvPendingConfig.Columns.Add("类型", 56);
            lvPendingConfig.Columns.Add("操作", 44);
            lvPendingConfig.Columns.Add("名称", 120);
            lvPendingConfig.Columns.Add("时间", 52);
            lvPendingConfig.Font = UiFont(9f);
            lvPendingConfig.BackColor = UiCard;
            lvPendingConfig.BorderStyle = BorderStyle.None;
            groupPendingConfig.Controls.Add(lvPendingConfig);

            btnSyncPendingConfig = new Button();
            btnSyncPendingConfig.Name = "btnSyncPendingConfig";
            btnSyncPendingConfig.Text = "同步到手持端";
            btnSyncPendingConfig.Font = UiFont(9f);
            btnSyncPendingConfig.BackColor = UiPrimary;
            btnSyncPendingConfig.ForeColor = Color.White;
            btnSyncPendingConfig.FlatStyle = FlatStyle.Flat;
            btnSyncPendingConfig.FlatAppearance.BorderSize = 0;
            btnSyncPendingConfig.Height = 32;
            btnSyncPendingConfig.Enabled = false;
            btnSyncPendingConfig.Click += BtnSyncPendingConfig_Click;
            groupPendingConfig.Controls.Add(btnSyncPendingConfig);

            sidePanel.Controls.Add(groupPendingConfig);

            // 旧“同步日志显示框”已移除：系统配置内不再展示同步日志。
        }

        // 初始化同步服务器
        private void InitializeSyncServer()
        {
            try
            {
                // 旧“同步日志显示框”已移除，这里不再绑定 RichTextBox 日志输出。
                syncServer = new AutoSyncServer(null, this);

                // 订阅事件（设备列表与数量以 OnConnectedDevicesChanged 快照为准）
                syncServer.OnConnectedDevicesChanged += SyncServer_OnConnectedDevicesChanged;
                syncServer.OnPendingConfigDeltaChanged += SyncServer_OnPendingConfigDeltaChanged;
                syncServer.OnPairingCodeChanged += SyncServer_OnPairingCodeChanged;
                syncServer.OnSyncProgress += SyncServer_OnSyncProgress;

                syncServer.RefreshKnownDeviceRegistry();

                // 更新配对码显示
                if (lblPairingCodeDisplay != null)
                {
                    lblPairingCodeDisplay.Text = syncServer.GetPairingCode();
                }
                RefreshPairingQrDisplay();
                RefreshHeaderConnectionStatus();
                RefreshSyncPortDisplay();

                // 设置托盘菜单
                NotifyIcon trayIcon = new NotifyIcon
                {
                    Icon = this.Icon,
                    Text = "冷库宝同步服务器",
                    Visible = true
                };
                syncServer.SetupTrayIcon(trayIcon);

                if (chkAutoStartSync != null)
                {
                    chkAutoStartSync.CheckedChanged -= ChkAutoStartSync_CheckedChanged;
                    chkAutoStartSync.Checked = syncServer.LoadAutoStartSetting();
                    chkAutoStartSync.CheckedChanged += ChkAutoStartSync_CheckedChanged;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化同步服务器失败: {ex.Message}");
            }
        }

        // 配对码变更事件
        private void RefreshPairingQrDisplay()
        {
            if (syncServer == null || picPairingQr == null || picPairingQr.IsDisposed) return;
            try
            {
                if (picPairingQr.InvokeRequired)
                {
                    picPairingQr.Invoke(new Action(RefreshPairingQrDisplay));
                    return;
                }
                PairingQrControlHelper.RefreshQr(picPairingQr, syncServer.GetPairingCode(), syncServer.GetTcpPort());
            }
            catch { }
        }

        private void SyncServer_OnPairingCodeChanged(object sender, string newCode)
        {
            if (lblPairingCodeDisplay != null && !lblPairingCodeDisplay.IsDisposed)
            {
                if (lblPairingCodeDisplay.InvokeRequired)
                {
                    lblPairingCodeDisplay.Invoke(new Action(() =>
                    {
                        lblPairingCodeDisplay.Text = newCode;
                    }));
                }
                else
                {
                    lblPairingCodeDisplay.Text = newCode;
                }
            }
            RefreshPairingQrDisplay();
        }

        private void SyncServer_OnSyncProgress(object sender, SyncProgressEventArgs e)
        {
            if (sidePanel == null || sidePanel.IsDisposed) return;

            void Apply()
            {
                if (lblSyncActivity != null && !lblSyncActivity.IsDisposed)
                    lblSyncActivity.Text = string.IsNullOrEmpty(e.Status) ? "同步空闲" : e.Status;
                if (pbSyncProgress == null || pbSyncProgress.IsDisposed) return;
                if (e.ShowProgressBar)
                {
                    pbSyncProgress.Visible = true;
                    int v = Math.Max(0, Math.Min(100, e.Progress));
                    pbSyncProgress.Value = v;
                }
                else
                {
                    pbSyncProgress.Visible = false;
                    pbSyncProgress.Value = 0;
                }
            }

            try
            {
                if (sidePanel.InvokeRequired)
                    sidePanel.BeginInvoke(new Action(Apply));
                else
                    Apply();
            }
            catch { }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            TrySilentDailyAutoBackup();
            TryAutoStartSyncServer();
        }

        private void TryAutoStartSyncServer()
        {
            if (isSyncServerRunning || syncServer == null) return;
            if (!syncServer.LoadAutoStartSetting()) return;

            try
            {
                syncServer.Start();
                isSyncServerRunning = true;
                UpdateSyncUIState(true);
                RefreshSyncPortDisplay();

                if (lblServiceStatus != null)
                {
                    lblServiceStatus.Text = "同步服务 · 运行中";
                    lblServiceStatus.ForeColor = UiSuccess;
                }

                if (lblPairingCodeDisplay != null)
                {
                    lblPairingCodeDisplay.Text = syncServer.GetPairingCode();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"自动启动同步服务失败: {ex.Message}");
            }
        }

        private void ChkAutoStartSync_CheckedChanged(object sender, EventArgs e)
        {
            if (syncServer == null || chkAutoStartSync == null) return;
            syncServer.SaveAutoStartSetting(chkAutoStartSync.Checked);
        }

        // 启动服务
        private void BtnStartSync_Click(object sender, EventArgs e)
        {
            if (isSyncServerRunning)
            {
                MessageBox.Show("同步服务已经在运行中！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                syncServer.Start();
                isSyncServerRunning = true;

                // 更新UI状态
                UpdateSyncUIState(true);
                RefreshSyncPortDisplay();

                if (lblServiceStatus != null)
                {
                    lblServiceStatus.Text = "同步服务 · 运行中";
                    lblServiceStatus.ForeColor = UiSuccess;
                }

                // 提示配对码
                if (lblPairingCodeDisplay != null)
                {
                    string code = syncServer.GetPairingCode();
                    lblPairingCodeDisplay.Text = code;

                    // 显示气泡提示
                    NotifyIcon trayIcon = new NotifyIcon();
                    trayIcon.Visible = true;
                    trayIcon.Icon = this.Icon;
                    trayIcon.ShowBalloonTip(5000, "冷库宝", $"同步服务已启动\n配对码: {code}", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动同步服务失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 停止服务
        private void BtnStopSync_Click(object sender, EventArgs e)
        {
            if (!isSyncServerRunning || syncServer == null) return;

            if (MessageBox.Show("确定要停止同步服务吗？\n这将断开所有已连接的设备。", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                syncServer.Stop();
                isSyncServerRunning = false;

                // 更新UI状态
                UpdateSyncUIState(false);

                if (lblServiceStatus != null)
                {
                    lblServiceStatus.Text = "同步服务 · 未启动";
                    lblServiceStatus.ForeColor = UiStatusStopped;
                }

                UpdatePendingSyncUiState(pendingConfigDeltaCount);

                if (lblSyncActivity != null && !lblSyncActivity.IsDisposed)
                    lblSyncActivity.Text = "同步空闲";
                if (pbSyncProgress != null && !pbSyncProgress.IsDisposed)
                {
                    pbSyncProgress.Visible = false;
                    pbSyncProgress.Value = 0;
                }
            }
        }

        // 更新UI状态
        private void UpdateSyncUIState(bool isRunning)
        {
            if (btnStartSync != null) btnStartSync.Enabled = !isRunning;
            if (btnStopSync != null) btnStopSync.Enabled = isRunning;
            if (lblOnlineCount != null)
            {
                lblOnlineCount.Text = isRunning ? "在线设备: 0 台" : "在线设备: - 台";
            }
            RefreshHeaderConnectionStatus(isRunning ? 0 : (int?)null);
            UpdatePendingSyncUiState(pendingConfigDeltaCount);
        }

        private void SyncServer_OnConnectedDevicesChanged(object sender, ConnectedDevicesChangedEventArgs e)
        {
            if (IsDisposed) return;

            void Apply()
            {
                int onlineCount = e.Devices.Count(d => d.IsConnected);
                RefreshHeaderConnectionStatus(onlineCount);

                if (sidePanel == null || sidePanel.IsDisposed) return;

                if (lblOnlineCount != null && !lblOnlineCount.IsDisposed)
                {
                    string summary = syncServer?.GetDeviceSyncStatusSummary() ?? "";
                    if (e.Devices.Count == 0)
                        lblOnlineCount.Text = "暂无注册设备";
                    else if (string.IsNullOrWhiteSpace(summary))
                        lblOnlineCount.Text = $"在线 {onlineCount} 台";
                    else
                        lblOnlineCount.Text = $"在线 {onlineCount} 台 · {summary}";
                }

                if (lvSyncClients == null || lvSyncClients.IsDisposed) return;

                lvSyncClients.Items.Clear();
                foreach (var d in e.Devices)
                {
                    var item = new ListViewItem(d.DeviceName ?? "");
                    item.SubItems.Add(string.IsNullOrWhiteSpace(d.IpAddress) ? "-" : d.IpAddress);
                    item.SubItems.Add(d.IsConnected ? "已连接" : "未连接");
                    item.SubItems.Add(d.SyncStatusLabel ?? "已同步");
                    var lastConn = d.LastConnectedAt != default && d.LastConnectedAt > DateTime.MinValue
                        ? d.LastConnectedAt.ToString("MM-dd HH:mm")
                        : "-";
                    item.SubItems.Add(lastConn);
                    item.ForeColor = d.IsConnected ? UiListOnline : UiListOffline;
                    item.Tag = d.DeviceId;
                    lvSyncClients.Items.Add(item);
                }

                RefreshSyncClientsEmptyState(e.Devices.Count);
                UpdatePendingSyncUiState(pendingConfigDeltaCount);
            }

            try
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(Apply));
                else
                    Apply();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新连接设备列表失败: {ex.Message}");
            }
        }

        private void LvSyncClients_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || lvSyncClients == null) return;
            var hit = lvSyncClients.HitTest(e.Location);
            if (hit.Item == null) return;
            hit.Item.Selected = true;
        }

        private void SyncClientsMenu_DeleteClick(object sender, EventArgs e)
        {
            if (syncServer == null || lvSyncClients == null || lvSyncClients.SelectedItems.Count == 0)
                return;

            var item = lvSyncClients.SelectedItems[0];
            string deviceId = item.Tag as string;
            string deviceName = item.Text;
            bool isConnected = item.SubItems.Count > 2 && item.SubItems[2].Text == "已连接";

            if (isConnected)
            {
                MessageBox.Show("设备当前在线，请先断开连接再删除。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    $"确定从列表中删除设备「{deviceName}」吗？\n删除后需重新扫码配对才会再次出现。",
                    "确认删除",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            if (syncServer.TryRemoveKnownDevice(deviceId))
                return;

            MessageBox.Show("删除失败，请稍后重试。", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SyncServer_OnPendingConfigDeltaChanged(object sender, PendingConfigDeltaChangedEventArgs e)
        {
            if (sidePanel == null || sidePanel.IsDisposed) return;

            void Apply()
            {
                if (lvPendingConfig == null || lvPendingConfig.IsDisposed) return;

                lvPendingConfig.Items.Clear();
                foreach (var item in e.Items)
                {
                    var row = new ListViewItem(item.EntityTypeLabel ?? item.EntityType ?? "");
                    row.SubItems.Add(item.OpTypeLabel ?? item.OpType ?? "");
                    row.SubItems.Add(item.DisplayName ?? item.EntityKey ?? "");
                    row.SubItems.Add(item.CommitTime.ToString("HH:mm:ss"));
                    row.Tag = item.CommitSeq;
                    if (string.Equals(item.OpType, "DELETE", StringComparison.OrdinalIgnoreCase))
                        row.ForeColor = UiDanger;
                    lvPendingConfig.Items.Add(row);
                }

                int count = e.Count;
                UpdatePendingSyncUiState(count);
            }

            try
            {
                if (sidePanel.InvokeRequired)
                    sidePanel.BeginInvoke(new Action(Apply));
                else
                    Apply();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新待同步基础配置列表失败: {ex.Message}");
            }
        }

        private void BtnSyncPendingConfig_Click(object sender, EventArgs e)
        {
            if (syncServer == null) return;
            try
            {
                int notified = syncServer.PushPendingConfigDeltaToHandhelds();
                if (notified <= 0)
                {
                    var pending = syncServer.GetPendingConfigDeltaItems();
                    if (pending.Count == 0)
                        MessageBox.Show("当前没有待同步的基础配置。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show("没有在线的手持端，请先让手持端连接电脑。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show($"已通知 {notified} 台手持端拉取配置，请稍候查看同步进程。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"同步失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 启动自动刷新
        private void StartAutoRefresh()
        {
            refreshTimer = new Timer();
            refreshTimer.Interval = 60000;
            refreshTimer.Tick += (s, e) => { }; // 空事件，保持兼容性
            refreshTimer.Start();
        }

        // ==================== 原有功能按钮事件 ====================

        private void button1_Click(object sender, EventArgs e)  // 入库查询
        {
            ShowModuleInHost(new CompleteInboundQueryForm(), 0);
        }

        private void button2_Click(object sender, EventArgs e) // 入库开单
        {
            ShowModuleInHost(new InboundForm(), 1, null, false);
        }

        private void button3_Click(object sender, EventArgs e) // 销售出库
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                bool connected = db.TestConnection();

                if (!connected)
                {
                    MessageBox.Show("数据库连接失败，请检查数据库服务！", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                DataTable clients = db.GetAllClients();
                DataTable products = db.GetAllProductTypes();

                if (clients.Rows.Count == 0)
                {
                    MessageBox.Show("请先添加客户信息！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (products.Rows.Count == 0)
                {
                    MessageBox.Show("请先添加商品型号！", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (activeWorkspaceHost != null && !activeWorkspaceHost.IsDisposed)
                {
                    var embedded = activeWorkspaceHost.Controls.OfType<SalesForm>().FirstOrDefault();
                    if (embedded != null)
                    {
                        selectedNavFunctionTag = 2;
                        ApplyNavFunctionSelectionStyles();
                        activeWorkspaceHost.Activate();
                        embedded.BringToFront();
                        embedded.Focus();
                        return;
                    }
                }

                var salesForm = new SalesForm();
                salesForm.FormClosed += (s, args) =>
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} - 销售出库窗口关闭");
                ShowModuleInHost(salesForm, 2, null, false);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} - 打开销售出库窗口");
            }
            catch (Exception ex)
            {
                string errorMessage = $"客户报账失败: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $"\n内部错误: {ex.InnerException.Message}";
                }
                MessageBox.Show(errorMessage, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} - 销售出库错误: {ex.Message}");
            }
        }

        private void button4_Click(object sender, EventArgs e)  // 销售查询
        {
            ShowModuleInHost(new CompleteSalesQueryForm(), 3);
        }

        private void button5_Click(object sender, EventArgs e) // 包装记账
        {
            ShowModuleInHost(new PackagingForm(), 4, null, false);
        }

        private void button6_Click(object sender, EventArgs e)  // 包装查询
        {
            ShowModuleInHost(new CompletePackagingQueryForm(), 5);
        }

        private void button7_Click(object sender, EventArgs e) // 客户对账
        {
            try
            {
                ShowModuleInHost(new ClientBalanceForm(), 6);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"客户对账失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button8_Click(object sender, EventArgs e) // 系统设置
        {
            ShowModuleInHost(new SystemSettingsForm(), 7);
        }

        private void BtnPresaleQuery_Click(object sender, EventArgs e)
        {
            ShowModuleInHost(new CompletePresaleQueryForm(), -1);
        }

        private void BtnLedgerQuery_Click(object sender, EventArgs e)
        {
            ShowModuleInHost(new LedgerQueryForm(), -1);
        }

        // ==================== 工具按钮事件 ====================

        private void BtnYearEndSettlement_Click(object sender, EventArgs e)
        {
            // 已迁移至顶栏年份切换，保留方法避免设计器引用报错
        }

        private void BtnHistoricalArchive_Click(object sender, EventArgs e)
        {
            // 已迁移至顶栏年份切换，保留方法避免设计器引用报错
        }

        private void BtnDataBackup_Click(object sender, EventArgs e)
        {
            TryRunDataBackup();
        }

        private bool TryRunDataBackup()
        {
            try
            {
                using (BackupOptionsDialog optionsDialog = new BackupOptionsDialog())
                {
                    if (optionsDialog.ShowDialog() != DialogResult.OK)
                        return false;

                    if (optionsDialog.BackupType == BackupType.Simple)
                        return SimpleBackup();

                    if (optionsDialog.BackupType == BackupType.Full)
                        return FullBackup();

                    if (optionsDialog.BackupType == BackupType.Auto)
                        return AutoBackup();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"备份失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }

        // ==================== 恢复数据相关方法 ====================

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            // 创建恢复选项菜单
            ContextMenuStrip menu = new ContextMenuStrip();

            menu.Items.Add("📁 恢复数据库文件 (.db)", null, (s, args) =>
            {
                RestoreDatabaseFile();
            });

            menu.Items.Add("📦 恢复完整备份 (.zip)", null, (s, args) =>
            {
                RestoreZipBackup();
            });

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("📂 打开自动备份文件夹（文档）", null, (s, args) =>
            {
                SafeDataPaths.OpenSafeAutoBackupFolder();
            });

            menu.Items.Add("📂 打开程序备份文件夹", null, (s, args) =>
            {
                string backupDir = Path.Combine(Application.StartupPath, "数据备份");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }
                System.Diagnostics.Process.Start("explorer.exe", backupDir);
            });

            // 在按钮位置显示菜单
            if (sender is Button button)
            {
                menu.Show(button, new Point(0, button.Height));
            }
            else
            {
                menu.Show(Cursor.Position);
            }
        }

        private void RestoreDatabaseFile()
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Filter = "数据库文件 (*.db)|*.db|所有文件 (*.*)|*.*";
            openDialog.Title = "选择要恢复的数据库备份文件";

            openDialog.InitialDirectory = SafeDataPaths.SafeAutoBackupDirectory;
            try
            {
                if (!Directory.Exists(openDialog.InitialDirectory))
                    Directory.CreateDirectory(openDialog.InitialDirectory);
            }
            catch
            {
                string fallback = Path.Combine(Application.StartupPath, "数据备份");
                Directory.CreateDirectory(fallback);
                openDialog.InitialDirectory = fallback;
            }

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                string backupFile = openDialog.FileName;

                DialogResult confirm = MessageBox.Show(
                    $"⚠️ 警告：恢复备份将覆盖当前数据库！\n\n" +
                    $"文件: {Path.GetFileName(backupFile)}\n" +
                    $"大小: {FormatFileSize(new FileInfo(backupFile).Length)}\n\n" +
                    $"确定要继续吗？",
                    "确认恢复",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                try
                {
                    // 查找当前数据库文件
                    string currentDbFile = FindDatabaseFile();

                    if (currentDbFile == null)
                    {
                        MessageBox.Show("找不到当前数据库文件！", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // 备份当前数据库（以防万一）
                    string backupDir_pre = Path.Combine(Application.StartupPath, "恢复前备份");
                    Directory.CreateDirectory(backupDir_pre);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupFilePath = Path.Combine(backupDir_pre, $"恢复前_{timestamp}.db");
                    File.Copy(currentDbFile, backupFilePath, true);

                    // 如果同步服务正在运行，先停止
                    bool wasSyncRunning = isSyncServerRunning;
                    if (wasSyncRunning)
                    {
                        syncServer?.Stop();
                        isSyncServerRunning = false;
                        UpdateSyncUIState(false);
                    }

                    // 恢复备份
                    File.Copy(backupFile, currentDbFile, true);

                    // 如果之前同步服务在运行，重新启动
                    if (wasSyncRunning)
                    {
                        try
                        {
                            syncServer?.Start();
                            isSyncServerRunning = true;
                            UpdateSyncUIState(true);
                        }
                        catch { }
                    }

                    MessageBox.Show($"✅ 数据恢复成功！\n\n" +
                                   $"原数据库已备份到:\n{backupFilePath}\n\n" +
                                   $"建议重启应用程序以确保所有功能正常。",
                                   "恢复完成",
                                   MessageBoxButtons.OK,
                                   MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"恢复失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RestoreZipBackup()
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Filter = "压缩备份文件 (*.zip)|*.zip";
            openDialog.Title = "选择要恢复的完整备份文件";
            openDialog.InitialDirectory = Path.Combine(Application.StartupPath, "数据备份");

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                string backupFile = openDialog.FileName;

                DialogResult confirm = MessageBox.Show(
                    $"⚠️ 警告：恢复完整备份将覆盖所有当前数据！\n\n" +
                    $"文件: {Path.GetFileName(backupFile)}\n" +
                    $"大小: {FormatFileSize(new FileInfo(backupFile).Length)}\n\n" +
                    $"确定要继续吗？",
                    "确认恢复",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                try
                {
                    // 创建临时目录
                    string tempDir = Path.Combine(Path.GetTempPath(), $"restore_temp_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        // 解压备份文件
                        ZipFile.ExtractToDirectory(backupFile, tempDir);

                        // 如果同步服务正在运行，先停止
                        bool wasSyncRunning = isSyncServerRunning;
                        if (wasSyncRunning)
                        {
                            syncServer?.Stop();
                            isSyncServerRunning = false;
                            UpdateSyncUIState(false);
                        }

                        // 恢复数据库文件
                        var dbFiles = Directory.GetFiles(tempDir, "*.db");
                        foreach (var dbFile in dbFiles)
                        {
                            string destFile = FindDatabaseFile();
                            if (destFile != null)
                            {
                                // 备份原文件
                                string backupDir = Path.Combine(Application.StartupPath, "恢复前备份");
                                Directory.CreateDirectory(backupDir);
                                string backupFilePath = Path.Combine(backupDir, $"恢复前_{Path.GetFileName(dbFile)}");
                                if (File.Exists(destFile))
                                {
                                    File.Copy(destFile, backupFilePath, true);
                                }

                                // 恢复文件
                                File.Copy(dbFile, destFile, true);
                            }
                        }

                        // 恢复配置文件
                        RestoreConfigFiles(tempDir);

                        // 如果之前同步服务在运行，重新启动
                        if (wasSyncRunning)
                        {
                            try
                            {
                                syncServer?.Start();
                                isSyncServerRunning = true;
                                UpdateSyncUIState(true);
                            }
                            catch { }
                        }

                        MessageBox.Show($"✅ 完整恢复成功！\n\n" +
                                       $"所有数据已从备份中恢复。\n" +
                                       $"建议重启应用程序以确保所有功能正常。",
                                       "恢复完成",
                                       MessageBoxButtons.OK,
                                       MessageBoxIcon.Information);
                    }
                    finally
                    {
                        // 清理临时文件
                        if (Directory.Exists(tempDir))
                        {
                            try { Directory.Delete(tempDir, true); }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"恢复失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RestoreConfigFiles(string sourceDir)
        {
            string configDir = Path.Combine(sourceDir, "Config");
            if (Directory.Exists(configDir))
            {
                string destConfigDir = Path.Combine(Application.StartupPath, "Config");
                if (Directory.Exists(destConfigDir))
                {
                    // 备份原配置目录
                    string backupConfigDir = Path.Combine(Application.StartupPath, "恢复前备份", "Config");
                    if (Directory.Exists(destConfigDir))
                    {
                        CopyDirectory(destConfigDir, backupConfigDir);
                    }
                }

                // 复制新配置
                CopyDirectory(configDir, destConfigDir);
            }

            // 恢复单独的配置文件
            string[] configFiles =
            {
                "appsettings.json",
                "config.xml",
                "settings.ini",
                "connectionstrings.config"
            };

            foreach (string configFile in configFiles)
            {
                string sourceFile = Path.Combine(sourceDir, configFile);
                if (File.Exists(sourceFile))
                {
                    string destFile = Path.Combine(Application.StartupPath, configFile);

                    // 备份原文件
                    if (File.Exists(destFile))
                    {
                        string backupDir = Path.Combine(Application.StartupPath, "恢复前备份");
                        Directory.CreateDirectory(backupDir);
                        string backupFile = Path.Combine(backupDir, configFile);
                        File.Copy(destFile, backupFile, true);
                    }

                    // 恢复文件
                    File.Copy(sourceFile, destFile, true);
                }
            }
        }

        // 简单备份
        private bool SimpleBackup()
        {
            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "数据库文件 (*.db)|*.db|所有文件 (*.*)|*.*";
            saveDialog.FileName = $"冷库宝备份_{DateTime.Now:yyyyMMdd_HHmmss}.db";
            saveDialog.Title = "选择备份位置";

            if (saveDialog.ShowDialog() != DialogResult.OK)
                return false;

            string dbFile = FindDatabaseFile();

            if (dbFile == null)
            {
                MessageBox.Show("找不到数据库文件！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            File.Copy(dbFile, saveDialog.FileName, true);
            MessageBox.Show($"✅ 备份成功！\n\n文件: {saveDialog.FileName}", "备份完成");
            return true;
        }

        // 完整备份
        private bool FullBackup()
        {
            try
            {
                string backupDir = Path.Combine(Application.StartupPath, "数据备份");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupName = $"冷库宝完整备份_{timestamp}";
                string zipFile = Path.Combine(backupDir, $"{backupName}.zip");

                string tempDir = Path.Combine(Path.GetTempPath(), $"backup_temp_{timestamp}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    string dbFile = FindDatabaseFile();
                    if (dbFile != null && File.Exists(dbFile))
                    {
                        File.Copy(dbFile, Path.Combine(tempDir, Path.GetFileName(dbFile)), true);
                    }

                    BackupConfigFiles(tempDir);
                    BackupLogFiles(tempDir);
                    CreateBackupInfoFile(tempDir);
                    ZipFile.CreateFromDirectory(tempDir, zipFile);
                    CleanOldBackups(backupDir, 30);

                    MessageBox.Show($"✅ 完整备份成功！\n\n文件: {zipFile}\n大小: {FormatFileSize(new FileInfo(zipFile).Length)}",
                                  "备份完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return true;
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"完整备份失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 自动备份（写入文档目录，避免安装目录被清理残留删掉）
        private bool AutoBackup()
        {
            try
            {
                string dbFile = FindDatabaseFile();
                if (dbFile == null || !File.Exists(dbFile))
                {
                    MessageBox.Show("找不到数据库文件！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                int year = FiscalYearService.IsInitialized
                    ? FiscalYearService.ActiveYear
                    : DateTime.Now.Year;

                if (!Directory.Exists(SafeDataPaths.SafeAutoBackupDirectory))
                    Directory.CreateDirectory(SafeDataPaths.SafeAutoBackupDirectory);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFile = Path.Combine(
                    SafeDataPaths.SafeAutoBackupDirectory,
                    $"手动自动备份_{year}_{timestamp}.db");

                FiscalYearService.CheckpointDatabase(dbFile);
                File.Copy(dbFile, backupFile, true);
                SafeDataPaths.TrimOldAutoBackups();

                MessageBox.Show(
                    $"✅ 自动备份成功！\n\n文件: {backupFile}\n\n" +
                    "已保存到「文档\\冷库宝自动备份」。\n请勿用清理软件删除该文件夹。",
                    "备份完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"自动备份失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>启动后静默做一次当日自动备份（不弹窗）。</summary>
        private void TrySilentDailyAutoBackup()
        {
            try
            {
                if (!FiscalYearService.IsInitialized)
                    return;

                string dbPath = FiscalYearService.GetActiveDbPath();
                SafeDataPaths.TryCreateDailyAutoBackup(dbPath, FiscalYearService.ActiveYear);
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 静默每日备份失败: {ex.Message}");
            }
        }

        private void BtnManualImport_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Filter = "数据文件 (*.csv;*.txt)|*.csv;*.txt|Excel文件 (*.xlsx;*.xls)|*.xlsx;*.xls";
            openFile.Title = "选择要导入的数据文件";

            if (openFile.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string filePath = openFile.FileName;
                    string extension = Path.GetExtension(filePath).ToLower();

                    if (extension == ".csv" || extension == ".txt")
                    {
                        ImportCSVFile(filePath);
                    }
                    else if (extension == ".xlsx" || extension == ".xls")
                    {
                        MessageBox.Show("Excel导入功能开发中...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 查找数据库文件
        private string FindDatabaseFile()
        {
            if (FiscalYearService.IsInitialized)
            {
                string activePath = FiscalYearService.GetActiveDbPath();
                if (File.Exists(activePath))
                    return activePath;
            }

            string[] possibleDbPaths =
            {
                Path.Combine(SafeDataPaths.DataDirectory, $"lengkubao_{DateTime.Now.Year}.db"),
                Path.Combine(SafeDataPaths.DataDirectory, "lengkubao.db"),
                Path.Combine(Application.StartupPath, "data", "lengkubao.db"),
                Path.Combine(Application.StartupPath, "lengkubao.db"),
                "lengkubao.db"
            };

            foreach (string path in possibleDbPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return FiscalYearService.IsInitialized ? FiscalYearService.GetActiveDbPath() : null;
        }

        // 备份配置文件
        private void BackupConfigFiles(string tempDir)
        {
            string[] configFiles =
            {
                "appsettings.json",
                "config.xml",
                "settings.ini",
                "connectionstrings.config"
            };

            foreach (string configFile in configFiles)
            {
                string configPath = Path.Combine(Application.StartupPath, configFile);
                if (File.Exists(configPath))
                {
                    File.Copy(configPath, Path.Combine(tempDir, configFile), true);
                }
            }

            string configDir = Path.Combine(Application.StartupPath, "Config");
            if (Directory.Exists(configDir))
            {
                string destConfigDir = Path.Combine(tempDir, "Config");
                CopyDirectory(configDir, destConfigDir);
            }
        }

        // 备份日志文件
        private void BackupLogFiles(string tempDir)
        {
            string logDir = Path.Combine(Application.StartupPath, "Logs");
            if (Directory.Exists(logDir))
            {
                string destLogDir = Path.Combine(tempDir, "Logs");
                CopyDirectory(logDir, destLogDir);
            }
        }

        // 创建备份信息文件
        private void CreateBackupInfoFile(string tempDir)
        {
            string infoFile = Path.Combine(tempDir, "备份信息.txt");

            string info = $"=== 冷库宝数据备份信息 ===\n\n" +
                         $"备份时间: {DateTime.Now:yyyy年MM月dd日 HH:mm:ss}\n" +
                         $"应用程序: {Application.ProductName}\n" +
                         $"版本: {Application.ProductVersion}\n" +
                         $"用户: {Environment.UserName}\n" +
                         $"计算机: {Environment.MachineName}\n\n" +
                         $"=== 系统信息 ===\n" +
                         $"操作系统: {Environment.OSVersion}\n" +
                         $".NET版本: {Environment.Version}\n\n" +
                         $"=== 备注 ===\n" +
                         $"此备份由系统自动生成，请妥善保管。";

            File.WriteAllText(infoFile, info, Encoding.UTF8);
        }

        // 复制目录
        private void CopyDirectory(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        // 清理旧备份
        private void CleanOldBackups(string backupDir, int keepDays)
        {
            try
            {
                var backupFiles = Directory.GetFiles(backupDir, "*.zip");
                DateTime cutoffDate = DateTime.Now.AddDays(-keepDays);

                foreach (var file in backupFiles)
                {
                    FileInfo info = new FileInfo(file);
                    if (info.CreationTime < cutoffDate)
                    {
                        info.Delete();
                    }
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        // 清理旧的自动备份
        private void CleanOldAutoBackups(string autoBackupDir, int keepDays)
        {
            try
            {
                var backupFiles = Directory.GetFiles(autoBackupDir, "自动备份_*.db");
                DateTime cutoffDate = DateTime.Now.AddDays(-keepDays);

                foreach (var file in backupFiles)
                {
                    FileInfo info = new FileInfo(file);
                    if (info.CreationTime < cutoffDate)
                    {
                        info.Delete();
                    }
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        // 格式化文件大小
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        private void BtnSimpleExport_Click(object sender, EventArgs e)
        {
            try
            {
                ShowModuleInHost(new StatisticsForm(), -1, "多维库存统计");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出报表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnStatistics_Click(object sender, EventArgs e)
        {
            try
            {
                ShowModuleInHost(new StatisticsForm(), -1, "多维库存统计");
            }
            catch (Exception ex)
            {
                // #region agent log
                AgentDebugLog.Write("E", "Form1.OpenStatistics", "fail",
                    "{\"type\":\"" + ex.GetType().Name + "\",\"msg\":\"" + AgentDebugLog.Escape(ex.Message) + "\"}");
                // #endregion
                MessageBox.Show($"打开统计失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ==================== 辅助方法 ====================

        private void ImportCSVFile(string filePath)
        {
            try
            {
                using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    int lineCount = 0;
                    int successCount = 0;

                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        lineCount++;

                        if (lineCount == 1) continue;

                        string[] fields = line.Split(',');
                        if (fields.Length >= 3)
                        {
                            DatabaseManager db = new DatabaseManager();
                            string sql = @"INSERT OR REPLACE INTO clients (code, name, phone, created_time) 
                                         VALUES (@code, @name, @phone, datetime('now'))";

                            db.ExecuteQuery(sql, new Dictionary<string, object>
                            {
                                { "@code", fields[0] },
                                { "@name", fields[1] },
                                { "@phone", fields.Length > 2 ? fields[2] : "" }
                            });

                            successCount++;
                        }
                    }

                    MessageBox.Show($"导入完成！\n成功: {successCount}/{lineCount - 1} 条", "导入结果",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnTestBalance_Click(object sender, EventArgs e)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                DataTable clients = db.GetAllClients();

                if (clients.Rows.Count > 0)
                {
                    MessageBox.Show($"测试对账功能\n发现 {clients.Rows.Count} 个客户", "测试对账");
                }
                else
                {
                    MessageBox.Show("没有客户数据，请先添加客户", "测试对账");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"对账测试失败: {ex.Message}", "错误");
            }
        }

        private void btnDiagnostic_Click(object sender, EventArgs e)
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                StringBuilder result = new StringBuilder();
                result.AppendLine("=== 系统诊断报告 ===");
                result.AppendLine($"诊断时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                result.AppendLine();

                try
                {
                    var clients = db.GetAllClients();
                    var products = db.GetAllProductTypes();
                    var locations = db.GetAllLocations();
                    var handlers = db.GetAllHandlers();

                    result.AppendLine("✅ 数据库连接正常");
                    result.AppendLine($"   客户数: {clients.Rows.Count}");
                    result.AppendLine($"   商品型号: {products.Rows.Count}");
                    result.AppendLine($"   库位数: {locations.Rows.Count}");
                    result.AppendLine($"   经手人数: {handlers.Rows.Count}");
                }
                catch (Exception ex)
                {
                    result.AppendLine($"❌ 数据库连接失败: {ex.Message}");
                }

                result.AppendLine();

                result.AppendLine("=== 文件检查 ===");
                string[] checkFiles = {
                    Path.Combine(Application.StartupPath, "data", "lengkubao.db"),
                    Path.Combine(Application.StartupPath, "lengkubao.db")
                };

                bool foundDb = false;
                foreach (string file in checkFiles)
                {
                    if (File.Exists(file))
                    {
                        result.AppendLine($"✅ 数据库文件: {file}");
                        foundDb = true;
                        break;
                    }
                }

                if (!foundDb)
                {
                    result.AppendLine("❌ 未找到数据库文件");
                }

                MessageBox.Show(result.ToString(), "系统诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"诊断失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TestDatabase()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();

                string sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                DataTable tables = db.ExecuteQuery(sql);

                string message = "数据库中的表：\n\n";
                foreach (DataRow row in tables.Rows)
                {
                    string tableName = row["name"].ToString();
                    message += tableName + "\n";

                    try
                    {
                        string sampleSql = $"SELECT * FROM {tableName} LIMIT 2";
                        DataTable sample = db.ExecuteQuery(sampleSql);
                        message += $"  记录数：{sample.Rows.Count}\n";

                        if (sample.Rows.Count > 0)
                        {
                            message += "  字段：";
                            foreach (DataColumn col in sample.Columns)
                            {
                                message += col.ColumnName + ", ";
                            }
                            message += "\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        message += $"  错误：{ex.Message}\n";
                    }

                    message += "\n";
                }

                MessageBox.Show(message, "数据库结构", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"测试失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            
            StartSingleInstanceListener();
            BeginInvoke(new Action(ApplyResponsiveLayout));
        }

        internal void RestoreAndActivate()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RestoreAndActivate));
                return;
            }

            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            SetForegroundWindow(Handle);

            if (activeWorkspaceHost != null && !activeWorkspaceHost.IsDisposed)
            {
                activeWorkspaceHost.Show();
                activeWorkspaceHost.WindowState = FormWindowState.Normal;
                activeWorkspaceHost.Activate();
                SetForegroundWindow(activeWorkspaceHost.Handle);
            }
        }

        private void StartSingleInstanceListener()
        {
            showWindowEvent = SingleInstanceHelper.GetOwnerShowWindowEvent()
                ?? SingleInstanceHelper.CreateShowWindowEvent();
            stopInstanceListener = false;
            instanceListenerThread = new System.Threading.Thread(() =>
            {
                while (!stopInstanceListener)
                {
                    if (!showWindowEvent.WaitOne(500))
                        continue;

                    if (stopInstanceListener)
                        break;

                    try
                    {
                        RestoreAndActivate();
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "SingleInstanceListener"
            };
            instanceListenerThread.Start();
        }

        private void StopSingleInstanceListener()
        {
            stopInstanceListener = true;
            try { showWindowEvent?.Set(); } catch { }
            try { showWindowEvent?.Dispose(); } catch { }
            showWindowEvent = null;
        }

        // 在窗体关闭时停止服务
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopSingleInstanceListener();

            if (!skipBackupReminderOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult backupChoice = MessageBox.Show(
                    this,
                    "关闭程序前，建议先备份数据，以防意外丢失。\n\n" +
                    "【是】立即备份并退出\n" +
                    "【否】仍要退出\n" +
                    "【取消】返回继续使用",
                    "备份提醒",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button3);

                if (backupChoice == DialogResult.Yes)
                {
                    e.Cancel = true;
                    if (TryRunDataBackup())
                    {
                        skipBackupReminderOnClose = true;
                        Close();
                    }
                    return;
                }

                if (backupChoice == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    base.OnFormClosing(e);
                    return;
                }
            }

            if (activeWorkspaceHost != null && !activeWorkspaceHost.IsDisposed)
            {
                activeWorkspaceHost.FormClosed -= WorkspaceHost_FormClosed;
                try
                {
                    activeWorkspaceHost.Close();
                }
                catch { }
                activeWorkspaceHost = null;
            }

            if (isSyncServerRunning && syncServer != null)
            {
                try
                {
                    syncServer.Stop();
                }
                catch { }
            }

            if (refreshTimer != null)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
            }

            base.OnFormClosing(e);
        }
    }
}