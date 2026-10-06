using System.Globalization;
using System.Windows.Data;
using Player.Core.Playback;

namespace Player.App.Resources;

public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    { RepeatMode mode => Strings.Get("Repeat" + mode), ReplayGainMode mode => Strings.Get("Gain" + mode), _ => value };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
