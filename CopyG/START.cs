using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SHA3.Net;

namespace CopyG
{
    public partial class START : Form
    {
        private string[] sourceFiles;
        private string savePath;
        private CancellationTokenSource cts = new CancellationTokenSource();
        private List<string> copiedFiles = new List<string>();
        public START(string[] files, string path)
        {
            InitializeComponent();
            sourceFiles = files;
            savePath = path;
            this.Load += START_Load;
        }
        private async void START_Load(object sender, EventArgs e)
        {
            progressBar1.Minimum = 0;
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;

            OK.Enabled = false;
            CANCEL.Enabled = true;
            BFEN.Text = "0%";
            label1.Text = "Preparing to copy files...";

            try
            {
                await StartCopy();

                await StartHashingAsync();
            }
            catch (OperationCanceledException)
            {
                label1.Text = "Operation canceled";
                OK.Enabled = true;
                CANCEL.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                OK.Enabled = true;
                CANCEL.Enabled = false;
            }
        }
        private async Task StartCopy()
        {
            int count = 0;
            bool isCanceled = false;

            foreach (string line in sourceFiles)
            {
                cts.Token.ThrowIfCancellationRequested();

                string sourcePath = line.Trim().Trim('"');
                if (!File.Exists(sourcePath)) continue;

                int index = sourcePath.IndexOf(@"\BDMV\", StringComparison.OrdinalIgnoreCase);
                if (index == -1)
                    index = sourcePath.IndexOf(@"\CERTIFICATE\", StringComparison.OrdinalIgnoreCase);
                if (index == -1) continue;

                Invoke((Action)(() =>
                {
                    label1.Text = $"Copying file: {Path.GetFileName(sourcePath)} ({count + 1}/{sourceFiles.Length})";
                }));

                string relativePath = sourcePath.Substring(index + 1);
                string destinationPath = Path.Combine(savePath, relativePath);
                string directory = Path.GetDirectoryName(destinationPath);

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                try
                {
                    await CopyFileWithBufferAsync(sourcePath, destinationPath, cts.Token, count, sourceFiles.Length);
                    copiedFiles.Add(destinationPath);
                }
                catch (OperationCanceledException)
                {
                    isCanceled = true;
                    Invoke((Action)(() =>
                    {
                        CANCEL.Enabled = false;
                        BFEN.Text = "Canceled";
                        OK.Enabled = false;
                    }));
                    break;
                }
                catch (Exception ex)
                {
                    Invoke((Action)(() =>
                    {
                        MessageBox.Show($"Copy failed: {sourcePath}\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }

                count++;
                Invoke((Action)(() =>
                {
                    double percent = (double)count / sourceFiles.Length * 100;
                    progressBar1.Value = Math.Min((int)percent, 100);
                    BFEN.Text = $"{percent:0}%";
                }));
            }

            Invoke((Action)(() =>
            {
                if (!isCanceled)
                {
                    label1.Text = "Copy complete, preparing to calculate hashes...";
                    progressBar1.Value = 100;
                    BFEN.Text = "100%";
                    CANCEL.Enabled = false;
                }
                else
                {
                    label1.Text = "Copy canceled";
                    CANCEL.Enabled = false;
                }
            }));

            if (isCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        private async Task CopyFileWithBufferAsync(string sourcePath, string destinationPath, CancellationToken token, int fileIndex, int totalFiles, int bufferSize = 4 * 1024 * 1024)
        {
            try
            {
                using (FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
                using (FileStream destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
                {
                    byte[] buffer = new byte[bufferSize];
                    long totalBytes = sourceStream.Length;
                    long copiedBytes = 0;

                    while (true)
                    {
                        int bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0) break;

                        await destinationStream.WriteAsync(buffer, 0, bytesRead, token);
                        copiedBytes += bytesRead;

                        double overallProgress = (fileIndex + (double)copiedBytes / totalBytes) / totalFiles * 100;

                        BeginInvoke(new Action(() =>
                        {
                            progressBar1.Value = Math.Min((int)(overallProgress), 100);
                            BFEN.Text = $"{overallProgress:0}%";
                        }));
                    }
                }

                File.SetAttributes(destinationPath, FileAttributes.Normal);
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                throw;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying file: {sourcePath}\nDetails: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private async Task StartHashingAsync()
        {
            label1.Text = "Starting hash calculation...";
            progressBar1.Minimum = 0;
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;

            OK.Enabled = false;
            CANCEL.Enabled = false;

            string shaFilePath = Path.Combine(savePath, "SHA.txt");
            List<string> allHashes = new List<string>();

            int index = 0;
            foreach (var file in copiedFiles)
            {
                label1.Text = $"Calculating hash: {Path.GetFileName(file)} ({index + 1}/{copiedFiles.Count})";

                var progress = new Progress<double>(percent =>
                {
                    progressBar1.Value = Math.Min((int)percent, 100);
                    BFEN.Text = $"Hashing: {percent:0}%";
                });

                try
                {
                    var hashes = await GenerateHashesAsync(file, progress);
                    allHashes.AddRange(hashes);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hash generation failed: {file}\n{ex.Message}", "Hash Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                progressBar1.Value = 100;
                BFEN.Text = "Hashing: 100%";
            }

            try
            {
                File.WriteAllLines(shaFilePath, allHashes, Encoding.UTF8);
                label1.Text = "Hash Calculation Completed";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to write SHA.txt: {ex.Message}", "Write Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                label1.Text = "Failed to write hash file";
            }

            OK.Enabled = true;
        }
        private async Task<List<string>> GenerateHashesAsync(string filePath, IProgress<double> progress = null)
        {
            return await Task.Run(() =>
            {
                var fileName = Path.GetFileName(filePath);
                var results = new List<string>();

                using (var md5 = MD5.Create())
                using (var sha1 = SHA1.Create())
                using (var sha256 = SHA256.Create())
                using (var sha3_256 = Sha3.Sha3256())
                using (var sha3_512 = Sha3.Sha3512())
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[1024 * 1024];
                    int bytesRead;
                    long totalRead = 0;
                    long totalLength = fs.Length;

                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha3_256.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha3_512.TransformBlock(buffer, 0, bytesRead, null, 0);

                        totalRead += bytesRead;
                        double percent = (double)totalRead / totalLength * 100;

                        progress?.Report(percent);
                    }

                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha3_256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha3_512.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                    string md5Hex = BitConverter.ToString(md5.Hash).Replace("-", "").ToUpperInvariant();
                    string sha1Hex = BitConverter.ToString(sha1.Hash).Replace("-", "").ToUpperInvariant();
                    string sha256Hex = BitConverter.ToString(sha256.Hash).Replace("-", "").ToUpperInvariant();
                    string sha3_256Hex = BitConverter.ToString(sha3_256.Hash).Replace("-", "").ToUpperInvariant();
                    string sha3_512Hex = BitConverter.ToString(sha3_512.Hash).Replace("-", "").ToUpperInvariant();

                    results.Add($"#MD5 *{md5Hex} *{fileName}");
                    results.Add($"#SHA-1 *{sha1Hex} *{fileName}");
                    results.Add($"#SHA-256 *{sha256Hex} *{fileName}");
                    results.Add($"#SHA3-256 *{sha3_256Hex} *{fileName}");
                    results.Add($"#SHA3-512 *{sha3_512Hex} *{fileName}");
                }

                return results;
            });
        }
        private string ComputeHash(string filePath, HashAlgorithm hashAlgorithm)
        {
            using (hashAlgorithm)
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[1024 * 1024];
                int bytesRead;
                long totalRead = 0;
                long totalLength = stream.Length;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalRead += bytesRead;

                    double percent = totalRead * 100 / totalLength;
                    BeginInvoke(new Action(() =>
                    {
                        BFEN.Text = $"Hashing: {percent:0}%";
                    }));
                }

                hashAlgorithm.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hashAlgorithm.Hash).Replace("-", "").ToUpperInvariant();
            }
        }
        private void CANCEL_Click(object sender, EventArgs e)
        {
            cts.Cancel();
            CANCEL.Enabled = false;
        }
        private void OK_Click(object sender, EventArgs e)
        {
            this.Close();
        }

    }
}
