using System;
using System.Globalization;
using System.Windows.Data;

namespace ImageViewer.Converters
{
    public class AddMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                return width + 20; // 添加20像素的边距
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}