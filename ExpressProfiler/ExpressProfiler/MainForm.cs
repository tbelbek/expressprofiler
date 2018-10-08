﻿//sample application for demonstrating Sql Server Profiling
//writen by Locky, 2009.

#region usings

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using ExpressProfiler.EventComparers;
using ExpressProfiler.Properties;

#endregion

namespace ExpressProfiler
{
    public partial class MainForm : Form
    {
        internal const string versionString = "Express Profiler v2.2";
        internal readonly List<ListViewItem> m_Cached = new List<ListViewItem>(1024);
        internal readonly List<ListViewItem> m_CachedUnFiltered = new List<ListViewItem>(1024);
        private readonly SqlCommand m_Cmd = new SqlCommand();
        private readonly List<PerfColumn> m_columns = new List<PerfColumn>();
        private readonly ProfilerEvent m_EventPaused = new ProfilerEvent();
        private readonly ProfilerEvent m_EventStarted = new ProfilerEvent();
        private readonly ProfilerEvent m_EventStopped = new ProfilerEvent();
        private readonly Dictionary<string, ListViewItem> m_itembysql = new Dictionary<string, ListViewItem>();

        private readonly YukonLexer m_Lex = new YukonLexer();
        private readonly Queue<PerfInfo> m_perf = new Queue<PerfInfo>();
        private bool dontUpdateSource;
        internal string lastpattern = "";
        internal int lastpos = -1;
        private ListViewNF lvEvents;
        private bool m_autostart;
        private SqlConnection m_Conn;
        internal TraceProperties.TraceSettings m_currentsettings;
        private int m_EventCount;
        private Queue<ProfilerEvent> m_events = new Queue<ProfilerEvent>(10);
        private PerfInfo m_first, m_prev;
        private bool m_NeedStop = true;
        private Exception m_profilerexception;
        private ProfilingStateEnum m_ProfilingState;
        private RawTraceReader m_Rdr;
        private string m_servername = "";
        private Thread m_Thr;
        private string m_username = "";
        private string m_userpassword = "";
        internal bool matchCase = false;
        internal bool wholeWord = false;

        public MainForm()
        {
            InitializeComponent();
            tbStart.DefaultItem = tbRun;
            Text = versionString;
            if (edPassword.TextBox != null) edPassword.TextBox.PasswordChar = '*';
            m_servername = Settings.Default.ServerName;
            m_username = Settings.Default.UserName;
            m_currentsettings = GetDefaultSettings();
            ParseCommandLine();
            InitLV();
            edServer.Text = m_servername;
            edUser.Text = m_username;
            edPassword.Text = m_userpassword;
            tbAuth.SelectedIndex = string.IsNullOrEmpty(m_username) ? 0 : 1;
            if (m_autostart) RunProfiling(false);
            UpdateButtons();
        }

        private TraceProperties.TraceSettings GetDefaultSettings()
        {
            try
            {
                var x = new XmlSerializer(typeof(TraceProperties.TraceSettings));
                using (var sr = new StringReader(Settings.Default.TraceSettings))
                {
                    return (TraceProperties.TraceSettings) x.Deserialize(sr);
                }
            }
            catch (Exception)
            {
            }

            return TraceProperties.TraceSettings.GetDefaultSettings();
        }


//DatabaseName = Filters.DatabaseName,
//LoginName = Filters.LoginName,
//HostName = Filters.HostName,
//TextData = Filters.TextData,
//ApplicationName = Filters.ApplicationName,


        private bool ParseFilterParam(string[] args, int idx)
        {
            var condition = idx + 1 < args.Length ? args[idx + 1] : "";
            var value = idx + 2 < args.Length ? args[idx + 2] : "";

            switch (args[idx].ToLower())
            {
                case "-cpu":
                    m_currentsettings.Filters.CPU = int.Parse(value);
                    m_currentsettings.Filters.CpuFilterCondition = TraceProperties.ParseIntCondition(condition);
                    break;
                case "-duration":
                    m_currentsettings.Filters.Duration = int.Parse(value);
                    m_currentsettings.Filters.DurationFilterCondition = TraceProperties.ParseIntCondition(condition);
                    break;
                case "-reads":
                    m_currentsettings.Filters.Reads = int.Parse(value);
                    m_currentsettings.Filters.ReadsFilterCondition = TraceProperties.ParseIntCondition(condition);
                    break;
                case "-writes":
                    m_currentsettings.Filters.Writes = int.Parse(value);
                    m_currentsettings.Filters.WritesFilterCondition = TraceProperties.ParseIntCondition(condition);
                    break;
                case "-spid":
                    m_currentsettings.Filters.SPID = int.Parse(value);
                    m_currentsettings.Filters.SPIDFilterCondition = TraceProperties.ParseIntCondition(condition);
                    break;

                case "-databasename":
                    m_currentsettings.Filters.DatabaseName = value;
                    m_currentsettings.Filters.DatabaseNameFilterCondition =
                        TraceProperties.ParseStringCondition(condition);
                    break;
                case "-loginname":
                    m_currentsettings.Filters.LoginName = value;
                    m_currentsettings.Filters.LoginNameFilterCondition =
                        TraceProperties.ParseStringCondition(condition);
                    break;
                case "-hostname":
                    m_currentsettings.Filters.HostName = value;
                    m_currentsettings.Filters.HostNameFilterCondition = TraceProperties.ParseStringCondition(condition);
                    break;
                case "-textdata":
                    m_currentsettings.Filters.TextData = value;
                    m_currentsettings.Filters.TextDataFilterCondition = TraceProperties.ParseStringCondition(condition);
                    break;
                case "-applicationname":
                    m_currentsettings.Filters.ApplicationName = value;
                    m_currentsettings.Filters.ApplicationNameFilterCondition =
                        TraceProperties.ParseStringCondition(condition);
                    break;
            }

            return false;
        }

        private void ParseCommandLine()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                var i = 1;
                while (i < args.Length)
                {
                    var ep = i + 1 < args.Length ? args[i + 1] : "";
                    switch (args[i].ToLower())
                    {
                        case "-s":
                        case "-server":
                            m_servername = ep;
                            i++;
                            break;
                        case "-u":
                        case "-user":
                            m_username = ep;
                            i++;
                            break;
                        case "-p":
                        case "-password":
                            m_userpassword = ep;
                            i++;
                            break;
                        case "-m":
                        case "-maxevents":
                            if (!int.TryParse(ep, out var m)) m = 1000;
                            m_currentsettings.Filters.MaximumEventCount = m;
                            break;
                        case "-d":
                        case "-duration":
                            if (int.TryParse(ep, out var d))
                            {
                                m_currentsettings.Filters.DurationFilterCondition =
                                    TraceProperties.IntFilterCondition.GreaterThan;
                                m_currentsettings.Filters.Duration = d;
                            }

                            break;
                        case "-start":
                            m_autostart = true;
                            break;
                        case "-batchcompleted":
                            m_currentsettings.EventsColumns.BatchCompleted = true;
                            break;
                        case "-batchstarting":
                            m_currentsettings.EventsColumns.BatchStarting = true;
                            break;
                        case "-existingconnection":
                            m_currentsettings.EventsColumns.ExistingConnection = true;
                            break;
                        case "-loginlogout":
                            m_currentsettings.EventsColumns.LoginLogout = true;
                            break;
                        case "-rpccompleted":
                            m_currentsettings.EventsColumns.RPCCompleted = true;
                            break;
                        case "-rpcstarting":
                            m_currentsettings.EventsColumns.RPCStarting = true;
                            break;
                        case "-spstmtcompleted":
                            m_currentsettings.EventsColumns.SPStmtCompleted = true;
                            break;
                        case "-spstmtstarting":
                            m_currentsettings.EventsColumns.SPStmtStarting = true;
                            break;
                        default:
                            if (ParseFilterParam(args, i)) i++;
                            break;
                    }

                    i++;
                }

