using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public static class PackagingQueryHelper
    {
        public static void ShowPackagingQuery()
        {
            Application.Run(new PackagingQueryForm());
        }
    }

    public class PackagingQueryForm : Form
    {
        private DataGridView dgvPackaging;
        private Label lblCount;
        private Label lblStatus;
        private Button btnRefresh;
        private Button btnClose;
        private Button btnSearch;
        private Button btnReset;
        private TextBox txtSearchOrderNo;
        private TextBox txtSearchClient;
        private ComboBox cmbPackType;
        private DateTimePicker dtpFromDate;
        private DateTimePicker dtpToDate;
        private Panel panelHeader;
        private Panel panelQuery;
        private Panel panelGrid;
        private Panel panelStatus;
        private Panel panelButtons;

        private DataTable dataSource;
        private const string PlaceholderOrderNo = "输入单据号...";
        private const string PlaceholderClient = "输入客户名称...";

        public PackagingQueryForm()
        {
            InitializeComponents();
            LoadDefaultData();
        }

        private void InitializeComponents()
        {
            // 窗体设置
            this.Text = "包装记录查询";
            this.Size = new Size(1100, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(248, 248, 248);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 主面板 - 使用TableLayoutPanel进行精确布局
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 4;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));     // 标题栏
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));    // 查询条件
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));     // 数据网格
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));     // 状态栏
            mainLayout.Padding = new Padding(5);
            this.Controls.Add(mainLayout);

            // 1. 标题栏
            panelHeader = CreateHeaderPanel();
            mainLayout.Controls.Add(panelHeader, 0, 0);

            // 2. 查询条件面板
            panelQuery = CreateQueryPanel();
            mainLayout.Controls.Add(panelQuery, 0, 1);

            // 3. 数据网格面板
            panelGrid = CreateGridPanel();
            mainLayout.Controls.Add(panelGrid, 0, 2);

            // 4. 状态栏
            panelStatus = CreateStatusPanel();
            mainLayout.Controls.Add(panelStatus, 0, 3);
        }

        private Panel CreateHeaderPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.White;

            Label lblTitle = new Label();
            lblTitle.Text = "包装记录查询";
            lblTitle.Font = new Font("微软雅黑", 16, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(52, 73, 94);
            lblTitle.Location = new Point(10, 10);
            lblTitle.Size = new Size(200, 30);
            panel.Controls.Add(lblTitle);

            return panel;
        }

        private Panel CreateQueryPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Padding = new Padding(10);

            int yPos = 15;
            int labelWidth = 80;
            int controlWidth = 180;
            int spacing = 20;

            // 单据号查询
            AddLabel("单据号:", 20, yPos, labelWidth, panel);
            txtSearchOrderNo = AddTextBox(20 + labelWidth, yPos, controlWidth, panel);
            SetPlaceholderText(txtSearchOrderNo, PlaceholderOrderNo);

            // 客户查询
            AddLabel("客户:", 20 + labelWidth + controlWidth + spacing, yPos, labelWidth, panel);
            txtSearchClient = AddTextBox(20 + labelWidth + controlWidth + spacing + labelWidth, yPos, controlWidth, panel);
            SetPlaceholderText(txtSearchClient, PlaceholderClient);

            // 包装类型
            AddLabel("包装类型:", 20 + (labelWidth + controlWidth + spacing) * 2, yPos, labelWidth, panel);
            cmbPackType = new ComboBox();
            cmbPackType.Location = new Point(20 + (labelWidth + controlWidth + spacing) * 2 + labelWidth, yPos);
            cmbPackType.Size = new Size(controlWidth, 25);
            cmbPackType.Font = new Font("微软雅黑", 9);
            cmbPackType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbPackType.Items.AddRange(new object[] { "全部", "纸箱", "木箱", "托盘", "其他" });
            cmbPackType.SelectedIndex = 0;
            panel.Controls.Add(cmbPackType);

            yPos += 35;

            // 日期范围
            AddLabel("开始日期:", 20, yPos, labelWidth, panel);
            dtpFromDate = new DateTimePicker();
            dtpFromDate.Location = new Point(20 + labelWidth, yPos);
            dtpFromDate.Size = new Size(controlWidth, 25);
            dtpFromDate.Font = new Font("微软雅黑", 9);
            dtpFromDate.Format = DateTimePickerFormat.Short;
            dtpFromDate.Value = DateTime.Now.AddMonths(-1);
            panel.Controls.Add(dtpFromDate);

            AddLabel("结束日期:", 20 + labelWidth + controlWidth + spacing, yPos, labelWidth, panel);
            dtpToDate = new DateTimePicker();
            dtpToDate.Location = new Point(20 + labelWidth + controlWidth + spacing + labelWidth, yPos);
            dtpToDate.Size = new Size(controlWidth, 25);
            dtpToDate.Font = new Font("微软雅黑", 9);
            dtpToDate.Format = DateTimePickerFormat.Short;
            dtpToDate.Value = DateTime.Now;
            panel.Controls.Add(dtpToDate);

            // 按钮
            panelButtons = new Panel();
            panelButtons.Location = new Point(800, 15);
            panelButtons.Size = new Size(250, 60);
            panel.Controls.Add(panelButtons);

            btnSearch = CreateButton("查询", Color.FromArgb(52, 152, 219), 0, 0);
            btnSearch.Click += BtnSearch_Click;
            panelButtons.Controls.Add(btnSearch);

            btnReset = CreateButton("重置", Color.FromArgb(149, 165, 166), 85, 0);
            btnReset.Click += BtnReset_Click;
            panelButtons.Controls.Add(btnReset);

            btnRefresh = CreateButton("刷新", Color.FromArgb(46, 204, 113), 170, 0);
            btnRefresh.Click += BtnRefresh_Click;
            panelButtons.Controls.Add(btnRefresh);

            return panel;
        }

        private Panel CreateGridPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.White;
            panel.Padding = new Padding(5);

            // 创建DataGridView
            dgvPackaging = new DataGridView();
            dgvPackaging.Dock = DockStyle.Fill;
            dgvPackaging.ReadOnly = true;
            dgvPackaging.RowHeadersVisible = false;
            dgvPackaging.AllowUserToAddRows = false;
            dgvPackaging.AllowUserToDeleteRows = false;
            dgvPackaging.AllowUserToOrderColumns = false;
            dgvPackaging.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvPackaging.MultiSelect = false;
            dgvPackaging.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvPackaging.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            dgvPackaging.ColumnHeadersHeight = 40;
            dgvPackaging.RowTemplate.Height = 35;

            // 表头样式
            dgvPackaging.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                Padding = new Padding(5)
            };

            // 单元格样式
            dgvPackaging.DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("微软雅黑", 9),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(5)
            };

            // 交替行颜色
            dgvPackaging.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(245, 249, 252)
            };

            // 选择行样式
            dgvPackaging.RowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 152, 219);
            dgvPackaging.RowsDefaultCellStyle.SelectionForeColor = Color.White;

            // 网格线
            dgvPackaging.GridColor = Color.FromArgb(224, 224, 224);
            dgvPackaging.BorderStyle = BorderStyle.FixedSingle;

            // 双击事件
            dgvPackaging.CellDoubleClick += DgvPackaging_CellDoubleClick;

            panel.Controls.Add(dgvPackaging);
            return panel;
        }

        private Panel CreateStatusPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(52, 73, 94);

            // 记录数标签
            lblCount = new Label();
            lblCount.Text = "共 0 条记录";
            lblCount.Font = new Font("微软雅黑", 9);
            lblCount.ForeColor = Color.White;
            lblCount.Location = new Point(10, 10);
            lblCount.Size = new Size(200, 20);
            panel.Controls.Add(lblCount);

            // 状态标签
            lblStatus = new Label();
            lblStatus.Text = "就绪";
            lblStatus.Font = new Font("微软雅黑", 9);
            lblStatus.ForeColor = Color.LightGreen;
            lblStatus.Location = new Point(220, 10);
            lblStatus.Size = new Size(200, 20);
            panel.Controls.Add(lblStatus);

            // 关闭按钮
            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Font = new Font("微软雅黑", 9);
            btnClose.BackColor = Color.FromArgb(231, 76, 60);
            btnClose.ForeColor = Color.White;
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Size = new Size(80, 30);
            btnClose.Location = new Point(1000, 5);
            btnClose.Click += (s, e) => this.Close();
            panel.Controls.Add(btnClose);

            return panel;
        }

        private void SetPlaceholderText(TextBox textBox, string placeholder)
        {
            textBox.Text = placeholder;
            textBox.ForeColor = Color.Gray;

            textBox.GotFocus += (s, e) =>
            {
                if (textBox.Text == placeholder)
                {
                    textBox.Text = "";
                    textBox.ForeColor = Color.Black;
                }
            };

            textBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = placeholder;
                    textBox.ForeColor = Color.Gray;
                }
            };
        }

        private void LoadDefaultData()
        {
            BtnSearch_Click(null, null);
        }

        private void BtnSearch_Click(object sender, EventArgs e)
        {
            try
            {
                lblStatus.Text = "正在查询数据...";
                lblStatus.ForeColor = Color.Yellow;
                Application.DoEvents();

                // 构建查询条件
                string whereClause = "WHERE 1=1";
                var parameters = new System.Collections.Generic.Dictionary<string, object>();

                // 单据号查询（需要处理占位符）
                string orderNoText = txtSearchOrderNo.Text;
                if (!string.IsNullOrWhiteSpace(orderNoText) && orderNoText != PlaceholderOrderNo)
                {
                    whereClause += " AND order_no LIKE @order_no";
                    parameters.Add("@order_no", "%" + orderNoText.Trim() + "%");
                }

                // 客户查询（需要处理占位符）
                string clientText = txtSearchClient.Text;
                if (!string.IsNullOrWhiteSpace(clientText) && clientText != PlaceholderClient)
                {
                    whereClause += " AND client_name LIKE @client_name";
                    parameters.Add("@client_name", "%" + clientText.Trim() + "%");
                }

                // 包装类型
                if (cmbPackType.SelectedIndex > 0)
                {
                    whereClause += " AND pack_type = @pack_type";
                    parameters.Add("@pack_type", cmbPackType.SelectedItem.ToString());
                }

                // 日期范围
                whereClause += " AND created_time >= @from_date AND created_time <= @to_date";
                parameters.Add("@from_date", dtpFromDate.Value.Date);
                parameters.Add("@to_date", dtpToDate.Value.Date.AddDays(1).AddSeconds(-1));

                // 执行查询
                dataSource = LoadPackagingData(whereClause, parameters);
                BindDataToGrid(dataSource);

                lblStatus.Text = "查询完成";
                lblStatus.ForeColor = Color.LightGreen;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "查询失败";
                lblStatus.ForeColor = Color.Red;
                MessageBox.Show($"查询失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            // 重置文本框到占位符状态
            txtSearchOrderNo.Text = PlaceholderOrderNo;
            txtSearchOrderNo.ForeColor = Color.Gray;

            txtSearchClient.Text = PlaceholderClient;
            txtSearchClient.ForeColor = Color.Gray;

            cmbPackType.SelectedIndex = 0;
            dtpFromDate.Value = DateTime.Now.AddMonths(-1);
            dtpToDate.Value = DateTime.Now;

            BtnSearch_Click(sender, e);
        }

        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            BtnSearch_Click(sender, e);
        }

        private DataTable LoadPackagingData(string whereClause, System.Collections.Generic.Dictionary<string, object> parameters)
        {
            DataTable table = new DataTable();

            string dbPath = System.IO.Path.Combine(Application.StartupPath, "lengkubao.db");
            string connStr = $"Data Source={dbPath};Version=3;";

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                string sql = $@"SELECT 
                    order_no as '包装单号',
                    client_name as '客户名称',
                    pack_type as '包装类型',
                    quantity as '包装数量',
                    printf('%.2f', unit_price) as '包装单价',
                    printf('%.2f', total_amount) as '金额合计',
                    datetime(created_time) as '创建时间'
                FROM packaging_transactions 
                {whereClause}
                ORDER BY created_time DESC";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    // 添加参数
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.AddWithValue(param.Key, param.Value);
                    }

                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(table);
                    }
                }
            }

            return table;
        }

        private void BindDataToGrid(DataTable data)
        {
            // 先清空绑定
            dgvPackaging.DataSource = null;
            Application.DoEvents();

            // 绑定新数据
            dgvPackaging.DataSource = data;

            // 更新记录数
            lblCount.Text = $"共 {data.Rows.Count} 条记录";

            // 调整列宽和格式
            if (dgvPackaging.Columns.Count > 0)
            {
                // 设置列的对齐方式
                foreach (DataGridViewColumn column in dgvPackaging.Columns)
                {
                    if (column.HeaderText.Contains("数量") ||
                        column.HeaderText.Contains("单价") ||
                        column.HeaderText.Contains("金额"))
                    {
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    }
                    else if (column.HeaderText.Contains("时间"))
                    {
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        column.Width = 150;
                    }
                    else
                    {
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                    }
                }

                // 确保表头显示完整
                dgvPackaging.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            }
        }

        private void DgvPackaging_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < dgvPackaging.RowCount)
            {
                string orderNo = dgvPackaging.Rows[e.RowIndex].Cells["包装单号"].Value?.ToString();
                if (!string.IsNullOrEmpty(orderNo))
                {
                    MessageBox.Show($"您双击了单据号：{orderNo}\n可以在这里实现查看详情功能",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // 辅助方法
        private Label AddLabel(string text, int x, int y, int width, Panel parent)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y + 5);
            label.Size = new Size(width, 20);
            label.TextAlign = ContentAlignment.MiddleRight;
            label.Font = new Font("微软雅黑", 10);
            parent.Controls.Add(label);
            return label;
        }

        private TextBox AddTextBox(int x, int y, int width, Panel parent)
        {
            TextBox textBox = new TextBox();
            textBox.Location = new Point(x, y);
            textBox.Size = new Size(width, 25);
            textBox.Font = new Font("微软雅黑", 9);
            textBox.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(textBox);
            return textBox;
        }

        private Button CreateButton(string text, Color backColor, int x, int y)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(80, 35);
            button.Font = new Font("微软雅黑", 10);
            button.BackColor = backColor;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            return button;
        }
    }
}