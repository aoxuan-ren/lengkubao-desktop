// DatabaseViewerForm.cs
using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class DatabaseViewerForm : Form
    {
        private DataGridView dataGridView;
        private ComboBox cmbTables;
        private Button btnRefresh;

        public DatabaseViewerForm()
        {
            InitializeComponents();
            LoadTableNames();
        }

        private void InitializeComponents()
        {
            this.Text = "数据库查看器";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 表选择框
            cmbTables = new ComboBox();
            cmbTables.Location = new Point(20, 20);
            cmbTables.Size = new Size(200, 25);
            cmbTables.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTables.SelectedIndexChanged += CmbTables_SelectedIndexChanged;
            this.Controls.Add(cmbTables);

            // 刷新按钮
            btnRefresh = new Button();
            btnRefresh.Text = "刷新";
            btnRefresh.Location = new Point(230, 20);
            btnRefresh.Size = new Size(80, 25);
            btnRefresh.Click += (s, e) => LoadTableData();
            this.Controls.Add(btnRefresh);

            // 数据表格
            dataGridView = new DataGridView();
            dataGridView.Location = new Point(20, 60);
            dataGridView.Size = new Size(740, 500);
            dataGridView.Dock = DockStyle.Bottom;
            dataGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(dataGridView);
        }

        private void LoadTableNames()
        {
            try
            {
                DatabaseManager db = new DatabaseManager();
                string sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                DataTable tables = db.ExecuteQuery(sql, null);

                cmbTables.Items.Clear();
                foreach (DataRow row in tables.Rows)
                {
                    cmbTables.Items.Add(row["name"].ToString());
                }

                if (cmbTables.Items.Count > 0)
                    cmbTables.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载表名失败: {ex.Message}", "错误");
            }
        }

        private void CmbTables_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadTableData();
        }

        private void LoadTableData()
        {
            if (cmbTables.SelectedItem == null) return;

            try
            {
                string tableName = cmbTables.SelectedItem.ToString();
                DatabaseManager db = new DatabaseManager();

                // 先查看表结构
                string schemaSql = $"PRAGMA table_info({tableName})";
                DataTable schema = db.ExecuteQuery(schemaSql, null);

                // 查看数据
                string dataSql = $"SELECT * FROM {tableName} LIMIT 100";
                DataTable data = db.ExecuteQuery(dataSql, null);

                // 显示信息
                string info = $"表名: {tableName}\n";
                info += $"字段数: {schema.Rows.Count}\n";
                info += $"记录数: {data.Rows.Count}\n\n";
                info += "字段列表:\n";

                foreach (DataRow row in schema.Rows)
                {
                    info += $"{row["name"]} ({row["type"]}) - ";
                    info += $"允许NULL: {(Convert.ToInt32(row["notnull"]) == 0 ? "是" : "否")}\n";
                }

                MessageBox.Show(info, "表信息");

                // 显示数据
                dataGridView.DataSource = data;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载表数据失败: {ex.Message}\n表可能不存在或结构有问题。", "错误");
            }
        }
    }
}