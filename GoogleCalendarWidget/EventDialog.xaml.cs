using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GoogleCalendarWidget
{
    public partial class EventDialog : Window
    {
        public EventDialogData EventData { get; private set; }
        public bool IsDeleteRequested { get; private set; }

        private List<CalendarListItem> _availableCalendars;
        private CalendarEventItem _existingEvent;

        public EventDialog(List<CalendarListItem> availableCalendars, DateTime selectedDate, CalendarEventItem existingEvent = null)
        {
            InitializeComponent();
            
            _availableCalendars = availableCalendars;
            _existingEvent = existingEvent;

            InitializeComboBoxes();
            InitializeCalendarComboBox();
            
            if (existingEvent != null)
            {
                // 수정 모드
                this.Title = "일정 수정";
                LoadExistingEvent(existingEvent);
                DeleteButton.Visibility = Visibility.Visible;
            }
            else
            {
                // 추가 모드
                this.Title = "일정 추가";
                SetDefaultValues(selectedDate);
                DeleteButton.Visibility = Visibility.Collapsed;
            }
        }

        private void InitializeComboBoxes()
        {
            // 시간 콤보박스 초기화
            for (int i = 0; i < 24; i++)
            {
                StartHourComboBox.Items.Add(i.ToString("00"));
                EndHourComboBox.Items.Add(i.ToString("00"));
            }

            for (int i = 0; i < 60; i += 15)
            {
                StartMinuteComboBox.Items.Add(i.ToString("00"));
                EndMinuteComboBox.Items.Add(i.ToString("00"));
            }
        }

        private void InitializeCalendarComboBox()
        {
            CalendarComboBox.ItemsSource = _availableCalendars;
            
            if (_availableCalendars.Any())
            {
                CalendarComboBox.SelectedIndex = 0;
            }
        }

        private void SetDefaultValues(DateTime selectedDate)
        {
            TitleTextBox.Text = "";
            DescriptionTextBox.Text = "";
            LocationTextBox.Text = "";
            
            StartDatePicker.SelectedDate = selectedDate;
            EndDatePicker.SelectedDate = selectedDate;
            
            StartHourComboBox.SelectedIndex = 9; // 오전 9시
            StartMinuteComboBox.SelectedIndex = 0; // 0분
            EndHourComboBox.SelectedIndex = 10; // 오전 10시
            EndMinuteComboBox.SelectedIndex = 0; // 0분
            
            AllDayCheckBox.IsChecked = false;
        }

        private void LoadExistingEvent(CalendarEventItem eventItem)
        {
            TitleTextBox.Text = eventItem.Title;
            DescriptionTextBox.Text = eventItem.Description ?? "";
            LocationTextBox.Text = eventItem.Location ?? "";
            
            StartDatePicker.SelectedDate = eventItem.StartTime.Date;
            EndDatePicker.SelectedDate = eventItem.EndTime.Date;
            
            AllDayCheckBox.IsChecked = eventItem.IsAllDay;
            
            if (!eventItem.IsAllDay)
            {
                StartHourComboBox.SelectedIndex = eventItem.StartTime.Hour;
                StartMinuteComboBox.SelectedIndex = eventItem.StartTime.Minute / 15;
                EndHourComboBox.SelectedIndex = eventItem.EndTime.Hour;
                EndMinuteComboBox.SelectedIndex = eventItem.EndTime.Minute / 15;
            }

            // 캘린더 선택
            var calendar = _availableCalendars.FirstOrDefault(c => c.Id == eventItem.CalendarId);
            if (calendar != null)
            {
                CalendarComboBox.SelectedItem = calendar;
            }
        }

        private void AllDayCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            StartTimePanel.Visibility = Visibility.Collapsed;
            EndTimePanel.Visibility = Visibility.Collapsed;
        }

        private void AllDayCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            StartTimePanel.Visibility = Visibility.Visible;
            EndTimePanel.Visibility = Visibility.Visible;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                EventData = CreateEventData();
                this.DialogResult = true;
                this.Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show($"'{TitleTextBox.Text}' 일정을 삭제하시겠습니까?", 
                "일정 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                IsDeleteRequested = true;
                this.DialogResult = true;
                this.Close();
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                MessageBox.Show("제목을 입력해주세요.", "입력 확인", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TitleTextBox.Focus();
                return false;
            }

            if (StartDatePicker.SelectedDate == null)
            {
                MessageBox.Show("시작 날짜를 선택해주세요.", "입력 확인", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (EndDatePicker.SelectedDate == null)
            {
                MessageBox.Show("종료 날짜를 선택해주세요.", "입력 확인", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (CalendarComboBox.SelectedItem == null)
            {
                MessageBox.Show("캘린더를 선택해주세요.", "입력 확인", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private EventDialogData CreateEventData()
        {
            var selectedCalendar = (CalendarListItem)CalendarComboBox.SelectedItem;
            var isAllDay = AllDayCheckBox.IsChecked == true;
            
            var startDate = StartDatePicker.SelectedDate.Value;
            var endDate = EndDatePicker.SelectedDate.Value;
            
            DateTime startTime, endTime;
            
            if (isAllDay)
            {
                startTime = startDate;
                endTime = endDate.AddDays(1); // 종일 일정은 다음날 자정까지
            }
            else
            {
                var startHour = StartHourComboBox.SelectedIndex;
                var startMinute = StartMinuteComboBox.SelectedIndex * 15;
                var endHour = EndHourComboBox.SelectedIndex;
                var endMinute = EndMinuteComboBox.SelectedIndex * 15;
                
                startTime = startDate.AddHours(startHour).AddMinutes(startMinute);
                endTime = endDate.AddHours(endHour).AddMinutes(endMinute);
            }

            return new EventDialogData
            {
                EventId = _existingEvent?.Id,
                Title = TitleTextBox.Text.Trim(),
                Description = DescriptionTextBox.Text.Trim(),
                Location = LocationTextBox.Text.Trim(),
                StartTime = startTime,
                EndTime = endTime,
                IsAllDay = isAllDay,
                CalendarId = selectedCalendar.Id
            };
        }
    }
}