                if (m_servername.Length == 0) m_servername = @".\sqlexpress";
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tbStart_Click(object sender, EventArgs e)
        {
            if (!TraceProperties.AtLeastOneEventSelected(m_currentsettings))
            {
                MessageBox.Show("You should select at least 1 event", "Starting trace", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RunProfiling(true);
            }

            {
                RunProfiling(false);
            }
        }

        private void UpdateButtons()
        {
            tbStart.Enabled = m_ProfilingState == ProfilingStateEnum.psStopped ||
                              m_ProfilingState == ProfilingStateEnum.psPaused;
            tbRun.Enabled = tbStart.Enabled;
            mnRun.Enabled = tbRun.Enabled;
            tbRunWithFilters.Enabled = ProfilingStateEnum.psStopped == m_ProfilingState;
            mnRunWithFilters.Enabled = tbRunWithFilters.Enabled;
            startTraceToolStripMenuItem.Enabled = tbStart.Enabled;
            tbStop.Enabled = m_ProfilingState == ProfilingStateEnum.psPaused ||
                             m_ProfilingState == ProfilingStateEnum.psProfiling;
            stopTraceToolStripMenuItem.Enabled = tbStop.Enabled;
            tbPause.Enabled = m_ProfilingState == ProfilingStateEnum.psProfiling;
            pauseTraceToolStripMenuItem.Enabled = tbPause.Enabled;
            timer1.Enabled = m_ProfilingState == ProfilingStateEnum.psProfiling;
            edServer.Enabled = m_ProfilingState == ProfilingStateEnum.psStopped;
            tbAuth.Enabled = m_ProfilingState == ProfilingStateEnum.psStopped;
            edUser.Enabled = edServer.Enabled && tbAuth.SelectedIndex == 1;
            edPassword.Enabled = edServer.Enabled && tbAuth.SelectedIndex == 1;
        }


        private void InitLV()
        {
            lvEvents = new ListViewNF
            {
                Dock = DockStyle.Fill,
                Location = new Point(0, 0),
                Name = "lvEvents",
                Size = new Size(979, 297),
                TabIndex = 0,
                VirtualMode = true,
                UseCompatibleStateImageBehavior = false,
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                View = View.Details,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                AllowColumnReorder = false
            };
            lvEvents.RetrieveVirtualItem += lvEvents_RetrieveVirtualItem;
            lvEvents.KeyDown += lvEvents_KeyDown;
            lvEvents.ItemSelectionChanged += listView1_ItemSelectionChanged_1;
            lvEvents.ColumnClick += lvEvents_ColumnClick;
            lvEvents.SelectedIndexChanged += lvEvents_SelectedIndexChanged;
            lvEvents.VirtualItemsSelectionRangeChanged += LvEventsOnVirtualItemsSelectionRangeChanged;
            lvEvents.ContextMenuStrip = contextMenuStrip1;
            splitContainer1.Panel1.Controls.Add(lvEvents);
            InitColumns();
            InitGridColumns();
        }

        private void InitColumns()
        {
            m_columns.Clear();
            m_columns.Add(new PerfColumn
                {Caption = "Event Class", Column = ProfilerEventColumns.EventClass, Width = 122});
            m_columns.Add(new PerfColumn {Caption = "Text Data", Column = ProfilerEventColumns.TextData, Width = 255});
            m_columns.Add(new PerfColumn {Caption = "Login Name", Column = ProfilerEventColumns.LoginName, Width = 79});
            m_columns.Add(new PerfColumn
            {
                Caption = "CPU", Column = ProfilerEventColumns.CPU, Width = 82, Alignment = HorizontalAlignment.Right,
                Format = "#,0"
            });
            m_columns.Add(new PerfColumn
            {
                Caption = "Reads", Column = ProfilerEventColumns.Reads, Width = 78,
                Alignment = HorizontalAlignment.Right, Format = "#,0"
            });
            m_columns.Add(new PerfColumn
            {
                Caption = "Writes", Column = ProfilerEventColumns.Writes, Width = 78,
                Alignment = HorizontalAlignment.Right, Format = "#,0"
            });
            m_columns.Add(new PerfColumn
            {
                Caption = "Duration, ms", Column = ProfilerEventColumns.Duration, Width = 82,
                Alignment = HorizontalAlignment.Right, Format = "#,0"
            });
            m_columns.Add(new PerfColumn
            {
                Caption = "SPID", Column = ProfilerEventColumns.SPID, Width = 50, Alignment = HorizontalAlignment.Right
            });

            if (m_currentsettings.EventsColumns.StartTime)
                m_columns.Add(new PerfColumn
                {
                    Caption = "Start time", Column = ProfilerEventColumns.StartTime, Width = 140,
                    Format = "yyyy-MM-dd hh:mm:ss.ffff"
                });
            if (m_currentsettings.EventsColumns.EndTime)
                m_columns.Add(new PerfColumn
                {
                    Caption = "End time", Column = ProfilerEventColumns.EndTime, Width = 140,
                    Format = "yyyy-MM-dd hh:mm:ss.ffff"
                });
            if (m_currentsettings.EventsColumns.DatabaseName)
                m_columns.Add(new PerfColumn
                    {Caption = "DatabaseName", Column = ProfilerEventColumns.DatabaseName, Width = 70});
            if (m_currentsettings.EventsColumns.ObjectName)
                m_columns.Add(new PerfColumn
                    {Caption = "Object name", Column = ProfilerEventColumns.ObjectName, Width = 70});
            if (m_currentsettings.EventsColumns.ApplicationName)
                m_columns.Add(new PerfColumn
                    {Caption = "Application name", Column = ProfilerEventColumns.ApplicationName, Width = 70});
            if (m_currentsettings.EventsColumns.HostName)
                m_columns.Add(
                    new PerfColumn {Caption = "Host name", Column = ProfilerEventColumns.HostName, Width = 70});

            m_columns.Add(
                new PerfColumn {Caption = "#", Column = -1, Width = 53, Alignment = HorizontalAlignment.Right});
        }

        private void InitGridColumns()
        {
            InitColumns();
            lvEvents.BeginUpdate();
            try
            {
                lvEvents.Columns.Clear();
                foreach (var pc in m_columns)
                {
                    var l = lvEvents.Columns.Add(pc.Caption, pc.Width);
                    l.TextAlign = pc.Alignment;
                }
            }
            finally
            {
                lvEvents.EndUpdate();
            }
        }

        private void LvEventsOnVirtualItemsSelectionRangeChanged(object sender,
            ListViewVirtualItemsSelectionRangeChangedEventArgs listViewVirtualItemsSelectionRangeChangedEventArgs)
        {
            UpdateSourceBox();
        }

        private void lvEvents_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSourceBox();
        }

