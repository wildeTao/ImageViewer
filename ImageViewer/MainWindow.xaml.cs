using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Drawing; // 用于图片哈希计算
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Windows.Shapes;
using Path = System.IO.Path;  // 明确使用 System.IO.Path
using Image = System.Windows.Controls.Image; // 明确指定使用 WPF 的 Image 类
using Point = System.Windows.Point; // 明确指定使用 WPF 的 Point 类
using Brushes = System.Windows.Media.Brushes; // 明确指定使用 WPF 的 Brushes 类
using Color = System.Windows.Media.Color; // 明确指定使用 WPF 的 Color 类
using System.Windows.Input;  // 用于 Key, MouseButtonEventArgs 等
using System.ComponentModel;
using System.Windows.Media.Animation;
using Newtonsoft.Json.Linq;

namespace ImageViewer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private double currentZoom = 1.0;
        private double currentRotation = 0;
        private ObservableCollection<string> imageFiles = new ObservableCollection<string>();
        private int currentImageIndex = -1;

        private Window currentImageWindow;
        private Image currentImage;

        private bool isFullScreenMode = false;

        private const double ZOOM_FACTOR = 2.0; // 放大倍率，可以根据需要调整

        // 修改 zoomLevels 数组，添加小于1的值用于缩小
        private readonly double[] zoomLevels = { 0.1, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0 };
        private Canvas zoomIndicator; // 用于显示放大倍率指示器

        private List<string> allImageFiles = new List<string>();
        private Dictionary<string, string> imageCategories = new Dictionary<string, string>();

        private DateTime lastClickTime = DateTime.MinValue;
        private TextBlock currentEditingTextBlock;
        private TextBox currentEditingTextBox;

        public double thumbnailWidth { get; set; } = 160;  // 默认缩略图宽度
        public double thumbnailHeight { get; set; } = 120; // 默认缩略图高度

        private System.Windows.Threading.DispatcherTimer slideShowTimer;
        private Random random = new Random();
        private List<int> slideShowSequence;
        private int currentSlideIndex;

        private ImageAnalysisService imageAnalysis;
        private Dictionary<string, bool> searchResults = new Dictionary<string, bool>();
        private bool isSearching = false;

        private bool isComposing = false;

        private System.Threading.Timer searchTimer;
        private readonly object searchLock = new object();

        private Point initialMousePosition;
        private Point initialImagePosition;
        private bool isDragging = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeFolderTree();

            // 设置 DataContext 为当前窗口实例
            this.DataContext = this;

            // 检查启动参数
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                string filePath = args[1];
                if (File.Exists(filePath))
                {
                    string folderPath = Path.GetDirectoryName(filePath);
                    LoadImagesFromFolder(folderPath);

                    // 找到并选中当前图片
                    int index = imageFiles.IndexOf(filePath);
                    if (index >= 0)
                    {
                        thumbnailListBox.SelectedIndex = index;
                    }
                }
            }

            // 添加按键事件处理
            this.KeyDown += MainWindow_KeyDown;
            this.KeyUp += MainWindow_KeyUp;

            // 添加鼠标事件
            thumbnailListBox.MouseDown += ThumbnailListBox_MouseDown;
            thumbnailListBox.MouseUp += ThumbnailListBox_MouseUp;

            // 初始化图片分析服务
            imageAnalysis = new ImageAnalysisService(
                "zXLqrr9C7rStviOhqMHfjH5h",    // 替换为你的 API Key
                "oRxC5C2yPiHU03Lu7ThnuXrMUNIhm4un"  // 替换为你的 Secret Key
            );
        }

        private void InitializeFolderTree()
        {
            foreach (var drive in Directory.GetLogicalDrives())
            {
                var item = new System.Windows.Controls.TreeViewItem();
                item.Header = drive;
                item.Tag = drive;
                item.Items.Add("Loading...");
                item.Expanded += FolderTreeItem_Expanded;
                folderTreeView.Items.Add(item);
            }
        }

        private void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            var item = sender as System.Windows.Controls.TreeViewItem;
            if (item.Items.Count == 1 && item.Items[0].ToString() == "Loading...")
            {
                item.Items.Clear();
                try
                {
                    var dirInfo = new DirectoryInfo(item.Tag.ToString());
                    foreach (var dir in dirInfo.GetDirectories())
                    {
                        var subItem = new System.Windows.Controls.TreeViewItem();
                        subItem.Header = dir.Name;
                        subItem.Tag = dir.FullName;
                        subItem.Items.Add("Loading...");
                        subItem.Expanded += FolderTreeItem_Expanded;
                        item.Items.Add(subItem);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cannot access folder: " + ex.Message, "Error");
                }
            }
        }

        private void LoadImagesFromFolder(string folderPath)
        {
            imageFiles.Clear();
            thumbnailListBox.Items.Clear();
            currentPathText.Text = folderPath;

            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            allImageFiles = Directory.GetFiles(folderPath)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            // 初始加载所有图片
            foreach (var file in allImageFiles)
            {
                imageFiles.Add(file);
                var thumbnailItem = CreateThumbnailItem(file);
                thumbnailListBox.Items.Add(thumbnailItem);

                // 自动分类图片
                ClassifyImage(file);
            }
        }

        private void ClassifyImage(string imagePath)
        {
            // 简单的分类逻辑，可以根据需要扩展
            string fileName = Path.GetFileName(imagePath).ToLower();
            if (fileName.Contains("screenshot") || fileName.Contains("截图"))
            {
                imageCategories[imagePath] = "截图";
            }
            else if (fileName.StartsWith("img") || fileName.StartsWith("dsc") ||
                     fileName.StartsWith("pic") || fileName.Contains("photo"))
            {
                imageCategories[imagePath] = "照片";
            }
            else
            {
                imageCategories[imagePath] = "其他";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = searchBox.Text.ToLower();
            FilterImages(searchText, GetSelectedCategory());
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterImages(searchBox.Text.ToLower(), GetSelectedCategory());
        }

        private string GetSelectedCategory()
        {
            if (categoryComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                return selectedItem.Content.ToString() == "全部" ? null : selectedItem.Content.ToString();
            }
            return null;
        }

        private void FilterImages(string searchText, string category)
        {
            thumbnailListBox.Items.Clear();
            imageFiles.Clear();

            var filteredFiles = allImageFiles
                .Where(file =>
                {
                    bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                                       Path.GetFileName(file).ToLower().Contains(searchText);
                    bool matchesCategory = category == null ||
                                         (imageCategories.ContainsKey(file) &&
                                          imageCategories[file] == category);
                    return matchesSearch && matchesCategory;
                });

            foreach (var file in filteredFiles)
            {
                imageFiles.Add(file);
                thumbnailListBox.Items.Add(CreateThumbnailItem(file));
            }
        }

        private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
        {
            // 添加加载提示
            var progressWindow = new Window
            {
                Title = "正在查找重复图片",
                Width = 300,
                Height = 100,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            var progressText = new TextBlock
            {
                Text = "正在扫描图片，请稍候...",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            progressWindow.Content = progressText;
            progressWindow.Show();

            try
            {
                var progress = new Progress<string>(status =>
                {
                    progressText.Text = status;
                });

                var duplicates = await FindDuplicateImagesAsync(progress);

                progressWindow.Close();

                if (duplicates.Any())
                {
                    ShowDuplicatesWindow(duplicates);
                }
                else
                {
                    MessageBox.Show("未找到重复图片。", "查找完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                progressWindow.Close();
                MessageBox.Show("查找重复图片时出错: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<List<List<string>>> FindDuplicateImagesAsync(IProgress<string> progress)
        {
            var duplicates = new List<List<string>>();
            var fileGroups = new Dictionary<string, List<string>>();

            foreach (var file in allImageFiles)
            {
                progress.Report($"正在处理: {Path.GetFileName(file)}");

                // 计算图片哈希
                string hash = await Task.Run(() => CalculateImageHash(file));

                if (!fileGroups.ContainsKey(hash))
                {
                    fileGroups[hash] = new List<string>();
                }
                fileGroups[hash].Add(file);
            }

            // 收集重复的图片组
            foreach (var group in fileGroups.Where(g => g.Value.Count > 1))
            {
                duplicates.Add(group.Value);
            }

            return duplicates;
        }

        private string CalculateImageHash(string imagePath)
        {
            try
            {
                using (var bitmap = new System.Drawing.Bitmap(imagePath))
                {
                    // 将图片缩小到8x8，转为灰度图
                    var resized = new System.Drawing.Bitmap(8, 8);
                    using (var g = System.Drawing.Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bitmap, 0, 0, 8, 8);
                    }

                    // 计算平均值
                    var bytes = new byte[64];
                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 8; x++)
                        {
                            var pixel = resized.GetPixel(x, y);
                            bytes[y * 8 + x] = (byte)((pixel.R + pixel.G + pixel.B) / 3);
                        }
                    }

                    // 计算哈希
                    using (var md5 = MD5.Create())
                    {
                        byte[] hash = md5.ComputeHash(bytes);
                        return BitConverter.ToString(hash).Replace("-", "");
                    }
                }
            }
            catch
            {
                return Guid.NewGuid().ToString(); // 如果无法计算哈希，返回唯一值
            }
        }

        private void ShowDuplicatesWindow(List<List<string>> duplicates)
        {
            var window = new DuplicatesWindow(duplicates);
            window.Owner = this;

            // 订阅文件删除事件
            window.FilesDeleted += (deletedFiles) =>
            {
                // 从当前图片列表中移除已删除的文件
                foreach (var file in deletedFiles)
                {
                    var index = imageFiles.IndexOf(file);
                    if (index >= 0)
                    {
                        imageFiles.RemoveAt(index);
                        thumbnailListBox.Items.RemoveAt(index);
                    }
                    allImageFiles.Remove(file);
                    imageCategories.Remove(file);
                }

                // 如果当前显示的图片被删除，更新当前图片索引
                if (currentImageIndex >= imageFiles.Count)
                {
                    currentImageIndex = imageFiles.Count - 1;
                }

                // 刷新缩略图列表
                thumbnailListBox.Items.Refresh();
            };

            window.ShowDialog();
        }

        private ThumbnailItem CreateThumbnailItem(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.DecodePixelWidth = (int)thumbnailWidth;  // 使用当前缩略图宽度
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                return new ThumbnailItem
                {
                    ImageSource = bitmap,
                    FileName = Path.GetFileName(imagePath),
                    FullPath = imagePath
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建缩略图失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                    LoadImagesFromFolder(folderPath);

                    // 找到并选中当前图片
                    int index = imageFiles.IndexOf(openFileDialog.FileName);
                    if (index >= 0)
                    {
                        thumbnailListBox.SelectedIndex = index;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("无法加载图片: " + ex.Message);
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (currentImage != null)
            {
                currentZoom *= ZOOM_FACTOR;
                UpdateImageTransform();
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (currentImage != null)
            {
                currentZoom /= ZOOM_FACTOR;
                UpdateImageTransform();
            }
        }

        private void FitToWindow_Click(object sender, RoutedEventArgs e)
        {
            if (currentImage != null && currentImage.Source is BitmapImage bitmap)
            {
                // 计算适应窗口的缩放比例
                double windowWidth = currentImageWindow?.ActualWidth ?? this.ActualWidth;
                double windowHeight = currentImageWindow?.ActualHeight ?? this.ActualHeight;
                double scaleX = windowWidth / bitmap.PixelWidth;
                double scaleY = windowHeight / bitmap.PixelHeight;
                currentZoom = Math.Min(scaleX, scaleY);
                UpdateImageTransform();
            }
        }

        private void ActualSize_Click(object sender, RoutedEventArgs e)
        {
            currentZoom = 1.0;
            ApplyTransform();
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        private void PreviousImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageIndex > 0)
            {
                thumbnailListBox.SelectedIndex = currentImageIndex - 1;
            }
        }

        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageIndex < imageFiles.Count - 1)
            {
                thumbnailListBox.SelectedIndex = currentImageIndex + 1;
            }
        }

        private void ApplyTransform()
        {
            if (currentImage?.Source != null)
            {
                var transform = new TransformGroup();
                transform.Children.Add(new RotateTransform(currentRotation));
                transform.Children.Add(new ScaleTransform(currentZoom, currentZoom));
                currentImage.RenderTransform = transform;
            }
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = e.NewValue as System.Windows.Controls.TreeViewItem;
            if (selectedItem != null && selectedItem.Tag != null)
            {
                string folderPath = selectedItem.Tag.ToString();
                LoadImagesFromFolder(folderPath);
            }
        }

        private void ShowFullImage(string imagePath)
        {
            try
            {
                if (currentImageWindow != null)
                {
                    currentImageWindow.Close();
                }

                currentImageWindow = new Window
                {
                    Title = Path.GetFileName(imagePath),
                    WindowState = WindowState.Maximized,
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.Black,
                    Topmost = true
                };

                var grid = new Grid();
                
                // 创建一个 Canvas 来容纳图片，使其可以自由移动
                var canvas = new Canvas();
                canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
                canvas.VerticalAlignment = VerticalAlignment.Stretch;

                currentImage = new Image
                {
                    Source = new BitmapImage(new Uri(imagePath)),
                    Stretch = Stretch.Uniform
                };

                // 将图片添加到 Canvas 中
                canvas.Children.Add(currentImage);
                
                // 初始化图片位置到中心
                currentImage.Loaded += (s, e) =>
                {
                    double left = (canvas.ActualWidth - currentImage.ActualWidth) / 2;
                    double top = (canvas.ActualHeight - currentImage.ActualHeight) / 2;
                    Canvas.SetLeft(currentImage, left);
                    Canvas.SetTop(currentImage, top);
                };

                // 添加用于显示缩放指示器的 Canvas
                zoomIndicator = new Canvas
                {
                    Visibility = Visibility.Collapsed
                };

                // 将 Canvas 和缩放指示器添加到 Grid
                grid.Children.Add(canvas);
                grid.Children.Add(zoomIndicator);
                currentImageWindow.Content = grid;

                // 重置变换
                currentZoom = 1.0;
                currentRotation = 0;

                // 添加鼠标滚轮事件处理
                grid.MouseWheel += (s, e) =>
                {
                    Point imageCenter = new Point(
                        currentImage.ActualWidth / 2,
                        currentImage.ActualHeight / 2
                    );

                    double newZoom = currentZoom;
                    
                    if (e.Delta > 0)
                    {
                        newZoom = Math.Min(currentZoom * 1.1, 10.0);
                    }
                    else
                    {
                        newZoom = Math.Max(currentZoom / 1.1, 0.1);
                    }

                    if (Math.Abs(newZoom - currentZoom) > 0.001)
                    {
                        ApplySmoothZoom(currentImage, newZoom, imageCenter);
                        ShowZoomLevel(e.GetPosition(grid), newZoom);
                    }

                    e.Handled = true;
                };

                // 添加鼠标拖动支持
                grid.MouseDown += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        if (e.ClickCount == 1)  // 单击开始拖动
                        {
                            isDragging = true;
                            initialMousePosition = e.GetPosition(grid);
                            initialImagePosition = new Point(
                                Canvas.GetLeft(currentImage),
                                Canvas.GetTop(currentImage)
                            );
                            grid.CaptureMouse();
                        }
                        else if (e.ClickCount == 2)  // 双击关闭
                        {
                            currentImageWindow.Close();
                        }
                    }
                };

                grid.MouseMove += (s, e) =>
                {
                    if (isDragging)
                    {
                        Point currentPosition = e.GetPosition(grid);
                        double deltaX = currentPosition.X - initialMousePosition.X;
                        double deltaY = currentPosition.Y - initialMousePosition.Y;

                        double newLeft = initialImagePosition.X + deltaX;
                        double newTop = initialImagePosition.Y + deltaY;

                        Canvas.SetLeft(currentImage, newLeft);
                        Canvas.SetTop(currentImage, newTop);
                    }
                };

                grid.MouseUp += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Released)
                    {
                        isDragging = false;
                        grid.ReleaseMouseCapture();
                    }
                };

                // ESC 键退出
                currentImageWindow.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        currentImageWindow.Close();
                    }
                };

                currentImageWindow.Closed += (s, e) =>
                {
                    currentImageWindow = null;
                    currentImage = null;
                };

                currentImageWindow.Show();
                currentImageWindow.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法加载图片: " + ex.Message);
            }
        }

        // 添加显示缩放倍率的方法
        private void ShowZoomLevel(Point position, double zoom)
        {
            zoomIndicator.Children.Clear();
            zoomIndicator.Visibility = Visibility.Visible;

            var text = new TextBlock
            {
                Text = $"{zoom:F2}x",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                Padding = new Thickness(5),
                FontSize = 16
            };

            Canvas.SetLeft(text, position.X + 20);
            Canvas.SetTop(text, position.Y - 10);
            zoomIndicator.Children.Add(text);

            // 2秒后隐藏提示
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                zoomIndicator.Children.Clear();
                zoomIndicator.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        }

        private void ThumbnailListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (thumbnailListBox.SelectedItem is ThumbnailItem selectedItem)
            {
                currentImageIndex = thumbnailListBox.SelectedIndex;
            }
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 确保焦点在 ListBox 上并且有选中项
            if (e.Key == System.Windows.Input.Key.Space &&
                thumbnailListBox.IsFocused &&
                thumbnailListBox.SelectedItem is ThumbnailItem selectedItem &&
                currentImageWindow == null)
            {
                ShowFullImage(selectedItem.FullPath);
                e.Handled = true; // 防止事件继续传播
            }
        }

        private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space && currentImageWindow != null)
            {
                currentImageWindow.Close();
            }
        }

        private void ThumbnailListBox_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 获取点击的具体元素
            var hitTestResult = VisualTreeHelper.HitTest(thumbnailListBox, e.GetPosition(thumbnailListBox));
            if (hitTestResult != null)
            {
                // 向上遍历视觉树，查找是否点击在图片项上
                DependencyObject current = hitTestResult.VisualHit;
                while (current != null && !(current is ListBoxItem))
                {
                    current = VisualTreeHelper.GetParent(current);
                }

                // 如果点击在列表项上
                if (current is ListBoxItem)
                {
                    if ((e.LeftButton == System.Windows.Input.MouseButtonState.Pressed ||
                         e.RightButton == System.Windows.Input.MouseButtonState.Pressed) &&
                        currentImageWindow == null)
                    {
                        if (thumbnailListBox.SelectedItem is ThumbnailItem selectedItem)
                        {
                            ShowFullImage(selectedItem.FullPath);
                            e.Handled = true; // 防止事件继续传播
                        }
                    }
                }
            }
        }

        private void ThumbnailListBox_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (currentImageWindow != null)
            {
                currentImageWindow.Close();
                e.Handled = true; // 防止事件继续传播
            }
        }

        private void Image_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Image image && image.DataContext is ThumbnailItem item)
            {
                ShowFullImage(item.FullPath);
                e.Handled = true;
            }
        }

        private void Image_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (currentImageWindow != null)
            {
                currentImageWindow.Close();
                e.Handled = true;
            }
        }

        private void Image_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space)
            {
                if (sender is Image image && image.DataContext is ThumbnailItem item)
                {
                    ShowFullImage(item.FullPath);
                    e.Handled = true;
                }
            }
        }

        private void Image_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space && currentImageWindow != null)
            {
                currentImageWindow.Close();
                e.Handled = true;
            }
        }

        private void ShowImageProperties_Click(object sender, RoutedEventArgs e)
        {
            // 获取点击的图片项
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is Image image &&
                image.DataContext is ThumbnailItem item)
            {
                var infoWindow = new ImageInfoWindow(item.FullPath);
                infoWindow.Owner = this;
                infoWindow.ShowDialog();
            }
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (thumbnailListBox.SelectedItem is ThumbnailItem selectedItem)
            {
                var window = new RenameWindow { FilePath = selectedItem.FullPath };
                window.Owner = this;
                if (window.ShowDialog() == true)
                {
                    // 更新缩略图列表中的文件名
                    var index = thumbnailListBox.SelectedIndex;
                    imageFiles[index] = window.FilePath;

                    // 清理旧的缩略图资源
                    if (thumbnailListBox.Items[index] is ThumbnailItem oldItem)
                    {
                        if (oldItem.ImageSource is BitmapImage oldBitmap)
                        {
                            oldBitmap.StreamSource?.Dispose();
                        }
                    }

                    // 创建新的缩略图项
                    thumbnailListBox.Items[index] = CreateThumbnailItem(window.FilePath);

                    // 更新所有相关集合
                    var oldPath = selectedItem.FullPath;
                    var newPath = window.FilePath;

                    int allIndex = allImageFiles.IndexOf(oldPath);
                    if (allIndex >= 0)
                    {
                        allImageFiles[allIndex] = newPath;
                    }

                    if (imageCategories.ContainsKey(oldPath))
                    {
                        var category = imageCategories[oldPath];
                        imageCategories.Remove(oldPath);
                        imageCategories[newPath] = category;
                    }
                }
            }
            else
            {
                MessageBox.Show("请先选择要重命名的文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BatchRename_Click(object sender, RoutedEventArgs e)
        {
            if (imageFiles.Count == 0)
            {
                MessageBox.Show("当前文件夹没有图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new BatchRenameWindow(imageFiles.ToList());
            window.Owner = this;
            if (window.ShowDialog() == true)
            {
                // 保存当前选中的索引
                int selectedIndex = thumbnailListBox.SelectedIndex;

                // 获取当前文件夹中的所有图片
                string currentFolder = Path.GetDirectoryName(window.GetNewPath(window._filePaths[0]));
                var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var newFiles = Directory.GetFiles(currentFolder)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f)  // 保持文件顺序
                    .ToList();

                // 更新所有集合
                allImageFiles = newFiles;
                imageFiles.Clear();

                // 创建新的缩略图项列表
                var newThumbnailItems = new List<ThumbnailItem>();
                foreach (var file in newFiles)
                {
                    imageFiles.Add(file);
                    var thumbnailItem = CreateThumbnailItem(file);
                    if (thumbnailItem != null)
                    {
                        newThumbnailItems.Add(thumbnailItem);
                    }

                    // 更新分类
                    ClassifyImage(file);
                }

                // 释放旧的缩略图资源
                foreach (ThumbnailItem item in thumbnailListBox.Items)
                {
                    item.Dispose();
                }

                // 更新UI
                thumbnailListBox.Items.Clear();
                foreach (var item in newThumbnailItems)
                {
                    thumbnailListBox.Items.Add(item);
                }

                // 恢复选中状态
                if (selectedIndex >= 0 && selectedIndex < thumbnailListBox.Items.Count)
                {
                    thumbnailListBox.SelectedIndex = selectedIndex;
                }

                // 强制刷新UI
                thumbnailListBox.Items.Refresh();
                thumbnailListBox.UpdateLayout();

                // 通知属性更改
                foreach (ThumbnailItem item in thumbnailListBox.Items)
                {
                    if (item is INotifyPropertyChanged notifyItem)
                    {
                        var propertyChanged = notifyItem.GetType().GetEvent("PropertyChanged");
                        if (propertyChanged != null)
                        {
                            propertyChanged.GetRaiseMethod()?.Invoke(item, new object[]
                            {
                                item,
                                new PropertyChangedEventArgs("FileName")
                            });
                        }
                    }
                }
            }
        }

        private void FileName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var textBlock = sender as TextBlock;
            if (textBlock == null) return;

            var now = DateTime.Now;
            if ((now - lastClickTime).TotalMilliseconds < 500) // 双击检测
            {
                e.Handled = true;  // 防止事件冒泡
                StartEditing(textBlock);
            }
            lastClickTime = now;
        }

        private void StartEditing(TextBlock textBlock)
        {
            try
            {
                // 如果已经在编辑，先结束之前的编辑
                if (currentEditingTextBlock != null)
                {
                    FinishEditing(true);
                }

                // 查找父级 Grid
                var parent = textBlock.Parent as Grid;
                if (parent == null)
                {
                    MessageBox.Show("无法找到编辑控件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var textBox = parent.Children.OfType<TextBox>().FirstOrDefault();
                if (textBox != null)
                {
                    // 保存原始文件名，用于取消编辑
                    textBox.Tag = textBox.Text;

                    // 设置文本框的宽度为文本块的宽度
                    textBox.MinWidth = Math.Max(textBlock.ActualWidth + 10, 50);

                    textBlock.Visibility = Visibility.Collapsed;
                    textBox.Visibility = Visibility.Visible;
                    textBox.Focus();

                    // 选择文件名部分（不包括扩展名）
                    string fileName = textBox.Text;
                    int extensionIndex = fileName.LastIndexOf('.');
                    if (extensionIndex > 0)
                    {
                        textBox.Select(0, extensionIndex);
                    }
                    else
                    {
                        textBox.SelectAll();
                    }

                    currentEditingTextBlock = textBlock;
                    currentEditingTextBox = textBox;

                    // 确保文本框获得焦点
                    Dispatcher.BeginInvoke(new Action(() => textBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动编辑失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FileNameEditor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 防止事件冒泡到 ListBox
            e.Handled = true;
        }

        private void FileNameEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            // 当文本框失去焦点时保存更改
            if (currentEditingTextBox != null && currentEditingTextBox.IsVisible)
            {
                FinishEditing(true);
            }
        }

        private void FileNameEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FinishEditing(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // 取消编辑，恢复原始文件名
                if (currentEditingTextBox != null)
                {
                    currentEditingTextBox.Text = (string)currentEditingTextBox.Tag;
                }
                FinishEditing(false);
                e.Handled = true;
            }
        }

        private void FinishEditing(bool saveChanges)
        {
            if (currentEditingTextBlock == null || currentEditingTextBox == null) return;

            try
            {
                if (saveChanges)
                {
                    var item = (currentEditingTextBox.DataContext as ThumbnailItem);
                    if (item != null)
                    {
                        string oldPath = item.FullPath;
                        string newName = currentEditingTextBox.Text;

                        if (string.IsNullOrWhiteSpace(newName))
                        {
                            MessageBox.Show("文件名不能为空。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            // 恢复原始文件名
                            currentEditingTextBox.Text = (string)currentEditingTextBox.Tag;
                            return;
                        }

                        // 检查文件名是否有效
                        var invalidChars = Path.GetInvalidFileNameChars();
                        if (newName.IndexOfAny(invalidChars) >= 0)
                        {
                            MessageBox.Show("文件名包含无效字符。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            // 恢复原始文件名
                            currentEditingTextBox.Text = (string)currentEditingTextBox.Tag;
                            return;
                        }

                        string newPath = Path.Combine(Path.GetDirectoryName(oldPath), newName);

                        // 检查文件是否已存在
                        if (File.Exists(newPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show("文件名已存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            // 恢复原始文件名
                            currentEditingTextBox.Text = (string)currentEditingTextBox.Tag;
                            item.FileName = (string)currentEditingTextBox.Tag;
                            return;
                        }

                        // 释放图片资源
                        if (item.ImageSource is BitmapImage bitmap)
                        {
                            bitmap.StreamSource?.Dispose();
                            item.ImageSource = null;
                        }
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        // 执行重命名
                        File.Move(oldPath, newPath);

                        // 重新加载图片
                        var newBitmap = new BitmapImage();
                        newBitmap.BeginInit();
                        newBitmap.UriSource = new Uri(newPath);
                        newBitmap.DecodePixelWidth = 160;
                        newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        newBitmap.EndInit();
                        newBitmap.Freeze();

                        // 更新数据
                        item.ImageSource = newBitmap;
                        item.FileName = newName;
                        item.FullPath = newPath;

                        // 更新相关集合
                        int index = imageFiles.IndexOf(oldPath);
                        if (index >= 0)
                        {
                            imageFiles[index] = newPath;
                        }

                        int allIndex = allImageFiles.IndexOf(oldPath);
                        if (allIndex >= 0)
                        {
                            allImageFiles[allIndex] = newPath;
                        }

                        if (imageCategories.ContainsKey(oldPath))
                        {
                            var category = imageCategories[oldPath];
                            imageCategories.Remove(oldPath);
                            imageCategories[newPath] = category;
                        }
                    }
                }
                else
                {
                    // 取消编辑时恢复原始文件名
                    if (currentEditingTextBox.DataContext is ThumbnailItem item)
                    {
                        item.FileName = (string)currentEditingTextBox.Tag;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("重命名失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 发生错误时恢复原始文件名
                if (currentEditingTextBox.DataContext is ThumbnailItem item)
                {
                    item.FileName = (string)currentEditingTextBox.Tag;
                }
            }
            finally
            {
                currentEditingTextBlock.Visibility = Visibility.Visible;
                currentEditingTextBox.Visibility = Visibility.Collapsed;
                currentEditingTextBlock = null;
                currentEditingTextBox = null;
            }
        }

        // 添加一个新的事件处理方法
        private void ListBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 如果点击了空白处，结束编辑
            if (currentEditingTextBox != null && currentEditingTextBox.IsVisible)
            {
                // 获取点击的元素
                var element = e.OriginalSource as FrameworkElement;

                // 检查是否点击了编辑框或文本块
                bool clickedEditor = IsAncestorOf(element, currentEditingTextBox);
                bool clickedTextBlock = IsAncestorOf(element, currentEditingTextBlock);

                // 如果点击的不是编辑相关的元素，结束编辑
                if (!clickedEditor && !clickedTextBlock)
                {
                    FinishEditing(true);
                    e.Handled = true;
                }
            }
        }

        // 添加一个辅助方法来检查元素的父级关系
        private bool IsAncestorOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null)
                return false;

            DependencyObject current = child;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void RegisterFileAssociation_Click(object sender, RoutedEventArgs e)
        {
            FileAssociation.RegisterFileAssociations();
        }

        private void UnregisterFileAssociation_Click(object sender, RoutedEventArgs e)
        {
            FileAssociation.UnregisterFileAssociations();
        }

        private void ConvertImage_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = thumbnailListBox.SelectedItems.Cast<ThumbnailItem>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要转换的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var filePaths = selectedItems.Select(item => item.FullPath).ToList();
            var window = new ImageConvertWindow(filePaths);
            window.Owner = this;

            // 订阅转换完成事件
            window.ConversionCompleted += (convertedFiles) =>
            {
                // 如果是在同一个文件夹下
                if (convertedFiles.Any() && Path.GetDirectoryName(convertedFiles[0]) == Path.GetDirectoryName(imageFiles[0]))
                {
                    // 保存当前选中的索引
                    int selectedIndex = thumbnailListBox.SelectedIndex;

                    // 清理旧的缩略图资源
                    foreach (ThumbnailItem item in thumbnailListBox.Items)
                    {
                        item.Dispose();
                    }

                    // 重新加载当前文件夹
                    LoadImagesFromFolder(Path.GetDirectoryName(imageFiles[0]));

                    // 尝试选中第一个转换后的文件
                    if (convertedFiles.Count > 0)
                    {
                        int newIndex = imageFiles.IndexOf(convertedFiles[0]);
                        if (newIndex >= 0)
                        {
                            thumbnailListBox.SelectedIndex = newIndex;
                        }
                        else if (selectedIndex < thumbnailListBox.Items.Count)
                        {
                            thumbnailListBox.SelectedIndex = selectedIndex;
                        }
                    }
                }
            };

            window.ShowDialog();
        }

        private void ReactImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentImage != null)
            {
                currentRotation = 0;
                currentZoom = 1.0;
                UpdateImageTransform();
            }
        }

        private void ResizeImage_Click(object sender, RoutedEventArgs e)
        {
            if (thumbnailListBox.SelectedItem is ThumbnailItem selectedItem)
            {
                var window = new ImageConvertWindow(new List<string> { selectedItem.FullPath });
                window.Owner = this;
                window.ShowDialog();
            }
            else
            {
                MessageBox.Show("请先选择要调整大小的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateImageTransform()
        {
            if (currentImage != null)
            {
                var transform = new TransformGroup();
                transform.Children.Add(new ScaleTransform(currentZoom, currentZoom));
                transform.Children.Add(new RotateTransform(currentRotation));
                currentImage.RenderTransform = transform;

                // 更新缩放指示器
                UpdateZoomIndicator();
            }
        }

        private void UpdateZoomIndicator()
        {
            // 显示当前缩放比例
            if (zoomIndicator != null)
            {
                zoomIndicator.Children.Clear();
                var text = new TextBlock
                {
                    Text = $"{currentZoom:P0}",
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                    Padding = new Thickness(5),
                    FontSize = 14
                };
                zoomIndicator.Children.Add(text);

                // 3秒后隐藏指示器
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (s, e) =>
                {
                    zoomIndicator.Children.Clear();
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void ZoomThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                double factor = button.Tag.ToString() == "in" ? 1.2 : 0.8;
                thumbnailWidth *= factor;
                thumbnailHeight *= factor;

                // 限制最小和最大尺寸
                thumbnailWidth = Math.Max(80, Math.Min(thumbnailWidth, 320));
                thumbnailHeight = Math.Max(60, Math.Min(thumbnailHeight, 240));

                UpdateThumbnailSize();
            }
        }

        private void FitThumbnail_Click(object sender, RoutedEventArgs e)
        {
            // 重置为默认尺寸
            thumbnailWidth = 160;
            thumbnailHeight = 120;
            UpdateThumbnailSize();
        }

        private void UpdateThumbnailSize()
        {
            // 更新所有缩略图的大小
            foreach (ThumbnailItem item in thumbnailListBox.Items)
            {
                if (item.ImageSource is BitmapImage bitmap)
                {
                    // 创建新的缩略图
                    var newBitmap = new BitmapImage();
                    newBitmap.BeginInit();
                    newBitmap.UriSource = bitmap.UriSource;
                    newBitmap.DecodePixelWidth = (int)thumbnailWidth;
                    newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    newBitmap.EndInit();
                    newBitmap.Freeze();

                    // 更新图片源
                    item.ImageSource = newBitmap;
                }
            }

            // 更新 ItemTemplate 中的尺寸
            if (thumbnailListBox.ItemTemplate.LoadContent() is FrameworkElement element)
            {
                var stackPanel = element as StackPanel;
                if (stackPanel != null)
                {
                    stackPanel.Width = thumbnailWidth + 20; // 添加一些边距
                    var image = stackPanel.Children.OfType<Image>().FirstOrDefault();
                    if (image != null)
                    {
                        image.Width = thumbnailWidth;
                        image.Height = thumbnailHeight;
                    }
                }
            }

            // 刷新列表显示
            thumbnailListBox.Items.Refresh();
        }

        private void SlideShow_Click(object sender, RoutedEventArgs e)
        {
            if (imageFiles.Count == 0)
            {
                MessageBox.Show("当前文件夹没有图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var settingsWindow = new SlideShowSettingsWindow();
            settingsWindow.Owner = this;

            if (settingsWindow.ShowDialog() == true)
            {
                // 准备幻灯片序列
                slideShowSequence = Enumerable.Range(0, imageFiles.Count).ToList();
                if (settingsWindow.RandomOrder)
                {
                    slideShowSequence = slideShowSequence.OrderBy(x => random.Next()).ToList();
                }
                currentSlideIndex = 0;

                // 创建全屏窗口
                var slideShowWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    WindowState = WindowState.Maximized,
                    Background = new SolidColorBrush(settingsWindow.BackgroundColor),
                    Tag = settingsWindow
                };

                // 显示第一张图片
                ShowSlide(slideShowWindow);

                // 设置键盘事件
                slideShowWindow.KeyDown += (s, args) =>
                {
                    if (args.Key == Key.Escape)
                    {
                        slideShowWindow.Close();
                    }
                    else if (args.Key == Key.Left)
                    {
                        ShowPreviousSlide();
                    }
                    else if (args.Key == Key.Right || args.Key == Key.Space)
                    {
                        ShowNextSlide();
                    }
                };

                // 设置自动播放
                if (settingsWindow.AutoPlay)
                {
                    slideShowTimer?.Stop(); // 确保先停止可能存在的计时器
                    slideShowTimer = new System.Windows.Threading.DispatcherTimer();
                    slideShowTimer.Interval = TimeSpan.FromSeconds(settingsWindow.Interval);

                    EventHandler timerTick = null;
                    timerTick = (s, args) =>
                    {
                        if (currentSlideIndex >= slideShowSequence.Count - 1)
                        {
                            if (settingsWindow.RepeatPlay)
                            {
                                currentSlideIndex = 0;
                                ShowSlide(slideShowWindow);
                            }
                            else
                            {
                                slideShowTimer.Stop();
                                slideShowWindow.Close();
                            }
                        }
                        else
                        {
                            currentSlideIndex++;
                            ShowSlide(slideShowWindow);
                        }
                    };

                    slideShowTimer.Tick += timerTick;
                    slideShowTimer.Start();
                }

                slideShowWindow.ShowDialog();

                // 清理
                slideShowTimer?.Stop();
                slideShowTimer = null;
            }
        }

        private void ShowSlide(Window slideShowWindow)
        {
            if (currentSlideIndex >= 0 && currentSlideIndex < slideShowSequence.Count)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imageFiles[slideShowSequence[currentSlideIndex]]);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // 创建新的 Grid
                    var newGrid = new Grid();

                    // 添加图片
                    var image = new Image
                    {
                        Source = bitmap,
                        Stretch = (slideShowWindow.Tag as SlideShowSettingsWindow)?.StretchSmallImages == true
                            ? Stretch.Fill
                            : Stretch.Uniform
                    };
                    newGrid.Children.Add(image);

                    // 添加文字（如果需要）
                    if (slideShowWindow.Tag is SlideShowSettingsWindow settings && settings.ShowText)
                    {
                        var textBlock = new TextBlock
                        {
                            Text = Path.GetFileName(imageFiles[slideShowSequence[currentSlideIndex]]),
                            Foreground = Brushes.White,
                            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                            Padding = new Thickness(10),
                            VerticalAlignment = VerticalAlignment.Bottom,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Margin = new Thickness(10)
                        };
                        newGrid.Children.Add(textBlock);
                    }

                    // 应用过渡效果
                    if (slideShowWindow.Tag is SlideShowSettingsWindow settings2)
                    {
                        switch (settings2.TransitionEffect)
                        {
                            case TransitionEffect.Fade:
                                ApplyFadeTransition(slideShowWindow, newGrid);
                                break;
                            case TransitionEffect.SlideFromRight:
                                ApplySlideTransition(slideShowWindow, newGrid, true);
                                break;
                            case TransitionEffect.SlideFromLeft:
                                ApplySlideTransition(slideShowWindow, newGrid, false);
                                break;
                            case TransitionEffect.ZoomIn:
                                ApplyZoomTransition(slideShowWindow, newGrid, true);
                                break;
                            case TransitionEffect.ZoomOut:
                                ApplyZoomTransition(slideShowWindow, newGrid, false);
                                break;
                            default:
                                slideShowWindow.Content = newGrid;
                                break;
                        }
                    }
                    else
                    {
                        slideShowWindow.Content = newGrid;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("加载图片失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ApplyFadeTransition(Window window, Grid newContent)
        {
            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5)
            };

            newContent.Opacity = 0;
            window.Content = newContent;
            newContent.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        private void ApplySlideTransition(Window window, Grid newContent, bool fromRight)
        {
            var slideAnimation = new ThicknessAnimation
            {
                From = new Thickness(fromRight ? window.ActualWidth : -window.ActualWidth, 0, 0, 0),
                To = new Thickness(0),
                Duration = TimeSpan.FromSeconds(0.5)
            };

            newContent.Margin = fromRight ?
                new Thickness(window.ActualWidth, 0, 0, 0) :
                new Thickness(-window.ActualWidth, 0, 0, 0);
            window.Content = newContent;
            newContent.BeginAnimation(FrameworkElement.MarginProperty, slideAnimation);
        }

        private void ApplyZoomTransition(Window window, Grid newContent, bool zoomIn)
        {
            var scaleTransform = new ScaleTransform();
            newContent.RenderTransform = scaleTransform;
            newContent.RenderTransformOrigin = new Point(0.5, 0.5);

            var scaleAnimation = new DoubleAnimation
            {
                From = zoomIn ? 0.1 : 2.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.5)
            };

            window.Content = newContent;
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }

        private void ShowNextSlide()
        {
            if (currentSlideIndex < slideShowSequence.Count - 1)
            {
                currentSlideIndex++;
            }
            else if (slideShowTimer?.IsEnabled == true)
            {
                currentSlideIndex = 0;
            }
            else
            {
                return;
            }

            var window = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.WindowStyle == WindowStyle.None);
            if (window != null)
            {
                ShowSlide(window);
            }
        }

        private void ShowPreviousSlide()
        {
            if (currentSlideIndex > 0)
            {
                currentSlideIndex--;
                var window = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.WindowStyle == WindowStyle.None);
                if (window != null)
                {
                    ShowSlide(window);
                }
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchText = imageSearchBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // 清除搜索，显示所有图片
                foreach (ThumbnailItem item in thumbnailListBox.Items)
                {
                    item.Opacity = 1;
                }
                return;
            }

            // 执行搜索
            _ = PerformSearch(searchText);
        }

        private async Task PerformSearch(string searchText)
        {
            if (isSearching)
            {
                MessageBox.Show("Searching in progress, please wait...", "Notice");
                return;
            }

            try
            {
                isSearching = true;
                searchButton.IsEnabled = false;  // 禁用搜索按钮

                // 清除旧的搜索结果
                foreach (ThumbnailItem item in thumbnailListBox.Items)
                {
                    item.Opacity = 0.3;
                }

                // 分批处理图片以提高响应性
                const int batchSize = 10;
                for (int i = 0; i < imageFiles.Count; i += batchSize)
                {
                    var batch = imageFiles.Skip(i).Take(batchSize);
                    var tasks = batch.Select(async imagePath =>
                    {
                        try
                        {
                            var analysis = await imageAnalysis.AnalyzeImageAsync(imagePath);
                            var matches = imageAnalysis.MatchesDescription(analysis, searchText);
                            return (imagePath, matches);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"处理图片失败 {imagePath}: {ex.Message}");
                            return (imagePath, false);
                        }
                    });

                    var results = await Task.WhenAll(tasks);

                    // 更新UI
                    foreach (var (path, matches) in results)
                    {
                        var item = thumbnailListBox.Items.Cast<ThumbnailItem>()
                            .FirstOrDefault(i => i.FullPath == path);
                        if (item != null)
                        {
                            item.Opacity = matches ? 1 : 0.3;
                        }
                    }

                    // 让UI有机会更新
                    await Task.Delay(10);
                }

                // 显示搜索完成提示
                var matchCount = thumbnailListBox.Items.Cast<ThumbnailItem>()
                    .Count(item => item.Opacity == 1);
                MessageBox.Show($"Search completed, found {matchCount} matches.", "Search Result");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Search failed: " + ex.Message, "Error");
            }
            finally
            {
                isSearching = false;
                searchButton.IsEnabled = true;  // 重新启用搜索按钮
            }
        }

        // 可以保留这些输入法相关的处理
        private void ImageSearchBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            isComposing = true;
        }

        private void ImageSearchBox_TextInput(object sender, TextCompositionEventArgs e)
        {
            isComposing = false;
        }

        private void ApplySmoothZoom(Image image, double newZoom, Point center)
        {
            var animation = new DoubleAnimation
            {
                From = currentZoom,
                To = newZoom,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase()
            };

            // 使用图片中心点作为缩放中心
            var scaleTransform = new ScaleTransform(currentZoom, currentZoom, center.X, center.Y);
            
            // 如果已经有变换，保留其他变换
            var transformGroup = image.RenderTransform as TransformGroup ?? new TransformGroup();
            
            // 更新或添加缩放变换
            bool foundScale = false;
            for (int i = 0; i < transformGroup.Children.Count; i++)
            {
                if (transformGroup.Children[i] is ScaleTransform)
                {
                    transformGroup.Children[i] = scaleTransform;
                    foundScale = true;
                    break;
                }
            }
            
            if (!foundScale)
            {
                transformGroup.Children.Add(scaleTransform);
            }
            
            image.RenderTransform = transformGroup;

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            
            currentZoom = newZoom;
        }
    }

    public class ThumbnailItem : IDisposable, INotifyPropertyChanged
    {
        private ImageSource _imageSource;
        public ImageSource ImageSource
        {
            get => _imageSource;
            set
            {
                _imageSource = value;
                OnPropertyChanged(nameof(ImageSource));
            }
        }

        private string _fileName;
        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }

        private string _fullPath;
        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }

        private double opacity = 1.0;
        public double Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        public void Dispose()
        {
            if (ImageSource is BitmapImage bitmap)
            {
                bitmap.StreamSource?.Dispose();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}