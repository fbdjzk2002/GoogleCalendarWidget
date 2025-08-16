using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Newtonsoft.Json;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace GoogleCalendarWidget
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        // 시스템 트레이 관련 Win32 API
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        static extern IntPtr CreateIconFromResource(byte[] presbits, uint dwResSize, bool fIcon, uint dwVer);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        private const uint GW_HWNDPREV = 3;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;

        // 시스템 트레이 상수
        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIM_SETFOCUS = 0x00000003;
        private const uint NIM_SETVERSION = 0x00000004;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_STATE = 0x00000008;
        private const uint NIF_INFO = 0x00000010;
        private const uint NIF_GUID = 0x00000020;

        private const uint WM_USER = 0x0400;
        private const uint WM_TRAYICON = WM_USER + 1;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;

        private IntPtr _handle;
        private bool _trayIconAdded = false;
        private uint _trayIconId = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        #endregion

        private CalendarService _service;
        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _positionTimer;
        private ObservableCollection<CalendarEventItem> _selectedDateEvents;
        private ObservableCollection<CalendarDayItem> _calendarDays;
        private ObservableCollection<CalendarListItem> _availableCalendars;
        private Dictionary<DateTime, List<Event>> _eventsCache;
        private DateTime _currentMonth;
        private DateTime _selectedDate;
        private bool _isAuthenticated;
        private bool _isPinned;
        private bool _isCompactMode;
        private bool _isSettingsOpen;
        private string _statusMessage;
        private string _currentMonthDisplay;
        private double _windowOpacity = 0.95;
        private bool _isResizing;
        private System.Windows.Point _resizeStartPoint;
        private System.Windows.Size _resizeStartSize;
        private UserSettings _userSettings;
        private bool _isLoadingSettings = false;

        public ObservableCollection<CalendarEventItem> SelectedDateEvents
        {
            get => _selectedDateEvents;
            set { _selectedDateEvents = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CalendarDayItem> CalendarDays
        {
            get => _calendarDays;
            set { _calendarDays = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CalendarListItem> AvailableCalendars
        {
            get => _availableCalendars;
            set { _availableCalendars = value; OnPropertyChanged(); }
        }

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                _selectedDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDateDisplay));
                LoadEventsForSelectedDate();
            }
        }

        public string SelectedDateDisplay => SelectedDate.ToString("yyyy년 M월 d일 dddd", new CultureInfo("ko-KR"));

        public string CurrentMonthDisplay
        {
            get => _currentMonthDisplay;
            set { _currentMonthDisplay = value; OnPropertyChanged(); }
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            set { _isAuthenticated = value; OnPropertyChanged(); }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                _isPinned = value;
                SetWindowPosition(value);
                OnPropertyChanged();
                if (!_isLoadingSettings) SaveSettings();
            }
        }

        public bool IsCompactMode
        {
            get => _isCompactMode;
            set
            {
                _isCompactMode = value;
                UpdateWindowSize();
                OnPropertyChanged();
                if (!_isLoadingSettings) SaveSettings();
            }
        }

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set { _isSettingsOpen = value; OnPropertyChanged(); }
        }

        public double WindowOpacity
        {
            get => _windowOpacity;
            set
            {
                _windowOpacity = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public ICommand PreviousMonthCommand { get; }
        public ICommand NextMonthCommand { get; }
        public ICommand TodayCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand AddEventCommand { get; }
        public ICommand EditEventCommand { get; }
        public ICommand DeleteEventCommand { get; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 초기화
            _eventsCache = new Dictionary<DateTime, List<Event>>();
            SelectedDateEvents = new ObservableCollection<CalendarEventItem>();
            CalendarDays = new ObservableCollection<CalendarDayItem>();
            AvailableCalendars = new ObservableCollection<CalendarListItem>();
            _currentMonth = DateTime.Today;
            SelectedDate = DateTime.Today;
            IsSettingsOpen = false;

            // 커맨드 초기화
            PreviousMonthCommand = new RelayCommand(_ => ChangeMonth(-1));
            NextMonthCommand = new RelayCommand(_ => ChangeMonth(1));
            TodayCommand = new RelayCommand(_ => GoToToday());
            ToggleSettingsCommand = new RelayCommand(_ => ToggleSettings());
            AddEventCommand = new RelayCommand(_ => ShowEventDialog());
            EditEventCommand = new RelayCommand(param => ShowEventDialog(param as CalendarEventItem));
            DeleteEventCommand = new RelayCommand(async param => await DeleteEvent(param as CalendarEventItem));

            // 윈도우 설정
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
            this.ResizeMode = ResizeMode.NoResize;
            this.ShowInTaskbar = false;

            // 기본 윈도우 크기 및 위치 설정 (설정 로드 전)
            this.Width = 800;
            this.Height = 500;
            this.Left = SystemParameters.PrimaryScreenWidth - this.Width - 20;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;

            // 설정 로드 (저장된 설정이 있으면 위 기본값들을 덮어씀)
            LoadSettings();

            // 시스템 트레이 아이콘 설정 (Win32 API 사용)
            InitializeSystemTray();

            // 타이머 설정 (10분마다 새로고침)
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromMinutes(10);
            _refreshTimer.Tick += async (s, e) => await LoadMonthEvents();

            // 초기 로드
            GenerateCalendarDays();
            _ = InitializeGoogleCalendar();
        }

        private void InitializeSystemTray()
        {
            // Win32 API를 사용한 시스템 트레이 아이콘 추가는 윈도우 핸들이 생성된 후에 실행
            // OnSourceInitialized에서 실제 구현
        }

        private void AddTrayIcon()
        {
            if (_handle == IntPtr.Zero || _trayIconAdded) return;

            try
            {
                var nid = new NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf(nid);
                nid.hWnd = _handle;
                nid.uID = _trayIconId;
                nid.uFlags = NIF_MESSAGE | NIF_TIP | NIF_ICON;
                nid.uCallbackMessage = WM_TRAYICON;
                nid.szTip = "Google Calendar Widget";
                
                // 커스텀 아이콘 로드
                nid.hIcon = LoadTrayIcon();

                if (Shell_NotifyIcon(NIM_ADD, ref nid))
                {
                    _trayIconAdded = true;
                    
                    // 윈도우 메시지 후킹 설정
                    HwndSource source = HwndSource.FromHwnd(_handle);
                    source.AddHook(WndProc);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"시스템 트레이 초기화 실패: {ex.Message}";
            }
        }

        private IntPtr LoadTrayIcon()
        {
            try
            {
                // 방법 1: favicon.ico 파일에서 로드
                if (File.Exists("favicon.ico"))
                {
                    IntPtr hIcon = LoadImage(IntPtr.Zero, Path.GetFullPath("favicon.ico"), 
                        IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (hIcon != IntPtr.Zero)
                        return hIcon;
                }

                // 방법 2: 애플리케이션 실행 파일의 아이콘 사용
                IntPtr hInstance = GetModuleHandle(null);
                IntPtr appIcon = LoadIcon(hInstance, new IntPtr(32512));
                if (appIcon != IntPtr.Zero)
                    return appIcon;

                // 방법 3: 프로그래밍으로 아이콘 생성
                return CreateCustomIcon();
            }
            catch
            {
                // 방법 4: 기본 시스템 아이콘
                return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
            }
        }

        private IntPtr CreateCustomIcon()
        {
            try
            {
                // 16x16 아이콘 데이터 생성 (간단한 파란색 사각형)
                byte[] iconData = new byte[]
                {
                    // ICO 헤더
                    0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
                    // 이미지 디렉토리 엔트리
                    0x10, 0x10, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00,
                    0x68, 0x04, 0x00, 0x00, 0x16, 0x00, 0x00, 0x00,
                    // BITMAPINFOHEADER
                    0x28, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,
                    0x20, 0x00, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                };

                // 픽셀 데이터 추가 (16x16 x 4바이트 = 1024바이트)
                var pixelData = new byte[1024];
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int index = (y * 16 + x) * 4;
                        if (x >= 2 && x <= 13 && y >= 2 && y <= 13)
                        {
                            // Google Blue (RGB: 66, 133, 244)
                            pixelData[index] = 244;     // Blue
                            pixelData[index + 1] = 133; // Green  
                            pixelData[index + 2] = 66;  // Red
                            pixelData[index + 3] = 255; // Alpha
                        }
                    }
                }

                var fullIconData = new byte[iconData.Length + pixelData.Length];
                Array.Copy(iconData, fullIconData, iconData.Length);
                Array.Copy(pixelData, 0, fullIconData, iconData.Length, pixelData.Length);

                return CreateIconFromResource(fullIconData, (uint)fullIconData.Length, true, 0x00030000);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void RemoveTrayIcon()
        {
            if (!_trayIconAdded || _handle == IntPtr.Zero) return;

            try
            {
                var nid = new NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf(nid);
                nid.hWnd = _handle;
                nid.uID = _trayIconId;

                Shell_NotifyIcon(NIM_DELETE, ref nid);
                _trayIconAdded = false;
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                switch ((uint)lParam)
                {
                    case WM_LBUTTONDBLCLK:
                        ToggleVisibility();
                        handled = true;
                        break;
                    case WM_RBUTTONUP:
                        ShowTrayContextMenu();
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private void ShowTrayContextMenu()
        {
            try
            {
                // WPF ContextMenu 생성
                var contextMenu = new ContextMenu();

                var showMenuItem = new MenuItem { Header = "보이기/숨기기" };
                showMenuItem.Click += (s, e) => ToggleVisibility();

                var pinMenuItem = new MenuItem { Header = "바탕화면 고정", IsCheckable = true };
                pinMenuItem.Click += (s, e) =>
                {
                    IsPinned = !IsPinned;
                    pinMenuItem.IsChecked = IsPinned;
                    SaveSettings();
                };
                pinMenuItem.IsChecked = _userSettings?.IsPinned ?? false;

                var compactMenuItem = new MenuItem { Header = "컴팩트 모드", IsCheckable = true };
                compactMenuItem.Click += (s, e) =>
                {
                    IsCompactMode = !IsCompactMode;
                    compactMenuItem.IsChecked = IsCompactMode;
                    SaveSettings();
                };
                compactMenuItem.IsChecked = _userSettings?.IsCompactMode ?? false;

                var settingsMenuItem = new MenuItem { Header = "캘린더 선택" };
                settingsMenuItem.Click += (s, e) =>
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    IsSettingsOpen = true;
                };

                var refreshMenuItem = new MenuItem { Header = "새로고침" };
                refreshMenuItem.Click += async (s, e) => await LoadMonthEvents();

                var exitMenuItem = new MenuItem { Header = "종료" };
                exitMenuItem.Click += (s, e) => ExitApplication();

                contextMenu.Items.Add(showMenuItem);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(pinMenuItem);
                contextMenu.Items.Add(compactMenuItem);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(settingsMenuItem);
                contextMenu.Items.Add(refreshMenuItem);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(exitMenuItem);

                // 현재 마우스 위치에서 컨텍스트 메뉴 표시
                contextMenu.IsOpen = true;

                // 포커스를 위해 윈도우를 잠시 포그라운드로
                SetForegroundWindow(_handle);
            }
            catch (Exception ex)
            {
                StatusMessage = $"컨텍스트 메뉴 오류: {ex.Message}";
            }
        }

        private void ToggleVisibility()
        {
            if (this.IsVisible)
            {
                this.Hide();
            }
            else
            {
                this.Show();
                this.WindowState = WindowState.Normal;

                // 핀 고정 상태에 따라 위치 설정
                if (IsPinned)
                {
                    SetWindowPosition(true);
                }
            }
        }

        private void ExitApplication()
        {
            SaveSettings();
            RemoveTrayIcon();
            _refreshTimer?.Stop();
            _positionTimer?.Stop();
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 윈도우 핸들 가져오기
            _handle = new WindowInteropHelper(this).Handle;

            // Win32 API 시스템 트레이 아이콘 추가
            AddTrayIcon();

            // 초기 설정 적용 (LoadSettings에서 이미 IsPinned가 설정됨)
            SetWindowPosition(IsPinned);
        }

        private void SetWindowPosition(bool pinToDesktop)
        {
            if (_handle == IntPtr.Zero) return;

            if (pinToDesktop)
            {
                // 바탕화면 바로 위에 고정 (다른 창들 아래)
                this.Topmost = false;

                // 타이머로 주기적으로 최하단 유지
                if (_positionTimer == null)
                {
                    _positionTimer = new DispatcherTimer();
                    _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _positionTimer.Tick += (s, e) =>
                    {
                        if (IsPinned && IsVisible)
                        {
                            // 현재 창 바로 아래에 있는 창 찾기
                            IntPtr nextWindow = GetWindow(_handle, GW_HWNDPREV);

                            // 다른 창이 아래에 있고 그 창이 보이는 상태라면
                            if (nextWindow != IntPtr.Zero && IsWindowVisible(nextWindow))
                            {
                                // 최하단으로 이동
                                SetWindowPos(_handle, HWND_BOTTOM, 0, 0, 0, 0,
                                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                            }
                        }
                    };
                    _positionTimer.Start();
                }

                // 즉시 최하단으로 이동
                SetWindowPos(_handle, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else
            {
                // 일반 모드: Topmost 해제, 일반 창처럼 동작
                this.Topmost = false;

                // 타이머 중지
                _positionTimer?.Stop();
                _positionTimer = null;

                // 일반 창 위치로 설정
                SetWindowPos(_handle, HWND_NOTOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // X 버튼 클릭 시 숨기기만 하고 종료하지 않음 (시스템 트레이로 최소화)
            e.Cancel = true;
            this.Hide();
        }

        private async Task InitializeGoogleCalendar()
        {
            try
            {
                StatusMessage = "Google Calendar 연결 중...";

                string[] scopes = {
                    CalendarService.Scope.Calendar,
                    CalendarService.Scope.CalendarEvents
                };
                string applicationName = "Google Calendar Widget";

                if (!File.Exists("credentials.json"))
                {
                    StatusMessage = "credentials.json 파일을 찾을 수 없습니다.";
                    IsAuthenticated = false;
                    return;
                }

                using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
                {
                    string credPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GoogleCalendarWidget");

                    var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true));

                    _service = new CalendarService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = applicationName,
                    });
                }

                IsAuthenticated = true;
                StatusMessage = "연결됨";
                await LoadCalendarList();
                await LoadMonthEvents();
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                StatusMessage = $"오류: {ex.Message}";
                IsAuthenticated = false;
            }
        }

        private async Task LoadCalendarList()
        {
            if (_service == null) return;

            try
            {
                var calendarListRequest = _service.CalendarList.List();
                var calendarList = await calendarListRequest.ExecuteAsync();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableCalendars.Clear();

                    foreach (var calendar in calendarList.Items)
                    {
                        var calendarItem = new CalendarListItem
                        {
                            Id = calendar.Id,
                            Summary = calendar.Summary,
                            Description = calendar.Description,
                            BackgroundColor = calendar.BackgroundColor ?? "#4285F4",
                            ForegroundColor = calendar.ForegroundColor ?? "#FFFFFF",
                            IsSelected = _userSettings?.SelectedCalendarIds?.Contains(calendar.Id) ?? calendar.Id == "primary",
                            IsPrimary = calendar.Id == "primary" || calendar.Primary == true
                        };

                        calendarItem.PropertyChanged += async (s, e) =>
                        {
                            if (e.PropertyName == nameof(CalendarListItem.IsSelected))
                            {
                                SaveSelectedCalendars();                           
                                await LoadMonthEvents();
                            }
                        };

                        AvailableCalendars.Add(calendarItem);
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"캘린더 목록 로드 실패: {ex.Message}";
            }
        }

        private void GenerateCalendarDays()
        {
            CalendarDays.Clear();
            CurrentMonthDisplay = _currentMonth.ToString("yyyy년 M월", new CultureInfo("ko-KR"));

            var firstDayOfMonth = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            var startDate = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek);

            for (int i = 0; i < 42; i++)
            {
                var date = startDate.AddDays(i);
                var dayItem = new CalendarDayItem
                {
                    Date = date,
                    Day = date.Day,
                    IsCurrentMonth = date.Month == _currentMonth.Month,
                    IsToday = date.Date == DateTime.Today,
                    IsSelected = date.Date == SelectedDate.Date,
                    HasEvents = false,
                    EventCount = 0,
                    EventColors = new List<string>()
                };

                CalendarDays.Add(dayItem);
            }
        }

        private async Task LoadMonthEvents()
        {
            if (_service == null) return;

            var selectedCalendars = AvailableCalendars.Where(c => c.IsSelected).ToList();

            _eventsCache.Clear();
            GenerateCalendarDays(); // 월 전체를 새로 그림

            try
            {
                StatusMessage = "일정 업데이트 중...";

                // 선택된 캘린더가 변경된 경우 UI 즉시 업데이트
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateCalendarDaysDisplay(selectedCalendars);
                    LoadEventsForSelectedDate();
                });

                if (!selectedCalendars.Any())
                {
                    StatusMessage = "선택된 캘린더가 없습니다.";
                    return;
                }

                // 선택된 캘린더만 새로 로드 (기존 캐시는 유지하되 필요시에만 업데이트)
                bool needsRefresh = _eventsCache.Count == 0 || 
                    selectedCalendars.Any(cal => !_eventsCache.Values.Any(events => 
                        events.Any(e => e.Organizer?.Email == cal.Id)));

                if (!needsRefresh)
                {
                    StatusMessage = $"업데이트: {DateTime.Now:HH:mm} ({selectedCalendars.Count}개 캘린더)";
                    return;
                }

                // 캐시 초기화는 실제로 새 데이터가 필요할 때만
                _eventsCache.Clear();

                var firstDay = CalendarDays.First().Date;
                var lastDay = CalendarDays.Last().Date;

                var allEvents = new List<Event>();

                foreach (var calendar in selectedCalendars)
                {
                    try
                    {
                        var request = _service.Events.List(calendar.Id);
                        request.TimeMin = firstDay;
                        request.TimeMax = lastDay.AddDays(1);
                        request.ShowDeleted = false;
                        request.SingleEvents = true;
                        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                        request.MaxResults = 250;

                        var events = await request.ExecuteAsync();

                        if (events.Items != null)
                        {
                            foreach (var item in events.Items)
                            {
                                item.Organizer = new Event.OrganizerData
                                {
                                    DisplayName = calendar.Summary,
                                    Email = calendar.Id
                                };
                                allEvents.Add(item);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"캘린더 {calendar.Summary} 로드 실패: {ex.Message}");
                    }
                }

                foreach (var eventItem in allEvents)
                {
                    var startDate = ParseDateTime(eventItem.Start).Date;

                    if (!_eventsCache.ContainsKey(startDate))
                        _eventsCache[startDate] = new List<Event>();

                    _eventsCache[startDate].Add(eventItem);
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateCalendarDaysDisplay(selectedCalendars);
                    LoadEventsForSelectedDate();
                    StatusMessage = $"업데이트: {DateTime.Now:HH:mm} ({selectedCalendars.Count}개 캘린더)";
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"로드 실패: {ex.Message}";
            }
        }

        private void UpdateCalendarDaysDisplay(List<CalendarListItem> selectedCalendars)
        {
            var selectedCalendarIds = selectedCalendars?.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet() ?? new HashSet<string>();

            foreach (var dayItem in CalendarDays)
            {
                dayItem.EventColors.Clear();

                // 항상 filteredEvents 기준으로만 점 표시
                var filteredEvents = _eventsCache.ContainsKey(dayItem.Date.Date)
                    ? _eventsCache[dayItem.Date.Date].Where(e => selectedCalendarIds.Contains(e.Organizer?.Email)).ToList()
                    : new List<Event>();

                if (filteredEvents.Any())
                {
                    dayItem.HasEvents = true;
                    dayItem.EventCount = filteredEvents.Count;
                    dayItem.EventColors = filteredEvents
                        .Select(e => GetCalendarColor(e.Organizer?.Email))
                        .Distinct()
                        .Take(3)
                        .ToList();
                }
                else
                {
                    dayItem.HasEvents = false;
                    dayItem.EventCount = 0;
                }
            }
        }

        private void LoadEventsForSelectedDate()
        {
            SelectedDateEvents.Clear();

            if (_eventsCache.ContainsKey(SelectedDate.Date))
            {
                // 선택된 캘린더만 필터링
                var selectedCalendarIds = AvailableCalendars
                    .Where(c => c.IsSelected)
                    .Select(c => c.Id)
                    .ToHashSet();

                var filteredEvents = _eventsCache[SelectedDate.Date]
                    .Where(e => selectedCalendarIds.Contains(e.Organizer?.Email))
                    .OrderBy(e => ParseDateTime(e.Start));

                foreach (var eventItem in filteredEvents)
                {
                    var calendarEvent = new CalendarEventItem
                    {
                        Id = eventItem.Id,
                        Title = eventItem.Summary ?? "제목 없음",
                        Description = eventItem.Description,
                        Location = eventItem.Location,
                        StartTime = ParseDateTime(eventItem.Start),
                        EndTime = ParseDateTime(eventItem.End),
                        IsAllDay = eventItem.Start.Date != null,
                        ColorId = GetEventColor(eventItem.ColorId) ?? GetCalendarColor(eventItem.Organizer?.Email),
                        CalendarName = eventItem.Organizer?.DisplayName ?? "기본 캘린더",
                        CalendarId = eventItem.Organizer?.Email ?? "primary"
                    };

                    SelectedDateEvents.Add(calendarEvent);
                }
            }
        }

        private string GetCalendarColor(string calendarId)
        {
            var calendar = AvailableCalendars.FirstOrDefault(c => c.Id == calendarId);
            return calendar?.BackgroundColor ?? "#4285F4";
        }

        private DateTime ParseDateTime(EventDateTime eventDateTime)
        {
            if (!string.IsNullOrEmpty(eventDateTime.DateTime?.ToString()))
                return eventDateTime.DateTime.Value;
            else if (!string.IsNullOrEmpty(eventDateTime.Date))
                return DateTime.Parse(eventDateTime.Date);
            return DateTime.Now;
        }

        private string GetEventColor(string colorId)
        {
            return colorId switch
            {
                "1" => "#7986CB",
                "2" => "#33B679",
                "3" => "#8E24AA",
                "4" => "#E67C73",
                "5" => "#F6BF26",
                "6" => "#F4511E",
                "7" => "#039BE5",
                "8" => "#616161",
                "9" => "#3F51B5",
                "10" => "#0B8043",
                "11" => "#D50000",
                _ => null
            };
        }

        private void ChangeMonth(int direction)
        {
            _currentMonth = _currentMonth.AddMonths(direction);
            _ = LoadMonthEvents();
        }

        private void GoToToday()
        {
            _currentMonth = DateTime.Today;
            SelectedDate = DateTime.Today;
            _ = LoadMonthEvents();
        }

        private void ToggleSettings()
        {
            IsSettingsOpen = !IsSettingsOpen;
        }

        private void UpdateWindowSize()
        {
            if (IsCompactMode)
            {
                this.Width = 400;
                this.Height = 450;
            }
            else
            {
                this.Width = 800;
                this.Height = 500;
            }
        }

        private void LoadSettings()
        {
            try
            {
                _isLoadingSettings = true;
                
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GoogleCalendarWidget",
                    "settings.json");

                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    _userSettings = JsonConvert.DeserializeObject<UserSettings>(json);
                    
                    // 디버깅: 로드된 설정 확인
                    System.Diagnostics.Debug.WriteLine($"Settings loaded: IsPinned={_userSettings.IsPinned}, Position=({_userSettings.WindowPosition?.X}, {_userSettings.WindowPosition?.Y}), Size=({_userSettings.WindowSize?.Width}x{_userSettings.WindowSize?.Height})");
                    
                    WindowOpacity = _userSettings.Opacity;
                    IsPinned = _userSettings.IsPinned;
                    IsCompactMode = _userSettings.IsCompactMode;

                    if (_userSettings.WindowPosition != null)
                    {
                        this.Left = _userSettings.WindowPosition.Value.X;
                        this.Top = _userSettings.WindowPosition.Value.Y;
                        System.Diagnostics.Debug.WriteLine($"Window position set to: ({this.Left}, {this.Top})");
                    }

                    if (_userSettings.WindowSize != null)
                    {
                        this.Width = _userSettings.WindowSize.Value.Width;
                        this.Height = _userSettings.WindowSize.Value.Height;
                        System.Diagnostics.Debug.WriteLine($"Window size set to: {this.Width}x{this.Height}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Settings file not found at: {settingsPath}");
                    _userSettings = new UserSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                _userSettings = new UserSettings();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                _userSettings.Opacity = WindowOpacity;
                _userSettings.IsPinned = IsPinned;
                _userSettings.IsCompactMode = IsCompactMode;
                _userSettings.WindowPosition = new Point(this.Left, this.Top);
                _userSettings.WindowSize = new Size(this.Width, this.Height);

                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GoogleCalendarWidget");

                Directory.CreateDirectory(settingsPath);

                var json = JsonConvert.SerializeObject(_userSettings, Formatting.Indented);
                var fullPath = Path.Combine(settingsPath, "settings.json");
                File.WriteAllText(fullPath, json);
                
                // 디버깅: 저장된 설정 확인
                System.Diagnostics.Debug.WriteLine($"Settings saved to: {fullPath}");
                System.Diagnostics.Debug.WriteLine($"Settings saved: IsPinned={_userSettings.IsPinned}, Position=({_userSettings.WindowPosition.X}, {_userSettings.WindowPosition.Y}), Size=({_userSettings.WindowSize.Width}x{_userSettings.WindowSize.Height})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void SaveSelectedCalendars()
        {
            _userSettings.SelectedCalendarIds = AvailableCalendars
                .Where(c => c.IsSelected)
                .Select(c => c.Id)
                .ToList();
            SaveSettings();
        }

        // Window 이벤트 핸들러
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPinned && !_isResizing)
            {
                this.DragMove();
                SaveSettings();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadMonthEvents();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            IsPinned = !IsPinned;
            SaveSettings();
        }

        private void SizeButton_Click(object sender, RoutedEventArgs e)
        {
            IsCompactMode = !IsCompactMode;
            SaveSettings();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSettings();
        }

        private void CalendarDay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is CalendarDayItem dayItem)
            {
                foreach (var day in CalendarDays)
                {
                    day.IsSelected = false;
                }

                dayItem.IsSelected = true;
                SelectedDate = dayItem.Date;
            }
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(this);
            _resizeStartSize = new System.Windows.Size(this.Width, this.Height);
            Mouse.Capture(sender as UIElement);
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing)
            {
                var currentPoint = e.GetPosition(this);
                var deltaX = currentPoint.X - _resizeStartPoint.X;
                var deltaY = currentPoint.Y - _resizeStartPoint.Y;

                var newWidth = Math.Max(400, _resizeStartSize.Width + deltaX);
                var newHeight = Math.Max(400, _resizeStartSize.Height + deltaY);

                this.Width = newWidth;
                this.Height = newHeight;
            }
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isResizing = false;
            Mouse.Capture(null);
            SaveSettings();
        }

        private void EventItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is CalendarEventItem eventItem)
            {
                ShowEventDialog(eventItem);
            }
        }

        private void EventItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 우클릭 시 컨텍스트 메뉴가 자동으로 표시됨
        }

        private void EditEvent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && 
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                element.DataContext is CalendarEventItem eventItem)
            {
                ShowEventDialog(eventItem);
            }
        }

        private async void DeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && 
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement element &&
                element.DataContext is CalendarEventItem eventItem)
            {
                await DeleteEvent(eventItem);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 일정 추가/수정/삭제 기능

        private void ShowEventDialog(CalendarEventItem existingEvent = null)
        {
            try
            {
                var selectedCalendars = AvailableCalendars.Where(c => c.IsSelected).ToList();
                if (!selectedCalendars.Any())
                {
                    MessageBox.Show("선택된 캘린더가 없습니다. 설정에서 캘린더를 선택해주세요.", "알림", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new EventDialog(selectedCalendars, SelectedDate, existingEvent);
                dialog.Owner = this;
                
                if (dialog.ShowDialog() == true)
                {
                    if (dialog.IsDeleteRequested && existingEvent != null)
                    {
                        // 삭제 요청 (EventDialog에서 이미 확인받음)
                        _ = DeleteEvent(existingEvent, skipConfirmation: true);
                    }
                    else if (existingEvent == null)
                    {
                        // 새 일정 추가
                        _ = CreateEvent(dialog.EventData);
                    }
                    else
                    {
                        // 기존 일정 수정
                        _ = UpdateEvent(dialog.EventData, existingEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"일정 편집 오류: {ex.Message}";
                MessageBox.Show($"일정 편집 중 오류가 발생했습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CreateEvent(EventDialogData eventData)
        {
            if (_service == null) return;

            try
            {
                StatusMessage = "일정 추가 중...";

                var newEvent = new Event
                {
                    Summary = eventData.Title,
                    Description = eventData.Description,
                    Location = eventData.Location,
                    Start = new EventDateTime
                    {
                        DateTime = eventData.IsAllDay ? null : eventData.StartTime,
                        Date = eventData.IsAllDay ? eventData.StartTime.ToString("yyyy-MM-dd") : null,
                        TimeZone = eventData.IsAllDay ? null : GetGoogleTimeZone()
                    },
                    End = new EventDateTime
                    {
                        DateTime = eventData.IsAllDay ? null : eventData.EndTime,
                        Date = eventData.IsAllDay ? eventData.EndTime.ToString("yyyy-MM-dd") : null,
                        TimeZone = eventData.IsAllDay ? null : GetGoogleTimeZone()
                    }
                };

                var request = _service.Events.Insert(newEvent, eventData.CalendarId);
                await request.ExecuteAsync();

                StatusMessage = "일정이 추가되었습니다.";
                await LoadMonthEvents();
            }
            catch (Exception ex)
            {
                StatusMessage = $"일정 추가 실패: {ex.Message}";
                MessageBox.Show($"일정 추가 중 오류가 발생했습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateEvent(EventDialogData eventData, CalendarEventItem originalEvent)
        {
            if (_service == null || string.IsNullOrEmpty(originalEvent.Id)) return;

            try
            {
                StatusMessage = "일정 수정 중...";

                var eventRequest = _service.Events.Get(originalEvent.CalendarId, originalEvent.Id);
                var eventToUpdate = await eventRequest.ExecuteAsync();

                eventToUpdate.Summary = eventData.Title;
                eventToUpdate.Description = eventData.Description;
                eventToUpdate.Location = eventData.Location;
                eventToUpdate.Start = new EventDateTime
                {
                    DateTime = eventData.IsAllDay ? null : eventData.StartTime,
                    Date = eventData.IsAllDay ? eventData.StartTime.ToString("yyyy-MM-dd") : null,
                    TimeZone = eventData.IsAllDay ? null : GetGoogleTimeZone()
                };
                eventToUpdate.End = new EventDateTime
                {
                    DateTime = eventData.IsAllDay ? null : eventData.EndTime,
                    Date = eventData.IsAllDay ? eventData.EndTime.ToString("yyyy-MM-dd") : null,
                    TimeZone = eventData.IsAllDay ? null : GetGoogleTimeZone()
                };

                var updateRequest = _service.Events.Update(eventToUpdate, originalEvent.CalendarId, originalEvent.Id);
                await updateRequest.ExecuteAsync();

                StatusMessage = "일정이 수정되었습니다.";
                await LoadMonthEvents();
            }
            catch (Exception ex)
            {
                StatusMessage = $"일정 수정 실패: {ex.Message}";
                MessageBox.Show($"일정 수정 중 오류가 발생했습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteEvent(CalendarEventItem eventToDelete, bool skipConfirmation = false)
        {
            if (_service == null || eventToDelete == null || string.IsNullOrEmpty(eventToDelete.Id)) return;

            try
            {
                bool shouldDelete = skipConfirmation;
                
                if (!skipConfirmation)
                {
                    var result = MessageBox.Show($"'{eventToDelete.Title}' 일정을 삭제하시겠습니까?", 
                        "일정 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    shouldDelete = result == MessageBoxResult.Yes;
                }

                if (shouldDelete)
                {
                    StatusMessage = "일정 삭제 중...";

                    var deleteRequest = _service.Events.Delete(eventToDelete.CalendarId, eventToDelete.Id);
                    await deleteRequest.ExecuteAsync();

                    StatusMessage = "일정이 삭제되었습니다.";
                    await LoadMonthEvents();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"일정 삭제 실패: {ex.Message}";
                MessageBox.Show($"일정 삭제 중 오류가 발생했습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetGoogleTimeZone()
        {
            try
            {
                var localTimeZone = TimeZoneInfo.Local;
                
                // 한국 표준시 처리
                if (localTimeZone.Id.Contains("Korea") || localTimeZone.StandardName.Contains("Korea"))
                {
                    return "Asia/Seoul";
                }
                
                // 일본 표준시 처리
                if (localTimeZone.Id.Contains("Tokyo") || localTimeZone.StandardName.Contains("Tokyo"))
                {
                    return "Asia/Tokyo";
                }
                
                // 중국 표준시 처리
                if (localTimeZone.Id.Contains("China") || localTimeZone.StandardName.Contains("China"))
                {
                    return "Asia/Shanghai";
                }
                
                // UTC 오프셋으로 타임존 결정
                var offset = localTimeZone.GetUtcOffset(DateTime.Now);
                return offset.TotalHours switch
                {
                    9 => "Asia/Seoul",     // UTC+9 (한국, 일본)
                    8 => "Asia/Shanghai",  // UTC+8 (중국)
                    -5 => "America/New_York", // UTC-5 (미국 동부)
                    -8 => "America/Los_Angeles", // UTC-8 (미국 서부)
                    0 => "UTC",           // UTC
                    1 => "Europe/London", // UTC+1 (영국)
                    _ => "Asia/Seoul"     // 기본값으로 한국 시간
                };
            }
            catch
            {
                // 오류 발생 시 기본값으로 한국 시간 반환
                return "Asia/Seoul";
            }
        }

        #endregion
    }

    // 이벤트 대화상자 데이터 클래스
    public class EventDialogData
    {
        public string EventId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsAllDay { get; set; }
        public string CalendarId { get; set; }
    }

    // 데이터 모델
    public class CalendarDayItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _hasEvents;
        private int _eventCount;
        private List<string> _eventColors;

        public DateTime Date { get; set; }
        public int Day { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsToday { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public bool HasEvents
        {
            get => _hasEvents;
            set { _hasEvents = value; OnPropertyChanged(); }
        }

        public int EventCount
        {
            get => _eventCount;
            set { _eventCount = value; OnPropertyChanged(); }
        }

        public List<string> EventColors
        {
            get => _eventColors;
            set { _eventColors = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CalendarEventItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsAllDay { get; set; }
        public string ColorId { get; set; }
        public string CalendarName { get; set; }
        public string CalendarId { get; set; }

        public string TimeDisplay
        {
            get
            {
                if (IsAllDay)
                    return "종일";

                return $"{StartTime:HH:mm} - {EndTime:HH:mm}";
            }
        }

        public string DurationDisplay
        {
            get
            {
                if (IsAllDay)
                    return "종일 일정";

                var duration = EndTime - StartTime;
                if (duration.TotalMinutes < 60)
                    return $"{duration.TotalMinutes:0}분";
                else if (duration.TotalHours < 24)
                    return $"{duration.TotalHours:0.#}시간";
                else
                    return $"{duration.Days}일";
            }
        }
    }

    public class CalendarListItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string BackgroundColor { get; set; }
        public string ForegroundColor { get; set; }
        public bool IsPrimary { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class UserSettings
    {
        public double Opacity { get; set; } = 0.95;
        public bool IsPinned { get; set; } = false;
        public bool IsCompactMode { get; set; } = false;
        public Point? WindowPosition { get; set; }
        public Size? WindowSize { get; set; }
        public List<string> SelectedCalendarIds { get; set; } = new List<string>();
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
    }
}