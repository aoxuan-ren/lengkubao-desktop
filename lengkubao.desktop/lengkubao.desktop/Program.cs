using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    internal static class Program
    {
       
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Mutex instanceMutex;
            if (!SingleInstanceHelper.TryAcquireMutex(out instanceMutex))
            {
                SingleInstanceHelper.SignalExistingInstance();
                return;
            }

            try
            {
                SingleInstanceHelper.PrepareOwnerInstance();
                RunApplication();
            }
            finally
            {
                instanceMutex?.ReleaseMutex();
                instanceMutex?.Dispose();
            }
        }

        private static void RunApplication()
        {
            // 添加调试信息
            Console.WriteLine($"程序启动时间: {DateTime.Now:HH:mm:ss.fff}");
            Console.WriteLine($"进程ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            FiscalYearService.Initialize();
            // 检查并添加数据库字段
            DatabaseMigrationHelper.CheckAndAddSettleFields();
            // 更新旧记录
            DatabaseMigrationHelper.UpdateExistingRecords();
            DatabaseMigrationHelper.EnsureYearEndTables();
            if (!LicenseManager.IsLicensed())
            {
                using (var activation = new LicenseActivationForm())
                {
                    if (activation.ShowDialog() != DialogResult.OK)
                        return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (UpdateChecker.TryCheckAndApplyUpdate())
                return;

            Application.Run(new Form1());
        }
    }
}
