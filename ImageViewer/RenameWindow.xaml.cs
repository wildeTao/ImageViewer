using System;
using System.IO;
using System.Windows;
using System.ComponentModel;

namespace ImageViewer
{
    public partial class RenameWindow : Window, INotifyPropertyChanged
    {
        private string _oldName;
        public string OldName
        {
            get => _oldName;
            set
            {
                _oldName = value;
                OnPropertyChanged(nameof(OldName));
            }
        }

        private string _newName;
        public string NewName
        {
            get => _newName;
            set
            {
                _newName = value;
                ValidateNewName();
                OnPropertyChanged(nameof(NewName));
            }
        }

        private string _filePath;
        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OldName = Path.GetFileName(value);
                NewName = OldName;
            }
        }

        public RenameWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void ValidateNewName()
        {
            errorText.Text = "";
            if (string.IsNullOrWhiteSpace(NewName))
            {
                errorText.Text = "文件名不能为空";
                return;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            if (NewName.IndexOfAny(invalidChars) >= 0)
            {
                errorText.Text = "文件名包含无效字符";
                return;
            }

            string newPath = Path.Combine(Path.GetDirectoryName(FilePath), NewName);
            if (File.Exists(newPath) && !string.Equals(OldName, NewName, StringComparison.OrdinalIgnoreCase))
            {
                errorText.Text = "文件名已存在";
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(errorText.Text))
            {
                return;
            }

            try
            {
                string newPath = Path.Combine(Path.GetDirectoryName(FilePath), NewName);
                File.Move(FilePath, newPath);
                FilePath = newPath;
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
    }
} 