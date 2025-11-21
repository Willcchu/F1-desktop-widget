using System.Configuration;
using System.Data;
using System.Windows;

namespace F1_widgets
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public void ChangeLanguage(string culture)
        {
            var dict = new ResourceDictionary();

            if (culture == "zh-CN")
                dict.Source = new Uri("Resources/StringResources.zh-CN.xaml", UriKind.Relative);
            else
                dict.Source = new Uri("Resources/StringResources.xaml", UriKind.Relative);

            // 这里一定要用 Application.Current.Resources
            var appResources = Application.Current.Resources;

            appResources.MergedDictionaries.Clear();
            appResources.MergedDictionaries.Add(dict);
        }


    }

}
