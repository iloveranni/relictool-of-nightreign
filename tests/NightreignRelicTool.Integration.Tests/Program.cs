using System;
using System.IO;
using System.Linq;
using System.Windows;
using NightreignRelicTool.Core;
using NightreignRelicTool.Localization;

namespace NightreignRelicTool.Integration.Tests
{
    internal static partial class Program
    {
        private static int _assertions;

        [STAThread]
        private static int Main()
        {
            try
            {
                RunReleaseResourceTests();
                var model = RuntimeModel.LoadBuiltIn();
                var catalog = CustomEffectCatalog.LoadBuiltIn();
                Assert(model.RegulationVersion == "1.03.5", "Supported model version");
                Assert(model.Characters.Length == 10, "Complete character catalog");
                Assert(catalog.GameVersion == "1.03.5", "Supported search data version");
                var layout = RelicInventoryLayoutProfile.LoadBuiltIn();
                Assert(layout.Columns == 8 && layout.SortKey == "inventory_entry_sort_key_uint32_le",
                    "Eight-column UInt32 location profile");
                Assert(layout.IsExactPositionVerified && layout.SupportsGameVersion("1.03.5"),
                    "Public provenance preserves runtime validation status");
                var assembly = typeof(RuntimeModel).Assembly;
                Assert(assembly.GetName().Version.ToString() == "1.0.0.0", "Release assembly version");
                using (var key = assembly.GetManifestResourceStream("NightreignRelicTool.Resources.NrpackPublicKey.xml"))
                    Assert(key != null && key.Length > 0, "Public verification key remains embedded");

                // Load WPF dictionaries and About without running App.OnStartup or discovering saves.
                var app = new NightreignRelicTool.App();
                app.InitializeComponent();
                var about = new AboutWindow();
                Assert(about.UpdateLabel is System.Windows.Controls.TextBlock, "Update label is plain text");
                Assert((string)about.RepositoryButton.ToolTip == AboutWindow.UpdateUrl,
                    "Repository button opens the release page");
                Assert(AboutWindow.UpdateUrl == "https://github.com/iloveranni/relictool-of-nightreign/releases",
                    "Public release URL");
                about.Close();

                _assertions += SearchImplementationFixTests.Run();
                _assertions += CoLocationFrontierRegressionTests.Run();
                Console.WriteLine("PUBLIC_SYNTHETIC_PASS assertions=" + _assertions
                    + " resources_model_search_about=true saves_read=0");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
