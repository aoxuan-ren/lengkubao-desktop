namespace lengkubao.desktop
{
    partial class InboundForm
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
            this.label1 = new System.Windows.Forms.Label();
            this.textclientcode = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.textclientname = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.combospec = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.numericquantity = new System.Windows.Forms.NumericUpDown();
            this.btnsave = new System.Windows.Forms.Button();
            this.btncancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numericquantity)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(78, 79);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(80, 18);
            this.label1.TabIndex = 0;
            this.label1.Text = "客户编号";
            // 
            // textclientcode
            // 
            this.textclientcode.Location = new System.Drawing.Point(262, 79);
            this.textclientcode.Name = "textclientcode";
            this.textclientcode.Size = new System.Drawing.Size(100, 28);
            this.textclientcode.TabIndex = 1;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(78, 138);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(80, 18);
            this.label2.TabIndex = 2;
            this.label2.Text = "客户名称";
            // 
            // textclientname
            // 
            this.textclientname.Location = new System.Drawing.Point(262, 138);
            this.textclientname.Name = "textclientname";
            this.textclientname.Size = new System.Drawing.Size(100, 28);
            this.textclientname.TabIndex = 3;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(81, 195);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(80, 18);
            this.label3.TabIndex = 4;
            this.label3.Text = "商品规格";
            // 
            // combospec
            // 
            this.combospec.FormattingEnabled = true;
            this.combospec.Location = new System.Drawing.Point(262, 186);
            this.combospec.Name = "combospec";
            this.combospec.Size = new System.Drawing.Size(121, 26);
            this.combospec.TabIndex = 5;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(84, 241);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(80, 18);
            this.label4.TabIndex = 6;
            this.label4.Text = "入库数量";
            this.label4.Click += new System.EventHandler(this.label4_Click_1);
            // 
            // numericquantity
            // 
            this.numericquantity.Location = new System.Drawing.Point(262, 241);
            this.numericquantity.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.numericquantity.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericquantity.Name = "numericquantity";
            this.numericquantity.Size = new System.Drawing.Size(120, 28);
            this.numericquantity.TabIndex = 7;
            this.numericquantity.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // btnsave
            // 
            this.btnsave.Location = new System.Drawing.Point(87, 320);
            this.btnsave.Name = "btnsave";
            this.btnsave.Size = new System.Drawing.Size(97, 36);
            this.btnsave.TabIndex = 8;
            this.btnsave.Text = "保存记录";
            this.btnsave.UseVisualStyleBackColor = true;
            this.btnsave.Click += new System.EventHandler(this.btnsave_Click_1);
            // 
            // btncancel
            // 
            this.btncancel.Location = new System.Drawing.Point(276, 332);
            this.btncancel.Name = "btncancel";
            this.btncancel.Size = new System.Drawing.Size(75, 23);
            this.btncancel.TabIndex = 9;
            this.btncancel.Text = "取消";
            this.btncancel.UseVisualStyleBackColor = true;
            // 
            // InboundForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.btncancel);
            this.Controls.Add(this.btnsave);
            this.Controls.Add(this.numericquantity);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.combospec);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.textclientname);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.textclientcode);
            this.Controls.Add(this.label1);
            this.Name = "InboundForm";
            this.Text = "InboundForm";
            ((System.ComponentModel.ISupportInitialize)(this.numericquantity)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textclientcode;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox textclientname;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox combospec;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown numericquantity;
        private System.Windows.Forms.Button btnsave;
        private System.Windows.Forms.Button btncancel;
    }
}