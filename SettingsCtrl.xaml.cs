using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MidiTrailRender
{
    /// <summary>
    /// Interaction logic for SettingsCtrl.xaml
    /// </summary>
    public partial class SettingsCtrl : UserControl
    {
        Settings settings;
        public AuraSelect auraselect;

        public void SetValues()
        {
            firstNote.Value = settings.firstNote;
            lastNote.Value = settings.lastNote - 1;
            noteDownSpeed.Value = (decimal)settings.noteDownSpeed;
            noteUpSpeed.Value = (decimal)settings.noteUpSpeed;
            boxNotes.IsChecked = settings.boxNotes;
            useVel.IsChecked = settings.useVel;
            notesChangeSize.IsChecked = settings.notesChangeSize;
            notesChangeTint.IsChecked = settings.notesChangeTint;
            sameWidthNotes.IsChecked = settings.sameWidthNotes; 
            noteDeltaScreenTime.Value = Math.Log(settings.deltaTimeOnScreen, 2);
            camHeight.Value = (decimal)settings.viewHeight;
            camOffset.Value = (decimal)settings.viewOffset;
            FOVSlider.Value = settings.FOV / Math.PI * 180;
            viewAngSlider.Value = settings.camAng / Math.PI * 180;
            renderDistSlider.Value = settings.viewdist;
            renderDistBackSlider.Value = settings.viewback;
            auraselect.LoadSettings();
        }

        public SettingsCtrl(Settings settings) : base()
        {
            InitializeComponent();
            this.settings = settings;
            LoadSettings(true);
            auraselect = new AuraSelect(settings);
            auraSubControlGrid.Children.Add(auraselect);
            auraselect.Margin = new Thickness(0);
            auraselect.HorizontalAlignment = HorizontalAlignment.Stretch;
            auraselect.VerticalAlignment = VerticalAlignment.Stretch;
            auraselect.Width = double.NaN;
            auraselect.Height = double.NaN;
            SetValues();
        }

        private void Nud_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (sender == firstNote) settings.firstNote = (int)firstNote.Value;
                if (sender == lastNote) settings.lastNote = (int)lastNote.Value + 1;
                if (sender == noteDownSpeed) settings.noteDownSpeed = (double)noteDownSpeed.Value;
                if (sender == noteUpSpeed) settings.noteUpSpeed = (double)noteUpSpeed.Value;
                if (sender == camHeight) settings.viewHeight = (double)camHeight.Value;
                if (sender == camOffset) settings.viewOffset = (double)camOffset.Value;
            }
            catch (NullReferenceException) { }
            catch (InvalidOperationException) { }
        }

        void injectSettings(Settings sett)
        {
            var sourceProps = typeof(Settings).GetFields().ToList();
            var destProps = typeof(Settings).GetFields().ToList();

            foreach (var sourceProp in sourceProps)
            {
                if (destProps.Any(x => x.Name == sourceProp.Name))
                {
                    var p = destProps.First(x => x.Name == sourceProp.Name);
                    p.SetValue(settings, sourceProp.GetValue(sett));
                }
            }
            SetValues();
        }

        private void BoxNotes_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                settings.boxNotes = (bool)boxNotes.IsChecked;
            }
            catch { }
        }

        private void FOVSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settings.FOV = (double)FOVSlider.Value / 180 * Math.PI;
                FOVVal.Content = Math.Round(FOVSlider.Value).ToString();
            }
            catch { }
        }

        private void ViewAngSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settings.camAng = (double)viewAngSlider.Value / 180 * Math.PI;
                viewAngVal.Content = Math.Round(viewAngSlider.Value).ToString();
            }
            catch { }
        }

        private void NoteDeltaScreenTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settings.deltaTimeOnScreen = Math.Pow(2, noteDeltaScreenTime.Value);
                screenTime.Content = (Math.Round(settings.deltaTimeOnScreen * 100) / 100).ToString();
            }
            catch (NullReferenceException)
            {

            }
        }

        private void RenderDistSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settings.viewdist = (double)renderDistSlider.Value;
                renderDistVal.Content = Math.Round(renderDistSlider.Value).ToString();
            }
            catch { }
        }

        private void RenderDistBackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settings.viewback = (double)renderDistBackSlider.Value;
                renderDistBackVal.Content = Math.Round(renderDistBackSlider.Value).ToString();
            }
            catch { }
        }

        private void FarPreset_Click(object sender, RoutedEventArgs e)
        {
            camHeight.Value = 0.5M;
            camOffset.Value = 0.4M;
            FOVSlider.Value = 60;
            viewAngSlider.Value = 32.08;
            renderDistSlider.Value = 14;
            renderDistBackSlider.Value = 0.2;
        }

        private void MediumPreset_Click(object sender, RoutedEventArgs e)
        {
            camHeight.Value = 0.52M;
            camOffset.Value = 0.37M;
            FOVSlider.Value = 60;
            viewAngSlider.Value = 34.98;
            renderDistSlider.Value = 5.52;
            renderDistBackSlider.Value = 0.2;
        }

        private void ClosePreset_Click(object sender, RoutedEventArgs e)
        {
            camHeight.Value = 0.58M;
            camOffset.Value = 0.12M;
            FOVSlider.Value = 60;
            viewAngSlider.Value = 61.11;
            renderDistSlider.Value = 1.25;
            renderDistBackSlider.Value = 0.2;
        }

        private void CloserPreset_Click(object sender, RoutedEventArgs e)
        {
            camHeight.Value = 0.55M;
            camOffset.Value = 0.33M;
            FOVSlider.Value = 60;
            viewAngSlider.Value = 39.62;
            renderDistSlider.Value = 3.06;
            renderDistBackSlider.Value = 0.2;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string s = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText("Plugins/MidiTrailRender.json", s);
                Console.WriteLine("Saved settings to MidiTrailRender.json");
            }
            catch
            {
                Console.WriteLine("Could not save settings");
            }
        }

        void LoadSettings(bool startup = false)
        {

            try
            {
                string s = File.ReadAllText("Plugins/MidiTrailRender.json");
                var sett = JsonConvert.DeserializeObject<Settings>(s);
                injectSettings(sett);
                Console.WriteLine("Loaded settings from MidiTrailRender.json");
            }
            catch
            {
                if (!startup)
                    Console.WriteLine("Could not load saved plugin settings");
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings();
        }

        private void DefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            injectSettings(new Settings());
        }

        private void UseVel_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                settings.useVel = (bool)useVel.IsChecked;
            }
            catch { }
        }

        private void CheckboxChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender == notesChangeSize) settings.notesChangeSize = (bool)notesChangeSize.IsChecked;
                if (sender == notesChangeTint) settings.notesChangeTint = (bool)notesChangeTint.IsChecked;
                if (sender == eatNotes) settings.eatNotes = (bool)eatNotes.IsChecked;
                if (sender == sameWidthNotes) settings.sameWidthNotes = (bool)sameWidthNotes.IsChecked;
            }
            catch { }
        }
    }
}
