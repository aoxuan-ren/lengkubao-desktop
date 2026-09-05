using System;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }
        #region 自定义控件声明
        private System.Windows.Forms.Panel sidePanel;
        private System.Windows.Forms.Label lblServiceStatus;
        private System.Windows.Forms.Label lblOnlineCount;
        private System.Windows.Forms.Button btnStartSync;
        private System.Windows.Forms.Button btnStopSync;
        private System.Windows.Forms.CheckBox chkAutoStartSync;
        private System.Windows.Forms.ListView lvSyncClients;
        private System.Windows.Forms.ListView lvPendingConfig;
        private System.Windows.Forms.Button btnSyncPendingConfig;
        private System.Windows.Forms.Label lblPendingConfigSummary;
        private System.Windows.Forms.RichTextBox txtSyncLog;
        private System.Windows.Forms.Label lblPortInfo;
        private System.Windows.Forms.Label lblSyncActivity;
        private System.Windows.Forms.ProgressBar pbSyncProgress;
        #endregion
        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.button5 = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.button7 = new System.Windows.Forms.Button();
            this.button8 = new System.Windows.Forms.Button();
            this.btnDiagnostic = new System.Windows.Forms.Button();
            this.mainMenuStrip = new System.Windows.Forms.MenuStrip();
            this.数据库ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.repairMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.mainMenuStrip.SuspendLayout();
            this.SuspendLayout();

            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(75, 105);
            this.button1.Margin = new System.Windows.Forms.Padding(4);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(180, 60);  // 从 150,45 改为 180,60
            this.button1.TabIndex = 9;
            this.button1.Text = "入库查询";
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(75, 180);  // 调整 Y 坐标以适应更大的按钮
            this.button2.Margin = new System.Windows.Forms.Padding(4);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(180, 60);  // 从 150,45 改为 180,60
            this.button2.TabIndex = 8;
            this.button2.Text = "入库开单";
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(75, 255);  // 调整 Y 坐标以适应更大的按钮
            this.button3.Margin = new System.Windows.Forms.Padding(4);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(180, 60);  // 从 150,45 改为 180,60
            this.button3.TabIndex = 7;
            this.button3.Text = "客户报账";
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // button4
            // 
            this.button4.Location = new System.Drawing.Point(75, 330);  // 调整 Y 坐标以适应更大的按钮
            this.button4.Margin = new System.Windows.Forms.Padding(4);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(150, 45);
            this.button4.TabIndex = 6;
            this.button4.Text = "报账查询";
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // button5
            // 
            this.button5.Location = new System.Drawing.Point(75, 390);
            this.button5.Margin = new System.Windows.Forms.Padding(4);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(150, 45);
            this.button5.TabIndex = 5;
            this.button5.Text = "包装记账";
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // button6
            // 
            this.button6.Location = new System.Drawing.Point(75, 450);
            this.button6.Margin = new System.Windows.Forms.Padding(4);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(150, 45);
            this.button6.TabIndex = 4;
            this.button6.Text = "包装查询";
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button7
            // 
            this.button7.Location = new System.Drawing.Point(75, 510);
            this.button7.Margin = new System.Windows.Forms.Padding(4);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(150, 45);
            this.button7.TabIndex = 3;
            this.button7.Text = "客户对账";
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // button8
            // 
            this.button8.Location = new System.Drawing.Point(75, 570);
            this.button8.Margin = new System.Windows.Forms.Padding(4);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(150, 45);
            this.button8.TabIndex = 2;
            this.button8.Text = "系统设置";
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // btnDiagnostic
            // 
            this.btnDiagnostic.Location = new System.Drawing.Point(300, 105);
            this.btnDiagnostic.Margin = new System.Windows.Forms.Padding(4);
            this.btnDiagnostic.Name = "btnDiagnostic";
            this.btnDiagnostic.Size = new System.Drawing.Size(150, 45);
            this.btnDiagnostic.TabIndex = 0;
            this.btnDiagnostic.Text = "系统诊断";
            this.btnDiagnostic.Click += new System.EventHandler(this.btnDiagnostic_Click);
            // 
            // mainMenuStrip
            // 
            this.mainMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.mainMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
    this.数据库ToolStripMenuItem});
            this.mainMenuStrip.Location = new System.Drawing.Point(0, 0);
            this.mainMenuStrip.Name = "mainMenuStrip";
            this.mainMenuStrip.Padding = new System.Windows.Forms.Padding(9, 3, 0, 3);
            this.mainMenuStrip.Size = new System.Drawing.Size(1200, 35);
            this.mainMenuStrip.TabIndex = 10;
            this.mainMenuStrip.Text = "menuStrip1";

            
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 660);
            this.Controls.Add(this.btnDiagnostic);
            this.Controls.Add(this.button8);
            this.Controls.Add(this.button7);
            this.Controls.Add(this.button6);
            this.Controls.Add(this.button5);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.mainMenuStrip);
            this.MainMenuStrip = this.mainMenuStrip;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "Form1";
            this.Text = "冷库宝管理系统";
            this.mainMenuStrip.ResumeLayout(false);
            this.mainMenuStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button7;
        private System.Windows.Forms.Button button8;
        private System.Windows.Forms.Button btnDiagnostic;
        private System.Windows.Forms.MenuStrip mainMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem 数据库ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem repairMenuItem;
    }
}

