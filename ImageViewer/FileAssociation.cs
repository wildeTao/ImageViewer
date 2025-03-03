using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace ImageViewer
{
    public static class FileAssociation
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
        private static readonly string AppName = "ImageViewer";
        private static readonly string FileType = "ImageViewer.Picture";
        private static readonly string Description = "图片查看器";

        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void RegisterFileAssociations()
        {
            try
            {
                if (!IsAdministrator())
                {
                    var result = MessageBox.Show(
                        "注册文件关联需要管理员权限。是否以管理员身份重新启动程序？",
                        "需要管理员权限",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartAsAdmin();
                    }
                    return;
                }

                string executablePath = Process.GetCurrentProcess().MainModule.FileName;

                // 注册应用程序
                using (var key = Registry.ClassesRoot.CreateSubKey(FileType))
                {
                    key.SetValue("", Description);
                    using (var iconKey = key.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue("", $"{executablePath},0");
                    }
                    using (var commandKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue("", $"\"{executablePath}\" \"%1\"");
                    }
                }

                // 注册文件扩展名
                foreach (string ext in SupportedExtensions)
                {
                    using (var key = Registry.ClassesRoot.CreateSubKey(ext))
                    {
                        // 保存原有的关联
                        string existingAssoc = key.GetValue("") as string;
                        if (!string.IsNullOrEmpty(existingAssoc) && existingAssoc != FileType)
                        {
                            key.SetValue("ImageViewer.Backup", existingAssoc);
                        }

                        key.SetValue("", FileType);
                    }
                }

                MessageBox.Show("文件关联注册成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"注册文件关联失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void UnregisterFileAssociations()
        {
            try
            {
                if (!IsAdministrator())
                {
                    var result = MessageBox.Show(
                        "取消文件关联需要管理员权限。是否以管理员身份重新启动程序？",
                        "需要管理员权限",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartAsAdmin();
                    }
                    return;
                }

                // 恢复文件扩展名关联
                foreach (string ext in SupportedExtensions)
                {
                    using (var key = Registry.ClassesRoot.OpenSubKey(ext, true))
                    {
                        if (key != null)
                        {
                            string backup = key.GetValue("ImageViewer.Backup") as string;
                            if (!string.IsNullOrEmpty(backup))
                            {
                                key.SetValue("", backup);
                                key.DeleteValue("ImageViewer.Backup", false);
                            }
                            else
                            {
                                key.DeleteValue("", false);
                            }
                        }
                    }
                }

                // 删除应用程序注册信息
                Registry.ClassesRoot.DeleteSubKeyTree(FileType, false);

                MessageBox.Show("文件关联已取消！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"取消文件关联失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void RestartAsAdmin()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"以管理员身份重启失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 