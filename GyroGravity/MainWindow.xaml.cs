using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace GyroGravity;

public partial class MainWindow : Window
{
    private readonly AxisSettings xSettings = new AxisSettings();
    private readonly AxisSettings ySettings = new AxisSettings();
    private const int WH_MOUSE_LL = 14;
    private LowLevelMouseProc _proc;
    private IntPtr _hookID = IntPtr.Zero;
    private readonly Stopwatch stopwatch = new Stopwatch();
    private bool g_sendingInput = false;
    private const long SENT_INPUT_FLAG = 0xDEADBEEFL;
    private int accumulatedDeltaX = 0;
    private int accumulatedDeltaY = 0;
    private const double accumulationInterval = 0.001; // 1ms
    private DateTime lastProcessTime = DateTime.Now;
    private System.Windows.Threading.DispatcherTimer uiUpdateTimer;
    private int lastRawX, lastRawY, lastAdjustedX, lastAdjustedY;
    private static readonly INPUT[] MouseInputs = new INPUT[1];
    private double fractionalX = 0, fractionalY = 0;
    private NotifyIcon trayIcon;
    private static readonly IntPtr rawInputBuffer = Marshal.AllocHGlobal(1024); // Allocate once, size should exceed typical RAWINPUT size
    private static bool bufferAllocated = true;
    private double countsPerDegree = 1.0; // Default: 360 counts = 360 degrees, so 1 count/degree