        private void lvEvents_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            lvEvents.ToggleSortOrder();
            lvEvents.SetSortIcon(e.Column, lvEvents.SortOrder);
            var comparer = new TextDataComparer(e.Column, lvEvents.SortOrder);
            m_Cached.Sort(comparer);
            UpdateSourceBox();
            ShowSelectedEvent();
        }

        private string GetEventCaption(ProfilerEvent evt)
        {
            if (evt == m_EventStarted) return "Trace started";
            if (evt == m_EventPaused) return "Trace paused";
            return evt == m_EventStopped ? "Trace stopped" : ProfilerEvents.Names[evt.EventClass];
        }

        private string GetFormattedValue(ProfilerEvent evt, int column, string format)
        {
            return ProfilerEventColumns.Duration == column
                ? (evt.Duration / 1000).ToString(format)
                : evt.GetFormattedData(column, format);
        }

        private void NewEventArrived(ProfilerEvent evt, bool last)
        {
            {
                var current = lvEvents.SelectedIndices.Count > 0 ? m_Cached[lvEvents.SelectedIndices[0]] : null;
                m_EventCount++;
                var caption = GetEventCaption(evt);
                var lvi = new ListViewItem(caption);
                var items = new string[m_columns.Count];
                for (var i = 1; i < m_columns.Count; i++)
                {
                    var pc = m_columns[i];
                    items[i - 1] = pc.Column == -1
                        ? m_EventCount.ToString("#,0")
                        : GetFormattedValue(evt, pc.Column, pc.Format) ?? "";
                }

                lvi.SubItems.AddRange(items);
                lvi.Tag = evt;
                m_Cached.Add(lvi);
                if (!last) return;
                lvEvents.VirtualListSize = m_Cached.Count;
                lvEvents.SelectedIndices.Clear();
                FocusLVI(tbScroll.Checked ? lvEvents.Items[m_Cached.Count - 1] : current, tbScroll.Checked);
                lvEvents.Invalidate(lvi.Bounds);
            }
        }

        internal void FocusLVI(ListViewItem lvi, bool ensure)
        {
            if (null == lvi) return;
            lvi.Focused = true;
            lvi.Selected = true;
            listView1_ItemSelectionChanged_1(lvEvents, null);
            if (ensure) lvEvents.EnsureVisible(lvEvents.Items.IndexOf(lvi));
        }

        private void ProfilerThread(object state)
        {
            try
            {
                while (!m_NeedStop && m_Rdr.TraceIsActive)
                {
                    var evt = m_Rdr.Next();
                    if (evt == null) continue;
                    lock (this)
                    {
                        m_events.Enqueue(evt);
                    }
                }
            }
            catch (Exception e)
            {
                lock (this)
                {
                    if (!m_NeedStop && m_Rdr.TraceIsActive) m_profilerexception = e;
                }
            }
        }

        private SqlConnection GetConnection()
        {
            return new SqlConnection
            {
                ConnectionString =
                    tbAuth.SelectedIndex == 0
                        ? $@"Data Source = {edServer.Text}; Initial Catalog = master; Integrated Security=SSPI;Application Name=Express Profiler"
                        : $@"Data Source={edServer.Text};Initial Catalog=master;User Id={edUser.Text};Password='{edPassword.Text}';;Application Name=Express Profiler"
            };
        }

        private void StartProfiling()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                m_perf.Clear();
                m_first = null;
                m_prev = null;
                if (m_ProfilingState == ProfilingStateEnum.psPaused)
                {
                    ResumeProfiling();
                    return;
                }

                if (m_Conn != null && m_Conn.State == ConnectionState.Open) m_Conn.Close();
                InitGridColumns();
                m_EventCount = 0;
                m_Conn = GetConnection();
                m_Conn.Open();
                m_Rdr = new RawTraceReader(m_Conn);

                m_Rdr.CreateTrace();
                if (true)
                {
                    if (m_currentsettings.EventsColumns.LoginLogout)
                    {
                        m_Rdr.SetEvent(ProfilerEvents.SecurityAudit.AuditLogin,
                            ProfilerEventColumns.TextData,
                            ProfilerEventColumns.LoginName,
                            ProfilerEventColumns.SPID,
                            ProfilerEventColumns.StartTime,
                            ProfilerEventColumns.EndTime,
                            ProfilerEventColumns.HostName
                        );
                        m_Rdr.SetEvent(ProfilerEvents.SecurityAudit.AuditLogout,
                            ProfilerEventColumns.CPU,
                            ProfilerEventColumns.Reads,
                            ProfilerEventColumns.Writes,
                            ProfilerEventColumns.Duration,
                            ProfilerEventColumns.LoginName,
                            ProfilerEventColumns.SPID,
                            ProfilerEventColumns.StartTime,
                            ProfilerEventColumns.EndTime,
                            ProfilerEventColumns.ApplicationName,
                            ProfilerEventColumns.HostName
                        );
                    }

                    if (m_currentsettings.EventsColumns.ExistingConnection)
                        m_Rdr.SetEvent(ProfilerEvents.Sessions.ExistingConnection,
                            ProfilerEventColumns.TextData,
                            ProfilerEventColumns.SPID,
                            ProfilerEventColumns.StartTime,
                            ProfilerEventColumns.EndTime,
                            ProfilerEventColumns.ApplicationName,
                            ProfilerEventColumns.HostName
                        );
                    if (m_currentsettings.EventsColumns.BatchCompleted)
                        m_Rdr.SetEvent(ProfilerEvents.TSQL.SQLBatchCompleted,
                            ProfilerEventColumns.TextData,
                            ProfilerEventColumns.LoginName,
                            ProfilerEventColumns.CPU,
                            ProfilerEventColumns.Reads,
                            ProfilerEventColumns.Writes,
                            ProfilerEventColumns.Duration,
                            ProfilerEventColumns.SPID,
                            ProfilerEventColumns.StartTime,
                            ProfilerEventColumns.EndTime,
                            ProfilerEventColumns.DatabaseName,
                            ProfilerEventColumns.ApplicationName,
                            ProfilerEventColumns.HostName
                        );
                    if (m_currentsettings.EventsColumns.BatchStarting)
                        m_Rdr.SetEvent(ProfilerEvents.TSQL.SQLBatchStarting,
                            ProfilerEventColumns.TextData,
                            ProfilerEventColumns.LoginName,
                            ProfilerEventColumns.SPID,
                            ProfilerEventColumns.StartTime,
                            ProfilerEventColumns.EndTime,
                            ProfilerEventColumns.DatabaseName,
                            ProfilerEventColumns.ApplicationName,
                            ProfilerEventColumns.HostName
                        );
                    if (m_currentsettings.EventsColumns.RPCStarting)
                        m_Rdr.SetEvent(ProfilerEvents.StoredProcedures.RPCStarting,
                            ProfilerEventColumns.TextData,
                            ProfilerEventColumns.LoginName,
                            ProfilerEventColumns.SPID,
                            ProfilerEventColumns.StartTime,
                            ProfilerEventColumns.EndTime,
                            ProfilerEventColumns.DatabaseName,
                            ProfilerEventColumns.ObjectName,
                            ProfilerEventColumns.ApplicationName,
                            ProfilerEventColumns.HostName
                        );
                }

