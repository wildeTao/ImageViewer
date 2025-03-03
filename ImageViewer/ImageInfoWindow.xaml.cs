using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Linq;
using System.Collections.ObjectModel;
using System.Xml.Linq;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.ComponentModel;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using Baidu.Aip.Speech;
using System.Threading.Tasks;
using NAudio.Wave;  // 需要安装 NAudio NuGet 包
using Newtonsoft.Json.Linq;  // 添加这行
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Text;

namespace ImageViewer
{
    public partial class ImageInfoWindow : Window
    {
        private string imagePath;
        private const string CommentFileName = "ImageComments.xml";
        private XDocument commentsDoc;
        private Asr client;
        private bool isListening = false;
        private WaveInEvent waveIn;
        private MemoryStream audioStream;

        // 百度语音识别的 API Key（需要在百度云申请，免费）
        private const string API_KEY = "hKVEpdLh3BGh6yPOlxRP6RiB";
        private const string SECRET_KEY = "gdQnxrdCppxPi8GqF8vCF12ZBRBlPqVL";
        private const string APP_ID = "6409410";

        // 添加输入法相关的变量
        private bool isComposing = false;

        public ImageInfoWindow(string imagePath)
        {
            InitializeComponent();
            this.imagePath = imagePath;
            LoadImageInfo(imagePath);
            LoadComment();

            // 初始化百度语音识别客户端，需要提供所有三个参数
            client = new Asr(APP_ID, API_KEY, SECRET_KEY);
            client.Timeout = 60000;  // 设置超时时间

            // 添加输入法事件处理
            commentTextBox.PreviewTextInput += CommentTextBox_PreviewTextInput;
            commentTextBox.TextInput += CommentTextBox_TextInput;
        }

