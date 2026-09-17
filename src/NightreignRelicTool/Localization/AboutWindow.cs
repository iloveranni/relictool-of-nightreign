using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace NightreignRelicTool.Localization
{
    public sealed class AboutWindow : Window
    {
        public const string UpdateUrl = "https://github.com/iloveranni/relictool-of-nightreign/releases";
        public readonly TextBlock UpdateLabel;
        public readonly Button RepositoryButton;
        private readonly TextBlock error;
        public AboutWindow()
        {
            Width=300; MinWidth=280; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;
            WindowStyle=WindowStyle.None; AllowsTransparency=true;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome { CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0) });
            Background=new SolidColorBrush(Color.FromRgb(17,16,15)); Foreground=new SolidColorBrush(Color.FromRgb(242,233,215));
            FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=15;
            SetBinding(TitleProperty,new Binding("[about.title]"){Source=LocalizationService.Current});
            var root=new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(44) });
            root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            root.Background=Background; Background=Brushes.Transparent;
            root.SizeChanged+=(s,e)=>root.Clip=new RectangleGeometry(new Rect(0,0,root.ActualWidth,root.ActualHeight),16,16);
            Content=root;
            var header=new Grid { Background=Brushes.Black };
            root.Children.Add(header);
            var heading=Label("about.title",16);
            heading.Margin=new Thickness(16,0,52,0);heading.VerticalAlignment=VerticalAlignment.Center;
            header.Children.Add(heading);
            NightreignRelicTool.FinalUI.WindowDrag.Attach(this,header);
            var panel=new StackPanel { Margin=new Thickness(24) };Grid.SetRow(panel,1);root.Children.Add(panel);
            var close=NightreignRelicTool.FinalUI.Ui.Button("×",Close);
            close.Style=(Style)Application.Current.FindResource("FinalCaptionButton");
            close.Width=36;close.Height=36;close.FontSize=20;close.HorizontalAlignment=HorizontalAlignment.Right;close.VerticalAlignment=VerticalAlignment.Center;close.Margin=new Thickness(0,0,4,0);
            System.Windows.Automation.AutomationProperties.SetName(close,LocalizationService.Current["关闭"]);
            header.Children.Add(close);
            PreviewKeyDown+=(s,e)=> { if(e.Key==System.Windows.Input.Key.Escape) { Close();e.Handled=true; } };
            var version=Label("about.currentVersion",14); version.Margin=new Thickness(0,0,0,18);panel.Children.Add(version);
            panel.Children.Add(Label("about.credits",12));
            panel.Children.Add(new TextBlock { Text="yiyi\nJia\nみたに\nymy_ds\nis0091\nNightreign Community",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(158,151,138)),Margin=new Thickness(0,6,0,18),LineHeight=20 });
            var updates=new Grid { Margin=new Thickness(0,4,0,16) };
            updates.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            updates.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            UpdateLabel=Label("about.viewUpdates",14);UpdateLabel.Margin=new Thickness(0,0,10,0);UpdateLabel.VerticalAlignment=VerticalAlignment.Center;
            RepositoryButton=UpdateAction(new TextBlock {Text="iloveranni/relictool-of-nightreign",FontSize=14,TextWrapping=TextWrapping.Wrap});
            Grid.SetColumn(RepositoryButton,1);updates.Children.Add(UpdateLabel);updates.Children.Add(RepositoryButton);panel.Children.Add(updates);
            panel.Children.Add(Label("about.feedbackEmail",15));
            panel.Children.Add(new TextBlock { Text="nrrelictool@gmail.com",FontSize=15,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) });
            error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Salmon,Margin=new Thickness(0,12,0,0)};panel.Children.Add(error);
        }
        private TextBlock Label(string key,double size)
        {
            var label=new TextBlock{FontSize=size,TextWrapping=TextWrapping.Wrap};
            label.SetBinding(TextBlock.TextProperty,new Binding("["+key+"]"){Source=LocalizationService.Current});return label;
        }
        private Button UpdateAction(UIElement content)
        {
            var button=NightreignRelicTool.FinalUI.Ui.Button("",()=>{
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateUrl) { UseShellExecute=true });error.Text=""; }
                catch(Exception ex) when(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException)
                { error.Text=LocalizationService.Current.Text("操作未完成；当前会话仍可继续使用。"); }
            });
            button.Content=content;button.MinHeight=36;button.Padding=new Thickness(0,6,0,6);button.HorizontalContentAlignment=HorizontalAlignment.Left;
            button.Foreground=NightreignRelicTool.FinalUI.Ui.Gold;button.ToolTip=UpdateUrl;
            return button;
        }
    }
}
