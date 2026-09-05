using lengkubao.desktop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class DashboardForm : Form
    {
        public DashboardForm()
        {
            InitializeComponent();
            LoadDashboardData();
        }


        private void LoadDashboardData()
        {
            try
            {
                DatabaseManager dbManager = new DatabaseManager();

                // 获取今日所有规格的总入库量
                int totalQuantity = 0;
                var specs = new[] { "42型", "48型", "60型", "45型", "框型", "次型", "精品型" }; // 您的规格列表

                foreach (string spec in specs)
                {
                    totalQuantity += dbManager.GetTodayTotalQuantity(spec);
                }

                labelTodayCount.Text = $"今日入库：{totalQuantity} 箱";
            }
            catch (Exception)
            {
                labelTodayCount.Text = "今日入库：数据加载失败";
            }
        }

        private void DashboardForm_Load(object sender, EventArgs e)
        {

        }
    }
}
