using F1_widgets.Models;   // DriverStanding
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;





namespace F1_widgets
{
    public partial class MainWindow : Window
    {


        private DispatcherTimer _countdownTimer;
        private DateTime _nextSessionLocal;
        private string _nextSessionName;


        // =============================
        // =======  时间转换工具  =======
        // =============================

        /// <summary>
        /// 通用 UTC 时间解析（支持 ISO / ICS）
        /// </summary>
        private DateTime? ParseAnyUtc(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            input = input.Trim();

            // ① ICS：无秒  20250314T0030Z
            const string ics1 = "yyyyMMdd'T'HHmm'Z'";
            if (DateTime.TryParseExact(input, ics1,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt1))
                return DateTime.SpecifyKind(dt1, DateTimeKind.Utc);

            // ② ICS：有秒  20250314T003000Z
            const string ics2 = "yyyyMMdd'T'HHmmss'Z'";
            if (DateTime.TryParseExact(input, ics2,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt2))
                return DateTime.SpecifyKind(dt2, DateTimeKind.Utc);

            // ③ 标准 ISO 8601  2025-03-14T00:30:00Z
            if (DateTime.TryParse(input,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dt3))
                return DateTime.SpecifyKind(dt3, DateTimeKind.Utc);

            return null;
        }

        /// <summary>
        /// UTC 字符串 → 本地时区（Halifax 或任意系统时区）
        /// </summary>
        private string ToLocalString(string input)
        {
            var dt = ParseAnyUtc(input);
            if (dt == null) return "—";
            return dt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        // =============================
        // =========  Worker DTO =========
        // =============================

        private class RaceSessions
        {
            public string fp1 { get; set; }
            public string fp2 { get; set; }
            public string fp3 { get; set; }
            public string qualifying { get; set; }
            public string sprint { get; set; }
            public string race { get; set; }
        }

        private class RaceRound
        {
            public int round { get; set; }
            public string name { get; set; }
            public string circuit { get; set; }
            public string country { get; set; }
            public string date { get; set; }
            public RaceSessions sessions { get; set; }
        }

        private class SeasonData
        {
            public string season { get; set; }
            public List<RaceRound> rounds { get; set; }
        }

        // =============================
        // ====== 字段 / 构造函数 ======
        // =============================

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly ObservableCollection<DriverStanding> _standings =
            new ObservableCollection<DriverStanding>();

        private readonly DispatcherTimer _refreshTimer;
        private const string WORKER_URL = "https://f1-calendar-api.williamchu0605.workers.dev/2025";

        public MainWindow()
        {
            InitializeComponent();

            StandingList.ItemsSource = _standings;

            this.Width = 340;
            this.Height = 520;
            this.Left = 200;
            this.Top = 200;
            this.Topmost = false;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _refreshTimer.Tick += async (_, __) => await RefreshAllAsync();

            Loaded += async (_, __) =>
            {
                await RefreshAllAsync();
                _refreshTimer.Start();
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.Activate();
        }

        // =============================
        // ========= 拖动窗口 ==========
        // =============================

        private void WindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        // =============================
        // ========= 总刷新 ============
        // =============================

        private async Task RefreshAllAsync()
        {
            try
            {
                await LoadNextRaceAsync();
                await LoadDriverStandingsAsync(); // 占位
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Refresh error: {ex}");
            }
        }

        // =============================
        // ========= 加载赛历 ==========
        // =============================

        private async Task<SeasonData> LoadSeasonAsync()
        {
            string json = await _httpClient.GetStringAsync(WORKER_URL);
            return JsonSerializer.Deserialize<SeasonData>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private async Task LoadNextRaceAsync()
        {
            SeasonData season;

            try
            {
                season = await LoadSeasonAsync();
            }
            catch
            {
                ShowErrorUI();
                return;
            }

            if (season?.rounds == null || season.rounds.Count == 0)
            {
                ShowErrorUI();
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;

            var next = season.rounds
                .Where(r => !string.IsNullOrWhiteSpace(r.sessions?.race))
                .Select(r => new
                {
                    Round = r,
                    RaceUtc = ParseAnyUtc(r.sessions.race) ?? DateTime.MinValue
                })
                .Where(x => x.RaceUtc > nowUtc)
                .OrderBy(x => x.RaceUtc)
                .FirstOrDefault();

            if (next == null)
            {
                ShowErrorUI();
                return;
            }

            RaceRound r = next.Round;

            UpdateUIWithRace(r);

            LoadTrackImage(r.circuit, r.name);

            StartCountdownForNextSession(r.sessions);

        }

        private void ShowErrorUI()
        {
            Dispatcher.Invoke(() =>
            {
                GrandPrixNameText.Text = "Schedule unavailable";
                LocationText.Text = "";
                RaceTimeText.Text = "—";
                FP1Text.Text = FP2Text.Text = FP3Text.Text = "—";
                QualyText.Text = SprintText.Text = "—";
            });
        }

        // =============================
        // ==== 根据 Sprint 周末动态排布 ====
        // =============================

        private void UpdateUIWithRace(RaceRound r)
        {
            Dispatcher.Invoke(() =>
            {
                GrandPrixNameText.Text = r.name;
                LocationText.Text = $"{r.circuit}, {r.country}";
                RaceTimeText.Text = ToLocalString(r.sessions.race);

                bool isSprint = !string.IsNullOrWhiteSpace(r.sessions.sprint);

                if (isSprint)
                {
                    // =======================
                    //   Sprint 周末格式：
                    //   FP1
                    //   Sprint Qualifying
                    //   Sprint
                    //   Qualifying（主排位）
                    // =======================

                    RowFP1.Visibility = Visibility.Visible;
                    FP1Text.Text = ToLocalString(r.sessions.fp1);

                    RowFP2.Visibility = Visibility.Visible;
                    RowFP2Label().Text = "Sprint Qualifying";
                    FP2Text.Text = ToLocalString(r.sessions.qualifying);

                    RowFP3.Visibility = Visibility.Visible;
                    RowFP3Label().Text = "Sprint";
                    FP3Text.Text = ToLocalString(r.sessions.sprint);

                    RowQualy.Visibility = Visibility.Visible;
                    QualyText.Text = ToLocalString(r.sessions.qualifying);

                    RowSprint.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // 普通周末
                    RowFP1.Visibility = Visibility.Visible;
                    RowFP2.Visibility = Visibility.Visible;
                    RowFP3.Visibility = Visibility.Visible;
                    RowQualy.Visibility = Visibility.Visible;
                    RowSprint.Visibility = Visibility.Visible;

                    FP1Text.Text = ToLocalString(r.sessions.fp1);
                    FP2Text.Text = ToLocalString(r.sessions.fp2);
                    FP3Text.Text = ToLocalString(r.sessions.fp3);
                    QualyText.Text = ToLocalString(r.sessions.qualifying);
                    SprintText.Text = ToLocalString(r.sessions.sprint);
                }
            });
        }

        private TextBlock RowFP2Label()
            => RowFP2.Children.OfType<TextBlock>().First();

        private TextBlock RowFP3Label()
            => RowFP3.Children.OfType<TextBlock>().First();

        // =============================
        // ========= 积分榜（占位） ====
        // =============================

        private async Task LoadDriverStandingsAsync()
        {
            try
            {
                string url = "https://f1-driver-standing.williamchu0605.workers.dev/"; 

                using HttpClient client = new HttpClient();
                string json = await client.GetStringAsync(url);

                // 解析 JSON
                var result = JsonSerializer.Deserialize<DriverStandingsResponse>(json);

                Dispatcher.Invoke(() =>
                {
                    _standings.Clear();

                    if (result?.drivers != null && result.drivers.Any())
                    {
                        foreach (var d in result.drivers)
                        {
                            _standings.Add(new DriverStanding
                            {
                                DriverName = $"{d.position}. {d.driver}",
                                Points = d.points
                            });
                        }
                    }
                    else
                    {
                        // 没有数据
                        _standings.Add(new DriverStanding
                        {
                            DriverName = "Standings unavailable",
                            Points = 0
                        });
                    }
                });
            }
            catch
            {
                // 出错时显示占位
                Dispatcher.Invoke(() =>
                {
                    _standings.Clear();
                    _standings.Add(new DriverStanding
                    {
                        DriverName = "Standings unavailable",
                        Points = 0
                    });
                });
            }
        }

        public class DriverStandingsResponse
        {
            public int season { get; set; }
            public string last_updated { get; set; }
            public List<DriverInfo> drivers { get; set; }
        }

        public class DriverInfo
        {
            public int position { get; set; }
            public string driver { get; set; }
            public int points { get; set; }
            public string team { get; set; }
        }



        // =============================
        // ========= 赛道图 ============
        // =============================

        private void LoadTrackImage(string circuitIdOrName, string gpName)
        {
            TrackImage.Visibility = Visibility.Collapsed;
            TrackPlaceholderText.Visibility = Visibility.Visible;
            TrackImage.Source = null;

            string tracksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tracks");

            if (!Directory.Exists(tracksDir)) return;

            string Normalize(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                return string.Join("_",
                            s.Split(Path.GetInvalidFileNameChars(),
                                StringSplitOptions.RemoveEmptyEntries))
                        .Replace(' ', '_')
                        .ToLowerInvariant();
            }

            var candidates = new[]
            {
                $"{Normalize(circuitIdOrName)}.png",
                $"{Normalize(circuitIdOrName)}.jpg",
                $"{Normalize(gpName)}.png",
                $"{Normalize(gpName)}.jpg",
            }.Select(f => Path.Combine(tracksDir, f)).Where(File.Exists).ToList();

            if (!candidates.Any()) return;

            try
            {
                var bitmap = new BitmapImage(new Uri(candidates.First()));
                TrackImage.Source = bitmap;
                TrackImage.Visibility = Visibility.Visible;
                TrackPlaceholderText.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        // =============================
        // ========= 右键菜单 ==========
        // =============================

        private async void RefreshNow_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        private void LanguageEnglish_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.ChangeLanguage("en");
            RestartMainWindow();
        }

        private void LanguageChinese_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.ChangeLanguage("zh-CN");
            RestartMainWindow();
        }

        private void RestartMainWindow()
        {
            var newWin = new MainWindow
            {
                Left = this.Left,
                Top = this.Top
            };
            newWin.Show();
            this.Close();
        }


        private void EnableAutostart_Click(object sender, RoutedEventArgs e)
        {
            EnableAutoStart();
            MessageBox.Show("Auto start enabled.");
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // =============================
        // ========= 开机自启 ==========
        // =============================

        private void EnableAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

                if (key == null) return;

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exePath)) return;

                key.SetValue("F1Widget", $"\"{exePath}\"");
            }
            catch { }
        }

        private void StartCountdownForNextSession(RaceSessions s)
        {
            _countdownTimer?.Stop();

            bool isSprint = !string.IsNullOrWhiteSpace(s.sprint);

            List<(string name, string time)> list;

            if (isSprint)
            {
                // SPRINT 周末
                list = new List<(string, string)>
        {
            ("Practice 1", s.fp1),
            ("Sprint Qualifying", s.qualifying),
            ("Sprint", s.sprint),
            ("Qualifying", s.qualifying),   // 主排位
            ("Race", s.race)
        };
            }
            else
            {
                // 普通周末
                list = new List<(string, string)>
        {
            ("Practice 1", s.fp1),
            ("Practice 2", s.fp2),
            ("Practice 3", s.fp3),
            ("Qualifying", s.qualifying),
            ("Race", s.race)
        };
            }

            DateTime now = DateTime.Now;

            foreach (var item in list)
            {
                var t = ParseAnyUtc(item.time);
                if (t == null) continue;

                var local = t.Value.ToLocalTime();

                if (local > now)
                {
                    _nextSessionLocal = local;
                    _nextSessionName = item.name;

                    CountdownText.Text = $"{_nextSessionName} starts in …";
                    StartCountdownTimer();
                    return;
                }
            }

            CountdownText.Text = "Season finished";
        }


        private void StartCountdownTimer()
        {
            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1);
            _countdownTimer.Tick += (s, e) =>
            {
                TimeSpan diff = _nextSessionLocal - DateTime.Now;

                if (diff.TotalSeconds <= 0)
                {
                    CountdownText.Text = $"{_nextSessionName} is starting now!";
                    _countdownTimer.Stop();
                    return;
                }

                CountdownText.Text =
                    $"{_nextSessionName} starts in {diff.Hours:D2}:{diff.Minutes:D2}:{diff.Seconds:D2}";
            };

            _countdownTimer.Start();
        }

    }
}