                if (m_currentsettings.EventsColumns.RPCCompleted)
                    m_Rdr.SetEvent(ProfilerEvents.StoredProcedures.RPCCompleted,
                        ProfilerEventColumns.TextData, ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU, ProfilerEventColumns.Reads,
                        ProfilerEventColumns.Writes, ProfilerEventColumns.Duration,
                        ProfilerEventColumns.SPID
                        , ProfilerEventColumns.StartTime, ProfilerEventColumns.EndTime
                        , ProfilerEventColumns.DatabaseName
                        , ProfilerEventColumns.ObjectName
                        , ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );
                if (m_currentsettings.EventsColumns.SPStmtCompleted)
                    m_Rdr.SetEvent(ProfilerEvents.StoredProcedures.SPStmtCompleted,
                        ProfilerEventColumns.TextData, ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU, ProfilerEventColumns.Reads,
                        ProfilerEventColumns.Writes, ProfilerEventColumns.Duration,
                        ProfilerEventColumns.SPID
                        , ProfilerEventColumns.StartTime, ProfilerEventColumns.EndTime
                        , ProfilerEventColumns.DatabaseName
                        , ProfilerEventColumns.ObjectName
                        , ProfilerEventColumns.ObjectID
                        , ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );
                if (m_currentsettings.EventsColumns.SPStmtStarting)
                    m_Rdr.SetEvent(ProfilerEvents.StoredProcedures.SPStmtStarting,
                        ProfilerEventColumns.TextData, ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU, ProfilerEventColumns.Reads,
                        ProfilerEventColumns.Writes, ProfilerEventColumns.Duration,
                        ProfilerEventColumns.SPID
                        , ProfilerEventColumns.StartTime, ProfilerEventColumns.EndTime
                        , ProfilerEventColumns.DatabaseName
                        , ProfilerEventColumns.ObjectName
                        , ProfilerEventColumns.ObjectID
                        , ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );
                if (m_currentsettings.EventsColumns.UserErrorMessage)
                    m_Rdr.SetEvent(ProfilerEvents.ErrorsAndWarnings.UserErrorMessage,
                        ProfilerEventColumns.TextData,
                        ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU,
                        ProfilerEventColumns.SPID,
                        ProfilerEventColumns.StartTime,
                        ProfilerEventColumns.DatabaseName,
                        ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );
                if (m_currentsettings.EventsColumns.BlockedProcessPeport)
                    m_Rdr.SetEvent(ProfilerEvents.ErrorsAndWarnings.Blockedprocessreport,
                        ProfilerEventColumns.TextData,
                        ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU,
                        ProfilerEventColumns.SPID,
                        ProfilerEventColumns.StartTime,
                        ProfilerEventColumns.DatabaseName,
                        ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );

                if (m_currentsettings.EventsColumns.SQLStmtStarting)
                    m_Rdr.SetEvent(ProfilerEvents.TSQL.SQLStmtStarting,
                        ProfilerEventColumns.TextData, ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU, ProfilerEventColumns.Reads,
                        ProfilerEventColumns.Writes, ProfilerEventColumns.Duration,
                        ProfilerEventColumns.SPID
                        , ProfilerEventColumns.StartTime, ProfilerEventColumns.EndTime
                        , ProfilerEventColumns.DatabaseName
                        , ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );
                if (m_currentsettings.EventsColumns.SQLStmtCompleted)
                    m_Rdr.SetEvent(ProfilerEvents.TSQL.SQLStmtCompleted,
                        ProfilerEventColumns.TextData, ProfilerEventColumns.LoginName,
                        ProfilerEventColumns.CPU, ProfilerEventColumns.Reads,
                        ProfilerEventColumns.Writes, ProfilerEventColumns.Duration,
                        ProfilerEventColumns.SPID
                        , ProfilerEventColumns.StartTime, ProfilerEventColumns.EndTime
                        , ProfilerEventColumns.DatabaseName
                        , ProfilerEventColumns.ApplicationName
                        , ProfilerEventColumns.HostName
                    );

                if (null != m_currentsettings.Filters.Duration)
                    SetIntFilter(m_currentsettings.Filters.Duration * 1000,
                        m_currentsettings.Filters.DurationFilterCondition, ProfilerEventColumns.Duration);
                SetIntFilter(m_currentsettings.Filters.Reads, m_currentsettings.Filters.ReadsFilterCondition,
                    ProfilerEventColumns.Reads);
                SetIntFilter(m_currentsettings.Filters.Writes, m_currentsettings.Filters.WritesFilterCondition,
                    ProfilerEventColumns.Writes);
                SetIntFilter(m_currentsettings.Filters.CPU, m_currentsettings.Filters.CpuFilterCondition,
                    ProfilerEventColumns.CPU);
                SetIntFilter(m_currentsettings.Filters.SPID, m_currentsettings.Filters.SPIDFilterCondition,
                    ProfilerEventColumns.SPID);

                SetStringFilter(m_currentsettings.Filters.LoginName, m_currentsettings.Filters.LoginNameFilterCondition,
                    ProfilerEventColumns.LoginName);
                SetStringFilter(m_currentsettings.Filters.HostName, m_currentsettings.Filters.HostNameFilterCondition,
                    ProfilerEventColumns.HostName);
                SetStringFilter(m_currentsettings.Filters.DatabaseName,
                    m_currentsettings.Filters.DatabaseNameFilterCondition, ProfilerEventColumns.DatabaseName);
                SetStringFilter(m_currentsettings.Filters.TextData, m_currentsettings.Filters.TextDataFilterCondition,
                    ProfilerEventColumns.TextData);
                SetStringFilter(m_currentsettings.Filters.ApplicationName,
                    m_currentsettings.Filters.ApplicationNameFilterCondition, ProfilerEventColumns.ApplicationName);