        private void LoadImageInfo(string imagePath)
        {
            try
            {
                var fileInfo = new FileInfo(imagePath);
                var bitmap = new BitmapImage(new Uri(imagePath));

                // 创建基本属性列表
                var properties = new List<ImageProperty>
                {
                    new ImageProperty("File Name", Path.GetFileName(imagePath)),
                    new ImageProperty("Location", Path.GetDirectoryName(imagePath)),
                    new ImageProperty("Type", GetFileTypeDescription(Path.GetExtension(imagePath))),
                    new ImageProperty("Size", $"{fileInfo.Length / 1024.0 / 1024.0:F2} MB"),
                    new ImageProperty("Created", $"{fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}"),
                    new ImageProperty("Modified", $"{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}"),
                    new ImageProperty("Resolution", $"{bitmap.PixelWidth} x {bitmap.PixelHeight} ({(bitmap.PixelWidth * bitmap.PixelHeight / 1000000.0):F2} MP)"),
                    new ImageProperty("Print Size", $"{bitmap.PixelWidth / 300:F2} x {bitmap.PixelHeight / 300:F2} inches, DPI: 300 x 300")
                };

                // 使用 WPF 内置的 BitmapMetadata 读取 EXIF 数据
                try
                {
                    using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
                        if (decoder.Frames[0].Metadata is BitmapMetadata metadata)
                        {
                            // 相机信息
                            if (!string.IsNullOrEmpty(metadata.CameraManufacturer))
                                properties.Add(new ImageProperty("Camera Manufacturer", metadata.CameraManufacturer));
                            
                            if (!string.IsNullOrEmpty(metadata.CameraModel))
                                properties.Add(new ImageProperty("Camera Model", metadata.CameraModel));
                            
                            // 拍摄时间
                            if (metadata.DateTaken != null)
                                properties.Add(new ImageProperty("Date Taken", metadata.DateTaken));
                            
                            // 尝试读取更多 EXIF 数据
                            TryAddExifProperty(metadata, properties, "Lens Model", "/app1/ifd/exif:{uint=42036}");
                            TryAddExifProperty(metadata, properties, "Focal Length", "/app1/ifd/exif:{uint=37386}");
                            TryAddExifProperty(metadata, properties, "35mm Equivalent Focal Length", "/app1/ifd/exif:{uint=41989}");
                            TryAddExifProperty(metadata, properties, "F Number", "/app1/ifd/exif:{uint=33437}");
                            TryAddExifProperty(metadata, properties, "Shutter Speed", "/app1/ifd/exif:{uint=33434}");
                            TryAddExifProperty(metadata, properties, "ISO", "/app1/ifd/exif:{uint=34855}");
                            TryAddExifProperty(metadata, properties, "Exposure Bias", "/app1/ifd/exif:{uint=37380}");
                            TryAddExifProperty(metadata, properties, "Metering Mode", "/app1/ifd/exif:{uint=37383}");
                            TryAddExifProperty(metadata, properties, "White Balance", "/app1/ifd/exif:{uint=37384}");
                            
                            // 著作者
                            TryAddExifProperty(metadata, properties, "Author", "/app1/ifd:{uint=315}");
                            
                            // 版权
                            TryAddExifProperty(metadata, properties, "Copyright", "/app1/ifd:{uint=33432}");

                            // 添加 iPhone 特有的 EXIF 路径
                            if (metadata.CameraManufacturer == "Apple" || metadata.CameraModel?.Contains("iPhone") == true)
                            {
                                // iPhone 特有的 EXIF 路径
                                TryAddExifProperty(metadata, properties, "F Number", "/app1/ifd/exif:{uint=33437}");
                                TryAddExifProperty(metadata, properties, "F Number", "/ifd/exif:{uint=33437}");
                                TryAddExifProperty(metadata, properties, "F Number", "/{ushort=0}");
                                
                                TryAddExifProperty(metadata, properties, "Focal Length", "/app1/ifd/exif:{uint=37386}");
                                TryAddExifProperty(metadata, properties, "Focal Length", "/ifd/exif:{uint=37386}");
                                
                                TryAddExifProperty(metadata, properties, "ISO", "/app1/ifd/exif:{uint=34855}");
                                TryAddExifProperty(metadata, properties, "ISO", "/ifd/exif:{uint=34855}");
                                
                                TryAddExifProperty(metadata, properties, "Shutter Speed", "/app1/ifd/exif:{uint=33434}");
                                TryAddExifProperty(metadata, properties, "Shutter Speed", "/ifd/exif:{uint=33434}");
                            }

                            // 在 LoadImageInfo 方法中添加这些路径
                            // 光圈值
                            TryAddExifProperty(metadata, properties, "F Number", "/app1/ifd/exif:{uint=33437}");
                            TryAddExifProperty(metadata, properties, "F Number", "/app1/ifd/exif/subifd:{uint=33437}");
                            TryAddExifProperty(metadata, properties, "F Number", "/{ushort=33437}");
                            TryAddExifProperty(metadata, properties, "F Number", "/exif:{uint=33437}");

                            // 焦距
                            TryAddExifProperty(metadata, properties, "Focal Length", "/app1/ifd/exif:{uint=37386}");
                            TryAddExifProperty(metadata, properties, "Focal Length", "/app1/ifd/exif/subifd:{uint=37386}");
                            TryAddExifProperty(metadata, properties, "Focal Length", "/{ushort=37386}");
                            TryAddExifProperty(metadata, properties, "Focal Length", "/exif:{uint=37386}");

                            // ISO
                            TryAddExifProperty(metadata, properties, "ISO", "/app1/ifd/exif:{uint=34855}");
                            TryAddExifProperty(metadata, properties, "ISO", "/app1/ifd/exif/subifd:{uint=34855}");
                            TryAddExifProperty(metadata, properties, "ISO", "/{ushort=34855}");
                            TryAddExifProperty(metadata, properties, "ISO", "/exif:{uint=34855}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    properties.Add(new ImageProperty("EXIF Error", ex.Message));
                }

                // 绑定到 ListView
                propertiesListView.ItemsSource = properties;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image info: {ex.Message}", "Error");
            }
        }

        private string GetFileTypeDescription(string extension)
        {
            switch (extension.ToLower())
            {
                case ".jpg":
                case ".jpeg":
                    return "JPEG Bitmap (JPG)";
                case ".png":
                    return "Portable Network Graphics (PNG)";
                case ".bmp":
                    return "Bitmap Image (BMP)";
                case ".gif":
                    return "Graphics Interchange Format (GIF)";
                default:
                    return extension.ToUpper().Substring(1) + " File";
            }
        }

        private string GetMeteringModeText(int meteringMode)
        {
            switch (meteringMode)
            {
                case 0: return "Unknown";
                case 1: return "Average";
                case 2: return "Center-weighted average";
                case 3: return "Spot";
                case 4: return "Multi-spot";
                case 5: return "Pattern";
                case 6: return "Partial";
                case 255: return "Other";
                default: return $"Unknown ({meteringMode})";
            }
        }

        private void LoadComment()
        {
            try
            {
                string commentsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ImageViewer",
                    CommentFileName);

                if (File.Exists(commentsPath))
                {
                    commentsDoc = XDocument.Load(commentsPath);
                }
                else
                {
                    System.IO.Directory.CreateDirectory(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ImageViewer"));
                    commentsDoc = new XDocument(new XElement("Comments"));
                    commentsDoc.Save(commentsPath);
                }

                var commentElement = commentsDoc.Root.Elements("Comment")
                    .FirstOrDefault(e => e.Attribute("Path")?.Value == imagePath);

                if (commentElement != null)
                {
                    commentTextBox.Text = commentElement.Value;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load comment: {ex.Message}", "Error");
            }
        }

        private void SaveComment()
        {
            try
            {
                string commentsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ImageViewer",
                    CommentFileName);

                var commentElement = commentsDoc.Root.Elements("Comment")
                    .FirstOrDefault(e => e.Attribute("Path")?.Value == imagePath);

                if (commentElement != null)
                {
                    commentElement.Value = commentTextBox.Text;
                }
                else
                {
                    commentsDoc.Root.Add(new XElement("Comment",
                        new XAttribute("Path", imagePath),
                        commentTextBox.Text));
                }

                commentsDoc.Save(commentsPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save comment: {ex.Message}", "Error");
            }
        }

        private void CommentTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                commentTextBox.IsReadOnly = true;
                SaveComment();
            }
            else if (e.Key == Key.T && commentTextBox.IsReadOnly)
            {
                commentTextBox.IsReadOnly = false;
                commentTextBox.Focus();
            }
        }

        private void CommentTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            commentTextBox.IsReadOnly = false;
            commentTextBox.Focus();
        }

