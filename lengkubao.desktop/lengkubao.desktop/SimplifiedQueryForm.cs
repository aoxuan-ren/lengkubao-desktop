// SimplifiedQueryForm.cs - 完全替换为以下代码
using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public class SimplifiedQueryForm : Form
    {
        private DataGridView dataGridView;
        private Button btnClose;
        private Label lblTitle;
        private string queryType;

        public SimplifiedQueryForm(string type)
        {
            queryType = type;
            InitializeForm();
            LoadData();
        }

        private void InitializeForm()
        {
            // 使用传统的if-else，不用switch表达式
            string title = "数据查询";
            if (queryType == "inbound")
                title = "入库记录查询";
            else if (queryType == "sales")
                title = "销售记录查询";
            else if (queryType == "packaging")
                title = "包装记录查询";

            this.Text = title;
            this.Size = new Size(900, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;

            // 标题
            lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Location = new Point(20, 10);
            lblTitle.Size = new Size(300, 30);
            lblTitle.Font = new Font("微软雅黑", 14, FontStyle.Bold);
            this.Controls.Add(lblTitle);

            // 数据表格
            dataGridView = new DataGridView();
            dataGridView.Location = new Point(20, 50);
            dataGridView.Size = new Size(840, 380);
            dataGridView.BackgroundColor = Color.White;
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView.ReadOnly = true;
            dataGridView.AllowUserToAddRows = false;
            dataGridView.Font = new Font("微软雅黑", 9);
            this.Controls.Add(dataGridView);

            // 关闭按钮
            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(760, 440);
            btnClose.Size = new Size(100, 30);
            btnClose.BackColor = Color.FromArgb(0, 122, 204);
            btnClose.ForeColor = Color.White;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private void LoadData()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                DatabaseManager db = new DatabaseManager();
                DataTable data = null;

                // 使用传统的if-else
                if (queryType == "inbound")
                {
                    data = db.GetInboundRecordsSimple();
                }
                else if (queryType == "sales")
                {
                    data = db.GetSalesRecordsSimple();
                }
                else if (queryType == "packaging")
                {
                    data = db.GetPackagingRecordsSimple();
                }

                if (data != null)
                {
                    dataGridView.DataSource = data;
                    FormatColumns();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载数据失败: {ex.Message}", "错误");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void FormatColumns()
        {
            // 格式化列标题
            foreach (DataGridViewColumn column in dataGridView.Columns)
            {
                string header = column.HeaderText;

                // 使用传统的if-else
                if (header == "order_no") header = "单号";
                else if (header == "client_name") header = "客户名称";
                else if (header == "client_code") header = "客户编号";
                else if (header == "location") header = "库位";
                else if (header == "date") header = "日期";
                else if (header == "spec") header = "规格";
                else if (header == "quantity") header = "数量";
                else if (header == "unit_price") header = "单价";
                else if (header == "total_amount") header = "总金额";
                else if (header == "handler") header = "经手人";
                else if (header == "pack_type") header = "包装类型";
                else if (header == "created_time") header = "创建时间";

                column.HeaderText = header;

                // 格式化数字列
                if (column.Name == "quantity")
                {
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                else if (column.Name == "unit_price" || column.Name == "total_amount")
                {
                    column.DefaultCellStyle.Format = "N2";
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            }
        }
    }
}