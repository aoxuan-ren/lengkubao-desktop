using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

public enum BackupType
{
    Simple,     // 简单备份（单个文件）
    Full,       // 完整备份（压缩包）
    Auto        // 自动备份
}

public class BackupOptionsDialog : Form
{
    private RadioButton rbSimple;
    private RadioButton rbFull;
    private RadioButton rbAuto;
    private Button btnOk;
    private Button btnCancel;

    public BackupType BackupType { get; private set; }

    public BackupOptionsDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "选择备份方式";
        this.Size = new Size(400, 250);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;

        Label lblTitle = new Label();
        lblTitle.Text = "请选择备份方式：";
        lblTitle.Location = new Point(20, 20);
        lblTitle.Size = new Size(350, 25);
        lblTitle.Font = new Font("微软雅黑", 10, FontStyle.Bold);
        this.Controls.Add(lblTitle);

        // 简单备份选项
        rbSimple = new RadioButton();
        rbSimple.Text = "简单备份（只备份数据库文件）";
        rbSimple.Location = new Point(40, 60);
        rbSimple.Size = new Size(300, 25);
        rbSimple.Checked = true;
        this.Controls.Add(rbSimple);

        Label lblSimpleDesc = new Label();
        lblSimpleDesc.Text = "• 快速备份单个数据库文件\n• 需要手动选择保存位置";
        lblSimpleDesc.Location = new Point(60, 85);
        lblSimpleDesc.Size = new Size(300, 40);
        lblSimpleDesc.Font = new Font("微软雅黑", 9);
        this.Controls.Add(lblSimpleDesc);

        // 完整备份选项
        rbFull = new RadioButton();
        rbFull.Text = "完整备份（备份所有数据）";
        rbFull.Location = new Point(40, 130);
        rbFull.Size = new Size(300, 25);
        this.Controls.Add(rbFull);

        Label lblFullDesc = new Label();
        lblFullDesc.Text = "• 备份数据库、配置文件和日志\n• 自动保存到备份文件夹";
        lblFullDesc.Location = new Point(60, 155);
        lblFullDesc.Size = new Size(300, 40);
        lblFullDesc.Font = new Font("微软雅黑", 9);
        this.Controls.Add(lblFullDesc);

        // 自动备份选项
        rbAuto = new RadioButton();
        rbAuto.Text = "自动备份（推荐）";
        rbAuto.Location = new Point(40, 200);
        rbAuto.Size = new Size(300, 25);
        this.Controls.Add(rbAuto);

        Label lblAutoDesc = new Label();
        lblAutoDesc.Text = "• 自动创建带时间戳的备份\n• 自动清理旧备份文件";
        lblAutoDesc.Location = new Point(60, 225);
        lblAutoDesc.Size = new Size(300, 40);
        lblAutoDesc.Font = new Font("微软雅黑", 9);
        this.Controls.Add(lblAutoDesc);

        // 按钮
        btnOk = new Button();
        btnOk.Text = "确定";
        btnOk.Location = new Point(200, 280);
        btnOk.Size = new Size(80, 30);
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Click += (s, e) =>
        {
            if (rbSimple.Checked) BackupType = BackupType.Simple;
            else if (rbFull.Checked) BackupType = BackupType.Full;
            else if (rbAuto.Checked) BackupType = BackupType.Auto;
        };
        this.Controls.Add(btnOk);

        btnCancel = new Button();
        btnCancel.Text = "取消";
        btnCancel.Location = new Point(290, 280);
        btnCancel.Size = new Size(80, 30);
        btnCancel.DialogResult = DialogResult.Cancel;
        this.Controls.Add(btnCancel);

        this.Height = 350; // 调整高度
    }
}
// 1. 简单备份（您原有的功能）
