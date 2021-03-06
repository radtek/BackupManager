﻿using BackupManager.Domain.Interfaces;
using BackupManager.Domain.Services;
using Quartz;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace BackupManager.WindowsService
{
    public enum ServiceState
    {
        SERVICE_STOPPED = 0x00000001,
        SERVICE_START_PENDING = 0x00000002,
        SERVICE_STOP_PENDING = 0x00000003,
        SERVICE_RUNNING = 0x00000004,
        SERVICE_CONTINUE_PENDING = 0x00000005,
        SERVICE_PAUSE_PENDING = 0x00000006,
        SERVICE_PAUSED = 0x00000007,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatus
    {
        public int dwServiceType;
        public ServiceState dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    };

    public partial class BackupService : ServiceBase
    {
        private const string BACKUP_SERVICE_NAME = "Inovatech Backup Service";
        private const string EVENT_LOG_NAME = "Backup Service Log";
        private IBackupService _backupService;
        private ISettings _settings;

        public BackupService()
        {
            InitializeComponent();

            eventLog1 = new System.Diagnostics.EventLog();

            if (!System.Diagnostics.EventLog.SourceExists(BACKUP_SERVICE_NAME))
            {
                System.Diagnostics.EventLog.CreateEventSource(BACKUP_SERVICE_NAME, EVENT_LOG_NAME);
            }
            eventLog1.Source = BACKUP_SERVICE_NAME;
            eventLog1.Log = EVENT_LOG_NAME;

            var codeBase = Assembly.GetExecutingAssembly().CodeBase;
            var uri = new UriBuilder(codeBase);
            var path = Uri.UnescapeDataString(uri.Path);
            var directory = Path.GetDirectoryName(path);
            var settingsService = new SettingsService(directory, "settings.json", false);
            _settings = settingsService.Load();
            _backupService = new BackupManager.Domain.Services.BackupService(_settings);
        }

        protected override void OnStart(string[] args)
        {
            Thread.Sleep(10000);

            // Update the service state to Start Pending.
            var serviceStatus = new ServiceStatus
            {
                dwCurrentState = ServiceState.SERVICE_START_PENDING,
                dwWaitHint = 100000
            };

            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            // Log startup process
            eventLog1.WriteEntry("In OnStart");
            CreateRoutine();

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        protected override void OnStop()
        {
            // Update the service state to Start Pending.
            var serviceStatus = new ServiceStatus
            {
                dwCurrentState = ServiceState.SERVICE_STOP_PENDING,
                dwWaitHint = 100000
            };

            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            // Log startup process
            eventLog1.WriteEntry("In OnStop");

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        private static IEnumerable<DayOfWeek> GetSystemDaysOfWeek(BackupManager.Domain.Enumerations.DayOfWeek flaDaysOfWeek)
        {
            foreach (var day in flaDaysOfWeek.ToString().Split(','))
            {
                if (Enum.TryParse<DayOfWeek>(day, out var dayOfWeek))
                    yield return dayOfWeek;
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(System.IntPtr handle, ref ServiceStatus serviceStatus);

        private void CreateRoutine()
        {
            var time = _settings.Schedule.Time;
            var days = GetSystemDaysOfWeek(_settings.Schedule.DaysOfWeek).ToArray();
            var scheduleBuilder = CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(time.Hour, time.Minute, days);
            var trigger = TriggerBuilder.Create().StartNow().WithSchedule(scheduleBuilder).Build();
        }
    }
}