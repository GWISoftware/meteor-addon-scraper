namespace AddonScraper
{
    partial class Form1
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
            this.logBox = new System.Windows.Forms.RichTextBox();
            this.scrapeNow = new System.Windows.Forms.Button();
            this.pgBar = new System.Windows.Forms.ProgressBar();
            this.SuspendLayout();
            // 
            // logBox
            // 
            this.logBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.logBox.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.logBox.Location = new System.Drawing.Point(0, 77);
            this.logBox.Name = "logBox";
            this.logBox.Size = new System.Drawing.Size(477, 336);
            this.logBox.TabIndex = 3;
            this.logBox.Text = "";
            // 
            // scrapeNow
            // 
            this.scrapeNow.Font = new System.Drawing.Font("Microsoft JhengHei", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.scrapeNow.Location = new System.Drawing.Point(12, 12);
            this.scrapeNow.Name = "scrapeNow";
            this.scrapeNow.Size = new System.Drawing.Size(453, 30);
            this.scrapeNow.TabIndex = 4;
            this.scrapeNow.Text = "Scrape Now";
            this.scrapeNow.UseVisualStyleBackColor = true;
            this.scrapeNow.Click += new System.EventHandler(this.scrapeNow_Click);
            // 
            // pgBar
            // 
            this.pgBar.Location = new System.Drawing.Point(24, 48);
            this.pgBar.Name = "pgBar";
            this.pgBar.Size = new System.Drawing.Size(431, 23);
            this.pgBar.TabIndex = 5;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(477, 413);
            this.Controls.Add(this.pgBar);
            this.Controls.Add(this.scrapeNow);
            this.Controls.Add(this.logBox);
            this.Name = "Form1";
            this.Text = "Form1";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.ProgressBar pgBar;

        private System.Windows.Forms.RichTextBox logBox;
        private System.Windows.Forms.Button scrapeNow;

        #endregion
    }
}