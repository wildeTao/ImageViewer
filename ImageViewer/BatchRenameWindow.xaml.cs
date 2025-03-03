using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace ImageViewer
{
    public partial class BatchRenameWindow : Window, INotifyPropertyChanged
    {
        private string _pattern = "IMG_{n}";
        public string Pattern
        {
            get => _pattern;
            set
            {
                _pattern = value;
                OnPropertyChanged(nameof(Pattern));
            }
        }

        private int _startNumber = 1;
        public int StartNumber
        {
            get => _startNumber;
            set
            {
                _startNumber = value;
                OnPropertyChanged(nameof(StartNumber));
            }
        }

        private int _digitCount = 3;
        public int DigitCount
        {
            get => _digitCount;
            set
            {
                _digitCount = value;
                OnPropertyChanged(nameof(DigitCount));
            }
        }

        private ObservableCollection<RenamePreviewItem> _previewItems = new ObservableCollection<RenamePreviewItem>();
        public ObservableCollection<RenamePreviewItem> PreviewItems
        {
            get => _previewItems;
            set
            {
                _previewItems = value;
                OnPropertyChanged(nameof(PreviewItems));
            }
        }

        public List<string> _filePaths;

        public BatchRenameWindow(List<string> filePaths)
        {
            InitializeComponent();
            DataContext = this;
            _filePaths = filePaths;
            previewList.ItemsSource = PreviewItems;
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PreviewItems.Clear();
                var newNames = GenerateNewNames();

                // 检查文件名冲突
                var duplicates = newNames.GroupBy(n => n.ToLower())
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicates.Any())
                {
                    MessageBox.Show("生成的文件名有重复，请修改命名规则。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                for (int i = 0; i < _filePaths.Count; i++)
                {
                    PreviewItems.Add(new RenamePreviewItem
                    {
                        OldName = Path.GetFileName(_filePaths[i]),
                        NewName = newNames[i]
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"预览失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<string> GenerateNewNames()
        {
            var newNames = new List<string>();
            int currentNumber = StartNumber;
            string format = new string('0', DigitCount);

            foreach (var path in _filePaths)
            {
                string extension = Path.GetExtension(path);
                string newName = Pattern.Replace("{n}", currentNumber.ToString(format)) + extension;

                // 验证文件名
                var invalidChars = Path.GetInvalidFileNameChars();
                if (newName.IndexOfAny(invalidChars) >= 0)
                {
                    throw new Exception("生成的文件名包含无效字符");
                }

                newNames.Add(newName);
                currentNumber++;
            }

            return newNames;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewItems.Count == 0)
            {
                MessageBox.Show("请先预览重命名结果。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 先检查是否有文件名冲突
                var conflicts = new List<string>();
                for (int i = 0; i < _filePaths.Count; i++)
                {
                    string newPath = Path.Combine(Path.GetDirectoryName(_filePaths[i]), PreviewItems[i].NewName);
                    if (File.Exists(newPath) && !string.Equals(_filePaths[i], newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add(PreviewItems[i].NewName);
                    }
                }

                if (conflicts.Any())
                {
                    var result = MessageBox.Show(
                        $"以下文件已存在：\n{string.Join("\n", conflicts)}\n\n是否自动添加序号以避免冲突？",
                        "文件已存在",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 为冲突的文件名添加序号
                        for (int i = 0; i < _filePaths.Count; i++)
                        {
                            string newPath = Path.Combine(Path.GetDirectoryName(_filePaths[i]), PreviewItems[i].NewName);
                            if (File.Exists(newPath) && !string.Equals(_filePaths[i], newPath, StringComparison.OrdinalIgnoreCase))
                            {
                                string directory = Path.GetDirectoryName(newPath);
                                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(newPath);
                                string extension = Path.GetExtension(newPath);
                                int counter = 1;

                                // 尝试添加序号直到找到可用的文件名
                                while (File.Exists(newPath))
                                {
                                    string newFileName = $"{fileNameWithoutExt} ({counter}){extension}";
                                    newPath = Path.Combine(directory, newFileName);
                                    counter++;
                                }

                                PreviewItems[i].NewName = Path.GetFileName(newPath);
                            }
                        }
                    }
                    else
                    {
                        return; // 用户取消操作
                    }
                }

                // 执行重命名
                var tempPaths = new List<string>(_filePaths); // 创建副本以保存原始路径

                // 等待一小段时间，确保所有资源都被释放
                System.Threading.Thread.Sleep(100);
                GC.Collect();
                GC.WaitForPendingFinalizers();

                for (int i = 0; i < tempPaths.Count; i++)
                {
                    string newPath = Path.Combine(Path.GetDirectoryName(tempPaths[i]), PreviewItems[i].NewName);
                    try
                    {
                        if (File.Exists(newPath) && !string.Equals(tempPaths[i], newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // 跳过已存在的文件
                        }
                        File.Move(tempPaths[i], newPath);
                        _filePaths[i] = newPath;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"重命名文件 {tempPaths[i]} 失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重命名失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string GetNewPath(string oldPath)
        {
            var item = _filePaths.IndexOf(oldPath);
            if (item >= 0 && item < PreviewItems.Count)
            {
                return Path.Combine(Path.GetDirectoryName(oldPath), PreviewItems[item].NewName);
            }
            return oldPath;
        }
    }

    public class RenamePreviewItem
    {
        public string OldName { get; set; }
        public string NewName { get; set; }
    }
}