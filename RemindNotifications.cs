using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NotificationApp
{
    public class NotificationForm : Form
    {
        private NotifyIcon _notifyIcon;
        private Form notificationForm;

        private readonly string _apiUrl = ConfigurationManager.AppSettings["ApiUrl"]; // URL API или метод получения уведомления

        private Timer _timer;
        private bool _canDisplayNotification = true; // Флаг, разрешающий показ уведомления
        private DateTime _notificationDelayTime; // Время отложенного показа уведомления
        private bool isEventExecuted = false;

        private int rightClickCounter = 0;
        private DateTime lastRightClickTime;

        // Константы для работы с окнами
        private const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        public NotificationForm()
        {
            // Создание и настройка иконки в системном трее
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Information;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += NotifyIconMouseClick;

            // Создание контекстного меню
            ContextMenu contextMenu = new ContextMenu();
            string intervalDelayAllNotifications = GetIntervalDelayAllNotificationsFromSettings().ToString();
            contextMenu.MenuItems.Add("Отложить уведомления на " + intervalDelayAllNotifications + " минут", DelayAllNotifications);
            //contextMenu.MenuItems.Add("Показать уведомление", ShowNotification);
            //contextMenu.MenuItems.Add("Выход", ExitApplication);

            _notifyIcon.ContextMenu = contextMenu;

            // Создание и настройка таймера
            _timer = new Timer();
            _timer.Interval = 60000;
            _timer.Tick += TimerTick;
            _timer.Start();
        }

        private void ShowNotification(object sender, EventArgs e)
        {
            string notification = GetNotificationFromWebsite();
            DisplayNotification(notification);
        }

        private int GetIntervalDelayAllNotificationsFromSettings()
        {
            // Получение интервала из файла настроек
            try
            {
                return Convert.ToInt32(ConfigurationManager.AppSettings["GetIntervalDelayAllNotificationsInMinutes"]);
            }
            catch (Exception)
            {
                // Обработка ошибки, если интервал не может быть прочитан или конвертирован
                // устанавливаем интервала по умолчанию (60 секунд)
                return 60;
            }
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (IsUserActive())
            {
                if (_canDisplayNotification)
                {
                    string notification = GetNotificationFromWebsite();
                    DisplayNotification(notification);
                }
                else if (_notificationDelayTime <= DateTime.Now)
                {
                    _canDisplayNotification = true;
                    _notificationDelayTime = DateTime.MaxValue;
                    string notification = GetNotificationFromWebsite();
                    DisplayNotification(notification);
                }
            }
        }

        private void DisplayNotification(string notification)
        {
            if (!string.IsNullOrEmpty(notification))
            {
                try
                {
                    // Извлечение данных заголовка и текста уведомления
                    JObject notificationObject = JObject.Parse(notification);
                    string title = notificationObject["title"].ToString();
                    string message = notificationObject["message"].ToString();
                    int timeout = int.Parse(notificationObject["timeout"]?.ToString());
                    bool showForm = notificationObject["showForm"]?.ToObject<bool>() ?? false;
                    string websiteUrl = notificationObject["websiteUrl"]?.ToString();
                    int diffDays = int.Parse(notificationObject["diffDays"]?.ToString());

                    // Отображение уведомления
                    DateTime now = DateTime.Now;
                    if (diffDays == 1 && (now.Hour == 11 || now.Hour == 13))
                    {
                        if (showForm)
                        {
                            ShowNotificationForm(notification);
                        }

                        _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
                    }
                    else if (diffDays == 2 && now.Hour >= 8 && now.Hour <= 16 && now.Hour % 2 == 0 && now.Minute == 0)
                    {
                        if (showForm)
                        {
                            ShowNotificationForm(notification);
                        }

                        _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
                    }
                    else if (diffDays == 3 && now.Minute == 0)
                    {
                        if (showForm)
                        {
                            ShowNotificationForm(notification);
                        }

                        _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
                    }
                    else if (diffDays >= 4 && now.Minute % 5 == 0)
                    {
                        if (showForm)
                        {
                            ShowNotificationForm(notification);
                        }

                        _notifyIcon.ShowBalloonTip(timeout, title, message, ToolTipIcon.Info);
                    }

                    if (websiteUrl != "")
                    {
                        // Обработчик события нажатия на BalloonTip уведомление
                        isEventExecuted = false;
                        _notifyIcon.BalloonTipClicked += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(websiteUrl) && !isEventExecuted)
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = websiteUrl,
                                    UseShellExecute = true
                                });

                                isEventExecuted = true;
                            }
                        };
                    }
                }
                catch (Exception)
                {
                    
                }
            }
        }

        private void DelayAllNotifications(object sender, EventArgs e)
        {
            _canDisplayNotification = false;
            _notificationDelayTime = DateTime.Now.AddMinutes(GetIntervalDelayAllNotificationsFromSettings());
        }

        public static bool IsUserActive()
        {
            LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

            if (GetLastInputInfo(ref lastInputInfo))
            {
                uint idleTime = (uint)Environment.TickCount - lastInputInfo.dwTime;
                TimeSpan idleDuration = TimeSpan.FromMilliseconds(idleTime);

                // Пользователь считается активным, если время простоя составляет менее n-секунд указанных в конфигурации
                return idleDuration.TotalSeconds < GetIntervalUserActiveFromSettings();
            }

            // В случае ошибки считаем пользователя неактивным
            return false;
        }

        private static int GetIntervalUserActiveFromSettings()
        {
            // Получение интервала из файла настроек
            try
            {
                return Convert.ToInt32(ConfigurationManager.AppSettings["IsUserActiveIntervalInSeconds"]);
            }
            catch (Exception)
            {
                // Обработка ошибки, если интервал не может быть прочитан или сконвертирован
                // Устанавливаем интервал по умолчанию (60 секунд)
                return 60;
            }
        }

        private string GetNotificationFromWebsite()
        {
            try
            {
                // Создание запроса к api
                WebClient client = new WebClient();
                client.Encoding = Encoding.UTF8;
                var userName = Environment.UserName;
                string preparedUrl = _apiUrl.Replace("{USERNAME}", userName);
                string response = client.DownloadString(preparedUrl);

                return response;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private void NotifyIconMouseClick(object sender, MouseEventArgs e)
        {
            // Отображение окна при нажатии на иконку в системном трее
            if (e.Button == MouseButtons.Left)
            {
                string notification = GetNotificationFromWebsite();
                ShowNotificationForm(notification);
            }

            if (e.Button == MouseButtons.Right)
            {
                // Проверяем, прошло ли указанное время с момента последнего нажатия
                if ((DateTime.Now - lastRightClickTime).TotalSeconds >= 20)
                {
                    rightClickCounter = 0;
                }

                rightClickCounter++;
                lastRightClickTime = DateTime.Now;

                if (rightClickCounter >= 5)
                {
                    Application.Exit();
                }
            }
        }

        private void ShowNotificationForm(string notification)
        {
            // Закрытие предыдущего окна уведомления, если оно уже открыто
            if (notificationForm != null && !notificationForm.IsDisposed && notificationForm.Visible)
            {
                return;
            }
            else if (notificationForm != null && !notificationForm.IsDisposed)
            {
                notificationForm.Close();
            }

            if (!string.IsNullOrEmpty(notification))
            {
                try
                {
                    int fontSize = Convert.ToInt32(ConfigurationManager.AppSettings["FontSizeForm"]);

                    // Извлечение данных заголовка и текста уведомления
                    JObject notificationObject = JObject.Parse(notification);
                    string title = notificationObject["title"].ToString();
                    string message = notificationObject["message"].ToString();

                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
                    {
                        notificationForm = new Form();
                        notificationForm.Width = 600;
                        notificationForm.Text = title;
                        notificationForm.TopMost = true; // Устанавливает форму поверх всех остальных окон
                        notificationForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                        notificationForm.MinimizeBox = false;
                        notificationForm.MaximizeBox = false;
                        notificationForm.StartPosition = FormStartPosition.CenterScreen;

                        Label notificationLabel = new Label();
                        notificationLabel.Text = message;
                        notificationLabel.Dock = DockStyle.Fill;
                        notificationLabel.TextAlign = ContentAlignment.MiddleCenter;
                        notificationLabel.Font = new Font(notificationLabel.Font.FontFamily, fontSize);

                        notificationForm.Controls.Add(notificationLabel);
                        notificationForm.ShowDialog();
                    }
                }
                catch (Exception)
                {
                   
                }
            }
        }

        private void NotifyIconBalloonTipClosed(object sender, EventArgs e)
        {
            // Очистка предыдущего уведомления после закрытия всплывающей подсказки
            _notifyIcon.Dispose();
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += NotifyIconMouseClick;
            _notifyIcon.BalloonTipClosed += NotifyIconBalloonTipClosed;
        }

        private void ExitApplication(object sender, EventArgs e)
        {
            Application.Exit();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated)
            {
                CreateHandle();
                value = false;  // Скрываем окно перед его созданием
            }
            base.SetVisibleCore(value);
        }

        protected override void OnLoad(EventArgs e)
        {
            // Скрываем окно приложения из панели задач
            base.OnLoad(e);
            ShowWindow(Handle, SW_HIDE);
        }

        [STAThread]
        public static void Main()
        {
            Application.Run(new NotificationForm());
        }
    }
}