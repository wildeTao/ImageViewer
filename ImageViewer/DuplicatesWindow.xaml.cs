using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace ImageViewer
{
    public partial class DuplicatesWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<DuplicateGroup> _duplicateGroups = new ObservableCollection<DuplicateGroup>();
        public ObservableCollection<DuplicateGroup> DuplicateGroups
        {
            get => _duplicateGroups;
            set
            {
                _duplicateGroups = value;
                OnPropertyChanged(nameof(DuplicateGroups));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // 添加一个事件来通知文件删除
        public event Action<List<string>> FilesDeleted;

        public DuplicatesWindow(List<List<string>> duplicates)
        {
            InitializeComponent();
            DataContext = this;

            // 转换数据
            int groupIndex = 1;
            var groups = new ObservableCollection<DuplicateGroup>();
            foreach (var group in duplicates)
            {
                var duplicateGroup = new DuplicateGroup
                {
                    Header = $"重复组 {groupIndex++} ({group.Count} 个文件)",
                    Files = new ObservableCollection<DuplicateFile>(group.Select(f => new DuplicateFile
                    {
                        FilePath = f,
                        FileSize = new FileInfo(f).Length.ToString("N0") + " 字节",
                        ModifiedTime = File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm:ss")
                    }))
                };
                groups.Add(duplicateGroup);
            }
            DuplicateGroups = groups;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var filesToDelete = DuplicateGroups
                .SelectMany(g => g.Files)
                .Where(f => f.IsSelected)
                .ToList();

            if (filesToDelete.Count == 0)
            {
                MessageBox.Show("请选择要删除的文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                $"确定要删除选中的 {filesToDelete.Count} 个文件吗？此操作不可撤销。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var deletedFiles = new List<string>();
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file.FilePath);
                        deletedFiles.Add(file.FilePath);

                        // 从 UI 中移除文件
                        var group = DuplicateGroups.FirstOrDefault(g => g.Files.Contains(file));
                        if (group != null)
                        {
                            group.Files.Remove(file);

                            if (group.Files.Count <= 1)
                            {
                                DuplicateGroups.Remove(group);
                            }
                            else
                            {
                                group.Header = $"重复组 ({group.Files.Count} 个文件)";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"删除文件 {file.FilePath} 时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // 通知主窗口文件已被删除
                FilesDeleted?.Invoke(deletedFiles);

                if (!DuplicateGroups.Any())
                {
                    MessageBox.Show("所有重复文件已处理完毕。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class DuplicateGroup : INotifyPropertyChanged
    {
        public string Header { get; set; }
        private ObservableCollection<DuplicateFile> _files;
        public ObservableCollection<DuplicateFile> Files
        {
            get => _files;
            set
            {
                _files = value;
                OnPropertyChanged(nameof(Files));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class DuplicateFile : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileSize { get; set; }
        public string ModifiedTime { get; set; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        private ImageSource _imageSource;
        public ImageSource ImageSource
        {
            get
            {
                if (_imageSource == null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(FilePath);
                    bitmap.DecodePixelWidth = 100;  // 设置缩略图宽度
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze(); // 提高性能
                    _imageSource = bitmap;
                }
                return _imageSource;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}