    public MainWindow()
    {
        InitializeComponent();
        // Load last settings if available
        string lastSettingsPath = GetLastSettingsPath();
        if (File.Exists(lastSettingsPath))
        {
            try
            {
                string json = File.ReadAllText(lastSettingsPath);
                var preset = JsonSerializer.Deserialize<Preset>(json);
                if (preset != null && preset.XSettings != null && preset.YSettings != null)
                {
                    SetUIFromPreset(preset); // Update UI with loaded settings
                    SynchronizeUI();         // Sync UI elements as needed
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading last settings: {ex.Message}");
            }
        }
        CountsFor360Text.TextChanged += (s, e) => SynchronizeUI();
        UpdateSettings();
        UpdateGraph();
        UpdateJoltGraph();
        UpdateVelocityGraph();
        SynchronizeUI();

        ApplyButton.Click += ApplyButton_Click;
        ResetSettings.Click += ResetSettings_Click;
        SavePreset.Click += SavePreset_Click;
        LoadPreset.Click += LoadPreset_Click;

        // Add event handlers for real-time UI synchronization (X-axis)
        TargetSenseTextX.TextChanged += (s, e) => SynchronizeUI();
        BaseSenseTextX.TextChanged += (s, e) => SynchronizeUI();
        TargetCountTextX.TextChanged += (s, e) => SynchronizeUI();
        OffsetTextX.TextChanged += (s, e) => SynchronizeUI();
        ExponentTextX.TextChanged += (s, e) => SynchronizeUI();
        EnableLimitCheckX.Checked += (s, e) => SynchronizeUI();
        EnableLimitCheckX.Unchecked += (s, e) => SynchronizeUI();
        MirrorSenseCheckX.Checked += (s, e) => SynchronizeUI();
        MirrorSenseCheckX.Unchecked += (s, e) => SynchronizeUI();
        NaturalRadioX.Checked += (s, e) => SynchronizeUI();
        LinearRadioX.Checked += (s, e) => SynchronizeUI();
        PowerRadioX.Checked += (s, e) => SynchronizeUI();
        SigmoidRadioX.Checked += (s, e) => SynchronizeUI();
        GainCheckX.Checked += (s, e) => SynchronizeUI();
        GainCheckX.Unchecked += (s, e) => SynchronizeUI();
        // Add event handlers for real-time UI synchronization (Y-axis)
        TargetSenseTextY.TextChanged += (s, e) => SynchronizeUI();
        BaseSenseTextY.TextChanged += (s, e) => SynchronizeUI();
        TargetCountTextY.TextChanged += (s, e) => SynchronizeUI();
        OffsetTextY.TextChanged += (s, e) => SynchronizeUI();
        ExponentTextY.TextChanged += (s, e) => SynchronizeUI();
        EnableLimitCheckY.Checked += (s, e) => SynchronizeUI();
        EnableLimitCheckY.Unchecked += (s, e) => SynchronizeUI();
        MirrorSenseCheckY.Checked += (s, e) => SynchronizeUI();
        MirrorSenseCheckY.Unchecked += (s, e) => SynchronizeUI();
        NaturalRadioY.Checked += (s, e) => SynchronizeUI();
        LinearRadioY.Checked += (s, e) => SynchronizeUI();
        PowerRadioY.Checked += (s, e) => SynchronizeUI();
        SigmoidRadioY.Checked += (s, e) => SynchronizeUI();
        GainCheckY.Checked += (s, e) => SynchronizeUI();
        GainCheckY.Unchecked += (s, e) => SynchronizeUI();

        // Sync checkboxes
        SyncCurvesCheck.Checked += (s, e) => SynchronizeUI();
        SyncCurvesCheck.Unchecked += (s, e) => SynchronizeUI();
        SyncSettingsCheck.Checked += (s, e) => SynchronizeUI();
        SyncSettingsCheck.Unchecked += (s, e) => SynchronizeUI();

        NaturalRadioX.Checked += (s, e) => { GainCheckX.IsChecked = true; UpdateSettings(); SynchronizeUI(); };
        LinearRadioX.Checked += (s, e) => { GainCheckX.IsChecked = true; UpdateSettings(); SynchronizeUI(); };
        PowerRadioX.Checked += (s, e) => { GainCheckX.IsChecked = false; UpdateSettings(); SynchronizeUI(); };
        SigmoidRadioX.Checked += (s, e) => { GainCheckX.IsChecked = false; UpdateSettings(); SynchronizeUI(); };
        NaturalRadioY.Checked += (s, e) => { GainCheckY.IsChecked = true; UpdateSettings(); SynchronizeUI(); };
        LinearRadioY.Checked += (s, e) => { GainCheckY.IsChecked = true; UpdateSettings(); SynchronizeUI(); };
        PowerRadioY.Checked += (s, e) => { GainCheckY.IsChecked = false; UpdateSettings(); SynchronizeUI(); };
        SigmoidRadioY.Checked += (s, e) => { GainCheckY.IsChecked = false; UpdateSettings(); SynchronizeUI(); };

        // Set up the low-level hook to suppress default cursor movement
        _proc = HookCallback;
        _hookID = SetHook(_proc);
        stopwatch.Start();

        uiUpdateTimer = new System.Windows.Threading.DispatcherTimer();
        uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(42);
        uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        uiUpdateTimer.Start();

        this.Icon = new BitmapImage(new Uri("C:\\Users\\yue_b\\source\\repos\\GyroGravity\\GyroGravity\\GyroGravity2.ico"));
        trayIcon = new NotifyIcon
        {
            Icon = new Icon("C:\\Users\\yue_b\\source\\repos\\GyroGravity\\GyroGravity\\GyroGravity2.ico"),
            Text = "GyroGravity",
            Visible = true
        };
        trayIcon.DoubleClick += (s, e) => { WindowState = WindowState.Normal; Show(); };
        trayIcon.ContextMenuStrip = new ContextMenuStrip();
        trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => Close());

        StateChanged += (s, e) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        };
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (LastRawMoveText != null)
        {
            LastRawMoveText.Text = $"Last Raw Move: ({lastRawX}, {lastRawY})";
        }
        if (LastAdjustedMoveText != null)
        {
            LastAdjustedMoveText.Text = $"Last Adjusted Move: ({lastAdjustedX}, {lastAdjustedY})";
        }
    }
    public enum CurveType
    {
        Natural,
        Linear,
        Power,
        Sigmoid
    }
    public class AxisSettings
    {
        public double GainConstantC1 { get; set; }      // New property
        public bool UseGain { get; set; }
        public CurveType CurveType { get; set; }
        public double TargetSensitivity { get; set; } // L2
        public bool EnableLimit { get; set; }
        public double BaseSensitivity { get; set; }   // K2
        public bool MirrorSense { get; set; }
        public double TargetCounts { get; set; }      // M2
        public double Offset { get; set; }            // N2
        public double Exponent { get; set; }          // K3
        public double K4 { get; set; }                // For Sigmoid
        public double M4 { get; set; }                // For Sigmoid
        public double NaturalCoeff { get; set; } // For Natural curve
        public double PowerDeltaSensitivity { get; set; } // (L2 - K2) for Power
        public double PowerVelocityRange { get; set; }   // (M2 - N2) for Power
        public AxisSettings()
        {
            CurveType = CurveType.Natural;
            UseGain = false;
        }
        public double LinearSlope { get; set; } // Precomputed for Linear
        public double LinearOffset { get; set; } // Precomputed for Linear
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettings();
        SaveLastSettings();
    }
    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        // Set curve types to Natural (default for X and Y)
        NaturalRadioX.IsChecked = true;
        NaturalRadioY.IsChecked = true;

        // Set sync checkboxes to checked (default)
        SyncCurvesCheck.IsChecked = true;
        SyncSettingsCheck.IsChecked = true;

        // Set gain checkboxes to checked (default)
        GainCheckX.IsChecked = true;
        GainCheckY.IsChecked = true;

        // Set text boxes to default values
        TargetSenseTextX.Text = "2";       // Target Gyro Ratio X
        TargetSenseTextY.Text = "2";       // Target Gyro Ratio Y
        BaseSenseTextX.Text = "1";         // Base Sensitivity X
        BaseSenseTextY.Text = "1";         // Base Sensitivity Y
        TargetCountTextX.Text = "4";      // Target Degrees Per Second X
        TargetCountTextY.Text = "4";      // Target Degrees Per Second Y
        OffsetTextX.Text = "0";            // Offset X
        OffsetTextY.Text = "0";            // Offset Y
        ExponentTextX.Text = "0.05";       // Exponent X
        ExponentTextY.Text = "0.05";       // Exponent Y

        // Set additional checkboxes
        EnableLimitCheckX.IsChecked = true;   // Enable Limit X
        EnableLimitCheckY.IsChecked = true;   // Enable Limit Y
        MirrorSenseCheckX.IsChecked = false;  // Mirror Sense X
        MirrorSenseCheckY.IsChecked = false;  // Mirror Sense Y

        CountsFor360Text.Text = "360"; // Add this line

        SynchronizeUI();
        UpdateSettings();
    }
    public class Preset
    {
        public AxisSettings? XSettings { get; set; }
        public AxisSettings? YSettings { get; set; }
        public bool SyncCurves { get; set; }
        public bool SyncSettings { get; set; }
        public double CountsFor360 { get; set; } = 360.0; // New property with default
    }
    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettings();
        var preset = new Preset
        {
            XSettings = xSettings,
            YSettings = ySettings,
            SyncCurves = SyncCurvesCheck.IsChecked == true,
            SyncSettings = SyncSettingsCheck.IsChecked == true,
            CountsFor360 = double.TryParse(CountsFor360Text.Text, out double counts) && counts > 0 ? counts : 360.0
        };
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Preset files (*.json)|*.json",
            DefaultExt = "json"
        };
        if (dialog.ShowDialog() == true)
        {
            string json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
        }
    }
    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Preset files (*.json)|*.json"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var preset = JsonSerializer.Deserialize<Preset>(json);
                if (preset != null && preset.XSettings != null && preset.YSettings != null)
                {
                    SetUIFromPreset(preset);
                    SynchronizeUI();
                    UpdateSettings();
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to load preset: Invalid preset data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    private string GetLastSettingsPath()
    {
        // Get the AppData directory and create a subfolder for GyroGravity
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "GyroGravity");
        Directory.CreateDirectory(dir); // Ensure the directory exists
        return Path.Combine(dir, "last_settings.json");
    }

    private void SaveLastSettings()
    {
        var preset = new Preset
        {
            XSettings = xSettings,
            YSettings = ySettings,
            SyncCurves = SyncCurvesCheck.IsChecked == true,
            SyncSettings = SyncSettingsCheck.IsChecked == true,
            CountsFor360 = double.Parse(CountsFor360Text.Text)
        };
        string json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        string path = GetLastSettingsPath();
        File.WriteAllText(path, json);
    }

    private void SetUIFromPreset(Preset preset)
    {
        // Set sync checkboxes first to ensure correct synchronization state
        SyncCurvesCheck.IsChecked = preset.SyncCurves;
        SyncSettingsCheck.IsChecked = preset.SyncSettings;

        // Set X curve type radio buttons
        NaturalRadioX.IsChecked = preset.XSettings!.CurveType == CurveType.Natural;
        LinearRadioX.IsChecked = preset.XSettings!.CurveType == CurveType.Linear;
        PowerRadioX.IsChecked = preset.XSettings!.CurveType == CurveType.Power;
        SigmoidRadioX.IsChecked = preset.XSettings!.CurveType == CurveType.Sigmoid;

        // Set Y curve type radio buttons
        NaturalRadioY.IsChecked = preset.YSettings!.CurveType == CurveType.Natural;
        LinearRadioY.IsChecked = preset.YSettings!.CurveType == CurveType.Linear;
        PowerRadioY.IsChecked = preset.YSettings!.CurveType == CurveType.Power;
        SigmoidRadioY.IsChecked = preset.YSettings!.CurveType == CurveType.Sigmoid;

        // Set X text boxes
        TargetSenseTextX.Text = preset.XSettings!.TargetSensitivity.ToString(CultureInfo.InvariantCulture);
        BaseSenseTextX.Text = preset.XSettings!.BaseSensitivity.ToString(CultureInfo.InvariantCulture);
        TargetCountTextX.Text = preset.XSettings!.TargetCounts.ToString(CultureInfo.InvariantCulture);
        OffsetTextX.Text = preset.XSettings!.Offset.ToString(CultureInfo.InvariantCulture);
        ExponentTextX.Text = preset.XSettings!.Exponent.ToString(CultureInfo.InvariantCulture);

        // Set X checkboxes
        GainCheckX.IsChecked = preset.XSettings!.UseGain;
        EnableLimitCheckX.IsChecked = preset.XSettings!.EnableLimit;
        MirrorSenseCheckX.IsChecked = preset.XSettings!.MirrorSense;

        // Set Y text boxes
        TargetSenseTextY.Text = preset.YSettings!.TargetSensitivity.ToString(CultureInfo.InvariantCulture);
        BaseSenseTextY.Text = preset.YSettings!.BaseSensitivity.ToString(CultureInfo.InvariantCulture);
        TargetCountTextY.Text = preset.YSettings!.TargetCounts.ToString(CultureInfo.InvariantCulture);
        OffsetTextY.Text = preset.YSettings!.Offset.ToString(CultureInfo.InvariantCulture);
        ExponentTextY.Text = preset.YSettings!.Exponent.ToString(CultureInfo.InvariantCulture);

        // Set Y checkboxes
        GainCheckY.IsChecked = preset.YSettings!.UseGain;
        EnableLimitCheckY.IsChecked = preset.YSettings!.EnableLimit;
        MirrorSenseCheckY.IsChecked = preset.YSettings!.MirrorSense;

        CountsFor360Text.Text = preset.CountsFor360.ToString(CultureInfo.InvariantCulture);
    }
    private IntPtr SetHook(LowLevelMouseProc proc)
    {
        return SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }
    private void UpdateSettings()
    {
        try
        {        // Parse CountsFor360 from UI
            if (double.TryParse(CountsFor360Text.Text, out double countsFor360) && countsFor360 > 0)
            {
                this.countsPerDegree = countsFor360 / 360.0;
            }
            else
            {
                this.countsPerDegree = 1.0; // Default: 360 counts = 360 degrees
                CountsFor360Text.Text = "360"; // Reset UI to default if invalid
            }
            // X Settings
            xSettings.CurveType = GetSelectedCurveTypeX();
            xSettings.TargetSensitivity = double.Parse(TargetSenseTextX.Text);
            xSettings.EnableLimit = EnableLimitCheckX.IsChecked == true;
            xSettings.BaseSensitivity = double.Parse(BaseSenseTextX.Text);
            xSettings.MirrorSense = MirrorSenseCheckX.IsChecked == true;
            xSettings.TargetCounts = double.Parse(TargetCountTextX.Text);
            xSettings.Offset = double.Parse(OffsetTextX.Text);
            xSettings.Exponent = double.Parse(ExponentTextX.Text);
            xSettings.UseGain = GainCheckX.IsChecked == true;
            if (PowerRadioX.IsChecked == true)
                xSettings.Exponent = double.Parse(ExponentTextX.Text);

            if (xSettings.CurveType == CurveType.Natural)
            {
                xSettings.NaturalCoeff = (xSettings.BaseSensitivity - xSettings.TargetSensitivity) /
                                         Math.Pow(xSettings.Offset - xSettings.TargetCounts, 2);
                double N2_minus_M2 = xSettings.Offset - xSettings.TargetCounts;
                xSettings.GainConstantC1 = (xSettings.BaseSensitivity - xSettings.TargetSensitivity) * xSettings.Offset
                                           - xSettings.NaturalCoeff * (1.0 / 3.0) * Math.Pow(N2_minus_M2, 3);
            }
            if (xSettings.CurveType == CurveType.Linear)
            {
                xSettings.LinearSlope = (xSettings.TargetSensitivity - xSettings.BaseSensitivity) /
                                        (xSettings.TargetCounts - xSettings.Offset);
                xSettings.LinearOffset = xSettings.BaseSensitivity - xSettings.LinearSlope * xSettings.Offset;
            }
            if (xSettings.CurveType == CurveType.Sigmoid)
            {
                xSettings.M4 = (xSettings.TargetSensitivity - xSettings.BaseSensitivity) /
                               (xSettings.TargetCounts - xSettings.Offset);
                xSettings.K4 = (xSettings.BaseSensitivity * (xSettings.BaseSensitivity + Math.Exp(xSettings.M4 * xSettings.Offset)) -
                                xSettings.TargetSensitivity) /
                               (xSettings.BaseSensitivity + Math.Exp(xSettings.M4 * xSettings.Offset));
            }
            if (xSettings.CurveType == CurveType.Power)
            {
                xSettings.PowerDeltaSensitivity = xSettings.TargetSensitivity - xSettings.BaseSensitivity;
                xSettings.PowerVelocityRange = xSettings.TargetCounts - xSettings.Offset;
            }
            if (xSettings.MirrorSense)
            {
                xSettings.BaseSensitivity = 1 / xSettings.TargetSensitivity;
                BaseSenseTextX.Text = xSettings.BaseSensitivity.ToString();
            }
            // Parse X-axis target sensitivity
            if (double.TryParse(TargetSenseTextX.Text, out double targetSenseX) && targetSenseX > 0)
            {
                xSettings.TargetSensitivity = targetSenseX; // Apply valid value
            }
            else
            {
                xSettings.TargetSensitivity = 1.0; // Safe default
                TargetSenseTextX.Text = "1.0"; // Reset UI
                System.Windows.MessageBox.Show("Target Sensitivity for X must be greater than 0.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            // Y Settings
            ySettings.CurveType = GetSelectedCurveTypeY();
            ySettings.TargetSensitivity = double.Parse(TargetSenseTextY.Text);
            ySettings.EnableLimit = EnableLimitCheckY.IsChecked == true;
            ySettings.BaseSensitivity = double.Parse(BaseSenseTextY.Text);
            ySettings.MirrorSense = MirrorSenseCheckY.IsChecked == true;
            ySettings.TargetCounts = double.Parse(TargetCountTextY.Text);
            ySettings.Offset = double.Parse(OffsetTextY.Text);
            ySettings.Exponent = double.Parse(ExponentTextY.Text);
            ySettings.UseGain = GainCheckY.IsChecked == true;
            if (PowerRadioY.IsChecked == true)
                ySettings.Exponent = double.Parse(ExponentTextY.Text);

            if (ySettings.CurveType == CurveType.Natural)
            {
                ySettings.NaturalCoeff = (ySettings.BaseSensitivity - ySettings.TargetSensitivity) /
                                         Math.Pow(ySettings.Offset - ySettings.TargetCounts, 2);
                double N2_minus_M2 = ySettings.Offset - ySettings.TargetCounts;
                ySettings.GainConstantC1 = (ySettings.BaseSensitivity - ySettings.TargetSensitivity) * ySettings.Offset
                                           - ySettings.NaturalCoeff * (1.0 / 3.0) * Math.Pow(N2_minus_M2, 3);
            }
            if (ySettings.CurveType == CurveType.Linear)
            {
                ySettings.LinearSlope = (ySettings.TargetSensitivity - ySettings.BaseSensitivity) /
                                        (ySettings.TargetCounts - ySettings.Offset);
                ySettings.LinearOffset = ySettings.BaseSensitivity - ySettings.LinearSlope * ySettings.Offset;
            }
            if (ySettings.CurveType == CurveType.Sigmoid)
            {
                ySettings.M4 = (ySettings.TargetSensitivity - ySettings.BaseSensitivity) /
                               (ySettings.TargetCounts - ySettings.Offset);
                ySettings.K4 = (ySettings.BaseSensitivity * (ySettings.BaseSensitivity + Math.Exp(ySettings.M4 * ySettings.Offset)) -
                                ySettings.TargetSensitivity) /
                               (ySettings.BaseSensitivity + Math.Exp(ySettings.M4 * ySettings.Offset));
            }
            if (ySettings.CurveType == CurveType.Power)
            {
                ySettings.PowerDeltaSensitivity = ySettings.TargetSensitivity - ySettings.BaseSensitivity;
                ySettings.PowerVelocityRange = ySettings.TargetCounts - ySettings.Offset;
            }
            if (ySettings.MirrorSense)
            {
                ySettings.BaseSensitivity = 1 / ySettings.TargetSensitivity;
                BaseSenseTextY.Text = ySettings.BaseSensitivity.ToString();
            }
            // Parse Y-axis target sensitivity
            if (double.TryParse(TargetSenseTextY.Text, out double targetSenseY) && targetSenseY > 0)
            {
                ySettings.TargetSensitivity = targetSenseY; // Apply valid value
            }
            else
            {
                ySettings.TargetSensitivity = 1.0; // Safe default
                TargetSenseTextY.Text = "1.0"; // Reset UI
                System.Windows.MessageBox.Show("Target Sensitivity for Y must be greater than 0.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            // Synchronization applies to settings only on "Apply"
            if (SyncCurvesCheck.IsChecked == true)
            {
                ySettings.CurveType = xSettings.CurveType;
            }
            if (SyncSettingsCheck.IsChecked == true)
            {
                ySettings.TargetSensitivity = xSettings.TargetSensitivity;
                ySettings.EnableLimit = xSettings.EnableLimit;
                ySettings.BaseSensitivity = xSettings.BaseSensitivity;
                ySettings.MirrorSense = xSettings.MirrorSense;
                ySettings.TargetCounts = xSettings.TargetCounts;
                ySettings.Offset = xSettings.Offset;
                ySettings.Exponent = xSettings.Exponent;
                ySettings.K4 = xSettings.K4;
                ySettings.M4 = xSettings.M4;
                ySettings.UseGain = xSettings.UseGain;
                if (PowerRadioX.IsChecked == true)
                    ySettings.Exponent = xSettings.Exponent;
            }
            UpdateGraph();
            UpdateJoltGraph();
            UpdateVelocityGraph();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating settings: {ex.Message}");
        }
    }

    private void SynchronizeUI()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                // Get current curve types
                CurveType curveTypeX = GetSelectedCurveTypeX();
                CurveType curveTypeY = GetSelectedCurveTypeY();

                // Update visibility for X axis
                bool showLimitX = (curveTypeX != CurveType.Natural && curveTypeX != CurveType.Sigmoid);
                EnableLimitLabelX.Visibility = showLimitX ? Visibility.Visible : Visibility.Collapsed;
                EnableLimitCheckX.Visibility = showLimitX ? Visibility.Visible : Visibility.Collapsed;
                bool showExponentX = (curveTypeX == CurveType.Power);
                ExponentLabelX.Visibility = showExponentX ? Visibility.Visible : Visibility.Collapsed;
                ExponentTextX.Visibility = showExponentX ? Visibility.Visible : Visibility.Collapsed;

                // Update visibility for Y axis
                bool showLimitY = (curveTypeY != CurveType.Natural && curveTypeY != CurveType.Sigmoid);
                EnableLimitLabelY.Visibility = showLimitY ? Visibility.Visible : Visibility.Collapsed;
                EnableLimitCheckY.Visibility = showLimitY ? Visibility.Visible : Visibility.Collapsed;
                bool showExponentY = (curveTypeY == CurveType.Power);
                ExponentLabelY.Visibility = showExponentY ? Visibility.Visible : Visibility.Collapsed;
                ExponentTextY.Visibility = showExponentY ? Visibility.Visible : Visibility.Collapsed;

                // Sync curves if checked
                if (SyncCurvesCheck.IsChecked == true)
                {
                    SetCurveTypeY(curveTypeX);
                }

                // Sync settings if checked
                if (SyncSettingsCheck.IsChecked == true)
                {
                    TargetSenseTextY.Text = TargetSenseTextX.Text;
                    EnableLimitCheckY.IsChecked = EnableLimitCheckX.IsChecked;
                    BaseSenseTextY.Text = BaseSenseTextX.Text;
                    MirrorSenseCheckY.IsChecked = MirrorSenseCheckX.IsChecked;
                    TargetCountTextY.Text = TargetCountTextX.Text;
                    OffsetTextY.Text = OffsetTextX.Text;
                    ExponentTextY.Text = ExponentTextX.Text;
                    GainCheckY.IsChecked = GainCheckX.IsChecked;
                    // Removed event handler additions here
                }

                // Calculate recommended max limits
                if (SyncSettingsCheck.IsChecked == true)
                {
                    if (double.TryParse(TargetSenseTextX.Text, out double targetSenseX))
                    {
                        double minLimit = targetSenseX;
                        double maxLimit = Math.Pow(targetSenseX, 2);
                        string rangeText = $"{minLimit:F2}-{maxLimit:F2}";
                        RecommendedMaxLimitX.Text = rangeText;
                        RecommendedMaxLimitY.Text = rangeText;
                    }
                }
                else
                {
                    if (double.TryParse(TargetSenseTextX.Text, out double targetSenseX))
                    {
                        double minLimitX = targetSenseX;
                        double maxLimitX = Math.Pow(targetSenseX, 2);
                        RecommendedMaxLimitX.Text = $"{minLimitX:F2}-{maxLimitX:F2}";
                    }
                    if (double.TryParse(TargetSenseTextY.Text, out double targetSenseY))
                    {
                        double minLimitY = targetSenseY;
                        double maxLimitY = Math.Pow(targetSenseY, 2);
                        RecommendedMaxLimitY.Text = $"{minLimitY:F2}-{maxLimitY:F2}";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error synchronizing UI: {ex.Message}");
            }
        });
    }

    private CurveType GetSelectedCurveTypeX()
    {
        if (NaturalRadioX.IsChecked == true) return CurveType.Natural;
        if (LinearRadioX.IsChecked == true) return CurveType.Linear;
        if (PowerRadioX.IsChecked == true) return CurveType.Power;
        if (SigmoidRadioX.IsChecked == true) return CurveType.Sigmoid;
        return CurveType.Natural; // Default fallback
    }

    private CurveType GetSelectedCurveTypeY()
    {
        if (NaturalRadioY.IsChecked == true) return CurveType.Natural;
        if (LinearRadioY.IsChecked == true) return CurveType.Linear;
        if (PowerRadioY.IsChecked == true) return CurveType.Power;
        if (SigmoidRadioY.IsChecked == true) return CurveType.Sigmoid;
        return CurveType.Natural; // Default fallback
    }

    private void SetCurveTypeY(CurveType curveType)
    {
        NaturalRadioY.IsChecked = curveType == CurveType.Natural;
        LinearRadioY.IsChecked = curveType == CurveType.Linear;
        PowerRadioY.IsChecked = curveType == CurveType.Power;
        SigmoidRadioY.IsChecked = curveType == CurveType.Sigmoid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWMOUSE mouse;
        // Add padding or additional structs if needed for keyboard/hid, but we only need mouse here
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    private void RegisterRawInput(IntPtr hwnd)
    {
        RAWINPUTDEVICE rid = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,      // Generic Desktop
            usUsage = 0x02,          // Mouse
            dwFlags = 0x00000100,    // RIDEV_INPUTSINK (capture input even when not in foreground)
            hwndTarget = hwnd        // Your window handle
        };

        RAWINPUTDEVICE[] devices = new RAWINPUTDEVICE[] { rid };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(rid)))
        {
            throw new Exception("Failed to register raw input devices.");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        if (source != null)
        {
            RegisterRawInput(source.Handle);
            source.AddHook(WndProc);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("Failed to get HwndSource.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_INPUT = 0x00FF;
        if (msg == WM_INPUT)
        {
            ProcessRawInput(lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr lParam)
    {
        if (!bufferAllocated) return; // Safety check

        uint size = 0;
        GetRawInputData(lParam, 0x10000003 /* RID_INPUT */, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (size > 1024)
        {
            Debug.WriteLine("Raw input size exceeds buffer; input ignored.");
            return;
        }

        if (GetRawInputData(lParam, 0x10000003 /* RID_INPUT */, rawInputBuffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == size)
        {
            RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(rawInputBuffer);
            if (raw.header.dwType == 0) // RIM_TYPEMOUSE
            {
                if (raw.mouse.ulExtraInformation == SENT_INPUT_FLAG) return;

                int deltaX = raw.mouse.lLastX;
                int deltaY = raw.mouse.lLastY;
                accumulatedDeltaX += deltaX;
                accumulatedDeltaY += deltaY;
                if (stopwatch.Elapsed.TotalSeconds >= accumulationInterval)
                {
                    ProcessAccumulatedDeltas();
                }
            }
        }
        else
        {
            Debug.WriteLine("Failed to get raw input data: " + Marshal.GetLastWin32Error());
        }
    }

    private void ProcessAccumulatedDeltas()
    {
        if (accumulatedDeltaX == 0 && accumulatedDeltaY == 0)
        {
            return;
        }

        double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        // Calculate velocities from accumulated deltas (in counts/s)
        double velocityX = accumulatedDeltaX / elapsedSeconds;
        double velocityY = accumulatedDeltaY / elapsedSeconds;

        // Convert to degrees/s
        double velocityXDegrees = velocityX / countsPerDegree;
        double velocityYDegrees = velocityY / countsPerDegree;

        // Calculate sensitivity multipliers using degrees/s
        double sensitivityX = CalculateMain(xSettings, Math.Abs(velocityXDegrees));
        double sensitivityY = CalculateMain(ySettings, Math.Abs(velocityYDegrees));

        int adjustedDeltaX;
        int adjustedDeltaY;

        // X-axis processing
        if (sensitivityX == 0)
        {
            adjustedDeltaX = 0;
            fractionalX = 0;
        }
        else
        {
            double adjustedDeltaXDouble = accumulatedDeltaX * sensitivityX + fractionalX;
            adjustedDeltaX = (int)Math.Round(adjustedDeltaXDouble);
            fractionalX = adjustedDeltaXDouble - adjustedDeltaX;
        }

        // Y-axis processing
        if (sensitivityY == 0)
        {
            adjustedDeltaY = 0;
            fractionalY = 0;
        }
        else
        {
            double adjustedDeltaYDouble = accumulatedDeltaY * sensitivityY + fractionalY;
            adjustedDeltaY = (int)Math.Round(adjustedDeltaYDouble);
            fractionalY = adjustedDeltaYDouble - adjustedDeltaY;
        }

        // Store values for UI update
        lastRawX = accumulatedDeltaX;
        lastRawY = accumulatedDeltaY;
        lastAdjustedX = adjustedDeltaX;
        lastAdjustedY = adjustedDeltaY;

        // Send adjusted input
        SendMouseInput(adjustedDeltaX, adjustedDeltaY);

        // Reset accumulation
        accumulatedDeltaX = 0;
        accumulatedDeltaY = 0;
        stopwatch.Restart();
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)0x0200) // WM_MOUSEMOVE
        {
            if (g_sendingInput)
            {
                // Allow the event to pass through when sending input
                return CallNextHookEx(_hookID, nCode, wParam, lParam);
            }
            // Block the event otherwise
            return (IntPtr)1;
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private double CalculateMain(AxisSettings settings, double velocity)
    {
        if (velocity <= 0) return 0; // Early exit for zero or negative velocity

        double v = velocity;
        double K2 = settings.BaseSensitivity;
        double L2 = settings.TargetSensitivity;
        double N2 = settings.Offset;
        double M2 = settings.TargetCounts;
        double K3 = settings.Exponent;

        if (settings.UseGain)
        {
            switch (settings.CurveType)
            {
                case CurveType.Natural:
                    if (v <= N2) return K2;
                    if (v <= M2)
                    {
                        double G = settings.GainConstantC1 + L2 * v + (settings.NaturalCoeff / 3.0) * Math.Pow(v - M2, 3);
                        return G / v;
                    }
                    double G_above = settings.GainConstantC1 + L2 * v;
                    return G_above / v;

                case CurveType.Linear:
                    if (v <= N2) return K2;
                    if (v <= M2 || !settings.EnableLimit)
                    {
                        double G = K2 * v + (settings.LinearSlope / 2.0) * Math.Pow(v - N2, 2);
                        return G / v;
                    }
                    double G_M2 = K2 * M2 + (settings.LinearSlope / 2.0) * Math.Pow(M2 - N2, 2);
                    return (G_M2 + L2 * (v - M2)) / v;

                case CurveType.Power:
                    if (v <= N2) return K2;
                    if (v <= M2 || !settings.EnableLimit)
                    {
                        double G = K2 * v + (settings.PowerDeltaSensitivity / (K3 + 1)) *
                                   Math.Pow((v - N2) / settings.PowerVelocityRange, K3 + 1) *
                                   settings.PowerVelocityRange;
                        return G / v;
                    }
                    double G_M2_power = K2 * M2 + (settings.PowerDeltaSensitivity / (K3 + 1)) *
                                        Math.Pow((M2 - N2) / settings.PowerVelocityRange, K3 + 1) *
                                        settings.PowerVelocityRange;
                    return (G_M2_power + L2 * (v - M2)) / v;

                case CurveType.Sigmoid:
                    return CalculateSigmoidGain(settings, v);

                default:
                    return 1.0;
            }
        }
        else
        {
            switch (settings.CurveType)
            {
                case CurveType.Natural:
                    if (v <= N2) return K2;
                    if (v <= M2) return settings.NaturalCoeff * Math.Pow(v - M2, 2) + L2;
                    return L2;

                case CurveType.Linear:
                    if (v <= N2) return K2;
                    if (v <= M2 || !settings.EnableLimit) return K2 + settings.LinearSlope * (v - N2);
                    return L2;

                case CurveType.Power:
                    if (v <= N2) return K2;
                    if (v <= M2 || !settings.EnableLimit)
                        return K2 + settings.PowerDeltaSensitivity * Math.Pow((v - N2) / settings.PowerVelocityRange, K3);
                    return L2;

                case CurveType.Sigmoid:
                    return settings.K4 + (L2 - settings.K4) / (1 + Math.Exp(-settings.M4 * (v - N2)));

                default:
                    return 1.0;
            }
        }
    }

    private double CalculateSigmoidGain(AxisSettings settings, double v)
    {
        const int steps = 100; // Number of steps for numerical integration (adjustable)
        double stepSize = v / steps;
        double G = 0.0;

        for (int i = 1; i <= steps; i++)
        {
            double u1 = (i - 1) * stepSize; // Previous point
            double u2 = i * stepSize;       // Current point
            double s1 = settings.K4 + (settings.TargetSensitivity - settings.K4) / (1 + Math.Exp(-settings.M4 * (u1 - settings.Offset)));
            double s2 = settings.K4 + (settings.TargetSensitivity - settings.K4) / (1 + Math.Exp(-settings.M4 * (u2 - settings.Offset)));
            G += (s1 + s2) * stepSize / 2.0; // Trapezoidal rule
        }
        return G / v;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private void SendMouseInput(int dx, int dy)
    {
        g_sendingInput = true;
        try
        {
            Console.WriteLine($"Sending input: dx = {dx}, dy = {dy}");
            MouseInputs[0] = new INPUT
            {
                type = 0, // INPUT_MOUSE
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = 0x0001, // MOUSEEVENTF_MOVE
                    mouseData = 0,
                    time = 0,
                    dwExtraInfo = new IntPtr(SENT_INPUT_FLAG)
                }
            };

            if (SendInput(1, MouseInputs, Marshal.SizeOf(typeof(INPUT))) == 0)
            {
                Debug.WriteLine("SendInput failed: " + Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            g_sendingInput = false;
        }
    }
    private void UpdateGraph()
    {
        // Check if settings are initialized
        if (xSettings == null || ySettings == null) return;

        // Determine scaling based on maximum values
        double maxTargetCounts = Math.Max(xSettings.TargetCounts, ySettings.TargetCounts);
        double maxTargetSense = Math.Max(xSettings.TargetSensitivity, ySettings.TargetSensitivity);
        double xMax = 4 * maxTargetCounts; // X-axis to 4x the target counts
        double yMax = maxTargetSense + 1; // Y-axis to target sense + 1

        // Create a new plot model
        var plotModel = new PlotModel { Title = "Sensitivity" };

        // Generate X-axis curve points
        var xPoints = new List<DataPoint>();
        double step = xMax / 1000; // 1000 points for smoothness
        for (int i = 0; i <= 1000; i++)
        {
            double velocity = i * step;
            double sensitivity = CalculateMain(xSettings, velocity);
            xPoints.Add(new DataPoint(velocity, sensitivity));
        }
        var xSeries = new LineSeries
        {
            Title = "X Axis",
            Color = OxyColors.Blue,
            ItemsSource = xPoints
        };
        plotModel.Series.Add(xSeries);

        // Generate Y-axis curve points
        var yPoints = new List<DataPoint>();
        for (int i = 0; i <= 1000; i++)
        {
            double velocity = i * step;
            double sensitivity = CalculateMain(ySettings, velocity);
            yPoints.Add(new DataPoint(velocity, sensitivity));
        }
        var ySeries = new LineSeries
        {
            Title = "Y Axis",
            Color = OxyColors.Red,
            ItemsSource = yPoints
        };
        plotModel.Series.Add(ySeries);

        // Add horizontal reference line at sensitivity = 1
        var refLine = new LineSeries
        {
            Title = "Reality",
            Color = OxyColors.Gray,
            StrokeThickness = 1
        };
        refLine.Points.Add(new DataPoint(0, 1));
        refLine.Points.Add(new DataPoint(xMax, 1));
        plotModel.Series.Add(refLine);

        // Configure axes
        var xAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Input Speed (Degrees/s)",
            Minimum = 0,
            Maximum = xMax
        };
        var yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Gyro Ratio",
            Minimum = 0,
            Maximum = yMax
        };
        plotModel.Axes.Add(xAxis);
        plotModel.Axes.Add(yAxis);

        // Assign the model to the PlotView
        SensitivityPlot.Model = plotModel;
    }
    private double CalculateJolt(AxisSettings settings, double velocity)
    {
        double v = velocity;

        if (settings.UseGain)
        {
            // For gain-based curves, sensitivity H(v) = G(v) / v, where G(v) = ∫ s_original(u) du
            // Derivative: dH/dv = (dG/dv * v - G(v)) / v^2, and dG/dv = s_original(v)
            double s_original = CalculateOriginalSensitivity(settings, v);
            double G = CalculateIntegral(settings, v);
            return v > 0 ? (s_original * v - G) / (v * v) : 0; // Avoid division by zero
        }
        else
        {
            switch (settings.CurveType)
            {
                case CurveType.Natural:
                    if (v <= settings.Offset) return 0; // Constant sensitivity
                    if (v <= settings.TargetCounts)
                        return 2 * settings.NaturalCoeff * (v - settings.TargetCounts); // Quadratic term
                    return 0; // Constant beyond limit

                case CurveType.Linear:
                    if (v <= settings.Offset) return 0; // Constant
                    if (v <= settings.TargetCounts || !settings.EnableLimit) return settings.LinearSlope; // Slope
                    return 0; // Constant beyond limit

                case CurveType.Power:
                    if (v <= settings.Offset) return 0; // Constant
                    if (v <= settings.TargetCounts || !settings.EnableLimit)
                    {
                        double term = (v - settings.Offset) / settings.PowerVelocityRange;
                        return settings.PowerDeltaSensitivity * settings.Exponent *
                               Math.Pow(term, settings.Exponent - 1) / settings.PowerVelocityRange;
                    }
                    return 0; // Constant beyond limit

                case CurveType.Sigmoid:
                    double x = settings.M4 * (v - settings.Offset);
                    double sigma = Sigmoid(x);
                    return (settings.TargetSensitivity - settings.K4) * settings.M4 * sigma * (1 - sigma);

                default:
                    return 0;
            }
        }
    }
    private double CalculateOriginalSensitivity(AxisSettings settings, double v)
    {
        switch (settings.CurveType)
        {
            case CurveType.Natural:
                if (v <= settings.Offset) return settings.BaseSensitivity;
                if (v <= settings.TargetCounts) return settings.NaturalCoeff * Math.Pow(v - settings.TargetCounts, 2) + settings.TargetSensitivity;
                return settings.TargetSensitivity;

            case CurveType.Linear:
                if (v <= settings.Offset) return settings.BaseSensitivity;
                if (v <= settings.TargetCounts || !settings.EnableLimit) return settings.BaseSensitivity + settings.LinearSlope * (v - settings.Offset);
                return settings.TargetSensitivity;

            case CurveType.Power:
                if (v <= settings.Offset) return settings.BaseSensitivity;
                if (v <= settings.TargetCounts || !settings.EnableLimit)
                    return settings.BaseSensitivity + settings.PowerDeltaSensitivity * Math.Pow((v - settings.Offset) / settings.PowerVelocityRange, settings.Exponent);
                return settings.TargetSensitivity;

            case CurveType.Sigmoid:
                return settings.K4 + (settings.TargetSensitivity - settings.K4) / (1 + Math.Exp(-settings.M4 * (v - settings.Offset)));

            default:
                return 1.0;
        }
    }

    private double CalculateIntegral(AxisSettings settings, double v)
    {
        const int steps = 100;
        double stepSize = v / steps;
        double G = 0.0;

        for (int i = 1; i <= steps; i++)
        {
            double u1 = (i - 1) * stepSize;
            double u2 = i * stepSize;
            double s1 = CalculateOriginalSensitivity(settings, u1);
            double s2 = CalculateOriginalSensitivity(settings, u2);
            G += (s1 + s2) * stepSize / 2.0; // Trapezoidal rule
        }
        return G;
    }
    private void UpdateJoltGraph()
    {
        if (xSettings == null || ySettings == null) return;

        // Determine the X-axis range
        double maxTargetCounts = Math.Max(xSettings.TargetCounts, ySettings.TargetCounts);
        double xMax = 4 * maxTargetCounts;

        var plotModel = new PlotModel { Title = "Jolt (ds/dv)" };

        // Generate X-axis jolt curve points and find max jolt
        var xPoints = new List<DataPoint>();
        double step = xMax / 1000;
        double maxJoltX = double.MinValue;
        for (int i = 0; i <= 1000; i++)
        {
            double velocity = i * step;
            double jolt = CalculateJolt(xSettings, velocity);
            xPoints.Add(new DataPoint(velocity, jolt));
            if (jolt > maxJoltX) maxJoltX = jolt;
        }
        plotModel.Series.Add(new LineSeries { Title = "X Axis", Color = OxyColors.Blue, ItemsSource = xPoints });

        // Generate Y-axis jolt curve points and find max jolt
        var yPoints = new List<DataPoint>();
        double maxJoltY = double.MinValue;
        for (int i = 0; i <= 1000; i++)
        {
            double velocity = i * step;
            double jolt = CalculateJolt(ySettings, velocity);
            yPoints.Add(new DataPoint(velocity, jolt));
            if (jolt > maxJoltY) maxJoltY = jolt;
        }
        plotModel.Series.Add(new LineSeries { Title = "Y Axis", Color = OxyColors.Red, ItemsSource = yPoints });

        // Calculate the overall maximum jolt and add 10% headroom
        double maxJolt = Math.Max(maxJoltX, maxJoltY);
        double yMax = maxJolt * 1.1; // 10% extra space

        // Configure axes
        plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Input Speed (Degrees/s)",
            Minimum = 0,
            Maximum = xMax
        });
        plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Jolt (ds/dv)",
            Minimum = 0, // Adjust if jolt can be negative
            Maximum = yMax
        });

        // Assign to the PlotView
        JoltPlot.Model = plotModel;
    }
    private double Sigmoid(double x)
    {
        return 1.0 / (1.0 + Math.Exp(-x));
    }
    private void UpdateVelocityGraph()
    {
        if (xSettings == null || ySettings == null) return;

        // Determine the X-axis range (same as other graphs)
        double maxTargetCounts = Math.Max(xSettings.TargetCounts, ySettings.TargetCounts);
        double xMax = 4 * maxTargetCounts;

        var plotModel = new PlotModel { Title = "Velocity (Output vs Input)" };

        // Generate X-axis velocity curve points and find max velocity
        var xPoints = new List<DataPoint>();
        double step = xMax / 1000; // 1000 points for smoothness
        double maxVelocityX = double.MinValue;
        for (int i = 0; i <= 1000; i++)
        {
            double velocity = i * step;
            double sensitivity = CalculateMain(xSettings, velocity);
            double adjustedVelocity = xSettings.UseGain ? CalculateIntegral(xSettings, velocity) : sensitivity * velocity;
            xPoints.Add(new DataPoint(velocity, adjustedVelocity));
            if (adjustedVelocity > maxVelocityX) maxVelocityX = adjustedVelocity;
        }
        plotModel.Series.Add(new LineSeries { Title = "X Axis", Color = OxyColors.Blue, ItemsSource = xPoints });

        // Generate Y-axis velocity curve points and find max velocity
        var yPoints = new List<DataPoint>();
        double maxVelocityY = double.MinValue;
        for (int i = 0; i <= 1000; i++)
        {
            double velocity = i * step;
            double sensitivity = CalculateMain(ySettings, velocity);
            double adjustedVelocity = ySettings.UseGain ? CalculateIntegral(ySettings, velocity) : sensitivity * velocity;
            yPoints.Add(new DataPoint(velocity, adjustedVelocity));
            if (adjustedVelocity > maxVelocityY) maxVelocityY = adjustedVelocity;
        }
        plotModel.Series.Add(new LineSeries { Title = "Y Axis", Color = OxyColors.Red, ItemsSource = yPoints });

        // Add a reference line (y = x) for unadjusted velocity
        var refLine = new LineSeries
        {
            Title = "Unadjusted",
            Color = OxyColors.Gray,
            StrokeThickness = 1
        };
        refLine.Points.Add(new DataPoint(0, 0));
        refLine.Points.Add(new DataPoint(xMax, xMax));
        plotModel.Series.Add(refLine);

        // Calculate the overall maximum velocity and add 10% headroom
        double maxVelocity = Math.Max(maxVelocityX, maxVelocityY);
        double yMax = maxVelocity * 1.1;

        // Configure axes
        plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Input Speed (Degrees/s)",
            Minimum = 0,
            Maximum = xMax
        });
        plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Output Speed (Counts/s)",
            Minimum = 0,
            Maximum = yMax
        });

        // Assign to the PlotView
        VelocityPlot.Model = plotModel;
    }
    protected override void OnClosed(EventArgs e)
    {
        SaveLastSettings();
        trayIcon.Dispose();
        UnhookWindowsHookEx(_hookID);
        if (bufferAllocated)
        {
            Marshal.FreeHGlobal(rawInputBuffer);
            bufferAllocated = false;
        }
        base.OnClosed(e);
    }
}