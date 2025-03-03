using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace ImageViewer
{
    public static class ImageConverter
    {
        public static bool ConvertImage(string sourcePath, string targetPath, ImageFormat format, int? maxWidth = null, int? maxHeight = null)
        {
            try
            {
                using (var image = Image.FromFile(sourcePath))
                {
                    // 计算新的尺寸
                    int newWidth = image.Width;
                    int newHeight = image.Height;

                    if (maxWidth.HasValue || maxHeight.HasValue)
                    {
                        double ratio = (double)image.Width / image.Height;

                        if (maxWidth.HasValue && maxHeight.HasValue)
                        {
                            if (ratio > (double)maxWidth.Value / maxHeight.Value)
                            {
                                newWidth = maxWidth.Value;
                                newHeight = (int)(newWidth / ratio);
                            }
                            else
                            {
                                newHeight = maxHeight.Value;
                                newWidth = (int)(newHeight * ratio);
                            }
                        }
                        else if (maxWidth.HasValue)
                        {
                            if (image.Width > maxWidth.Value)
                            {
                                newWidth = maxWidth.Value;
                                newHeight = (int)(newWidth / ratio);
                            }
                        }
                        else if (maxHeight.HasValue)
                        {
                            if (image.Height > maxHeight.Value)
                            {
                                newHeight = maxHeight.Value;
                                newWidth = (int)(newHeight * ratio);
                            }
                        }
                    }

                    using (var resized = new Bitmap(newWidth, newHeight))
                    {
                        using (var graphics = Graphics.FromImage(resized))
                        {
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(image, 0, 0, newWidth, newHeight);
                        }

                        // 设置JPEG的质量
                        var encoderParameters = new EncoderParameters(1);
                        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 90L);

                        // 获取对应格式的编码器
                        ImageCodecInfo encoder = GetEncoder(format);

                        // 保存图片
                        resized.Save(targetPath, encoder, encoderParameters);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"转换图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
} 