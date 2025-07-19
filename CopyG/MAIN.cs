using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CopyG
{
    public partial class MAIN : Form
    {
        public MAIN()
        {
            InitializeComponent();
            textBox2.MouseDoubleClick += textBox2_MouseDoubleClick;
            textBox1.Text = Properties.Settings.Default.ini;
        }
        private void button1_Click(object sender, EventArgs e)
        {
            string[] lines = textBox1.Text
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            string savePath = textBox2.Text.Trim();

            if (lines.Length == 0)
            {
                MessageBox.Show("Source file path cannot be empty!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                MessageBox.Show("Save path cannot be empty!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            START startForm = new START(lines, savePath);
            startForm.ShowDialog();
        }
        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ini = textBox1.Text;
            Properties.Settings.Default.Save();
        }
        private void textBox2_MouseDoubleClick(object sender, EventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            dialog.Title = "Save Output Folder";
            dialog.InitialDirectory = "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                textBox2.Text = dialog.FileName;
            }
        }
    }
}
