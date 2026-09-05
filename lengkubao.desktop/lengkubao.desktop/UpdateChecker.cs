using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace lengkubao.desktop
{
    internal static class UpdateChecker
    {
        private const int CheckTimeoutSeconds = 15;
        private const int DownloadTimeoutMinutes = 30;

        private sealed class UpdateManifest
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("downloadUrl")]
            public string DownloadUrl { get; set; }

            [JsonProperty("releaseNotes")]
            public string ReleaseNotes { get; set; }

            [JsonProperty("mandatory")]
            public bool Mandatory { get; set; }
        }

        /// <summary>
        /// 永久授权用户：检查更新并在用户确认后下载、启动安装包。
        /// 返回 true 表示已启动安装程序，调用方应退出当前进程。
        /// </summary>
        public static bool TryCheckAndApplyUpdate()
        {
            if (!LicenseManager.IsPermanentLicensed())
                return false;

            string manifestUrl = ConfigurationManager.AppSettings["UpdateManifestUrl"];
            if (string.IsNullOrWhiteSpace(manifestUrl))
                return false;

            try
            {
                UpdateManifest manifest = FetchManifest(manifestUrl);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                    return false;

                Version currentVersion = GetCurrentVersion();
                if (!Version.TryParse(manifest.Version.Trim(), out Version remoteVersion))
                    return false;

                if (remoteVersion <= currentVersion)
                    return false;

                return PromptDownloadAndInstall(manifest, currentVersion, remoteVersion);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("更新检查失败: " + ex.Message);
                return false;
            }
        }

        private static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        }

        private static UpdateManifest FetchManifest(string manifestUrl)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(CheckTimeoutSeconds);
                string json = client.GetStringAsync(manifestUrl).GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<UpdateManifest>(json);
            }
        }

        private static bool PromptDownloadAndInstall(UpdateManifest manifest, Version currentVersion, Version remoteVersion)
        {
            while (true)
            {
                string notes = string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
                    ? "建议尽快更新以获得最新功能与修复。"
                    : manifest.ReleaseNotes.Trim();

                string message =
                    "发现新版本：" + remoteVersion + Environment.NewLine +
                    "当前版本：" + currentVersion + Environment.NewLine + Environment.NewLine +
                    notes + Environment.NewLine + Environment.NewLine +
                    (manifest.Mandatory
                        ? "当前版本已停止支持，请立即更新。"
                        : "是否立即下载并安装？安装时将自动覆盖旧版本，无需卸载。");

                if (!manifest.Mandatory)
                {
                    DialogResult choice = MessageBox.Show(
                        message,
                        "冷库宝 - 软件更新",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);

                    if (choice != DialogResult.Yes)
                        return false;
                }
                else
                {
                    MessageBox.Show(
                        message,
                        "冷库宝 - 需要更新",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                string installerPath = DownloadInstaller(manifest, remoteVersion);
                if (installerPath == null)
                {
                    DialogResult retry = MessageBox.Show(
                        "下载更新包失败，请检查网络后重试。",
                        "冷库宝 - 更新失败",
                        MessageBoxButtons.RetryCancel,
                        MessageBoxIcon.Warning);

                    if (retry == DialogResult.Retry)
                        continue;

                    return manifest.Mandatory;
                }

                LaunchInstaller(installerPath);
                return true;
            }
        }

        private static string DownloadInstaller(UpdateManifest manifest, Version remoteVersion)
        {
            string updateDir = Path.Combine(Path.GetTempPath(), "LengkubaoUpdate");
            Directory.CreateDirectory(updateDir);

            string fileName = "冷库宝_Setup_v" + remoteVersion + ".exe";
            string targetPath = Path.Combine(updateDir, fileName);

            using (var progressForm = new UpdateProgressForm())
            {
                progressForm.Show();
                Application.DoEvents();

                try
                {
                    DownloadFileAsync(manifest.DownloadUrl, targetPath, progressForm).GetAwaiter().GetResult();
                    return File.Exists(targetPath) ? targetPath : null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("下载更新包失败: " + ex.Message);
                    return null;
                }
                finally
                {
                    progressForm.Close();
                }
            }
        }

        private static async Task DownloadFileAsync(string downloadUrl, string targetPath, UpdateProgressForm progressForm)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(DownloadTimeoutMinutes);

                using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(true))
                    using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(true)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read).ConfigureAwait(true);
                            totalRead += read;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                int percent = (int)(totalRead * 100 / totalBytes.Value);
                                progressForm.ReportProgress(percent, "正在下载更新包… " + percent + "%");
                            }
                            else
                            {
                                progressForm.ReportProgress(0, "正在下载更新包…");
                            }
                        }
                    }
                }
            }

            progressForm.ReportProgress(100, "下载完成，准备安装…");
        }

        private static void LaunchInstaller(string installerPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
        }
    }
}
