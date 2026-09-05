namespace lengkubao.desktop
{
    partial class DashboardForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.labelTodayCount = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // labelTodayCount
            // 
            this.labelTodayCount.Font = new System.Drawing.Font("宋体", 14F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.labelTodayCount.Location = new System.Drawing.Point(277, 182);
            this.labelTodayCount.Name = "labelTodayCount";
            this.labelTodayCount.Size = new System.Drawing.Size(230, 40);
            this.labelTodayCount.TabIndex = 0;
            this.labelTodayCount.Text = "今日入库:--箱";
            // 
            // DashboardForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.labelTodayCount);
            this.Name = "DashboardForm";
            this.Text = "今日入库：--箱";
            this.Load += new System.EventHandler(this.DashboardForm_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label labelTodayCount;
    }
}