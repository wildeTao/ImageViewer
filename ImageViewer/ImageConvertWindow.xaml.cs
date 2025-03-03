using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace ImageViewer
{
    public partial class ImageConvertWindow : Window
    {
        private List<string> _filePaths;
        private bool _isBatchMode;
        private bool _isUserChangingPath = false;  // 添加标志，防止循环更新
        public event Action<List<string>> ConversionCompleted;
        private List<string> _convertedFiles = new List<string>();

        public ImageConvertWindow(List<string> filePaths)
        {
            InitializeComponent();
            _filePaths = filePaths;
            _isBatchMode = filePaths.Count > 1;

            // 设置默认保存路径
            UpdateSavePath();

            // 添加单选按钮的事件处理
            jpgRadio.Checked += FormatRadio_Checked;
            pngRadio.Checked += FormatRadio_Checked;
            bmpRadio.Checked += FormatRadio_Checked;
            gifRadio.Checked += FormatRadio_Checked;
        }

        private void FormatRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isUserChangingPath)
            {
                UpdateSavePath();
            }
        }

        private void UpdateSavePath()
        {
            if (_isBatchMode)
            {
                savePathBox.Text = Path.GetDirectoryName(_filePaths[0]);
            }
            else
            {
                string directory = Path.GetDirectoryName(_filePaths[0]);
                string fileName = Path.GetFileNameWithoutExtension(_filePaths[0]);
                string extension = GetCurrentExtension();

                // 确保扩展名以点开始
                if (!extension.StartsWith("."))
                {
                    extension = "." + extension;
                }

                savePathBox.Text = Path.Combine(directory, fileName + extension);
            }
        }

        private string GetCurrentExtension()
        {
            // 确保扩展名始终包含点
            if (jpgRadio.IsChecked == true) return ".jpg";
            if (pngRadio.IsChecked == true) return ".png";
            if (bmpRadio.IsChecked == true) return ".bmp";
            if (gifRadio.IsChecked == true) return ".gif";
            return ".jpg"; // 默认返回jpg
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            _isUserChangingPath = true;
            try
            {
                if (_isBatchMode)
                {
                    var dialog = new FolderBrowserDialog();
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        savePathBox.Text = dialog.SelectedPath;
                    }
                }
                else
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "JPG图片|*.jpg|PNG图片|*.png|BMP图片|*.bmp|GIF图片|*.gif",
                        FileName = Path.GetFileNameWithoutExtension(_filePaths[0]) + GetCurrentExtension()
                    };

                    // 设置默认选中的格式
                    if (jpgRadio.IsChecked == true) dialog.FilterIndex = 1;
                    else if (pngRadio.IsChecked == true) dialog.FilterIndex = 2;
                    else if (bmpRadio.IsChecked == true) dialog.FilterIndex = 3;
                    else if (gifRadio.IsChecked == true) dialog.FilterIndex = 4;

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        savePathBox.Text = dialog.FileName;

                        // 根据用户选择的文件类型更新单选按钮
                        string ext = Path.GetExtension(dialog.FileName).ToLower();
                        switch (ext)
                        {
                            case ".jpg":
                            case ".jpeg":
                                jpgRadio.IsChecked = true;
                                break;
                            case ".png":
                                pngRadio.IsChecked = true;
                                break;
                            case ".bmp":
                                bmpRadio.IsChecked = true;
                                break;
                            case ".gif":
                                gifRadio.IsChecked = true;
                                break;
                        }
                    }
                }
            }
            finally
            {
                _isUserChangingPath = false;
            }
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(savePathBox.Text))
            {
                MessageBox.Show("请选择保存位置。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查文件扩展名格式
            if (!_isBatchMode)
            {
                string extension = Path.GetExtension(savePathBox.Text).ToLower();
                if (!IsValidExtension(extension))
                {
                    MessageBox.Show("文件扩展名格式不正确，请确保包含正确的扩展名（.jpg、.png、.bmp 或 .gif）。",
                        "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _convertedFiles.Clear();
            progressBar.Maximum = _filePaths.Count;
            progressBar.Value = 0;

            try
            {
                // 检查是否有重名文件
                var duplicateFiles = new List<string>();
                foreach (string sourcePath in _filePaths)
                {
                    string targetPath;
                    if (_isBatchMode)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(sourcePath) + GetCurrentExtension();
                        targetPath = Path.Combine(savePathBox.Text, fileName);
                    }
                    else
                    {
                        targetPath = savePathBox.Text;
                    }

                    if (File.Exists(targetPath) && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateFiles.Add(Path.GetFileName(targetPath));
                    }
                }

                // 如果有重名文件，提示用户
                if (duplicateFiles.Count > 0)
                {
                    string fileList = string.Join("\n", duplicateFiles);
                    var result = MessageBox.Show(
                        $"以下文件已存在：\n{fileList}\n\n请修改保存位置或文件名后重试。",
                        "文件已存在",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Cancel)
                    {
                        return;
                    }
                    else
                    {
                        return; // OK 按钮也返回，让用户修改名称
                    }
                }

                // 执行转换
                foreach (string sourcePath in _filePaths)
                {
                    string targetPath;
                    if (_isBatchMode)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(sourcePath) + GetCurrentExtension();
                        targetPath = Path.Combine(savePathBox.Text, fileName);
                    }
                    else
                    {
                        targetPath = savePathBox.Text;
                    }

                    if (ImageConverter.ConvertImage(sourcePath, targetPath, GetCurrentFormat(),
                        maxWidthCheck.IsChecked == true ? int.Parse(maxWidthBox.Text) : null,
                        maxHeightCheck.IsChecked == true ? int.Parse(maxHeightBox.Text) : null))
                    {
                        progressBar.Value++;
                        _convertedFiles.Add(targetPath);
                    }
                }

                MessageBox.Show("转换完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                // 触发转换完成事件
                ConversionCompleted?.Invoke(_convertedFiles);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"转换失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsValidExtension(string extension)
        {
            return extension == ".jpg" ||
                   extension == ".jpeg" ||
                   extension == ".png" ||
                   extension == ".bmp" ||
                   extension == ".gif";
        }

        private ImageFormat GetCurrentFormat()
        {
            if (pngRadio.IsChecked == true) return ImageFormat.Png;
            if (bmpRadio.IsChecked == true) return ImageFormat.Bmp;
            if (gifRadio.IsChecked == true) return ImageFormat.Gif;
            return ImageFormat.Jpeg;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}