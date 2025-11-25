using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common;
using Ryujinx.Ava.UI.SetupWizard;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ryujinx.Ava.UI.Helpers
{
    public class NotificationHelper
    {
        public static NotificationHelper Shared { get; set; }

        private const int MaxNotifications = 4;
        private const int NotificationDelayInMs = 5000;

        private readonly WindowNotificationManager _notificationManager;

        private readonly BlockingCollection<Notification> _notifications = new();

        public NotificationHelper(Window host,
            NotificationPosition visiblePosition = NotificationPosition.BottomRight,
            int maxItems = MaxNotifications)
        {
            _notificationManager = new WindowNotificationManager(host)
            {
                Position = visiblePosition,
                MaxItems = maxItems,
                Margin = new Thickness(0, 0, 15, 40)
            };

            Lazy<AsyncWorkQueue<Notification>> maybeAsyncWorkQueue = new(
                () => new AsyncWorkQueue<Notification>(notification =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _notificationManager.Show(notification);
                        });
                    },
                    "UI.NotificationThread",
                    _notifications),
                LazyThreadSafetyMode.ExecutionAndPublication);

            _notificationManager.TemplateApplied += (sender, args) =>
            {
                // NOTE: Force creation of the AsyncWorkQueue.
                _ = maybeAsyncWorkQueue.Value;
            };

            host.Closing += (sender, args) =>
            {
                if (maybeAsyncWorkQueue.IsValueCreated)
                {
                    maybeAsyncWorkQueue.Value.Dispose();
                }
            };
        }

        public static void Show(string title, string text, NotificationType type, bool waitingExit = false,
            Action onClick = null, Action onClose = null)
            => Shared?.Notify(title, text, type, waitingExit, onClick, onClose);

        public void Notify(string title, string text, NotificationType type, bool waitingExit = false,
            Action onClick = null, Action onClose = null)
        {
            TimeSpan delay = waitingExit
                ? TimeSpan.FromMilliseconds(0)
                : TimeSpan.FromMilliseconds(NotificationDelayInMs);

            _notifications.Add(new Notification(title, text, type, delay, onClick, onClose));
        }

        #region Instance notification senders

        public void NotifyInformation(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Notify(
                title,
                text,
                NotificationType.Information,
                waitingExit,
                onClick,
                onClose);

        public void NotifySuccess(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Notify(
                title,
                text,
                NotificationType.Success,
                waitingExit,
                onClick,
                onClose);

        public void NotifyWarning(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Notify(
                title,
                text,
                NotificationType.Warning,
                waitingExit,
                onClick,
                onClose);

        public void NotifyError(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Notify(
                title,
                text,
                NotificationType.Error,
                waitingExit,
                onClick,
                onClose);

        public void NotifyError(string message, bool waitingExit = false) =>
            NotifyError(
                LocaleManager.Instance[LocaleKeys.DialogErrorTitle],
                $"{LocaleManager.Instance[LocaleKeys.DialogErrorMessage]}\n\n{message}",
                waitingExit: waitingExit
            );

        #endregion

        #region Static notification senders

        public static void ShowInformation(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Show(
                title,
                text,
                NotificationType.Information,
                waitingExit,
                onClick,
                onClose);

        public static void ShowSuccess(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Show(
                title,
                text,
                NotificationType.Success,
                waitingExit,
                onClick,
                onClose);

        public static void ShowWarning(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Show(
                title,
                text,
                NotificationType.Warning,
                waitingExit,
                onClick,
                onClose);

        public static void ShowError(string title, string text, bool waitingExit = false, Action onClick = null,
            Action onClose = null) =>
            Show(
                title,
                text,
                NotificationType.Error,
                waitingExit,
                onClick,
                onClose);

        public static void ShowError(string message, bool waitingExit = false) =>
            ShowError(
                LocaleManager.Instance[LocaleKeys.DialogErrorTitle],
                $"{LocaleManager.Instance[LocaleKeys.DialogErrorMessage]}\n\n{message}",
                waitingExit: waitingExit
            );

        #endregion
    }
}
