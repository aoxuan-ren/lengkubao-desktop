// 在您的项目中添加一个新的 BackupForm.cs
using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;
using System.Drawing;

namespace lengkubao.desktop
{
    public class BackupForm : Form
    {
        private ListBox listBackups;
        private Button btnCreateBackup;
        private Button btnRestoreBackup;
        private Button btnDeleteBackup;
        private Button btnOpenFolder;
        private Button btnClose;
        private Label lblInfo;

        public BackupForm()
        {
            InitializeForm();
            LoadBackupList();
        }

        private void InitializeForm()
        {
            this.Text = "数据备份与恢复";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.Font = new Font("微软雅黑", 9);

            // 标题
            lblInfo = new Label();
            lblInfo.Text = "数据备份管理";
            lblInfo.Location = new Point(20, 20);
            lblInfo.Size = new Size(300, 25);
            lblInfo.Font = new Font("微软雅黑", 12, FontStyle.Bold);
            this.Controls.Add(lblInfo);

            // 备份列表
            Label lblList = new Label();
            lblList.Text = "现有备份文件：";
            lblList.Location = new Point(20, 60);
            lblList.Size = new Size(200, 25);
            this.Controls.Add(lblList);

            listBackups = new ListBox();
            listBackups.Location = new Point(20, 90);
            listBackups.Size = new Size(550, 250);
            listBackups.Font = new Font("微软雅黑", 9);
            this.Controls.Add(listBackups);

            // 创建备份按钮
            btnCreateBackup = new Button();
            btnCreateBackup.Text = "创建新备份";
            btnCreateBackup.Location = new Point(20, 360);
            btnCreateBackup.Size = new Size(120, 35);
            btnCreateBackup.BackColor = Color.FromArgb(46, 204, 113);
            btnCreateBackup.ForeColor = Color.White;
            btnCreateBackup.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            btnCreateBackup.Click += BtnCreateBackup_Click;
            this.Controls.Add(btnCreateBackup);

            // 恢复备份按钮
            btnRestoreBackup = new Button();
            btnRestoreBackup.Text = "恢复选中备份";
            btnRestoreBackup.Location = new Point(150, 360);
            btnRestoreBackup.Size = new Size(120, 35);
            btnRestoreBackup.BackColor = Color.FromArgb(52, 152, 219);
            btnRestoreBackup.ForeColor = Color.White;
            btnRestoreBackup.Click += BtnRestoreBackup_Click;
            this.Controls.Add(btnRestoreBackup);

            // 删除备份按钮
            btnDeleteBackup = new Button();
            btnDeleteBackup.Text = "删除选中备份";
            btnDeleteBackup.Location = new Point(280, 360);
            btnDeleteBackup.Size = new Size(120, 35);
            btnDeleteBackup.BackColor = Color.FromArgb(231, 76, 60);
            btnDeleteBackup.ForeColor = Color.White;
            btnDeleteBackup.Click += BtnDeleteBackup_Click;
            this.Controls.Add(btnDeleteBackup);

            // 打开文件夹按钮
            btnOpenFolder = new Button();
            btnOpenFolder.Text = "打开备份文件夹";
            btnOpenFolder.Location = new Point(410, 360);
            btnOpenFolder.Size = new Size(160, 35);
            btnOpenFolder.Click += BtnOpenFolder_Click;
            this.Controls.Add(btnOpenFolder);

            // 关闭按钮
            btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(470, 410);
            btnClose.Size = new Size(100, 35);
            btnClose.BackColor = Color.FromArgb(149, 165, 166);
            btnClose.ForeColor = Color.White;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private void LoadBackupList()
        {
            try
            {
                listBackups.Items.Clear();

                string backupDir = Path.Combine(Application.StartupPath, "Backups");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    listBackups.Items.Add("（暂无备份文件）");
                    return;
                }

                var backupFiles = Directory.GetFiles(backupDir, "*.zip");
                Array.Sort(backupFiles);
                Array.Reverse(backupFiles); // 最新的在前面

                if (backupFiles.Length == 0)
                {
                    listBackups.Items.Add("（暂无备份文件）");
                }
                else
                {
                    foreach (var file in backupFiles)
                    {
                        FileInfo info = new FileInfo(file);
                        string displayText = $"{Path.GetFileNameWithoutExtension(file)} " +
                                           $"[{info.CreationTime:yyyy-MM-dd HH:mm}] " +
                                           $"({FormatFileSize(info.Length)})";
                        listBackups.Items.Add(displayText);
                        listBackups.Tag = listBackups.Items.Count - 1; // 保存索引
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载备份列表失败：{ex.Message}", "错误");
            }
        }

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

        private void BtnCreateBackup_Click(object sender, EventArgs e)
        {
            try
            {
                // 创建备份目录
                string backupDir = Path.Combine(Application.StartupPath, "Backups");
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                // 生成备份文件名
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupName = $"数据备份_{timestamp}";
                string zipFilePath = Path.Combine(backupDir, $"{backupName}.zip");

                // 显示进度
                using (ProgressForm progressForm = new ProgressForm("正在备份数据..."))
                {
                    progressForm.Show();
                    Application.DoEvents();

                    // 查找数据文件
                    string[] dataExtensions = { "*.db", "*.sqlite", "*.mdf", "*.accdb", "*.xml", "*.json" };

                    // 创建临时目录
                    string tempDir = Path.Combine(Path.GetTempPath(), $"backup_temp_{timestamp}");
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        // 备份所有数据文件
                        foreach (string extension in dataExtensions)
                        {
                            var files = Directory.GetFiles(Application.StartupPath, extension);
                            foreach (var file in files)
                            {
                                string destFile = Path.Combine(tempDir, Path.GetFileName(file));
                                File.Copy(file, destFile, true);
                            }
                        }

                        // 备份Data目录（如果存在）
                        string dataDir = Path.Combine(Application.StartupPath, "Data");
                        if (Directory.Exists(dataDir))
                        {
                            string dataBackupDir = Path.Combine(tempDir, "Data");
                            CopyDirectory(dataDir, dataBackupDir);
                        }

                        // 创建备份信息文件
                        string infoFile = Path.Combine(tempDir, "backup_info.txt");
                        File.WriteAllText(infoFile,
                            $"备份名称：{backupName}\n" +
                            $"备份时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                            $"应用程序：{Application.ProductName}\n" +
                            $"版本：{Application.ProductVersion}\n" +
                            $"用户：{Environment.UserName}");

                        // 压缩备份
                        ZipFile.CreateFromDirectory(tempDir, zipFilePath);

                        // 清理旧备份（保留最近10个）
                        CleanOldBackups(backupDir, 10);
                    }
                    finally
                    {
                        // 清理临时文件
                        if (Directory.Exists(tempDir))
                            Directory.Delete(tempDir, true);
                    }

                    progressForm.Close();
                }

                MessageBox.Show($"备份创建成功！\n\n文件：{backupName}.zip", "备份成功");
                LoadBackupList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"备份失败：{ex.Message}", "错误");
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

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

        private void CleanOldBackups(string backupDir, int keepCount)
        {
            try
            {
                var backupFiles = Directory.GetFiles(backupDir, "*.zip");
                if (backupFiles.Length <= keepCount)
                    return;

                // 按创建时间排序，删除最旧的
                Array.Sort(backupFiles, (a, b) =>
                    File.GetCreationTime(a).CompareTo(File.GetCreationTime(b)));

                for (int i = 0; i < backupFiles.Length - keepCount; i++)
                {
                    File.Delete(backupFiles[i]);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        private void BtnRestoreBackup_Click(object sender, EventArgs e)
        {
            if (listBackups.SelectedIndex < 0 || listBackups.Items[0].ToString().Contains("（暂无备份文件）"))
            {
                MessageBox.Show("请先选择一个备份文件", "提示");
                return;
            }

            DialogResult result = MessageBox.Show(
                "警告：恢复备份将覆盖现有数据！\n\n请确保已关闭所有其他程序窗口。\n确定要恢复吗？",
                "确认恢复",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            try
            {
                string backupDir = Path.Combine(Application.StartupPath, "Backups");
                var backupFiles = Directory.GetFiles(backupDir, "*.zip");

                if (backupFiles.Length == 0)
                    return;

                Array.Sort(backupFiles);
                Array.Reverse(backupFiles);

                string selectedFile = backupFiles[listBackups.SelectedIndex];

                using (ProgressForm progressForm = new ProgressForm("正在恢复数据..."))
                {
                    progressForm.Show();
                    Application.DoEvents();

                    // 创建临时目录
                    string tempDir = Path.Combine(Path.GetTempPath(), $"restore_temp_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        // 解压备份文件
                        ZipFile.ExtractToDirectory(selectedFile, tempDir);

                        // 恢复文件
                        foreach (string file in Directory.GetFiles(tempDir))
                        {
                            string fileName = Path.GetFileName(file);
                            string destFile = Path.Combine(Application.StartupPath, fileName);

                            // 备份原文件
                            if (File.Exists(destFile))
                            {
                                string backup = Path.Combine(backupDir, $"恢复前备份_{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}");
                                File.Copy(destFile, backup);
                            }

                            // 恢复文件
                            File.Copy(file, destFile, true);
                        }

                        // 恢复Data目录
                        string sourceDataDir = Path.Combine(tempDir, "Data");
                        string destDataDir = Path.Combine(Application.StartupPath, "Data");

                        if (Directory.Exists(sourceDataDir))
                        {
                            if (Directory.Exists(destDataDir))
                                Directory.Delete(destDataDir, true);

                            CopyDirectory(sourceDataDir, destDataDir);
                        }

                        progressForm.Close();
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir))
                            Directory.Delete(tempDir, true);
                    }
                }

                MessageBox.Show("数据恢复成功！\n\n请重启应用程序以使更改生效。", "恢复成功");
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"恢复失败：{ex.Message}", "错误");
            }
        }

        private void BtnDeleteBackup_Click(object sender, EventArgs e)
        {
            if (listBackups.SelectedIndex < 0 || listBackups.Items[0].ToString().Contains("（暂无备份文件）"))
            {
                MessageBox.Show("请先选择一个备份文件", "提示");
                return;
            }

            DialogResult result = MessageBox.Show(
                "确定要删除选中的备份文件吗？",
                "确认删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    string backupDir = Path.Combine(Application.StartupPath, "Backups");
                    var backupFiles = Directory.GetFiles(backupDir, "*.zip");

                    if (backupFiles.Length == 0)
                        return;

                    Array.Sort(backupFiles);
                    Array.Reverse(backupFiles);

                    string selectedFile = backupFiles[listBackups.SelectedIndex];
                    File.Delete(selectedFile);

                    MessageBox.Show("备份文件已删除", "提示");
                    LoadBackupList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败：{ex.Message}", "错误");
                }
            }
        }
        
        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                string backupDir = Path.Combine(Application.StartupPath, "Backups");

                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                System.Diagnostics.Process.Start("explorer.exe", backupDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开文件夹失败：{ex.Message}", "错误");
            }
        }
    }

    // 进度显示窗口
    public class ProgressForm : Form
    {
        private Label lblMessage;
        private ProgressBar progressBar;

        public ProgressForm(string message)
        {
            InitializeForm(message);
        }

        private void InitializeForm(string message)
        {
            this.Text = "请稍候";
            this.Size = new Size(400, 150);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ControlBox = false;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            lblMessage = new Label();
            lblMessage.Text = message;
            lblMessage.Location = new Point(20, 20);
            lblMessage.Size = new Size(360, 30);
            lblMessage.TextAlign = ContentAlignment.MiddleCenter;
            lblMessage.Font = new Font("微软雅黑", 10);
            this.Controls.Add(lblMessage);

            progressBar = new ProgressBar();
            progressBar.Location = new Point(20, 70);
            progressBar.Size = new Size(360, 30);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 30;
            this.Controls.Add(progressBar);
        }
    }
}