        private void EnableEditing()
        {
            commentTextBox.IsReadOnly = false;
            commentTextBox.Background = Brushes.White;
            commentTextBox.Focus();
        }

        private void DisableEditing()
        {
            commentTextBox.IsReadOnly = true;
            commentTextBox.Background = SystemColors.ControlBrush;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!commentTextBox.IsReadOnly)
            {
                SaveComment();
            }

            if (waveIn != null)
            {
                waveIn.StopRecording();
                waveIn.Dispose();
            }

            if (audioStream != null)
            {
                audioStream.Dispose();
            }

            base.OnClosing(e);
        }

        private void StartRecording()
        {
            try
            {
                waveIn = new WaveInEvent();
                // 修改音频格式：16kHz采样率，16位采样深度，单声道
                waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                audioStream = new MemoryStream();

                waveIn.DataAvailable += (s, e) =>
                {
                    try
                    {
                        audioStream.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to write recording data: {ex.Message}", "Error");
                    }
                };

                waveIn.RecordingStopped += (s, e) =>
                {
                    try
                    {
                        if (waveIn != null)
                        {
                            waveIn.Dispose();
                            waveIn = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to stop recording: {ex.Message}", "Error");
                    }
                };

                waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start recording: {ex.Message}", "Error");
            }
        }

        private async Task StopRecordingAndRecognize()
        {
            if (waveIn != null)
            {
                waveIn.StopRecording();

                var audioData = audioStream.ToArray();
                audioStream.Dispose();

                try
                {
                    // 确保音频数据长度合适（至少100ms的音频）
                    if (audioData.Length < 3200) // 16000Hz * 16bit * 0.1s = 3200 bytes
                    {
                        MessageBox.Show("Recording time too short, please speak longer", "Notice");
                        return;
                    }

                    // 调用百度语音识别
                    var result = client.Recognize(audioData, "pcm", 16000);

                    if (result != null)
                    {
                        // 解析返回的 JSON 结果
                        var jsonResult = JObject.Parse(result.ToString());

                        // 如果发生错误，显示详细信息
                        if (jsonResult["err_no"].Value<int>() != 0)
                        {
                            MessageBox.Show($"Voice recognition error: {jsonResult["err_msg"]?.ToString()}", "Error");
                            return;
                        }

                        if (jsonResult["result"] != null && jsonResult["result"].HasValues)
                        {
                            string recognizedText = jsonResult["result"][0].Value<string>();
                            Dispatcher.Invoke(() =>
                            {
                                int selectionStart = commentTextBox.SelectionStart;
                                commentTextBox.Text = commentTextBox.Text.Insert(selectionStart, recognizedText + " ");
                                commentTextBox.SelectionStart = selectionStart + recognizedText.Length + 1;
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Voice recognition failed: {ex.Message}\n\n{ex.StackTrace}", "Error");
                }
            }
        }

        private async void VoiceInputButton_Click(object sender, RoutedEventArgs e)
        {
            if (commentTextBox.IsReadOnly)
            {
                commentTextBox.IsReadOnly = false;
                commentTextBox.Focus();
            }

            if (!isListening)
            {
                try
                {
                    voiceInputButton.Content = "Stop Recording";
                    StartRecording();
                    isListening = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to start voice recognition: {ex.Message}", "Error");
                }
            }
            else
            {
                try
                {
                    await StopRecordingAndRecognize();
                    voiceInputButton.Content = "Voice Input";
                    isListening = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to stop voice recognition: {ex.Message}", "Error");
                }
            }
        }

        // 添加输入法事件处理方法
        private void CommentTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            isComposing = true;
        }

        private void CommentTextBox_TextInput(object sender, TextCompositionEventArgs e)
        {
            isComposing = false;
        }

        // 添加辅助方法
        private void TryAddExifProperty(BitmapMetadata metadata, List<ImageProperty> properties, string name, string query)
        {
            try
            {
                var value = metadata.GetQuery(query);
                if (value != null)
                {
                    // 对特殊值进行格式化
                    if (name == "F Number")
                        properties.Add(new ImageProperty(name, $"f/{value}"));
                    else if (name == "Focal Length")
                        properties.Add(new ImageProperty(name, $"{value} mm"));
                    else if (name == "Shutter Speed")
                    {
                        double shutterSpeed = Convert.ToDouble(value);
                        string shutterSpeedText = shutterSpeed >= 1 ?
                            $"{shutterSpeed:F1} seconds" :
                            $"1/{1 / shutterSpeed:F0} seconds";
                        properties.Add(new ImageProperty(name, shutterSpeedText));
                    }
                    else if (name == "ISO")
                        properties.Add(new ImageProperty(name, $"ISO {value}"));
                    else if (name == "Metering Mode")
                        properties.Add(new ImageProperty(name, GetMeteringModeText(Convert.ToInt32(value))));
                    else if (name == "White Balance")
                        properties.Add(new ImageProperty(name, value.ToString() == "0" ? "Auto" : "Manual"));
                    else
                        properties.Add(new ImageProperty(name, value.ToString()));
                }
            }
            catch { /* 忽略单个属性的读取错误 */ }
        }

        // 使用 ExifTool 读取完整的 EXIF 数据
        private void LoadExifToolData(string imagePath, List<ImageProperty> properties)
        {
            try
            {
                // ExifTool 路径 - 需要先下载并安装 ExifTool
                string exifToolPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exiftool.exe");
                
                if (!File.Exists(exifToolPath))
                {
                    properties.Add(new ImageProperty("ExifTool", "ExifTool not found. Please install it for complete EXIF data."));
                    return;
                }
                
                // 创建进程调用 ExifTool
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = $"-j \"{imagePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    // 解析 JSON 输出
                    var json = Newtonsoft.Json.Linq.JArray.Parse(output);
                    var exifData = json[0] as Newtonsoft.Json.Linq.JObject;
                    
                    if (exifData != null)
                    {
                        // 添加所有可用的 EXIF 数据
                        foreach (var property in exifData.Properties())
                        {
                            // 跳过一些基本属性，因为我们已经添加过了
                            if (property.Name == "SourceFile" || property.Name == "FileName" || 
                                property.Name == "Directory" || property.Name == "FileSize")
                                continue;
                            
                            properties.Add(new ImageProperty(property.Name, property.Value.ToString()));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                properties.Add(new ImageProperty("ExifTool Error", ex.Message));
            }
        }

        private void DebugExif_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
                    if (decoder.Frames[0].Metadata is BitmapMetadata metadata)
                    {
                        var debugInfo = new StringBuilder();
                        debugInfo.AppendLine("EXIF 调试信息:");
                        debugInfo.AppendLine($"相机制造商: {metadata.CameraManufacturer}");
                        debugInfo.AppendLine($"相机型号: {metadata.CameraModel}");
                        debugInfo.AppendLine($"拍摄时间: {metadata.DateTaken}");
                        
                        // 尝试常见的 EXIF 路径
                        string[] commonPaths = {
                            "/app1/ifd/exif:{uint=33437}", // F Number
                            "/ifd/exif:{uint=33437}",
                            "/{ushort=33437}",
                            "/exif:{uint=33437}",
                            "/app1/ifd/exif:{uint=37386}", // Focal Length
                            "/ifd/exif:{uint=37386}",
                            "/{ushort=37386}",
                            "/exif:{uint=37386}",
                            "/app1/ifd/exif:{uint=34855}", // ISO
                            "/ifd/exif:{uint=34855}",
                            "/{ushort=34855}",
                            "/exif:{uint=34855}"
                        };
                        
                        foreach (var path in commonPaths)
                        {
                            try
                            {
                                var value = metadata.GetQuery(path);
                                if (value != null)
                                {
                                    debugInfo.AppendLine($"路径 {path}: {value}");
                                }
                            }
                            catch { }
                        }
                        
                        // 尝试获取所有可能的 EXIF 标签
                        debugInfo.AppendLine("\n所有找到的 EXIF 标签:");
                        for (uint i = 33000; i < 43000; i++)
                        {
                            try
                            {
                                var query = $"/app1/ifd/exif:{{uint={i}}}";
                                var value = metadata.GetQuery(query);
                                if (value != null)
                                {
                                    debugInfo.AppendLine($"标签 {i}: {value}");
                                }
                            }
                            catch { }
                        }
                        
                        MessageBox.Show(debugInfo.ToString(), "EXIF 调试信息");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"调试失败: {ex.Message}", "错误");
            }
        }
    }

    public class ImageProperty
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public ImageProperty(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    // 添加值转换器
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty((string)value) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}