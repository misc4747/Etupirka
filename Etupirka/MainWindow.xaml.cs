﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using System.Net;
using System.Threading;
using System.IO;
using System.Xml;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Serialization.Formatters.Binary;
using SatoruErogeTimer;
using System.Diagnostics;
using System.ComponentModel;
using Hardcodet.Wpf.TaskbarNotification;
using System.Media;
using System.Collections;
using System.Xml.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Controls.Primitives;
using System.Data.SQLite;
using Etupirka.Dialog;
using System.Threading.Tasks;
using Etupirka.Properties;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Web;

namespace Etupirka
{
	public enum StatusBarStatus
	{
		watching = 0, resting = 1, erogehelper = 2
	}

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : MetroWindow, INotifyPropertyChanged
	{
		#region Property
		public event PropertyChangedEventHandler PropertyChanged;
		protected void OnPropertyChanged(string name)
		{
			if (PropertyChanged == null) return;
			PropertyChanged(this, new PropertyChangedEventArgs(name));
		}

		public int ItemCount
		{
			get
			{
				if (items == null)
				{
					return 0;
				}
				return items.Count;
			}
		}

		public string TotalTime
		{
			get
			{
				if (items == null) return "";
				int t = 0;
				foreach (GameExecutionInfo i in items)
				{
					t += i.TotalPlayTime;
				}
				return t / 3600 + @"時間" + (t / 60) % 60 + @"分";
			}
		}

		public bool WatchProc
		{
			get
			{
				return Properties.Settings.Default.watchProcess;
			}
			set
			{
				Properties.Settings.Default.watchProcess = value;

				OnPropertyChanged("WatchProc");
				OnPropertyChanged("StatusBarStat");
			}
		}


		bool erogeHelper=false;
		public bool ErogeHelper
		{
			get
			{
				return erogeHelper;
			}
			set
			{

				if (!value)
				{
					GameListView.Visibility = Visibility.Visible;
					MenuBar.Visibility = Visibility.Visible;
					this.ShowTitleBar = true;

				}
				else
				{
					GameListView.Visibility = Visibility.Hidden;
					MenuBar.Visibility = Visibility.Hidden;
					this.ShowTitleBar = false;
				}

				erogeHelper = value;
				OnPropertyChanged("ErogeHelper");
				OnPropertyChanged("StatusBarStat");
			}
		}


		public StatusBarStatus StatusBarStat
		{
			get
			{
				if (ErogeHelper) return StatusBarStatus.erogehelper;
				if (WatchProc) return StatusBarStatus.watching;
				return StatusBarStatus.resting;
			}
		}

		#endregion

		#region Hotkey
		private HotKey _hotkey;
		private void OnHotKeyHandler_WatchProc(HotKey hotKey)
		{
			if (Properties.Settings.Default.playVoice)
			{
				if (WatchProc)
				{
					SoundPlayer sp = new SoundPlayer(Properties.Resources.stop2);
					sp.Play();
				}
				else
				{
					SoundPlayer sp = new SoundPlayer(Properties.Resources.start2);
					sp.Play();

				}
			}
			WatchProc = !WatchProc;
		}

		private void OnHotKeyHandler_ErogeHelper(HotKey hotKey)
		{
			ErogeHelper = !ErogeHelper;
		}

		#endregion
		
		private ObservableCollection<GameExecutionInfo> items;
        private System.Windows.Threading.DispatcherTimer watchProcTimer;
		private DBManager db;
        private ProcessInfoCache processInfoCache = new ProcessInfoCache();

        public MainWindow()
		{

			if (Settings.Default.UpgradeRequired)
			{
				Settings.Default.Upgrade();
				Settings.Default.UpgradeRequired = false;
				Settings.Default.Save();
			}

			InitializeComponent();
			tbico.DoubleClickCommand = new ShowAppCommand(this);
			tbico.DataContext = this;

			if (Settings.Default.Do_minimize)
			{
				this.Hide();
				Settings.Default.Do_minimize = false;
				Settings.Default.Save();
			}

			db = new DBManager(Utility.userDBPath);
			Utility.im = new InformationManager(Utility.infoDBPath);

            items = new ObservableCollection<GameExecutionInfo>();
            db.LoadGame(items);

			GameListView.ItemsSource = items;
			GameListView.SelectedItem = null;
			UpdateStatus();
			OnPropertyChanged("ItemCount");

			_hotkey = new HotKey(Key.F9, KeyModifier.Alt, OnHotKeyHandler_WatchProc);
			_hotkey = new HotKey(Key.F8, KeyModifier.Alt, OnHotKeyHandler_ErogeHelper);

			RegisterInStartup(Properties.Settings.Default.setStartUp);
			if (Properties.Settings.Default.disableGlowBrush)
			{
				this.GlowBrush = null;
			}

			watchProcTimer = new System.Windows.Threading.DispatcherTimer();
			watchProcTimer.Tick += new EventHandler(dispatcherTimer_Tick);
			watchProcTimer.Interval = new TimeSpan(0, 0, Properties.Settings.Default.monitorInterval);
			watchProcTimer.Start();

            if (Settings.Default.checkUpdate)
            {
                try
                {
                    _ = doCheckUpdate();
                }
                catch { }
            }
        }

        #region Function
        private async Task<bool> doCheckUpdate()
        {
            JToken json = await NetworkUtility.GetJson("https://api.github.com/repos/aixile/etupirka/releases");
            JArray jArr = (JArray)json;
            string str = jArr.Count > 0 ? jArr[0]["tag_name"]?.ToString() : null;

            if (str == null)
            {
                throw new Exception("Unable to parse Github response");
            }

            Version lastestVersion = new Version(str);
            Version myVersion = new Version(FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion);
            if (lastestVersion > myVersion)
            {
                if (MessageBox.Show("Version " + str + " が見つかりました、更新しますか？", "Etupirkaを更新する", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    Process.Start("https://github.com/Aixile/Etupirka/releases");
                }
                return true;
            }
            return false;
        }

        private void UpdateStatus(int time=0)
		{
			IntPtr actWin = Utility.GetForegroundWindow();
			int calcID;
			Utility.GetWindowThreadProcessId(actWin, out calcID);
            var currentProc = Process.GetProcessById(calcID);
            try
            {
                if (System.IO.Path.GetFileName(currentProc.MainModule.FileName) == "main.bin") //SoftDenchi DRM
                {
                    calcID = Utility.ParentProcessUtilities.GetParentProcess(calcID).Id;
                }
            }
            catch(Exception e) {
                Console.WriteLine(e);
            }

            bool play_flag = false;

            this.Dispatcher.BeginInvoke(new Action(() => {
                Process[] proc = Process.GetProcesses();

                Dictionary<String, bool> dic = new Dictionary<string, bool>();
                using (var scopedAccess = processInfoCache.scopedAccess())
                {
                    foreach (Process p in proc)
                    {
                        try
                        {
                            string path = processInfoCache.getProcessPath(p);
                            if (path == "")
                            {
                                continue;
                            }
                            bool isForeground = p.Id == calcID;
                            if (dic.ContainsKey(path))
                            {
                                dic[path] |= isForeground;
                            }
                            else
                            {
                                dic[path] = isForeground;
                            }
                        }
                        catch (Exception e)
                        {
                            // Console.WriteLine(e);
                        }
                    }
                }

                string statusBarText = "";
                string trayTipText = "Etupirka Version " + FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion;

                foreach (GameExecutionInfo i in items)
                {
                    bool running = false;
                    if (i.UpdateStatus2(dic, ref running, time))
                    {
                        if (time != 0)
                        {
                            db.UpdateTimeNow(i.UID, time);
                        }
                        db.UpdateGameTimeInfo(i.UID, i.TotalPlayTime, i.FirstPlayTime, i.LastPlayTime);
                        if (i.Status == ProcStat.Focused)
                        {
                            play_flag = true;
                            PlayMessage.Content = i.Title + " : " + i.TotalPlayTimeString;

                            if (Properties.Settings.Default.hideListWhenPlaying)
                            {
                                ErogeHelper = true;
                            }
                        }
                    }
                    if (running)
                    {
                        trayTipText += "\n" + i.Title + " : " + i.TotalPlayTimeString;
                    }
                }

                dic.Clear();
                if (!play_flag)
                {
                    PlayMessage.Content = statusBarText;

                    if (Properties.Settings.Default.hideListWhenPlaying)
                    {
                        ErogeHelper = false;
                    }
                }
                tbico.ToolTipText = trayTipText;

                OnPropertyChanged("TotalTime");
            }));
        }

        private void RegisterInStartup(bool isChecked)
        {
            try
            {
                RegistryKey registryKey = Registry.CurrentUser.OpenSubKey
                        ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (isChecked)
                {
                    if (Settings.Default.minimizeAtStartup)
                    {
                        registryKey.SetValue("Etupirka", "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\" -min");

                    }
                    else
                    {
                        registryKey.SetValue("Etupirka", System.Reflection.Assembly.GetEntryAssembly().Location);
                    }
                }
                else
                {
                    registryKey.DeleteValue("Etupirka");
                }
            }
            catch
            {

            }
        }

        void ImportXML(string dataPath)
        {
            GameExecutionInfo[] erogeList = null;
            System.Xml.Serialization.XmlSerializer serializer2 = new System.Xml.Serialization.XmlSerializer(typeof(GameExecutionInfo[]));
            System.IO.StreamReader sr = new System.IO.StreamReader(dataPath);
            erogeList = (GameExecutionInfo[])serializer2.Deserialize(sr);
            sr.Close();
            if (erogeList != null)
            {
                foreach (GameExecutionInfo i in erogeList)
                {
                    if (i.UID == null)
                    {
                        i.GenerateUID();
                    }
                    items.Add(i);
                }
            }
            db.InsertOrIgnoreGame(items);
            UpdateStatus();
            OnPropertyChanged("ItemCount");
        }

        void ImportXML_Overwrite(string dataPath)
        {
            GameExecutionInfo[] erogeList = null;
            System.Xml.Serialization.XmlSerializer serializer2 = new System.Xml.Serialization.XmlSerializer(typeof(GameExecutionInfo[]));
            System.IO.StreamReader sr = new System.IO.StreamReader(dataPath);
            erogeList = (GameExecutionInfo[])serializer2.Deserialize(sr);
            sr.Close();
            if (erogeList != null)
            {
                foreach (GameExecutionInfo i in erogeList)
                {
                    if (i.UID == null)
                    {
                        i.GenerateUID();
                    }
                    items.Add(i);
                }
            }
            db.InsertOrReplaceGame(items);
            UpdateStatus();
            OnPropertyChanged("ItemCount");
        }

        public void ExportXML(string dataPath)
        {
            System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(GameExecutionInfo[]));
            System.IO.StreamWriter sw = new System.IO.StreamWriter(dataPath, false);
            serializer.Serialize(sw, items.ToArray());
            sw.Close();
        }

        void ExportXML(string dataPath, List<GameExecutionInfo> a)
        {
            System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(GameExecutionInfo[]));
            System.IO.StreamWriter sw = new System.IO.StreamWriter(dataPath, false);
            serializer.Serialize(sw, a.ToArray());
            sw.Close();
        }

