using System.Windows;
using System.Windows.Controls;
using ClipDropPro.Models;

namespace ClipDropPro.Selectors
{
    public class ItemContentSelector : DataTemplateSelector
    {
        public DataTemplate ImageTemplate { get; set; } = null!;
        public DataTemplate ColorTemplate { get; set; } = null!;
        public DataTemplate UrlTemplate { get; set; } = null!;
        public DataTemplate TextTemplate { get; set; } = null!;

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is ClipboardItem ci)
            {
                if (ci.IsImage) return ImageTemplate;
                if (ci.IsColor) return ColorTemplate;
                if (ci.IsUrl) return UrlTemplate;
            }
            return TextTemplate;
        }
    }
}