                m_Cmd.Connection = m_Conn;
                m_Cmd.CommandTimeout = 0;
                m_Rdr.SetFilter(ProfilerEventColumns.ApplicationName, LogicalOperators.AND, ComparisonOperators.NotLike,
                    "Express Profiler");
                m_Cached.Clear();
                m_events.Clear();
                m_itembysql.Clear();
                lvEvents.VirtualListSize = 0;
                StartProfilerThread();
                m_servername = edServer.Text;
                m_username = edUser.Text;
                SaveDefaultSettings();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateButtons();
                Cursor = Cursors.Default;
            }
        }

        private void SaveDefaultSettings()
        {
            Settings.Default.ServerName = m_servername;
            Settings.Default.UserName = tbAuth.SelectedIndex == 0 ? "" : m_username;
            Settings.Default.Save();
        }


        private void SetIntFilter(int? value, TraceProperties.IntFilterCondition condition, int column)
        {
            int[] com =
            {
                ComparisonOperators.Equal, ComparisonOperators.NotEqual, ComparisonOperators.GreaterThan,
                ComparisonOperators.LessThan
            };
            if (null == value) return;
            long? v = value;
            m_Rdr.SetFilter(column, LogicalOperators.AND, com[(int) condition], v);
        }

        private void SetStringFilter(string value, TraceProperties.StringFilterCondition condition, int column)
        {
            if (!string.IsNullOrEmpty(value))
                m_Rdr.SetFilter(column, LogicalOperators.AND
                    , condition == TraceProperties.StringFilterCondition.Like
                        ? ComparisonOperators.Like
                        : ComparisonOperators.NotLike
                    , value
                );
        }

        private void StartProfilerThread()
        {
            m_Rdr?.Close();
            m_Rdr?.StartTrace();
            m_Thr = new Thread(ProfilerThread) {IsBackground = true, Priority = ThreadPriority.Lowest};
            m_NeedStop = false;
            m_ProfilingState = ProfilingStateEnum.psProfiling;
            NewEventArrived(m_EventStarted, true);
            m_Thr.Start();
        }

        private void ResumeProfiling()
        {
            StartProfilerThread();
            UpdateButtons();
        }

        private void tbStop_Click(object sender, EventArgs e)
        {
            StopProfiling();
        }

        private void StopProfiling()
        {
            tbStop.Enabled = false;
            using (var cn = GetConnection())
            {
                cn.Open();
                m_Rdr.StopTrace(cn);
                m_Rdr.CloseTrace(cn);
                cn.Close();
            }

            m_NeedStop = true;
            if (m_Thr.IsAlive) m_Thr.Abort();
            m_Thr.Join();
            m_ProfilingState = ProfilingStateEnum.psStopped;
            NewEventArrived(m_EventStopped, true);
            UpdateButtons();
        }

        private void listView1_ItemSelectionChanged_1(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            UpdateSourceBox();
        }

        private void UpdateSourceBox()
        {
            if (dontUpdateSource) return;
            var sb = new StringBuilder();

            foreach (int i in lvEvents.SelectedIndices)
            {
                var lv = m_Cached[i];
                if (lv.SubItems[1].Text != "") sb.AppendFormat("{0}\r\ngo\r\n", lv.SubItems[1].Text);
            }

            m_Lex.FillRichEdit(reTextData, sb.ToString());
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (m_ProfilingState != ProfilingStateEnum.psPaused &&
                m_ProfilingState != ProfilingStateEnum.psProfiling) return;
            if (MessageBox.Show("There are traces still running. Are you sure you want to close the application?",
                    "ExpressProfiler", MessageBoxButtons.YesNo, MessageBoxIcon.Question
                    , MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                StopProfiling();
            else
                e.Cancel = true;
        }

        private void lvEvents_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = m_Cached[e.ItemIndex];
        }

        private void tbPause_Click(object sender, EventArgs e)
        {
            PauseProfiling();
        }

        private void PauseProfiling()
        {
            using (var cn = GetConnection())
            {
                cn.Open();
                m_Rdr.StopTrace(cn);
                cn.Close();
            }

            m_ProfilingState = ProfilingStateEnum.psPaused;
            NewEventArrived(m_EventPaused, true);
            UpdateButtons();
        }


        internal void SelectAllEvents(bool select)
        {
            lock (m_Cached)
            {
                lvEvents.BeginUpdate();
                dontUpdateSource = true;
                try
                {
                    foreach (var lv in m_Cached) lv.Selected = select;
                }
                finally
                {
                    dontUpdateSource = false;
                    UpdateSourceBox();
                    lvEvents.EndUpdate();
                }
            }
        }

        private void lvEvents_KeyDown(object sender, KeyEventArgs e)
        {
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
        }


        private void timer1_Tick(object sender, EventArgs e)
        {
            Queue<ProfilerEvent> saved;
            Exception exc;
            lock (this)
            {
                saved = m_events;
                m_events = new Queue<ProfilerEvent>(10);
                exc = m_profilerexception;
                m_profilerexception = null;
            }

            if (null != exc)
                using (var dlg = new ThreadExceptionDialog(exc))
                {
                    dlg.ShowDialog();
                }

            lock (m_Cached)
            {
                while (0 != saved.Count) NewEventArrived(saved.Dequeue(), 0 == saved.Count);
                if (m_Cached.Count > m_currentsettings.Filters.MaximumEventCount)
                {
                    while (m_Cached.Count > m_currentsettings.Filters.MaximumEventCount) m_Cached.RemoveAt(0);
                    lvEvents.VirtualListSize = m_Cached.Count;
                    lvEvents.Invalidate();
                }

                if (null != m_prev && !(m_prev.m_date.Subtract(DateTime.Now).TotalSeconds >= 1)) return;
                var curr = new PerfInfo {m_count = m_EventCount};
                if (m_perf.Count >= 60) m_first = m_perf.Dequeue();
                if (null == m_first) m_first = curr;
                if (null == m_prev) m_prev = curr;

                var now = DateTime.Now;
                var d1 = now.Subtract(m_prev.m_date).TotalSeconds;
                var d2 = now.Subtract(m_first.m_date).TotalSeconds;
                slEPS.Text =
                    $"{(Math.Abs(d1 - 0) > 0.001 ? ((curr.m_count - m_prev.m_count) / d1).ToString("#,0.00") : "")} / {(Math.Abs(d2 - 0) > 0.001 ? ((curr.m_count - m_first.m_count) / d2).ToString("#,0.00") : "")} EPS(last/avg for {d2.ToString("0")} second(s))";

                m_perf.Enqueue(curr);
                m_prev = curr;
            }
        }

        private void tbAuth_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void ClearTrace()
        {
            lock (lvEvents)
            {
                m_Cached.Clear();
                m_itembysql.Clear();
                lvEvents.VirtualListSize = 0;
                listView1_ItemSelectionChanged_1(lvEvents, null);
                lvEvents.Invalidate();
            }
        }

        private void tbClear_Click(object sender, EventArgs e)
        {
            ClearTrace();
        }

        private void NewAttribute(XmlNode node, string name, string value)
        {
            if (node.OwnerDocument == null) return;
            var attr = node.OwnerDocument.CreateAttribute(name);
            attr.Value = value;
            node.Attributes?.Append(attr);
        }

        private void NewAttribute(XmlNode node, string name, string value, string namespaceURI)
        {
            if (node.OwnerDocument == null) return;
            var attr = node.OwnerDocument.CreateAttribute("ss", name, namespaceURI);
            attr.Value = value;
            node.Attributes?.Append(attr);
        }

        private void copyAllToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyEventsToClipboard(false);
        }

        private void CopyEventsToClipboard(bool copySelected)
        {
            var doc = new XmlDocument();
            XmlNode root = doc.CreateElement("events");
            lock (m_Cached)
            {
                if (copySelected)
                    foreach (int i in lvEvents.SelectedIndices)
                        CreateEventRow((ProfilerEvent) m_Cached[i].Tag, root);
                else
                    foreach (var i in m_Cached)
                        CreateEventRow((ProfilerEvent) i.Tag, root);
            }

            doc.AppendChild(root);
            doc.PreserveWhitespace = true;
            using (var writer = new StringWriter())
            {
                var textWriter = new XmlTextWriter(writer) {Formatting = Formatting.Indented};
                doc.Save(textWriter);
                Clipboard.SetText(writer.ToString());
            }

            MessageBox.Show("Event(s) data copied to clipboard", "Information", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void CreateEventRow(ProfilerEvent evt, XmlNode root)
        {
            XmlNode row = root.OwnerDocument?.CreateElement("event");
            NewAttribute(row, "EventClass", evt.EventClass.ToString(CultureInfo.InvariantCulture));
            NewAttribute(row, "CPU", evt.CPU.ToString(CultureInfo.InvariantCulture));
            NewAttribute(row, "Reads", evt.Reads.ToString(CultureInfo.InvariantCulture));
            NewAttribute(row, "Writes", evt.Writes.ToString(CultureInfo.InvariantCulture));
            NewAttribute(row, "Duration", evt.Duration.ToString(CultureInfo.InvariantCulture));
            NewAttribute(row, "SPID", evt.SPID.ToString(CultureInfo.InvariantCulture));
            NewAttribute(row, "LoginName", evt.LoginName);
            NewAttribute(row, "DatabaseName", evt.DatabaseName);
            NewAttribute(row, "ObjectName", evt.ObjectName);
            NewAttribute(row, "ApplicationName", evt.ApplicationName);
            NewAttribute(row, "HostName", evt.HostName);
            row.InnerText = evt.TextData;
            root.AppendChild(row);
        }

        private void copySelectedToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyEventsToClipboard(true);
        }

        private void clearTraceWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ClearTrace();
        }

        private void extractAllEventsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyEventsToClipboard(false);
        }

        private void extractSelectedEventsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyEventsToClipboard(true);
        }


        private void pauseTraceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PauseProfiling();
        }

        private void stopTraceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopProfiling();
        }

        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoFind();
        }

        private void DoFind()
        {
            if (m_ProfilingState == ProfilingStateEnum.psProfiling)
            {
                MessageBox.Show("You cannot find when trace is running", "ExpressProfiler", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var f = new FindForm(this))
            {
                f.TopMost = TopMost;
                f.ShowDialog();
            }
        }

        private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lvEvents.Focused && m_ProfilingState != ProfilingStateEnum.psProfiling)
                SelectAllEvents(true);
            else if (reTextData.Focused) reTextData.SelectAll();
        }

        //internal void PerformFind(bool forwards)
        //{
        //    if(String.IsNullOrEmpty(lastpattern)) return;

        //    if (forwards)
        //    {
        //        for (int i = lastpos = lvEvents.Items.IndexOf(lvEvents.FocusedItem) + 1; i < m_Cached.Count; i++)
        //        {
        //            if (FindText(i))
        //            {
        //                return;
        //            }
        //        }
        //    }
        //    else
        //    {
        //        for (int i = lastpos = lvEvents.Items.IndexOf(lvEvents.FocusedItem) - 1; i > 0; i--)
        //        {
        //            if (FindText(i))
        //            {
        //                return;
        //            }
        //        }
        //    }
        //    MessageBox.Show(String.Format("Failed to find \"{0}\". Searched to the end of data. ", lastpattern), "ExpressProfiler", MessageBoxButtons.OK, MessageBoxIcon.Information);
        //}


        internal void PerformFind(bool forwards, bool wrapAround)
        {
            if (string.IsNullOrEmpty(lastpattern)) return;
            var lastpos = lvEvents.Items.IndexOf(lvEvents.FocusedItem);
            if (forwards)
            {
                for (var i = lastpos + 1; i < m_Cached.Count; i++)
                    if (FindText(i))
                        return;
                if (wrapAround)
                    for (var i = 0; i < lastpos; i++)
                        if (FindText(i))
                            return;
            }
            else
            {
                for (var i = lastpos - 1; i > 0; i--)
                    if (FindText(i))
                        return;
                if (wrapAround)
                    for (var i = m_Cached.Count; i > lastpos; i--)
                        if (FindText(i))
                            return;
            }

            MessageBox.Show($"Failed to find \"{lastpattern}\". Searched to the end of data. ", "ExpressProfiler",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        private void ShowSelectedEvent()
        {
            var focusedIndex = lvEvents.Items.IndexOf(lvEvents.FocusedItem);
            if (focusedIndex <= -1 || focusedIndex >= m_Cached.Count) return;
            var lvi = m_Cached[focusedIndex];

            lvi.Focused = true;
            lastpos = focusedIndex;
            SelectAllEvents(false);
            FocusLVI(lvi, true);
        }


        private bool FindText(int i)
        {
            var lvi = m_Cached[i];
            var evt = (ProfilerEvent) lvi.Tag;
            var pattern = wholeWord ? "\\b" + lastpattern + "\\b" : lastpattern;
            if (!Regex.IsMatch(evt.TextData, pattern, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase))
                return false;
            lvi.Focused = true;
            lastpos = i;
            SelectAllEvents(false);
            FocusLVI(lvi, true);
            return true;
        }

        private void findNextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_ProfilingState == ProfilingStateEnum.psProfiling)
            {
                MessageBox.Show("You cannot find when trace is running", "ExpressProfiler", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            PerformFind(true, false);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        internal void RunProfiling(bool showfilters)
        {
            if (showfilters)
            {
                var ts = m_currentsettings.GetCopy();
                using (var frm = new TraceProperties())
                {
                    frm.SetSettings(ts);
                    if (DialogResult.OK != frm.ShowDialog()) return;
                    m_currentsettings = frm.m_currentsettings.GetCopy();
                }
            }

            StartProfiling();
        }

        private void tbRunWithFilters_Click(object sender, EventArgs e)
        {
            RunProfiling(true);
        }

        private void copyToXlsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyForExcel();
        }

        private void CopyForExcel()
        {
            var doc = new XmlDocument();
            var pi = doc.CreateProcessingInstruction("mso-application", "progid='Excel.Sheet'");
            doc.AppendChild(pi);
            const string urn = "urn:schemas-microsoft-com:office:spreadsheet";
            XmlNode root = doc.CreateElement("ss", "Workbook", urn);
            NewAttribute(root, "xmlns:ss", urn);
            doc.AppendChild(root);

            XmlNode styles = doc.CreateElement("ss", "Styles", urn);
            root.AppendChild(styles);
            XmlNode style = doc.CreateElement("ss", "Style", urn);
            styles.AppendChild(style);
            NewAttribute(style, "ID", "s62", urn);
            XmlNode font = doc.CreateElement("ss", "Font", urn);
            style.AppendChild(font);
            NewAttribute(font, "Bold", "1", urn);


            XmlNode worksheet = doc.CreateElement("ss", "Worksheet", urn);
            root.AppendChild(worksheet);
            NewAttribute(worksheet, "Name", "Sql Trace", urn);
            XmlNode table = doc.CreateElement("ss", "Table", urn);
            worksheet.AppendChild(table);
            NewAttribute(table, "ExpandedColumnCount", m_columns.Count.ToString(CultureInfo.InvariantCulture), urn);

            foreach (ColumnHeader lv in lvEvents.Columns)
            {
                XmlNode r = doc.CreateElement("ss", "Column", urn);
                NewAttribute(r, "AutoFitWidth", "0", urn);
                NewAttribute(r, "Width", lv.Width.ToString(CultureInfo.InvariantCulture), urn);
                table.AppendChild(r);
            }

            XmlNode row = doc.CreateElement("ss", "Row", urn);
            table.AppendChild(row);
            foreach (ColumnHeader lv in lvEvents.Columns)
            {
                XmlNode cell = doc.CreateElement("ss", "Cell", urn);
                row.AppendChild(cell);
                NewAttribute(cell, "StyleID", "s62", urn);
                XmlNode data = doc.CreateElement("ss", "Data", urn);
                cell.AppendChild(data);
                NewAttribute(data, "Type", "String", urn);
                data.InnerText = lv.Text;
            }

            lock (m_Cached)
            {
                long rowNumber = 1;
                foreach (var lvi in m_Cached)
                {
                    row = doc.CreateElement("ss", "Row", urn);
                    table.AppendChild(row);
                    foreach (var pc in m_columns)
                        if (pc.Column != -1)
                        {
                            XmlNode cell = doc.CreateElement("ss", "Cell", urn);
                            row.AppendChild(cell);
                            XmlNode data = doc.CreateElement("ss", "Data", urn);
                            cell.AppendChild(data);
                            string dataType = string.Empty;
                            switch (ProfilerEventColumns.ProfilerColumnDataTypes[pc.Column])
                            {
                                case ProfilerColumnDataType.Int:
                                case ProfilerColumnDataType.Long:
                                    dataType = "Number";
                                    break;
                                case ProfilerColumnDataType.DateTime:
                                    dataType = "String";
                                    break;
                                case ProfilerColumnDataType.Byte:
                                    break;
                                case ProfilerColumnDataType.String:
                                    break;
                                case ProfilerColumnDataType.Guid:
                                    break;
                                default:
                                    dataType = "String";
                                    break;
                            }

                            if (ProfilerEventColumns.EventClass == pc.Column) dataType = "String";
                            NewAttribute(data, "Type", dataType, urn);
                            if (ProfilerEventColumns.EventClass == pc.Column)
                                data.InnerText = GetEventCaption((ProfilerEvent) lvi.Tag);
                            else
                                data.InnerText = pc.Column == -1
                                    ? ""
                                    : GetFormattedValue((ProfilerEvent) lvi.Tag, pc.Column,
                                          ProfilerEventColumns.ProfilerColumnDataTypes[pc.Column] ==
                                          ProfilerColumnDataType.DateTime
                                              ? pc.Format
                                              : "") ??
                                      "";
                        }
                        else
                        {
                            //The export of the sequence number '#' is handled here.
                            XmlNode cell = doc.CreateElement("ss", "Cell", urn);
                            row.AppendChild(cell);
                            XmlNode data = doc.CreateElement("ss", "Data", urn);
                            cell.AppendChild(data);
                            const string dataType = "Number";
                            NewAttribute(data, "Type", dataType, urn);
                            data.InnerText = rowNumber.ToString();
                        }

                    rowNumber++;
                }
            }

            using (var writer = new StringWriter())
            {
                var textWriter = new XmlTextWriter(writer) {Formatting = Formatting.Indented, Namespaces = true};
                doc.Save(textWriter);
                var xml = writer.ToString();
                var xmlStream = new MemoryStream();
                xmlStream.Write(Encoding.UTF8.GetBytes(xml), 0, xml.Length);
                Clipboard.SetData("XML Spreadsheet", xmlStream);
            }

            MessageBox.Show("Event(s) data copied to clipboard", "Information", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void mnAbout_Click(object sender, EventArgs e)
        {
            var aboutMsgOrig =
                $"{versionString} nhttps://expressprofiler.codeplex.com/ \n Filter Icon: http://www.softicons.com/toolbar-icons/iconza-light-blue-icons-by-turbomilk/filter-icon";

            var aboutMsg = new StringBuilder();
            aboutMsg.AppendLine(versionString + "\nhttps://expressprofiler.codeplex.com/");
            aboutMsg.AppendLine();
            aboutMsg.AppendLine("Filter Icon Downloaded From:");
            aboutMsg.AppendLine(
                "    http://www.softicons.com/toolbar-icons/iconza-light-blue-icons-by-turbomilk/filter-icon");
            aboutMsg.AppendLine("    By Author Turbomilk:  	http://turbomilk.com/");
            aboutMsg.AppendLine("    Used under Creative Commons License: http://creativecommons.org/licenses/by/3.0/");

            MessageBox.Show(aboutMsg.ToString(), "About", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void tbStayOnTop_Click(object sender, EventArgs e)
        {
            SetStayOnTop();
        }

        private void SetStayOnTop()
        {
            tbStayOnTop.Checked = !tbStayOnTop.Checked;
            TopMost = tbStayOnTop.Checked;
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            SetTransparent();
        }

        private void SetTransparent()
        {
            tbTransparent.Checked = !tbTransparent.Checked;
            Opacity = tbTransparent.Checked ? 0.50 : 1;
        }

        private void stayOnTopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetStayOnTop();
        }

        private void transparentToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetTransparent();
        }

        private void deleteSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (var i = lvEvents.SelectedIndices.Count - 1; i >= 0; i--)
                m_Cached.RemoveAt(lvEvents.SelectedIndices[i]);
            lvEvents.VirtualListSize = m_Cached.Count;
            lvEvents.SelectedIndices.Clear();
        }

        private void keepSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (var i = m_Cached.Count - 1; i >= 0; i--)
                if (!lvEvents.SelectedIndices.Contains(i))
                    m_Cached.RemoveAt(i);
            lvEvents.VirtualListSize = m_Cached.Count;
            lvEvents.SelectedIndices.Clear();
        }


        private void SaveToExcelXmlFile()
        {
            var doc = new XmlDocument();
            var pi = doc.CreateProcessingInstruction("mso-application", "progid='Excel.Sheet'");
            doc.AppendChild(pi);
            const string urn = "urn:schemas-microsoft-com:office:spreadsheet";
            XmlNode root = doc.CreateElement("ss", "Workbook", urn);
            NewAttribute(root, "xmlns:ss", urn);
            doc.AppendChild(root);

            XmlNode styles = doc.CreateElement("ss", "Styles", urn);
            root.AppendChild(styles);
            XmlNode style = doc.CreateElement("ss", "Style", urn);
            styles.AppendChild(style);
            NewAttribute(style, "ID", "s62", urn);
            XmlNode font = doc.CreateElement("ss", "Font", urn);
            style.AppendChild(font);
            NewAttribute(font, "Bold", "1", urn);


            XmlNode worksheet = doc.CreateElement("ss", "Worksheet", urn);
            root.AppendChild(worksheet);
            NewAttribute(worksheet, "Name", "Sql Trace", urn);
            XmlNode table = doc.CreateElement("ss", "Table", urn);
            worksheet.AppendChild(table);
            NewAttribute(table, "ExpandedColumnCount", m_columns.Count.ToString(CultureInfo.InvariantCulture), urn);

            foreach (ColumnHeader lv in lvEvents.Columns)
            {
                XmlNode r = doc.CreateElement("ss", "Column", urn);
                NewAttribute(r, "AutoFitWidth", "0", urn);
                NewAttribute(r, "Width", lv.Width.ToString(CultureInfo.InvariantCulture), urn);
                table.AppendChild(r);
            }

            XmlNode row = doc.CreateElement("ss", "Row", urn);
            table.AppendChild(row);
            foreach (ColumnHeader lv in lvEvents.Columns)
            {
                XmlNode cell = doc.CreateElement("ss", "Cell", urn);
                row.AppendChild(cell);
                NewAttribute(cell, "StyleID", "s62", urn);
                XmlNode data = doc.CreateElement("ss", "Data", urn);
                cell.AppendChild(data);
                NewAttribute(data, "Type", "String", urn);
                data.InnerText = lv.Text;
            }

            lock (m_Cached)
            {
                long rowNumber = 1;
                foreach (var lvi in m_Cached)
                {
                    row = doc.CreateElement("ss", "Row", urn);
                    table.AppendChild(row);
                    foreach (var pc in m_columns)
                        if (pc.Column != -1)
                        {
                            XmlNode cell = doc.CreateElement("ss", "Cell", urn);
                            row.AppendChild(cell);
                            XmlNode data = doc.CreateElement("ss", "Data", urn);
                            cell.AppendChild(data);
                            string dataType = string.Empty;
                            switch (ProfilerEventColumns.ProfilerColumnDataTypes[pc.Column])
                            {
                                case ProfilerColumnDataType.Int:
                                case ProfilerColumnDataType.Long:
                                    dataType = "Number";
                                    break;
                                case ProfilerColumnDataType.DateTime:
                                    dataType = "String";
                                    break;
                                case ProfilerColumnDataType.Byte:
                                    break;
                                case ProfilerColumnDataType.String:
                                    break;
                                case ProfilerColumnDataType.Guid:
                                    break;
                                default:
                                    dataType = "String";
                                    break;
                            }

                            if (ProfilerEventColumns.EventClass == pc.Column) dataType = "String";
                            NewAttribute(data, "Type", dataType, urn);
                            if (ProfilerEventColumns.EventClass == pc.Column)
                                data.InnerText = GetEventCaption((ProfilerEvent) lvi.Tag);
                            else
                                data.InnerText = pc.Column == -1
                                    ? ""
                                    : GetFormattedValue((ProfilerEvent) lvi.Tag, pc.Column,
                                          ProfilerEventColumns.ProfilerColumnDataTypes[pc.Column] ==
                                          ProfilerColumnDataType.DateTime
                                              ? pc.Format
                                              : "") ??
                                      "";
                        }
                        else
                        {
                            //The export of the sequence number '#' is handled here.
                            XmlNode cell = doc.CreateElement("ss", "Cell", urn);
                            row.AppendChild(cell);
                            XmlNode data = doc.CreateElement("ss", "Data", urn);
                            cell.AppendChild(data);
                            const string dataType = "Number";
                            NewAttribute(data, "Type", dataType, urn);
                            data.InnerText = rowNumber.ToString();
                        }

                    rowNumber++;
                }
            }

            var sfd = new SaveFileDialog {Filter = "Excel XML|*.xml", Title = "Save the Excel XML FIle"};
            sfd.ShowDialog();

            if (string.IsNullOrEmpty(sfd.FileName)) return;
            using (var writer = new StringWriter())
            {
                var textWriter = new XmlTextWriter(writer)
                {
                    Formatting = Formatting.Indented,
                    Namespaces = true
                };
                doc.Save(textWriter);
                var xml = writer.ToString();
                var xmlStream = new MemoryStream();
                xmlStream.Write(Encoding.UTF8.GetBytes(xml), 0, xml.Length);
                xmlStream.Position = 0;
                var fs = new FileStream(sfd.FileName, FileMode.Create, FileAccess.Write);
                xmlStream.WriteTo(fs);
                fs.Close();
                xmlStream.Close();
            }

            MessageBox.Show($"File saved to: {sfd.FileName}", "Information", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }


        private void SetFilterEvents()
        {
            if (m_CachedUnFiltered.Count != 0) return;
            lvEvents.SelectedIndices.Clear();
            var ts = m_currentsettings.GetCopy();
            using (var frm = new TraceProperties())
            {
                frm.SetSettings(ts);
                if (DialogResult.OK != frm.ShowDialog()) return;
                ts = frm.m_currentsettings.GetCopy();

                m_CachedUnFiltered.AddRange(m_Cached);
                m_Cached.Clear();
                foreach (var lvi in m_CachedUnFiltered)
                    if (frm.IsIncluded(lvi) && m_Cached.Count < ts.Filters.MaximumEventCount)
                        m_Cached.Add(lvi);
            }

            lvEvents.VirtualListSize = m_Cached.Count;
            UpdateSourceBox();
            ShowSelectedEvent();
        }


        private void ClearFilterEvents()
        {
            if (m_CachedUnFiltered.Count <= 0) return;
            m_Cached.Clear();
            m_Cached.AddRange(m_CachedUnFiltered);
            m_CachedUnFiltered.Clear();
            lvEvents.VirtualListSize = m_Cached.Count;
            lvEvents.SelectedIndices.Clear();
            UpdateSourceBox();
            ShowSelectedEvent();
        }


        private void saveAllEventsToExcelXmlFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveToExcelXmlFile();
        }

        /// <summary>
        ///     Persist the server string when it changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void edServer_TextChanged(object sender, EventArgs e)
        {
            m_servername = edServer.Text;
            SaveDefaultSettings();
        }


        /// <summary>
        ///     Persist the user name string when it changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void edUser_TextChanged(object sender, EventArgs e)
        {
            m_username = edUser.Text;
            SaveDefaultSettings();
        }


        private void filterCapturedEventsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetFilterEvents();
        }

        private void clearCapturedFiltersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ClearFilterEvents();
        }

        private void tbFilterEvents_Click(object sender, EventArgs e)
        {
            var filterButton = (ToolStripButton) sender;
            if (filterButton.Checked)
                SetFilterEvents();
            else
                ClearFilterEvents();
        }

        private class PerfInfo
        {
            internal readonly DateTime m_date = DateTime.Now;
            internal int m_count;
        }

        public class PerfColumn
        {
            public HorizontalAlignment Alignment = HorizontalAlignment.Left;
            public string Caption;
            public int Column;
            public string Format;
            public int Width;
        }

        private enum ProfilingStateEnum
        {
            psStopped,
            psProfiling,
            psPaused
        }
    }
}