        void ImportTimeDict(string dataPath)
        {
            Dictionary<string, TimeData> timeDict = new Dictionary<string, TimeData>();
            try
            {
                XElement xElem = XElement.Load(dataPath);
                timeDict = xElem.Descendants("day").ToDictionary(
                    x => ((string)x.Attribute("d")).Replace('/', '-'), x => new TimeData(x.Descendants("game").ToDictionary(
                          y => (string)y.Attribute("uid"), y => (int)y.Attribute("value"))));
            }
            catch
            {
                timeDict = new Dictionary<string, TimeData>();
            }

            db.AddPlayTime(timeDict);

        }

        void ImportTimeDict_Overwrite(string dataPath)
        {
            Dictionary<string, TimeData> timeDict = new Dictionary<string, TimeData>();
            try
            {
                XElement xElem = XElement.Load(dataPath);
                timeDict = xElem.Descendants("day").ToDictionary(
                    x => ((string)x.Attribute("d")).Replace('/', '-'), x => new TimeData(x.Descendants("game").ToDictionary(
                         y => (string)y.Attribute("uid"), y => (int)y.Attribute("value"))));
            }
            catch
            {
                timeDict = new Dictionary<string, TimeData>();
            }

            db.InsertOrReplaceTime(timeDict);
        }

