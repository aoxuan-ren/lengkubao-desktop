using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    public partial class SystemSettingsForm : Form
    {
        private DatabaseManager dbManager;
        private TabControl mainTabControl;
        private TabPage tabCustomers;
        private TabPage tabProducts;
        private TabPage tabPackTypes;
        private TabPage tabLocations;
        private TabPage tabHandlers;
        private TabPage tabBuyers;
        private DataGridView gridCustomers;
        private DataGridView gridProducts;
        private DataGridView gridPackTypes;
        private ListBox listLocations;
        private DataGridView gridHandlers;
        private DataGridView gridBuyers;
        private TabControl tabControl;
        private TextBox txtRestoreFile;
        private DataGridView gridLocations;  // 用于库位管理的表格
        private TextBox txtSearchCustomerField;
        private List<CustomerRowEntry> _customerCache = new List<CustomerRowEntry>();

        private sealed class CustomerRowEntry
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Phone { get; set; }
            public string Address { get; set; }
            public string Initials { get; set; }
        }

        public SystemSettingsForm()
        {
            Console.WriteLine(">>> [构造函数] SystemSettingsForm 开始");
            try
            {
                InitializeComponent();
                Console.WriteLine(">>> [构造函数] InitializeComponent 完成");

                dbManager = new DatabaseManager();
                Console.WriteLine(">>> [构造函数] DatabaseManager 创建完成");

                bool dbConnected = dbManager.TestConnection();
                Console.WriteLine($">>> 数据库连接测试: {dbConnected}");

                DataTable clientsCheck = dbManager.ExecuteQuery("SELECT COUNT(*) as cnt FROM clients");
                if (clientsCheck.Rows.Count > 0)
                {
                    Console.WriteLine($">>> 客户表记录数: {clientsCheck.Rows[0]["cnt"]}");
                }

                CreateMainControls();
                Console.WriteLine(">>> [构造函数] CreateMainControls 完成");

                DatabaseManager.ConfigDataChanged += OnConfigDataChanged;
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [构造函数] 异常: {ex.ToString()}");
            }
            Console.WriteLine(">>> [构造函数] SystemSettingsForm 结束");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DatabaseManager.ConfigDataChanged -= OnConfigDataChanged;
            base.OnFormClosed(e);
        }

        private void OnConfigDataChanged(object sender, LocalConfigDeltaEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            void apply() => ApplyConfigDataRefresh(e?.EntityType);
            if (InvokeRequired)
                BeginInvoke(new Action(apply));
            else
                apply();
        }

        /// <summary>根据同步/本地变更类型刷新对应配置页。</summary>
        internal void ApplyConfigDataRefresh(string entityType)
        {
            if (IsDisposed)
                return;

            try
            {
                string type = (entityType ?? "").Trim().ToUpperInvariant();
                switch (type)
                {
                    case "CUSTOMER":
                        ReloadAndFilterCustomers();
                        RefreshBuyerData();
                        break;
                    case "LOCATION":
                        RefreshLocationData();
                        break;
                    case "OPERATOR":
                        RefreshHandlerData();
                        break;
                    case "PRODUCT":
                        RefreshProductData();
                        break;
                    case "PACK_TYPE":
                        RefreshPackTypeData();
                        break;
                    default:
                        ReloadAndFilterCustomers();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [ApplyConfigDataRefresh] 异常: {ex.Message}");
            }
        }

        private void CreateMainControls()
        {
            Console.WriteLine(">>> [CreateMainControls] 开始");
            try
            {
                mainTabControl = new TabControl();
                mainTabControl.Dock = DockStyle.Fill;
                mainTabControl.Location = new Point(0, 0);
                mainTabControl.Size = new Size(800, 600);
                mainTabControl.Appearance = TabAppearance.Normal;

                Console.WriteLine(">>> [CreateMainControls] TabControl 创建完成");

                CreateCustomerTab();
                Console.WriteLine(">>> [CreateMainControls] CreateCustomerTab 完成");

                CreateProductTab();
                Console.WriteLine(">>> [CreateMainControls] CreateProductTab 完成");

                CreatePackTypeTab();
                Console.WriteLine(">>> [CreateMainControls] CreatePackTypeTab 完成");

                CreateLocationTab();
                Console.WriteLine(">>> [CreateMainControls] CreateLocationTab 完成");

                CreateHandlerTab();
                Console.WriteLine(">>> [CreateMainControls] CreateHandlerTab 完成");

                CreateBuyerTab();
                Console.WriteLine(">>> [CreateMainControls] CreateBuyerTab 完成");

                this.Controls.Add(mainTabControl);
                SettingsUiHelper.ApplyFormChrome(this);
                SettingsUiHelper.StyleTabControl(mainTabControl);
                foreach (TabPage page in mainTabControl.TabPages)
                    SettingsUiHelper.StyleTabPage(page);
                Console.WriteLine(">>> [CreateMainControls] TabControl 添加到窗体完成");

                this.Resize -= SystemSettingsForm_WorkspaceLayout;
                this.Resize += SystemSettingsForm_WorkspaceLayout;
                mainTabControl.SelectedIndexChanged -= SystemSettingsForm_TabLayout;
                mainTabControl.SelectedIndexChanged += SystemSettingsForm_TabLayout;
                mainTabControl.SizeChanged -= SystemSettingsForm_TabLayout;
                mainTabControl.SizeChanged += SystemSettingsForm_TabLayout;
                this.Load -= SystemSettingsForm_DeferredLayout;
                this.Load += SystemSettingsForm_DeferredLayout;
                this.Shown -= SystemSettingsForm_DeferredLayout;
                this.Shown += SystemSettingsForm_DeferredLayout;
                this.ParentChanged -= SystemSettingsForm_ParentLayout;
                this.ParentChanged += SystemSettingsForm_ParentLayout;
                this.HandleCreated -= SystemSettingsForm_HandleCreatedLayout;
                this.HandleCreated += SystemSettingsForm_HandleCreatedLayout;
                SystemSettingsForm_WorkspaceLayout(null, EventArgs.Empty);
                ScheduleSettingsWorkspaceLayout();

                Console.WriteLine($">>> [CreateMainControls] 窗体控件数量: {this.Controls.Count}");
                Console.WriteLine($">>> [CreateMainControls] TabControl选项卡数量: {mainTabControl.TabPages.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreateMainControls] 异常: {ex.ToString()}");
                MessageBox.Show($"创建主控件失败: {ex.Message}", "错误");
            }
            Console.WriteLine(">>> [CreateMainControls] 结束");
        }

        private void SystemSettingsForm_TabLayout(object sender, EventArgs e) =>
            SystemSettingsForm_WorkspaceLayout(null, EventArgs.Empty);

        private void SystemSettingsForm_DeferredLayout(object sender, EventArgs e) =>
            ScheduleSettingsWorkspaceLayout();

        private void SystemSettingsForm_ParentLayout(object sender, EventArgs e) =>
            ScheduleSettingsWorkspaceLayout();

        private void SystemSettingsForm_HandleCreatedLayout(object sender, EventArgs e) =>
            ScheduleSettingsWorkspaceLayout();

        /// <summary>
        /// 构造阶段 TabPage 的 ClientSize 往往尚未就绪；嵌入宿主后需在消息泵中再布局一次。
        /// </summary>
        private void ScheduleSettingsWorkspaceLayout()
        {
            if (IsDisposed)
                return;
            if (!IsHandleCreated)
                return;
            try
            {
                BeginInvoke(new MethodInvoker(() =>
                {
                    if (IsDisposed || mainTabControl == null || mainTabControl.IsDisposed)
                        return;
                    SystemSettingsForm_WorkspaceLayout(null, EventArgs.Empty);
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void SystemSettingsForm_WorkspaceLayout(object sender, EventArgs e)
        {
            if (mainTabControl == null || mainTabControl.IsDisposed)
                return;
            LayoutSettingsStandardTab(tabCustomers, gridCustomers);
            LayoutSettingsStandardTab(tabProducts, gridProducts);
            LayoutSettingsStandardTab(tabPackTypes, gridPackTypes);
            LayoutSettingsStandardTab(tabLocations, gridLocations);
            LayoutSettingsStandardTab(tabHandlers, gridHandlers);
            LayoutSettingsStandardTab(tabBuyers, gridBuyers);
        }

        private static GroupBox FindSettingsGroupBox(TabPage tab, string text)
        {
            if (tab == null)
                return null;
            foreach (Control c in tab.Controls)
            {
                if (c is GroupBox gb && gb.Text == text)
                    return gb;
            }
            return null;
        }

        private static void LayoutSettingsStandardTab(TabPage tab, Control grid)
        {
            if (tab == null || grid == null || grid.IsDisposed)
                return;
            int pad = 12;
            int tw = tab.ClientSize.Width;
            int th = tab.ClientSize.Height;
            if (tw < 60 || th < 60)
                return;

            GroupBox grpSearch = FindSettingsGroupBox(tab, "搜索");
            GroupBox grpActions = FindSettingsGroupBox(tab, "操作");

            foreach (Control c in tab.Controls)
            {
                if (c is Label lbl && (lbl.Font?.Bold ?? false))
                {
                    int lh = lbl.Height > 0 ? lbl.Height : 28;
                    lbl.SetBounds(pad, pad, Math.Max(80, tw - 2 * pad), lh);
                    break;
                }
            }

            int searchTop = 54;
            if (grpSearch != null)
                grpSearch.SetBounds(pad, searchTop, Math.Max(220, tw - 2 * pad), grpSearch.Height);

            int contentTop = grpSearch != null ? grpSearch.Bottom + 12 : searchTop;
            int actionsH = grpActions != null ? grpActions.Height : 90;
            if (grpActions != null)
                grpActions.SetBounds(pad, th - actionsH - pad, Math.Max(200, tw - 2 * pad), actionsH);

            int gridBottom = grpActions != null ? grpActions.Top - 12 : th - pad;
            int gridH = Math.Max(48, gridBottom - contentTop);
            grid.SetBounds(pad, contentTop, Math.Max(80, tw - 2 * pad), gridH);
        }

        private static void ApplyHandlerStatusStyle(DataGridViewRow gridRow, int status)
        {
            if (gridRow == null || !gridRow.DataGridView.Columns.Contains("status"))
                return;

            var cell = gridRow.Cells["status"];
            if (status == 1)
            {
                cell.Style.BackColor = Color.FromArgb(212, 237, 218);
                cell.Style.ForeColor = Color.FromArgb(21, 87, 36);
            }
            else
            {
                cell.Style.BackColor = Color.FromArgb(248, 215, 218);
                cell.Style.ForeColor = Color.FromArgb(114, 28, 36);
            }
        }

        private void PopulateHandlerGrid(DataTable dt)
        {
            if (gridHandlers == null || gridHandlers.IsDisposed)
                return;

            gridHandlers.SuspendLayout();
            gridHandlers.Rows.Clear();

            if (dt == null || dt.Rows.Count == 0)
            {
                int emptyRow = gridHandlers.Rows.Add("—", "暂无经手人数据", "");
                gridHandlers.Rows[emptyRow].DefaultCellStyle.ForeColor = Color.Gray;
                gridHandlers.ResumeLayout();
                return;
            }

            foreach (DataRow row in dt.Rows)
            {
                string handlerName = row["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(handlerName))
                    continue;

                int status = row["status"] != DBNull.Value ? Convert.ToInt32(row["status"]) : 1;
                string statusLabel = status == 1 ? "启用" : "禁用";
                string id = dt.Columns.Contains("id") ? row["id"]?.ToString() ?? "" : "";

                int rowIndex = gridHandlers.Rows.Add(statusLabel, handlerName, id);
                ApplyHandlerStatusStyle(gridHandlers.Rows[rowIndex], status);
            }

            gridHandlers.ResumeLayout();
        }

        private bool TryGetSelectedHandler(out string handlerName, out bool isEnabled)
        {
            handlerName = null;
            isEnabled = true;

            if (gridHandlers == null || gridHandlers.SelectedRows.Count == 0)
                return false;

            DataGridViewRow row = gridHandlers.SelectedRows[0];
            handlerName = row.Cells["name"].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(handlerName) || handlerName == "暂无经手人数据")
                return false;

            string status = row.Cells["status"].Value?.ToString() ?? "";
            isEnabled = status == "启用";
            return true;
        }

        private void RefreshHandlerData()
        {
            try
            {
                var dt = dbManager.GetAllHandlersWithDetails();
                PopulateHandlerGrid(dt);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新经手人数据失败: {ex.Message}", "错误");
            }
        }

        private void EditHandler()
        {
            try
            {
                if (!TryGetSelectedHandler(out string selectedHandler, out _))
                {
                    MessageBox.Show("请先选择要编辑的经手人", "提示");
                    return;
                }

                string newName = ShowInputDialog("请输入新的经手人名称:", "编辑经手人", selectedHandler);

                if (!string.IsNullOrWhiteSpace(newName) && newName != selectedHandler)
                {
                    Console.WriteLine($">>> [EditHandler] 调用数据库更新: '{selectedHandler}' -> '{newName}'");
                    bool success = dbManager.UpdateHandler(selectedHandler, newName);

                    if (success)
                    {
                        MessageBox.Show("经手人更新成功!", "提示");
                        RefreshHandlerData();
                    }
                    else
                    {
                        MessageBox.Show("经手人更新失败!", "错误");
                    }
                }
                else
                {
                    Console.WriteLine($">>> [EditHandler] 名称未改变或为空");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [EditHandler] 异常: {ex.Message}");
                MessageBox.Show($"编辑经手人失败: {ex.Message}", "错误");
            }
        }

        private void DeleteHandler()
        {
            try
            {
                if (!TryGetSelectedHandler(out string selectedHandler, out _))
                {
                    MessageBox.Show("请先选择要删除的经手人", "提示");
                    return;
                }

                string message = $"⚠️ 确定要永久删除经手人 [{selectedHandler}] 吗？\n\n" +
                                "此操作将：\n" +
                                "1. 从数据库中彻底删除该经手人记录\n" +
                                "2. 如果该经手人已被业务单据引用，将无法删除\n" +
                                "3. 删除后不可恢复！";

                if (MessageBox.Show(message, "确认永久删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    bool success = dbManager.DeleteHandler(selectedHandler);

                    if (success)
                    {
                        MessageBox.Show("经手人已永久删除！", "成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        RefreshHandlerData();
                    }
                    else
                    {
                        string errorMsg = "删除失败！\n\n可能原因：\n" +
                                        "1. 该经手人已被业务单据引用，不能删除\n" +
                                        "2. 数据库连接异常\n" +
                                        "3. 经手人不存在";
                        MessageBox.Show(errorMsg, "删除失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [DeleteHandler] 异常: {ex.Message}");
                MessageBox.Show($"删除经手人失败: {ex.Message}", "错误");
            }
        }

        private const string ConfigDeleteFailureHint =
            "删除失败，可能该项仍被历史业务单据引用。可先禁用，或在新年度业务数据清空后再删除。";

        private static string BuildConfigDeleteConfirmMessage(string itemLabel) =>
            $"⚠️ 确定要永久删除 {itemLabel} 吗？\n\n" +
            "此操作将：\n" +
            "1. 从数据库中彻底删除该记录\n" +
            "2. 若已被业务单据引用，将无法删除\n" +
            "3. 删除后不可恢复！";

        private void DeleteProduct()
        {
            try
            {
                if (gridProducts.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要删除的型号", "提示");
                    return;
                }

                int productId = Convert.ToInt32(gridProducts.CurrentRow.Cells["id"].Value);
                string productCode = gridProducts.CurrentRow.Cells["code"].Value?.ToString() ?? "";
                string productName = gridProducts.CurrentRow.Cells["name"].Value?.ToString() ?? "";
                string itemLabel = string.IsNullOrEmpty(productCode)
                    ? $"型号 [{productName}]"
                    : $"型号 [{productCode}] {productName}";

                if (MessageBox.Show(BuildConfigDeleteConfirmMessage(itemLabel), "确认永久删除",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                if (dbManager.DeleteProductType(productId))
                {
                    MessageBox.Show("型号已永久删除！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshProductData();
                }
                else
                {
                    MessageBox.Show(ConfigDeleteFailureHint, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除型号失败: {ex.Message}", "错误");
            }
        }

        private void DeletePackTypeItem()
        {
            try
            {
                if (gridPackTypes.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要删除的包装类型", "提示");
                    return;
                }

                int packTypeId = Convert.ToInt32(gridPackTypes.CurrentRow.Cells["id"].Value);
                string typeName = gridPackTypes.CurrentRow.Cells["name"].Value?.ToString() ?? "";

                if (MessageBox.Show(BuildConfigDeleteConfirmMessage($"包装类型 [{typeName}]"), "确认永久删除",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                if (dbManager.DeletePackType(packTypeId))
                {
                    MessageBox.Show("包装类型已永久删除！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshPackTypeData();
                }
                else
                {
                    MessageBox.Show(ConfigDeleteFailureHint, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除包装类型失败: {ex.Message}", "错误");
            }
        }

        private void DeleteLocation()
        {
            try
            {
                if (gridLocations.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要删除的库位", "提示");
                    return;
                }

                string locationName = gridLocations.CurrentRow.Cells["name"].Value?.ToString()?.Trim();
                if (string.IsNullOrEmpty(locationName) || locationName == "暂无库位数据" || locationName == "未找到匹配的库位")
                {
                    MessageBox.Show("请选择有效的库位", "提示");
                    return;
                }

                if (MessageBox.Show(BuildConfigDeleteConfirmMessage($"库位 [{locationName}]"), "确认永久删除",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                if (dbManager.DeleteLocation(locationName))
                {
                    MessageBox.Show("库位已永久删除！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshLocationData();
                }
                else
                {
                    MessageBox.Show(ConfigDeleteFailureHint, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除库位失败: {ex.Message}", "错误");
            }
        }

        private void ToggleHandlerStatus()
        {
            try
            {
                if (!TryGetSelectedHandler(out string selectedHandler, out bool isCurrentlyEnabled))
                {
                    MessageBox.Show("请先选择要操作的经手人", "提示");
                    return;
                }

                string newStatus = isCurrentlyEnabled ? "禁用" : "启用";

                string message = $"确定要{newStatus}经手人 [{selectedHandler}] 吗？";

                if (MessageBox.Show(message, "确认操作", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    bool success = dbManager.ToggleHandlerStatus(selectedHandler);
                    if (success)
                    {
                        MessageBox.Show($"经手人{newStatus}成功!", "提示");
                        RefreshHandlerData();
                    }
                    else
                    {
                        MessageBox.Show("状态切换失败!", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [ToggleHandlerStatus] 异常: {ex.Message}");
                MessageBox.Show($"操作失败: {ex.Message}", "错误");
            }
        }

        private void SearchHandlers(string keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    RefreshHandlerData();
                    return;
                }

                var dt = dbManager.ExecuteQuery(
                    "SELECT id, name, status FROM handlers WHERE name LIKE @keyword ORDER BY status DESC, name",
                    new Dictionary<string, object> { { "@keyword", "%" + keyword.Trim() + "%" } });

                PopulateHandlerGrid(dt);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"搜索经手人失败: {ex.Message}", "错误");
            }
        }

        private void AddHandler()
        {
            try
            {
                string handlerName = ShowInputDialog("请输入经手人名称:", "新增经手人");
                if (!string.IsNullOrWhiteSpace(handlerName))
                {
                    bool success = dbManager.AddHandler(handlerName);
                    if (success)
                    {
                        MessageBox.Show("经手人添加成功!", "提示");
                        RefreshHandlerData();
                    }
                    else
                    {
                        MessageBox.Show("经手人添加失败!", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新增经手人失败: {ex.Message}", "错误");
            }
        }

        private void PopulateBuyerGrid(DataTable dt)
        {
            if (gridBuyers == null || gridBuyers.IsDisposed)
                return;

            gridBuyers.SuspendLayout();
            gridBuyers.Rows.Clear();

            if (dt == null || dt.Rows.Count == 0)
            {
                int emptyRow = gridBuyers.Rows.Add("—", "暂无买家数据", "", "");
                gridBuyers.Rows[emptyRow].DefaultCellStyle.ForeColor = Color.Gray;
                gridBuyers.ResumeLayout();
                return;
            }

            foreach (DataRow row in dt.Rows)
            {
                string buyerCode = row["code"]?.ToString() ?? "";
                string buyerName = row["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(buyerCode) || string.IsNullOrWhiteSpace(buyerName))
                    continue;

                int status = row["status"] != DBNull.Value ? Convert.ToInt32(row["status"]) : 1;
                string statusLabel = status == 1 ? "启用" : "禁用";
                string phone = row.Table.Columns.Contains("phone") && row["phone"] != DBNull.Value
                    ? row["phone"]?.ToString()?.Trim() ?? ""
                    : "";
                int rowIndex = gridBuyers.Rows.Add(statusLabel, buyerName, phone, buyerCode);
                ApplyHandlerStatusStyle(gridBuyers.Rows[rowIndex], status);
            }

            gridBuyers.ResumeLayout();
        }

        private bool TryGetSelectedBuyer(out string buyerCode, out string buyerName, out bool isEnabled)
        {
            buyerCode = null;
            buyerName = null;
            isEnabled = true;

            if (gridBuyers == null || gridBuyers.SelectedRows.Count == 0)
                return false;

            DataGridViewRow row = gridBuyers.SelectedRows[0];
            buyerName = row.Cells["name"].Value?.ToString()?.Trim();
            buyerCode = row.Cells["code"].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(buyerName) || buyerName == "暂无买家数据" || string.IsNullOrEmpty(buyerCode))
                return false;

            string status = row.Cells["status"].Value?.ToString() ?? "";
            isEnabled = status == "启用";
            return true;
        }

        private void RefreshBuyerData()
        {
            try
            {
                var dt = dbManager.GetAllBuyersWithDetails();
                PopulateBuyerGrid(dt);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新买家数据失败: {ex.Message}", "错误");
            }
        }

        private void AddBuyer()
        {
            try
            {
                string buyerName = ShowInputDialog("填买家名称（必填）", "新增买家 (1/2 填名称)");
                if (string.IsNullOrWhiteSpace(buyerName))
                    return;

                string phone = ShowInputDialog("填联系电话（选填，不填可直接点确定）", "新增买家 (2/2 填电话)", "");
                bool success = dbManager.AddBuyer(buyerName.Trim(), phone?.Trim() ?? "");
                if (success)
                {
                    MessageBox.Show("买家添加成功!", "提示");
                    RefreshBuyerData();
                }
                else
                {
                    MessageBox.Show("买家添加失败!", "错误");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新增买家失败: {ex.Message}", "错误");
            }
        }

        private void EditBuyer()
        {
            try
            {
                if (!TryGetSelectedBuyer(out string buyerCode, out string buyerName, out _))
                {
                    MessageBox.Show("请先选择要编辑的买家", "提示");
                    return;
                }

                string currentPhone = "";
                var phoneRow = dbManager.ExecuteQuery(
                    "SELECT phone FROM clients WHERE code = @code",
                    new Dictionary<string, object> { { "@code", buyerCode } });
                if (phoneRow.Rows.Count > 0 && phoneRow.Rows[0]["phone"] != DBNull.Value)
                    currentPhone = phoneRow.Rows[0]["phone"]?.ToString() ?? "";

                string newName = ShowInputDialog("填买家名称（必填）", "编辑买家 (1/2 填名称)", buyerName);
                if (string.IsNullOrWhiteSpace(newName))
                    return;

                string phone = ShowInputDialog("填联系电话（选填，不填可直接点确定）", "编辑买家 (2/2 填电话)", currentPhone);
                bool success = dbManager.UpdateBuyer(buyerCode, newName.Trim(), phone?.Trim() ?? "");
                if (success)
                {
                    MessageBox.Show("买家更新成功!", "提示");
                    RefreshBuyerData();
                }
                else
                {
                    MessageBox.Show("买家更新失败!", "错误");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"编辑买家失败: {ex.Message}", "错误");
            }
        }

        private void DeleteBuyer()
        {
            try
            {
                if (!TryGetSelectedBuyer(out string buyerCode, out string buyerName, out _))
                {
                    MessageBox.Show("请先选择要删除的买家", "提示");
                    return;
                }

                string message = $"⚠️ 确定要删除买家 [{buyerName}] ({buyerCode}) 吗？\n\n" +
                                "此操作将：\n" +
                                "1. 从数据库中删除该买家记录\n" +
                                "2. 若该买家已有预售业务记录，将改为禁用\n" +
                                "3. 删除后不可恢复！";

                if (MessageBox.Show(message, "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    BuyerDeleteResult result = dbManager.DeleteBuyer(buyerCode);
                    switch (result)
                    {
                        case BuyerDeleteResult.Deleted:
                            MessageBox.Show("买家已删除!", "成功");
                            break;
                        case BuyerDeleteResult.Disabled:
                            MessageBox.Show(
                                "该买家已有预售业务记录，已改为禁用。\n买家仍会保留在列表中，状态显示为「禁用」。",
                                "已禁用",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            break;
                        default:
                            MessageBox.Show("删除失败！请检查买家是否存在或稍后重试。",
                                "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;
                    }

                    if (result != BuyerDeleteResult.Failed)
                        RefreshBuyerData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除买家失败: {ex.Message}", "错误");
            }
        }

        private void ToggleBuyerStatus()
        {
            try
            {
                if (!TryGetSelectedBuyer(out string buyerCode, out string buyerName, out bool isCurrentlyEnabled))
                {
                    MessageBox.Show("请先选择要操作的买家", "提示");
                    return;
                }

                string newStatus = isCurrentlyEnabled ? "禁用" : "启用";
                if (MessageBox.Show($"确定要{newStatus}买家 [{buyerName}] 吗？", "确认操作",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    bool success = dbManager.ToggleBuyerStatus(buyerCode);
                    if (success)
                    {
                        MessageBox.Show($"买家{newStatus}成功!", "提示");
                        RefreshBuyerData();
                    }
                    else
                    {
                        MessageBox.Show("状态切换失败!", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误");
            }
        }

        private void SearchBuyers(string keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    RefreshBuyerData();
                    return;
                }

                var dt = dbManager.ExecuteQuery(@"
SELECT code, name, phone, status
FROM clients
WHERE COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'BUYER'
  AND (name LIKE @keyword OR code LIKE @keyword OR COALESCE(phone, '') LIKE @keyword)
ORDER BY status DESC, code",
                    new Dictionary<string, object> { { "@keyword", "%" + keyword.Trim() + "%" } });

                PopulateBuyerGrid(dt);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"搜索买家失败: {ex.Message}", "错误");
            }
        }

        private void RefreshCustomerData()
        {
            ReloadAndFilterCustomers();
        }

        private void EnableCustomerButtons(bool enable)
        {
            try
            {
                string[] buttonNames = { "btnEditCustomer", "btnDeleteCustomer", "btnTestImmediate" };

                foreach (string btnName in buttonNames)
                {
                    Control[] buttons = this.Controls.Find(btnName, true);
                    foreach (Control btn in buttons)
                    {
                        if (btn is Button button)
                        {
                            button.Enabled = enable;
                            Console.WriteLine($"按钮 {btnName} 设置为: {enable}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置按钮状态失败: {ex.Message}");
            }
        }

        private void UpdateCustomerTitle(int count)
        {
            try
            {
                foreach (Control ctrl in tabCustomers.Controls)
                {
                    if (ctrl is Label lbl && lbl.Text.Contains("客户信息管理"))
                    {
                        string currentText = lbl.Text;
                        string newText = $"客户信息管理 (共 {count} 个客户)";

                        if (currentText != newText)
                        {
                            lbl.Text = newText;
                            Console.WriteLine($">>> 更新标题: {newText}");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 更新标题失败: {ex.Message}");
            }
        }

        private void TestCustomerData()
        {
            try
            {
                Console.WriteLine("=== 客户数据测试 ===");

                string sql = "SELECT * FROM clients";
                var dt = dbManager.ExecuteQuery(sql);
                Console.WriteLine($">>> 直接查询客户表，记录数: {dt.Rows.Count}");

                if (dt.Rows.Count > 0)
                {
                    Console.WriteLine(">>> 客户数据详情:");
                    foreach (DataRow row in dt.Rows)
                    {
                        Console.WriteLine($"   编号: {row["code"]}, 名称: {row["name"]}, ID: {row["id"]}");
                    }
                }
                else
                {
                    Console.WriteLine(">>> 数据库中没有任何客户记录！");
                }

                var clients = dbManager.GetAllClients();
                Console.WriteLine($">>> GetAllClients() 返回记录数: {clients.Rows.Count}");

                Console.WriteLine(">>> 表格列配置:");
                foreach (DataGridViewColumn col in gridCustomers.Columns)
                {
                    Console.WriteLine($"   列: {col.Name}, 标题: {col.HeaderText}, 宽度: {col.Width}");
                }

                Console.WriteLine("=== 测试完成 ===");

                if (dt.Rows.Count == 0)
                {
                    MessageBox.Show("数据库中没有任何客户记录！\n请先添加客户。", "提示");
                }
                else if (clients.Rows.Count == 0)
                {
                    MessageBox.Show("GetAllClients() 方法没有返回数据！\n可能存在数据过滤问题。", "警告");
                }
                else
                {
                    MessageBox.Show($"数据库中有 {dt.Rows.Count} 条记录\nGetAllClients() 返回 {clients.Rows.Count} 条记录", "测试结果");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"测试失败: {ex.Message}", "错误");
            }
        }

        private void RefreshLocationData()
        {
            try
            {
                Console.WriteLine(">>> [RefreshLocationData] 开始");

                if (gridLocations == null || gridLocations.IsDisposed)
                    return;

                gridLocations.SuspendLayout();
                gridLocations.Rows.Clear();

                var dt = dbManager.GetAllLocations();
                Console.WriteLine($">>> 获取到数据表: {dt.Rows.Count} 行, {dt.Columns.Count} 列");

                if (dt.Rows.Count > 0)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string locationName = row["name"].ToString();
                        string description = row["description"]?.ToString() ?? "";
                        int status = Convert.ToInt32(row["status"]);

                        int rowIndex = gridLocations.Rows.Add(
                            status == 1 ? "启用" : "禁用",
                            locationName,
                            description
                        );

                        // 根据状态设置行背景色
                        DataGridViewRow gridRow = gridLocations.Rows[rowIndex];
                        if (status == 1)
                        {
                            gridRow.Cells["status"].Style.BackColor = Color.FromArgb(212, 237, 218); // 浅绿色
                            gridRow.Cells["status"].Style.ForeColor = Color.FromArgb(21, 87, 36);    // 深绿色文字
                        }
                        else
                        {
                            gridRow.Cells["status"].Style.BackColor = Color.FromArgb(248, 215, 218); // 浅红色
                            gridRow.Cells["status"].Style.ForeColor = Color.FromArgb(114, 28, 36);    // 深红色文字
                        }

                        Console.WriteLine($">>> 添加库位: {locationName} ({(status == 1 ? "启用" : "禁用")})");
                    }

                    // 更新标题显示统计
                    int enabledCount = dt.Select("status = 1").Length;
                    int disabledCount = dt.Rows.Count - enabledCount;
                    Console.WriteLine($">>> 统计: 总计 {dt.Rows.Count} 个库位 (启用: {enabledCount}, 禁用: {disabledCount})");
                }
                else
                {
                    gridLocations.Rows.Add("", "暂无库位数据", "");
                    Console.WriteLine(">>> 没有库位数据");
                }

                gridLocations.ResumeLayout();
                Console.WriteLine($">>> [RefreshLocationData] 完成，表格行数: {gridLocations.Rows.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [RefreshLocationData] 异常: {ex.Message}");
                MessageBox.Show($"加载库位数据失败: {ex.Message}", "错误");
            }
        }

        private void SearchLocations(string keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    RefreshLocationData();
                    return;
                }

                gridLocations.SuspendLayout();
                gridLocations.Rows.Clear();

                var dt = dbManager.SearchLocations(keyword);
                Console.WriteLine($">>> [SearchLocations] 搜索完成，找到 {dt.Rows.Count} 条记录");

                if (dt.Rows.Count > 0)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string locationName = row["name"].ToString();
                        string description = row["description"]?.ToString() ?? "";
                        int status = Convert.ToInt32(row["status"]);

                        int rowIndex = gridLocations.Rows.Add(
                            status == 1 ? "启用" : "禁用",
                            locationName,
                            description
                        );

                        DataGridViewRow gridRow = gridLocations.Rows[rowIndex];
                        if (status == 1)
                        {
                            gridRow.Cells["status"].Style.BackColor = Color.FromArgb(212, 237, 218);
                            gridRow.Cells["status"].Style.ForeColor = Color.FromArgb(21, 87, 36);
                        }
                        else
                        {
                            gridRow.Cells["status"].Style.BackColor = Color.FromArgb(248, 215, 218);
                            gridRow.Cells["status"].Style.ForeColor = Color.FromArgb(114, 28, 36);
                        }
                    }
                }
                else
                {
                    gridLocations.Rows.Add("", "未找到匹配的库位", "");
                }

                gridLocations.ResumeLayout();
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [SearchLocations] 异常: {ex.Message}");
                MessageBox.Show($"搜索库位失败: {ex.Message}", "错误");
            }
        }

        private void AddLocation()
        {
            try
            {
                string locationName = ShowInputDialog("请输入库位名称:", "新增库位");
                if (!string.IsNullOrWhiteSpace(locationName))
                {
                    // 先检查库位是否存在
                    string checkSql = "SELECT COUNT(*) FROM locations WHERE name = @name";
                    int count = Convert.ToInt32(dbManager.ExecuteScalar(checkSql, new Dictionary<string, object>
            {
                { "@name", locationName }
            }));

                    if (count > 0)
                    {
                        MessageBox.Show($"库位 [{locationName}] 已存在！", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;  // 存在则直接返回，不继续添加
                    }

                    bool success = dbManager.AddLocation(locationName, "");
                    if (success)
                    {
                        MessageBox.Show($"库位 [{locationName}] 添加成功! (默认启用)", "提示");
                        RefreshLocationData();
                    }
                    else
                    {
                        MessageBox.Show("库位添加失败! 可能是名称已存在。", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新增库位失败: {ex.Message}", "错误");
            }
        }

        private void ToggleLocationStatus()
        {
            try
            {
                if (gridLocations.SelectedRows.Count == 0)
                {
                    MessageBox.Show("请先选择要操作的库位", "提示");
                    return;
                }

                DataGridViewRow selectedRow = gridLocations.SelectedRows[0];
                string locationName = selectedRow.Cells["name"].Value?.ToString() ?? "";
                string currentStatus = selectedRow.Cells["status"].Value?.ToString() ?? "";

                if (string.IsNullOrEmpty(locationName) || locationName == "暂无库位数据")
                {
                    MessageBox.Show("请选择有效的库位", "提示");
                    return;
                }

                bool isCurrentlyEnabled = currentStatus == "启用";
                string action = isCurrentlyEnabled ? "禁用" : "启用";

                string message = $"确定要{action}库位 [{locationName}] 吗？\n\n" +
                                (isCurrentlyEnabled ?
                                 "禁用后，该库位将不会显示在业务单据的选择列表中。" :
                                 "启用后，该库位将可以正常在业务单据中使用。");

                if (MessageBox.Show(message, "确认操作",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    bool success = dbManager.ToggleLocationStatus(locationName);

                    if (success)
                    {
                        MessageBox.Show($"库位 [{locationName}] 已{action}!", "成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        RefreshLocationData();
                    }
                    else
                    {
                        MessageBox.Show("操作失败，请重试！", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [ToggleLocationStatus] 异常: {ex.Message}");
                MessageBox.Show($"操作失败: {ex.Message}", "错误");
            }
        }

        private void RefreshProductData()
        {
            try
            {
                Console.WriteLine(">>> [RefreshProductData] 开始");
                var dt = dbManager.GetAllProductTypes();
                gridProducts.DataSource = dt;

                SettingsUiHelper.LocalizeProductTypeGrid(gridProducts);

                Console.WriteLine($">>> [RefreshProductData] 完成，加载 {dt.Rows.Count} 条记录");

                // 显示第一条记录的编号示例
                if (dt.Rows.Count > 0)
                {
                    string firstCode = dt.Rows[0]["code"]?.ToString() ?? "无编号";
                    Console.WriteLine($">>> 示例编号: {firstCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [RefreshProductData] 异常: {ex.Message}");
                MessageBox.Show($"加载型号数据失败: {ex.Message}", "错误");
            }
        }

        private void SearchProducts(string keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    RefreshProductData();
                    return;
                }

                var dt = dbManager.SearchProductTypes(keyword);
                gridProducts.DataSource = dt;
                SettingsUiHelper.LocalizeProductTypeGrid(gridProducts);
                Console.WriteLine($">>> [SearchProducts] 搜索完成，找到 {dt.Rows.Count} 条记录");

                if (dt.Rows.Count > 0)
                {
                    Console.WriteLine($">>> 搜索结果示例编号: {dt.Rows[0]["code"]}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [SearchProducts] 异常: {ex.Message}");
                MessageBox.Show($"搜索型号失败: {ex.Message}", "错误");
            }
        }

        private void AddProduct()
        {
            try
            {
                string productName = ShowInputDialog("请输入型号名称:", "新增型号");
                if (!string.IsNullOrWhiteSpace(productName))
                {
                    bool success = dbManager.AddProductType(productName);
                    if (success)
                    {
                        MessageBox.Show("型号添加成功! 系统已自动生成编号。", "提示");
                        RefreshProductData();
                    }
                    else
                    {
                        MessageBox.Show("型号添加失败! 可能是名称已存在。", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新增型号失败: {ex.Message}", "错误");
            }
        }

        private void EditProduct()
        {
            try
            {
                if (gridProducts.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要编辑的型号", "提示");
                    return;
                }

                int productId = Convert.ToInt32(gridProducts.CurrentRow.Cells["id"].Value);
                string currentCode = gridProducts.CurrentRow.Cells["code"].Value?.ToString() ?? "";
                string currentName = gridProducts.CurrentRow.Cells["name"].Value.ToString();

                string newName = ShowInputDialog("请输入新的型号名称:", $"编辑型号 (编号: {currentCode})", currentName);
                if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
                {
                    bool isActive = SettingsUiHelper.TryParseGridActive(gridProducts.CurrentRow.Cells["is_active"].Value, true);

                    bool success = dbManager.UpdateProductType(productId, newName, isActive);
                    if (success)
                    {
                        MessageBox.Show($"型号 [{currentCode}] 更新成功!", "提示");
                        RefreshProductData();
                    }
                    else
                    {
                        MessageBox.Show("型号更新失败! 可能是名称已存在。", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"编辑型号失败: {ex.Message}", "错误");
            }
        }


        private string ShowInputDialog(string prompt, string title, string defaultValue = "") =>
            SettingsUiHelper.ShowInputDialog(prompt, title, defaultValue, this);

        private void ToggleProductStatus()
        {
            try
            {
                if (gridProducts.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要操作的型号", "提示");
                    return;
                }

                int productId = Convert.ToInt32(gridProducts.CurrentRow.Cells["id"].Value);
                string productCode = gridProducts.CurrentRow.Cells["code"].Value?.ToString() ?? "";
                string productName = gridProducts.CurrentRow.Cells["name"].Value.ToString();
                bool isActive = SettingsUiHelper.TryParseGridActive(gridProducts.CurrentRow.Cells["is_active"].Value, true);

                string message = isActive ?
                    $"确定要禁用型号 [{productCode}] {productName} 吗？" :
                    $"确定要启用型号 [{productCode}] {productName} 吗？";

                if (MessageBox.Show(message, "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    bool success = dbManager.ToggleProductStatus(productId, !isActive);
                    if (success)
                    {
                        MessageBox.Show($"型号 [{productCode}] 操作成功!", "提示");
                        RefreshProductData();
                    }
                    else
                    {
                        MessageBox.Show("操作失败!", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误");
            }
        }

        private void InitializeComponent()
        {
            tabControl = new TabControl();
            txtRestoreFile = new TextBox();
            Console.WriteLine(">>> [InitializeComponent] 开始");
            this.SuspendLayout();
            SettingsUiHelper.ApplyFormChrome(this);
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.MinimumSize = new System.Drawing.Size(640, 480);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.Text = "系统设置";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ResumeLayout(false);
            Console.WriteLine(">>> [InitializeComponent] 结束");
        }

        private void CreateCustomerTab()
        {
            Console.WriteLine(">>> [CreateCustomerTab] 开始");
            try
            {
                tabCustomers = new TabPage();
                tabCustomers.Text = "客户管理";
                tabCustomers.BackColor = Color.White;
                tabCustomers.Padding = new Padding(10);
                mainTabControl.TabPages.Add(tabCustomers);
                Console.WriteLine(">>> [CreateCustomerTab] TabPage 创建完成");

                var label = new Label();
                label.Text = "客户信息管理";
                label.Location = new Point(20, 20);
                label.Size = new Size(400, 28);
                label.Font = new Font("微软雅黑", 13, FontStyle.Bold);
                tabCustomers.Controls.Add(label);

                var grpSearch = new GroupBox();
                grpSearch.Text = "搜索";
                grpSearch.Location = new Point(20, 60);
                grpSearch.Size = new Size(700, 76);
                grpSearch.Font = new Font("微软雅黑", 10);

                var txtSearchCustomer = SettingsUiHelper.CreatePlaceholderTextBox(ClientSearchHelper.PlaceholderText, 252);
                txtSearchCustomer.Name = "txtSearchCustomer";
                txtSearchCustomer.Location = new Point(20, 24);
                txtSearchCustomerField = txtSearchCustomer;

                var btnSearchCustomer = new Button();
                btnSearchCustomer.Text = "搜索";
                btnSearchCustomer.Location = new Point(282, 22);
                btnSearchCustomer.Size = new Size(92, 30);
                btnSearchCustomer.BackColor = Color.FromArgb(52, 152, 219);
                btnSearchCustomer.ForeColor = Color.White;
                btnSearchCustomer.FlatStyle = FlatStyle.Flat;

                grpSearch.Controls.Add(txtSearchCustomer);
                grpSearch.Controls.Add(btnSearchCustomer);
                tabCustomers.Controls.Add(grpSearch);

                gridCustomers = new DataGridView();
                gridCustomers.Name = "gridCustomers";
                gridCustomers.Location = new Point(20, 140);
                gridCustomers.Size = new Size(750, 300);
                gridCustomers.ReadOnly = true;
                gridCustomers.RowHeadersVisible = false;
                gridCustomers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                gridCustomers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                gridCustomers.BackgroundColor = Color.White;
                gridCustomers.Font = new Font("微软雅黑", 10);
                gridCustomers.RowTemplate.Height = 30;

                gridCustomers.Columns.Add("code", "客户编号");
                gridCustomers.Columns.Add("name", "客户名称");
                gridCustomers.Columns.Add("phone", "联系电话");
                gridCustomers.Columns.Add("address", "地址");

                gridCustomers.Columns["code"].Width = 100;
                gridCustomers.Columns["name"].Width = 200;
                gridCustomers.Columns["phone"].Width = 120;
                gridCustomers.Columns["address"].Width = 250;

                SettingsUiHelper.LocalizeManualCustomerGrid(gridCustomers);
                tabCustomers.Controls.Add(gridCustomers);

                GroupBox grpActions = new GroupBox();
                grpActions.Text = "操作";
                grpActions.Location = new Point(20, 450);
                grpActions.Size = new Size(750, 92);
                grpActions.Font = new Font("微软雅黑", 10);

                var btnAddCustomer = new Button();
                btnAddCustomer.Name = "btnAddCustomer";
                btnAddCustomer.Text = "➕ 新增客户";
                btnAddCustomer.Location = new Point(20, 28);
                btnAddCustomer.Size = new Size(110, 40);
                btnAddCustomer.BackColor = Color.FromArgb(46, 204, 113);
                btnAddCustomer.ForeColor = Color.White;
                btnAddCustomer.FlatStyle = FlatStyle.Flat;

                var btnEditCustomer = new Button();
                btnEditCustomer.Name = "btnEditCustomer";
                btnEditCustomer.Text = "✏️ 编辑客户";
                btnEditCustomer.Location = new Point(138, 28);
                btnEditCustomer.Size = new Size(110, 40);
                btnEditCustomer.BackColor = Color.FromArgb(52, 152, 219);
                btnEditCustomer.ForeColor = Color.White;
                btnEditCustomer.FlatStyle = FlatStyle.Flat;

                var btnDeleteCustomer = new Button();
                btnDeleteCustomer.Name = "btnDeleteCustomer";
                btnDeleteCustomer.Text = "🗑️ 删除客户";
                btnDeleteCustomer.Location = new Point(256, 28);
                btnDeleteCustomer.Size = new Size(110, 40);
                btnDeleteCustomer.BackColor = Color.FromArgb(231, 76, 60);
                btnDeleteCustomer.ForeColor = Color.White;
                btnDeleteCustomer.FlatStyle = FlatStyle.Flat;

                var btnRefreshCustomer = new Button();
                btnRefreshCustomer.Name = "btnRefreshCustomer";
                btnRefreshCustomer.Text = "🔄 刷新";
                btnRefreshCustomer.Location = new Point(374, 28);
                btnRefreshCustomer.Size = new Size(110, 40);
                btnRefreshCustomer.BackColor = Color.FromArgb(155, 89, 182);
                btnRefreshCustomer.ForeColor = Color.White;
                btnRefreshCustomer.FlatStyle = FlatStyle.Flat;

                var btnExportCustomers = new Button();
                btnExportCustomers.Name = "btnExportCustomers";
                btnExportCustomers.Text = "📄 导出";
                btnExportCustomers.Location = new Point(492, 28);
                btnExportCustomers.Size = new Size(110, 40);
                btnExportCustomers.BackColor = Color.FromArgb(241, 196, 15);
                btnExportCustomers.ForeColor = Color.White;
                btnExportCustomers.FlatStyle = FlatStyle.Flat;

                Button btnClearSearch = new Button();
                btnClearSearch.Text = "清空";
                btnClearSearch.Font = new Font("微软雅黑", 10);
                btnClearSearch.BackColor = Color.FromArgb(149, 165, 166);
                btnClearSearch.ForeColor = Color.White;
                btnClearSearch.FlatStyle = FlatStyle.Flat;
                btnClearSearch.Size = new Size(72, 30);
                btnClearSearch.Location = new Point(382, 24);
                btnClearSearch.Click += (s, e) =>
                {
                    SettingsUiHelper.ResetPlaceholder(txtSearchCustomer);
                    ApplyCustomerSearchFilter();
                };
                grpSearch.Controls.Add(btnClearSearch);

                grpActions.Controls.Add(btnAddCustomer);
                grpActions.Controls.Add(btnEditCustomer);
                grpActions.Controls.Add(btnDeleteCustomer);
                grpActions.Controls.Add(btnRefreshCustomer);
                grpActions.Controls.Add(btnExportCustomers);

                tabCustomers.Controls.Add(grpActions);

                btnRefreshCustomer.Click += (s, e) => ForceRefreshCustomerData();
                btnAddCustomer.Click += (s, e) => AddCustomer();
                btnEditCustomer.Click += (s, e) => EditCustomer();
                btnDeleteCustomer.Click += (s, e) => DeleteCustomer();
                btnExportCustomers.Click += (s, e) => ExportCustomers();

                btnSearchCustomer.Click += (s, e) => ApplyCustomerSearchFilter();

                txtSearchCustomer.TextChanged += (s, e) => ApplyCustomerSearchFilter();

                txtSearchCustomer.KeyPress += (s, e) =>
                {
                    if (e.KeyChar == (char)Keys.Enter)
                        e.Handled = true;
                };

                gridCustomers.CellDoubleClick += (s, e) =>
                {
                    if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                    {
                        EditCustomer();
                    }
                };

                Console.WriteLine(">>> 进入客户管理页面，立即加载客户数据");
                InitializeCustomerData();

                Console.WriteLine(">>> [CreateCustomerTab] 客户管理界面创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreateCustomerTab] 异常: {ex}");
                MessageBox.Show($"创建客户管理界面失败: {ex.Message}", "错误");
            }
            Console.WriteLine(">>> [CreateCustomerTab] 结束");
        }

        private void ReloadCustomerCache()
        {
            _customerCache.Clear();

            string sql = @"SELECT code, name, phone, address FROM clients 
WHERE COALESCE(customer_type, CASE WHEN UPPER(code) LIKE 'MJ%' THEN 'BUYER' ELSE 'SELLER' END) = 'SELLER'
ORDER BY code";
            DataTable customers = dbManager.ExecuteQuery(sql);

            foreach (DataRow row in customers.Rows)
            {
                string name = row["name"]?.ToString() ?? string.Empty;
                _customerCache.Add(new CustomerRowEntry
                {
                    Code = row["code"]?.ToString() ?? string.Empty,
                    Name = name,
                    Phone = row["phone"]?.ToString() ?? string.Empty,
                    Address = row["address"]?.ToString() ?? string.Empty,
                    Initials = PinyinHelper.GetInitials(name)
                });
            }
        }

        private void ReloadAndFilterCustomers()
        {
            ReloadCustomerCache();
            ApplyCustomerSearchFilter();
        }

        private void ApplyCustomerSearchFilter(string keyword = null)
        {
            if (gridCustomers == null || gridCustomers.IsDisposed)
                return;

            if (keyword == null && txtSearchCustomerField != null)
                keyword = SettingsUiHelper.GetEffectiveText(txtSearchCustomerField);

            List<CustomerRowEntry> rows;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                rows = _customerCache.ToList();
            }
            else
            {
                string key = keyword.Trim();
                string keyLower = key.ToLowerInvariant();
                bool letterKeyword = IsInitialSearchKeyword(keyLower);

                rows = _customerCache
                    .Where(entry => ClientSearchHelper.MatchesClientFields(
                        entry.Code, entry.Name, key, ClientSearchHelper.PlaceholderText))
                    .OrderBy(entry => GetCustomerMatchPriority(entry, key, keyLower, letterKeyword))
                    .ThenBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            RenderCustomerGrid(rows, !string.IsNullOrWhiteSpace(keyword));
        }

        private static bool IsInitialSearchKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return false;

            foreach (char c in keyword)
            {
                if (!char.IsLetter(c) || c > 127)
                    return false;
            }

            return true;
        }

        private static int GetCustomerMatchPriority(CustomerRowEntry entry, string key, string keyLower, bool letterKeyword)
        {
            string initials = entry.Initials ?? PinyinHelper.GetInitials(entry.Name);

            if (letterKeyword && initials.StartsWith(keyLower, StringComparison.Ordinal))
                return 0;
            if (letterKeyword && initials.IndexOf(keyLower, StringComparison.Ordinal) >= 0)
                return 1;
            if (entry.Code != null && entry.Code.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                return 2;
            if (entry.Name != null && entry.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                return 3;
            if (initials.StartsWith(keyLower, StringComparison.Ordinal))
                return 4;
            if (initials.IndexOf(keyLower, StringComparison.Ordinal) >= 0)
                return 5;

            return 6;
        }

        private void RenderCustomerGrid(IList<CustomerRowEntry> rows, bool isFiltered)
        {
            gridCustomers.SuspendLayout();

            try
            {
                gridCustomers.Rows.Clear();

                if (rows.Count == 0)
                {
                    gridCustomers.Rows.Add(isFiltered ? "未找到匹配客户" : "暂无客户数据", "", "", "");
                    EnableCustomerButtons(false);
                }
                else
                {
                    foreach (CustomerRowEntry entry in rows)
                        gridCustomers.Rows.Add(entry.Code, entry.Name, entry.Phone, entry.Address);

                    EnableCustomerButtons(true);
                }

                UpdateCustomerTitle(rows.Count);
            }
            finally
            {
                gridCustomers.ResumeLayout();
                gridCustomers.Refresh();

                if (gridCustomers.Rows.Count > 0)
                    gridCustomers.FirstDisplayedScrollingRowIndex = 0;
            }
        }

        private void SearchCustomersNew(string keyword)
        {
            ApplyCustomerSearchFilter(keyword);
        }

        private void ForceRefreshCustomerData()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(ForceRefreshCustomerData));
                    return;
                }

                if (gridCustomers == null || gridCustomers.IsDisposed)
                    return;

                ReloadAndFilterCustomers();
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [ForceRefreshCustomerData] 严重异常: {ex.Message}");
            }
        }

        private void InitializeCustomerData()
        {
            try
            {
                ReloadAndFilterCustomers();
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [InitializeCustomerData] 异常: {ex.Message}");
            }
        }

        private void AddCustomer()
        {
            try
            {
                Console.WriteLine(">>> [AddCustomer] 开始添加客户");

                string clientCode = dbManager.GenerateClientCode();
                Console.WriteLine($">>> 生成的客户编号: {clientCode}");

                string name = Microsoft.VisualBasic.Interaction.InputBox(
                    "请输入客户名称：",
                    "添加客户",
                    "", -1, -1);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    string phone = Microsoft.VisualBasic.Interaction.InputBox(
                        "请输入联系电话（可选）：",
                        "添加客户",
                        "", -1, -1);

                    string address = Microsoft.VisualBasic.Interaction.InputBox(
                        "请输入地址（可选）：",
                        "添加客户",
                        "", -1, -1);

                    bool success = dbManager.AddClient(
                        clientCode,
                        name.Trim(),
                        "",
                        phone?.Trim() ?? "",
                        address?.Trim() ?? ""
                    );

                    if (success)
                    {
                        MessageBox.Show($"客户添加成功！\n编号: {clientCode}\n名称: {name}",
                            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        ReloadAndFilterCustomers();
                    }
                    else
                    {
                        MessageBox.Show("客户添加失败，可能是编号重复！", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [AddCustomer] 异常: {ex}");
                MessageBox.Show($"添加客户失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EditCustomer()
        {
            try
            {
                if (gridCustomers.SelectedRows.Count == 0)
                {
                    MessageBox.Show("请先选择一个客户", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                DataGridViewRow selectedRow = gridCustomers.SelectedRows[0];
                string clientCode = selectedRow.Cells["code"].Value?.ToString() ?? "";
                string currentName = selectedRow.Cells["name"].Value?.ToString() ?? "";
                string currentPhone = selectedRow.Cells["phone"].Value?.ToString() ?? "";
                string currentAddress = selectedRow.Cells["address"].Value?.ToString() ?? "";

                Console.WriteLine($">>> [EditCustomer] 开始编辑客户: 编号={clientCode}");

                if (string.IsNullOrEmpty(clientCode))
                {
                    MessageBox.Show("无效的客户编号", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string newName = ShowSimpleInputDialog("请输入客户名称:", "编辑客户", currentName);
                if (string.IsNullOrWhiteSpace(newName))
                {
                    Console.WriteLine(">>> 用户取消编辑");
                    return;
                }

                string newPhone = ShowSimpleInputDialog("请输入联系电话:", "编辑客户", currentPhone);
                string newAddress = ShowSimpleInputDialog("请输入地址:", "编辑客户", currentAddress);

                try
                {
                    string currentContact = "";
                    var contactRow = dbManager.ExecuteQuery(
                        "SELECT contact FROM clients WHERE code = @code",
                        new Dictionary<string, object> { { "@code", clientCode } });
                    if (contactRow.Rows.Count > 0 && contactRow.Rows[0]["contact"] != DBNull.Value)
                        currentContact = contactRow.Rows[0]["contact"]?.ToString() ?? "";

                    bool success = dbManager.UpdateClient(
                        clientCode,
                        newName.Trim(),
                        currentContact,
                        newPhone?.Trim() ?? "",
                        newAddress?.Trim() ?? "");

                    if (success)
                    {
                        MessageBox.Show("客户信息更新成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ReloadAndFilterCustomers();
                    }
                    else
                    {
                        MessageBox.Show("客户信息更新失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($">>> 更新客户异常: {ex.Message}");
                    MessageBox.Show($"更新失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [EditCustomer] 异常: {ex.Message}");
                MessageBox.Show($"编辑客户失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string ShowSimpleInputDialog(string prompt, string title, string defaultValue = "") =>
            SettingsUiHelper.ShowInputDialog(prompt, title, defaultValue, this);

        private void DeleteCustomer()
        {
            try
            {
                if (gridCustomers.SelectedRows.Count == 0)
                {
                    MessageBox.Show("请先选择一个客户", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                DataGridViewRow selectedRow = gridCustomers.SelectedRows[0];
                string clientCode = selectedRow.Cells["code"].Value?.ToString() ?? "";
                string clientName = selectedRow.Cells["name"].Value?.ToString() ?? "";

                if (string.IsNullOrEmpty(clientCode))
                {
                    MessageBox.Show("无效的客户编号", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                DialogResult confirm = MessageBox.Show(
                    $"确定要删除客户吗？\n编号: {clientCode}\n名称: {clientName}\n\n删除后将无法恢复！",
                    "确认删除",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm == DialogResult.Yes)
                {
                    Console.WriteLine($">>> 删除客户: {clientCode} - {clientName}");

                    string checkSql = @"
                        SELECT COUNT(*) as cnt 
                        FROM (
                            SELECT client_code FROM inbound_transactions WHERE client_code = @code
                            UNION ALL
                            SELECT client_code FROM sales_transactions WHERE client_code = @code
                            UNION ALL
                            SELECT client_code FROM packaging_transactions WHERE client_code = @code
                        )";

                    DataTable checkResult = dbManager.ExecuteQuery(checkSql, new Dictionary<string, object> { { "@code", clientCode } });

                    bool hasRecords = false;
                    if (checkResult.Rows.Count > 0)
                    {
                        hasRecords = Convert.ToInt32(checkResult.Rows[0]["cnt"]) > 0;
                    }

                    if (hasRecords)
                    {
                        MessageBox.Show("该客户有业务记录，不能直接删除！\n建议在数据库中禁用该客户。",
                                      "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    if (dbManager.DeleteClientByCode(clientCode))
                    {
                        MessageBox.Show("客户删除成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ReloadAndFilterCustomers();
                    }
                    else
                    {
                        MessageBox.Show("客户删除失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除客户失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportCustomers()
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "CSV文件 (*.csv)|*.csv|Excel文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";
                saveFileDialog.FileName = $"客户列表_{DateTime.Now:yyyyMMdd_HHmmss}";
                saveFileDialog.Title = "导出客户数据";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = saveFileDialog.FileName;
                    string extension = Path.GetExtension(filePath).ToLower();

                    if (extension == ".csv")
                    {
                        ExportToCSV(filePath);
                    }
                    else if (extension == ".xlsx")
                    {
                        MessageBox.Show("Excel导出功能开发中...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportToCSV(string filePath)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("客户编号,客户名称,联系电话,地址");

                    foreach (DataGridViewRow row in gridCustomers.Rows)
                    {
                        if (row.IsNewRow) continue;

                        string code = EscapeCsvField(row.Cells["code"].Value?.ToString() ?? "");
                        string name = EscapeCsvField(row.Cells["name"].Value?.ToString() ?? "");
                        string phone = EscapeCsvField(row.Cells["phone"].Value?.ToString() ?? "");
                        string address = EscapeCsvField(row.Cells["address"].Value?.ToString() ?? "");

                        writer.WriteLine($"{code},{name},{phone},{address}");
                    }
                }

                MessageBox.Show($"客户数据已导出到：\n{filePath}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出CSV失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string EscapeCsvField(string field)
        {
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private void CreateProductTab()
        {
            try
            {
                Console.WriteLine(">>> [CreateProductTab] 开始创建型号管理Tab");

                tabProducts = new TabPage();
                tabProducts.Text = "型号管理";
                tabProducts.BackColor = Color.White;
                tabProducts.Padding = new Padding(10);
                mainTabControl.TabPages.Add(tabProducts);

                var lblTitle = new Label();
                lblTitle.Text = "商品型号管理";
                lblTitle.Location = new Point(20, 20);
                lblTitle.Size = new Size(360, 28);
                lblTitle.Font = new Font("微软雅黑", 13, FontStyle.Bold);
                tabProducts.Controls.Add(lblTitle);

                var grpSearch = new GroupBox();
                grpSearch.Text = "搜索";
                grpSearch.Location = new Point(20, 60);
                grpSearch.Size = new Size(500, 76);
                grpSearch.Font = new Font("微软雅黑", 10);

                var txtSearchProduct = SettingsUiHelper.CreatePlaceholderTextBox("输入型号名称搜索");
                txtSearchProduct.Location = new Point(20, 24);

                var btnSearchProduct = new Button();
                btnSearchProduct.Text = "搜索";
                btnSearchProduct.Location = new Point(342, 22);
                btnSearchProduct.Size = new Size(92, 30);
                btnSearchProduct.BackColor = Color.FromArgb(0, 102, 204);
                btnSearchProduct.ForeColor = Color.White;
                btnSearchProduct.FlatStyle = FlatStyle.Flat;

                grpSearch.Controls.Add(txtSearchProduct);
                grpSearch.Controls.Add(btnSearchProduct);
                tabProducts.Controls.Add(grpSearch);

                gridProducts = new DataGridView();
                gridProducts.Location = new Point(20, 140);
                gridProducts.Size = new Size(500, 200);
                gridProducts.ReadOnly = true;
                gridProducts.RowHeadersVisible = false;
                gridProducts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                gridProducts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                gridProducts.BackgroundColor = Color.White;
                gridProducts.Font = new Font("微软雅黑", 10);
                SettingsUiHelper.ApplySettingsGrid(gridProducts);
                tabProducts.Controls.Add(gridProducts);

                var grpActions = new GroupBox();
                grpActions.Text = "操作";
                grpActions.Location = new Point(20, 350);
                grpActions.Size = new Size(560, 92);
                grpActions.Font = new Font("微软雅黑", 10);

                var btnAddProduct = new Button();
                btnAddProduct.Text = "新增型号";
                btnAddProduct.Location = new Point(20, 30);
                btnAddProduct.Size = new Size(96, 36);
                btnAddProduct.BackColor = Color.FromArgb(40, 167, 69);
                btnAddProduct.ForeColor = Color.White;
                btnAddProduct.FlatStyle = FlatStyle.Flat;

                var btnEditProduct = new Button();
                btnEditProduct.Text = "编辑型号";
                btnEditProduct.Location = new Point(124, 30);
                btnEditProduct.Size = new Size(96, 36);
                btnEditProduct.BackColor = Color.FromArgb(0, 123, 255);
                btnEditProduct.ForeColor = Color.White;
                btnEditProduct.FlatStyle = FlatStyle.Flat;

                var btnToggleProduct = new Button();
                btnToggleProduct.Text = "启用/禁用";
                btnToggleProduct.Location = new Point(228, 30);
                btnToggleProduct.Size = new Size(96, 36);
                btnToggleProduct.BackColor = Color.FromArgb(255, 193, 7);
                btnToggleProduct.ForeColor = Color.Black;
                btnToggleProduct.FlatStyle = FlatStyle.Flat;

                var btnDeleteProduct = new Button();
                btnDeleteProduct.Text = "删除";
                btnDeleteProduct.Location = new Point(332, 30);
                btnDeleteProduct.Size = new Size(96, 36);
                btnDeleteProduct.BackColor = Color.FromArgb(220, 38, 38);
                btnDeleteProduct.ForeColor = Color.White;
                btnDeleteProduct.FlatStyle = FlatStyle.Flat;

                var btnRefreshProduct = new Button();
                btnRefreshProduct.Text = "刷新";
                btnRefreshProduct.Location = new Point(436, 30);
                btnRefreshProduct.Size = new Size(96, 36);
                btnRefreshProduct.BackColor = Color.FromArgb(108, 117, 125);
                btnRefreshProduct.ForeColor = Color.White;
                btnRefreshProduct.FlatStyle = FlatStyle.Flat;

                grpActions.Controls.Add(btnAddProduct);
                grpActions.Controls.Add(btnEditProduct);
                grpActions.Controls.Add(btnToggleProduct);
                grpActions.Controls.Add(btnDeleteProduct);
                grpActions.Controls.Add(btnRefreshProduct);
                tabProducts.Controls.Add(grpActions);

                btnRefreshProduct.Click += (s, e) => RefreshProductData();
                btnSearchProduct.Click += (s, e) => SearchProducts(SettingsUiHelper.GetEffectiveText(txtSearchProduct));
                btnAddProduct.Click += (s, e) => AddProduct();
                btnEditProduct.Click += (s, e) => EditProduct();
                btnToggleProduct.Click += (s, e) => ToggleProductStatus();
                btnDeleteProduct.Click += (s, e) => DeleteProduct();

                RefreshProductData();

                Console.WriteLine(">>> [CreateProductTab] 型号管理Tab创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreateProductTab] 异常: {ex.Message}");
            }
        }

        private void RefreshPackTypeData()
        {
            try
            {
                var dt = dbManager.GetAllPackTypes();
                gridPackTypes.DataSource = dt;

                SettingsUiHelper.LocalizePackTypeGrid(gridPackTypes);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载包装类型失败: {ex.Message}", "错误");
            }
        }

        private void SearchPackTypesByKeyword(string keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    RefreshPackTypeData();
                    return;
                }

                var dt = dbManager.SearchPackTypes(keyword);
                gridPackTypes.DataSource = dt;
                SettingsUiHelper.LocalizePackTypeGrid(gridPackTypes);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"搜索包装类型失败: {ex.Message}", "错误");
            }
        }

        private void AddPackType()
        {
            try
            {
                string name = ShowInputDialog("请输入包装类型名称:", "新增包装类型");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    bool success = dbManager.AddPackType(name);
                    if (success)
                    {
                        MessageBox.Show("包装类型添加成功!", "提示");
                        RefreshPackTypeData();
                    }
                    else
                    {
                        MessageBox.Show("添加失败! 名称可能已存在。", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新增包装类型失败: {ex.Message}", "错误");
            }
        }

        private void EditPackType()
        {
            try
            {
                if (gridPackTypes.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要编辑的包装类型", "提示");
                    return;
                }

                int id = Convert.ToInt32(gridPackTypes.CurrentRow.Cells["id"].Value);
                string currentName = gridPackTypes.CurrentRow.Cells["name"].Value.ToString();

                string newName = ShowInputDialog("请输入新的名称:", "编辑包装类型", currentName);
                if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
                {
                    bool isActive = SettingsUiHelper.TryParseGridActive(gridPackTypes.CurrentRow.Cells["is_active"].Value, true);
                    bool success = dbManager.UpdatePackType(id, newName, isActive);
                    if (success)
                    {
                        MessageBox.Show("包装类型更新成功!", "提示");
                        RefreshPackTypeData();
                    }
                    else
                    {
                        MessageBox.Show("更新失败! 名称可能已存在。", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"编辑包装类型失败: {ex.Message}", "错误");
            }
        }

        private void TogglePackTypeStatusUi()
        {
            try
            {
                if (gridPackTypes.CurrentRow == null)
                {
                    MessageBox.Show("请先选择要操作的包装类型", "提示");
                    return;
                }

                int id = Convert.ToInt32(gridPackTypes.CurrentRow.Cells["id"].Value);
                string typeName = gridPackTypes.CurrentRow.Cells["name"].Value.ToString();
                bool isActive = SettingsUiHelper.TryParseGridActive(gridPackTypes.CurrentRow.Cells["is_active"].Value, true);

                string message = isActive
                    ? $"确定要禁用包装类型 [{typeName}] 吗？\n禁用后将不会出现在包装记账的下拉列表中。"
                    : $"确定要启用包装类型 [{typeName}] 吗？";

                if (MessageBox.Show(message, "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    bool success = dbManager.TogglePackTypeStatus(id, !isActive);
                    if (success)
                    {
                        MessageBox.Show("操作成功!", "提示");
                        RefreshPackTypeData();
                    }
                    else
                    {
                        MessageBox.Show("操作失败!", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误");
            }
        }

        private void CreatePackTypeTab()
        {
            try
            {
                tabPackTypes = new TabPage();
                tabPackTypes.Text = "包装类型";
                tabPackTypes.BackColor = Color.White;
                tabPackTypes.Padding = new Padding(10);
                mainTabControl.TabPages.Add(tabPackTypes);

                var lblTitle = new Label();
                lblTitle.Text = "包装类型管理";
                lblTitle.Location = new Point(20, 20);
                lblTitle.Size = new Size(360, 28);
                lblTitle.Font = new Font("微软雅黑", 13, FontStyle.Bold);
                tabPackTypes.Controls.Add(lblTitle);

                var grpSearch = new GroupBox();
                grpSearch.Text = "搜索";
                grpSearch.Location = new Point(20, 60);
                grpSearch.Size = new Size(500, 76);
                grpSearch.Font = new Font("微软雅黑", 10);

                var txtSearchPackType = SettingsUiHelper.CreatePlaceholderTextBox("输入包装类型名称搜索");
                txtSearchPackType.Location = new Point(20, 24);

                var btnSearchPackType = new Button();
                btnSearchPackType.Text = "搜索";
                btnSearchPackType.Location = new Point(342, 22);
                btnSearchPackType.Size = new Size(92, 30);
                btnSearchPackType.BackColor = Color.FromArgb(0, 102, 204);
                btnSearchPackType.ForeColor = Color.White;
                btnSearchPackType.FlatStyle = FlatStyle.Flat;

                grpSearch.Controls.Add(txtSearchPackType);
                grpSearch.Controls.Add(btnSearchPackType);
                tabPackTypes.Controls.Add(grpSearch);

                gridPackTypes = new DataGridView();
                gridPackTypes.Location = new Point(20, 140);
                gridPackTypes.Size = new Size(500, 200);
                gridPackTypes.ReadOnly = true;
                gridPackTypes.RowHeadersVisible = false;
                gridPackTypes.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                gridPackTypes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                gridPackTypes.BackgroundColor = Color.White;
                gridPackTypes.Font = new Font("微软雅黑", 10);
                SettingsUiHelper.ApplySettingsGrid(gridPackTypes);
                tabPackTypes.Controls.Add(gridPackTypes);

                var grpActions = new GroupBox();
                grpActions.Text = "操作";
                grpActions.Location = new Point(20, 350);
                grpActions.Size = new Size(560, 92);
                grpActions.Font = new Font("微软雅黑", 10);

                var btnAdd = new Button();
                btnAdd.Text = "新增";
                btnAdd.Location = new Point(20, 30);
                btnAdd.Size = new Size(96, 36);
                btnAdd.BackColor = Color.FromArgb(40, 167, 69);
                btnAdd.ForeColor = Color.White;
                btnAdd.FlatStyle = FlatStyle.Flat;

                var btnEdit = new Button();
                btnEdit.Text = "编辑";
                btnEdit.Location = new Point(124, 30);
                btnEdit.Size = new Size(96, 36);
                btnEdit.BackColor = Color.FromArgb(0, 123, 255);
                btnEdit.ForeColor = Color.White;
                btnEdit.FlatStyle = FlatStyle.Flat;

                var btnToggle = new Button();
                btnToggle.Text = "启用/禁用";
                btnToggle.Location = new Point(228, 30);
                btnToggle.Size = new Size(96, 36);
                btnToggle.BackColor = Color.FromArgb(255, 193, 7);
                btnToggle.ForeColor = Color.Black;
                btnToggle.FlatStyle = FlatStyle.Flat;

                var btnDeletePackType = new Button();
                btnDeletePackType.Text = "删除";
                btnDeletePackType.Location = new Point(332, 30);
                btnDeletePackType.Size = new Size(96, 36);
                btnDeletePackType.BackColor = Color.FromArgb(220, 38, 38);
                btnDeletePackType.ForeColor = Color.White;
                btnDeletePackType.FlatStyle = FlatStyle.Flat;

                var btnRefresh = new Button();
                btnRefresh.Text = "刷新";
                btnRefresh.Location = new Point(436, 30);
                btnRefresh.Size = new Size(96, 36);
                btnRefresh.BackColor = Color.FromArgb(108, 117, 125);
                btnRefresh.ForeColor = Color.White;
                btnRefresh.FlatStyle = FlatStyle.Flat;

                grpActions.Controls.Add(btnAdd);
                grpActions.Controls.Add(btnEdit);
                grpActions.Controls.Add(btnToggle);
                grpActions.Controls.Add(btnDeletePackType);
                grpActions.Controls.Add(btnRefresh);
                tabPackTypes.Controls.Add(grpActions);

                btnRefresh.Click += (s, e) => RefreshPackTypeData();
                btnSearchPackType.Click += (s, e) => SearchPackTypesByKeyword(SettingsUiHelper.GetEffectiveText(txtSearchPackType));
                btnAdd.Click += (s, e) => AddPackType();
                btnEdit.Click += (s, e) => EditPackType();
                btnToggle.Click += (s, e) => TogglePackTypeStatusUi();
                btnDeletePackType.Click += (s, e) => DeletePackTypeItem();

                RefreshPackTypeData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreatePackTypeTab] 异常: {ex.Message}");
            }
        }

        private void CreateLocationTab()
        {
            try
            {
                Console.WriteLine(">>> [CreateLocationTab] 开始创建库位管理Tab");

                tabLocations = new TabPage();
                tabLocations.Text = "库位管理";
                tabLocations.BackColor = Color.White;
                tabLocations.Padding = new Padding(10);
                mainTabControl.TabPages.Add(tabLocations);

                var lblTitle = new Label();
                lblTitle.Text = "库位信息管理";
                lblTitle.Location = new Point(20, 20);
                lblTitle.Size = new Size(360, 28);
                lblTitle.Font = new Font("微软雅黑", 13, FontStyle.Bold);
                tabLocations.Controls.Add(lblTitle);

                var grpSearch = new GroupBox();
                grpSearch.Text = "搜索";
                grpSearch.Location = new Point(20, 60);
                grpSearch.Size = new Size(500, 76);
                grpSearch.Font = new Font("微软雅黑", 10);

                var txtSearchLocation = SettingsUiHelper.CreatePlaceholderTextBox("输入库位名称搜索");
                txtSearchLocation.Name = "txtSearchLocation";
                txtSearchLocation.Location = new Point(20, 24);

                var btnSearchLocation = new Button();
                btnSearchLocation.Text = "搜索";
                btnSearchLocation.Location = new Point(342, 22);
                btnSearchLocation.Size = new Size(92, 30);
                btnSearchLocation.BackColor = Color.FromArgb(0, 102, 204);
                btnSearchLocation.ForeColor = Color.White;
                btnSearchLocation.FlatStyle = FlatStyle.Flat;

                // 添加清空按钮
                var btnClearSearch = new Button();
                btnClearSearch.Text = "清空";
                btnClearSearch.Location = new Point(442, 24);
                btnClearSearch.Size = new Size(72, 30);
                btnClearSearch.BackColor = Color.FromArgb(149, 165, 166);
                btnClearSearch.ForeColor = Color.White;
                btnClearSearch.FlatStyle = FlatStyle.Flat;
                btnClearSearch.Click += (s, e) =>
                {
                    SettingsUiHelper.ResetPlaceholder(txtSearchLocation);
                    RefreshLocationData();
                };

                grpSearch.Controls.Add(txtSearchLocation);
                grpSearch.Controls.Add(btnSearchLocation);
                grpSearch.Controls.Add(btnClearSearch);
                tabLocations.Controls.Add(grpSearch);

                // 改用DataGridView替代ListBox，以便显示状态
                gridLocations = new DataGridView();
                gridLocations.Name = "gridLocations";
                gridLocations.Location = new Point(20, 140);
                gridLocations.Size = new Size(500, 200);
                gridLocations.ReadOnly = true;
                gridLocations.RowHeadersVisible = false;
                gridLocations.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                gridLocations.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                gridLocations.BackgroundColor = Color.White;
                gridLocations.Font = new Font("微软雅黑", 10);
                gridLocations.RowTemplate.Height = 30;
                gridLocations.AllowUserToAddRows = false;

                // 添加列
                gridLocations.Columns.Add("status", "状态");
                gridLocations.Columns.Add("name", "库位名称");
                gridLocations.Columns.Add("description", "描述");

                // 设置列宽
                gridLocations.Columns["status"].Width = 80;
                gridLocations.Columns["name"].Width = 200;
                gridLocations.Columns["description"].Width = 200;

                // 设置状态列的显示样式
                gridLocations.Columns["status"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

                SettingsUiHelper.LocalizeLocationGrid(gridLocations);
                tabLocations.Controls.Add(gridLocations);

                var grpActions = new GroupBox();
                grpActions.Text = "操作";
                grpActions.Location = new Point(20, 350);
                grpActions.Size = new Size(560, 132);
                grpActions.Font = new Font("微软雅黑", 10);

                var btnAddLocation = new Button();
                btnAddLocation.Text = "➕ 新增库位";
                btnAddLocation.Location = new Point(20, 28);
                btnAddLocation.Size = new Size(110, 40);
                btnAddLocation.BackColor = Color.FromArgb(46, 204, 113);
                btnAddLocation.ForeColor = Color.White;
                btnAddLocation.FlatStyle = FlatStyle.Flat;

                var btnToggleLocation = new Button();
                btnToggleLocation.Text = "🔄 启用/禁用";
                btnToggleLocation.Location = new Point(138, 28);
                btnToggleLocation.Size = new Size(110, 40);
                btnToggleLocation.BackColor = Color.FromArgb(255, 193, 7);
                btnToggleLocation.ForeColor = Color.Black;
                btnToggleLocation.FlatStyle = FlatStyle.Flat;

                var btnDeleteLocation = new Button();
                btnDeleteLocation.Text = "🗑️ 删除";
                btnDeleteLocation.Location = new Point(256, 28);
                btnDeleteLocation.Size = new Size(110, 40);
                btnDeleteLocation.BackColor = Color.FromArgb(220, 38, 38);
                btnDeleteLocation.ForeColor = Color.White;
                btnDeleteLocation.FlatStyle = FlatStyle.Flat;

                var btnRefreshLocation = new Button();
                btnRefreshLocation.Text = "🔄 刷新列表";
                btnRefreshLocation.Location = new Point(374, 28);
                btnRefreshLocation.Size = new Size(110, 40);
                btnRefreshLocation.BackColor = Color.FromArgb(52, 152, 219);
                btnRefreshLocation.ForeColor = Color.White;
                btnRefreshLocation.FlatStyle = FlatStyle.Flat;

                // 添加状态说明标签
                var lblStatusInfo = new Label();
                lblStatusInfo.Text = "🟢 启用状态显示为绿色背景\n🔴 禁用状态显示为红色背景";
                lblStatusInfo.Location = new Point(20, 86);
                lblStatusInfo.Size = new Size(440, 38);
                lblStatusInfo.Font = new Font("微软雅黑", 10);
                lblStatusInfo.ForeColor = Color.FromArgb(100, 100, 100);

                grpActions.Controls.Add(btnAddLocation);
                grpActions.Controls.Add(btnToggleLocation);
                grpActions.Controls.Add(btnDeleteLocation);
                grpActions.Controls.Add(btnRefreshLocation);
                grpActions.Controls.Add(lblStatusInfo);
                tabLocations.Controls.Add(grpActions);

                // 绑定事件
                btnRefreshLocation.Click += (s, e) => RefreshLocationData();
                btnSearchLocation.Click += (s, e) => SearchLocations(SettingsUiHelper.GetEffectiveText(txtSearchLocation));
                btnAddLocation.Click += (s, e) => AddLocation();
                btnToggleLocation.Click += (s, e) => ToggleLocationStatus();
                btnDeleteLocation.Click += (s, e) => DeleteLocation();

                txtSearchLocation.KeyPress += (s, e) =>
                {
                    if (e.KeyChar == (char)Keys.Enter)
                    {
                        e.Handled = true;
                        SearchLocations(SettingsUiHelper.GetEffectiveText(txtSearchLocation));
                    }
                };

                // 双击切换状态
                gridLocations.CellDoubleClick += (s, e) =>
                {
                    if (e.RowIndex >= 0)
                    {
                        ToggleLocationStatus();
                    }
                };

                RefreshLocationData();

                Console.WriteLine(">>> [CreateLocationTab] 库位管理Tab创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreateLocationTab] 异常: {ex.Message}");
            }
        }


        private void CreateHandlerTab()
        {
            try
            {
                Console.WriteLine(">>> [CreateHandlerTab] 开始创建经手人管理Tab");

                tabHandlers = new TabPage();
                tabHandlers.Text = "经手人管理";
                tabHandlers.BackColor = Color.White;
                tabHandlers.Padding = new Padding(10);
                mainTabControl.TabPages.Add(tabHandlers);

                var lblTitle = new Label();
                lblTitle.Text = "经手人信息管理";
                lblTitle.Location = new Point(20, 20);
                lblTitle.Size = new Size(400, 32);
                tabHandlers.Controls.Add(lblTitle);

                var grpSearch = new GroupBox();
                grpSearch.Text = "搜索";
                grpSearch.Location = new Point(20, 60);
                grpSearch.Size = new Size(700, 76);

                var txtSearchHandler = SettingsUiHelper.CreatePlaceholderTextBox("输入经手人名称搜索", 252);
                txtSearchHandler.Name = "txtSearchHandler";
                txtSearchHandler.Location = new Point(20, 24);

                var btnSearchHandler = new Button();
                btnSearchHandler.Text = "搜索";
                btnSearchHandler.Location = new Point(282, 22);
                btnSearchHandler.Size = new Size(92, 30);
                btnSearchHandler.BackColor = Color.FromArgb(37, 99, 235);
                btnSearchHandler.ForeColor = Color.White;
                btnSearchHandler.FlatStyle = FlatStyle.Flat;

                var btnClearHandlerSearch = new Button();
                btnClearHandlerSearch.Text = "清空";
                btnClearHandlerSearch.Location = new Point(382, 24);
                btnClearHandlerSearch.Size = new Size(72, 30);
                btnClearHandlerSearch.BackColor = Color.FromArgb(100, 116, 139);
                btnClearHandlerSearch.ForeColor = Color.White;
                btnClearHandlerSearch.FlatStyle = FlatStyle.Flat;
                btnClearHandlerSearch.Click += (s, e) =>
                {
                    SettingsUiHelper.ResetPlaceholder(txtSearchHandler);
                    RefreshHandlerData();
                };

                grpSearch.Controls.Add(txtSearchHandler);
                grpSearch.Controls.Add(btnSearchHandler);
                grpSearch.Controls.Add(btnClearHandlerSearch);
                tabHandlers.Controls.Add(grpSearch);

                gridHandlers = new DataGridView();
                gridHandlers.Name = "gridHandlers";
                gridHandlers.Location = new Point(20, 140);
                gridHandlers.Size = new Size(700, 300);
                gridHandlers.ReadOnly = true;
                gridHandlers.RowHeadersVisible = false;
                gridHandlers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                gridHandlers.MultiSelect = false;
                gridHandlers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                gridHandlers.AllowUserToAddRows = false;

                gridHandlers.Columns.Add("status", "状态");
                gridHandlers.Columns.Add("name", "经手人姓名");
                gridHandlers.Columns.Add("id", "编号");
                gridHandlers.Columns["status"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

                SettingsUiHelper.LocalizeHandlerGrid(gridHandlers);
                tabHandlers.Controls.Add(gridHandlers);

                var grpActions = new GroupBox();
                grpActions.Text = "操作";
                grpActions.Location = new Point(20, 450);
                grpActions.Size = new Size(700, 100);

                var btnAddHandler = new Button();
                btnAddHandler.Text = "➕ 新增经手人";
                btnAddHandler.Location = new Point(20, 28);
                btnAddHandler.Size = new Size(110, 40);
                btnAddHandler.BackColor = Color.FromArgb(22, 163, 74);
                btnAddHandler.ForeColor = Color.White;
                btnAddHandler.FlatStyle = FlatStyle.Flat;

                var btnEditHandler = new Button();
                btnEditHandler.Text = "✏️ 编辑";
                btnEditHandler.Location = new Point(138, 28);
                btnEditHandler.Size = new Size(96, 40);
                btnEditHandler.BackColor = Color.FromArgb(37, 99, 235);
                btnEditHandler.ForeColor = Color.White;
                btnEditHandler.FlatStyle = FlatStyle.Flat;

                var btnToggleHandler = new Button();
                btnToggleHandler.Text = "启用/禁用";
                btnToggleHandler.Location = new Point(242, 28);
                btnToggleHandler.Size = new Size(96, 40);
                btnToggleHandler.BackColor = Color.FromArgb(217, 119, 6);
                btnToggleHandler.ForeColor = Color.White;
                btnToggleHandler.FlatStyle = FlatStyle.Flat;

                var btnDeleteHandler = new Button();
                btnDeleteHandler.Text = "🗑️ 删除";
                btnDeleteHandler.Location = new Point(346, 28);
                btnDeleteHandler.Size = new Size(96, 40);
                btnDeleteHandler.BackColor = Color.FromArgb(220, 38, 38);
                btnDeleteHandler.ForeColor = Color.White;
                btnDeleteHandler.FlatStyle = FlatStyle.Flat;

                var btnRefreshHandler = new Button();
                btnRefreshHandler.Text = "🔄 刷新";
                btnRefreshHandler.Location = new Point(450, 28);
                btnRefreshHandler.Size = new Size(96, 40);
                btnRefreshHandler.BackColor = Color.FromArgb(100, 116, 139);
                btnRefreshHandler.ForeColor = Color.White;
                btnRefreshHandler.FlatStyle = FlatStyle.Flat;

                var lblHandlerHint = new Label();
                lblHandlerHint.Text = "绿色为启用，红色为禁用；双击行可快速编辑";
                lblHandlerHint.Location = new Point(20, 74);
                lblHandlerHint.Size = new Size(520, 22);
                lblHandlerHint.ForeColor = Color.FromArgb(100, 116, 139);

                grpActions.Controls.Add(btnAddHandler);
                grpActions.Controls.Add(btnEditHandler);
                grpActions.Controls.Add(btnToggleHandler);
                grpActions.Controls.Add(btnDeleteHandler);
                grpActions.Controls.Add(btnRefreshHandler);
                grpActions.Controls.Add(lblHandlerHint);
                tabHandlers.Controls.Add(grpActions);

                btnRefreshHandler.Click += (s, e) => RefreshHandlerData();
                btnSearchHandler.Click += (s, e) => SearchHandlers(SettingsUiHelper.GetEffectiveText(txtSearchHandler));
                btnAddHandler.Click += (s, e) => AddHandler();
                btnEditHandler.Click += (s, e) => EditHandler();
                btnToggleHandler.Click += (s, e) => ToggleHandlerStatus();
                btnDeleteHandler.Click += (s, e) => DeleteHandler();
                gridHandlers.CellDoubleClick += (s, e) =>
                {
                    if (e.RowIndex >= 0)
                        EditHandler();
                };

                txtSearchHandler.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        SearchHandlers(SettingsUiHelper.GetEffectiveText(txtSearchHandler));
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                };

                SettingsUiHelper.StyleTabPage(tabHandlers);
                RefreshHandlerData();

                Console.WriteLine(">>> [CreateHandlerTab] 经手人管理Tab创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreateHandlerTab] 异常: {ex.Message}");
            }
        }

        private void CreateBuyerTab()
        {
            try
            {
                Console.WriteLine(">>> [CreateBuyerTab] 开始创建买家管理Tab");

                tabBuyers = new TabPage();
                tabBuyers.Text = "买家管理";
                tabBuyers.BackColor = Color.White;
                tabBuyers.Padding = new Padding(10);
                mainTabControl.TabPages.Add(tabBuyers);

                var lblTitle = new Label();
                lblTitle.Text = "买家信息管理";
                lblTitle.Location = new Point(20, 20);
                lblTitle.Size = new Size(400, 32);
                tabBuyers.Controls.Add(lblTitle);

                var grpSearch = new GroupBox();
                grpSearch.Text = "搜索";
                grpSearch.Location = new Point(20, 60);
                grpSearch.Size = new Size(700, 76);

                var txtSearchBuyer = SettingsUiHelper.CreatePlaceholderTextBox("输入买家名称、编号或电话搜索", 252);
                txtSearchBuyer.Name = "txtSearchBuyer";
                txtSearchBuyer.Location = new Point(20, 24);

                var btnSearchBuyer = new Button();
                btnSearchBuyer.Text = "搜索";
                btnSearchBuyer.Location = new Point(282, 22);
                btnSearchBuyer.Size = new Size(92, 30);
                btnSearchBuyer.BackColor = Color.FromArgb(37, 99, 235);
                btnSearchBuyer.ForeColor = Color.White;
                btnSearchBuyer.FlatStyle = FlatStyle.Flat;

                var btnClearBuyerSearch = new Button();
                btnClearBuyerSearch.Text = "清空";
                btnClearBuyerSearch.Location = new Point(382, 24);
                btnClearBuyerSearch.Size = new Size(72, 30);
                btnClearBuyerSearch.BackColor = Color.FromArgb(100, 116, 139);
                btnClearBuyerSearch.ForeColor = Color.White;
                btnClearBuyerSearch.FlatStyle = FlatStyle.Flat;
                btnClearBuyerSearch.Click += (s, e) =>
                {
                    SettingsUiHelper.ResetPlaceholder(txtSearchBuyer);
                    RefreshBuyerData();
                };

                grpSearch.Controls.Add(txtSearchBuyer);
                grpSearch.Controls.Add(btnSearchBuyer);
                grpSearch.Controls.Add(btnClearBuyerSearch);
                tabBuyers.Controls.Add(grpSearch);

                gridBuyers = new DataGridView();
                gridBuyers.Name = "gridBuyers";
                gridBuyers.Location = new Point(20, 140);
                gridBuyers.Size = new Size(700, 300);
                gridBuyers.ReadOnly = true;
                gridBuyers.RowHeadersVisible = false;
                gridBuyers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                gridBuyers.MultiSelect = false;
                gridBuyers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                gridBuyers.AllowUserToAddRows = false;

                gridBuyers.Columns.Add("status", "状态");
                gridBuyers.Columns.Add("name", "买家名称");
                gridBuyers.Columns.Add("phone", "联系电话");
                gridBuyers.Columns.Add("code", "买家编号");
                gridBuyers.Columns["status"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

                SettingsUiHelper.LocalizeBuyerGrid(gridBuyers);
                tabBuyers.Controls.Add(gridBuyers);

                var grpActions = new GroupBox();
                grpActions.Text = "操作";
                grpActions.Location = new Point(20, 450);
                grpActions.Size = new Size(700, 100);

                var btnAddBuyer = new Button();
                btnAddBuyer.Text = "➕ 新增买家";
                btnAddBuyer.Location = new Point(20, 28);
                btnAddBuyer.Size = new Size(110, 40);
                btnAddBuyer.BackColor = Color.FromArgb(22, 163, 74);
                btnAddBuyer.ForeColor = Color.White;
                btnAddBuyer.FlatStyle = FlatStyle.Flat;

                var btnEditBuyer = new Button();
                btnEditBuyer.Text = "✏️ 编辑";
                btnEditBuyer.Location = new Point(138, 28);
                btnEditBuyer.Size = new Size(96, 40);
                btnEditBuyer.BackColor = Color.FromArgb(37, 99, 235);
                btnEditBuyer.ForeColor = Color.White;
                btnEditBuyer.FlatStyle = FlatStyle.Flat;

                var btnToggleBuyer = new Button();
                btnToggleBuyer.Text = "启用/禁用";
                btnToggleBuyer.Location = new Point(242, 28);
                btnToggleBuyer.Size = new Size(96, 40);
                btnToggleBuyer.BackColor = Color.FromArgb(217, 119, 6);
                btnToggleBuyer.ForeColor = Color.White;
                btnToggleBuyer.FlatStyle = FlatStyle.Flat;

                var btnDeleteBuyer = new Button();
                btnDeleteBuyer.Text = "🗑️ 删除";
                btnDeleteBuyer.Location = new Point(346, 28);
                btnDeleteBuyer.Size = new Size(96, 40);
                btnDeleteBuyer.BackColor = Color.FromArgb(220, 38, 38);
                btnDeleteBuyer.ForeColor = Color.White;
                btnDeleteBuyer.FlatStyle = FlatStyle.Flat;

                var btnRefreshBuyer = new Button();
                btnRefreshBuyer.Text = "🔄 刷新";
                btnRefreshBuyer.Location = new Point(450, 28);
                btnRefreshBuyer.Size = new Size(96, 40);
                btnRefreshBuyer.BackColor = Color.FromArgb(100, 116, 139);
                btnRefreshBuyer.ForeColor = Color.White;
                btnRefreshBuyer.FlatStyle = FlatStyle.Flat;

                var lblBuyerHint = new Label();
                lblBuyerHint.Text = "新增买家分两步：先填名称，再填电话（电话可跳过）；绿色为启用，红色为禁用；双击行可编辑";
                lblBuyerHint.Location = new Point(20, 74);
                lblBuyerHint.Size = new Size(520, 22);
                lblBuyerHint.ForeColor = Color.FromArgb(100, 116, 139);

                grpActions.Controls.Add(btnAddBuyer);
                grpActions.Controls.Add(btnEditBuyer);
                grpActions.Controls.Add(btnToggleBuyer);
                grpActions.Controls.Add(btnDeleteBuyer);
                grpActions.Controls.Add(btnRefreshBuyer);
                grpActions.Controls.Add(lblBuyerHint);
                tabBuyers.Controls.Add(grpActions);

                btnRefreshBuyer.Click += (s, e) => RefreshBuyerData();
                btnSearchBuyer.Click += (s, e) => SearchBuyers(SettingsUiHelper.GetEffectiveText(txtSearchBuyer));
                btnAddBuyer.Click += (s, e) => AddBuyer();
                btnEditBuyer.Click += (s, e) => EditBuyer();
                btnToggleBuyer.Click += (s, e) => ToggleBuyerStatus();
                btnDeleteBuyer.Click += (s, e) => DeleteBuyer();
                gridBuyers.CellDoubleClick += (s, e) =>
                {
                    if (e.RowIndex >= 0)
                        EditBuyer();
                };

                txtSearchBuyer.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        SearchBuyers(SettingsUiHelper.GetEffectiveText(txtSearchBuyer));
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                };

                SettingsUiHelper.StyleTabPage(tabBuyers);
                RefreshBuyerData();

                Console.WriteLine(">>> [CreateBuyerTab] 买家管理Tab创建完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [CreateBuyerTab] 异常: {ex.Message}");
            }
        }

        private void TestDataPersistence()
        {
            try
            {
                Console.WriteLine("=== 数据持久化测试开始 ===");

                var dbManager = new DatabaseManager();

                string testName = "测试持久化_" + DateTime.Now.ToString("HHmmss");
                Console.WriteLine($">>> 添加测试经手人: {testName}");
                bool addResult = dbManager.AddHandler(testName);
                Console.WriteLine($">>> 添加结果: {addResult}");

                Console.WriteLine($">>> 立即查询验证...");
                var handlers1 = dbManager.GetAllHandlersWithDetails();
                Console.WriteLine($">>> 立即查询结果: {handlers1.Rows.Count} 行");

                Console.WriteLine($">>> 创建新实例查询...");
                var dbManager2 = new DatabaseManager();
                var handlers2 = dbManager2.GetAllHandlersWithDetails();
                Console.WriteLine($">>> 新实例查询结果: {handlers2.Rows.Count} 行");

                bool found = false;
                foreach (DataRow row in handlers2.Rows)
                {
                    if (row["name"].ToString() == testName)
                    {
                        found = true;
                        Console.WriteLine($">>> 找到测试记录: ID={row["id"]}");
                        break;
                    }
                }

                if (found)
                {
                    MessageBox.Show("数据持久化测试成功！", "测试成功");
                }
                else
                {
                    MessageBox.Show("数据持久化测试失败！数据未保存。", "测试失败");
                }

                Console.WriteLine("=== 数据持久化测试结束 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> [TestDataPersistence] 异常: {ex.ToString()}");
                MessageBox.Show($"测试失败: {ex.Message}", "错误");
            }
        }
    }
}