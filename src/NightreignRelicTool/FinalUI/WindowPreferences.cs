using System;
using System.IO;
using System.Web.Script.Serialization;
using NightreignRelicTool.Core;

namespace NightreignRelicTool.FinalUI
{
    // Only display preferences are stored here. Save identities, rules and results stay in their existing stores.
    public sealed class WindowPreferences
    {
        public double MainWidth { get; set; } = 1480;
        public double MainHeight { get; set; } = 930;
        public double PositionLeft { get; set; } = 160;
        public double PositionTop { get; set; } = 120;
        public double PositionWidth { get; set; } = 840;
        public double PositionHeight { get; set; } = 560;
        public bool Cards { get; set; }
        public int PositionScale { get; set; } = 1;
        public double Opacity { get; set; } = 1;
        public static readonly double[] PositionScales = { .9, 1, 1.2 };
        private static double Valid(double value, double fallback) { return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value; }
        public static WindowPreferences Load(string path)
        {
            WindowPreferences state;
            try { state = new JavaScriptSerializer().Deserialize<WindowPreferences>(File.ReadAllText(path)) ?? new WindowPreferences(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException) { state = new WindowPreferences(); }
            state.MainWidth = Math.Max(1100, Valid(state.MainWidth, 1480)); state.MainHeight = Math.Max(650, Valid(state.MainHeight, 930));
            state.PositionWidth = Math.Max(520, Valid(state.PositionWidth, 840)); state.PositionHeight = Math.Max(260, Valid(state.PositionHeight, 560));
            state.PositionLeft = Valid(state.PositionLeft, 160); state.PositionTop = Valid(state.PositionTop, 120);
            state.PositionScale = Math.Max(0, Math.Min(2, state.PositionScale)); state.Opacity = Math.Max(.4, Math.Min(1, Valid(state.Opacity, 1)));
            return state;
        }
        public void Save(string path)
        { Save(path, null); }
        internal void Save(string path, Action<string, string> publish)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool preserve = false;
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
                LocalPreferencePublication.Publish(temporary, path, publish);
            }
            catch (IOException error) { preserve = LocalPreferencePublication.IsUncertain(error); throw; }
            finally
            {
                try { if (!preserve && File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