        void ExportTimeDict(string dataPath)
        {
            Dictionary<string, TimeData> timeDict = db.GetPlayTime();

            XElement xElem = new XElement("days",
                    timeDict.Select(x => new XElement("day",
                    new XAttribute("d", x.Key),
                    new XElement("games",
                        ((TimeData)x.Value).d.Select(
                         y => new XElement("game",
                             new XAttribute("uid", y.Key),
                             new XAttribute("value", y.Value)))))));
            xElem.Save(dataPath);
        }

        void ExportTimeDict(string dataPath, List<GameExecutionInfo> a)
        {
            Dictionary<string, TimeData> timeDict = db.GetPlayTime(a);

            XElement xElem = new XElement("days",
                    timeDict.Select(x => new XElement("day",
                    new XAttribute("d", x.Key),
                    new XElement("games",
                        ((TimeData)x.Value).d.Select(
                         y => new XElement("game",
                             new XAttribute("uid", y.Key),
                             new XAttribute("value", y.Value)))))));
            xElem.Save(dataPath);
        }
        #endregion

        #region Event

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = false;
            if (Settings.Default.askBeforeExit)
            {
                try
                {
                    Dialog.AskExitDialog acd = new Dialog.AskExitDialog();
                    acd.Owner = this;
                    e.Cancel = true;
                    if (acd.ShowDialog() == true)
                    {
                        e.Cancel = false;
                        if (acd.DoNotDisplay)
                        {
                            Settings.Default.askBeforeExit = false;
                            Settings.Default.Save();
                        }
                    }
                }
                catch
                {
                }

            }

            base.OnClosing(e);
        }

        private void GameListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DoOpenGame();
            }
            else if (e.Key == Key.Delete)
            {

                DoDelete();
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == System.Windows.WindowState.Minimized)
                this.Hide();

            base.OnStateChanged(e);
        }


        private void doUpdate()
        {
            UpdateStatus(Properties.Settings.Default.monitorInterval);
        }

        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.watchProcess)
            {
                Thread t = new Thread(doUpdate);
                t.Start();
            }
        }

        private void MetroWindow_Deactivated(object sender, EventArgs e)
        {
            GameListView.UnselectAll();
        }

        private void GameListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listView = (ListView)sender;
            var item = listView.ContainerFromElement((DependencyObject)e.OriginalSource) as ListViewItem;
            if (item != null)
            {
                GameExecutionInfo g = (GameExecutionInfo)GameListView.SelectedItem;
                if (g != null) g.run();
            }
        }

        #endregion

        #region MenuRegion
        private void AddGameFromESID_Click(object sender, RoutedEventArgs e)
        {
            Dialogs.AddGameFromESIDDialog ad = new Dialogs.AddGameFromESIDDialog();
            ad.Owner = this;
            if (ad.ShowDialog() == true)
            {
                GameExecutionInfo i = new GameExecutionInfo(ad.Value);
                items.Add(i);
                db.InsertOrReplaceGame(i);

                UpdateStatus();
                OnPropertyChanged("ItemCount");
            }

        }

        private void AddGameFromProcess_Click(object sender, RoutedEventArgs e)
        {
            Dialog.ProcessDialog pd = new Dialog.ProcessDialog();
            pd.Owner = this;
            if (pd.ShowDialog() == true)
            {
                GameExecutionInfo i = new GameExecutionInfo(pd.SelectedProc.ProcTitle, pd.SelectedProc.ProcPath);
                items.Add(i);
                db.InsertOrReplaceGame(i);

                UpdateStatus();
                OnPropertyChanged("ItemCount");
            }
        }

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            Dialogs.GlobalSettingDialog gd = new Dialogs.GlobalSettingDialog();
            gd.Owner = this;
            int mi = Properties.Settings.Default.monitorInterval;
            bool watchProc = Properties.Settings.Default.watchProcess;
            if (gd.ShowDialog() == true)
            {
                GameListView.Items.Refresh();
                RegisterInStartup(Properties.Settings.Default.setStartUp);
                bool watchProc_a = Properties.Settings.Default.watchProcess;
                if (watchProc != watchProc_a)
                {
                    WatchProc = watchProc_a;
                }

                if (mi != Properties.Settings.Default.monitorInterval)
                {
                    watchProcTimer.Stop();
                    watchProcTimer.Interval = new TimeSpan(0, 0, Properties.Settings.Default.monitorInterval);
                    watchProcTimer.Start();
                }
            }
        }

        private void ImportList_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Multiselect = false;
            openFileDialog1.Filter = "Etupirka GameData XMLFile(*.xml)|*.xml";
            openFileDialog1.FilterIndex = 1;
            if (openFileDialog1.ShowDialog() == true)
            {
                ImportXML(openFileDialog1.FileName);
            }
        }

        private void ImportList_Overwrite_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Multiselect = false;
            openFileDialog1.Filter = "Etupirka GameData XMLFile(*.xml)|*.xml";
            openFileDialog1.FilterIndex = 1;
            if (openFileDialog1.ShowDialog() == true)
            {
                ImportXML_Overwrite(openFileDialog1.FileName);
            }
        }

        private void ImportPlayTimeList_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Multiselect = false;
            openFileDialog1.Filter = "Etupirka PlayTimeData XMLFile(*.xml)|*.xml";
            openFileDialog1.FilterIndex = 1;
            if (openFileDialog1.ShowDialog() == true)
            {
                ImportTimeDict(openFileDialog1.FileName);
            }
        }

        private void ImportPlayTimeList_Overwrite_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Multiselect = false;
            openFileDialog1.Filter = "Etupirka PlayTimeData XMLFile(*.xml)|*.xml";
            openFileDialog1.FilterIndex = 1;
            if (openFileDialog1.ShowDialog() == true)
            {
                ImportTimeDict_Overwrite(openFileDialog1.FileName);
            }
        }

        private void ExportList_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog openFileDialog1 = new SaveFileDialog();
            openFileDialog1.Filter = "ErogeTimerXMLFile(*.xml)|*.xml";
            openFileDialog1.FilterIndex = 1;
            if (openFileDialog1.ShowDialog() == true)
            {
                ExportXML(openFileDialog1.FileName);
                ExportTimeDict(System.IO.Path.ChangeExtension(openFileDialog1.FileName, ".time.xml"));
            }
        }

        private void ExportSelect_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog openFileDialog1 = new SaveFileDialog();
            openFileDialog1.Filter = "ErogeTimerXMLFile(*.xml)|*.xml";
            openFileDialog1.FilterIndex = 1;
            List<GameExecutionInfo> t = GameListView.SelectedItems.Cast<GameExecutionInfo>().ToList();
            if (openFileDialog1.ShowDialog() == true)
            {

                foreach (GameExecutionInfo i in t)
                {
                    GameListView.SelectedItems.Add(i);
                }
                ExportXML(openFileDialog1.FileName, t);
                ExportTimeDict(System.IO.Path.ChangeExtension(openFileDialog1.FileName, ".time.xml"), t);
            }
        }
        private void PlayTimeStatistic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Dialog.PlayTimeStatisticDialog pd = new Dialog.PlayTimeStatisticDialog(Utility.userDBPath);
                pd.Owner = this;
                if (pd.ShowDialog() == true)
                {
                }
            }
            catch (Exception a)
            {
                MessageBox.Show(a.Message);
            }

        }

        private async void UpdateOfflineDatabase_Click(object sender, RoutedEventArgs e)
        {
            var controller = await this.ShowProgressAsync("更新しています", "Downloading...");
            try
            {
                controller.SetCancelable(true);
                var source = new CancellationTokenSource();
                controller.Canceled += (_, __) => {
                    source.Cancel();
                };

                var document = await NetworkUtility.PostHtmlDocument(
                    "http://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/sql_for_erogamer_form.php",
                    new FormUrlEncodedContent(new[] {
                        new KeyValuePair<string, string>("sql",
                            @"SELECT g.id, g.gamename, g.sellday, b.brandname
                            FROM brandlist b, gamelist g
                            WHERE g.brandname = b.id
                            ORDER BY g.id"),
                    }),
                    source.Token,
                    timeoutMs: 30000);

                var infoList = new List<GameInfo>();
                var table = document.GetElementbyId("query_result_main").Descendants("table").First();
                foreach (var tr in table.Descendants("tr"))
                {
                    var tds = tr.Descendants("td").ToList();
                    if (tds.Count == 4)
                    {
                        var info = new GameInfo();
                        info.ErogameScapeID = int.Parse(tds[0].InnerText);
                        info.Title = HttpUtility.HtmlDecode(tds[1].InnerText);
                        info.SaleDay = DateTime.ParseExact(tds[2].InnerText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        info.Brand = HttpUtility.HtmlDecode(tds[3].InnerText);
                        infoList.Add(info);
                    }
                }

                controller.SetMessage("Updating database...");
                Utility.im.update(infoList);
                await controller.CloseAsync();
                await this.ShowMessageAsync("データベースを更新する", "成功しました");
            }
            catch
            {
                await controller.CloseAsync();
                await this.ShowMessageAsync("データベースを更新する", "失敗しました");
            }
        }

        private void QuitApp_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async void showMessage(string title, string str)
        {
            MessageDialogResult re = await this.ShowMessageAsync(title, str);

        }
        private void VersionInfo_Click(object sender, RoutedEventArgs e)
        {

            showMessage("バージョン情報", "Version " + FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion +
                " By Aixile (@namaniku0).");

        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!await doCheckUpdate())
                {
                    MessageBox.Show("すでに最新バージョンがインストールされている", "更新をチェックする");
                }
            }
            catch
            {
                MessageBox.Show("失敗しました", "更新をチェックする");
            }
        }


        #endregion

        #region ContextMenuRegion
        private void GameProperty_Click(object sender, RoutedEventArgs e)
        {
            if (GameListView.SelectedItems.Count > 0)
            {
                GameExecutionInfo i = (GameExecutionInfo)GameListView.SelectedItem;
                Dialog.GamePropertyDialog gd = new Dialog.GamePropertyDialog(i);
                gd.Owner = this;
                if (gd.ShowDialog() == true)
                {
                    UpdateStatus();
                    db.UpdateGameInfoAndExec(i);
                }
            }
        }

        private void TimeSetting_Click(object sender, RoutedEventArgs e)
        {
            if (GameListView.SelectedItems.Count > 0)
            {
                GameExecutionInfo i = (GameExecutionInfo)GameListView.SelectedItem;
                Dialog.TimeSettingDialog td = new Dialog.TimeSettingDialog(i);

                td.Owner = this;
                if (td.ShowDialog() == true)
                {
                    UpdateStatus();
                    db.UpdateGameTimeInfo(i.UID, i.TotalPlayTime, i.FirstPlayTime, i.LastPlayTime);
                }
            }
        }

        private void OpenPlayStatistics_Click(object sender, RoutedEventArgs e)
        {
            if (GameListView.SelectedItems.Count > 0)
            {
                GameExecutionInfo i = (GameExecutionInfo)GameListView.SelectedItem;
                List<TimeSummary> t = db.QueryGamePlayTime(i.UID);
                Dialog.GameTimeStatisticDialog td = new Dialog.GameTimeStatisticDialog(t);

                td.Owner = this;
                if (td.ShowDialog() == true)
                {
                }
            }
        }

        private async void DoDelete()
        {
            ArrayList a = new ArrayList(GameListView.SelectedItems);
            if (a.Count != 0)
            {
                MessageDialogResult re = await this.ShowMessageAsync("本当に削除しますか？", "", MessageDialogStyle.AffirmativeAndNegative);
                if (re != MessageDialogResult.Affirmative)
                {
                    return;
                }
            }
            foreach (GameExecutionInfo i in a)
            {
                db.DeleteGame(i.UID);
                (GameListView.ItemsSource as ObservableCollection<GameExecutionInfo>).Remove(i);
            }

            OnPropertyChanged("ItemCount");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DoDelete();
        }

        private void DoOpenGame()
        {
            GameExecutionInfo g = (GameExecutionInfo)GameListView.SelectedItem;
            if (g != null) g.run();
            UpdateStatus();
        }

        private void OpenGame_Click(object sender, RoutedEventArgs e)
        {
            DoOpenGame();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (GameListView.SelectedItems.Count > 0)
            {
                try
                {
                    GameExecutionInfo i = (GameExecutionInfo)GameListView.SelectedItem;
                    Process.Start(System.IO.Path.GetDirectoryName(i.ExecPath));
                }
                catch
                {

                }
            }
        }

        private void ShowApp_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        private void OpenES_Click(object sender, RoutedEventArgs e)
        {
            GameExecutionInfo g = (GameExecutionInfo)GameListView.SelectedItem;
            if (g != null && g.ErogameScapeID != 0)
            {
                var uri = "http://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game=" + g.ErogameScapeID;
                if (Settings.Default.useGoogleCache)
                {
                    uri = "https://webcache.googleusercontent.com/search?q=cache:" + HttpUtility.UrlEncode(uri) + "&strip=1";
                }
                Process.Start(uri);
            }
        }

        private void CopyTitle_Click(object sender, RoutedEventArgs e)
        {
            GameExecutionInfo g = (GameExecutionInfo)GameListView.SelectedItem;
            if (g != null)
            {
                Clipboard.SetText(g.Title);
            }

        }

        private void CopyBrand_Click(object sender, RoutedEventArgs e)
        {
            GameExecutionInfo g = (GameExecutionInfo)GameListView.SelectedItem;
            if (g != null)
            {
                Clipboard.SetText(g.Brand);
            }
        }

        private void TryGetGameInfo_Click(object sender, RoutedEventArgs e)
        {
            List<GameInfo> allGames = Utility.im.getAllEsInfo();
            GameExecutionInfo g = (GameExecutionInfo)GameListView.SelectedItem;
            Dictionary<GameInfo, int> dic = new Dictionary<GameInfo, int>();
            string title = g.Title;

            foreach (GameInfo i in allGames)
            {
                int dist = StringProcessing.LevenshteinDistance(title, i.Title);
                dic[i] = dist;
            }
            var ordered = dic.OrderBy(x => x.Value);
            GameInfo ans = ordered.ElementAt(0).Key;

            List<GameInfo> ans_l = new List<GameInfo>();
            for (int i = 0; i < 50; i++)
            {
                ans_l.Add(ordered.ElementAt(i).Key);
            }

            Dialog.GameInfoDialog td = new Dialog.GameInfoDialog(ans_l);

            td.Owner = this;
            if (td.ShowDialog() == true)
            {
                g.ErogameScapeID = td.SelectedGameInfo.ErogameScapeID;
                g.Title = td.SelectedGameInfo.Title;
                g.Brand = td.SelectedGameInfo.Brand;
                g.SaleDay = td.SelectedGameInfo.SaleDay;
                UpdateStatus();
                db.UpdateGameInfoAndExec(g);
            }

        }
        #endregion


    }

    #region Command
    public class ShowAppCommand : ICommand
    {
        MainWindow m;
        public ShowAppCommand(MainWindow _m)
        {
            m = _m;
        }

        public void Execute(object parameter)
        {
            if (m != null)
            {
                m.Show();
                m.WindowState = WindowState.Normal;
            }
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }
        public event EventHandler CanExecuteChanged;
    }
    #endregion

    #region Converter

    public class IndexConverter : IValueConverter
    {
        public object Convert(object value, Type TargetType, object parameter, CultureInfo culture)
        {
            var item = (ListViewItem)value;
            var listView = ItemsControl.ItemsControlFromItemContainer(item) as ListView;
            int index = listView.ItemContainerGenerator.IndexFromContainer(item) + 1;
            return index.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PathStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            if (Properties.Settings.Default.differExecuatableGame && !(bool)value)
            {
                return "#808080";
            }
            else
            {
                return "#FFFFFF";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class StatusStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            switch ((ProcStat)value)
            {
                case ProcStat.NotExist:
                    return "#808080";
                case ProcStat.Rest:
                    return "#f5f5f5";
                case ProcStat.Focused:
                    return "#05d3ff";
                case ProcStat.Unfocused:
                    return "#0e6c8f";
                default:
                    return "#f5f5f5";
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class StatusBarStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            if ((StatusBarStatus)value == StatusBarStatus.watching)
            {
                return "#1585b5";
            }
            else if ((StatusBarStatus)value == StatusBarStatus.resting)
            {
                return "#d27e05";
            }
            else
            {
                return "#252525";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    #endregion
}
