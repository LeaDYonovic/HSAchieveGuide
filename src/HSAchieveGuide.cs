using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: AssemblyVersion("0.0.0.0")]
internal static class Program
{
	private static void ExtractEmbeddedResources()
	{
		var asm = Assembly.GetExecutingAssembly();
		var baseDir = (AppDomain.CurrentDomain.BaseDirectory ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var jsonDir = Path.Combine(baseDir, "json");
		var mvDir = Path.Combine(jsonDir, "mindvision-export");
		// Embedded files to extract (resource name -> destination path)
		var map = new Dictionary<string, string>
		{
			["EmbedJson__hs_achievement_data"] = Path.Combine(jsonDir, "hs-achievement-data.json"),
			["EmbedJson__guide_table"] = Path.Combine(jsonDir, "guide-table.json"),
		};
		// Derived files to split out from hs-achievement-data.json
		var splitMap = new Dictionary<string, string>
		{
			["categories"]       = Path.Combine(mvDir, "mindvision-official-categories.json"),
			["type_map"]         = Path.Combine(mvDir, "mindvision-achievement-category-config.json"),
			["achievements"]     = Path.Combine(mvDir, "mindvision-achievement-reference.json"),
			["dual_class_map"]   = Path.Combine(jsonDir, "dual-class-achievement-map.json"),
			["achievement_cards"]= Path.Combine(jsonDir, "achievement-related-cards.json"),
		};
		var errors = new System.Text.StringBuilder();
		foreach (var entry in map)
		{
			try
			{
				if (File.Exists(entry.Value)) continue;
				using (var stream = asm.GetManifestResourceStream(entry.Key))
				{
					if (stream == null)
					{
						errors.AppendLine("Missing embedded resource: " + entry.Key);
						continue;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(entry.Value));
					using (var fs = File.Create(entry.Value))
						stream.CopyTo(fs);
				}
			}
			catch (Exception ex)
			{
				errors.AppendLine("Failed to extract " + entry.Key + ": " + ex.Message);
			}
		}
		// Split hs-achievement-data.json into individual files consumed by existing load logic
		try
		{
			var mergedPath = Path.Combine(jsonDir, "hs-achievement-data.json");
			if (File.Exists(mergedPath))
			{
				bool allSplitExist = true;
				foreach (var kv in splitMap) if (!File.Exists(kv.Value)) { allSplitExist = false; break; }
				if (!allSplitExist)
				{
					var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
					var root = ser.DeserializeObject(File.ReadAllText(mergedPath)) as System.Collections.Generic.Dictionary<string, object>;
					if (root != null)
					{
						foreach (var kv in splitMap)
						{
							if (File.Exists(kv.Value)) continue;
							if (!root.ContainsKey(kv.Key)) continue;
							Directory.CreateDirectory(Path.GetDirectoryName(kv.Value));
							File.WriteAllText(kv.Value, ser.Serialize(root[kv.Key]), System.Text.Encoding.UTF8);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			errors.AppendLine("Failed to split hs-achievement-data.json: " + ex.Message);
		}
		if (errors.Length > 0)
		{
			var logPath = Path.Combine(baseDir, "HSAchieveGuide.error.log");
			File.AppendAllText(logPath, "[ExtractEmbeddedResources " + DateTime.Now + "]\r\n" + errors + "\r\n");
		}
	}

	private static void WriteGlobalError(string source, Exception ex)
	{
		try
		{
			string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSAchieveGuide.error.log");
			string fallback = Path.Combine(Path.GetTempPath(), "HSAchieveGuide.error.log");
			string content = "[" + DateTime.Now + "] " + source + "\r\nType: " + ex.GetType().FullName + "\r\nMessage: " + ex.Message + "\r\nStack: " + ex.StackTrace + "\r\n\r\n";
			try { File.AppendAllText(logPath, content); } catch { File.AppendAllText(fallback, content); }
		}
		catch { }
	}

	[STAThread]
	private static void Main(string[] args)
	{
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			if (ex != null) WriteGlobalError("UnhandledException", ex);
		};
		Application.ThreadException += (s, e) =>
		{
			WriteGlobalError("ThreadException", e.Exception);
			MessageBox.Show("发生错误:\r\n" + e.Exception.Message + "\r\n\r\n日志: " + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSAchieveGuide.error.log"), "炉石成就攻略", MessageBoxButtons.OK, MessageBoxIcon.Error);
		};
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		try
		{
			ExtractEmbeddedResources();
			if (TryRunHeadlessCommand(args))
			{
				return;
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			Application.Run(new FirestoneDataViewer(args));
		}
		catch (Exception ex)
		{
			try
			{
				string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSAchieveGuide.error.log");
				List<string> list = new List<string>();
				list.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
				list.Add("Type: " + (ex.GetType().FullName ?? "-"));
				list.Add("Message: " + SafeString(ex.Message));
				list.Add("StackTrace: " + SafeString(ex.StackTrace));
				List<string> list2 = list;
				Exception innerException = ex.InnerException;
				int num = 0;
				while (innerException != null && num < 8)
				{
					list2.Add("InnerType: " + (innerException.GetType().FullName ?? "-"));
					list2.Add("InnerMessage: " + SafeString(innerException.Message));
					list2.Add("InnerStackTrace: " + SafeString(innerException.StackTrace));
					innerException = innerException.InnerException;
					num++;
				}
				File.AppendAllText(path, string.Join(Environment.NewLine, list2) + Environment.NewLine + Environment.NewLine);
			}
			catch
			{
			}
			MessageBox.Show("查看器启动失败。\r\n请把 tools\\HSAchieveGuide.error.log 发给我。", "炉石成就攻略", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private static string SafeString(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "-" : value;
	}

	private static bool TryRunHeadlessCommand(string[] args)
	{
		if (args == null || args.Length == 0)
		{
			return false;
		}
		string text = args[0] ?? string.Empty;
		if (!string.Equals(text, "--export-all-json", StringComparison.OrdinalIgnoreCase) && !string.Equals(text, "/export-all-json", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string outputPath = (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])) ? args[1] : null;
		string[] array = ((args.Length > 2 && !string.IsNullOrWhiteSpace(args[2])) ? new string[1] { args[2] } : Array.Empty<string>());
		using (FirestoneDataViewer firestoneDataViewer = new FirestoneDataViewer(array))
		{
			string text2 = firestoneDataViewer.ExportAllSourceJsonBundle(outputPath);
			Console.WriteLine("Exported: " + text2);
		}
		return true;
	}
}
internal sealed class FirestoneDataViewer : Form
{
	private static readonly Color ThemeCanvas = Color.FromArgb(246, 248, 251);

	private static readonly Color ThemeSurface = Color.White;

	private static readonly Color ThemeSurfaceMuted = Color.FromArgb(243, 246, 250);

	private static readonly Color ThemeSurfaceTint = Color.FromArgb(250, 252, 255);

	private static readonly Color ThemeBorder = Color.FromArgb(214, 221, 232);

	private static readonly Color ThemeAccent = Color.FromArgb(36, 99, 235);

	private static readonly Color ThemeAccentSoft = Color.FromArgb(232, 240, 255);

	private static readonly Color ThemeAccentSoftHover = Color.FromArgb(220, 232, 255);

	private static readonly Color ThemeAccentPressed = Color.FromArgb(205, 223, 255);

	private static readonly Color ThemeTextStrong = Color.FromArgb(30, 41, 59);

	private static readonly Color ThemeTextMuted = Color.FromArgb(87, 96, 112);

	private static readonly Color ThemeGridLine = Color.FromArgb(227, 232, 240);

	private static readonly Color ThemeGridAlternate = Color.FromArgb(248, 250, 252);

	private static readonly Color ThemeSelection = Color.FromArgb(221, 234, 255);

	private static readonly Color ThemeSelectionText = Color.FromArgb(28, 45, 84);

	private static Icon _applicationIcon;

	private static readonly Regex AchievementNotificationRegex = new Regex("(?<time>\\d{2}:\\d{2}:\\d{2}\\.\\d+)\\s+OnAchievementNotification:\\s+Achievement=\\[Achievement:\\s+ID=(?<id>\\d+)\\s+Type=(?<type>\\S+)\\s+Name='(?<name>.*?)'\\s+MaxProgress=(?<max>\\d+)\\s+Progress=(?<progress>\\d+)\\s+AckProgress=(?<ack>\\d+)\\s+IsActive=(?<active>\\w+)\\s+DateGiven=(?<given>\\d+)\\s+DateCompleted=(?<completed>\\d+)\\s+Description='(?<description>.*?)'\\s+Trigger=(?<trigger>\\S+)\\s+CanAck=(?<canAck>\\w+)\\]", RegexOptions.Compiled | RegexOptions.Singleline);

	private const string DefaultExtensionId = "lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob";

	private const int CollectionPageSize = 10;

	private const int DetailPageSize = 10;

	private const string GuideAuthorName = "活着就好";

	private const string GuideAuthorHomepageUrl = "https://www.iyingdi.com/tz/people/11839081";

	private const string GuideSupportQqGroup = "562175526";

	private const string RememberedFirestoneDirFileName = "firestone-data-dir.txt";

	private const string OfficialCalibrationFolderName = "official-calibration";

	private const string OfficialCalibrationSourceFolderName = "CurrentAchievementCalibration_zh";

	private enum AppDialogTone
	{
		Info,
		Warning,
		Error,
		Success,
		Question
	}

	private string _firestoneDir;

	private string _firestoneDirSourceLabel;

	private string _collectionPath;

	private string _completedPath;

	private string _profilePath;

	private readonly string _hearthstoneLogsRoot;

	private readonly string _mindVisionExportDir;

	private string _trackedAchievementsPath;

	private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer
	{
		MaxJsonLength = int.MaxValue,
		RecursionLimit = 256
	};

	private static string StartupTraceLogPath
	{
		get
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSAchieveGuide.startup.log");
		}
	}

	private const int StartupTraceLogMaxBytes = 262144;

	private const int StartupTraceLogKeepChars = 131072;

	private List<OwnedCollectionRow> _allCollectionRows = new List<OwnedCollectionRow>();

	private List<OwnedCollectionRow> _ownedCollectionRows = new List<OwnedCollectionRow>();

	private List<OwnedCollectionRow> _skinCollectionRows = new List<OwnedCollectionRow>();

	private List<CompletedAchievementRow> _completedAchievementRows = new List<CompletedAchievementRow>();

	private List<AchievementProgressRow> _achievementProgressRows = new List<AchievementProgressRow>();

	private string _achievementProgressCacheSignature;

	private int _achievementProgressCacheLogCount = -1;

	private List<AchievementProgressRow> _achievementProgressCache = new List<AchievementProgressRow>();

	private List<ProfileAchievementSummary> _profileRows = new List<ProfileAchievementSummary>();

	private List<AchievementCategoryViewRow> _achievementCategoryRows = new List<AchievementCategoryViewRow>();

	private List<AchievementCategoryViewRow> _ladderClassAchievementCategoryRows = new List<AchievementCategoryViewRow>();

	private List<OfficialCategoryExportRow> _officialCategoryExportRows = new List<OfficialCategoryExportRow>();

	private Dictionary<string, OfficialCategoryPathInfo> _officialTypePathMap = new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, List<AchievementRelatedCardReference>> _achievementRelatedCardLookup = new Dictionary<string, List<AchievementRelatedCardReference>>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, List<string>> _dualClassAchievementLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private List<AchievementGuideRow> _achievementGuideRows = new List<AchievementGuideRow>();

	private Dictionary<string, List<AchievementGuideRow>> _achievementGuideLookupByName = new Dictionary<string, List<AchievementGuideRow>>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, List<AchievementGuideRow>> _achievementGuideMatchCache = new Dictionary<string, List<AchievementGuideRow>>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, TrackedAchievementEntry> _trackedAchievementLookup = new Dictionary<string, TrackedAchievementEntry>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, OwnedCollectionRow> _collectionLookupById = new Dictionary<string, OwnedCollectionRow>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, List<OwnedCollectionRow>> _collectionLookupByName = new Dictionary<string, List<OwnedCollectionRow>>(StringComparer.OrdinalIgnoreCase);

	private string _achievementRelatedCardMapPath = "未找到关联卡牌表";

	private string _dualClassAchievementMapPath = "未找到双职业映射表";

	private string _achievementGuideDataPath = "未找到攻略总表";

	private string _metadataPath = "-";

	private MindVisionExportRefreshResult _mindVisionExportRefreshResult = MindVisionExportRefreshResult.CreateInitial();

	private bool _initialLoadStarted;

	private int _logFileCount;

	private int _collectionPageIndex;

	private int _skinPageIndex;

	private static readonly AchievementClassRule[] AchievementClassDetectionRules = new AchievementClassRule[11]
	{
		new AchievementClassRule("死亡骑士", "死亡骑士", "death knight", "deathknight", "dk "),
		new AchievementClassRule("恶魔猎手", "恶魔猎手", "demon hunter", "demonhunter"),
		new AchievementClassRule("德鲁伊", "德鲁伊", "druid"),
		new AchievementClassRule("猎人", "猎人", "hunter"),
		new AchievementClassRule("法师", "法师", "mage"),
		new AchievementClassRule("圣骑士", "圣骑士", "paladin"),
		new AchievementClassRule("牧师", "牧师", "priest"),
		new AchievementClassRule("潜行者", "潜行者", "rogue"),
		new AchievementClassRule("萨满祭司", "萨满", "萨满祭司", "shaman"),
		new AchievementClassRule("术士", "术士", "warlock"),
		new AchievementClassRule("战士", "战士", "warrior")
	};

	private Label HeaderLabel { get; set; }

	private Label LoadingStatusLabel { get; set; }

	private TabControl Tabs { get; set; }

	private FlowLayoutPanel PageButtonPanel { get; set; }

	private FlowLayoutPanel ActionButtonPanel { get; set; }

	private Button SelectDirectoryButton { get; set; }

	private Button RefreshButton { get; set; }

	private readonly Dictionary<string, Button> _pageSwitchButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

	private TextBox CollectionSearchBox { get; set; }

	private ComboBox ClassFilterBox { get; set; }

	private ComboBox CostFilterBox { get; set; }

	private ComboBox TypeFilterBox { get; set; }

	private ComboBox SetFilterBox { get; set; }

	private ComboBox CollectionOwnershipFilterBox { get; set; }

	private ComboBox CollectionRarityFilterBox { get; set; }

	private ComboBox CollectionPremiumFilterBox { get; set; }

	private Label CollectionSummaryLabel { get; set; }

	private Label CollectionPagerLabel { get; set; }

	private DataGridView CollectionGrid { get; set; }

	private Button CollectionPrevButton { get; set; }

	private Button CollectionNextButton { get; set; }

	private TextBox CollectionDetailsBox { get; set; }

	private TextBox SkinSearchBox { get; set; }

	private ComboBox SkinClassFilterBox { get; set; }

	private ComboBox SkinOwnershipFilterBox { get; set; }

	private ComboBox SkinRarityFilterBox { get; set; }

	private Label SkinSummaryLabel { get; set; }

	private Label SkinPagerLabel { get; set; }

	private DataGridView SkinGrid { get; set; }

	private Button SkinPrevButton { get; set; }

	private Button SkinNextButton { get; set; }

	private Label CompletedSummaryLabel { get; set; }

	private DataGridView CompletedGrid { get; set; }

	private TextBox CompletedDetailsBox { get; set; }

	private Label ProgressSummaryLabel { get; set; }

	private DataGridView ProgressGrid { get; set; }

	private Label TrackedSummaryLabel { get; set; }

	private DataGridView TrackedGrid { get; set; }

	private TextBox TrackedSearchBox { get; set; }

	private Button TrackedOpenGuideButton { get; set; }

	private Button TrackedRemoveButton { get; set; }

	private TextBox ProgressDetailsBox { get; set; }

	private ComboBox ProgressCompletionFilterBox { get; set; }

	private ComboBox ProfileCompletionFilterBox { get; set; }

	private Button ProfileOpenDetailButton { get; set; }

	private Label ProfileSummaryLabel { get; set; }

	private DataGridView ProfileGrid { get; set; }

	private TextBox ProfileDetailsBox { get; set; }

	private ComboBox LadderClassCompletionFilterBox { get; set; }

	private Button LadderClassOpenDetailButton { get; set; }

	private Label LadderClassSummaryLabel { get; set; }

	private DataGridView LadderClassGrid { get; set; }

	private Label LadderClassDetailsSummaryLabel { get; set; }

	private DataGridView LadderClassDetailsGrid { get; set; }

	public FirestoneDataViewer(string[] args)
	{
		_firestoneDir = ResolveFirestoneDirectory(args, out _firestoneDirSourceLabel, allowPrompt: false);
		UpdateFirestoneFilePaths();
		_hearthstoneLogsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Hearthstone", "Logs");
		_mindVisionExportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "json", "mindvision-export");
		LoadTrackedAchievements();
		InitializeComponent();
		UpdateHeader("-");
		Tabs.TabPages.Clear();
		Tabs.TabPages.Add(BuildLoadingPage("准备加载数据..."));
	}

	private void InitializeComponent()
	{
		this.Text = "炉石成就攻略";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		base.Size = new System.Drawing.Size(1080, 1024);
		this.MinimumSize = new System.Drawing.Size(1020, 860);
		base.BackColor = ThemeCanvas;
		base.Font = new Font("Microsoft YaHei UI", 9f);
		base.Icon = GetApplicationIcon();
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		UpdateStyles();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 2,
			Padding = new Padding(14, 14, 14, 12),
			BackColor = ThemeCanvas
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 6,
			RowCount = 1,
			Margin = new Padding(0),
			Padding = new Padding(0),
			BackColor = ThemeCanvas
		};
		for (int i = 0; i < 6; i++)
		{
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666667f));
		}
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		this.PageButtonPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = false,
			Margin = new Padding(0),
			Padding = new Padding(10, 8, 2, 8),
			Dock = DockStyle.Fill,
			BackColor = ThemeSurface
		};
		foreach (string item in new string[4] { "官方成就分类", "我追踪的成就", "我的卡牌收藏", "使用说明" })
		{
			Button button = CreatePageSwitchButton(item);
			_pageSwitchButtons[item] = button;
			PageButtonPanel.Controls.Add(button);
		}
		this.HeaderLabel = new System.Windows.Forms.Label
		{
			Dock = System.Windows.Forms.DockStyle.Fill,
			Padding = new System.Windows.Forms.Padding(2, 2, 2, 0),
			Font = new System.Drawing.Font("Microsoft YaHei UI", 8.75f),
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 8, 0, 0),
			ForeColor = ThemeTextMuted,
			BackColor = ThemeCanvas,
			Height = 34
		};
		this.ActionButtonPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = false,
			Margin = new Padding(0),
			Padding = new Padding(10, 8, 10, 8),
			Dock = DockStyle.Fill,
			BackColor = ThemeSurface
		};
		this.SelectDirectoryButton = new Button
		{
			Width = 110,
			Height = 60,
			Text = "选择目录",
			Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
			Margin = new Padding(0, 0, 8, 0)
		};
		this.SelectDirectoryButton.Click += async delegate
		{
			await PromptForFirestoneDirectoryAndReloadAsync();
		};
		ApplySecondaryButtonStyle(this.SelectDirectoryButton);
		this.RefreshButton = new Button
		{
			Width = 118,
			Height = 60,
			Text = "刷新数据",
			Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
			Margin = new Padding(0)
		};
		this.RefreshButton.Click += async delegate
		{
			if (!HasMinimumFirestoneDataAvailable())
			{
				await PromptForFirestoneDirectoryAndReloadAsync();
				return;
			}
		if (!ShowAppConfirm(this, "炉石成就攻略", "确认刷新数据？\r\n这会重新读取本地数据并更新当前界面。"))
			{
				return;
			}
			await ReloadDataSafeAsync();
		};
		ApplyPrimaryButtonStyle(this.RefreshButton);
		this.ActionButtonPanel.Controls.Add(this.SelectDirectoryButton);
		this.ActionButtonPanel.Controls.Add(this.RefreshButton);
		this.Tabs = new System.Windows.Forms.TabControl
		{
			Dock = System.Windows.Forms.DockStyle.Fill,
			Font = new System.Drawing.Font("Microsoft YaHei UI", 9f),
			Appearance = TabAppearance.FlatButtons,
			SizeMode = TabSizeMode.Fixed,
			ItemSize = new Size(1, 1),
			Multiline = true,
			Padding = new Point(0, 0),
			BackColor = ThemeCanvas
		};
		this.Tabs.SelectedIndexChanged += delegate
		{
			UpdatePageSwitchButtonStates();
		};
		base.Shown += delegate
		{
			if (_initialLoadStarted)
			{
				return;
			}
			_initialLoadStarted = true;
			BeginInvoke((Action)StartInitialLoadAfterShown);
		};
		List<Button> list = new List<Button>(6);
		foreach (string item2 in new string[4] { "官方成就分类", "我追踪的成就", "我的卡牌收藏", "使用说明" })
		{
			list.Add(_pageSwitchButtons[item2]);
		}
		list.Add(this.SelectDirectoryButton);
		list.Add(this.RefreshButton);
		for (int j = 0; j < list.Count; j++)
		{
			Button button2 = list[j];
			button2.Dock = DockStyle.Fill;
			button2.Margin = new Padding(0, 0, j < list.Count - 1 ? 10 : 0, 0);
			tableLayoutPanel2.Controls.Add(button2, j, 0);
		}
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(this.HeaderLabel, 0, 1);
		base.Controls.Add(this.Tabs);
		base.Controls.Add(tableLayoutPanel);
		UpdatePageSwitchButtonStates();
	}

	private async void StartInitialLoadAfterShown()
	{
		try
		{
			await Task.Delay(150);
			WriteStartupTrace("initial-shown: firestoneDir=" + SafeTraceText(_firestoneDir) + ", source=" + SafeTraceText(_firestoneDirSourceLabel));
			if (!HasMinimumFirestoneDataAvailable())
			{
				UpdateLoadingStatus("未找到可用的 Firestone 数据目录，请点击右上角“选择目录”。");
				WriteStartupTrace("initial-shown: skip auto load because Firestone directory is unavailable");
				return;
			}
			await ReloadDataSafeAsync(preserveSelectedTab: false, isInitialLoad: true);
		}
		catch (Exception ex)
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSAchieveGuide.error.log");
			try
			{
				File.AppendAllText(path, "[" + DateTime.Now + "] Initial load failed\r\nMessage: " + ex.Message + "\r\nStack: " + ex.StackTrace + "\r\n\r\n");
			}
			catch
			{
			}
			ShowAppError(this, "炉石成就攻略", "初始化失败:\r\n" + ex.Message + "\r\n\r\n详情已写入:\r\n" + path);
		}
	}

	private bool HasMinimumFirestoneDataAvailable()
	{
		DateTime latestWriteTime;
		return GetFirestoneDirectoryScore(_firestoneDir, out latestWriteTime) > 0;
	}

	private async Task PromptForFirestoneDirectoryAndReloadAsync()
	{
		WriteStartupTrace("prompt-directory: begin");
		string result;
		if (!TryPromptForFirestoneDirectory(GetDefaultOverwolfRoot(), this, out result))
		{
			WriteStartupTrace("prompt-directory: cancelled");
			return;
		}
		_firestoneDir = result;
		_firestoneDirSourceLabel = "手动选择";
		RememberFirestoneDirectory(result);
		UpdateFirestoneFilePaths();
		LoadTrackedAchievements();
		UpdateHeader("-");
		UpdateLoadingStatus("目录已更新，准备加载数据...");
		WriteStartupTrace("prompt-directory: selected=" + SafeTraceText(result));
		await ReloadDataSafeAsync();
	}

	private Button CreatePageSwitchButton(string tabText)
	{
		Button button = new Button
		{
			Text = tabText,
			AutoSize = false,
			Height = 64,
			Padding = new Padding(8, 0, 8, 0),
			Margin = new Padding(0),
			Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
			FlatStyle = FlatStyle.Flat,
			UseVisualStyleBackColor = false,
			BackColor = ThemeSurfaceTint,
			ForeColor = ThemeTextMuted
		};
		button.FlatAppearance.BorderColor = ThemeBorder;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = ThemeAccentSoft;
		button.FlatAppearance.MouseDownBackColor = ThemeAccentPressed;
		button.Cursor = Cursors.Hand;
		button.Click += delegate
		{
			SelectTopLevelPage(tabText);
		};
		return button;
	}

	private void SelectTopLevelPage(string tabText)
	{
		if (Tabs == null || string.IsNullOrWhiteSpace(tabText))
		{
			return;
		}
		TabPage tabPage = Tabs.TabPages.Cast<TabPage>().FirstOrDefault((TabPage page) => string.Equals(page.Text, tabText, StringComparison.OrdinalIgnoreCase));
		if (tabPage != null)
		{
			Tabs.SelectedTab = tabPage;
		}
	}

	private void UpdatePageSwitchButtonStates()
	{
		string text = Tabs?.SelectedTab?.Text ?? string.Empty;
		foreach (KeyValuePair<string, Button> pageSwitchButton in _pageSwitchButtons)
		{
			Button value = pageSwitchButton.Value;
			if (value == null)
			{
				continue;
			}
			bool flag = string.Equals(pageSwitchButton.Key, text, StringComparison.OrdinalIgnoreCase);
			value.BackColor = (flag ? ThemeAccent : ThemeSurfaceTint);
			value.ForeColor = (flag ? Color.White : ThemeTextMuted);
			value.FlatAppearance.BorderColor = (flag ? ThemeAccent : ThemeBorder);
			value.FlatAppearance.MouseOverBackColor = (flag ? ThemeAccent : ThemeAccentSoft);
			value.FlatAppearance.MouseDownBackColor = (flag ? ThemeAccent : ThemeAccentPressed);
		}
	}

	private void UpdateFirestoneFilePaths()
	{
		string path = _firestoneDir ?? string.Empty;
		_collectionPath = Path.Combine(path, "collection.json");
		_completedPath = Path.Combine(path, "achievements-completed.json");
		_profilePath = Path.Combine(path, "profile-achievements.json");
		_trackedAchievementsPath = Path.Combine(path, "tracked-achievements.json");
	}

	private void EnsureFirestoneDirectoryResolvedInteractive()
	{
		DateTime latestWriteTime;
		if (GetFirestoneDirectoryScore(_firestoneDir, out latestWriteTime) > 0)
		{
			return;
		}
		if (!Environment.UserInteractive)
		{
			return;
		}
		string result;
		if (!TryPromptForFirestoneDirectory(GetDefaultOverwolfRoot(), this, out result))
		{
			return;
		}
		_firestoneDir = result;
		_firestoneDirSourceLabel = "手动选择";
		RememberFirestoneDirectory(result);
		UpdateFirestoneFilePaths();
		LoadTrackedAchievements();
	}

	private static string ResolveFirestoneDirectory(string[] args, out string sourceLabel, bool allowPrompt = true)
	{
		string text = GetExplicitFirestoneDirectoryArgument(args);
		string result;
		if (TryResolveFirestoneDirectoryCandidate(text, out result))
		{
			RememberFirestoneDirectory(result);
			sourceLabel = "启动参数";
			return result;
		}
		string text2 = LoadRememberedFirestoneDirectory();
		if (TryResolveFirestoneDirectoryCandidate(text2, out result))
		{
			sourceLabel = "上次记住";
			return result;
		}
		string defaultFirestoneDirectory = GetDefaultFirestoneDirectory();
		if (TryResolveFirestoneDirectoryCandidate(defaultFirestoneDirectory, out result))
		{
			RememberFirestoneDirectory(result);
			sourceLabel = string.Equals(result, defaultFirestoneDirectory, StringComparison.OrdinalIgnoreCase) ? "默认目录" : "自动识别";
			return result;
		}
		string defaultOverwolfRoot = GetDefaultOverwolfRoot();
		if (TryResolveFirestoneDirectoryCandidate(defaultOverwolfRoot, out result))
		{
			RememberFirestoneDirectory(result);
			sourceLabel = "自动扫描";
			return result;
		}
		if (allowPrompt && Environment.UserInteractive && TryPromptForFirestoneDirectory(defaultOverwolfRoot, null, out result))
		{
			RememberFirestoneDirectory(result);
			sourceLabel = "手动选择";
			return result;
		}
		sourceLabel = "未找到，当前使用默认路径";
		return defaultFirestoneDirectory;
	}

	private static string GetExplicitFirestoneDirectoryArgument(string[] args)
	{
		if (args == null || args.Length == 0)
		{
			return null;
		}
		string text = args[0];
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	private static string GetDefaultFirestoneDirectory()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Overwolf", DefaultExtensionId);
	}

	private static string GetDefaultOverwolfRoot()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Overwolf");
	}

	private static bool TryResolveFirestoneDirectoryCandidate(string candidate, out string resolvedPath)
	{
		resolvedPath = null;
		string text = NormalizeExistingDirectory(candidate);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		List<string> list = new List<string>();
		list.Add(text);
		list.AddRange(GetImmediateChildDirectories(text));
		string bestFirestoneDirectory = GetBestFirestoneDirectory(list);
		if (string.IsNullOrWhiteSpace(bestFirestoneDirectory))
		{
			return false;
		}
		resolvedPath = bestFirestoneDirectory;
		return true;
	}

	private static string NormalizeExistingDirectory(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
			return Directory.Exists(fullPath) ? fullPath : null;
		}
		catch
		{
			return null;
		}
	}

	private static IEnumerable<string> GetImmediateChildDirectories(string path)
	{
		string text = NormalizeExistingDirectory(path);
		if (string.IsNullOrWhiteSpace(text))
		{
			return Enumerable.Empty<string>();
		}
		try
		{
			return Directory.GetDirectories(text);
		}
		catch
		{
			return Enumerable.Empty<string>();
		}
	}

	private static string GetBestFirestoneDirectory(IEnumerable<string> candidates)
	{
		string text = null;
		int num = 0;
		DateTime dateTime = DateTime.MinValue;
		foreach (string candidate in candidates ?? Enumerable.Empty<string>())
		{
			string text2 = NormalizeExistingDirectory(candidate);
			if (string.IsNullOrWhiteSpace(text2))
			{
				continue;
			}
			DateTime latestWriteTime;
			int firestoneDirectoryScore = GetFirestoneDirectoryScore(text2, out latestWriteTime);
			if (firestoneDirectoryScore <= 0)
			{
				continue;
			}
			bool flag = string.Equals(Path.GetFileName(text2), DefaultExtensionId, StringComparison.OrdinalIgnoreCase);
			bool flag2 = !string.IsNullOrWhiteSpace(text) && string.Equals(Path.GetFileName(text), DefaultExtensionId, StringComparison.OrdinalIgnoreCase);
			if (firestoneDirectoryScore > num || (firestoneDirectoryScore == num && flag && !flag2) || (firestoneDirectoryScore == num && flag == flag2 && latestWriteTime > dateTime))
			{
				text = text2;
				num = firestoneDirectoryScore;
				dateTime = latestWriteTime;
			}
		}
		return text;
	}

	private static int GetFirestoneDirectoryScore(string path, out DateTime latestWriteTime)
	{
		latestWriteTime = DateTime.MinValue;
		if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
		{
			return 0;
		}
		bool flag = false;
		int num = 0;
		num += ScoreFirestoneFile(path, "collection.json", 5, ref latestWriteTime, ref flag);
		num += ScoreFirestoneFile(path, "achievements-completed.json", 4, ref latestWriteTime);
		num += ScoreFirestoneFile(path, "profile-achievements.json", 4, ref latestWriteTime);
		num += ScoreFirestoneFile(path, "tracked-achievements.json", 2, ref latestWriteTime);
		num += ScoreFirestonePattern(path, "cards_*.json", 3, ref latestWriteTime, ref flag);
		num += ScoreFirestonePattern(path, "localization-*.json", 1, ref latestWriteTime, ref flag);
		if (!flag)
		{
			return 0;
		}
		return num;
	}

	private static int ScoreFirestoneFile(string directoryPath, string fileName, int score, ref DateTime latestWriteTime)
	{
		bool hasCollectionFile = false;
		return ScoreFirestoneFile(directoryPath, fileName, score, ref latestWriteTime, ref hasCollectionFile);
	}

	private static int ScoreFirestoneFile(string directoryPath, string fileName, int score, ref DateTime latestWriteTime, ref bool existsFlag)
	{
		string path = Path.Combine(directoryPath, fileName);
		if (!File.Exists(path))
		{
			return 0;
		}
		existsFlag = true;
		try
		{
			DateTime lastWriteTime = File.GetLastWriteTime(path);
			if (lastWriteTime > latestWriteTime)
			{
				latestWriteTime = lastWriteTime;
			}
		}
		catch
		{
		}
		return score;
	}

	private static int ScoreFirestonePattern(string directoryPath, string searchPattern, int score, ref DateTime latestWriteTime, ref bool existsFlag)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(searchPattern) || !Directory.Exists(directoryPath))
		{
			return 0;
		}
		try
		{
			string[] files = Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);
			if (files == null || files.Length == 0)
			{
				return 0;
			}
			existsFlag = true;
			foreach (string text in files)
			{
				try
				{
					DateTime lastWriteTime = File.GetLastWriteTime(text);
					if (lastWriteTime > latestWriteTime)
					{
						latestWriteTime = lastWriteTime;
					}
				}
				catch
				{
				}
			}
			return score;
		}
		catch
		{
			return 0;
		}
	}

	private static bool HasFirestoneBaseResourceFiles(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
		{
			return false;
		}
		try
		{
			return Directory.GetFiles(path, "cards_*.json", SearchOption.TopDirectoryOnly).Length != 0 || Directory.GetFiles(path, "localization-*.json", SearchOption.TopDirectoryOnly).Length != 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryPromptForFirestoneDirectory(string initialPath, IWin32Window owner, out string selectedPath)
	{
		selectedPath = null;
		using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
		{
			folderBrowserDialog.Description = "请选择 Firestone 本地数据目录。\r\n如果你只知道 Roaming\\\\Overwolf，也可以直接选上一级目录，软件会继续自动识别。";
			folderBrowserDialog.ShowNewFolderButton = false;
			if (Directory.Exists(initialPath))
			{
				folderBrowserDialog.SelectedPath = initialPath;
			}
			while ((owner != null) ? (folderBrowserDialog.ShowDialog(owner) == DialogResult.OK) : (folderBrowserDialog.ShowDialog() == DialogResult.OK))
			{
				if (TryResolveFirestoneDirectoryCandidate(folderBrowserDialog.SelectedPath, out selectedPath))
				{
					return true;
				}
			ShowAppWarning(null, "炉石成就攻略", "这个目录里没有找到可用的 Firestone 数据文件。\r\n\r\n请重新选择包含 collection.json、cards_*.json 或 localization-*.json 的目录。");
			}
		}
		return false;
	}

	private static string GetRememberedFirestoneDirectoryPath()
	{
		string text = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = AppDomain.CurrentDomain.BaseDirectory;
		}
		return Path.Combine(text, "HSAchieveGuide", RememberedFirestoneDirFileName);
	}

	private static string LoadRememberedFirestoneDirectory()
	{
		try
		{
			string rememberedFirestoneDirectoryPath = GetRememberedFirestoneDirectoryPath();
			if (!File.Exists(rememberedFirestoneDirectoryPath))
			{
				return null;
			}
			return NullIfWhiteSpace(File.ReadAllText(rememberedFirestoneDirectoryPath));
		}
		catch
		{
			return null;
		}
	}

	private static void RememberFirestoneDirectory(string path)
	{
		string text = NormalizeExistingDirectory(path);
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		try
		{
			string rememberedFirestoneDirectoryPath = GetRememberedFirestoneDirectoryPath();
			string directoryName = Path.GetDirectoryName(rememberedFirestoneDirectoryPath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(rememberedFirestoneDirectoryPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	public string ExportAllSourceJsonBundle(string outputPath)
	{
		MindVisionExportRefreshResult mindVisionExportRefreshResult = TryRefreshMindVisionExport();
		if (mindVisionExportRefreshResult != null)
		{
			_mindVisionExportRefreshResult = mindVisionExportRefreshResult;
		}
		List<DirectJsonBundleSource> list = BuildDirectJsonBundleSources();
		Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, object> dictionary2 = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		List<object> list2 = new List<object>();
		List<object> list3 = new List<object>();
		foreach (DirectJsonBundleSource item in list)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.Key))
			{
				continue;
			}
			if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
			{
				list2.Add(new
				{
					key = item.Key,
					label = item.Label,
					path = item.Path
				});
				continue;
			}
			try
			{
				dictionary[item.Key] = new
				{
					label = item.Label,
					file_name = Path.GetFileName(item.Path),
					path = item.Path,
					last_write_time = File.GetLastWriteTime(item.Path).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
				};
				dictionary2[item.Key] = ReadRawJsonFile(item.Path);
			}
			catch (Exception ex)
			{
				list3.Add(new
				{
					key = item.Key,
					label = item.Label,
					path = item.Path,
					error = ex.Message
				});
			}
		}
		if (string.IsNullOrWhiteSpace(outputPath))
		{
			outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "json", "firestone-all-data.json");
		}
		string directoryName = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		object obj = new
		{
			generated_at = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
			firestone_dir = _firestoneDir,
			source_file_count = dictionary2.Count,
			source_files = dictionary,
			missing_files = list2,
			read_errors = list3,
			json = dictionary2
		};
		string contents = _serializer.Serialize(obj);
		File.WriteAllText(outputPath, PrettyPrintJson(contents), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		return outputPath;
	}

	private List<DirectJsonBundleSource> BuildDirectJsonBundleSources()
	{
		List<DirectJsonBundleSource> list = new List<DirectJsonBundleSource>();
		AddDirectJsonBundleSource(list, "collection", "collection.json", _collectionPath);
		AddDirectJsonBundleSource(list, "achievements_completed", "achievements-completed.json", _completedPath);
		AddDirectJsonBundleSource(list, "profile_achievements", "profile-achievements.json", _profilePath);
		AddDirectJsonBundleSource(list, "tracked_achievements", "tracked-achievements.json", _trackedAchievementsPath);
		AddDirectJsonBundleSource(list, "card_metadata", "cards metadata", FindMetadataPath());
		AddDirectJsonBundleSource(list, "achievement_related_card_map", "achievement related card map", FindAchievementRelatedCardMapPath());
		AddDirectJsonBundleSource(list, "dual_class_achievement_map", "dual-class-achievement-map.json", FindDualClassAchievementMapPath());
		AddDirectJsonBundleSource(list, "achievement_total_table", "achievement_total_table.json", FindAchievementGuideDataPath());
		Dictionary<string, string> dictionary = GetMindVisionExportOutputPaths().Where(File.Exists).GroupBy((string path) => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).Select((IGrouping<string, string> group) => group.OrderByDescending(File.GetLastWriteTime).First()).ToDictionary((string path) => Path.GetFileName(path), (string path) => path, StringComparer.OrdinalIgnoreCase);
		foreach (string item in new string[6] { "mindvision-summary.json", "mindvision-official-categories.json", "mindvision-achievement-category-config.json", "mindvision-achievement-reference.json", "mindvision-achievements.json", "mindvision-achievement-categories.json" })
		{
			dictionary.TryGetValue(item, out var value);
			AddDirectJsonBundleSource(list, BuildJsonBundleKey(item), item, value);
		}
		return list.GroupBy((DirectJsonBundleSource item) => item.Key, StringComparer.OrdinalIgnoreCase).Select((IGrouping<string, DirectJsonBundleSource> group) => group.First()).ToList();
	}

	private static void AddDirectJsonBundleSource(List<DirectJsonBundleSource> sources, string key, string label, string path)
	{
		if (sources == null || string.IsNullOrWhiteSpace(key))
		{
			return;
		}
		sources.Add(new DirectJsonBundleSource
		{
			Key = key,
			Label = label,
			Path = path
		});
	}

	private static string BuildJsonBundleKey(string fileName)
	{
		string text = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
		if (string.IsNullOrWhiteSpace(text))
		{
			return "unknown_json";
		}
		return Regex.Replace(text, "[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();
	}

	private object ReadRawJsonFile(string path)
	{
		string input = File.ReadAllText(path);
		return _serializer.DeserializeObject(input);
	}

	private static string PrettyPrintJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return json ?? string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(json.Length + Math.Max(1024, json.Length / 8));
		bool flag = false;
		bool flag2 = false;
		int num = 0;
		for (int i = 0; i < json.Length; i++)
		{
			char c = json[i];
			if (flag2)
			{
				stringBuilder.Append(c);
				flag2 = false;
				continue;
			}
			if (c == '\\')
			{
				stringBuilder.Append(c);
				if (flag)
				{
					flag2 = true;
				}
				continue;
			}
			if (c == '"')
			{
				flag = !flag;
				stringBuilder.Append(c);
				continue;
			}
			if (flag)
			{
				stringBuilder.Append(c);
				continue;
			}
			switch (c)
			{
			case '{':
			case '[':
				stringBuilder.Append(c);
				stringBuilder.AppendLine();
				num++;
				AppendJsonIndent(stringBuilder, num);
				break;
			case '}':
			case ']':
				stringBuilder.AppendLine();
				num = Math.Max(0, num - 1);
				AppendJsonIndent(stringBuilder, num);
				stringBuilder.Append(c);
				break;
			case ',':
				stringBuilder.Append(c);
				stringBuilder.AppendLine();
				AppendJsonIndent(stringBuilder, num);
				break;
			case ':':
				stringBuilder.Append(": ");
				break;
			default:
				if (!char.IsWhiteSpace(c))
				{
					stringBuilder.Append(c);
				}
				break;
			}
		}
		return stringBuilder.ToString();
	}

	private static void AppendJsonIndent(StringBuilder builder, int indentLevel)
	{
		for (int i = 0; i < indentLevel; i++)
		{
			builder.Append("  ");
		}
	}

	private string GetLocalJsonRoot()
	{
		return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "json");
	}

	private FirestoneLoadedData LoadDataSnapshot(Action<string> progress = null)
	{
		WriteStartupTrace("snapshot: begin");
		progress?.Invoke("刷新运行时导出...");
		MindVisionExportRefreshResult mindVisionExportRefreshResult = TryRefreshMindVisionExport(progress);
		WriteStartupTrace("snapshot: export-status=" + SafeTraceText(mindVisionExportRefreshResult?.StatusLabel));
		progress?.Invoke("加载卡牌元数据...");
		string metadataPath;
		IReadOnlyDictionary<string, CardMetadataRow> metadata = LoadCardMetadata(out metadataPath);
		WriteStartupTrace("snapshot: metadata=" + SafeTraceText(metadataPath));
		progress?.Invoke("读取账号收藏...");
		List<CollectionCard> source = LoadJson<List<CollectionCard>>(_collectionPath) ?? new List<CollectionCard>();
		WriteStartupTrace("snapshot: collection-count=" + source.Count.ToString(CultureInfo.InvariantCulture));
		Dictionary<string, CollectionCard> collectionById = source.Where((CollectionCard card) => card != null && !string.IsNullOrWhiteSpace(card.Id)).GroupBy((CollectionCard card) => card.Id, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, CollectionCard> group) => group.Key, (IGrouping<string, CollectionCard> group) => group.First(), StringComparer.OrdinalIgnoreCase);
		progress?.Invoke("读取 Firestone 成就缓存...");
		CompletedAchievementsFile completedAchievementsFile = LoadJson<CompletedAchievementsFile>(_completedPath) ?? new CompletedAchievementsFile();
		List<ProfileAchievementSummary> profileRows = (from p in LoadJson<List<ProfileAchievementSummary>>(_profilePath) ?? new List<ProfileAchievementSummary>()
			where p != null
			orderby p.Id
			select p).ToList();
		WriteStartupTrace("snapshot: profile-count=" + profileRows.Count.ToString(CultureInfo.InvariantCulture));
		progress?.Invoke("解析官方进度明细...");
		int logFileCount;
		List<AchievementProgressRow> achievementProgressRows = LoadAchievementProgressRows(out logFileCount);
		WriteStartupTrace("snapshot: progress-count=" + achievementProgressRows.Count.ToString(CultureInfo.InvariantCulture) + ", logFiles=" + logFileCount.ToString(CultureInfo.InvariantCulture));
		progress?.Invoke("同步官方结构基准...");
		TryRefreshOfficialCalibrationCache();
		progress?.Invoke("加载官方分类导出...");
		List<OfficialCategoryExportRow> officialCategoryExportRows = LoadOfficialCategoryExports();
		Dictionary<string, OfficialCategoryPathInfo> officialTypePathMap = LoadOfficialTypePaths();
		WriteStartupTrace("snapshot: official-categories=" + officialCategoryExportRows.Count.ToString(CultureInfo.InvariantCulture));
		EnrichOfficialAchievementPaths(officialCategoryExportRows, officialTypePathMap);
		if (officialCategoryExportRows.Count > 0)
		{
			List<ProfileAchievementSummary> liveRows = officialCategoryExportRows
				.Where(r => r.RuntimeStats?.Stats != null)
				.Select(r => new ProfileAchievementSummary
				{
					Id = r.Id,
					AvailablePoints = r.RuntimeStats.Stats.AvailablePoints,
					Points = r.RuntimeStats.Stats.Points,
					TotalAchievements = r.RuntimeStats.Stats.TotalAchievements,
					CompletedAchievements = r.RuntimeStats.Stats.CompletedAchievements
				})
				.ToList();
			if (liveRows.Count > 0)
			{
				profileRows = liveRows;
			}
		}
		progress?.Invoke("整理收藏与皮肤列表...");
		List<string> source2 = metadata.Keys.Union(collectionById.Keys, StringComparer.OrdinalIgnoreCase).ToList();
		List<OwnedCollectionRow> allCollectionRows = (from cardId in source2
			select CreateOwnedCollectionRow(cardId, metadata, collectionById) into row
			where IsKnownCollectionCard(row)
			where !IsPlaceholderCollectionCard(row)
			orderby row.ClassSort, row.CostSort, row.TypeSort, row.Name, row.Id
			select row).ToList();
		List<OwnedCollectionRow> ownedCollectionRows = allCollectionRows.Where(IsDisplayableCollectionCard).ToList();
		List<OwnedCollectionRow> skinCollectionRows = allCollectionRows.Where(IsSkinCollectionCard).ToList();
		Dictionary<string, OwnedCollectionRow> collectionLookupById;
		Dictionary<string, List<OwnedCollectionRow>> collectionLookupByName;
		BuildCollectionLookups(allCollectionRows, out collectionLookupById, out collectionLookupByName);
		WriteStartupTrace("snapshot: all-collection-rows=" + allCollectionRows.Count.ToString(CultureInfo.InvariantCulture));
		progress?.Invoke("加载关联卡牌表...");
		string achievementRelatedCardMapPath;
		Dictionary<string, List<AchievementRelatedCardReference>> achievementRelatedCardLookup = LoadAchievementRelatedCardLookup(out achievementRelatedCardMapPath);
		WriteStartupTrace("snapshot: related-cards-path=" + SafeTraceText(achievementRelatedCardMapPath));
		progress?.Invoke("加载双职业映射表...");
		string dualClassAchievementMapPath;
		Dictionary<string, List<string>> dualClassAchievementLookup = LoadDualClassAchievementLookup(out dualClassAchievementMapPath);
		WriteStartupTrace("snapshot: dual-class-path=" + SafeTraceText(dualClassAchievementMapPath));
		progress?.Invoke("加载成就攻略表...");
		string achievementGuideDataPath;
		List<AchievementGuideRow> achievementGuideRows;
		Dictionary<string, List<AchievementGuideRow>> achievementGuideLookupByName;
		LoadAchievementGuideLookup(out achievementGuideRows, out achievementGuideLookupByName, out achievementGuideDataPath);
		WriteStartupTrace("snapshot: guide-path=" + SafeTraceText(achievementGuideDataPath) + ", guide-count=" + achievementGuideRows.Count.ToString(CultureInfo.InvariantCulture));
		List<CompletedAchievementRow> completedAchievementRows = (from item in completedAchievementsFile.Achievements ?? new List<CompletedAchievement>()
			where item != null
			select new CompletedAchievementRow
			{
				Id = (item.Id ?? string.Empty),
				Category = GuessAchievementCategory(item.Id),
				NumberOfCompletions = item.NumberOfCompletions
			} into row
			orderby row.Category, row.Id
			select row).ToList();
		progress?.Invoke("整理界面数据...");
		WriteStartupTrace("snapshot: end");
		return new FirestoneLoadedData
		{
			CompletedUpdateDate = completedAchievementsFile.LastUpdateDate,
			MetadataPath = metadataPath,
			LogFileCount = logFileCount,
			MindVisionExportRefreshResult = mindVisionExportRefreshResult,
			AllCollectionRows = allCollectionRows,
			OwnedCollectionRows = ownedCollectionRows,
			SkinCollectionRows = skinCollectionRows,
			CompletedAchievementRows = completedAchievementRows,
			AchievementProgressRows = achievementProgressRows,
			ProfileRows = profileRows,
			OfficialCategoryExportRows = officialCategoryExportRows,
			OfficialTypePathMap = officialTypePathMap,
			AchievementRelatedCardLookup = achievementRelatedCardLookup,
			AchievementRelatedCardMapPath = achievementRelatedCardMapPath,
			DualClassAchievementLookup = dualClassAchievementLookup,
			DualClassAchievementMapPath = dualClassAchievementMapPath,
			AchievementGuideRows = achievementGuideRows,
			AchievementGuideLookupByName = achievementGuideLookupByName,
			AchievementGuideDataPath = achievementGuideDataPath,
			CollectionLookupById = collectionLookupById,
			CollectionLookupByName = collectionLookupByName
		};
	}

	private async Task ReloadDataSafeAsync(bool preserveSelectedTab = true, bool isInitialLoad = false)
	{
		string text = preserveSelectedTab ? Tabs?.SelectedTab?.Text : null;
		Exception loadException = null;
		try
		{
			WriteStartupTrace("reload: begin, initial=" + isInitialLoad.ToString(CultureInfo.InvariantCulture) + ", selectedTab=" + SafeTraceText(text));
			SetLoadingUiState(isLoading: true, isInitialLoad);
			UpdateLoadingStatus(isInitialLoad ? "准备加载数据..." : "准备刷新数据...");
			IProgress<string> progress = new Progress<string>(delegate(string message)
			{
				WriteStartupTrace("progress: " + SafeTraceText(message));
				UpdateLoadingStatus(message);
			});
			FirestoneLoadedData firestoneLoadedData = await Task.Run(() => LoadDataSnapshot(progress.Report));
			ApplyLoadedData(firestoneLoadedData, text);
			ShowMindVisionExportRefreshWarningIfNeeded();
			WriteStartupTrace("reload: success");
		}
		catch (Exception ex)
		{
			WriteStartupTrace("reload: failed: " + SafeTraceText(ex.Message));
			loadException = ex;
		}
		finally
		{
			UpdateLoadingStatus(null);
			SetLoadingUiState(isLoading: false, isInitialLoad);
		}
		if (loadException != null)
		{
			string text2 = isInitialLoad ? "加载数据失败" : "刷新数据失败";
			string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSAchieveGuide.error.log");
			try { File.AppendAllText(logPath, "[" + DateTime.Now + "] " + text2 + "\r\nMessage: " + loadException.Message + "\r\nStack: " + loadException.StackTrace + "\r\n\r\n"); } catch { }
				ShowAppError(this, "炉石成就攻略", text2 + ":\r\n" + loadException.Message + "\r\n\r\n详情已写入:\r\n" + logPath);
		}
	}

	private void SetLoadingUiState(bool isLoading, bool isInitialLoad)
	{
		if (RefreshButton != null)
		{
			RefreshButton.Enabled = !isLoading;
			RefreshButton.Text = isLoading ? (isInitialLoad ? "加载中..." : "刷新中...") : "刷新数据";
		}
		if (Tabs != null)
		{
			Tabs.Enabled = !isLoading;
		}
		foreach (Button value in _pageSwitchButtons.Values)
		{
			if (value != null)
			{
				value.Enabled = !isLoading;
			}
		}
		UseWaitCursor = isLoading;
	}

	private void ApplyLoadedData(FirestoneLoadedData loadedData, string selectedTabText)
	{
		if (loadedData == null)
		{
			throw new ArgumentNullException("loadedData");
		}
		_allCollectionRows = loadedData.AllCollectionRows ?? new List<OwnedCollectionRow>();
		_ownedCollectionRows = loadedData.OwnedCollectionRows ?? new List<OwnedCollectionRow>();
		_skinCollectionRows = loadedData.SkinCollectionRows ?? new List<OwnedCollectionRow>();
		_completedAchievementRows = loadedData.CompletedAchievementRows ?? new List<CompletedAchievementRow>();
		_achievementProgressRows = loadedData.AchievementProgressRows ?? new List<AchievementProgressRow>();
		_profileRows = loadedData.ProfileRows ?? new List<ProfileAchievementSummary>();
		_officialCategoryExportRows = loadedData.OfficialCategoryExportRows ?? new List<OfficialCategoryExportRow>();
		_officialTypePathMap = loadedData.OfficialTypePathMap ?? new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
		_achievementRelatedCardLookup = loadedData.AchievementRelatedCardLookup ?? new Dictionary<string, List<AchievementRelatedCardReference>>(StringComparer.OrdinalIgnoreCase);
		_dualClassAchievementLookup = loadedData.DualClassAchievementLookup ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		_achievementGuideRows = loadedData.AchievementGuideRows ?? new List<AchievementGuideRow>();
		_achievementGuideLookupByName = loadedData.AchievementGuideLookupByName ?? new Dictionary<string, List<AchievementGuideRow>>(StringComparer.OrdinalIgnoreCase);
		_achievementGuideMatchCache = new Dictionary<string, List<AchievementGuideRow>>(StringComparer.OrdinalIgnoreCase);
		_collectionLookupById = loadedData.CollectionLookupById ?? new Dictionary<string, OwnedCollectionRow>(StringComparer.OrdinalIgnoreCase);
		_collectionLookupByName = loadedData.CollectionLookupByName ?? new Dictionary<string, List<OwnedCollectionRow>>(StringComparer.OrdinalIgnoreCase);
		_achievementRelatedCardMapPath = loadedData.AchievementRelatedCardMapPath ?? "未找到关联卡牌表";
		_dualClassAchievementMapPath = loadedData.DualClassAchievementMapPath ?? "未找到双职业映射表";
		_achievementGuideDataPath = loadedData.AchievementGuideDataPath ?? "未找到攻略总表";
		_metadataPath = loadedData.MetadataPath ?? "-";
		_logFileCount = loadedData.LogFileCount;
		UpdateMindVisionExportRefreshState(loadedData.MindVisionExportRefreshResult);
		Tabs.TabPages.Clear();
		Tabs.TabPages.Add(BuildAchievementCategoriesPage());
		Tabs.TabPages.Add(BuildTrackedAchievementsPage());
		Tabs.TabPages.Add(BuildMultiGuideManagementPage());
		Tabs.TabPages.Add(BuildCollectionPage());
		Tabs.TabPages.Add(BuildUsageGuidePage());
		PopulateFilterOptions();
		RefreshCollectionGrid();
		RefreshTrackedAchievementsGrid();
		RefreshProfileGrid();
		RefreshLadderClassGrid();
		UpdateHeader(loadedData.CompletedUpdateDate);
		if (!string.IsNullOrWhiteSpace(selectedTabText))
		{
			TabPage tabPage = Tabs.TabPages.Cast<TabPage>().FirstOrDefault((TabPage page) => string.Equals(page.Text, selectedTabText, StringComparison.OrdinalIgnoreCase));
			if (tabPage != null)
			{
				Tabs.SelectedTab = tabPage;
			}
		}
		UpdatePageSwitchButtonStates();
	}

	private TabPage BuildLoadingPage(string message)
	{
		TabPage tabPage = new TabPage("加载中");
		ApplyTabPageTheme(tabPage);
		LoadingStatusLabel = new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = new Font("Microsoft YaHei UI", 12f),
			Text = string.IsNullOrWhiteSpace(message) ? "正在加载数据..." : message,
			ForeColor = ThemeTextMuted,
			BackColor = ThemeCanvas
		};
		tabPage.Controls.Add(LoadingStatusLabel);
		return tabPage;
	}

	private void UpdateHeader(string completedUpdateDate)
	{
		bool flag = File.Exists(_collectionPath);
		bool flag2 = File.Exists(_completedPath);
		bool flag3 = File.Exists(_profilePath);
		bool flag4 = HasFirestoneBaseResourceFiles(_firestoneDir);
		string text = (!flag) ? (flag4 ? "收藏: 待生成" : "收藏: 缺失") : ("收藏: " + FileTimeText(_collectionPath));
		List<string> list = new List<string>();
		if (!flag2)
		{
			list.Add("已完成");
		}
		if (!flag3)
		{
			list.Add("分类");
		}
		string text2 = (!flag4) ? "状态: 未识别到可用的 Firestone 目录" : (flag ? "状态: 可正常运行" : "状态: 可运行（等待 Firestone 生成收藏缓存）");
		if (list.Count > 0 && flag4)
		{
			text2 = text2 + "  可选缺失: " + string.Join("/", list);
		}
		HeaderLabel.Text = "目录 " + CompactPathText(_firestoneDir, 42) + "   ·   定位 " + _firestoneDirSourceLabel + "   ·   " + text2 + "   ·   " + text + "   ·   导出 " + SafeText(_mindVisionExportRefreshResult.StatusLabel);
	}

	private static void ApplyTabPageTheme(TabPage tabPage)
	{
		if (tabPage == null)
		{
			return;
		}
		tabPage.BackColor = ThemeCanvas;
		tabPage.Padding = new Padding(14, 10, 14, 14);
	}

	private static void ApplyPrimaryButtonStyle(Button button)
	{
		if (button == null)
		{
			return;
		}
		if (button.Font == null || Math.Abs(button.Font.SizeInPoints - 8.25f) < 0.2f)
		{
			button.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
		}
		button.FlatStyle = FlatStyle.Flat;
		button.UseVisualStyleBackColor = false;
		button.BackColor = ThemeAccent;
		button.ForeColor = Color.White;
		button.Cursor = Cursors.Hand;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.BorderColor = ThemeAccent;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 64, 175);
	}

	private static void ApplySecondaryButtonStyle(Button button)
	{
		if (button == null)
		{
			return;
		}
		if (button.Font == null || Math.Abs(button.Font.SizeInPoints - 8.25f) < 0.2f)
		{
			button.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
		}
		button.FlatStyle = FlatStyle.Flat;
		button.UseVisualStyleBackColor = false;
		button.BackColor = ThemeSurfaceTint;
		button.ForeColor = ThemeTextStrong;
		button.Cursor = Cursors.Hand;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.BorderColor = ThemeBorder;
		button.FlatAppearance.MouseOverBackColor = ThemeSurfaceMuted;
		button.FlatAppearance.MouseDownBackColor = ThemeAccentSoft;
	}

	private static CheckBox CreateToggleFilterChip(string text)
	{
		CheckBox checkBox = new CheckBox
		{
			Appearance = Appearance.Button,
			AutoSize = false,
			Width = 138,
			Height = 28,
			Text = text,
			TextAlign = ContentAlignment.MiddleCenter,
			FlatStyle = FlatStyle.Flat,
			Margin = new Padding(12, 2, 0, 0),
			BackColor = ThemeSurfaceTint,
			ForeColor = ThemeTextStrong,
			Cursor = Cursors.Hand
		};
		checkBox.FlatAppearance.BorderSize = 1;
		checkBox.FlatAppearance.BorderColor = ThemeBorder;
		checkBox.FlatAppearance.CheckedBackColor = ThemeAccentSoft;
		checkBox.FlatAppearance.MouseOverBackColor = ThemeSurfaceMuted;
		checkBox.FlatAppearance.MouseDownBackColor = ThemeAccentSoft;
		checkBox.CheckedChanged += delegate
		{
			checkBox.BackColor = (checkBox.Checked ? ThemeAccentSoft : ThemeSurfaceTint);
			checkBox.ForeColor = (checkBox.Checked ? ThemeAccent : ThemeTextStrong);
			checkBox.FlatAppearance.BorderColor = (checkBox.Checked ? ThemeAccent : ThemeBorder);
		};
		return checkBox;
	}

	private static Panel CreateCardHost(Control content, Padding margin, Padding padding)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = margin,
			Padding = new Padding(1),
			BackColor = ThemeBorder
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = padding,
			BackColor = ThemeSurface
		};
		if (content != null)
		{
			content.Dock = DockStyle.Fill;
			content.Margin = new Padding(0);
			panel2.Controls.Add(content);
		}
		panel.Controls.Add(panel2);
		return panel;
	}

	private static Panel CreateAutoSizeCardHost(Control content, Padding margin, Padding padding)
	{
		Panel panel = new Panel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.Fill,
			Margin = margin,
			Padding = new Padding(1),
			BackColor = ThemeBorder
		};
		Panel panel2 = new Panel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = DockStyle.None,
			Padding = padding,
			BackColor = ThemeSurface
		};
		if (content != null)
		{
			content.Dock = DockStyle.None;
			content.Margin = new Padding(0);
			panel2.Controls.Add(content);
		}
		panel.Controls.Add(panel2);
		return panel;
	}

	private static TableLayoutPanel CreateSurfaceLayout(params RowStyle[] rowStyles)
	{
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(rowStyles);
		tableLayoutPanel.BackColor = ThemeSurface;
		tableLayoutPanel.Margin = new Padding(0);
		tableLayoutPanel.Padding = new Padding(0);
		return tableLayoutPanel;
	}

	private static FlowLayoutPanel CreateFilterBarPanel(bool wrapContents)
	{
		FlowLayoutPanel flowLayoutPanel = CreateHorizontalFlowPanel(new Padding(0), wrapContents);
		flowLayoutPanel.BackColor = ThemeSurface;
		flowLayoutPanel.Margin = new Padding(0);
		return flowLayoutPanel;
	}

	private static Label CreateCardTitleLabel(string text)
	{
		return new Label
		{
			AutoSize = true,
			Text = text,
			Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
			ForeColor = ThemeTextStrong,
			Padding = new Padding(0, 0, 0, 10),
			Margin = new Padding(0)
		};
	}

	private static void ApplyTextInputStyle(TextBox textBox)
	{
		if (textBox == null)
		{
			return;
		}
		textBox.BorderStyle = BorderStyle.FixedSingle;
		textBox.Font = new Font("Microsoft YaHei UI", 9f);
		textBox.Margin = new Padding(0, 3, 10, 0);
		textBox.BackColor = ThemeSurface;
		textBox.ForeColor = ThemeTextStrong;
	}

	private static void ApplyComboBoxStyle(ComboBox comboBox)
	{
		if (comboBox == null)
		{
			return;
		}
		comboBox.FlatStyle = FlatStyle.Flat;
		comboBox.Font = new Font("Microsoft YaHei UI", 9f);
		comboBox.BackColor = ThemeSurface;
		comboBox.ForeColor = ThemeTextStrong;
	}

	private static void ApplyListBoxStyle(ListBox listBox)
	{
		if (listBox == null)
		{
			return;
		}
		listBox.BorderStyle = BorderStyle.None;
		listBox.BackColor = ThemeSurface;
		listBox.ForeColor = ThemeTextStrong;
		listBox.Font = new Font("Microsoft YaHei UI", 9f);
	}

	private static void ApplyRadioButtonStyle(RadioButton radioButton)
	{
		if (radioButton == null)
		{
			return;
		}
		radioButton.BackColor = ThemeSurface;
		radioButton.ForeColor = ThemeTextStrong;
		radioButton.Font = new Font("Microsoft YaHei UI", 9f);
		radioButton.Margin = new Padding(0, 4, 14, 0);
	}

	private static void ApplyDialogFormStyle(Form form)
	{
		if (form == null)
		{
			return;
		}
		form.BackColor = ThemeCanvas;
		form.Font = new Font("Microsoft YaHei UI", 9f);
		form.Padding = new Padding(14);
		form.Icon = GetApplicationIcon();
	}

	private static Icon GetApplicationIcon()
	{
		if (_applicationIcon == null)
		{
			try
			{
				_applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			}
			catch
			{
				_applicationIcon = null;
			}
		}
		return _applicationIcon;
	}

	private static Control CreateTitledCard(string title, Control content, Padding margin, Padding padding)
	{
		TableLayoutPanel tableLayoutPanel = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(CreateCardTitleLabel(title), 0, 0);
		tableLayoutPanel.Controls.Add(content, 0, 1);
		return CreateCardHost(tableLayoutPanel, margin, padding);
	}

	private static Label CreateDialogSubtitleLabel(string text)
	{
		return new Label
		{
			AutoSize = true,
			Text = text,
			ForeColor = ThemeTextMuted,
			Font = new Font("Microsoft YaHei UI", 9f),
			Padding = new Padding(0, 2, 0, 0),
			Margin = new Padding(0)
		};
	}

	private static Control CreateDialogShell(string title, string subtitle, Control body, Control toolbar = null, bool wrapBodyInCard = true)
	{
		List<RowStyle> list = new List<RowStyle>
		{
			new RowStyle(SizeType.AutoSize)
		};
		if (toolbar != null)
		{
			list.Add(new RowStyle(SizeType.AutoSize));
		}
		list.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(list.ToArray());
		tableLayoutPanel.Padding = new Padding(0);
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize));
		Label label = new Label
		{
			AutoSize = true,
			Text = title,
			Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
			ForeColor = ThemeTextStrong,
			Margin = new Padding(0)
		};
		tableLayoutPanel2.Controls.Add(label, 0, 0);
		if (!string.IsNullOrWhiteSpace(subtitle))
		{
			tableLayoutPanel2.Controls.Add(CreateDialogSubtitleLabel(subtitle), 0, 1);
		}
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 12), new Padding(18, 16, 18, 16)), 0, 0);
		int num = 1;
		if (toolbar != null)
		{
			tableLayoutPanel.Controls.Add(CreateCardHost(toolbar, new Padding(0, 0, 0, 12), new Padding(14, 12, 14, 12)), 0, num++);
		}
		if (wrapBodyInCard)
		{
			tableLayoutPanel.Controls.Add(CreateCardHost(body, new Padding(0), new Padding(16, 16, 16, 16)), 0, num);
		}
		else
		{
			body.Dock = DockStyle.Fill;
			tableLayoutPanel.Controls.Add(body, 0, num);
		}
		return tableLayoutPanel;
	}

	private static void ApplyDialogControlTheme(Control root)
	{
		if (root == null)
		{
			return;
		}
		switch (root)
		{
		case Button button:
			ApplySecondaryButtonStyle(button);
			break;
		case TextBox textBox:
			if (textBox.ReadOnly)
			{
				textBox.BackColor = ThemeSurface;
				textBox.ForeColor = ThemeTextStrong;
				textBox.BorderStyle = (textBox.BorderStyle == BorderStyle.None) ? BorderStyle.None : BorderStyle.FixedSingle;
				textBox.Font = new Font("Microsoft YaHei UI", 9f);
			}
			else
			{
				ApplyTextInputStyle(textBox);
			}
			break;
		case ComboBox comboBox:
			ApplyComboBoxStyle(comboBox);
			break;
		case ListBox listBox:
			ApplyListBoxStyle(listBox);
			break;
		case RadioButton radioButton:
			ApplyRadioButtonStyle(radioButton);
			break;
		case LinkLabel linkLabel:
			linkLabel.LinkColor = ThemeAccent;
			linkLabel.ActiveLinkColor = Color.FromArgb(29, 78, 216);
			linkLabel.VisitedLinkColor = ThemeAccent;
			linkLabel.BackColor = ThemeSurface;
			break;
		case GroupBox groupBox:
			groupBox.BackColor = ThemeSurface;
			groupBox.ForeColor = ThemeTextStrong;
			groupBox.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
			groupBox.Padding = new Padding(12, 28, 12, 12);
			break;
		case TabControl tabControl:
			tabControl.BackColor = ThemeCanvas;
			tabControl.Padding = new Point(16, 8);
			break;
		case TabPage tabPage:
			ApplyTabPageTheme(tabPage);
			break;
		case SplitContainer splitContainer:
			splitContainer.BackColor = ThemeCanvas;
			splitContainer.Panel1.BackColor = ThemeCanvas;
			splitContainer.Panel2.BackColor = ThemeCanvas;
			break;
		case FlowLayoutPanel flowLayoutPanel:
			if (flowLayoutPanel.BackColor == SystemColors.Control || flowLayoutPanel.BackColor == default(Color))
			{
				flowLayoutPanel.BackColor = ThemeCanvas;
			}
			break;
		case TableLayoutPanel tableLayoutPanel:
			if (tableLayoutPanel.BackColor == SystemColors.Control || tableLayoutPanel.BackColor == default(Color))
			{
				tableLayoutPanel.BackColor = ThemeCanvas;
			}
			break;
		case Panel panel:
			if (panel.BackColor == SystemColors.Control || panel.BackColor == default(Color))
			{
				panel.BackColor = ThemeCanvas;
			}
			break;
		case Label label:
			if (label.ForeColor == SystemColors.ControlText)
			{
				label.ForeColor = ThemeTextStrong;
			}
			if (label.BackColor == SystemColors.Control || label.BackColor == default(Color))
			{
				label.BackColor = Color.Transparent;
			}
			break;
		}
		foreach (Control control in root.Controls)
		{
			ApplyDialogControlTheme(control);
		}
	}

	private static Color GetDialogToneColor(AppDialogTone tone)
	{
		switch (tone)
		{
		case AppDialogTone.Warning:
			return Color.FromArgb(217, 119, 6);
		case AppDialogTone.Error:
			return Color.FromArgb(220, 38, 38);
		case AppDialogTone.Success:
			return Color.FromArgb(22, 163, 74);
		case AppDialogTone.Question:
			return ThemeAccent;
		default:
			return ThemeAccent;
		}
	}

	private static string GetDialogToneLabel(AppDialogTone tone)
	{
		switch (tone)
		{
		case AppDialogTone.Warning:
			return "注意";
		case AppDialogTone.Error:
			return "错误";
		case AppDialogTone.Success:
			return "完成";
		case AppDialogTone.Question:
			return "确认";
		default:
			return "提示";
		}
	}

	private static DialogResult ShowAppDialog(IWin32Window owner, string title, string message, AppDialogTone tone, bool showCancel = false, string primaryButtonText = "确定")
	{
		using Form form = new Form();
		form.Text = string.IsNullOrWhiteSpace(title) ? "炉石成就攻略" : title;
		form.StartPosition = (owner != null) ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
		form.FormBorderStyle = FormBorderStyle.FixedDialog;
		form.MinimizeBox = false;
		form.MaximizeBox = false;
		form.ShowInTaskbar = false;
		form.Size = new Size(560, 340);
		form.MinimumSize = new Size(520, 300);
		ApplyDialogFormStyle(form);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f), new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Padding = new Padding(0);
		Color dialogToneColor = GetDialogToneColor(tone);
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize));
		FlowLayoutPanel flowLayoutPanel = CreateHorizontalFlowPanel(new Padding(0), wrapContents: false);
		flowLayoutPanel.BackColor = ThemeSurface;
		Panel panel = new Panel
		{
			Width = 10,
			Height = 10,
			BackColor = dialogToneColor,
			Margin = new Padding(0, 8, 10, 0)
		};
		Label label = new Label
		{
			AutoSize = true,
			Text = GetDialogToneLabel(tone),
			ForeColor = dialogToneColor,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
			Margin = new Padding(0, 0, 10, 0)
		};
		Label label2 = new Label
		{
			AutoSize = true,
			Text = form.Text,
			ForeColor = ThemeTextStrong,
			Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
			Margin = new Padding(0)
		};
		flowLayoutPanel.Controls.Add(panel);
		flowLayoutPanel.Controls.Add(label);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel2.Controls.Add(label2, 0, 1);
		TextBox textBox = CreateReadOnlyDetailsBox();
		textBox.BorderStyle = BorderStyle.None;
		textBox.Text = message ?? string.Empty;
		textBox.BackColor = ThemeSurface;
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 12), new Padding(18, 16, 18, 16)), 0, 0);
		tableLayoutPanel.Controls.Add(CreateCardHost(textBox, new Padding(0, 0, 0, 12), new Padding(18, 16, 18, 16)), 0, 1);
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			BackColor = ThemeCanvas
		};
		Button button = new Button
		{
			AutoSize = true,
			Text = primaryButtonText,
			DialogResult = DialogResult.OK,
			Margin = new Padding(10, 0, 0, 0)
		};
		ApplyPrimaryButtonStyle(button);
		flowLayoutPanel2.Controls.Add(button);
		form.AcceptButton = button;
		if (showCancel)
		{
			Button button2 = new Button
			{
				AutoSize = true,
				Text = "取消",
				DialogResult = DialogResult.Cancel
			};
			ApplySecondaryButtonStyle(button2);
			flowLayoutPanel2.Controls.Add(button2);
			form.CancelButton = button2;
		}
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 0, 2);
		form.Controls.Add(tableLayoutPanel);
		return (owner != null) ? form.ShowDialog(owner) : form.ShowDialog();
	}

	private static void ShowAppInfo(IWin32Window owner, string title, string message)
	{
		ShowAppDialog(owner, title, message, AppDialogTone.Info);
	}

	private static void ShowAppWarning(IWin32Window owner, string title, string message)
	{
		ShowAppDialog(owner, title, message, AppDialogTone.Warning);
	}

	private static void ShowAppError(IWin32Window owner, string title, string message)
	{
		ShowAppDialog(owner, title, message, AppDialogTone.Error);
	}

	private static bool ShowAppConfirm(IWin32Window owner, string title, string message, string primaryButtonText = "确定")
	{
		return ShowAppDialog(owner, title, message, AppDialogTone.Question, showCancel: true, primaryButtonText) == DialogResult.OK;
	}

	private void ResetCollectionFiltersAndRefresh()
	{
		if (CollectionSearchBox != null)
		{
			CollectionSearchBox.Text = string.Empty;
		}
		SetComboBoxToFirstItem(ClassFilterBox);
		SetComboBoxToFirstItem(CostFilterBox);
		SetComboBoxToFirstItem(TypeFilterBox);
		SetComboBoxToFirstItem(CollectionOwnershipFilterBox);
		SetComboBoxToFirstItem(CollectionRarityFilterBox);
		SetComboBoxToFirstItem(CollectionPremiumFilterBox);
		SetComboBoxToFirstItem(SetFilterBox);
		ResetCollectionPagingAndRefresh();
	}

	private static void SetComboBoxToFirstItem(ComboBox comboBox)
	{
		if (comboBox != null && comboBox.Items.Count > 0)
		{
			comboBox.SelectedIndex = 0;
		}
	}

	private TabPage BuildUsageGuidePage()
	{
		TabPage tabPage = new TabPage("使用说明");
		ApplyTabPageTheme(tabPage);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.Percent, 62f), new RowStyle(SizeType.Percent, 38f));
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		TextBox textBox = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Dock = DockStyle.Fill,
			Font = new Font("Microsoft YaHei UI", 10f),
			BackColor = ThemeSurface,
			BorderStyle = BorderStyle.None,
			Text = string.Join(Environment.NewLine, new string[]
			{
				"1. 首次打开",
				"软件会自动查找 Firestone 本地数据目录。找不到或识别错误时，点击右上角“选择目录”手动指定；选择成功后会自动记住。",
				"",
				"2. 手动找到 Firestone 数据目录",
				"按 Win + R，粘贴下面的常见路径并回车：",
				@"%APPDATA%\Overwolf\lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob",
				"它通常展开为：",
				@"C:\Users\<你的 Windows 用户名>\AppData\Roaming\Overwolf\lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob",
				"回到软件点击“选择目录”，选择这个文件夹本身。目录中通常能看到 collection.json、cards_zhCN.gz.json 或 localization-zhCN.json。",
				@"注意：不要选择 %LOCALAPPDATA%\Overwolf\Extensions\...，那是 Firestone 扩展安装目录，不是本软件需要的数据目录。lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob 是 Firestone 固定的扩展 ID。",
				"",
				"3. 刷新数据",
				"点击右上角“刷新数据”，确认后软件会重新读取本地 Firestone 数据并刷新当前页面。本版本只读取本地文件，不连接攻略服务器。",
				"",
				"4. 数据缺失时也可以继续运行",
				"即使 Firestone 没有生成 achievements-completed.json、profile-achievements.json，甚至暂时没有 collection.json，软件也会先正常打开；顶部状态会提示哪些本地缓存仍在等待生成。",
				"",
				"5. 查看官方成就分类",
				"进入“官方成就分类”，先看汇总，再点“打开细分”查看具体成就列表。",
				"",
				"6. 查看具体成就与攻略",
				"细分窗口会显示进度、完成状态、要求、关联卡牌收藏情况和本地已收录攻略。攻略弹窗可复制卡组、打开原帖链接或本地攻略文件。",
				"",
				"7. 收藏成就",
				"具体成就第一列的“收藏”按钮可以把成就加入“我追踪的成就”，再次点击可取消收藏。",
				"",
				"8. 查看卡牌",
				"“我的卡牌收藏”支持按搜索、职业、费用、类型、拥有、稀有度、品质和系列筛选。",
				"",
				"9. 主动刷新成就",
				"刚在游戏里完成新成就后，先结束一局对局，回到主界面等待 2 到 5 秒，再点“刷新数据”。休闲和天梯都可以。",
				"",
				"10. 主动刷新卡牌收藏",
				"获得新卡后，先在游戏里完成一次会改变收藏数量的操作，例如开包、合成、分解或领取新卡；保持游戏开启，等待 5 到 8 秒，再点“刷新数据”。"
			})
		};
		tableLayoutPanel2.Controls.Add(CreateCardTitleLabel("使用说明"), 0, 0);
		tableLayoutPanel2.Controls.Add(textBox, 0, 1);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 12), new Padding(18, 16, 18, 18)), 0, 0);
		tableLayoutPanel.Controls.Add(BuildAcknowledgementsPanel(), 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private Control BuildAcknowledgementsPanel()
	{
		TableLayoutPanel tableLayoutPanel = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f), new RowStyle(SizeType.AutoSize));
		TextBox textBox = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Dock = DockStyle.Fill,
			Font = new Font("Microsoft YaHei UI", 10f),
			BackColor = ThemeSurface,
			BorderStyle = BorderStyle.None,
			Text = "感谢攻略作者“" + GuideAuthorName + "”长期整理和分享炉石成就攻略，为本工具中的攻略整理与细分提供了重要参考。" + Environment.NewLine + Environment.NewLine + "另感谢 QQ 群 " + GuideSupportQqGroup + " 中长期分享攻略、测试结果与讨论思路的各位作者和群友。" + Environment.NewLine + Environment.NewLine + "感谢 OpenAI 提供的技术支持。虽然在实际开发过程中也有不少让我抓狂的时刻，但它确实帮我把这个工具更快做出来了。" + Environment.NewLine + Environment.NewLine + "作者主页见下方链接。"
		};
		FlowLayoutPanel flowLayoutPanel = CreateHorizontalFlowPanel(new Padding(0), wrapContents: true);
		flowLayoutPanel.BackColor = ThemeSurface;
		LinkLabel linkLabel = new LinkLabel
		{
			AutoSize = true,
			Text = "作者主页: " + GuideAuthorHomepageUrl,
			LinkColor = ThemeAccent,
			ActiveLinkColor = Color.FromArgb(29, 78, 216),
			VisitedLinkColor = ThemeAccent
		};
		linkLabel.Links.Add("作者主页: ".Length, GuideAuthorHomepageUrl.Length, GuideAuthorHomepageUrl);
		linkLabel.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
		{
			OpenExternalUrl(Convert.ToString(e.Link.LinkData, CultureInfo.InvariantCulture));
		};
		Button button = new Button
		{
			AutoSize = true,
			Text = "打开作者主页"
		};
		ApplySecondaryButtonStyle(button);
		button.Click += delegate
		{
			OpenExternalUrl(GuideAuthorHomepageUrl);
		};
		flowLayoutPanel.Controls.Add(linkLabel);
		flowLayoutPanel.Controls.Add(button);
		tableLayoutPanel.Controls.Add(CreateCardTitleLabel("鸣谢"), 0, 0);
		tableLayoutPanel.Controls.Add(textBox, 0, 1);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 2);
		return CreateCardHost(tableLayoutPanel, new Padding(0), new Padding(18, 16, 18, 18));
	}

	private static TableLayoutPanel CreateSingleColumnLayout(params RowStyle[] rowStyles)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = ((rowStyles != null) ? rowStyles.Length : 0),
			BackColor = ThemeCanvas
		};
		foreach (RowStyle rowStyle in rowStyles ?? Array.Empty<RowStyle>())
		{
			tableLayoutPanel.RowStyles.Add(rowStyle);
		}
		return tableLayoutPanel;
	}

	private static FlowLayoutPanel CreateHorizontalFlowPanel(Padding padding, bool wrapContents)
	{
		return new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = wrapContents,
			Padding = padding,
			BackColor = ThemeCanvas
		};
	}

	private static ComboBox CreateDropDownListComboBox(int width)
	{
		return new ComboBox
		{
			Width = width,
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			Font = new Font("Microsoft YaHei UI", 12f),
			Margin = new Padding(0, 2, 10, 0)
		};
	}

	private ComboBox CreateCompletionFilterComboBox(Action refreshAction)
	{
		ComboBox comboBox = CreateDropDownListComboBox(140);
		comboBox.Items.Add("全部");
		comboBox.Items.Add("已完成");
		comboBox.Items.Add("未完成");
		comboBox.SelectedIndex = 0;
		if (refreshAction != null)
		{
			comboBox.SelectedIndexChanged += delegate
			{
				refreshAction();
			};
		}
		return comboBox;
	}

	private static DataGridView CreateReadOnlyGrid(DataGridViewAutoSizeColumnsMode autoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells)
	{
		DataGridView dataGridView = new DataGridView
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AutoSizeColumnsMode = autoSizeColumnsMode,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = false,
			BackgroundColor = ThemeSurface,
			BorderStyle = BorderStyle.FixedSingle,
			CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
			GridColor = ThemeGridLine,
			RowHeadersVisible = false,
			EnableHeadersVisualStyles = false,
			AllowUserToResizeRows = false
		};
		ApplyUnifiedGridFormat(dataGridView);
		EnableHorizontalMouseWheelScroll(dataGridView);
		return dataGridView;
	}

	private static void ApplyUnifiedGridFormat(DataGridView dataGridView)
	{
		if (dataGridView == null)
		{
			return;
		}
		dataGridView.Margin = new Padding(0);
		dataGridView.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
		dataGridView.ColumnHeadersHeight = 42;
		dataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
		dataGridView.ColumnHeadersDefaultCellStyle.BackColor = ThemeSurfaceMuted;
		dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = ThemeTextStrong;
		dataGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = ThemeSurfaceMuted;
		dataGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = ThemeTextStrong;
		dataGridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		dataGridView.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
		dataGridView.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 11.2f, FontStyle.Bold);
		dataGridView.DefaultCellStyle.BackColor = ThemeSurface;
		dataGridView.DefaultCellStyle.ForeColor = ThemeTextStrong;
		dataGridView.DefaultCellStyle.SelectionBackColor = ThemeSelection;
		dataGridView.DefaultCellStyle.SelectionForeColor = ThemeSelectionText;
		dataGridView.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10.8f);
		dataGridView.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
		dataGridView.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
		dataGridView.AlternatingRowsDefaultCellStyle.BackColor = ThemeGridAlternate;
		dataGridView.AlternatingRowsDefaultCellStyle.ForeColor = ThemeTextStrong;
		dataGridView.AlternatingRowsDefaultCellStyle.SelectionBackColor = ThemeSelection;
		dataGridView.AlternatingRowsDefaultCellStyle.SelectionForeColor = ThemeSelectionText;
		dataGridView.AlternatingRowsDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10.8f);
		dataGridView.RowTemplate.Height = 38;
	}

	private static void EnableHorizontalMouseWheelScroll(DataGridView dataGridView)
	{
		if (dataGridView == null)
		{
			return;
		}
		dataGridView.MouseWheel += delegate(object sender, MouseEventArgs e)
		{
			if (!ShouldUseMouseWheelForHorizontalScroll(dataGridView))
			{
				return;
			}
			HScrollBar gridHorizontalScrollBar = GetGridHorizontalScrollBar(dataGridView);
			if (gridHorizontalScrollBar == null)
			{
				return;
			}
			int num = Math.Max(48, 96 * Math.Abs(e.Delta) / Math.Max(SystemInformation.MouseWheelScrollDelta, 1));
			int num2 = ((e.Delta < 0) ? num : (-num));
			int num3 = Math.Max(0, gridHorizontalScrollBar.Maximum - gridHorizontalScrollBar.LargeChange + 1);
			int horizontalScrollingOffset = dataGridView.HorizontalScrollingOffset;
			int num4 = Math.Max(0, Math.Min(num3, horizontalScrollingOffset + num2));
			if (num4 != horizontalScrollingOffset)
			{
				dataGridView.HorizontalScrollingOffset = num4;
				if (e is HandledMouseEventArgs handledMouseEventArgs)
				{
					handledMouseEventArgs.Handled = true;
				}
			}
		};
	}

	private static bool ShouldUseMouseWheelForHorizontalScroll(DataGridView dataGridView)
	{
		if (dataGridView == null || dataGridView.IsDisposed)
		{
			return false;
		}
		HScrollBar gridHorizontalScrollBar = GetGridHorizontalScrollBar(dataGridView);
		if (gridHorizontalScrollBar == null || !gridHorizontalScrollBar.Visible)
		{
			return false;
		}
		VScrollBar gridVerticalScrollBar = GetGridVerticalScrollBar(dataGridView);
		return gridVerticalScrollBar == null || !gridVerticalScrollBar.Visible;
	}

	private static HScrollBar GetGridHorizontalScrollBar(DataGridView dataGridView)
	{
		if (dataGridView == null)
		{
			return null;
		}
		return dataGridView.Controls.OfType<HScrollBar>().FirstOrDefault();
	}

	private static VScrollBar GetGridVerticalScrollBar(DataGridView dataGridView)
	{
		if (dataGridView == null)
		{
			return null;
		}
		return dataGridView.Controls.OfType<VScrollBar>().FirstOrDefault();
	}

	private static void ApplyCompactOverviewGridStyle(DataGridView dataGridView)
	{
		if (dataGridView == null)
		{
			return;
		}
		ApplyUnifiedGridFormat(dataGridView);
		dataGridView.DefaultCellStyle.Padding = new Padding(6, 2, 6, 2);
		dataGridView.RowTemplate.Height = 38;
	}

	private static void ApplyFilledOverviewGridStyle(DataGridView dataGridView)
	{
		if (dataGridView == null)
		{
			return;
		}
		dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
		dataGridView.ScrollBars = ScrollBars.None;
		dataGridView.AllowUserToResizeRows = false;
		dataGridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		dataGridView.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		dataGridView.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
		dataGridView.Resize += delegate
		{
			RebalanceOverviewGridRowHeights(dataGridView);
		};
		dataGridView.DataBindingComplete += delegate
		{
			RebalanceOverviewGridRowHeights(dataGridView);
		};
	}

	private static void RebalanceOverviewGridRowHeights(DataGridView dataGridView)
	{
		if (dataGridView == null || dataGridView.IsDisposed || dataGridView.Rows.Count == 0)
		{
			return;
		}
		int num = dataGridView.Rows.Cast<DataGridViewRow>().Count((DataGridViewRow row) => row.Visible && !row.IsNewRow);
		if (num <= 0)
		{
			return;
		}
		int num2 = Math.Max(0, dataGridView.ClientSize.Height - dataGridView.ColumnHeadersHeight - 4);
		if (num2 <= 0)
		{
			return;
		}
		int num3 = Math.Max(24, Math.Min(Math.Max(30, dataGridView.RowTemplate.Height), num2 / num));
		foreach (DataGridViewRow row2 in dataGridView.Rows)
		{
			if (row2.Visible && !row2.IsNewRow)
			{
				row2.Height = num3;
			}
		}
	}

	private static void SetGridColumnFillWeight(DataGridView dataGridView, string columnName, float fillWeight)
	{
		if (dataGridView != null && dataGridView.Columns.Contains(columnName))
		{
			dataGridView.Columns[columnName].FillWeight = fillWeight;
		}
	}

	private static Label CreateSummaryLabel(int height, Padding padding)
	{
		return new Label
		{
			Dock = DockStyle.Fill,
			Height = height,
			Padding = padding,
			ForeColor = ThemeTextMuted,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
			BackColor = ThemeSurface,
			TextAlign = ContentAlignment.MiddleLeft
		};
	}

	private static Label CreateInlineLabel(string text, Padding padding)
	{
		return new Label
		{
			AutoSize = true,
			Padding = padding,
			Text = text,
			ForeColor = ThemeTextMuted,
			BackColor = ThemeSurface,
			Font = new Font("Microsoft YaHei UI", 12f)
		};
	}

	private static Control CreateInfoBubble(string title, Label valueLabel, int maxValueWidth = 320)
	{
		if (valueLabel == null)
		{
			valueLabel = new Label();
		}
		valueLabel.AutoSize = true;
		valueLabel.MaximumSize = new Size(maxValueWidth, 0);
		valueLabel.Margin = new Padding(0);
		valueLabel.BackColor = ThemeAccentSoft;
		valueLabel.ForeColor = ThemeTextStrong;
		valueLabel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
		Label label = new Label
		{
			AutoSize = true,
			Text = title,
			ForeColor = ThemeAccent,
			BackColor = ThemeAccentSoft,
			Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold),
			Margin = new Padding(0),
			Padding = new Padding(0, 0, 0, 4)
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 2,
			Margin = new Padding(0),
			Padding = new Padding(0),
			BackColor = ThemeAccentSoft
		};
		tableLayoutPanel.Controls.Add(label, 0, 0);
		tableLayoutPanel.Controls.Add(valueLabel, 0, 1);
		Panel panel = new Panel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = new Padding(0, 0, 12, 12),
			Padding = new Padding(1),
			BackColor = ThemeAccentSoftHover
		};
		Panel panel2 = new Panel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = new Padding(0),
			Padding = new Padding(12, 9, 12, 9),
			BackColor = ThemeAccentSoft
		};
		panel2.Controls.Add(tableLayoutPanel);
		panel.Controls.Add(panel2);
		return panel;
	}

	private static void AddLabeledControl(FlowLayoutPanel panel, string caption, Control control)
	{
		panel.Controls.Add(MakeCaption(caption));
		panel.Controls.Add(control);
	}

	private static TextBox CreateReadOnlyDetailsBox()
	{
		return new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Font = new Font("Microsoft YaHei UI", 9f),
			BackColor = ThemeSurface,
			BorderStyle = BorderStyle.FixedSingle,
			ForeColor = ThemeTextStrong
		};
	}

	private static SplitContainer CreateVerticalDetailSplit(Control topControl, Control detailControl, int splitterDistance, int panel1MinSize, int panel2MinSize, Action<SplitContainer> onResize)
	{
		SplitContainer splitContainer = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal
		};
		Action applySplitLayout = delegate
		{
			if (onResize != null)
			{
				onResize(splitContainer);
			}
			else
			{
				ApplySplitContainerLayout(splitContainer, splitterDistance, panel1MinSize, panel2MinSize);
			}
		};
		splitContainer.Resize += delegate
		{
			applySplitLayout();
		};
		splitContainer.HandleCreated += delegate
		{
			applySplitLayout();
		};
		splitContainer.Panel1.Controls.Add(topControl);
		splitContainer.Panel2.Controls.Add(detailControl);
		return splitContainer;
	}

	private static FlowLayoutPanel CreatePagerPanel(Padding padding, out Button prevButton, out Button nextButton, out Label pagerLabel, Action onPrevClick, Action onNextClick)
	{
		FlowLayoutPanel flowLayoutPanel = CreateHorizontalFlowPanel(padding, wrapContents: false);
		prevButton = new Button
		{
			AutoSize = true,
			Text = "上一页"
		};
		nextButton = new Button
		{
			AutoSize = true,
			Text = "下一页"
		};
		ApplySecondaryButtonStyle(prevButton);
		ApplySecondaryButtonStyle(nextButton);
		pagerLabel = new Label
		{
			AutoSize = true,
			Padding = new Padding(10, 8, 0, 0),
			ForeColor = ThemeTextMuted
		};
		if (onPrevClick != null)
		{
			prevButton.Click += delegate
			{
				onPrevClick();
			};
		}
		if (onNextClick != null)
		{
			nextButton.Click += delegate
			{
				onNextClick();
			};
		}
		flowLayoutPanel.Controls.Add(prevButton);
		flowLayoutPanel.Controls.Add(nextButton);
		flowLayoutPanel.Controls.Add(pagerLabel);
		return flowLayoutPanel;
	}

	private TabPage BuildCollectionPage()
	{
		TabPage tabPage = new TabPage("我的卡牌收藏");
		ApplyTabPageTheme(tabPage);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize));
		FlowLayoutPanel flowLayoutPanel = CreateFilterBarPanel(wrapContents: false);
		FlowLayoutPanel flowLayoutPanel2 = CreateFilterBarPanel(wrapContents: false);
		CollectionSearchBox = new TextBox
		{
			Width = 220
		};
		ApplyTextInputStyle(CollectionSearchBox);
		ClassFilterBox = CreateDropDownListComboBox(130);
		CostFilterBox = CreateDropDownListComboBox(110);
		TypeFilterBox = CreateDropDownListComboBox(130);
		CollectionOwnershipFilterBox = CreateDropDownListComboBox(110);
		CollectionRarityFilterBox = CreateDropDownListComboBox(110);
		CollectionPremiumFilterBox = CreateDropDownListComboBox(110);
		SetFilterBox = CreateDropDownListComboBox(160);
		Button button = new Button
		{
			AutoSize = true,
			Text = "重置筛选"
		};
		ApplySecondaryButtonStyle(button);
		button.Click += delegate
		{
			ResetCollectionFiltersAndRefresh();
		};
		CollectionSearchBox.TextChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		ClassFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		CostFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		TypeFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		CollectionOwnershipFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		CollectionRarityFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		CollectionPremiumFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		SetFilterBox.SelectedIndexChanged += delegate
		{
			ResetCollectionPagingAndRefresh();
		};
		AddLabeledControl(flowLayoutPanel, "搜索", CollectionSearchBox);
		AddLabeledControl(flowLayoutPanel, "职业", ClassFilterBox);
		AddLabeledControl(flowLayoutPanel, "费用", CostFilterBox);
		AddLabeledControl(flowLayoutPanel, "卡牌类型", TypeFilterBox);
		AddLabeledControl(flowLayoutPanel2, "拥有", CollectionOwnershipFilterBox);
		AddLabeledControl(flowLayoutPanel2, "稀有度", CollectionRarityFilterBox);
		AddLabeledControl(flowLayoutPanel2, "品质", CollectionPremiumFilterBox);
		AddLabeledControl(flowLayoutPanel2, "系列", SetFilterBox);
		flowLayoutPanel2.Controls.Add(button);
		tableLayoutPanel2.Controls.Add(CreateCardTitleLabel("收藏筛选"), 0, 0);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 1);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel2, 0, 2);
		TableLayoutPanel tableLayoutPanel3 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		CollectionSummaryLabel = CreateSummaryLabel(30, new Padding(10, 6, 10, 0));
		Button button2;
		Button button3;
		Label label;
		FlowLayoutPanel control = CreatePagerPanel(new Padding(10, 0, 10, 6), out button2, out button3, out label, delegate
		{
			if (_collectionPageIndex > 0)
			{
				_collectionPageIndex--;
				RefreshCollectionGrid();
			}
		}, delegate
		{
			_collectionPageIndex++;
			RefreshCollectionGrid();
		});
		CollectionPrevButton = button2;
		CollectionNextButton = button3;
		CollectionPagerLabel = label;
		CollectionGrid = CreateReadOnlyGrid();
		tableLayoutPanel3.Controls.Add(CreateCardTitleLabel("收藏列表"), 0, 0);
		tableLayoutPanel3.Controls.Add(CollectionSummaryLabel, 0, 1);
		tableLayoutPanel3.Controls.Add(control, 0, 2);
		tableLayoutPanel3.Controls.Add(CollectionGrid, 0, 3);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 12), new Padding(16, 14, 16, 14)), 0, 0);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel3, new Padding(0), new Padding(16, 12, 16, 16)), 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private TabPage BuildAchievementCategoriesPage()
	{
		TabPage tabPage = new TabPage("官方成就分类");
		ApplyTabPageTheme(tabPage);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.Percent, 32f), new RowStyle(SizeType.Percent, 68f));
		FlowLayoutPanel flowLayoutPanel = CreateFilterBarPanel(wrapContents: false);
		FlowLayoutPanel flowLayoutPanel2 = CreateFilterBarPanel(wrapContents: false);
		ProfileCompletionFilterBox = CreateCompletionFilterComboBox(RefreshProfileGrid);
		ProfileOpenDetailButton = new Button
		{
			AutoSize = true,
			Text = "打开细分"
		};
		ApplySecondaryButtonStyle(ProfileOpenDetailButton);
		ProfileOpenDetailButton.Click += delegate
		{
			OpenSelectedProfileDetails();
		};
		flowLayoutPanel.Controls.Add(MakeCaption("官方分类"));
		flowLayoutPanel.Controls.Add(CreateInlineLabel("三层分类", new Padding(0, 8, 18, 0)));
		AddLabeledControl(flowLayoutPanel, "完成状态", ProfileCompletionFilterBox);
		flowLayoutPanel.Controls.Add(ProfileOpenDetailButton);
		ProfileSummaryLabel = CreateSummaryLabel(40, new Padding(10, 10, 10, 0));
		ProfileSummaryLabel.TextAlign = ContentAlignment.MiddleRight;
		ProfileSummaryLabel.Margin = new Padding(12, 0, 0, 0);
		ProfileSummaryLabel.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
		ProfileGrid = CreateReadOnlyGrid();
		ApplyCompactOverviewGridStyle(ProfileGrid);
		ApplyFilledOverviewGridStyle(ProfileGrid);
		ProfileGrid.CellDoubleClick += delegate
		{
			OpenSelectedProfileDetails();
		};
		ProfileGrid.KeyDown += delegate(object sender, KeyEventArgs args)
		{
			if (args.KeyCode == Keys.Return)
			{
				args.Handled = true;
				args.SuppressKeyPress = true;
				OpenSelectedProfileDetails();
			}
		};
		LadderClassCompletionFilterBox = CreateCompletionFilterComboBox(RefreshLadderClassGrid);
		LadderClassOpenDetailButton = new Button
		{
			AutoSize = true,
			Text = "打开细分"
		};
		ApplySecondaryButtonStyle(LadderClassOpenDetailButton);
		LadderClassOpenDetailButton.Click += delegate
		{
			OpenSelectedLadderClassDetails();
		};
		flowLayoutPanel2.Controls.Add(MakeCaption("按职业分类"));
		flowLayoutPanel2.Controls.Add(CreateInlineLabel("游戏成就", new Padding(0, 8, 18, 0)));
		AddLabeledControl(flowLayoutPanel2, "完成状态", LadderClassCompletionFilterBox);
		flowLayoutPanel2.Controls.Add(LadderClassOpenDetailButton);
		LadderClassSummaryLabel = CreateSummaryLabel(40, new Padding(10, 10, 10, 0));
		LadderClassSummaryLabel.TextAlign = ContentAlignment.MiddleRight;
		LadderClassSummaryLabel.Margin = new Padding(12, 0, 0, 0);
		LadderClassSummaryLabel.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
		LadderClassGrid = CreateReadOnlyGrid();
		ApplyCompactOverviewGridStyle(LadderClassGrid);
		ApplyFilledOverviewGridStyle(LadderClassGrid);
		LadderClassGrid.CellDoubleClick += delegate
		{
			OpenSelectedLadderClassDetails();
		};
		LadderClassGrid.KeyDown += delegate(object sender, KeyEventArgs args)
		{
			if (args.KeyCode == Keys.Return)
			{
				args.Handled = true;
				args.SuppressKeyPress = true;
				OpenSelectedLadderClassDetails();
			}
		};
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.ColumnStyles.Clear();
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel2.Controls.Add(ProfileSummaryLabel, 1, 0);
		tableLayoutPanel2.Controls.Add(ProfileGrid, 0, 1);
		tableLayoutPanel2.SetColumnSpan(ProfileGrid, 2);
		TableLayoutPanel tableLayoutPanel3 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.ColumnCount = 2;
		tableLayoutPanel3.ColumnStyles.Clear();
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.Controls.Add(flowLayoutPanel2, 0, 0);
		tableLayoutPanel3.Controls.Add(LadderClassSummaryLabel, 1, 0);
		tableLayoutPanel3.Controls.Add(LadderClassGrid, 0, 1);
		tableLayoutPanel3.SetColumnSpan(LadderClassGrid, 2);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 10), new Padding(12, 10, 12, 12)), 0, 0);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel3, new Padding(0), new Padding(12, 10, 12, 12)), 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private TabPage BuildTrackedAchievementsPage()
	{
		TabPage tabPage = new TabPage("我追踪的成就");
		ApplyTabPageTheme(tabPage);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize));
		FlowLayoutPanel flowLayoutPanel = CreateFilterBarPanel(wrapContents: false);
		TrackedSearchBox = new TextBox
		{
			Width = 240
		};
		ApplyTextInputStyle(TrackedSearchBox);
		Button button = new Button
		{
			AutoSize = true,
			Text = "清空搜索"
		};
		ApplySecondaryButtonStyle(button);
		button.Click += delegate
		{
			if (TrackedSearchBox != null)
			{
				TrackedSearchBox.Clear();
			}
		};
		TrackedOpenGuideButton = new Button
		{
			AutoSize = true,
			Text = "打开当前攻略",
			Enabled = false
		};
		ApplySecondaryButtonStyle(TrackedOpenGuideButton);
		TrackedOpenGuideButton.Click += delegate
		{
			DataGridViewRow actionableAchievementGridRow2 = GetActionableAchievementGridRow(TrackedGrid);
			if (actionableAchievementGridRow2 != null)
			{
				TryOpenAchievementGuideFromRow(TrackedGrid, actionableAchievementGridRow2.Index);
			}
			UpdateTrackedActionButtonsState();
		};
		TrackedRemoveButton = new Button
		{
			AutoSize = true,
			Text = "取消追踪当前行",
			Enabled = false
		};
		ApplySecondaryButtonStyle(TrackedRemoveButton);
		TrackedRemoveButton.Click += delegate
		{
			DataGridViewRow actionableAchievementGridRow = GetActionableAchievementGridRow(TrackedGrid);
			if (actionableAchievementGridRow != null && TrackedGrid != null && TrackedGrid.Columns.Contains("收藏"))
			{
				TryToggleTrackedAchievementFromGrid(TrackedGrid, actionableAchievementGridRow.Index, TrackedGrid.Columns["收藏"].Index);
			}
			UpdateTrackedActionButtonsState();
		};
		TrackedSearchBox.TextChanged += delegate
		{
			RefreshTrackedAchievementsGrid();
		};
		AddLabeledControl(flowLayoutPanel, "搜索", TrackedSearchBox);
		flowLayoutPanel.Controls.Add(CreateInlineLabel("支持名称 / 来源 / 要求 / 备注", new Padding(0, 8, 18, 0)));
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(TrackedOpenGuideButton);
		flowLayoutPanel.Controls.Add(TrackedRemoveButton);
		tableLayoutPanel2.Controls.Add(CreateCardTitleLabel("追踪管理"), 0, 0);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 1);
		TableLayoutPanel tableLayoutPanel3 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		TrackedSummaryLabel = CreateSummaryLabel(34, new Padding(10, 10, 10, 0));
		TrackedGrid = CreateReadOnlyGrid();
		TrackedGrid.CellDoubleClick += delegate
		{
			DataGridViewRow actionableAchievementGridRow4 = GetActionableAchievementGridRow(TrackedGrid);
			if (actionableAchievementGridRow4 != null)
			{
				TryOpenAchievementGuideFromRow(TrackedGrid, actionableAchievementGridRow4.Index);
			}
			UpdateTrackedActionButtonsState();
		};
		TrackedGrid.KeyDown += delegate(object sender, KeyEventArgs args)
		{
			if (args.KeyCode == Keys.Return)
			{
				args.Handled = true;
				args.SuppressKeyPress = true;
				DataGridViewRow actionableAchievementGridRow3 = GetActionableAchievementGridRow(TrackedGrid);
				if (actionableAchievementGridRow3 != null)
				{
					TryOpenAchievementGuideFromRow(TrackedGrid, actionableAchievementGridRow3.Index);
				}
			}
			UpdateTrackedActionButtonsState();
		};
		TrackedGrid.SelectionChanged += delegate
		{
			UpdateTrackedActionButtonsState();
		};
		tableLayoutPanel3.Controls.Add(CreateCardTitleLabel("追踪列表"), 0, 0);
		tableLayoutPanel3.Controls.Add(TrackedSummaryLabel, 0, 1);
		tableLayoutPanel3.Controls.Add(TrackedGrid, 0, 2);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 12), new Padding(16, 14, 16, 14)), 0, 0);
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel3, new Padding(0), new Padding(16, 14, 16, 16)), 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private TabPage BuildMultiGuideManagementPage()
	{
		TabPage tabPage = new TabPage("多攻略管理");
		ApplyTabPageTheme(tabPage);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel = CreateFilterBarPanel(wrapContents: false);
		TextBox textBox = new TextBox
		{
			Width = 260
		};
		ApplyTextInputStyle(textBox);
		Button button = new Button
		{
			AutoSize = true,
			Text = "打开当前行攻略"
		};
		ApplySecondaryButtonStyle(button);
		Button button2 = new Button
		{
			AutoSize = true,
			Text = "清空筛选"
		};
		ApplySecondaryButtonStyle(button2);
		flowLayoutPanel.Controls.Add(MakeCaption("筛选"));
		flowLayoutPanel.Controls.Add(textBox);
		flowLayoutPanel.Controls.Add(CreateInlineLabel("支持成就名 / 分类 / 投稿人 / 版本关键词", new Padding(0, 8, 18, 0)));
		flowLayoutPanel.Controls.Add(button2);
		flowLayoutPanel.Controls.Add(button);
		Label label = CreateSummaryLabel(34, new Padding(10, 10, 10, 0));
		DataGridView dataGridView = CreateReadOnlyGrid();
		TextBox textBox2 = CreateReadOnlyDetailsBox();
		textBox2.Text = "当前没有可预览的多份攻略。";
		Button button3 = new Button
		{
			AutoSize = true,
			Text = "刷新预览"
		};
		ApplySecondaryButtonStyle(button3);
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize));
		tableLayoutPanel2.Controls.Add(CreateCardTitleLabel("管理工具"), 0, 0);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 1);
		TableLayoutPanel tableLayoutPanel3 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.Controls.Add(CreateCardTitleLabel("多攻略列表"), 0, 0);
		tableLayoutPanel3.Controls.Add(label, 0, 1);
		tableLayoutPanel3.Controls.Add(dataGridView, 0, 2);
		TableLayoutPanel tableLayoutPanel4 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel2 = CreateFilterBarPanel(wrapContents: false);
		flowLayoutPanel2.Controls.Add(CreateInlineLabel("查看当前选中成就的已收录多版本攻略。", new Padding(0, 8, 18, 0)));
		flowLayoutPanel2.Controls.Add(button3);
		tableLayoutPanel4.Controls.Add(CreateCardTitleLabel("版本预览"), 0, 0);
		tableLayoutPanel4.Controls.Add(flowLayoutPanel2, 0, 1);
		tableLayoutPanel4.Controls.Add(textBox2, 0, 2);
		SplitContainer splitContainer = CreateVerticalDetailSplit(CreateCardHost(tableLayoutPanel3, new Padding(0), new Padding(16, 14, 16, 16)), CreateCardHost(tableLayoutPanel4, new Padding(0), new Padding(16, 14, 16, 16)), 360, 220, 180, null);
		List<MultiGuideManagementRow> list = BuildMultiGuideManagementRows();
		Dictionary<string, MultiGuideManagementRow> dictionary = list.ToDictionary((MultiGuideManagementRow row) => row.GroupKey, (MultiGuideManagementRow row) => row, StringComparer.OrdinalIgnoreCase);
		Action refreshPreview = delegate
		{
			DataGridViewRow actionableAchievementGridRow = GetActionableAchievementGridRow(dataGridView);
			string gridCellText = (actionableAchievementGridRow != null) ? GetGridCellText(actionableAchievementGridRow, "__GuideGroupKey") : string.Empty;
			MultiGuideManagementRow value;
			if (!string.IsNullOrWhiteSpace(gridCellText) && dictionary.TryGetValue(gridCellText, out value))
			{
				textBox2.Text = value.PreviewText;
			}
			else
			{
				textBox2.Text = ((list.Count == 0) ? "当前没有检测到多份攻略。" : "请选择一条成就，查看它的多份攻略版本。");
			}
		};
		Action refreshGrid = delegate
		{
			string keyword = NormalizeLookupKey(textBox.Text);
			List<MultiGuideManagementRow> list2 = list.Where((MultiGuideManagementRow row) => MultiGuideManagementMatchesFilter(row, keyword)).ToList();
			dataGridView.DataSource = BuildMultiGuideManagementTable(list2);
			ConfigureAchievementGuideGrid(dataGridView);
			HideGridColumnIfExists(dataGridView, "__GuideGroupKey");
			ConfigureMultiGuideManagementGridColumns(dataGridView);
			SelectFirstActionableGridRow(dataGridView);
			label.Text = "多份攻略成就: " + list2.Count.ToString(CultureInfo.InvariantCulture) + " / " + list.Count.ToString(CultureInfo.InvariantCulture) + "    当前可管理版本: " + list2.Sum((MultiGuideManagementRow row) => row.VersionCount).ToString(CultureInfo.InvariantCulture);
			button.Enabled = GetActionableAchievementGridRow(dataGridView) != null;
			refreshPreview();
		};
		textBox.TextChanged += delegate
		{
			refreshGrid();
		};
		button2.Click += delegate
		{
			textBox.Clear();
		};
		dataGridView.SelectionChanged += delegate
		{
			button.Enabled = GetActionableAchievementGridRow(dataGridView) != null;
			refreshPreview();
		};
		button3.Click += delegate
		{
			refreshPreview();
		};
		button.Click += delegate
		{
			DataGridViewRow actionableAchievementGridRow2 = GetActionableAchievementGridRow(dataGridView);
			if (actionableAchievementGridRow2 != null)
			{
				TryOpenAchievementGuideFromRow(dataGridView, actionableAchievementGridRow2.Index);
			}
		};
		tableLayoutPanel.Controls.Add(CreateCardHost(tableLayoutPanel2, new Padding(0, 0, 0, 12), new Padding(16, 14, 16, 14)), 0, 0);
		tableLayoutPanel.Controls.Add(splitContainer, 0, 1);
		tabPage.Controls.Add(tableLayoutPanel);
		refreshGrid();
		return tabPage;
	}

	private TabPage BuildLadderClassAchievementPage()
	{
		TabPage tabPage = new TabPage("按职业分类（游戏）");
		ApplyTabPageTheme(tabPage);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel = CreateHorizontalFlowPanel(new Padding(8, 8, 8, 0), wrapContents: false);
		LadderClassCompletionFilterBox = CreateCompletionFilterComboBox(RefreshLadderClassGrid);
		LadderClassOpenDetailButton = new Button
		{
			AutoSize = true,
			Text = "打开细分"
		};
		ApplySecondaryButtonStyle(LadderClassOpenDetailButton);
		LadderClassOpenDetailButton.Click += delegate
		{
			OpenSelectedLadderClassDetails();
		};
		flowLayoutPanel.Controls.Add(MakeCaption("分类方式"));
		flowLayoutPanel.Controls.Add(CreateInlineLabel("按职业", new Padding(0, 8, 18, 0)));
		AddLabeledControl(flowLayoutPanel, "完成状态", LadderClassCompletionFilterBox);
		flowLayoutPanel.Controls.Add(LadderClassOpenDetailButton);
		LadderClassSummaryLabel = CreateSummaryLabel(34, new Padding(10, 10, 10, 0));
		LadderClassGrid = CreateReadOnlyGrid();
		LadderClassGrid.CellDoubleClick += delegate
		{
			OpenSelectedLadderClassDetails();
		};
		LadderClassGrid.KeyDown += delegate(object sender, KeyEventArgs args)
		{
			if (args.KeyCode == Keys.Return)
			{
				args.Handled = true;
				args.SuppressKeyPress = true;
				OpenSelectedLadderClassDetails();
			}
		};
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel.Controls.Add(LadderClassSummaryLabel, 0, 1);
		tableLayoutPanel.Controls.Add(LadderClassGrid, 0, 2);
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private MindVisionExportRefreshResult TryRefreshMindVisionExport(Action<string> progress = null)
	{
		string[] mindVisionExportOutputPaths = GetMindVisionExportOutputPaths();
		string text = FindLatestExistingPath(mindVisionExportOutputPaths);
		DateTime? dateTime = TryGetFileWriteTime(text);
		try
		{
			progress?.Invoke("检测 Hearthstone 进程...");
			if (Process.GetProcessesByName("Hearthstone").Length == 0)
			{
				return MindVisionExportRefreshResult.Create("已跳过", "未检测到 Hearthstone 进程，继续使用现有导出文件。", null, text, dateTime, shouldWarnUser: false);
			}
			string[] source = new string[3]
			{
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExportMindVisionAchievements.v3.exe"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExportMindVisionAchievements.v2.exe"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExportMindVisionAchievements.exe")
			};
			string text2 = source.FirstOrDefault(File.Exists);
			if (string.IsNullOrWhiteSpace(text2))
			{
				return MindVisionExportRefreshResult.Create("已跳过", "未找到 ExportMindVisionAchievements 可执行文件。", "未找到可用导出程序", null, dateTime, shouldWarnUser: true);
			}
			Directory.CreateDirectory(_mindVisionExportDir);
			progress?.Invoke("调用运行时导出器...");
			DateTime now = DateTime.Now;
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = text2;
			processStartInfo.Arguments = "\"" + _mindVisionExportDir + "\"";
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.RedirectStandardError = true;
			using Process process = Process.Start(processStartInfo);
			if (process == null)
			{
				return MindVisionExportRefreshResult.Create("失败", "导出程序未能成功启动。", text2, text, dateTime, shouldWarnUser: true);
			}
			if (!process.WaitForExit(25000))
			{
				string text3 = "导出超时，已等待 25 秒。";
				try
				{
					process.Kill();
					process.WaitForExit(2000);
				}
				catch
				{
				}
				text3 = AppendProcessOutput(text3, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
				return MindVisionExportRefreshResult.Create("失败", text3, text2, text, dateTime, shouldWarnUser: true);
			}
			string text4 = process.StandardOutput.ReadToEnd();
			string text5 = process.StandardError.ReadToEnd();
			string text6 = FindLatestExistingPath(mindVisionExportOutputPaths);
			DateTime? dateTime2 = TryGetFileWriteTime(text6);
			if (process.ExitCode != 0)
			{
				string text7 = AppendProcessOutput("导出程序退出码: " + process.ExitCode.ToString(CultureInfo.InvariantCulture), text4, text5);
				return MindVisionExportRefreshResult.Create("失败", text7, text2, text6 ?? text, dateTime2 ?? dateTime, shouldWarnUser: true);
			}
			bool flag = dateTime2.HasValue && ((!dateTime.HasValue || dateTime2.Value > dateTime.Value) || dateTime2.Value >= now.AddSeconds(-1.0));
			if (!flag)
			{
				string text8 = AppendProcessOutput("导出程序返回成功，但导出文件时间戳没有更新。", text4, text5);
				return MindVisionExportRefreshResult.Create("警告", text8, text2, text6 ?? text, dateTime2 ?? dateTime, shouldWarnUser: true);
			}
			string text9 = AppendProcessOutput("运行时导出已刷新。", text4, text5);
			return MindVisionExportRefreshResult.Create("成功", text9, text2, text6, dateTime2, shouldWarnUser: false);
		}
		catch (Exception ex)
		{
			return MindVisionExportRefreshResult.Create("失败", "运行时导出异常: " + ex.Message, null, text, dateTime, shouldWarnUser: true);
		}
	}

	private List<OfficialCategoryExportRow> LoadOfficialCategoryExports()
	{
		try
		{
			List<OfficialCalibrationFlatRow> officialCalibrationRows = LoadOfficialCalibrationRows();
			if (officialCalibrationRows.Count > 0)
			{
				return BuildOfficialCategoryExportsFromCalibration(officialCalibrationRows, LoadRuntimeOfficialCategoryExports(), LoadRuntimeOfficialAchievementRows());
			}
			return LoadRuntimeOfficialCategoryExports();
		}
		catch
		{
			return new List<OfficialCategoryExportRow>();
		}
	}

	private Dictionary<string, OfficialCategoryPathInfo> LoadOfficialTypePaths()
	{
		try
		{
			List<OfficialCalibrationFlatRow> officialCalibrationRows = LoadOfficialCalibrationRows();
			if (officialCalibrationRows.Count > 0)
			{
				return BuildOfficialTypePathMapFromCalibration(officialCalibrationRows);
			}
			return LoadRuntimeOfficialTypePaths();
		}
		catch
		{
			return new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private List<OfficialCategoryExportRow> LoadRuntimeOfficialCategoryExports()
	{
		try
		{
			string path = FindMindVisionExportFilePath("mindvision-official-categories.json");
			if (string.IsNullOrWhiteSpace(path))
			{
				return new List<OfficialCategoryExportRow>();
			}
			return LoadJson<List<OfficialCategoryExportRow>>(path) ?? new List<OfficialCategoryExportRow>();
		}
		catch
		{
			return new List<OfficialCategoryExportRow>();
		}
	}

	private Dictionary<string, OfficialCategoryPathInfo> LoadRuntimeOfficialTypePaths()
	{
		try
		{
			string path = FindMindVisionExportFilePath("mindvision-achievement-category-config.json");
			if (!File.Exists(path))
			{
				return new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
			}
			string text = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(text))
			{
				return new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
			}
			Dictionary<string, object> configuration = _serializer.DeserializeObject(text) as Dictionary<string, object>;
			return BuildOfficialTypePathMap(configuration);
		}
		catch
		{
			return new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private List<OfficialAchievementExportRow> LoadRuntimeOfficialAchievementRows()
	{
		try
		{
			string path = FindMindVisionExportFilePath("mindvision-achievements.json");
			if (string.IsNullOrWhiteSpace(path))
			{
				return new List<OfficialAchievementExportRow>();
			}
			return LoadJson<List<OfficialAchievementExportRow>>(path) ?? new List<OfficialAchievementExportRow>();
		}
		catch
		{
			return new List<OfficialAchievementExportRow>();
		}
	}

	private List<OfficialCalibrationFlatRow> LoadOfficialCalibrationRows()
	{
		try
		{
			string path = FindOfficialCalibrationFilePath("flat.zh-CN.json");
			if (string.IsNullOrWhiteSpace(path))
			{
				return new List<OfficialCalibrationFlatRow>();
			}
			return (LoadJson<List<OfficialCalibrationFlatRow>>(path) ?? new List<OfficialCalibrationFlatRow>()).Where((OfficialCalibrationFlatRow row) => row != null && row.achievementId > 0).ToList();
		}
		catch
		{
			return new List<OfficialCalibrationFlatRow>();
		}
	}

	private void TryRefreshOfficialCalibrationCache()
	{
		try
		{
			string externalOfficialCalibrationRoot = GetExternalOfficialCalibrationRoot();
			if (string.IsNullOrWhiteSpace(externalOfficialCalibrationRoot) || !Directory.Exists(externalOfficialCalibrationRoot))
			{
				return;
			}
			string localOfficialCalibrationRoot = GetLocalOfficialCalibrationRoot();
			Directory.CreateDirectory(localOfficialCalibrationRoot);
			foreach (string item in new string[4] { "flat.zh-CN.json", "hierarchy.zh-CN.json", "summary.json", "README.txt" })
			{
				string path = Path.Combine(externalOfficialCalibrationRoot, item);
				if (File.Exists(path))
				{
					File.Copy(path, Path.Combine(localOfficialCalibrationRoot, item), overwrite: true);
				}
			}
		}
		catch (Exception ex)
		{
			WriteStartupTrace("official-calibration-sync: failed: " + SafeTraceText(ex.Message));
		}
	}

	private string GetLocalOfficialCalibrationRoot()
	{
		return Path.Combine(GetLocalJsonRoot(), OfficialCalibrationFolderName);
	}

	private string GetExternalOfficialCalibrationRoot()
	{
		try
		{
			return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "MonoBehaviour", OfficialCalibrationSourceFolderName));
		}
		catch
		{
			return null;
		}
	}

	private IEnumerable<string> GetCandidateOfficialCalibrationDirectories()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string externalOfficialCalibrationRoot = GetExternalOfficialCalibrationRoot();
		if (!string.IsNullOrWhiteSpace(externalOfficialCalibrationRoot))
		{
			hashSet.Add(externalOfficialCalibrationRoot);
		}
		hashSet.Add(GetLocalOfficialCalibrationRoot());
		return hashSet;
	}

	private string FindOfficialCalibrationFilePath(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}
		return GetCandidateOfficialCalibrationDirectories().Select((string directory) => Path.Combine(directory, fileName)).Where(File.Exists).OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
	}

	private List<OfficialCategoryExportRow> BuildOfficialCategoryExportsFromCalibration(List<OfficialCalibrationFlatRow> calibrationRows, IEnumerable<OfficialCategoryExportRow> runtimeCategoryRows, IEnumerable<OfficialAchievementExportRow> runtimeAchievementRows)
	{
		List<OfficialCalibrationFlatRow> list = (calibrationRows ?? new List<OfficialCalibrationFlatRow>()).Where((OfficialCalibrationFlatRow row) => row != null && row.achievementId > 0).ToList();
		if (list.Count == 0)
		{
			return new List<OfficialCategoryExportRow>();
		}
		Dictionary<int, OfficialAchievementExportRow> dictionary = (runtimeAchievementRows ?? Enumerable.Empty<OfficialAchievementExportRow>()).Where((OfficialAchievementExportRow row) => row != null).GroupBy((OfficialAchievementExportRow row) => row.AchievementId).ToDictionary((IGrouping<int, OfficialAchievementExportRow> group) => group.Key, (IGrouping<int, OfficialAchievementExportRow> group) => group.OrderByDescending((OfficialAchievementExportRow row) => row.Progress).ThenByDescending((OfficialAchievementExportRow row) => row.Status).First());
		Dictionary<int, OfficialRuntimeCategoryStats> dictionary2 = (runtimeCategoryRows ?? Enumerable.Empty<OfficialCategoryExportRow>()).Where((OfficialCategoryExportRow row) => row != null && row.RuntimeStats != null).GroupBy((OfficialCategoryExportRow row) => row.Id).ToDictionary((IGrouping<int, OfficialCategoryExportRow> group) => group.Key, (IGrouping<int, OfficialCategoryExportRow> group) => group.OrderByDescending((OfficialCategoryExportRow row) => row.RuntimeStats?.Stats?.Points ?? -1).First().RuntimeStats);
		HashSet<int> hashSet = new HashSet<int>(list.Where((OfficialCalibrationFlatRow row) => row.nextTierId > 0).Select((OfficialCalibrationFlatRow row) => row.nextTierId));
		return (from @group in list.GroupBy((OfficialCalibrationFlatRow row) => row.categoryId)
			orderby @group.Min((OfficialCalibrationFlatRow row) => row.categorySortOrder), @group.Key
			select @group).Select(delegate(IGrouping<int, OfficialCalibrationFlatRow> group)
		{
			OfficialCalibrationFlatRow officialCalibrationFlatRow = group.OrderBy((OfficialCalibrationFlatRow row) => row.categorySortOrder).ThenBy((OfficialCalibrationFlatRow row) => row.subcategorySortOrder).ThenBy((OfficialCalibrationFlatRow row) => row.sectionSortOrder).ThenBy((OfficialCalibrationFlatRow row) => row.achievementSortOrder).ThenBy((OfficialCalibrationFlatRow row) => row.achievementId).First();
			OfficialCategoryPathInfo officialCategoryPathInfo = BuildOfficialCalibrationPathInfo(officialCalibrationFlatRow);
			List<OfficialAchievementExportRow> list2 = (from row in @group
				orderby row.subcategorySortOrder, row.sectionSortOrder, row.achievementSortOrder, row.achievementId
				select BuildOfficialAchievementFromCalibration(row, dictionary.TryGetValue(row.achievementId, out var value) ? value : null, hashSet)).Where((OfficialAchievementExportRow row) => row != null).ToList();
			dictionary2.TryGetValue(group.Key, out var value2);
			return new OfficialCategoryExportRow
			{
				Id = officialCategoryPathInfo.RootCategory.Id,
				Name = officialCategoryPathInfo.RootCategory.Name,
				Icon = officialCategoryPathInfo.RootCategory.Icon,
				AchievementCount = list2.Count,
				Achievements = list2,
				RuntimeStats = BuildOfficialRuntimeCategoryStats(officialCategoryPathInfo.RootCategory, list2, value2)
			};
		}).ToList();
	}

	private Dictionary<string, OfficialCategoryPathInfo> BuildOfficialTypePathMapFromCalibration(IEnumerable<OfficialCalibrationFlatRow> calibrationRows)
	{
		Dictionary<string, OfficialCategoryPathInfo> dictionary = new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (OfficialCalibrationFlatRow calibrationRow in calibrationRows ?? Enumerable.Empty<OfficialCalibrationFlatRow>())
		{
			if (calibrationRow == null || calibrationRow.achievementId <= 0)
			{
				continue;
			}
			string text = BuildOfficialCalibrationType(calibrationRow);
			if (!string.IsNullOrWhiteSpace(text) && !dictionary.ContainsKey(text))
			{
				dictionary[text] = BuildOfficialCalibrationPathInfo(calibrationRow);
			}
		}
		return dictionary;
	}

	private OfficialAchievementExportRow BuildOfficialAchievementFromCalibration(OfficialCalibrationFlatRow row, OfficialAchievementExportRow runtimeRow, HashSet<int> priorTierTargets)
	{
		if (row == null)
		{
			return null;
		}
		OfficialCategoryPathInfo officialCategoryPathInfo = BuildOfficialCalibrationPathInfo(row);
		ReferenceAchievementExportRow reference = runtimeRow?.Reference;
		string text = FirstNonEmpty(NullIfWhiteSpace(row.achievementNameZhCN), new string[3]
		{
			reference?.DisplayName,
			reference?.Name,
			"成就 " + row.achievementId.ToString(CultureInfo.InvariantCulture)
		});
		string text2 = FirstNonEmpty(NullIfWhiteSpace(row.achievementDescZhCN), new string[3]
		{
			reference?.Text,
			reference?.CompletedText,
			reference?.EmptyText
		});
		return new OfficialAchievementExportRow
		{
			AchievementId = row.achievementId,
			Progress = runtimeRow?.Progress ?? 0,
			Index = runtimeRow?.Index ?? 0,
			Status = runtimeRow?.Status ?? 0,
			Reference = new ReferenceAchievementExportRow
			{
				Id = FirstNonEmpty(reference?.Id, new string[1] { "official_" + row.achievementId.ToString(CultureInfo.InvariantCulture) }),
				HsAchievementId = row.achievementId,
				HsSectionId = row.sectionId,
				HsRewardTrackXp = row.rewardTrackXp,
				Name = text,
				DisplayName = text,
				Text = text2,
				CompletedText = text2,
				EmptyText = text2,
				Type = FirstNonEmpty(reference?.Type, new string[1] { BuildOfficialCalibrationType(row) }),
				Icon = FirstNonEmpty(reference?.Icon, new string[1] { "boss_victory" }),
				DisplayCardId = reference?.DisplayCardId,
				DisplayCardType = reference?.DisplayCardType,
				Points = ((row.points > 0) ? row.points : (reference?.Points ?? 0)),
				Priority = row.achievementSortOrder,
				Quota = ((row.quota > 0) ? row.quota : (reference?.Quota ?? 0)),
				Root = priorTierTargets == null || !priorTierTargets.Contains(row.achievementId)
			},
			RootCategory = officialCategoryPathInfo.RootCategory,
			PrimaryCategory = officialCategoryPathInfo.PrimaryCategory,
			LeafCategory = officialCategoryPathInfo.LeafCategory
		};
	}

	private OfficialCategoryPathInfo BuildOfficialCalibrationPathInfo(OfficialCalibrationFlatRow row)
	{
		if (row == null)
		{
			return new OfficialCategoryPathInfo();
		}
		string text = FirstNonEmpty(NullIfWhiteSpace(row.categoryNameZhCN), new string[1] { GetOfficialCategoryDisplayName(row.categoryId) });
		string text2 = FirstNonEmpty(NullIfWhiteSpace(row.subcategoryNameZhCN), new string[1] { NullIfWhiteSpace(row.sectionNameZhCN) ?? text });
		string text3 = FirstNonEmpty(NullIfWhiteSpace(row.sectionNameZhCN), new string[2] { text2, text });
		OfficialCategoryReference rootCategory = new OfficialCategoryReference
		{
			Id = row.categoryId,
			Key = "official_root_" + row.categoryId.ToString(CultureInfo.InvariantCulture),
			Name = text,
			Icon = FirstNonEmpty(NullIfWhiteSpace(row.categoryIcon), new string[1] { GetOfficialCategoryIcon(row.categoryId) })
		};
		OfficialCategoryReference leafCategory = new OfficialCategoryReference
		{
			Id = row.sectionId,
			Key = "official_leaf_" + row.categoryId.ToString(CultureInfo.InvariantCulture) + "_" + row.sectionId.ToString(CultureInfo.InvariantCulture),
			Name = text3,
			Icon = rootCategory.Icon
		};
		OfficialCategoryReference primaryCategory = leafCategory;
		if (!string.IsNullOrWhiteSpace(NullIfWhiteSpace(row.subcategoryNameZhCN)))
		{
			primaryCategory = new OfficialCategoryReference
			{
				Id = row.subcategoryId,
				Key = "official_primary_" + row.categoryId.ToString(CultureInfo.InvariantCulture) + "_" + row.subcategoryId.ToString(CultureInfo.InvariantCulture),
				Name = text2,
				Icon = FirstNonEmpty(NullIfWhiteSpace(row.subcategoryIcon), new string[1] { rootCategory.Icon })
			};
		}
		return new OfficialCategoryPathInfo
		{
			RootCategory = rootCategory,
			PrimaryCategory = primaryCategory,
			LeafCategory = leafCategory
		};
	}

	private static OfficialRuntimeCategoryStats BuildOfficialRuntimeCategoryStats(OfficialCategoryReference rootCategory, IList<OfficialAchievementExportRow> achievements, OfficialRuntimeCategoryStats runtimeStats)
	{
		IList<OfficialAchievementExportRow> list = achievements ?? new List<OfficialAchievementExportRow>();
		OfficialRuntimeCategoryNumbers officialRuntimeCategoryNumbers = runtimeStats?.Stats;
		int num = list.Sum((OfficialAchievementExportRow item) => item?.Reference?.Points ?? 0);
		int num2 = list.Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => item?.Reference?.Points ?? 0);
		int num3 = list.Count(IsOfficialAchievementCompleted);
		return new OfficialRuntimeCategoryStats
		{
			Id = rootCategory?.Id ?? 0,
			Name = rootCategory?.Name,
			Icon = rootCategory?.Icon,
			Stats = new OfficialRuntimeCategoryNumbers
			{
				AvailablePoints = num,
				Points = officialRuntimeCategoryNumbers?.Points ?? num2,
				CompletedAchievements = officialRuntimeCategoryNumbers?.CompletedAchievements ?? num3,
				TotalAchievements = list.Count,
				Unclaimed = officialRuntimeCategoryNumbers?.Unclaimed ?? 0
			}
		};
	}

	private static string BuildOfficialCalibrationType(OfficialCalibrationFlatRow row)
	{
		if (row == null)
		{
			return null;
		}
		string text = NormalizeLookupKey(row.achievementNameZhCN);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = row.achievementId.ToString(CultureInfo.InvariantCulture);
		}
		return string.Format(CultureInfo.InvariantCulture, "official_{0}_{1}_{2}_{3}", row.categoryId, row.subcategoryId, row.sectionId, text);
	}

	private void EnrichOfficialAchievementPaths(List<OfficialCategoryExportRow> officialCategoryExportRows, Dictionary<string, OfficialCategoryPathInfo> officialTypePathMap)
	{
		if (officialCategoryExportRows == null || officialCategoryExportRows.Count == 0 || officialTypePathMap == null || officialTypePathMap.Count == 0)
		{
			return;
		}
		foreach (OfficialCategoryExportRow officialCategoryExportRow in officialCategoryExportRows)
		{
			foreach (OfficialAchievementExportRow item in officialCategoryExportRow.Achievements ?? new List<OfficialAchievementExportRow>())
			{
				string text = ((item.Reference != null) ? item.Reference.Type : null);
				if (!string.IsNullOrWhiteSpace(text) && officialTypePathMap.TryGetValue(text, out var value) && value != null)
				{
					item.RootCategory = item.RootCategory ?? value.RootCategory;
					item.PrimaryCategory = value.PrimaryCategory;
					item.LeafCategory = value.LeafCategory;
				}
			}
		}
	}

	private void BuildCollectionLookups(IEnumerable<OwnedCollectionRow> rows, out Dictionary<string, OwnedCollectionRow> collectionLookupById, out Dictionary<string, List<OwnedCollectionRow>> collectionLookupByName)
	{
		IEnumerable<OwnedCollectionRow> source = rows ?? Enumerable.Empty<OwnedCollectionRow>();
		collectionLookupById = source.Where((OwnedCollectionRow row) => row != null && !string.IsNullOrWhiteSpace(row.Id)).GroupBy((OwnedCollectionRow row) => row.Id, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, OwnedCollectionRow> group) => group.Key, (IGrouping<string, OwnedCollectionRow> group) => group.First(), StringComparer.OrdinalIgnoreCase);
		collectionLookupByName = source.Where((OwnedCollectionRow row) => row != null && !string.IsNullOrWhiteSpace(row.Name)).GroupBy((OwnedCollectionRow row) => NormalizeLookupKey(row.Name), StringComparer.OrdinalIgnoreCase).Where((IGrouping<string, OwnedCollectionRow> group) => !string.IsNullOrWhiteSpace(group.Key)).ToDictionary((IGrouping<string, OwnedCollectionRow> group) => group.Key, (IGrouping<string, OwnedCollectionRow> group) => group.OrderByDescending((OwnedCollectionRow row) => row.TotalOwned).ThenBy((OwnedCollectionRow row) => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy((OwnedCollectionRow row) => row.Id, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
	}

	private Dictionary<string, List<AchievementRelatedCardReference>> LoadAchievementRelatedCardLookup(out string achievementRelatedCardMapPath)
	{
		Dictionary<string, List<AchievementRelatedCardReference>> dictionary2 = new Dictionary<string, List<AchievementRelatedCardReference>>(StringComparer.OrdinalIgnoreCase);
		string text = FindAchievementRelatedCardMapPath();
		achievementRelatedCardMapPath = (string.IsNullOrWhiteSpace(text) ? "未找到关联卡牌表" : text);
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			return dictionary2;
		}
		try
		{
			List<AchievementRelatedCardMapRow> list = LoadJson<List<AchievementRelatedCardMapRow>>(text) ?? new List<AchievementRelatedCardMapRow>();
			Dictionary<string, HashSet<string>> dictionary = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (AchievementRelatedCardMapRow item in list)
			{
				if (item == null || string.IsNullOrWhiteSpace(item.achievementName))
				{
					continue;
				}
				string text2 = NormalizeLookupKey(item.achievementName);
				if (string.IsNullOrWhiteSpace(text2))
				{
					continue;
				}
				if (!dictionary2.TryGetValue(text2, out var value))
				{
					value = new List<AchievementRelatedCardReference>();
					dictionary2[text2] = value;
					dictionary[text2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				}
				HashSet<string> hashSet = dictionary[text2];
				foreach (AchievementRelatedCardEntry item2 in item.relatedCards ?? new List<AchievementRelatedCardEntry>())
				{
					string text3 = NullIfWhiteSpace(item2?.cardId);
					string text4 = NullIfWhiteSpace(item2?.cardName);
					if (text3 == null && text4 == null)
					{
						continue;
					}
					string item3 = (text3 ?? string.Empty) + "|" + (text4 ?? string.Empty);
					if (hashSet.Add(item3))
					{
						value.Add(new AchievementRelatedCardReference
						{
							CardId = text3,
							CardName = text4
						});
					}
				}
			}
		}
		catch (Exception ex)
		{
			dictionary2 = new Dictionary<string, List<AchievementRelatedCardReference>>(StringComparer.OrdinalIgnoreCase);
			achievementRelatedCardMapPath = text + " (加载失败: " + ex.Message + ")";
		}
		return dictionary2;
	}

	private Dictionary<string, List<string>> LoadDualClassAchievementLookup(out string dualClassAchievementMapPath)
	{
		Dictionary<string, List<string>> dictionary2 = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		string text = FindDualClassAchievementMapPath();
		dualClassAchievementMapPath = string.IsNullOrWhiteSpace(text) ? "未找到双职业映射表" : text;
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			return dictionary2;
		}
		try
		{
			Dictionary<string, object> dictionary = LoadJson<Dictionary<string, object>>(text) ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, object> item in dictionary)
			{
				string text2 = NormalizeLookupKey(item.Key);
				if (string.IsNullOrWhiteSpace(text2) || item.Value == null)
				{
					continue;
				}
				IEnumerable<string> source = null;
				if (item.Value is object[] source2)
				{
					source = source2.Select((object value) => Convert.ToString(value, CultureInfo.InvariantCulture));
				}
				else if (item.Value is Array array)
				{
					source = array.Cast<object>().Select((object value) => Convert.ToString(value, CultureInfo.InvariantCulture));
				}
				else if (item.Value is string text3)
				{
					source = new string[1] { text3 };
				}
				else if (item.Value is System.Collections.IEnumerable enumerable)
				{
					source = enumerable.Cast<object>().Select((object value) => Convert.ToString(value, CultureInfo.InvariantCulture));
				}
				if (source == null)
				{
					continue;
				}
				List<string> list = source.Select(NormalizeAchievementClassName).Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(ClassSortValue).ThenBy((string value) => value, StringComparer.OrdinalIgnoreCase).ToList();
				if (list.Count > 0)
				{
					dictionary2[text2] = list;
				}
			}
		}
		catch (Exception ex)
		{
			dictionary2 = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			dualClassAchievementMapPath = text + " (加载失败: " + ex.Message + ")";
		}
		return dictionary2;
	}

	private string FindDualClassAchievementMapPath()
	{
		List<string> list = new List<string>();
		try
		{
			string[] array = new string[3]
			{
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dual-class-achievement-map.json"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "json", "dual-class-achievement-map.json"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "json", "dual-class-achievement-map.json")
			};
			foreach (string path in array)
			{
				if (File.Exists(path))
				{
					list.Add(path);
				}
			}
		}
		catch
		{
		}
		foreach (string candidateAchievementRelatedCardDirectory in GetCandidateAchievementRelatedCardDirectories())
		{
			try
			{
				if (!Directory.Exists(candidateAchievementRelatedCardDirectory))
				{
					continue;
				}
				list.AddRange(Directory.GetFiles(candidateAchievementRelatedCardDirectory, "dual-class-achievement-map.json", SearchOption.TopDirectoryOnly));
				string path2 = Path.Combine(candidateAchievementRelatedCardDirectory, "json");
				if (Directory.Exists(path2))
				{
					list.AddRange(Directory.GetFiles(path2, "dual-class-achievement-map.json", SearchOption.TopDirectoryOnly));
				}
			}
			catch
			{
			}
		}
		return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending((string path) => File.GetLastWriteTime(path)).FirstOrDefault();
	}

	private string FindAchievementRelatedCardMapPath()
	{
		List<string> list = new List<string>();
		foreach (string candidateAchievementRelatedCardDirectory in GetCandidateAchievementRelatedCardDirectories())
		{
			try
			{
				if (!Directory.Exists(candidateAchievementRelatedCardDirectory))
				{
					continue;
				}
				list.AddRange(from path in Directory.GetFiles(candidateAchievementRelatedCardDirectory, "*\u5173\u8054\u5361\u724c.json", SearchOption.TopDirectoryOnly)
					where !path.EndsWith(".\u5206\u7ec4\u7248.json", StringComparison.OrdinalIgnoreCase)
					select path);
				// Also look for the extracted bundled file names.
				string extractedFullCards = Path.Combine(candidateAchievementRelatedCardDirectory, "json", "achievement-related-cards.json");
				if (File.Exists(extractedFullCards))
				{
					list.Add(extractedFullCards);
				}
				string extractedChineseFullCards = Path.Combine(candidateAchievementRelatedCardDirectory, "json", "\u6210\u5c31\u5173\u8054\u5361\u724c.json");
				if (File.Exists(extractedChineseFullCards))
				{
					list.Add(extractedChineseFullCards);
				}
				string extractedLegacyFullCards = Path.Combine(candidateAchievementRelatedCardDirectory, "json", "20250419\u7089\u77f3\u4f20\u8bf4\u6210\u5c31\u6a21\u677f.\u5173\u8054\u5361\u724c.json");
				if (File.Exists(extractedLegacyFullCards))
				{
					list.Add(extractedLegacyFullCards);
				}
				string splitCards = Path.Combine(candidateAchievementRelatedCardDirectory, "json", "\u56db\u7248\u672c\u6210\u5c31\u5173\u8054\u5361\u724c.json");
				if (File.Exists(splitCards))
				{
					list.Add(splitCards);
				}
			}
			catch
			{
			}
		}
		return list.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(GetAchievementRelatedCardMapPriority)
			.ThenByDescending((string path) => File.GetLastWriteTime(path))
			.FirstOrDefault();
	}

	private static int GetAchievementRelatedCardMapPriority(string path)
	{
		string text = Path.GetFileName(path) ?? string.Empty;
		if (string.Equals(text, "achievement-related-cards.json", StringComparison.OrdinalIgnoreCase))
		{
			return 6;
		}
		if (string.Equals(text, "成就关联卡牌.json", StringComparison.OrdinalIgnoreCase))
		{
			return 5;
		}
		if (string.Equals(text, "20250419炉石传说成就模板.关联卡牌.json", StringComparison.OrdinalIgnoreCase))
		{
			return 4;
		}
		if (text.IndexOf("成就模板.关联卡牌", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 3;
		}
		if (text.IndexOf("四版本成就关联卡牌", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 1;
		}
		return 2;
	}

	private IEnumerable<string> GetCandidateAchievementRelatedCardDirectories()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string text = Path.GetDirectoryName(typeof(FirestoneDataViewer).Assembly.Location);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = AppDomain.CurrentDomain.BaseDirectory;
		}
		text = (text ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
		string directoryName = Path.GetDirectoryName(text);
		string directoryName2 = string.IsNullOrWhiteSpace(directoryName) ? null : Path.GetDirectoryName(directoryName);
		if (!string.IsNullOrWhiteSpace(text))
		{
			hashSet.Add(text);
		}
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			hashSet.Add(directoryName);
			hashSet.Add(Path.Combine(directoryName, "攻略"));
		}
		if (!string.IsNullOrWhiteSpace(directoryName2))
		{
			hashSet.Add(directoryName2);
			hashSet.Add(Path.Combine(directoryName2, "攻略"));
		}
		return hashSet;
	}

	private void LoadAchievementGuideLookup(out List<AchievementGuideRow> achievementGuideRows, out Dictionary<string, List<AchievementGuideRow>> achievementGuideLookupByName, out string achievementGuideDataPath)
	{
		achievementGuideRows = new List<AchievementGuideRow>();
		achievementGuideLookupByName = new Dictionary<string, List<AchievementGuideRow>>(StringComparer.OrdinalIgnoreCase);
		string text = FindAchievementGuideDataPath();
		achievementGuideDataPath = (string.IsNullOrWhiteSpace(text) ? "未找到攻略总表" : text);
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			return;
		}
		try
		{
			List<AchievementGuideRow> list = LoadJson<List<AchievementGuideRow>>(text) ?? new List<AchievementGuideRow>();
			foreach (AchievementGuideRow item in list)
			{
				if (item == null)
				{
					continue;
				}
				item.achievement_name = CleanMultiline(item.achievement_name);
				item.requirement = CleanMultiline(item.requirement);
				item.category = CleanMultiline(item.category);
				item.sub_category = CleanMultiline(item.sub_category);
				item.idea = CleanMultiline(item.idea);
				item.recommended_deck_codes = CleanMultiline(item.recommended_deck_codes);
				item.title = CleanMultiline(item.title);
				item.series = CleanMultiline(item.series);
				item.editor_id = CleanMultiline(item.editor_id);
				item.source_url = NullIfWhiteSpace(item.source_url);
				item.local_text = CleanMultiline(item.local_text);
				string text2 = NormalizeLookupKey(item.achievement_name);
				if (string.IsNullOrWhiteSpace(text2))
				{
					continue;
				}
				achievementGuideRows.Add(item);
				if (!achievementGuideLookupByName.TryGetValue(text2, out var value))
				{
					value = new List<AchievementGuideRow>();
					achievementGuideLookupByName[text2] = value;
				}
				value.Add(item);
			}
		}
		catch (Exception ex)
		{
			achievementGuideRows = new List<AchievementGuideRow>();
			achievementGuideLookupByName = new Dictionary<string, List<AchievementGuideRow>>(StringComparer.OrdinalIgnoreCase);
			achievementGuideDataPath = text + " (加载失败: " + ex.Message + ")";
		}
	}

	private string FindAchievementGuideDataPath()
	{
		List<string> list = new List<string>();
		foreach (string candidateAchievementRelatedCardDirectory in GetCandidateAchievementRelatedCardDirectories())
		{
			try
			{
				if (!Directory.Exists(candidateAchievementRelatedCardDirectory))
				{
					continue;
				}
				list.AddRange(Directory.GetFiles(candidateAchievementRelatedCardDirectory, "achievement_total_table.json", SearchOption.TopDirectoryOnly));
				// Also support renamed guide-table.json
				string guideTable = Path.Combine(candidateAchievementRelatedCardDirectory, "json", "guide-table.json");
				if (File.Exists(guideTable)) list.Add(guideTable);
			}
			catch
			{
			}
		}
		string text = Path.GetDirectoryName(typeof(FirestoneDataViewer).Assembly.Location);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = AppDomain.CurrentDomain.BaseDirectory;
		}
		text = (text ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
		string directoryName = Path.GetDirectoryName(text);
		string path = string.IsNullOrWhiteSpace(directoryName) ? null : Path.GetDirectoryName(directoryName);
		string path2 = string.IsNullOrWhiteSpace(path) ? null : Path.Combine(path, "downloads");
		if (!string.IsNullOrWhiteSpace(path2) && Directory.Exists(path2))
		{
			try
			{
				foreach (string item in Directory.GetDirectories(path2, "iyingdi_*", SearchOption.TopDirectoryOnly))
				{
					string path3 = Path.Combine(item, "achievement_total_table.json");
					if (File.Exists(path3))
					{
						list.Add(path3);
					}
				}
			}
			catch
			{
			}
		}
		return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending((string path3) => File.GetLastWriteTime(path3)).FirstOrDefault();
	}

	private bool HasAchievementGuides(string achievementName, string requirement, string achievementClass)
	{
		return FindAchievementGuides(achievementName, requirement, achievementClass).Count > 0;
	}

	private List<AchievementGuideRow> FindAchievementGuides(string achievementName, string requirement, string achievementClass)
	{
		string text = BuildAchievementGuideMatchCacheKey(achievementName, requirement, achievementClass);
		if (_achievementGuideMatchCache.TryGetValue(text, out var value))
		{
			return value;
		}
		List<AchievementGuideRow> list = FindAchievementGuidesByNameCandidates(achievementName);
		if (list.Count == 0)
		{
			return CacheAchievementGuideMatches(text, new List<AchievementGuideRow>());
		}
		string text2 = NormalizeLookupKey(requirement);
		string text3 = NormalizeAchievementClassName(achievementClass) ?? CleanMultiline(achievementClass);
		if (!string.IsNullOrWhiteSpace(text2))
		{
			List<AchievementGuideRow> list2 = list.Where((AchievementGuideRow item) => string.Equals(NormalizeLookupKey(item.requirement), text2, StringComparison.OrdinalIgnoreCase)).ToList();
			if (list2.Count > 0)
			{
				return CacheAchievementGuideMatches(text, RankAchievementGuides(list2, achievementName));
			}
			List<AchievementGuideRow> list3 = list.Where((AchievementGuideRow item) => AchievementGuideRequirementMatches(item.requirement, requirement)).ToList();
			if (list3.Count > 0)
			{
				list = list3;
			}
		}
		if (!string.IsNullOrWhiteSpace(text3))
		{
			List<AchievementGuideRow> list4 = list.Where((AchievementGuideRow item) => AchievementGuideCategoryMatches(item, text3)).ToList();
			if (list4.Count > 0)
			{
				list = list4;
			}
		}
		return CacheAchievementGuideMatches(text, RankAchievementGuides(list, achievementName));
	}

	private List<AchievementGuideRow> FindAchievementGuidesByNameCandidates(string achievementName)
	{
		if (_achievementGuideLookupByName == null || _achievementGuideLookupByName.Count == 0)
		{
			return FindAchievementGuidesByFuzzyNameCandidates(achievementName);
		}
		List<AchievementGuideRow> list = new List<AchievementGuideRow>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in GetAchievementGuideNameCandidates(achievementName))
		{
			string text = NormalizeLookupKey(item);
			if (string.IsNullOrWhiteSpace(text) || !hashSet.Add(text) || !_achievementGuideLookupByName.TryGetValue(text, out var value) || value == null || value.Count == 0)
			{
				continue;
			}
			list.AddRange(value);
		}
		if (list.Count == 0)
		{
			list.AddRange(FindAchievementGuidesByFuzzyNameCandidates(achievementName));
		}
		return DeduplicateAchievementGuides(list);
	}

	private List<AchievementGuideRow> FindAchievementGuidesByFuzzyNameCandidates(string achievementName)
	{
		if (_achievementGuideRows == null || _achievementGuideRows.Count == 0)
		{
			return new List<AchievementGuideRow>();
		}
		string text = NormalizeLookupKey(achievementName);
		if (string.IsNullOrWhiteSpace(text))
		{
			return new List<AchievementGuideRow>();
		}
		return DeduplicateAchievementGuides(_achievementGuideRows.Where(delegate(AchievementGuideRow item)
		{
			string text2 = NormalizeLookupKey(item?.achievement_name);
			return !string.IsNullOrWhiteSpace(text2) && text2.Contains(text);
		}).ToList());
	}

	private static List<string> GetAchievementGuideNameCandidates(string achievementName)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Action<string> action = delegate(string raw)
		{
			string text2 = CleanMultiline(raw);
			if (!string.IsNullOrWhiteSpace(text2) && hashSet.Add(text2))
			{
				list.Add(text2);
			}
		};
		string text = CleanMultiline(achievementName);
		action(text);
		if (string.IsNullOrWhiteSpace(text))
		{
			return list;
		}
		foreach (string item in Regex.Split(text, "\\s*(?:/|／|\\||｜|、|,|，|;|；|\\+|＋|&|＆)\\s*"))
		{
			action(item);
		}
		return list;
	}

	private string BuildAchievementGuideMatchCacheKey(string achievementName, string requirement, string achievementClass)
	{
		string str = NormalizeLookupKey(achievementName) ?? string.Empty;
		string str2 = NormalizeLookupKey(requirement) ?? string.Empty;
		string str3 = NormalizeAchievementClassName(achievementClass) ?? CleanMultiline(achievementClass) ?? string.Empty;
		return str + "|" + str2 + "|" + str3;
	}

	private List<AchievementGuideRow> CacheAchievementGuideMatches(string key, List<AchievementGuideRow> matches)
	{
		List<AchievementGuideRow> list = matches ?? new List<AchievementGuideRow>();
		_achievementGuideMatchCache[key ?? string.Empty] = list;
		return list;
	}

	private static List<AchievementGuideRow> DeduplicateAchievementGuides(IEnumerable<AchievementGuideRow> guides)
	{
		List<AchievementGuideRow> list = new List<AchievementGuideRow>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (AchievementGuideRow guide in guides ?? Enumerable.Empty<AchievementGuideRow>())
		{
			if (guide == null)
			{
				continue;
			}
			string item = string.Join("|", new string[7]
			{
				NormalizeLookupKey(guide.achievement_name),
				NormalizeLookupKey(guide.requirement),
				CleanMultiline(guide.category),
				CleanMultiline(guide.source_url),
				CleanMultiline(guide.recommended_deck_codes),
				guide.guide_slot.ToString(CultureInfo.InvariantCulture),
				CleanMultiline(guide.editor_id)
			});
			if (hashSet.Add(item))
			{
				list.Add(guide);
			}
		}
		return list;
	}

	private static List<AchievementGuideRow> RankAchievementGuides(IEnumerable<AchievementGuideRow> guides, string achievementName)
	{
		string normalizedTargetName = NormalizeLookupKey(achievementName);
		return (guides ?? Enumerable.Empty<AchievementGuideRow>())
			.Where((AchievementGuideRow item) => item != null)
			.OrderByDescending((AchievementGuideRow item) => string.Equals(NormalizeLookupKey(item.achievement_name), normalizedTargetName, StringComparison.OrdinalIgnoreCase))
			.ThenBy((AchievementGuideRow item) => item.guide_slot > 0 ? item.guide_slot : int.MaxValue)
			.ThenByDescending((AchievementGuideRow item) => !string.IsNullOrWhiteSpace(item.source_url))
			.ThenByDescending((AchievementGuideRow item) => ParseGuideDate(item.date))
			.ThenBy((AchievementGuideRow item) => item.title, StringComparer.OrdinalIgnoreCase)
			.ThenBy((AchievementGuideRow item) => item.category, StringComparer.OrdinalIgnoreCase)
			.ThenBy((AchievementGuideRow item) => item.sub_category, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static DateTime ParseGuideDate(string value)
	{
		DateTime result;
		return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out result) ? result : DateTime.MinValue;
	}

	private static bool AchievementGuideRequirementMatches(string left, string right)
	{
		string text = NormalizeLookupKey(left);
		string text2 = NormalizeLookupKey(right);
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
		{
			return false;
		}
		return text.Contains(text2) || text2.Contains(text);
	}

	private static bool AchievementGuideCategoryMatches(AchievementGuideRow guide, string achievementClass)
	{
		if (guide == null || string.IsNullOrWhiteSpace(achievementClass))
		{
			return false;
		}
		string text = CleanMultiline(guide.category);
		string text2 = CleanMultiline(guide.sub_category);
		if (!string.IsNullOrWhiteSpace(text) && text.IndexOf(achievementClass, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(text2) && text2.IndexOf(achievementClass, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		return string.Equals(achievementClass, "中立", StringComparison.OrdinalIgnoreCase) && string.Equals(text, "中立", StringComparison.OrdinalIgnoreCase);
	}

	private Dictionary<string, OfficialCategoryPathInfo> BuildOfficialTypePathMap(Dictionary<string, object> configuration)
	{
		Dictionary<string, OfficialCategoryPathInfo> dictionary = new Dictionary<string, OfficialCategoryPathInfo>(StringComparer.OrdinalIgnoreCase);
		if (configuration == null || !configuration.ContainsKey("categories"))
		{
			return dictionary;
		}
		if (!(configuration["categories"] is object[] source))
		{
			return dictionary;
		}
		foreach (Dictionary<string, object> item in source.OfType<Dictionary<string, object>>())
		{
			OfficialCategoryReference rootCategory = ToOfficialCategoryReference(item);
			MapOfficialTypePaths(item, rootCategory, null, dictionary);
		}
		return dictionary;
	}

	private void MapOfficialTypePaths(Dictionary<string, object> category, OfficialCategoryReference rootCategory, OfficialCategoryReference primaryCategory, Dictionary<string, OfficialCategoryPathInfo> map)
	{
		if (category == null || rootCategory == null)
		{
			return;
		}
		OfficialCategoryReference officialCategoryReference = ToOfficialCategoryReference(category);
		OfficialCategoryReference primaryCategory2 = primaryCategory;
		if (primaryCategory2 == null && !string.Equals(officialCategoryReference.Key, rootCategory.Key, StringComparison.OrdinalIgnoreCase))
		{
			primaryCategory2 = officialCategoryReference;
		}
		object[] array = (category.ContainsKey("achievementTypes") ? (category["achievementTypes"] as object[]) : null);
		if (array != null)
		{
			foreach (string item in from value in array.Select(Convert.ToString)
				where !string.IsNullOrWhiteSpace(value)
				select value)
			{
				map[item] = new OfficialCategoryPathInfo
				{
					RootCategory = rootCategory,
					PrimaryCategory = primaryCategory2 ?? officialCategoryReference,
					LeafCategory = officialCategoryReference
				};
			}
		}
		object[] array2 = (category.ContainsKey("categories") ? (category["categories"] as object[]) : null);
		if (array2 == null)
		{
			return;
		}
		foreach (Dictionary<string, object> item2 in array2.OfType<Dictionary<string, object>>())
		{
			MapOfficialTypePaths(item2, rootCategory, primaryCategory2, map);
		}
	}

	private static OfficialCategoryReference ToOfficialCategoryReference(Dictionary<string, object> category)
	{
		if (category == null)
		{
			return null;
		}
		OfficialCategoryReference officialCategoryReference = new OfficialCategoryReference();
		officialCategoryReference.Id = ParseTrailingInt(GetString(category, "id"));
		officialCategoryReference.Key = GetString(category, "id");
		officialCategoryReference.Name = GetString(category, "name");
		officialCategoryReference.Icon = GetString(category, "icon");
		return officialCategoryReference;
	}

	private void PopulateFilterOptions()
	{
		PopulateCombo(ClassFilterBox, from value in (from row in _ownedCollectionRows
				select row.CardClass into value
				where !string.IsNullOrWhiteSpace(value)
				select value).Distinct()
			orderby value
			select value);
		PopulateCombo(CostFilterBox, (from row in _ownedCollectionRows
			select row.CostGroup into value
			where !string.IsNullOrWhiteSpace(value)
			select value).Distinct().OrderBy(CostSortValue));
		PopulateCombo(TypeFilterBox, from value in (from row in _ownedCollectionRows
				select row.Type into value
				where !string.IsNullOrWhiteSpace(value)
				select value).Distinct()
			orderby value
			select value);
		PopulateCombo(CollectionOwnershipFilterBox, new string[2] { "已拥有", "未拥有" });
		PopulateCombo(CollectionRarityFilterBox, from value in (from row in _ownedCollectionRows
				select row.Rarity into value
				where !string.IsNullOrWhiteSpace(value)
				select value).Distinct()
			orderby value
			select value);
		PopulateCombo(CollectionPremiumFilterBox, new string[4] { "普通", "金卡", "钻石", "异画" });
		PopulateCombo(SetFilterBox, from value in (from row in _ownedCollectionRows
				select row.Set into value
				where !string.IsNullOrWhiteSpace(value)
				select value).Distinct()
			orderby value
			select value);
	}

	private static void PopulateCombo(ComboBox comboBox, IEnumerable<string> values)
	{
		if (comboBox == null)
		{
			return;
		}
		comboBox.Items.Clear();
		comboBox.Items.Add("全部");
		foreach (string value in values)
		{
			comboBox.Items.Add(value);
		}
		comboBox.SelectedIndex = 0;
	}

	private void RefreshCollectionGrid()
	{
		List<OwnedCollectionRow> list = _ownedCollectionRows.Where(MatchesCollectionFilters).ToList();
		int num = list.Count((OwnedCollectionRow row) => row.TotalOwned > 0);
		int num2 = Math.Max(1, (int)Math.Ceiling((double)list.Count / CollectionPageSize));
		if (_collectionPageIndex >= num2)
		{
			_collectionPageIndex = num2 - 1;
		}
		if (_collectionPageIndex < 0)
		{
			_collectionPageIndex = 0;
		}
		List<OwnedCollectionRow> rows = list.Skip(_collectionPageIndex * CollectionPageSize).Take(CollectionPageSize).ToList();
		CollectionGrid.DataSource = BuildCollectionTable(rows);
		CollectionSummaryLabel.Text = "当前筛选卡牌: " + list.Count + " / " + _ownedCollectionRows.Count + "    已拥有: " + num + "    每页 " + CollectionPageSize + " 张";
		CollectionPagerLabel.Text = "第 " + (_collectionPageIndex + 1) + " / " + num2 + " 页";
		CollectionPrevButton.Enabled = _collectionPageIndex > 0;
		CollectionNextButton.Enabled = _collectionPageIndex < num2 - 1;
	}

	private void RefreshSkinGrid()
	{
		List<OwnedCollectionRow> list = _skinCollectionRows.Where(MatchesSkinFilters).ToList();
		int num = list.Count((OwnedCollectionRow row) => row.TotalOwned > 0);
		int num2 = Math.Max(1, (int)Math.Ceiling((double)list.Count / CollectionPageSize));
		if (_skinPageIndex >= num2)
		{
			_skinPageIndex = num2 - 1;
		}
		if (_skinPageIndex < 0)
		{
			_skinPageIndex = 0;
		}
		List<OwnedCollectionRow> rows = list.Skip(_skinPageIndex * CollectionPageSize).Take(CollectionPageSize).ToList();
		SkinGrid.DataSource = BuildCollectionTable(rows);
		SkinSummaryLabel.Text = "当前筛选皮肤: " + list.Count + " / " + _skinCollectionRows.Count + "    已拥有: " + num + "    每页 " + CollectionPageSize + " 张";
		SkinPagerLabel.Text = "第 " + (_skinPageIndex + 1) + " / " + num2 + " 页";
		SkinPrevButton.Enabled = _skinPageIndex > 0;
		SkinNextButton.Enabled = _skinPageIndex < num2 - 1;
	}

	private void ResetCollectionPagingAndRefresh()
	{
		_collectionPageIndex = 0;
		RefreshCollectionGrid();
	}

	private void ResetSkinPagingAndRefresh()
	{
		_skinPageIndex = 0;
		RefreshSkinGrid();
	}

	private void RefreshCompletedGrid()
	{
		if (CompletedGrid != null && CompletedSummaryLabel != null)
		{
			CompletedGrid.DataSource = BuildCompletedTable(_completedAchievementRows);
			CompletedSummaryLabel.Text = "Firestone 已完成成就条目数: " + _completedAchievementRows.Count;
		}
	}

	private void RefreshProgressGrid()
	{
		if (ProgressGrid == null || ProgressSummaryLabel == null)
		{
			return;
		}
		List<AchievementProgressRow> list = _achievementProgressRows;
		string text = ((ProgressCompletionFilterBox != null) ? (ProgressCompletionFilterBox.SelectedItem as string) : null) ?? "全部";
		if (text == "已完成")
		{
			list = _achievementProgressRows.Where((AchievementProgressRow row) => row.Completed).ToList();
		}
		else if (text == "未完成")
		{
			list = _achievementProgressRows.Where((AchievementProgressRow row) => !row.Completed).ToList();
		}
		ProgressGrid.DataSource = BuildProgressTable(list);
		ConfigureAchievementGuideGrid(ProgressGrid);
		ProgressSummaryLabel.Text = "官方进度条目数: " + list.Count + " / " + _achievementProgressRows.Count;
	}

	private void RefreshTrackedAchievementsGrid()
	{
		if (TrackedGrid == null || TrackedSummaryLabel == null)
		{
			return;
		}
		List<TrackedAchievementDisplayRow> list = BuildTrackedAchievementDisplayRows();
		int count = list.Count;
		string text = NormalizeLookupKey((TrackedSearchBox != null) ? TrackedSearchBox.Text : string.Empty);
		if (!string.IsNullOrWhiteSpace(text))
		{
			list = list.Where((TrackedAchievementDisplayRow row) => NormalizeLookupKey(row.Name).Contains(text) || NormalizeLookupKey(row.SourceLabel).Contains(text) || NormalizeLookupKey(row.Requirement).Contains(text) || NormalizeLookupKey(row.ExtraText).Contains(text) || NormalizeLookupKey(row.ProgressText).Contains(text) || NormalizeLookupKey(row.StatusText).Contains(text)).ToList();
		}
		TrackedGrid.DataSource = BuildTrackedAchievementTable(list);
		ConfigureAchievementGuideGrid(TrackedGrid);
		int num = _trackedAchievementLookup.Values.Count((TrackedAchievementEntry entry) => string.Equals(entry.Kind, "official", StringComparison.OrdinalIgnoreCase));
		int num2 = _trackedAchievementLookup.Values.Count((TrackedAchievementEntry entry) => string.Equals(entry.Kind, "progress", StringComparison.OrdinalIgnoreCase));
		int num3 = list.Count((TrackedAchievementDisplayRow row) => row.IsMissingLiveData);
		TrackedSummaryLabel.Text = "已追踪成就: " + list.Count + " / " + count + "    官方分类细分: " + num + "    官方进度明细: " + num2 + (num3 > 0 ? ("    未在当前数据中找到: " + num3) : string.Empty);
		SelectFirstActionableGridRow(TrackedGrid);
		UpdateTrackedActionButtonsState();
	}

	private void UpdateTrackedActionButtonsState()
	{
		if (TrackedGrid == null)
		{
			return;
		}
		DataGridViewRow actionableAchievementGridRow = GetActionableAchievementGridRow(TrackedGrid);
		bool flag = actionableAchievementGridRow != null;
		bool flag2 = !string.IsNullOrWhiteSpace(flag ? GetGridCellText(actionableAchievementGridRow, "攻略") : null);
		if (TrackedOpenGuideButton != null)
		{
			TrackedOpenGuideButton.Enabled = flag && flag2;
			TrackedOpenGuideButton.Text = (flag ? (flag2 ? "打开当前攻略" : "当前行没有攻略") : "打开当前攻略");
		}
		if (TrackedRemoveButton != null)
		{
			TrackedRemoveButton.Enabled = flag;
			TrackedRemoveButton.Text = (flag ? "取消追踪当前行" : "未选择成就");
		}
	}

	private void RefreshProfileGrid()
	{
		if (ProfileGrid != null)
		{
			string selectedProfileCompletionFilter = GetSelectedProfileCompletionFilter();
			_achievementCategoryRows = BuildOfficialAchievementCategoryRowsDetailed(selectedProfileCompletionFilter);
			int num = _achievementCategoryRows.Sum((AchievementCategoryViewRow row) => row.TotalPoints);
			int num2 = _achievementCategoryRows.Sum((AchievementCategoryViewRow row) => row.Points);
			int num3 = _achievementCategoryRows.Sum((AchievementCategoryViewRow row) => row.CompletedCount);
			int num4 = _achievementCategoryRows.Sum((AchievementCategoryViewRow row) => row.TotalCount);
			ProfileGrid.DataSource = null;
			ProfileGrid.AutoGenerateColumns = true;
			ProfileGrid.DataSource = _achievementCategoryRows;
			ConfigureProfileGridColumnsV2();
			if (ProfileSummaryLabel != null)
			{
				ProfileSummaryLabel.Text = "总成就点数：" + num + "    当前点数：" + num2 + "    已完成条目数：" + num3 + "    总条数：" + num4 + ((selectedProfileCompletionFilter == "全部") ? string.Empty : ("    筛选：" + selectedProfileCompletionFilter));
			}
		}
	}

	private void UpdateLoadingStatus(string message)
	{
		if (LoadingStatusLabel == null)
		{
			return;
		}
		LoadingStatusLabel.Text = string.IsNullOrWhiteSpace(message) ? "准备加载数据..." : message;
	}

	private static void WriteStartupTrace(string message)
	{
		try
		{
			string startupTraceLogPath = StartupTraceLogPath;
			File.AppendAllText(startupTraceLogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " + message + Environment.NewLine, Encoding.UTF8);
			TrimStartupTraceLogIfNeeded(startupTraceLogPath);
		}
		catch
		{
		}
	}

	private static void TrimStartupTraceLogIfNeeded(string logPath)
	{
		if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
		{
			return;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(logPath);
			if (fileInfo.Length <= StartupTraceLogMaxBytes)
			{
				return;
			}
			string text = File.ReadAllText(logPath, Encoding.UTF8);
			if (string.IsNullOrEmpty(text) || text.Length <= StartupTraceLogKeepChars)
			{
				return;
			}
			string contents = "[trimmed " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "]" + Environment.NewLine + text.Substring(text.Length - StartupTraceLogKeepChars);
			File.WriteAllText(logPath, contents, Encoding.UTF8);
		}
		catch
		{
		}
	}

	private static string SafeTraceText(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "-" : value;
	}

	private void RefreshLadderClassGrid()
	{
		if (LadderClassGrid != null)
		{
			string selectedLadderClassCompletionFilter = GetSelectedLadderClassCompletionFilter();
			_ladderClassAchievementCategoryRows = BuildClassAchievementCategoryRows(selectedLadderClassCompletionFilter);
			int num3 = _ladderClassAchievementCategoryRows.Sum((AchievementCategoryViewRow row) => row.CompletedCount);
			int num4 = _ladderClassAchievementCategoryRows.Sum((AchievementCategoryViewRow row) => row.TotalCount);
			LadderClassGrid.DataSource = null;
			LadderClassGrid.AutoGenerateColumns = true;
			LadderClassGrid.DataSource = _ladderClassAchievementCategoryRows;
			ConfigureLadderClassGridColumns();
			if (LadderClassSummaryLabel != null)
			{
				LadderClassSummaryLabel.Text = "职业分类：" + _ladderClassAchievementCategoryRows.Count + " 类    已完成条目数：" + num3 + "    总条数：" + num4 + ((selectedLadderClassCompletionFilter == "全部") ? string.Empty : ("    筛选：" + selectedLadderClassCompletionFilter));
			}
		}
	}

	private void HideProfileColumn(string name)
	{
		if (ProfileGrid.Columns.Contains(name))
		{
			ProfileGrid.Columns[name].Visible = false;
		}
	}

	private void SetProfileColumnHeader(string name, string header)
	{
		if (ProfileGrid.Columns.Contains(name))
		{
			ProfileGrid.Columns[name].HeaderText = header;
		}
	}

	private void ConfigureProfileGridColumnsV2()
	{
		if (ProfileGrid != null && ProfileGrid.Columns.Count != 0)
		{
			HideProfileColumn("Mode");
			HideProfileColumn("DetailText");
			HideProfileColumn("AttachedAchievements");
			HideProfileColumn("TotalPoints");
			HideProfileColumn("Points");
			HideProfileColumn("AvailablePoints");
			HideProfileColumn("CompletionRate");
			SetProfileColumnHeader("Name", "分类");
			SetProfileColumnHeader("Key", "标识");
			SetProfileColumnHeader("CompletedCount", "完成数");
			SetProfileColumnHeader("TotalCount", "总数");
			SetProfileColumnHeader("PointsProgress", "点数进度");
			SetProfileColumnHeader("CountCompletionRate", "计数完成率");
			SetProfileColumnHeader("PointCompletionRate", "点数完成率");
			SetProfileColumnHeader("CompletionRateDiff", "差值");
			SetProfileColumnHeader("AttachedCount", "挂接条目");
			SetProfileColumnDisplayIndex("Key", 0);
			SetProfileColumnDisplayIndex("Name", 1);
			SetProfileColumnDisplayIndex("CompletedCount", 2);
			SetProfileColumnDisplayIndex("TotalCount", 3);
			SetProfileColumnDisplayIndex("PointsProgress", 4);
			SetProfileColumnDisplayIndex("AttachedCount", 5);
			SetProfileColumnDisplayIndex("CountCompletionRate", 6);
			SetProfileColumnDisplayIndex("PointCompletionRate", 7);
			SetProfileColumnDisplayIndex("CompletionRateDiff", 8);
			SetGridColumnFillWeight(ProfileGrid, "Key", 50f);
			SetGridColumnFillWeight(ProfileGrid, "Name", 90f);
			SetGridColumnFillWeight(ProfileGrid, "CompletedCount", 70f);
			SetGridColumnFillWeight(ProfileGrid, "TotalCount", 70f);
			SetGridColumnFillWeight(ProfileGrid, "PointsProgress", 110f);
			SetGridColumnFillWeight(ProfileGrid, "AttachedCount", 75f);
			SetGridColumnFillWeight(ProfileGrid, "CountCompletionRate", 85f);
			SetGridColumnFillWeight(ProfileGrid, "PointCompletionRate", 85f);
			SetGridColumnFillWeight(ProfileGrid, "CompletionRateDiff", 70f);
			RebalanceOverviewGridRowHeights(ProfileGrid);
		}
	}

	private void SetProfileColumnDisplayIndex(string propertyName, int displayIndex)
	{
		if (ProfileGrid != null && ProfileGrid.Columns.Contains(propertyName))
		{
			ProfileGrid.Columns[propertyName].DisplayIndex = displayIndex;
		}
	}

	private void ConfigureLadderClassGridColumns()
	{
		if (LadderClassGrid != null && LadderClassGrid.Columns.Count != 0)
		{
			if (LadderClassGrid.Columns.Contains("Mode"))
			{
				LadderClassGrid.Columns["Mode"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("DetailText"))
			{
				LadderClassGrid.Columns["DetailText"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("AttachedAchievements"))
			{
				LadderClassGrid.Columns["AttachedAchievements"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("Name"))
			{
				LadderClassGrid.Columns["Name"].HeaderText = "职业";
			}
			if (LadderClassGrid.Columns.Contains("Key"))
			{
				LadderClassGrid.Columns["Key"].HeaderText = "标识";
			}
			if (LadderClassGrid.Columns.Contains("CompletedCount"))
			{
				LadderClassGrid.Columns["CompletedCount"].HeaderText = "完成数";
			}
			if (LadderClassGrid.Columns.Contains("TotalCount"))
			{
				LadderClassGrid.Columns["TotalCount"].HeaderText = "总数";
			}
			if (LadderClassGrid.Columns.Contains("AttachedCount"))
			{
				LadderClassGrid.Columns["AttachedCount"].HeaderText = "挂接条目";
			}
			if (LadderClassGrid.Columns.Contains("CompletionRate"))
			{
				LadderClassGrid.Columns["CompletionRate"].HeaderText = "完成率";
			}
			if (LadderClassGrid.Columns.Contains("CountCompletionRate"))
			{
				LadderClassGrid.Columns["CountCompletionRate"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("PointsProgress"))
			{
				LadderClassGrid.Columns["PointsProgress"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("PointCompletionRate"))
			{
				LadderClassGrid.Columns["PointCompletionRate"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("CompletionRateDiff"))
			{
				LadderClassGrid.Columns["CompletionRateDiff"].Visible = false;
			}
			SetGridColumnFillWeight(LadderClassGrid, "Key", 48f);
			SetGridColumnFillWeight(LadderClassGrid, "Name", 90f);
			SetGridColumnFillWeight(LadderClassGrid, "CompletedCount", 72f);
			SetGridColumnFillWeight(LadderClassGrid, "TotalCount", 72f);
			SetGridColumnFillWeight(LadderClassGrid, "CompletionRate", 80f);
			SetGridColumnFillWeight(LadderClassGrid, "AttachedCount", 78f);
			RebalanceOverviewGridRowHeights(LadderClassGrid);
			if (LadderClassGrid.Columns.Contains("Points"))
			{
				LadderClassGrid.Columns["Points"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("AvailablePoints"))
			{
				LadderClassGrid.Columns["AvailablePoints"].Visible = false;
			}
			if (LadderClassGrid.Columns.Contains("TotalPoints"))
			{
				LadderClassGrid.Columns["TotalPoints"].Visible = false;
			}
			SetLadderClassColumnDisplayIndex("Key", 0);
			SetLadderClassColumnDisplayIndex("Name", 1);
			SetLadderClassColumnDisplayIndex("CompletedCount", 2);
			SetLadderClassColumnDisplayIndex("TotalCount", 3);
			SetLadderClassColumnDisplayIndex("CompletionRate", 4);
			SetLadderClassColumnDisplayIndex("AttachedCount", 5);
		}
	}

	private void SetLadderClassColumnDisplayIndex(string propertyName, int displayIndex)
	{
		if (LadderClassGrid != null && LadderClassGrid.Columns.Contains(propertyName))
		{
			LadderClassGrid.Columns[propertyName].DisplayIndex = displayIndex;
		}
	}

	private List<AchievementCategoryViewRow> BuildOfficialAchievementCategoryRows()
	{
		return (from row in _profileRows
			orderby row.Id
			select new AchievementCategoryViewRow
			{
				Mode = AchievementCategoryMode.Official,
				Key = row.Id.ToString(CultureInfo.InvariantCulture),
				Name = GetOfficialCategoryDisplayName(row.Id),
				CompletedCount = row.CompletedAchievements,
				TotalCount = row.TotalAchievements,
				Points = row.Points,
				AvailablePoints = row.AvailablePoints,
				DetailText = "官方分类 ID: " + row.Id + Environment.NewLine + "分类名称: " + GetOfficialCategoryDisplayName(row.Id) + Environment.NewLine + "当前点数: " + row.Points + " / " + row.AvailablePoints + Environment.NewLine + "完成数: " + row.CompletedAchievements + " / " + row.TotalAchievements + Environment.NewLine + Environment.NewLine + "说明:" + Environment.NewLine + "1. 这一列来自 Firestone 本地缓存的官方分类汇总。" + Environment.NewLine + "2. 当前磁盘缓存只保存了分类 ID 和汇总数字，没有保存每条成就与官方分类的完整映射。" + Environment.NewLine + "3. 如果后续把 Firestone 运行时的 getAchievementCategories()/getAchievementsInfo() 导出下来，这里可以升级成按官方分类显示具体成就。"
			}).ToList();
	}

	private string GetSelectedProfileCompletionFilter()
	{
		return ((ProfileCompletionFilterBox != null) ? (ProfileCompletionFilterBox.SelectedItem as string) : null) ?? "全部";
	}

	private string GetSelectedLadderClassCompletionFilter()
	{
		return ((LadderClassCompletionFilterBox != null) ? (LadderClassCompletionFilterBox.SelectedItem as string) : null) ?? "全部";
	}

	private static List<OfficialAchievementExportRow> ApplyOfficialAchievementCompletionFilter(IEnumerable<OfficialAchievementExportRow> rows, string filter)
	{
		IEnumerable<OfficialAchievementExportRow> enumerable = rows ?? Enumerable.Empty<OfficialAchievementExportRow>();
		if (filter == "已完成")
		{
			enumerable = enumerable.Where(IsOfficialAchievementCompleted);
		}
		else if (filter == "未完成")
		{
			enumerable = enumerable.Where((OfficialAchievementExportRow row) => !IsOfficialAchievementCompleted(row));
		}
		return enumerable.ToList();
	}

	private static List<AchievementProgressRow> ApplyAchievementProgressCompletionFilter(IEnumerable<AchievementProgressRow> rows, string filter)
	{
		IEnumerable<AchievementProgressRow> enumerable = rows ?? Enumerable.Empty<AchievementProgressRow>();
		if (filter == "已完成")
		{
			enumerable = enumerable.Where((AchievementProgressRow row) => row.Completed);
		}
		else if (filter == "未完成")
		{
			enumerable = enumerable.Where((AchievementProgressRow row) => !row.Completed);
		}
		return enumerable.ToList();
	}

	private List<AchievementCategoryViewRow> BuildOfficialAchievementCategoryRowsDetailed(string completionFilter)
	{
		if (_officialCategoryExportRows != null && _officialCategoryExportRows.Count > 0)
		{
			return (from row in _officialCategoryExportRows
				orderby row.Id
				let filteredAchievements = ApplyOfficialAchievementCompletionFilter(row.Achievements ?? new List<OfficialAchievementExportRow>(), completionFilter)
				where filteredAchievements.Count > 0 || completionFilter == "全部"
				let stats = row.RuntimeStats?.Stats
				select new AchievementCategoryViewRow
				{
					Mode = AchievementCategoryMode.Official,
					Key = row.Id.ToString(CultureInfo.InvariantCulture),
					Name = (row.Name ?? GetOfficialCategoryDisplayName(row.Id)),
					CompletedCount = ((stats != null) ? stats.CompletedAchievements : filteredAchievements.Count(IsOfficialAchievementCompleted)),
					TotalCount = ((stats != null) ? stats.TotalAchievements : filteredAchievements.Count),
					CountCompletionRate = FormatCompletionRate((stats != null) ? stats.CompletedAchievements : filteredAchievements.Count(IsOfficialAchievementCompleted), (stats != null) ? stats.TotalAchievements : filteredAchievements.Count),
					CompletionRate = FormatCompletionRate((stats != null) ? stats.Points : filteredAchievements.Where((OfficialAchievementExportRow item) => IsOfficialAchievementCompleted(item)).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0), (stats != null) ? stats.AvailablePoints : filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
					PointsProgress = FormatProgress((stats != null) ? stats.Points : filteredAchievements.Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0), (stats != null) ? stats.AvailablePoints : filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
					PointCompletionRate = FormatCompletionRate((stats != null) ? stats.Points : filteredAchievements.Where((OfficialAchievementExportRow item) => IsOfficialAchievementCompleted(item)).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0), (stats != null) ? stats.AvailablePoints : filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
					CompletionRateDiff = string.Format(CultureInfo.InvariantCulture, "{0:+0.#;-0.#;0}%", (((stats != null) ? ((stats.AvailablePoints > 0) ? ((double)stats.Points * 100.0 / (double)stats.AvailablePoints) : 0.0) : ((filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0) > 0) ? ((double)filteredAchievements.Where((OfficialAchievementExportRow item) => IsOfficialAchievementCompleted(item)).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0) * 100.0 / (double)filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)) : 0.0)) - ((stats != null) ? ((stats.TotalAchievements > 0) ? ((double)stats.CompletedAchievements * 100.0 / (double)stats.TotalAchievements) : 0.0) : ((filteredAchievements.Count > 0) ? ((double)filteredAchievements.Count(IsOfficialAchievementCompleted) * 100.0 / (double)filteredAchievements.Count) : 0.0)))),
					TotalPoints = ((stats != null) ? stats.AvailablePoints : filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
					Points = ((stats != null) ? stats.Points : filteredAchievements.Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
					AvailablePoints = ((stats != null) ? stats.AvailablePoints : filteredAchievements.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
					AttachedCount = filteredAchievements.Count,
					AttachedAchievements = filteredAchievements,
					DetailText = BuildOfficialCategoryDetailTextV2(row, filteredAchievements, completionFilter)
				}).ToList();
		}
		return _profileRows.OrderBy((ProfileAchievementSummary row) => row.Id).Select(delegate(ProfileAchievementSummary row)
		{
			List<AchievementProgressRow> list = (from item in ApplyAchievementProgressCompletionFilter(_achievementProgressRows, completionFilter)
				where GuessOfficialCategory(item) == row.Id
				orderby item.Completed descending, item.ProgressRatio descending, item.AchievementId
				select item).Take(80).ToList();
			return new AchievementCategoryViewRow
			{
				Mode = AchievementCategoryMode.Official,
				Key = row.Id.ToString(CultureInfo.InvariantCulture),
				Name = GetOfficialCategoryDisplayName(row.Id),
				CompletedCount = list.Count((AchievementProgressRow item) => item.Completed),
				TotalCount = list.Count,
				CountCompletionRate = FormatCompletionRate(list.Count((AchievementProgressRow item) => item.Completed), list.Count),
				CompletionRate = FormatCompletionRate(list.Count((AchievementProgressRow item) => item.Completed), list.Count),
				PointsProgress = FormatProgress(row.Points, row.AvailablePoints),
				PointCompletionRate = FormatCompletionRate(row.Points, row.AvailablePoints),
				CompletionRateDiff = string.Format(CultureInfo.InvariantCulture, "{0:+0.#;-0.#;0}%", ((row.AvailablePoints > 0) ? ((double)row.Points * 100.0 / (double)row.AvailablePoints) : 0.0) - ((list.Count > 0) ? ((double)list.Count((AchievementProgressRow item) => item.Completed) * 100.0 / (double)list.Count) : 0.0)),
				TotalPoints = row.AvailablePoints,
				Points = row.Points,
				AvailablePoints = row.AvailablePoints,
				AttachedCount = list.Count,
				AttachedAchievements = list,
				DetailText = "官方分类 ID: " + row.Id + Environment.NewLine + "分类名称: " + GetOfficialCategoryDisplayName(row.Id) + Environment.NewLine + "当前筛选: " + completionFilter + Environment.NewLine + "已挂接具体成就: " + list.Count + Environment.NewLine + Environment.NewLine + "说明:" + Environment.NewLine + "1. 分类名称使用炉石官方根分类名称。" + Environment.NewLine + "2. 当前结果按顶部完成状态筛选。" + Environment.NewLine + "3. 下方具体成就是根据成就名称、描述和模式关键词做的 best-effort 挂接，不是 Firestone 直接落盘的官方逐条分类映射。" + Environment.NewLine + Environment.NewLine + "具体成就:" + Environment.NewLine + ((list.Count == 0) ? "- 当前没有可挂接的具体成就条目。" : string.Join(Environment.NewLine, list.Select((AchievementProgressRow item) => string.Format(CultureInfo.InvariantCulture, "- [{0}] {1} ({2}/{3}){4}", item.AchievementId, string.IsNullOrWhiteSpace(item.Name) ? "(未命名成就)" : item.Name, item.Progress, item.MaxProgress, item.Completed ? " [已完成]" : string.Empty))))
			};
		}).Where((AchievementCategoryViewRow row) => row.AttachedCount > 0 || completionFilter == "全部").ToList();
	}

	private List<AchievementCategoryViewRow> BuildOfficialPrimaryAchievementCategoryRowsDetailed()
	{
		List<OfficialAchievementExportRow> list = (_officialCategoryExportRows ?? new List<OfficialCategoryExportRow>()).SelectMany((OfficialCategoryExportRow row) => row.Achievements ?? new List<OfficialAchievementExportRow>()).ToList();
		if (list.Count == 0)
		{
			return new List<AchievementCategoryViewRow>();
		}
		return (from @group in list.Select((OfficialAchievementExportRow item) => new
			{
				Achievement = item,
				Path = GetOfficialCategoryPath(item)
			}).GroupBy(item => (item.Path != null && item.Path.PrimaryCategory != null) ? item.Path.PrimaryCategory.Key : "unknown", StringComparer.OrdinalIgnoreCase)
			orderby (@group.First().Path != null && @group.First().Path.RootCategory != null) ? @group.First().Path.RootCategory.Id : int.MaxValue
			select @group).ThenBy(group => (group.First().Path != null && group.First().Path.PrimaryCategory != null) ? group.First().Path.PrimaryCategory.Name : "未分类", StringComparer.OrdinalIgnoreCase).Select(group =>
		{
			var anon = group.First();
			OfficialCategoryPathInfo officialCategoryPathInfo = anon.Path ?? new OfficialCategoryPathInfo();
			List<OfficialAchievementExportRow> list2 = (from item in @group
				select item.Achievement into item
				orderby IsOfficialAchievementCompleted(item) descending, item.Reference != null && item.Reference.Root descending, (item.Reference != null) ? item.Reference.HsSectionId : int.MaxValue, (item.Reference != null) ? item.Reference.Priority : int.MaxValue, item.AchievementId
				select item).ToList();
			int completedCount = list2.Count(IsOfficialAchievementCompleted);
			int availablePoints = list2.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0);
			int points = list2.Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0);
			string name = ((officialCategoryPathInfo.RootCategory != null) ? officialCategoryPathInfo.RootCategory.Name : "未分类") + " / " + ((officialCategoryPathInfo.PrimaryCategory != null) ? officialCategoryPathInfo.PrimaryCategory.Name : "未分类");
			return new AchievementCategoryViewRow
			{
				Mode = AchievementCategoryMode.OfficialPrimary,
				Key = ((officialCategoryPathInfo.PrimaryCategory != null) ? officialCategoryPathInfo.PrimaryCategory.Key : group.Key),
				Name = name,
				CompletedCount = completedCount,
				TotalCount = list2.Count,
				Points = points,
				AvailablePoints = availablePoints,
				AttachedCount = list2.Count,
				AttachedAchievements = list2,
				DetailText = BuildOfficialPrimaryCategoryDetailText(officialCategoryPathInfo, list2)
			};
		}).ToList();
	}

	private List<AchievementCategoryViewRow> BuildClassAchievementCategoryRows(string completionFilter)
	{
		List<OfficialAchievementExportRow> source = (_officialCategoryExportRows ?? new List<OfficialCategoryExportRow>())
			.SelectMany((OfficialCategoryExportRow row) => row.Achievements ?? new List<OfficialAchievementExportRow>())
			.Where((OfficialAchievementExportRow item) => string.Equals((item.RootCategory != null) ? item.RootCategory.Name : string.Empty, "游戏", StringComparison.OrdinalIgnoreCase))
			.ToList();
		List<OfficialAchievementExportRow> list = ApplyOfficialAchievementCompletionFilter(source, completionFilter);
		List<ClassAchievementEntry> source2 = (from item in list
			from className in GuessOfficialAchievementClasses(item)
			where !string.IsNullOrWhiteSpace(className)
			select new ClassAchievementEntry
			{
				ClassName = className,
				Achievement = item
			}).ToList();
		List<AchievementCategoryViewRow> list2 = (from @group in (from @group in source2.GroupBy((ClassAchievementEntry item) => item.ClassName, StringComparer.OrdinalIgnoreCase)
				orderby ClassSortValue(@group.Key)
				select @group).ThenBy((IGrouping<string, ClassAchievementEntry> @group) => @group.Key, StringComparer.OrdinalIgnoreCase)
			select new AchievementCategoryViewRow
			{
				Mode = AchievementCategoryMode.Class,
				Key = @group.Key,
				Name = @group.Key,
				CompletedCount = @group.Select((ClassAchievementEntry entry) => entry.Achievement).Count(IsOfficialAchievementCompleted),
				TotalCount = @group.Count(),
				Points = @group.Select((ClassAchievementEntry entry) => entry.Achievement).Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0),
				AvailablePoints = @group.Select((ClassAchievementEntry entry) => entry.Achievement).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0),
				CompletionRate = FormatCompletionRate(@group.Select((ClassAchievementEntry entry) => entry.Achievement).Count(IsOfficialAchievementCompleted), @group.Count()),
				PointsProgress = FormatProgress(@group.Select((ClassAchievementEntry entry) => entry.Achievement).Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0), @group.Select((ClassAchievementEntry entry) => entry.Achievement).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0)),
				TotalPoints = @group.Select((ClassAchievementEntry entry) => entry.Achievement).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0),
				AttachedCount = @group.Count(),
				AttachedAchievements = (from entry in @group
					let item = entry.Achievement
					orderby IsOfficialAchievementCompleted(item) descending, item.Progress descending, item.AchievementId
					select item).ToList(),
				DetailText = "职业分类（游戏成就）: " + @group.Key + Environment.NewLine + "当前筛选: " + completionFilter + Environment.NewLine + "已完成: " + @group.Select((ClassAchievementEntry entry) => entry.Achievement).Count(IsOfficialAchievementCompleted) + Environment.NewLine + "已读取到的成就条目: " + @group.Count() + Environment.NewLine + Environment.NewLine + "明细:" + Environment.NewLine + string.Join(Environment.NewLine, from item in (from entry in @group
						let item = entry.Achievement
						orderby IsOfficialAchievementCompleted(item) descending, item.Progress descending, item.AchievementId
						select item).Take(80)
					select string.Format(CultureInfo.InvariantCulture, "- [{0}] {1} ({2})", item.AchievementId, GetOfficialAchievementDisplayName(item), FormatProgress(item.Progress, (item.Reference != null) ? item.Reference.Quota : 0)))
			}).ToList();
		List<OfficialAchievementExportRow> list3 = (from entry in source2
			select entry.Achievement).Distinct().OrderByDescending(IsOfficialAchievementCompleted).ThenByDescending((OfficialAchievementExportRow item) => item.Progress).ThenBy((OfficialAchievementExportRow item) => item.AchievementId).ToList();
		if (list3.Count > 0 || completionFilter == "全部")
		{
			int num = list3.Count(IsOfficialAchievementCompleted);
			int num2 = list3.Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0);
			int num3 = list3.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0);
			list2.Add(new AchievementCategoryViewRow
			{
				Mode = AchievementCategoryMode.Class,
				Key = "全部",
				Name = "全部",
				CompletedCount = num,
				TotalCount = list3.Count,
				Points = num2,
				AvailablePoints = num3,
				CompletionRate = FormatCompletionRate(num, list3.Count),
				PointsProgress = FormatProgress(num2, num3),
				TotalPoints = num3,
				AttachedCount = list3.Count,
				AttachedAchievements = list3,
				DetailText = "职业分类（游戏成就）: 全部" + Environment.NewLine + "当前筛选: " + completionFilter + Environment.NewLine + "已完成: " + num + Environment.NewLine + "已读取到的成就条目: " + list3.Count + Environment.NewLine + Environment.NewLine + "说明:" + Environment.NewLine + "1. 这一行会把所有职业和中立成就合在一起显示。" + Environment.NewLine + "2. 下方可直接查看全部明细、收藏和攻略。" + Environment.NewLine + Environment.NewLine + "明细:" + Environment.NewLine + ((list3.Count == 0) ? "- 当前没有可显示的成就条目。" : string.Join(Environment.NewLine, list3.Take(80).Select((OfficialAchievementExportRow item) => string.Format(CultureInfo.InvariantCulture, "- [{0}] {1} ({2})", item.AchievementId, GetOfficialAchievementDisplayName(item), FormatProgress(item.Progress, (item.Reference != null) ? item.Reference.Quota : 0)))))
			});
		}
		return list2;
	}

	private static string BuildOfficialCategoryDetailText(OfficialCategoryExportRow row)
	{
		List<string> list = new List<string>();
		list.Add("官方分类 ID: " + row.Id);
		list.Add("分类名称: " + (row.Name ?? string.Empty));
		list.Add("图标: " + (row.Icon ?? string.Empty));
		list.Add("当前点数: " + ((row.RuntimeStats != null && row.RuntimeStats.Stats != null) ? row.RuntimeStats.Stats.Points : 0) + " / " + ((row.RuntimeStats != null && row.RuntimeStats.Stats != null) ? row.RuntimeStats.Stats.AvailablePoints : 0));
		list.Add("完成数: " + ((row.RuntimeStats != null && row.RuntimeStats.Stats != null) ? row.RuntimeStats.Stats.CompletedAchievements : 0) + " / " + ((row.RuntimeStats != null && row.RuntimeStats.Stats != null) ? row.RuntimeStats.Stats.TotalAchievements : row.AchievementCount));
		list.Add("挂接条目: " + ((row.Achievements != null) ? row.Achievements.Count : 0));
		list.Add("");
		list.Add("具体成就:");
		List<string> list2 = list;
		List<OfficialAchievementExportRow> list3 = row.Achievements ?? new List<OfficialAchievementExportRow>();
		if (list3.Count == 0)
		{
			list2.Add("- 当前没有读取到这一类的具体成就。");
		}
		else
		{
			foreach (OfficialAchievementExportRow item in list3.Take(120))
			{
				string text = ((item.Reference != null) ? FirstNonEmpty(item.Reference.DisplayName, new string[2]
				{
					item.Reference.Name,
					item.Reference.Text
				}) : null);
				text = (string.IsNullOrWhiteSpace(text) ? "(未命名成就)" : text);
				int num = ((item.Reference != null) ? item.Reference.Quota : 0);
				string text2 = ((num > 0) ? (item.Progress + "/" + num) : item.Progress.ToString(CultureInfo.InvariantCulture));
				string text3 = (IsOfficialAchievementCompleted(item) ? "已完成" : "进行中");
				string text4 = ((item.Reference != null) ? item.Reference.HsSectionId.ToString(CultureInfo.InvariantCulture) : "-");
				list2.Add("- [" + item.AchievementId + "] " + text + " (" + text2 + ", " + text3 + ", section " + text4 + ")");
			}
			if (list3.Count > 120)
			{
				list2.Add("- ... 其余 " + (list3.Count - 120) + " 条未展开");
			}
		}
		return string.Join(Environment.NewLine, list2);
	}

	private static string BuildOfficialCategoryDetailTextV2(OfficialCategoryExportRow row, List<OfficialAchievementExportRow> filteredAchievements, string completionFilter)
	{
		List<OfficialAchievementExportRow> list = filteredAchievements ?? new List<OfficialAchievementExportRow>();
		List<string> list2 = new List<string>();
		list2.Add("官方分类 ID: " + ((row != null) ? row.Id.ToString(CultureInfo.InvariantCulture) : "-"));
		list2.Add("分类名称: " + ((row != null) ? (row.Name ?? string.Empty) : string.Empty));
		list2.Add("图标: " + ((row != null) ? (row.Icon ?? string.Empty) : string.Empty));
		list2.Add("当前官方根分类共 5 类：1 / 2 / 3 / 4 / 6");
		list2.Add("当前筛选: " + (string.IsNullOrWhiteSpace(completionFilter) ? "全部" : completionFilter));
		list2.Add("当前点数: " + list.Where(IsOfficialAchievementCompleted).Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0) + " / " + list.Sum((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Points : 0));
		list2.Add("完成数: " + list.Count(IsOfficialAchievementCompleted) + " / " + list.Count);
		list2.Add("挂接条目: " + list.Count);
		list2.Add("");
		list2.Add("说明:");
		list2.Add("1. 顶部表格按官方根分类汇总。");
		list2.Add("2. 打开细分后，可以继续查看一级分类和具体成就。");
		list2.Add("3. 下方具体成就来自 Firestone 运行时导出与官方分类配置的挂接结果。");
		list2.Add("");
		list2.Add("具体成就：");
		List<string> list3 = list2;
		if (list.Count == 0)
		{
			list3.Add("- 当前没有读取到这一类的具体成就。");
		}
		else
		{
			foreach (OfficialAchievementExportRow item in list.Take(40))
			{
				string text = ((item.Reference != null) ? FirstNonEmpty(item.Reference.DisplayName, new string[2]
				{
					item.Reference.Name,
					item.Reference.Text
				}) : null);
				text = (string.IsNullOrWhiteSpace(text) ? "(未命名成就)" : text);
				int num = ((item.Reference != null) ? item.Reference.Quota : 0);
				string text2 = ((num > 0) ? (item.Progress + "/" + num) : item.Progress.ToString(CultureInfo.InvariantCulture));
				string text3 = (IsOfficialAchievementCompleted(item) ? "已完成" : "进行中");
				string text4 = ((item.PrimaryCategory != null) ? item.PrimaryCategory.Name : "未分类");
				string text5 = ((item.LeafCategory != null) ? item.LeafCategory.Name : "未分类");
				list3.Add("- [" + item.AchievementId + "] " + text + "（" + text2 + "，" + text3 + "，" + text4 + " / " + text5 + "）");
			}
			if (list.Count > 40)
			{
				list3.Add("- ... 其余 " + (list.Count - 40) + " 条请在下方明细区查看。");
			}
		}
		return string.Join(Environment.NewLine, list3);
	}

	private OfficialCategoryPathInfo GetOfficialCategoryPath(OfficialAchievementExportRow row)
	{
		if (row == null)
		{
			return null;
		}
		string text = ((row.Reference != null) ? row.Reference.Type : null);
		if (!string.IsNullOrWhiteSpace(text) && _officialTypePathMap != null && _officialTypePathMap.TryGetValue(text, out var value))
		{
			return value;
		}
		if (row.RootCategory != null)
		{
			OfficialCategoryPathInfo officialCategoryPathInfo = new OfficialCategoryPathInfo();
			officialCategoryPathInfo.RootCategory = row.RootCategory;
			officialCategoryPathInfo.PrimaryCategory = row.PrimaryCategory ?? row.RootCategory;
			officialCategoryPathInfo.LeafCategory = row.LeafCategory ?? row.PrimaryCategory ?? row.RootCategory;
			return officialCategoryPathInfo;
		}
		return null;
	}

	private static string BuildOfficialPrimaryCategoryDetailText(OfficialCategoryPathInfo path, IList<OfficialAchievementExportRow> achievements)
	{
		List<string> list = new List<string>();
		list.Add("根分类: " + ((path != null && path.RootCategory != null) ? path.RootCategory.Name : "未分类"));
		list.Add("一级分类: " + ((path != null && path.PrimaryCategory != null) ? path.PrimaryCategory.Name : "未分类"));
		list.Add("挂接条目: " + (achievements?.Count ?? 0));
		list.Add("已完成: " + (achievements?.Count(IsOfficialAchievementCompleted) ?? 0));
		list.Add("");
		List<string> list2 = list;
		if (achievements == null || achievements.Count == 0)
		{
			list2.Add("当前没有读取到这一类的具体成就。");
			return string.Join(Environment.NewLine, list2);
		}
		list2.Add("细分类统计:");
		foreach (IGrouping<string, OfficialAchievementExportRow> item in (from item in achievements
			group item by (item.LeafCategory != null) ? item.LeafCategory.Name : "未分类" into @group
			orderby @group.Count() descending
			select @group).ThenBy((IGrouping<string, OfficialAchievementExportRow> group) => group.Key, StringComparer.OrdinalIgnoreCase))
		{
			list2.Add("- " + item.Key + ": " + item.Count() + " 条");
		}
		return string.Join(Environment.NewLine, list2);
	}

	private static bool IsOfficialAchievementCompleted(OfficialAchievementExportRow row)
	{
		return row != null && (row.Status == 2 || row.Status == 4);
	}

	private AchievementCategoryViewRow GetSelectedProfileRowLegacy()
	{
		return (ProfileGrid != null && ProfileGrid.CurrentRow != null) ? (ProfileGrid.CurrentRow.DataBoundItem as AchievementCategoryViewRow) : null;
	}

	private void OpenSelectedProfileDetailsLegacy()
	{
		AchievementCategoryViewRow selectedProfileRowLegacy = GetSelectedProfileRowLegacy();
		if (selectedProfileRowLegacy != null)
		{
			Form form = new Form();
			form.Text = "分类详情 - " + (selectedProfileRowLegacy.Name ?? selectedProfileRowLegacy.Key ?? string.Empty);
			form.StartPosition = FormStartPosition.CenterParent;
			form.Size = new Size(900, 700);
			form.MinimumSize = new Size(700, 500);
			ApplyDialogFormStyle(form);
			Form form2 = form;
			TextBox textBox = CreateReadOnlyDetailsBox();
			textBox.ScrollBars = ScrollBars.Both;
			textBox.WordWrap = false;
			textBox.BorderStyle = BorderStyle.None;
			textBox.Text = selectedProfileRowLegacy.DetailText ?? string.Empty;
			TextBox value = textBox;
			form2.Controls.Add(CreateDialogShell(form.Text, "查看当前分类的原始详情文本。", value));
			ApplyDialogControlTheme(form2);
			form2.Show(this);
		}
	}

	private void UpdateProfileDetailsFromSelection()
	{
		if (ProfileDetailsBox != null)
		{
			AchievementCategoryViewRow selectedProfileRow = GetSelectedProfileRow();
			ProfileDetailsBox.Text = ((selectedProfileRow != null) ? ((selectedProfileRow.DetailText ?? string.Empty) + Environment.NewLine + Environment.NewLine + "双击行、按回车，或点上方“打开细分”可查看细分窗口。") : "请选择一行分类以查看详情。");
		}
	}

	private void UpdateLadderClassDetailsFromSelection()
	{
		if (LadderClassDetailsGrid != null)
		{
			AchievementCategoryViewRow selectedLadderClassRow = GetSelectedLadderClassRow();
			List<OfficialAchievementExportRow> list = selectedLadderClassRow?.AttachedAchievements as List<OfficialAchievementExportRow> ?? (selectedLadderClassRow?.AttachedAchievements as IList<OfficialAchievementExportRow>)?.ToList() ?? new List<OfficialAchievementExportRow>();
			LadderClassDetailsGrid.DataSource = null;
			LadderClassDetailsGrid.AutoGenerateColumns = true;
			LadderClassDetailsGrid.Tag = list.Cast<object>().ToList();
			LadderClassDetailsGrid.DataSource = BuildOfficialAchievementTableV2(list);
			ConfigureAchievementGuideGrid(LadderClassDetailsGrid);
			if (LadderClassDetailsSummaryLabel != null)
			{
				LadderClassDetailsSummaryLabel.Text = ((selectedLadderClassRow != null) ? ("当前职业: " + (selectedLadderClassRow.Name ?? "全部") + "    成就条目: " + list.Count + "    可直接点“收藏”或“攻略”") : "请选择一行职业分类以查看并操作具体成就。");
			}
		}
	}

	private AchievementCategoryViewRow GetSelectedProfileRow()
	{
		if (ProfileGrid == null)
		{
			return null;
		}
		if (ProfileGrid.SelectedRows != null && ProfileGrid.SelectedRows.Count > 0)
		{
			return ProfileGrid.SelectedRows[0].DataBoundItem as AchievementCategoryViewRow;
		}
		return (ProfileGrid.CurrentRow != null) ? (ProfileGrid.CurrentRow.DataBoundItem as AchievementCategoryViewRow) : null;
	}

	private AchievementCategoryViewRow GetSelectedLadderClassRow()
	{
		if (LadderClassGrid == null)
		{
			return null;
		}
		if (LadderClassGrid.SelectedRows != null && LadderClassGrid.SelectedRows.Count > 0)
		{
			return LadderClassGrid.SelectedRows[0].DataBoundItem as AchievementCategoryViewRow;
		}
		return (LadderClassGrid.CurrentRow != null) ? (LadderClassGrid.CurrentRow.DataBoundItem as AchievementCategoryViewRow) : null;
	}

	private void OpenSelectedProfileDetails()
	{
		OpenAchievementCategoryDetails(GetSelectedProfileRow());
	}

	private void OpenSelectedLadderClassDetails()
	{
		OpenAchievementCategoryDetails(GetSelectedLadderClassRow());
	}

	private void OpenAchievementCategoryDetails(AchievementCategoryViewRow selectedProfileRow)
	{
		if (selectedProfileRow == null)
		{
			return;
		}
		Form form = new Form();
		form.Text = "成就分类细分 - " + (string.IsNullOrWhiteSpace(selectedProfileRow.Name) ? selectedProfileRow.Key : selectedProfileRow.Name);
		form.StartPosition = FormStartPosition.CenterParent;
		form.Size = new Size(1120, 760);
		form.MinimumSize = new Size(900, 620);
		form.KeyPreview = true;
		ApplyDialogFormStyle(form);
		form.KeyDown += delegate(object sender, KeyEventArgs args)
		{
			if (args.KeyCode == Keys.Escape)
			{
				args.Handled = true;
				args.SuppressKeyPress = true;
				form.Close();
			}
		};
		Button button = new Button
		{
			DialogResult = DialogResult.Cancel,
			Size = new Size(1, 1),
			Location = new Point(-100, -100),
			TabStop = false,
			Visible = false
		};
		form.CancelButton = button;
		IList<AchievementProgressRow> list = selectedProfileRow.AttachedAchievements as IList<AchievementProgressRow>;
		IList<OfficialAchievementExportRow> list2 = selectedProfileRow.AttachedAchievements as IList<OfficialAchievementExportRow>;
		Control control2;
		if (list != null && list.Count > 0)
		{
			control2 = BuildPagedDetailPanel(list, BuildProgressTable);
		}
		else if (list2 != null && list2.Count > 0)
		{
			control2 = ((selectedProfileRow.Mode == AchievementCategoryMode.Official) ? BuildOfficialCategoryHierarchyDetailPanel(list2) : ((selectedProfileRow.Mode == AchievementCategoryMode.OfficialPrimary) ? BuildOfficialPrimaryDetailPanel(list2) : BuildPagedDetailPanel(list2, BuildOfficialAchievementTableV2)));
		}
		else
		{
			TextBox textBox2 = CreateReadOnlyDetailsBox();
			textBox2.BorderStyle = BorderStyle.None;
			textBox2.Text = "当前分类没有可展示的细分成就条目。";
			control2 = textBox2;
		}
		control2.Dock = DockStyle.Fill;
		string text = ((list != null && list.Count > 0) ? ("进度条目 " + list.Count.ToString(CultureInfo.InvariantCulture)) : ((list2 != null && list2.Count > 0) ? ("成就条目 " + list2.Count.ToString(CultureInfo.InvariantCulture)) : "当前没有可展示的细分条目"));
		form.Controls.Add(button);
		form.Controls.Add(CreateDialogShell(form.Text, text, control2));
		ApplyDialogControlTheme(form);
		form.Show(this);
	}

	private void ConfigureAchievementGuideGrid(DataGridView grid)
	{
		if (grid == null || grid.Columns.Count == 0)
		{
			return;
		}
		HideGridColumnIfExists(grid, "__GuideName");
		HideGridColumnIfExists(grid, "__GuideRequirement");
		HideGridColumnIfExists(grid, "__GuideClass");
		HideGridColumnIfExists(grid, "__TrackKey");
		HideGridColumnIfExists(grid, "__TrackKind");
		HideGridColumnIfExists(grid, "__TrackId");
		HideGridColumnIfExists(grid, "__TrackName");
		HideGridColumnIfExists(grid, "__TrackRequirement");
		HideGridColumnIfExists(grid, "__TrackClass");
		HideGridColumnIfExists(grid, "__TrackSource");
		HideGridColumnIfExists(grid, "__TrackProgressText");
		HideGridColumnIfExists(grid, "__TrackStatusText");
		HideGridColumnIfExists(grid, "__TrackExtraText");
		if (grid.Columns.Contains("收藏"))
		{
			EnsureButtonColumn(grid, "收藏");
			DataGridViewColumn dataGridViewColumn = grid.Columns["收藏"];
			dataGridViewColumn.DisplayIndex = 0;
			dataGridViewColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			dataGridViewColumn.Width = 86;
			dataGridViewColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
			dataGridViewColumn.DefaultCellStyle.ForeColor = Color.DarkGreen;
			dataGridViewColumn.DefaultCellStyle.SelectionForeColor = Color.DarkGreen;
		}
		if (grid.Columns.Contains("攻略"))
		{
			DataGridViewColumn dataGridViewColumn2 = grid.Columns["攻略"];
			dataGridViewColumn2.DisplayIndex = grid.Columns.Contains("收藏") ? 1 : 0;
			dataGridViewColumn2.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			dataGridViewColumn2.Width = 72;
			dataGridViewColumn2.SortMode = DataGridViewColumnSortMode.NotSortable;
			dataGridViewColumn2.DefaultCellStyle.ForeColor = Color.RoyalBlue;
			dataGridViewColumn2.DefaultCellStyle.SelectionForeColor = Color.RoyalBlue;
		}
		grid.CellClick -= AchievementGuideGrid_CellClick;
		grid.CellClick += AchievementGuideGrid_CellClick;
		grid.CellContentClick -= AchievementGuideGrid_CellContentClick;
		grid.CellContentClick += AchievementGuideGrid_CellContentClick;
		grid.CellDoubleClick -= AchievementGuideGrid_CellDoubleClick;
		grid.CellDoubleClick += AchievementGuideGrid_CellDoubleClick;
	}

	private static void HideGridColumnIfExists(DataGridView grid, string columnName)
	{
		if (grid != null && !string.IsNullOrWhiteSpace(columnName) && grid.Columns.Contains(columnName))
		{
			grid.Columns[columnName].Visible = false;
		}
	}

	private static void EnsureButtonColumn(DataGridView grid, string columnName)
	{
		if (grid == null || string.IsNullOrWhiteSpace(columnName) || !grid.Columns.Contains(columnName) || grid.Columns[columnName] is DataGridViewButtonColumn)
		{
			return;
		}
		DataGridViewColumn dataGridViewColumn = grid.Columns[columnName];
		int index = dataGridViewColumn.Index;
		int displayIndex = dataGridViewColumn.DisplayIndex;
		string dataPropertyName = dataGridViewColumn.DataPropertyName;
		string headerText = dataGridViewColumn.HeaderText;
		bool visible = dataGridViewColumn.Visible;
		grid.Columns.Remove(dataGridViewColumn);
		DataGridViewButtonColumn dataGridViewButtonColumn = new DataGridViewButtonColumn
		{
			Name = columnName,
			HeaderText = headerText,
			DataPropertyName = dataPropertyName,
			UseColumnTextForButtonValue = false,
			ReadOnly = true,
			FlatStyle = FlatStyle.Flat
		};
		grid.Columns.Insert(index, dataGridViewButtonColumn);
		dataGridViewButtonColumn.Visible = visible;
		dataGridViewButtonColumn.DisplayIndex = displayIndex;
		dataGridViewButtonColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		dataGridViewButtonColumn.DefaultCellStyle.BackColor = ThemeSurfaceTint;
		dataGridViewButtonColumn.DefaultCellStyle.SelectionBackColor = ThemeAccentSoft;
		dataGridViewButtonColumn.DefaultCellStyle.SelectionForeColor = ThemeTextStrong;
		dataGridViewButtonColumn.DefaultCellStyle.Padding = new Padding(4, 1, 4, 1);
		dataGridViewButtonColumn.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10.2f, FontStyle.Bold);
	}

	private void AchievementGuideGrid_CellClick(object sender, DataGridViewCellEventArgs e)
	{
		DataGridView grid = sender as DataGridView;
		if (grid != null && e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns.Count > e.ColumnIndex)
		{
			string name = grid.Columns[e.ColumnIndex].Name;
			if (string.Equals(name, "收藏", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "攻略", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		if (TryToggleTrackedAchievementFromGrid(sender as DataGridView, e.RowIndex, e.ColumnIndex))
		{
			return;
		}
		TryOpenAchievementGuideFromGrid(sender as DataGridView, e.RowIndex, e.ColumnIndex);
	}

	private void AchievementGuideGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
	{
		if (TryToggleTrackedAchievementFromGrid(sender as DataGridView, e.RowIndex, e.ColumnIndex))
		{
			return;
		}
		TryOpenAchievementGuideFromGrid(sender as DataGridView, e.RowIndex, e.ColumnIndex);
	}

	private void AchievementGuideGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
	{
		DataGridView grid = sender as DataGridView;
		if (grid != null && e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns.Count > e.ColumnIndex && string.Equals(grid.Columns[e.ColumnIndex].Name, "收藏", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		TryOpenAchievementGuideFromRow(sender as DataGridView, e.RowIndex);
	}

	private bool TryToggleTrackedAchievementFromGrid(DataGridView grid, int rowIndex, int columnIndex)
	{
		if (grid == null || rowIndex < 0 || columnIndex < 0 || !grid.Columns.Contains("收藏"))
		{
			return false;
		}
		DataGridViewColumn dataGridViewColumn = grid.Columns[columnIndex];
		if (dataGridViewColumn == null || !string.Equals(dataGridViewColumn.Name, "收藏", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		DataGridViewRow dataGridViewRow = grid.Rows[rowIndex];
		if (dataGridViewRow == null)
		{
			return false;
		}
		string gridCellText = GetGridCellText(dataGridViewRow, "__TrackKey");
		if (string.IsNullOrWhiteSpace(gridCellText))
		{
			bool handled = TryToggleTrackedAchievementFromItem(GetBoundAchievementItemFromGrid(grid, rowIndex), GetAchievementGuideDialogOwner(grid));
			if (handled)
			{
				UpdateTrackedButtonCellValue(dataGridViewRow, !IsAchievementTracked(GetGridCellText(dataGridViewRow, "__TrackKey")));
			}
			return handled;
		}
		bool flag = IsAchievementTracked(gridCellText);
		TrackedAchievementEntry trackedAchievementEntry = flag ? null : CreateTrackedAchievementEntryFromGridRow(dataGridViewRow);
		TrackedAchievementEntry value;
		_trackedAchievementLookup.TryGetValue(gridCellText, out value);
		if (flag)
		{
			_trackedAchievementLookup.Remove(gridCellText);
		}
		else if (trackedAchievementEntry != null)
		{
			_trackedAchievementLookup[gridCellText] = trackedAchievementEntry;
		}
		try
		{
			SaveTrackedAchievements();
		}
		catch (Exception ex)
		{
			if (flag && value != null)
			{
				_trackedAchievementLookup[gridCellText] = value;
			}
			else if (!flag)
			{
				_trackedAchievementLookup.Remove(gridCellText);
			}
			ShowAppError(GetAchievementGuideDialogOwner(grid), "炉石成就攻略", "更新追踪成就失败:\r\n" + ex.Message);
			return true;
		}
		UpdateTrackedButtonCellValue(dataGridViewRow, !flag);
		RefreshTrackedAchievementsGrid();
		return true;
	}

	private static void UpdateTrackedButtonCellValue(DataGridViewRow row, bool isTracked)
	{
		if (row != null && row.DataGridView != null && row.DataGridView.Columns.Contains("收藏"))
		{
			row.Cells["收藏"].Value = (isTracked ? "已收藏" : "收藏");
		}
	}

	private void TryOpenAchievementGuideFromGrid(DataGridView grid, int rowIndex, int columnIndex)
	{
		if (grid == null || rowIndex < 0 || columnIndex < 0 || !grid.Columns.Contains("攻略"))
		{
			return;
		}
		DataGridViewColumn dataGridViewColumn = grid.Columns[columnIndex];
		if (dataGridViewColumn == null || !string.Equals(dataGridViewColumn.Name, "攻略", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		TryOpenAchievementGuideFromRow(grid, rowIndex);
	}

	private void TryOpenAchievementGuideFromRow(DataGridView grid, int rowIndex)
	{
		if (grid == null || rowIndex < 0 || !grid.Columns.Contains("攻略"))
		{
			return;
		}
		DataGridViewRow dataGridViewRow = grid.Rows[rowIndex];
		if (dataGridViewRow == null)
		{
			return;
		}
		string text = GetGridCellText(dataGridViewRow, "攻略");
		if (string.IsNullOrWhiteSpace(text))
		{
			TryOpenAchievementGuideFromItem(GetBoundAchievementItemFromGrid(grid, rowIndex), GetAchievementGuideDialogOwner(grid));
			return;
		}
		OpenAchievementGuideDialog(GetGridCellText(dataGridViewRow, "__GuideName"), GetGridCellText(dataGridViewRow, "__GuideRequirement"), GetGridCellText(dataGridViewRow, "__GuideClass"), GetAchievementGuideDialogOwner(grid));
	}

	private IWin32Window GetAchievementGuideDialogOwner(Control source)
	{
		Form form = ((source != null) ? source.FindForm() : null);
		if (form != null)
		{
			return form;
		}
		Form activeForm = Form.ActiveForm;
		if (activeForm != null)
		{
			return activeForm;
		}
		return this;
	}

	private static string GetGridCellText(DataGridViewRow row, string columnName)
	{
		if (row == null || string.IsNullOrWhiteSpace(columnName) || !row.DataGridView.Columns.Contains(columnName))
		{
			return string.Empty;
		}
		object value = row.Cells[columnName].Value;
		return (value == null) ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
	}

	private static DataGridViewRow GetActionableAchievementGridRow(DataGridView grid)
	{
		if (grid == null)
		{
			return null;
		}
		if (grid.SelectedRows != null && grid.SelectedRows.Count > 0)
		{
			return grid.SelectedRows[0];
		}
		if (grid.CurrentRow != null)
		{
			return grid.CurrentRow;
		}
		return (grid.Rows.Count > 0) ? grid.Rows[0] : null;
	}

	private static void SelectFirstActionableGridRow(DataGridView grid)
	{
		DataGridViewRow actionableAchievementGridRow = GetActionableAchievementGridRow(grid);
		if (grid == null || actionableAchievementGridRow == null)
		{
			return;
		}
		DataGridViewCell firstVisibleCell = GetFirstVisibleCell(actionableAchievementGridRow);
		if (firstVisibleCell == null)
		{
			return;
		}
		grid.ClearSelection();
		actionableAchievementGridRow.Selected = true;
		grid.CurrentCell = firstVisibleCell;
	}

	private static object GetBoundAchievementItemFromGrid(DataGridView grid, int rowIndex)
	{
		if (grid == null || rowIndex < 0)
		{
			return null;
		}
		IList<object> list = grid.Tag as IList<object>;
		if (list == null || rowIndex >= list.Count)
		{
			return null;
		}
		return list[rowIndex];
	}

	private TrackedAchievementEntry CreateTrackedAchievementEntryFromOfficialAchievement(OfficialAchievementExportRow row)
	{
		if (row == null)
		{
			return null;
		}
		string text = GetOfficialAchievementDisplayName(row);
		string officialAchievementRequirementText = GetOfficialAchievementRequirementText(row.Reference);
		string text2 = GuessOfficialAchievementClass(row);
		string text3 = string.Join("/", new string[3]
		{
			row.RootCategory?.Name,
			row.PrimaryCategory?.Name,
			row.LeafCategory?.Name
		}.Where((string part) => !string.IsNullOrWhiteSpace(part)));
		return new TrackedAchievementEntry
		{
			Key = GetOfficialAchievementTrackKey(row),
			Kind = "official",
			TrackId = GetOfficialAchievementStableId(row),
			Name = text,
			Requirement = officialAchievementRequirementText,
			AchievementClass = text2,
			SourceLabel = "官方分类细分",
			ProgressText = FormatProgress(row.Progress, row.Reference?.Quota ?? 0),
			StatusText = (IsOfficialAchievementCompleted(row) ? "已完成" : "进行中"),
			ExtraText = text3,
			TrackedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
		};
	}

	private TrackedAchievementEntry CreateTrackedAchievementEntryFromProgressAchievement(AchievementProgressRow row)
	{
		if (row == null)
		{
			return null;
		}
		string text = CleanMultiline(row.Description);
		string text2 = GuessAchievementClass(row);
		string text3 = string.Join(" / ", new string[2] { row.Type, row.Trigger }.Where((string part) => !string.IsNullOrWhiteSpace(part)));
		return new TrackedAchievementEntry
		{
			Key = GetProgressAchievementTrackKey(row),
			Kind = "progress",
			TrackId = row.AchievementId,
			Name = row.Name,
			Requirement = text,
			AchievementClass = text2,
			SourceLabel = "官方进度明细",
			ProgressText = FormatProgress(row.Progress, row.MaxProgress),
			StatusText = (row.Completed ? "已完成" : "进行中"),
			ExtraText = text3,
			TrackedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
		};
	}

	private bool ToggleTrackedAchievementEntry(TrackedAchievementEntry entry, IWin32Window owner)
	{
		if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
		{
			return false;
		}
		bool flag = IsAchievementTracked(entry.Key);
		TrackedAchievementEntry value;
		_trackedAchievementLookup.TryGetValue(entry.Key, out value);
		if (flag)
		{
			_trackedAchievementLookup.Remove(entry.Key);
		}
		else
		{
			_trackedAchievementLookup[entry.Key] = entry;
		}
		try
		{
			SaveTrackedAchievements();
		}
		catch (Exception ex)
		{
			if (flag && value != null)
			{
				_trackedAchievementLookup[entry.Key] = value;
			}
			else if (flag)
			{
				_trackedAchievementLookup.Remove(entry.Key);
			}
			else if (value != null)
			{
				_trackedAchievementLookup[entry.Key] = value;
			}
			else
			{
				_trackedAchievementLookup.Remove(entry.Key);
			}
			ShowAppError(owner ?? this, "炉石成就攻略", "更新追踪成就失败:\r\n" + ex.Message);
			return true;
		}
		RefreshTrackedAchievementsGrid();
		return true;
	}

	private bool TryToggleTrackedAchievementFromItem(object item, IWin32Window owner)
	{
		if (item is OfficialAchievementExportRow officialAchievementExportRow)
		{
			return ToggleTrackedAchievementEntry(CreateTrackedAchievementEntryFromOfficialAchievement(officialAchievementExportRow), owner);
		}
		if (item is AchievementProgressRow achievementProgressRow)
		{
			return ToggleTrackedAchievementEntry(CreateTrackedAchievementEntryFromProgressAchievement(achievementProgressRow), owner);
		}
		return false;
	}

	private bool TryOpenAchievementGuideFromItem(object item, IWin32Window owner)
	{
		if (item is OfficialAchievementExportRow officialAchievementExportRow)
		{
			string officialAchievementDisplayName = GetOfficialAchievementDisplayName(officialAchievementExportRow);
			string officialAchievementRequirementText = GetOfficialAchievementRequirementText(officialAchievementExportRow.Reference);
			string text = GuessOfficialAchievementClass(officialAchievementExportRow);
			if (!HasAchievementGuides(officialAchievementDisplayName, officialAchievementRequirementText, text))
			{
				return false;
			}
			OpenAchievementGuideDialog(officialAchievementDisplayName, officialAchievementRequirementText, text, owner);
			return true;
		}
		if (item is AchievementProgressRow achievementProgressRow)
		{
			string text2 = CleanMultiline(achievementProgressRow.Description);
			string text3 = GuessAchievementClass(achievementProgressRow);
			if (!HasAchievementGuides(achievementProgressRow.Name, text2, text3))
			{
				return false;
			}
			OpenAchievementGuideDialog(achievementProgressRow.Name, text2, text3, owner);
			return true;
		}
		return false;
	}

	private void LoadTrackedAchievements()
	{
		try
		{
			List<TrackedAchievementEntry> list = LoadJson<List<TrackedAchievementEntry>>(_trackedAchievementsPath) ?? new List<TrackedAchievementEntry>();
			_trackedAchievementLookup = list.Where((TrackedAchievementEntry entry) => entry != null && !string.IsNullOrWhiteSpace(entry.Key)).GroupBy((TrackedAchievementEntry entry) => entry.Key, StringComparer.OrdinalIgnoreCase).Select((IGrouping<string, TrackedAchievementEntry> group) => group.OrderByDescending((TrackedAchievementEntry entry) => entry.TrackedAt ?? string.Empty, StringComparer.OrdinalIgnoreCase).First()).ToDictionary((TrackedAchievementEntry entry) => entry.Key, (TrackedAchievementEntry entry) => entry, StringComparer.OrdinalIgnoreCase);
		}
		catch
		{
			_trackedAchievementLookup = new Dictionary<string, TrackedAchievementEntry>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private void SaveTrackedAchievements()
	{
		string directoryName = Path.GetDirectoryName(_trackedAchievementsPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		List<TrackedAchievementEntry> obj = _trackedAchievementLookup.Values.OrderByDescending((TrackedAchievementEntry entry) => entry.TrackedAt ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy((TrackedAchievementEntry entry) => entry.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
		File.WriteAllText(_trackedAchievementsPath, _serializer.Serialize(obj));
	}

	private bool IsAchievementTracked(string trackKey)
	{
		return !string.IsNullOrWhiteSpace(trackKey) && _trackedAchievementLookup.ContainsKey(trackKey);
	}

	private static string GetProgressAchievementTrackKey(AchievementProgressRow row)
	{
		return BuildTrackedAchievementKey("progress", row?.AchievementId, row?.Name, CleanMultiline(row?.Description));
	}

	private static string GetOfficialAchievementTrackKey(OfficialAchievementExportRow row)
	{
		return BuildTrackedAchievementKey("official", GetOfficialAchievementStableId(row), GetOfficialAchievementDisplayName(row), GetOfficialAchievementRequirementText(row?.Reference));
	}

	private static string BuildAchievementGuideVersionLabel(AchievementGuideRow guide)
	{
		if (guide == null)
		{
			return "攻略";
		}
		string text = (guide.guide_slot > 1) ? ("攻略" + guide.guide_slot.ToString(CultureInfo.InvariantCulture)) : "攻略";
		string text2 = string.IsNullOrWhiteSpace(guide.date) ? "-" : guide.date;
		string text3 = string.IsNullOrWhiteSpace(guide.title) ? "(未命名帖子)" : guide.title;
		return text + " | " + text2 + " | " + text3;
	}

	private List<MultiGuideManagementRow> BuildMultiGuideManagementRows()
	{
		List<MultiGuideManagementRow> list = new List<MultiGuideManagementRow>();
		if (_achievementGuideRows == null || _achievementGuideRows.Count == 0)
		{
			return list;
		}
		foreach (IGrouping<string, AchievementGuideRow> item in _achievementGuideRows.Where((AchievementGuideRow row) => row != null && !string.IsNullOrWhiteSpace(row.achievement_name)).GroupBy((AchievementGuideRow row) => BuildMultiGuideManagementGroupKey(row), StringComparer.OrdinalIgnoreCase))
		{
			List<AchievementGuideRow> list2 = item.GroupBy((AchievementGuideRow row) => BuildAchievementGuideVersionSignature(row), StringComparer.OrdinalIgnoreCase).Select((IGrouping<string, AchievementGuideRow> group) => group.First()).ToList();
			if (list2.Count < 2)
			{
				continue;
			}
			AchievementGuideRow achievementGuideRow = list2.FirstOrDefault((AchievementGuideRow row) => row != null && !string.IsNullOrWhiteSpace(row.achievement_name));
			if (achievementGuideRow == null)
			{
				continue;
			}
			List<AchievementGuideRow> list3 = RankAchievementGuides(list2, achievementGuideRow.achievement_name);
			AchievementGuideRow achievementGuideRow2 = list3.OrderByDescending((AchievementGuideRow row) => ParseGuideDate(row.date)).ThenByDescending((AchievementGuideRow row) => row.guide_slot).FirstOrDefault() ?? list3[0];
			string achievementGuideCategoryLabel = BuildAchievementGuideCategoryLabel(list3);
			list.Add(new MultiGuideManagementRow
			{
				GroupKey = item.Key,
				AchievementName = achievementGuideRow.achievement_name,
				AchievementClass = achievementGuideCategoryLabel,
				GuideClassFilter = BuildMultiGuideManagementClassFilter(list3),
				VersionCount = list3.Count,
				VersionList = string.Join(" / ", list3.Select((AchievementGuideRow row) => BuildAchievementGuideVersionLabel(row))),
				LatestDateText = (string.IsNullOrWhiteSpace(achievementGuideRow2.date) ? "-" : achievementGuideRow2.date),
				LatestEditor = GetAchievementGuideEditorLabel(achievementGuideRow2),
				PreviewText = BuildMultiGuideManagementPreviewText(achievementGuideRow.achievement_name, achievementGuideCategoryLabel, list3)
			});
		}
		return list.OrderByDescending((MultiGuideManagementRow row) => row.VersionCount).ThenByDescending((MultiGuideManagementRow row) => ParseGuideDate(row.LatestDateText)).ThenBy((MultiGuideManagementRow row) => row.AchievementName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string BuildMultiGuideManagementGroupKey(AchievementGuideRow guide)
	{
		if (guide == null)
		{
			return string.Empty;
		}
		return string.Join("|", new string[3]
		{
			NormalizeLookupKey(guide.achievement_name),
			NormalizeLookupKey(guide.category),
			NormalizeLookupKey(guide.sub_category)
		});
	}

	private static string BuildAchievementGuideVersionSignature(AchievementGuideRow guide)
	{
		if (guide == null)
		{
			return string.Empty;
		}
		return string.Join("|", new string[10]
		{
			NormalizeLookupKey(guide.achievement_name),
			CleanMultiline(guide.title),
			CleanMultiline(guide.date),
			CleanMultiline(guide.series),
			CleanMultiline(guide.category),
			CleanMultiline(guide.sub_category),
			CleanMultiline(guide.idea),
			CleanMultiline(guide.recommended_deck_codes),
			CleanMultiline(guide.source_url),
			guide.guide_slot.ToString(CultureInfo.InvariantCulture) + "|" + CleanMultiline(guide.editor_id)
		});
	}

	private static string BuildAchievementGuideCategoryLabel(IEnumerable<AchievementGuideRow> guides)
	{
		AchievementGuideRow achievementGuideRow = (guides ?? Enumerable.Empty<AchievementGuideRow>()).FirstOrDefault((AchievementGuideRow row) => row != null && (!string.IsNullOrWhiteSpace(row.category) || !string.IsNullOrWhiteSpace(row.sub_category)));
		if (achievementGuideRow == null)
		{
			return "未分类";
		}
		string text = CleanMultiline(achievementGuideRow.category);
		string text2 = CleanMultiline(achievementGuideRow.sub_category);
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
		{
			return text + " / " + text2;
		}
		return string.IsNullOrWhiteSpace(text) ? (string.IsNullOrWhiteSpace(text2) ? "未分类" : text2) : text;
	}

	private static string BuildMultiGuideManagementClassFilter(IEnumerable<AchievementGuideRow> guides)
	{
		AchievementGuideRow achievementGuideRow = (guides ?? Enumerable.Empty<AchievementGuideRow>()).FirstOrDefault((AchievementGuideRow row) => row != null && !string.IsNullOrWhiteSpace(row.category));
		return (achievementGuideRow != null && !string.IsNullOrWhiteSpace(achievementGuideRow.category)) ? achievementGuideRow.category : BuildAchievementGuideCategoryLabel(guides);
	}

	private static string GetAchievementGuideEditorLabel(AchievementGuideRow guide)
	{
		return string.IsNullOrWhiteSpace(guide?.editor_id) ? "历史导入" : guide.editor_id.Trim();
	}

	private static string BuildMultiGuideManagementPreviewText(string achievementName, string achievementClass, IEnumerable<AchievementGuideRow> guides)
	{
		List<AchievementGuideRow> list = (guides ?? Enumerable.Empty<AchievementGuideRow>()).Where((AchievementGuideRow row) => row != null).ToList();
		if (list.Count == 0)
		{
			return "当前没有可预览的多份攻略。";
		}
		List<string> list2 = new List<string>
		{
			"成就名称: " + (string.IsNullOrWhiteSpace(achievementName) ? "(未命名成就)" : achievementName),
			"分类: " + (string.IsNullOrWhiteSpace(achievementClass) ? "未分类" : achievementClass),
			"攻略版本数: " + list.Count.ToString(CultureInfo.InvariantCulture),
			"建议操作: 双击当前行，或点击上方“管理当前行攻略”。",
			string.Empty
		};
		foreach (AchievementGuideRow item in list)
		{
			list2.Add("【" + BuildAchievementGuideVersionLabel(item) + "】");
			list2.Add("投稿人: " + GetAchievementGuideEditorLabel(item));
			list2.Add("日期: " + (string.IsNullOrWhiteSpace(item.date) ? "-" : item.date));
			list2.Add("具体要求: " + (string.IsNullOrWhiteSpace(item.requirement) ? "-" : item.requirement));
			list2.Add("来源链接: " + (string.IsNullOrWhiteSpace(item.source_url) ? "-" : item.source_url));
			list2.Add("卡组代码: " + (string.IsNullOrWhiteSpace(item.recommended_deck_codes) ? "-" : item.recommended_deck_codes));
			list2.Add(string.Empty);
			list2.Add("思路:");
			list2.Add(string.IsNullOrWhiteSpace(item.idea) ? "-" : item.idea);
			list2.Add(string.Empty);
		}
		return string.Join(Environment.NewLine, list2).TrimEnd();
	}

	private static bool MultiGuideManagementMatchesFilter(MultiGuideManagementRow row, string keyword)
	{
		if (row == null)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return true;
		}
		return new string[5]
		{
			row.AchievementName,
			row.AchievementClass,
			row.VersionList,
			row.LatestEditor,
			row.PreviewText
		}.Any((string value) => NormalizeLookupKey(value).Contains(keyword));
	}

	private static void ConfigureMultiGuideManagementGridColumns(DataGridView grid)
	{
		if (grid == null || grid.Columns.Count == 0)
		{
			return;
		}
		if (grid.Columns.Contains("名称"))
		{
			grid.Columns["名称"].HeaderText = "成就名称";
			grid.Columns["名称"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			grid.Columns["名称"].Width = 220;
		}
		if (grid.Columns.Contains("分类"))
		{
			grid.Columns["分类"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			grid.Columns["分类"].Width = 150;
		}
		if (grid.Columns.Contains("版本数"))
		{
			grid.Columns["版本数"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			grid.Columns["版本数"].Width = 72;
		}
		if (grid.Columns.Contains("版本列表"))
		{
			grid.Columns["版本列表"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
		}
		if (grid.Columns.Contains("最近更新"))
		{
			grid.Columns["最近更新"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			grid.Columns["最近更新"].Width = 96;
		}
		if (grid.Columns.Contains("最近投稿人"))
		{
			grid.Columns["最近投稿人"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
			grid.Columns["最近投稿人"].Width = 120;
		}
	}

	private void BeginReloadDataSoon()
	{
		if (IsDisposed)
		{
			return;
		}
		BeginInvoke((Action)(async delegate
		{
			await ReloadDataSafeAsync();
		}));
	}

	private static string BuildTrackedAchievementKey(string kind, string id, string name, string requirement)
	{
		string text = NormalizeTrackedKeyPart(id);
		if (string.IsNullOrWhiteSpace(text))
		{
			string text2 = NormalizeTrackedKeyPart(name);
			string text3 = NormalizeTrackedKeyPart(requirement);
			text = string.IsNullOrWhiteSpace(text2) ? text3 : (string.IsNullOrWhiteSpace(text3) ? text2 : (text2 + "|" + text3));
		}
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(kind))
		{
			return string.Empty;
		}
		return kind.Trim().ToLowerInvariant() + ":" + text;
	}

	private static string NormalizeTrackedKeyPart(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		return Regex.Replace(CleanMultiline(value).ToLowerInvariant(), "\\s+", " ").Trim();
	}

	private static string GetOfficialAchievementStableId(OfficialAchievementExportRow row)
	{
		if (row?.Reference != null)
		{
			if (row.Reference.HsAchievementId > 0)
			{
				return row.Reference.HsAchievementId.ToString(CultureInfo.InvariantCulture);
			}
			if (!string.IsNullOrWhiteSpace(row.Reference.Id))
			{
				return row.Reference.Id;
			}
		}
		return (row != null && row.AchievementId > 0) ? row.AchievementId.ToString(CultureInfo.InvariantCulture) : string.Empty;
	}

	private TrackedAchievementEntry CreateTrackedAchievementEntryFromGridRow(DataGridViewRow row)
	{
		if (row == null)
		{
			return null;
		}
		return new TrackedAchievementEntry
		{
			Key = GetGridCellText(row, "__TrackKey"),
			Kind = GetGridCellText(row, "__TrackKind"),
			TrackId = GetGridCellText(row, "__TrackId"),
			Name = GetGridCellText(row, "__TrackName"),
			Requirement = GetGridCellText(row, "__TrackRequirement"),
			AchievementClass = GetGridCellText(row, "__TrackClass"),
			SourceLabel = GetGridCellText(row, "__TrackSource"),
			ProgressText = GetGridCellText(row, "__TrackProgressText"),
			StatusText = GetGridCellText(row, "__TrackStatusText"),
			ExtraText = GetGridCellText(row, "__TrackExtraText"),
			TrackedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
		};
	}

	private List<TrackedAchievementDisplayRow> BuildTrackedAchievementDisplayRows()
	{
		return _trackedAchievementLookup.Values.OrderByDescending((TrackedAchievementEntry entry) => entry.TrackedAt ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy((TrackedAchievementEntry entry) => entry.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).Select(BuildTrackedAchievementDisplayRow).Where((TrackedAchievementDisplayRow row) => row != null).ToList();
	}

	private TrackedAchievementDisplayRow BuildTrackedAchievementDisplayRow(TrackedAchievementEntry entry)
	{
		if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
		{
			return null;
		}
		if (string.Equals(entry.Kind, "progress", StringComparison.OrdinalIgnoreCase))
		{
			AchievementProgressRow achievementProgressRow = _achievementProgressRows.FirstOrDefault((AchievementProgressRow row) => string.Equals(GetProgressAchievementTrackKey(row), entry.Key, StringComparison.OrdinalIgnoreCase));
			if (achievementProgressRow != null)
			{
				string text = CleanMultiline(achievementProgressRow.Description);
				string text2 = GuessAchievementClass(achievementProgressRow);
				return CreateTrackedAchievementDisplayRow(entry.Key, entry.Kind, achievementProgressRow.AchievementId, achievementProgressRow.Name, text, text2, "官方进度明细", FormatProgress(achievementProgressRow.Progress, achievementProgressRow.MaxProgress), achievementProgressRow.Completed ? "已完成" : "进行中", string.Join(" / ", new string[2] { achievementProgressRow.Type, achievementProgressRow.Trigger }.Where((string part) => !string.IsNullOrWhiteSpace(part))), entry.TrackedAt, HasAchievementGuides(achievementProgressRow.Name, text, text2), isMissingLiveData: false);
			}
		}
		else if (string.Equals(entry.Kind, "official", StringComparison.OrdinalIgnoreCase))
		{
			OfficialAchievementExportRow officialAchievementExportRow = _officialCategoryExportRows.SelectMany((OfficialCategoryExportRow category) => category.Achievements ?? new List<OfficialAchievementExportRow>()).FirstOrDefault((OfficialAchievementExportRow row) => string.Equals(GetOfficialAchievementTrackKey(row), entry.Key, StringComparison.OrdinalIgnoreCase));
			if (officialAchievementExportRow != null)
			{
				string officialAchievementDisplayName = GetOfficialAchievementDisplayName(officialAchievementExportRow);
				string officialAchievementRequirementText = GetOfficialAchievementRequirementText(officialAchievementExportRow.Reference);
				string text3 = GuessOfficialAchievementClass(officialAchievementExportRow);
				string text4 = string.Join("/", new string[3]
				{
					officialAchievementExportRow.RootCategory?.Name,
					officialAchievementExportRow.PrimaryCategory?.Name,
					officialAchievementExportRow.LeafCategory?.Name
				}.Where((string part) => !string.IsNullOrWhiteSpace(part)));
				return CreateTrackedAchievementDisplayRow(entry.Key, entry.Kind, GetOfficialAchievementStableId(officialAchievementExportRow), officialAchievementDisplayName, officialAchievementRequirementText, text3, "官方分类细分", FormatProgress(officialAchievementExportRow.Progress, officialAchievementExportRow.Reference?.Quota ?? 0), IsOfficialAchievementCompleted(officialAchievementExportRow) ? "已完成" : "进行中", text4, entry.TrackedAt, HasAchievementGuides(officialAchievementDisplayName, officialAchievementRequirementText, text3), isMissingLiveData: false);
			}
		}
		string text5 = string.IsNullOrWhiteSpace(entry.Name) ? "(未命名成就)" : entry.Name;
		string text6 = entry.Requirement ?? string.Empty;
		string text7 = string.IsNullOrWhiteSpace(entry.AchievementClass) ? "中立" : entry.AchievementClass;
		return CreateTrackedAchievementDisplayRow(entry.Key, entry.Kind, entry.TrackId, text5, text6, text7, string.IsNullOrWhiteSpace(entry.SourceLabel) ? "已失配" : (entry.SourceLabel + "（当前未匹配）"), entry.ProgressText, string.IsNullOrWhiteSpace(entry.StatusText) ? "未知" : entry.StatusText, entry.ExtraText, entry.TrackedAt, HasAchievementGuides(text5, text6, text7), isMissingLiveData: true);
	}

	private static TrackedAchievementDisplayRow CreateTrackedAchievementDisplayRow(string trackKey, string trackKind, string trackId, string name, string requirement, string trackClass, string sourceLabel, string progressText, string statusText, string extraText, string trackedAt, bool hasGuide, bool isMissingLiveData)
	{
		return new TrackedAchievementDisplayRow
		{
			TrackKey = trackKey,
			TrackKind = trackKind,
			TrackId = trackId,
			Name = string.IsNullOrWhiteSpace(name) ? "(未命名成就)" : name,
			Requirement = requirement ?? string.Empty,
			TrackClass = string.IsNullOrWhiteSpace(trackClass) ? "中立" : trackClass,
			SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "未知来源" : sourceLabel,
			ProgressText = string.IsNullOrWhiteSpace(progressText) ? "-" : progressText,
			StatusText = string.IsNullOrWhiteSpace(statusText) ? "-" : statusText,
			ExtraText = extraText ?? string.Empty,
			TrackedAtText = string.IsNullOrWhiteSpace(trackedAt) ? "-" : trackedAt,
			HasGuide = hasGuide,
			GuideName = string.IsNullOrWhiteSpace(name) ? "(未命名成就)" : name,
			GuideRequirement = requirement ?? string.Empty,
			GuideClass = string.IsNullOrWhiteSpace(trackClass) ? "中立" : trackClass,
			IsMissingLiveData = isMissingLiveData
		};
	}

	private void OpenAchievementGuideDialog(string achievementName, string requirement, string achievementClass, IWin32Window owner)
	{
		List<AchievementGuideRow> list = FindAchievementGuides(achievementName, requirement, achievementClass);
		using Form form = new Form();
		form.Text = "成就攻略 - " + (string.IsNullOrWhiteSpace(achievementName) ? "未命名成就" : achievementName);
		form.StartPosition = FormStartPosition.CenterParent;
		form.Size = new Size(980, 880);
		form.MinimumSize = new Size(860, 720);
		form.KeyPreview = true;
		ApplyDialogFormStyle(form);
		form.KeyDown += delegate(object sender, KeyEventArgs args)
		{
			if (args.KeyCode == Keys.Escape)
			{
				args.Handled = true;
				args.SuppressKeyPress = true;
				form.Close();
			}
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Padding = new Padding(0),
			BackColor = ThemeCanvas
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel = CreateHorizontalFlowPanel(new Padding(0, 2, 0, 0), wrapContents: true);
		flowLayoutPanel.BackColor = ThemeSurface;
		ComboBox comboBox = null;
		Button button = new Button
		{
			AutoSize = true,
			Text = "复制卡组"
		};
		Button button4 = new Button
		{
			AutoSize = true,
			Text = "打开链接"
		};
		Button button12 = new Button
		{
			AutoSize = true,
			Text = "切换攻略"
		};
		if (list.Count > 0)
		{
			flowLayoutPanel.Controls.Add(MakeCaption("多攻略"));
			comboBox = CreateDropDownListComboBox(420);
			foreach (AchievementGuideRow item in list)
			{
				comboBox.Items.Add(BuildAchievementGuideVersionLabel(item));
			}
			comboBox.SelectedIndex = 0;
			flowLayoutPanel.Controls.Add(comboBox);
			flowLayoutPanel.Controls.Add(button12);
		}
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(button4);
		Button button3 = new Button
		{
			DialogResult = DialogResult.Cancel,
			Size = new Size(1, 1),
			Location = new Point(-100, -100),
			TabStop = false,
			Visible = false
		};
		form.CancelButton = button3;
		Label linkTitleLabel = new Label
		{
			AutoSize = true,
			Text = "链接",
			Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
			ForeColor = ThemeTextStrong,
			Margin = new Padding(0, 0, 0, 8)
		};
		LinkLabel linkLabel = new LinkLabel
		{
			AutoSize = true,
			MaximumSize = new Size(860, 0),
			Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular),
			LinkColor = ThemeAccent,
			ActiveLinkColor = Color.FromArgb(29, 78, 216),
			VisitedLinkColor = ThemeAccent,
			Margin = new Padding(0, 0, 0, 12)
		};
		Label guideBodyTitleLabel = new Label
		{
			AutoSize = true,
			Text = "正文",
			Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
			ForeColor = ThemeTextStrong,
			Margin = new Padding(0, 4, 0, 8)
		};
		Label guideBodyLabel = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(860, 0),
			Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular),
			ForeColor = ThemeTextStrong,
			Margin = new Padding(0, 0, 0, 14)
		};
		Label deckTitleLabel = new Label
		{
			AutoSize = true,
			Text = "卡组代码",
			Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
			ForeColor = ThemeTextStrong,
			Margin = new Padding(0, 0, 0, 8)
		};
		Label deckValueLabel = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(860, 0),
			Font = new Font("Consolas", 10.5f, FontStyle.Regular),
			ForeColor = ThemeTextStrong,
			Margin = new Padding(0, 0, 0, 14)
		};
		TableLayoutPanel tableLayoutPanel2 = CreateSurfaceLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize));
		tableLayoutPanel2.Controls.Add(linkTitleLabel, 0, 0);
		tableLayoutPanel2.Controls.Add(linkLabel, 0, 1);
		tableLayoutPanel2.Controls.Add(guideBodyTitleLabel, 0, 2);
		tableLayoutPanel2.Controls.Add(guideBodyLabel, 0, 3);
		tableLayoutPanel2.Controls.Add(deckTitleLabel, 0, 4);
		tableLayoutPanel2.Controls.Add(deckValueLabel, 0, 5);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			BackColor = ThemeSurface
		};
		tableLayoutPanel2.AutoSize = true;
		tableLayoutPanel2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		panel.Controls.Add(tableLayoutPanel2);
		Control control3 = CreateTitledCard("攻略内容", panel, new Padding(0, 0, 0, 12), new Padding(16, 16, 16, 16));
		AchievementGuideRow achievementGuideRow = (list.Count > 0) ? list[0] : null;
		Action refreshGuideView = delegate
		{
			string text = achievementGuideRow?.source_url ?? string.Empty;
			List<string> list2 = SplitAchievementGuideDeckCodes(achievementGuideRow?.recommended_deck_codes);
			linkLabel.Text = (string.IsNullOrWhiteSpace(text) ? "无" : text);
			linkLabel.Links.Clear();
			if (!string.IsNullOrWhiteSpace(text))
			{
				linkLabel.Links.Add(0, text.Length, text);
			}
			guideBodyLabel.Text = ((achievementGuideRow == null) ? "当前还没有已审核攻略。" : (string.IsNullOrWhiteSpace(achievementGuideRow.idea) ? "-" : achievementGuideRow.idea));
			deckValueLabel.Text = ((list2.Count == 0) ? "-" : string.Join(Environment.NewLine + Environment.NewLine, list2));
			button.Enabled = list2.Count > 0;
			button4.Enabled = !string.IsNullOrWhiteSpace(text);
		};
		if (comboBox != null)
		{
			comboBox.SelectedIndexChanged += delegate
			{
				button12.Enabled = comboBox.SelectedIndex >= 0 && comboBox.SelectedIndex < list.Count;
			};
			button12.Click += delegate
			{
				int selectedIndex = comboBox.SelectedIndex;
				if (selectedIndex >= 0 && selectedIndex < list.Count)
				{
					achievementGuideRow = list[selectedIndex];
					refreshGuideView();
				}
			};
			button12.Enabled = comboBox.SelectedIndex >= 0 && comboBox.SelectedIndex < list.Count;
		}
		else
		{
			button12.Enabled = false;
		}
		linkLabel.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs args)
		{
			string text8 = (args.Link != null && args.Link.LinkData is string) ? (args.Link.LinkData as string) : achievementGuideRow?.source_url;
			OpenExternalUrl(text8);
		};
		button4.Click += delegate
		{
			OpenExternalUrl(achievementGuideRow?.source_url);
		};
		button.Click += delegate
		{
			string text9 = SplitAchievementGuideDeckCodes(achievementGuideRow?.recommended_deck_codes).FirstOrDefault() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(text9) || text9 == "(无推荐卡组代码)")
			{
				return;
			}
			try
			{
				Clipboard.SetText(text9);
			}
			catch (Exception ex)
			{
				ShowAppError(this, "成就攻略", "复制卡组代码失败:\r\n" + ex.Message);
			}
		};
		refreshGuideView();
		tableLayoutPanel.Controls.Add(CreateCardHost(flowLayoutPanel, new Padding(0, 0, 0, 12), new Padding(16, 14, 16, 14)), 0, 0);
		tableLayoutPanel.Controls.Add(control3, 0, 1);
		form.Controls.Add(button3);
		form.Controls.Add(tableLayoutPanel);
		ApplyDialogControlTheme(form);
		ApplyComboBoxStyle(comboBox);
		ApplySecondaryButtonStyle(button4);
		ApplySecondaryButtonStyle(button);
		form.ShowDialog(owner ?? this);
	}

	private static List<string> SplitAchievementGuideDeckCodes(string value)
	{
		return (from item in (value ?? string.Empty).Split(new string[1] { "||" }, StringSplitOptions.RemoveEmptyEntries)
			select item.Trim() into item
			where !string.IsNullOrWhiteSpace(item)
			select item).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<string> SplitAchievementGuideLocalPaths(string value)
	{
		return (from item in (value ?? string.Empty).Split(new string[1] { "||" }, StringSplitOptions.RemoveEmptyEntries)
			select item.Trim() into item
			where !string.IsNullOrWhiteSpace(item)
			select item).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<string> SplitAchievementGuideIdeaLines(string value)
	{
		return (from item in (value ?? string.Empty).Split(new string[1] { " / " }, StringSplitOptions.RemoveEmptyEntries)
			select item.Trim() into item
			where !string.IsNullOrWhiteSpace(item)
			select item).ToList();
	}

	private static string BuildAchievementGuideDetailText(AchievementGuideRow guide)
	{
		if (guide == null)
		{
			return string.Empty;
		}
		List<string> list = SplitAchievementGuideDeckCodes(guide.recommended_deck_codes);
		List<string> list2 = SplitAchievementGuideIdeaLines(guide.idea);
		List<string> list4 = SplitAchievementGuideLocalPaths(guide.local_text);
		List<string> values = new List<string>();
		if (!string.IsNullOrWhiteSpace(guide.category))
		{
			values.Add(guide.category);
		}
		if (!string.IsNullOrWhiteSpace(guide.sub_category))
		{
			values.Add(guide.sub_category);
		}
		string str = (values.Count > 0) ? string.Join(" / ", values) : "未分类";
		string text = string.IsNullOrWhiteSpace(guide.date) ? string.Empty : (guide.date + " ");
		string text2 = string.IsNullOrWhiteSpace(guide.title) ? "(未命名帖子)" : guide.title;
		string text3 = (guide.guide_slot > 1) ? ("攻略" + guide.guide_slot.ToString(CultureInfo.InvariantCulture)) : "攻略";
		List<string> list3 = new List<string>
		{
			"版本: " + text3,
			"成就名称: " + (string.IsNullOrWhiteSpace(guide.achievement_name) ? "(未命名成就)" : guide.achievement_name),
			"具体要求: " + (string.IsNullOrWhiteSpace(guide.requirement) ? "-" : guide.requirement),
			"分类: " + str,
			"投稿人: " + (string.IsNullOrWhiteSpace(guide.editor_id) ? "历史导入" : guide.editor_id),
			"来源帖子: " + text + text2,
			"网站链接: " + (guide.source_url ?? "-"),
			string.Empty,
			"推荐卡组代码:"
		};
		if (list.Count == 0)
		{
			list3.Add("- 无");
		}
		else
		{
			for (int i = 0; i < list.Count; i++)
			{
				list3.Add(string.Format(CultureInfo.InvariantCulture, "{0}. {1}", i + 1, list[i]));
			}
		}
		list3.Add(string.Empty);
		list3.Add("思路:");
		if (list2.Count == 0)
		{
			list3.Add("- 无");
		}
		else
		{
			foreach (string item3 in list2)
			{
				list3.Add("- " + item3);
			}
		}
		list3.Add(string.Empty);
		list3.Add("本地攻略文件:");
		if (list4.Count == 0)
		{
			list3.Add("- 无");
		}
		else
		{
			foreach (string item4 in list4)
			{
				list3.Add("- " + item4);
			}
		}
		return string.Join(Environment.NewLine, list3);
	}

	private void OpenExternalUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowAppError(this, "成就攻略", "打开链接失败:\r\n" + ex.Message);
		}
	}

	private void OpenAchievementGuideLocalFile(string path, IWin32Window owner)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			string text = ResolveAchievementGuideLocalFilePath(path);
			if (!File.Exists(text))
			{
			ShowAppWarning(owner ?? this, "成就攻略", "未找到本地攻略文件:\r\n" + path);
				return;
			}
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowAppError(owner ?? this, "成就攻略", "打开本地攻略失败:\r\n" + ex.Message);
		}
	}

	private static string ResolveAchievementGuideLocalFilePath(string path)
	{
		string text = (path ?? string.Empty).Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(text) || Path.IsPathRooted(text))
		{
			return text;
		}
		string text2 = (AppDomain.CurrentDomain.BaseDirectory ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.IsNullOrWhiteSpace(text2))
		{
			return text;
		}
		string fileName = Path.GetFileName(text);
		foreach (string item in new string[5]
		{
			Path.Combine(text2, text),
			Path.Combine(text2, fileName),
			Path.Combine(text2, "guides", text),
			Path.Combine(text2, "guides", fileName),
			Path.Combine(text2, "攻略", fileName)
		})
		{
			if (File.Exists(item))
			{
				return item;
			}
		}
		return Path.Combine(text2, text);
	}

	private Control BuildPagedDetailPanel<T>(IList<T> items, Func<IEnumerable<T>, DataTable> tableBuilder)
	{
		bool supportsCompletionFilter = typeof(T) == typeof(OfficialAchievementExportRow) || typeof(T) == typeof(AchievementProgressRow);
		TableLayoutPanel tableLayoutPanel = CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f), new RowStyle(SizeType.AutoSize));
		FlowLayoutPanel filterPanel = CreateHorizontalFlowPanel(new Padding(8, 8, 8, 0), wrapContents: false);
		ComboBox completionFilterBox = null;
		int pageIndex = 0;
		Action refreshPage = null;
		if (supportsCompletionFilter)
		{
			completionFilterBox = CreateCompletionFilterComboBox(delegate
			{
				pageIndex = 0;
				refreshPage();
			});
			AddLabeledControl(filterPanel, "完成状态", completionFilterBox);
		}
		DataGridView grid = CreateReadOnlyGrid();
		FlowLayoutPanel actionPanel = CreateHorizontalFlowPanel(new Padding(8, 6, 8, 0), wrapContents: false);
		Button trackButton = new Button
		{
			AutoSize = true,
			Text = "收藏当前行"
		};
		Button guideButton = new Button
		{
			AutoSize = true,
			Text = "查看当前行攻略"
		};
		Label actionHintLabel = CreateInlineLabel("从这里进入的细分界面，也可以直接用这两个按钮。", new Padding(12, 8, 0, 0));
		actionPanel.Controls.Add(trackButton);
		actionPanel.Controls.Add(guideButton);
		actionPanel.Controls.Add(actionHintLabel);
		grid.DataBindingComplete += delegate
		{
			ConfigureAchievementGuideGrid(grid);
			SelectFirstActionableGridRow(grid);
			UpdateAchievementActionButtonsState(grid, trackButton, guideButton);
		};
		FlowLayoutPanel flowLayoutPanel = CreatePagerPanel(new Padding(8, 6, 8, 6), out Button prevButton, out Button nextButton, out Label pageLabel, delegate
		{
			if (pageIndex > 0)
			{
				pageIndex--;
				refreshPage();
			}
		}, delegate
		{
			List<T> list = items.ToList();
			string text = ((completionFilterBox != null) ? (completionFilterBox.SelectedItem as string) : null) ?? "全部";
			if (supportsCompletionFilter)
			{
				if (typeof(T) == typeof(OfficialAchievementExportRow))
				{
					list = ApplyOfficialAchievementCompletionFilter(items.Cast<OfficialAchievementExportRow>(), text).Cast<T>().ToList();
				}
				else if (typeof(T) == typeof(AchievementProgressRow))
				{
					list = ApplyAchievementProgressCompletionFilter(items.Cast<AchievementProgressRow>(), text).Cast<T>().ToList();
				}
			}
			int totalPages = Math.Max(1, (int)Math.Ceiling((double)list.Count / DetailPageSize));
			if (pageIndex < totalPages - 1)
			{
				pageIndex++;
				refreshPage();
			}
		});
		refreshPage = delegate
		{
			List<T> list = items.ToList();
			string text = ((completionFilterBox != null) ? (completionFilterBox.SelectedItem as string) : null) ?? "全部";
			if (supportsCompletionFilter)
			{
				if (typeof(T) == typeof(OfficialAchievementExportRow))
				{
					list = ApplyOfficialAchievementCompletionFilter(items.Cast<OfficialAchievementExportRow>(), text).Cast<T>().ToList();
				}
				else if (typeof(T) == typeof(AchievementProgressRow))
				{
					list = ApplyAchievementProgressCompletionFilter(items.Cast<AchievementProgressRow>(), text).Cast<T>().ToList();
				}
			}
			int totalPages = Math.Max(1, (int)Math.Ceiling((double)list.Count / DetailPageSize));
			if (pageIndex >= totalPages)
			{
				pageIndex = totalPages - 1;
			}
			if (pageIndex < 0)
			{
				pageIndex = 0;
			}
			List<T> arg = list.Skip(pageIndex * DetailPageSize).Take(DetailPageSize).ToList();
			grid.Tag = arg.Cast<object>().ToList();
			grid.DataSource = tableBuilder(arg);
			ConfigureAchievementGuideGrid(grid);
			SelectFirstActionableGridRow(grid);
			UpdateAchievementActionButtonsState(grid, trackButton, guideButton);
			pageLabel.Text = string.Format(CultureInfo.InvariantCulture, "第 {0} / {1} 页，共 {2} 条，每页 {3} 条", pageIndex + 1, totalPages, list.Count, DetailPageSize);
			prevButton.Enabled = pageIndex > 0;
			nextButton.Enabled = pageIndex < totalPages - 1;
		};
		grid.SelectionChanged += delegate
		{
			UpdateAchievementActionButtonsState(grid, trackButton, guideButton);
		};
		trackButton.Click += delegate
		{
			DataGridViewRow actionableAchievementGridRow = GetActionableAchievementGridRow(grid);
			if (actionableAchievementGridRow != null && grid.Columns.Contains("收藏"))
			{
				TryToggleTrackedAchievementFromGrid(grid, actionableAchievementGridRow.Index, grid.Columns["收藏"].Index);
				UpdateAchievementActionButtonsState(grid, trackButton, guideButton);
			}
		};
		guideButton.Click += delegate
		{
			DataGridViewRow actionableAchievementGridRow2 = GetActionableAchievementGridRow(grid);
			if (actionableAchievementGridRow2 != null)
			{
				TryOpenAchievementGuideFromRow(grid, actionableAchievementGridRow2.Index);
				UpdateAchievementActionButtonsState(grid, trackButton, guideButton);
			}
		};
		refreshPage();
		if (supportsCompletionFilter)
		{
			tableLayoutPanel.Controls.Add(filterPanel, 0, 0);
		}
		else
		{
			actionPanel.Margin = new Padding(8, 8, 8, 0);
		}
		tableLayoutPanel.Controls.Add(actionPanel, 0, 1);
		tableLayoutPanel.Controls.Add(grid, 0, 2);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 3);
		return tableLayoutPanel;
	}

	private void UpdateAchievementActionButtonsState(DataGridView grid, Button trackButton, Button guideButton)
	{
		if (trackButton == null || guideButton == null)
		{
			return;
		}
		DataGridViewRow dataGridViewRow = GetActionableAchievementGridRow(grid);
		string text = (dataGridViewRow != null) ? GetGridCellText(dataGridViewRow, "__TrackKey") : null;
		object boundAchievementItemFromGrid = GetBoundAchievementItemFromGrid(grid, (dataGridViewRow != null) ? dataGridViewRow.Index : (-1));
		bool flag = !string.IsNullOrWhiteSpace(text) || boundAchievementItemFromGrid is OfficialAchievementExportRow || boundAchievementItemFromGrid is AchievementProgressRow;
		bool flag2 = flag && IsAchievementTracked(text);
		if (string.IsNullOrWhiteSpace(text))
		{
			if (boundAchievementItemFromGrid is OfficialAchievementExportRow officialAchievementExportRow)
			{
				flag2 = IsAchievementTracked(GetOfficialAchievementTrackKey(officialAchievementExportRow));
			}
			else if (boundAchievementItemFromGrid is AchievementProgressRow achievementProgressRow)
			{
				flag2 = IsAchievementTracked(GetProgressAchievementTrackKey(achievementProgressRow));
			}
		}
		trackButton.Enabled = flag;
		trackButton.Text = (flag ? (flag2 ? "取消收藏当前行" : "收藏当前行") : "当前行不可收藏");
		bool flag3 = !string.IsNullOrWhiteSpace((dataGridViewRow != null) ? GetGridCellText(dataGridViewRow, "攻略") : null);
		if (!flag3)
		{
			if (boundAchievementItemFromGrid is OfficialAchievementExportRow officialAchievementExportRow2)
			{
				flag3 = HasAchievementGuides(GetOfficialAchievementDisplayName(officialAchievementExportRow2), GetOfficialAchievementRequirementText(officialAchievementExportRow2.Reference), GuessOfficialAchievementClass(officialAchievementExportRow2));
			}
			else if (boundAchievementItemFromGrid is AchievementProgressRow achievementProgressRow2)
			{
				string text2 = CleanMultiline(achievementProgressRow2.Description);
				string text3 = GuessAchievementClass(achievementProgressRow2);
				flag3 = HasAchievementGuides(achievementProgressRow2.Name, text2, text3);
			}
		}
		bool flag4 = flag3;
		guideButton.Enabled = flag4;
		guideButton.Text = (flag4 ? "查看当前行攻略" : "当前行没有攻略");
	}

	private static void ApplySplitContainerLayout(SplitContainer split, int preferredDistance, int requestedPanel1MinSize, int requestedPanel2MinSize)
	{
		if (split == null || split.IsDisposed)
		{
			return;
		}
		int num = ((split.Orientation == Orientation.Horizontal) ? (split.ClientSize.Height - split.SplitterWidth) : (split.ClientSize.Width - split.SplitterWidth));
		if (num <= 0)
		{
			return;
		}
		int num2 = Math.Max(0, Math.Min(preferredDistance, num));
		int num3 = Math.Max(0, Math.Min(Math.Max(0, requestedPanel1MinSize), num2));
		int num4 = Math.Max(0, Math.Min(Math.Max(0, requestedPanel2MinSize), num - num2));
		num2 = Math.Max(num3, Math.Min(num2, num - num4));
		try
		{
			if (split.Panel1MinSize != 0)
			{
				split.Panel1MinSize = 0;
			}
			if (split.Panel2MinSize != 0)
			{
				split.Panel2MinSize = 0;
			}
			if (split.SplitterDistance != num2)
			{
				split.SplitterDistance = num2;
			}
			if (split.Panel1MinSize != num3)
			{
				split.Panel1MinSize = num3;
			}
			if (split.Panel2MinSize != num4)
			{
				split.Panel2MinSize = num4;
			}
			if (split.SplitterDistance != num2)
			{
				split.SplitterDistance = num2;
			}
		}
		catch (ArgumentOutOfRangeException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	private static DataGridViewCell GetFirstVisibleCell(DataGridViewRow row)
	{
		if (row == null)
		{
			return null;
		}
		foreach (DataGridViewCell cell in row.Cells)
		{
			if (cell.Visible)
			{
				return cell;
			}
		}
		return null;
	}

	private Control BuildOfficialGroupedDetailPanel<TGroup>(IList<TGroup> allGroups, int leftColumnWidth, DataGridViewAutoSizeColumnsMode leftGridAutoSizeMode, Action<DataGridView> configureLeftGrid, Func<IList<TGroup>> getVisibleGroups, Action<FlowLayoutPanel, Action> addExtraFilterControls, Func<TGroup, string> getGroupKey, Func<TGroup, List<OfficialAchievementExportRow>> getGroupAchievements, Func<TGroup, string> buildSummaryText, Func<TGroup, string> buildPagePrefix) where TGroup : class
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, leftColumnWidth));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		DataGridView leftGrid = CreateReadOnlyGrid(leftGridAutoSizeMode);
		leftGrid.AutoGenerateColumns = true;
		configureLeftGrid?.Invoke(leftGrid);
		bool flag = buildSummaryText != null;
		TableLayoutPanel tableLayoutPanel2 = flag ? CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f), new RowStyle(SizeType.AutoSize)) : CreateSingleColumnLayout(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100f), new RowStyle(SizeType.AutoSize));
		FlowLayoutPanel filterPanel = CreateHorizontalFlowPanel(new Padding(8, 8, 8, 0), wrapContents: false);
		int pageIndex = 0;
		Action refreshGroups = null;
		Action refreshPage = null;
		TGroup currentGroup = ((allGroups != null) ? allGroups.FirstOrDefault() : null);
		ComboBox completionFilterBox = CreateCompletionFilterComboBox(delegate
		{
			pageIndex = 0;
			refreshPage();
		});
		Func<List<OfficialAchievementExportRow>> getFilteredAchievements = delegate
		{
			List<OfficialAchievementExportRow> list3 = (currentGroup != null) ? getGroupAchievements(currentGroup) : new List<OfficialAchievementExportRow>();
			return ApplyOfficialAchievementCompletionFilter(list3, (completionFilterBox.SelectedItem as string) ?? "全部");
		};
		AddLabeledControl(filterPanel, "完成状态", completionFilterBox);
		addExtraFilterControls?.Invoke(filterPanel, delegate
		{
			pageIndex = 0;
			refreshGroups();
			refreshPage();
		});
		Label label = flag ? CreateSummaryLabel(0, new Padding(8, 8, 8, 4)) : null;
		DataGridView grid = CreateReadOnlyGrid();
		FlowLayoutPanel flowLayoutPanel = CreatePagerPanel(new Padding(8, 6, 8, 6), out Button prevButton, out Button nextButton, out Label pageLabel, delegate
		{
			if (pageIndex > 0)
			{
				pageIndex--;
				refreshPage();
			}
		}, delegate
		{
			List<OfficialAchievementExportRow> list3 = getFilteredAchievements();
			int num2 = Math.Max(1, (int)Math.Ceiling((double)list3.Count / DetailPageSize));
			if (pageIndex < num2 - 1)
			{
				pageIndex++;
				refreshPage();
			}
		});
		refreshGroups = delegate
		{
			List<TGroup> list = ((getVisibleGroups != null) ? getVisibleGroups() : allGroups)?.ToList() ?? new List<TGroup>();
			leftGrid.DataSource = null;
			leftGrid.DataSource = list;
			if (list.Count == 0)
			{
				currentGroup = null;
				return;
			}
			string text = (currentGroup != null) ? getGroupKey(currentGroup) : null;
			TGroup val = list.FirstOrDefault((TGroup row) => string.Equals(getGroupKey(row), text, StringComparison.OrdinalIgnoreCase));
			currentGroup = ((val != null) ? val : list[0]);
			leftGrid.ClearSelection();
			for (int i = 0; i < leftGrid.Rows.Count; i++)
			{
				TGroup val2 = leftGrid.Rows[i].DataBoundItem is TGroup ? (TGroup)leftGrid.Rows[i].DataBoundItem : default(TGroup);
				if (val2 != null && string.Equals(getGroupKey(val2), getGroupKey(currentGroup), StringComparison.OrdinalIgnoreCase))
				{
					DataGridViewCell firstVisibleCell = GetFirstVisibleCell(leftGrid.Rows[i]);
					if (firstVisibleCell != null)
					{
						leftGrid.Rows[i].Selected = true;
						leftGrid.CurrentCell = firstVisibleCell;
					}
					break;
				}
			}
		};
		refreshPage = delegate
		{
			List<OfficialAchievementExportRow> list2 = getFilteredAchievements();
			int num = Math.Max(1, (int)Math.Ceiling((double)list2.Count / DetailPageSize));
			if (pageIndex >= num)
			{
				pageIndex = num - 1;
			}
			if (pageIndex < 0)
			{
				pageIndex = 0;
			}
			List<OfficialAchievementExportRow> rows = list2.Skip(pageIndex * DetailPageSize).Take(DetailPageSize).ToList();
			grid.DataSource = BuildOfficialAchievementTableV2(rows);
			ConfigureAchievementGuideGrid(grid);
			if (label != null)
			{
				label.Text = (currentGroup == null) ? "当前没有可展示的分组。" : (buildSummaryText(currentGroup) ?? string.Empty);
			}
			string text2 = (currentGroup == null) ? string.Empty : (((buildPagePrefix != null) ? buildPagePrefix(currentGroup) : null) ?? string.Empty);
			pageLabel.Text = string.IsNullOrWhiteSpace(text2) ? string.Format(CultureInfo.InvariantCulture, "第 {0} / {1} 页，共 {2} 条，每页 {3} 条", pageIndex + 1, num, list2.Count, DetailPageSize) : string.Format(CultureInfo.InvariantCulture, "{0} | 第 {1} / {2} 页，共 {3} 条，每页 {4} 条", text2, pageIndex + 1, num, list2.Count, DetailPageSize);
			prevButton.Enabled = pageIndex > 0;
			nextButton.Enabled = pageIndex < num - 1;
		};
		leftGrid.SelectionChanged += delegate
		{
			TGroup val3 = (leftGrid.CurrentRow != null && leftGrid.CurrentRow.DataBoundItem is TGroup) ? (TGroup)leftGrid.CurrentRow.DataBoundItem : null;
			if (val3 != null)
			{
				currentGroup = val3;
				pageIndex = 0;
				refreshPage();
			}
		};
		if (flag)
		{
			tableLayoutPanel2.Controls.Add(filterPanel, 0, 0);
			tableLayoutPanel2.Controls.Add(label, 0, 1);
			tableLayoutPanel2.Controls.Add(grid, 0, 2);
			tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 3);
		}
		else
		{
			tableLayoutPanel2.Controls.Add(filterPanel, 0, 0);
			tableLayoutPanel2.Controls.Add(grid, 0, 1);
			tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 2);
		}
		tableLayoutPanel.Controls.Add(leftGrid, 0, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 1, 0);
		refreshGroups();
		refreshPage();
		return tableLayoutPanel;
	}

	private Control BuildOfficialCategoryHierarchyDetailPanel(IList<OfficialAchievementExportRow> items)
	{
		List<OfficialPrimaryGroupDisplayRow> list = items.Select((OfficialAchievementExportRow item) => new
		{
			Achievement = item,
			Path = GetOfficialCategoryPath(item)
		}).GroupBy(item => (item.Path != null && item.Path.PrimaryCategory != null) ? item.Path.PrimaryCategory.Key : "unknown", StringComparer.OrdinalIgnoreCase).Select(group =>
		{
			var anon = group.First();
			OfficialCategoryPathInfo officialCategoryPathInfo = anon.Path ?? new OfficialCategoryPathInfo();
			List<OfficialAchievementExportRow> list3 = group.Select(item => item.Achievement).OrderByDescending(IsOfficialAchievementCompleted).ThenBy((OfficialAchievementExportRow item) => (item.Reference != null) ? item.Reference.Priority : int.MaxValue)
				.ThenBy((OfficialAchievementExportRow item) => item.AchievementId)
				.ToList();
			return new OfficialPrimaryGroupDisplayRow
			{
				Key = group.Key,
				RootCategory = ((officialCategoryPathInfo.RootCategory != null) ? officialCategoryPathInfo.RootCategory.Name : "未分类"),
				PrimaryCategory = ((officialCategoryPathInfo.PrimaryCategory != null) ? officialCategoryPathInfo.PrimaryCategory.Name : "未分类"),
				LeafCount = list3.Select((OfficialAchievementExportRow item) => (item.LeafCategory != null) ? item.LeafCategory.Key : "uncategorized").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
				ItemCount = list3.Count,
				CompletedCount = list3.Count(IsOfficialAchievementCompleted),
				CompletionProgress = FormatCompletionRate(list3.Count(IsOfficialAchievementCompleted), list3.Count) + " (" + list3.Count(IsOfficialAchievementCompleted).ToString(CultureInfo.InvariantCulture) + "/" + list3.Count.ToString(CultureInfo.InvariantCulture) + ")",
				Achievements = list3
			};
		})
			.OrderBy((OfficialPrimaryGroupDisplayRow row) => row.RootCategory, StringComparer.OrdinalIgnoreCase)
			.ThenBy((OfficialPrimaryGroupDisplayRow row) => row.PrimaryCategory, StringComparer.OrdinalIgnoreCase)
			.ToList();
		CheckBox incompletePrimaryOnlyCheckBox = CreateToggleFilterChip("只显示未完成");
		return BuildOfficialGroupedDetailPanel(list, 320, DataGridViewAutoSizeColumnsMode.Fill, delegate(DataGridView leftGrid)
		{
			leftGrid.DataBindingComplete += delegate
			{
				if (leftGrid.Columns.Contains("Key"))
				{
					leftGrid.Columns["Key"].Visible = false;
				}
				if (leftGrid.Columns.Contains("Achievements"))
				{
					leftGrid.Columns["Achievements"].Visible = false;
				}
				if (leftGrid.Columns.Contains("RootCategory"))
				{
					leftGrid.Columns["RootCategory"].Visible = false;
				}
				if (leftGrid.Columns.Contains("PrimaryCategory"))
				{
					leftGrid.Columns["PrimaryCategory"].HeaderText = "一级分类";
					leftGrid.Columns["PrimaryCategory"].DisplayIndex = 0;
					leftGrid.Columns["PrimaryCategory"].FillWeight = 62f;
					leftGrid.Columns["PrimaryCategory"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
				}
				if (leftGrid.Columns.Contains("LeafCount"))
				{
					leftGrid.Columns["LeafCount"].Visible = false;
				}
				if (leftGrid.Columns.Contains("ItemCount"))
				{
					leftGrid.Columns["ItemCount"].Visible = false;
				}
				if (leftGrid.Columns.Contains("CompletedCount"))
				{
					leftGrid.Columns["CompletedCount"].Visible = false;
				}
				if (leftGrid.Columns.Contains("CompletionProgress"))
				{
					leftGrid.Columns["CompletionProgress"].HeaderText = "完成度";
					leftGrid.Columns["CompletionProgress"].DisplayIndex = 1;
					leftGrid.Columns["CompletionProgress"].FillWeight = 38f;
					leftGrid.Columns["CompletionProgress"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
				}
			};
		}, delegate
		{
			return incompletePrimaryOnlyCheckBox.Checked ? list.Where((OfficialPrimaryGroupDisplayRow row) => row.CompletedCount < row.ItemCount).ToList() : list;
		}, delegate(FlowLayoutPanel filterPanel, Action refreshGroups)
		{
			filterPanel.Controls.Add(incompletePrimaryOnlyCheckBox);
			incompletePrimaryOnlyCheckBox.CheckedChanged += delegate
			{
				refreshGroups();
			};
		}, (OfficialPrimaryGroupDisplayRow row) => row.Key, (OfficialPrimaryGroupDisplayRow row) => row.Achievements ?? new List<OfficialAchievementExportRow>(), (OfficialPrimaryGroupDisplayRow row) => string.Format(CultureInfo.InvariantCulture, "根分类：{0}    一级分类：{1}    细分类：{2} 个    已完成：{3}/{4}", row.RootCategory, row.PrimaryCategory, row.LeafCount, row.CompletedCount, row.ItemCount), null);
	}

	private Control BuildOfficialPrimaryDetailPanel(IList<OfficialAchievementExportRow> items)
	{
		List<OfficialLeafGroupRow> list = (from @group in (from @group in items.GroupBy((OfficialAchievementExportRow item) => (item.LeafCategory != null) ? item.LeafCategory.Name : "未分类", StringComparer.OrdinalIgnoreCase)
				orderby @group.Count() descending
				select @group).ThenBy((IGrouping<string, OfficialAchievementExportRow> @group) => @group.Key, StringComparer.OrdinalIgnoreCase)
			select new OfficialLeafGroupRow
			{
				细分类 = @group.Key,
				条目数 = @group.Count(),
				已完成 = @group.Count(IsOfficialAchievementCompleted)
			}).ToList();
		return BuildOfficialGroupedDetailPanel(list, 280, DataGridViewAutoSizeColumnsMode.Fill, null, () => list, null, (OfficialLeafGroupRow row) => row.细分类 ?? "未分类", delegate(OfficialLeafGroupRow row)
		{
			return (from item in items
				where string.Equals((item.LeafCategory != null) ? item.LeafCategory.Name : "未分类", row.细分类 ?? "未分类", StringComparison.OrdinalIgnoreCase)
				orderby IsOfficialAchievementCompleted(item) descending, (item.Reference != null) ? item.Reference.Priority : int.MaxValue, item.AchievementId
				select item).ToList();
		}, null, (OfficialLeafGroupRow row) => "细分类：" + (row.细分类 ?? "未分类"));
	}

	private bool MatchesCollectionFilters(OwnedCollectionRow row)
	{
		string text = (CollectionSearchBox.Text ?? string.Empty).Trim();
		string text2 = (ClassFilterBox.SelectedItem as string) ?? "全部";
		string text3 = (CostFilterBox.SelectedItem as string) ?? "全部";
		string text4 = (TypeFilterBox.SelectedItem as string) ?? "全部";
		string text5 = (CollectionOwnershipFilterBox.SelectedItem as string) ?? "全部";
		string text6 = (CollectionRarityFilterBox.SelectedItem as string) ?? "全部";
		string text7 = (CollectionPremiumFilterBox.SelectedItem as string) ?? "全部";
		string text8 = (SetFilterBox.SelectedItem as string) ?? "全部";
		if (text2 != "全部" && !string.Equals(row.CardClass, text2, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text3 != "全部" && !string.Equals(row.CostGroup, text3, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text4 != "全部" && !string.Equals(row.Type, text4, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text5 == "已拥有" && row.TotalOwned <= 0)
		{
			return false;
		}
		if (text5 == "未拥有" && row.TotalOwned > 0)
		{
			return false;
		}
		if (text6 != "全部" && !string.Equals(row.Rarity, text6, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text7 == "普通" && row.Count <= 0)
		{
			return false;
		}
		if (text7 == "金卡" && row.PremiumCount <= 0)
		{
			return false;
		}
		if (text7 == "钻石" && row.DiamondCount <= 0)
		{
			return false;
		}
		if (text7 == "异画" && row.SignatureCount <= 0)
		{
			return false;
		}
		if (text8 != "全部" && !string.Equals(row.Set, text8, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		return ContainsIgnoreCase(row.Name, text) || ContainsIgnoreCase(row.Id, text) || ContainsIgnoreCase(row.Set, text) || ContainsIgnoreCase(row.Race, text);
	}

	private bool MatchesSkinFilters(OwnedCollectionRow row)
	{
		string text = (SkinSearchBox.Text ?? string.Empty).Trim();
		string text2 = (SkinClassFilterBox.SelectedItem as string) ?? "全部";
		string text3 = (SkinOwnershipFilterBox.SelectedItem as string) ?? "全部";
		string text4 = (SkinRarityFilterBox.SelectedItem as string) ?? "全部";
		if (text2 != "全部" && !string.Equals(row.CardClass, text2, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (text3 == "已拥有" && row.TotalOwned <= 0)
		{
			return false;
		}
		if (text3 == "未拥有" && row.TotalOwned > 0)
		{
			return false;
		}
		if (text4 != "全部" && !string.Equals(row.Rarity, text4, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		return ContainsIgnoreCase(row.Name, text) || ContainsIgnoreCase(row.Id, text) || ContainsIgnoreCase(row.Set, text);
	}

	private static bool ContainsIgnoreCase(string input, string search)
	{
		return !string.IsNullOrWhiteSpace(input) && input.IndexOf(search ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsDisplayableCollectionCard(OwnedCollectionRow row)
	{
		if (row == null)
		{
			return false;
		}
		return !string.Equals(row.Type, "皮肤", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSkinCollectionCard(OwnedCollectionRow row)
	{
		if (row == null)
		{
			return false;
		}
		return string.Equals(row.Type, "皮肤", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsKnownCollectionCard(OwnedCollectionRow row)
	{
		if (row == null)
		{
			return false;
		}
		return !string.Equals(row.Name, "(未识别卡牌)", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPlaceholderCollectionCard(OwnedCollectionRow row)
	{
		if (row == null || string.IsNullOrWhiteSpace(row.Set))
		{
			return false;
		}
		return row.Set.StartsWith("占位", StringComparison.OrdinalIgnoreCase);
	}

	private OwnedCollectionRow CreateOwnedCollectionRow(string cardId, IReadOnlyDictionary<string, CardMetadataRow> metadata, IReadOnlyDictionary<string, CollectionCard> collectionById)
	{
		metadata.TryGetValue(cardId ?? string.Empty, out var value);
		collectionById.TryGetValue(cardId ?? string.Empty, out var value2);
		CollectionCard collectionCard = value2;
		if (collectionCard == null)
		{
			CollectionCard collectionCard2 = new CollectionCard();
			collectionCard2.Id = cardId ?? string.Empty;
			collectionCard = collectionCard2;
		}
		value2 = collectionCard;
		string text = TranslateCardClass(value?.CardClass);
		string text2 = ResolveCollectionCardType(value2.Id, value?.Type, value?.Set, value?.Cost);
		OwnedCollectionRow ownedCollectionRow = new OwnedCollectionRow();
		ownedCollectionRow.Id = value2.Id ?? string.Empty;
		ownedCollectionRow.Name = ((value != null && !string.IsNullOrWhiteSpace(value.Name)) ? value.Name : "(未识别卡牌)");
		ownedCollectionRow.CardClass = text;
		ownedCollectionRow.Type = text2;
		ownedCollectionRow.CostGroup = ToCostGroup(value?.Cost);
		ownedCollectionRow.Cost = value?.Cost;
		ownedCollectionRow.Rarity = TranslateRarity(value?.Rarity);
		ownedCollectionRow.Race = TranslateRace(value?.Race);
		ownedCollectionRow.Set = TranslateSet(value?.Set);
		ownedCollectionRow.Count = value2.Count;
		ownedCollectionRow.PremiumCount = value2.PremiumCount;
		ownedCollectionRow.DiamondCount = value2.DiamondCount;
		ownedCollectionRow.SignatureCount = value2.SignatureCount;
		ownedCollectionRow.TotalOwned = value2.GetOwnedCount();
		ownedCollectionRow.ClassSort = text ?? string.Empty;
		ownedCollectionRow.TypeSort = text2 ?? string.Empty;
		ownedCollectionRow.CostSort = ((value != null && value.Cost.HasValue) ? value.Cost.Value : int.MaxValue);
		return ownedCollectionRow;
	}

	private IReadOnlyDictionary<string, CardMetadataRow> LoadCardMetadata(out string metadataPath)
	{
		string text = FindMetadataPath();
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			metadataPath = "未找到可用卡牌元数据";
			return new Dictionary<string, CardMetadataRow>(StringComparer.OrdinalIgnoreCase);
		}
		try
		{
			if (text.EndsWith("cards.json", StringComparison.OrdinalIgnoreCase))
			{
				List<LocalizedCardRecord> source = LoadJson<List<LocalizedCardRecord>>(text) ?? new List<LocalizedCardRecord>();
				metadataPath = text;
				return source.Where((LocalizedCardRecord card) => card != null && !string.IsNullOrWhiteSpace(card.Id) && card.Collectible).GroupBy((LocalizedCardRecord card) => card.Id, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, LocalizedCardRecord> group) => group.Key, delegate(IGrouping<string, LocalizedCardRecord> group)
				{
					LocalizedCardRecord localizedCardRecord = group.First();
					return new CardMetadataRow
					{
						Id = localizedCardRecord.Id,
						Name = (localizedCardRecord.Name ?? localizedCardRecord.Id),
						CardClass = localizedCardRecord.CardClass,
						Type = localizedCardRecord.Type,
						Cost = localizedCardRecord.Cost,
						Rarity = localizedCardRecord.Rarity,
						Race = FirstNonEmpty(localizedCardRecord.Race, localizedCardRecord.Races),
						Set = localizedCardRecord.Set
					};
				}, StringComparer.OrdinalIgnoreCase);
			}
			string input = File.ReadAllText(text);
			object[] array = _serializer.DeserializeObject(input) as object[];
			Dictionary<string, CardMetadataRow> dictionary = new Dictionary<string, CardMetadataRow>(StringComparer.OrdinalIgnoreCase);
			if (array != null)
			{
				foreach (Dictionary<string, object> item in array.OfType<Dictionary<string, object>>())
				{
					string @string = GetString(item, "id");
					if (!string.IsNullOrWhiteSpace(@string) && GetBool(item, "collectible"))
					{
						dictionary[@string] = new CardMetadataRow
						{
							Id = @string,
							Name = GetLocalizedString(item, "name"),
							CardClass = GetString(item, "cardClass"),
							Type = GetString(item, "type"),
							Cost = GetNullableInt(item, "cost"),
							Rarity = GetString(item, "rarity"),
							Race = (GetString(item, "race") ?? GetFirstArrayValue(item, "races")),
							Set = GetString(item, "set")
						};
					}
				}
			}
			metadataPath = text;
			return dictionary;
		}
		catch (Exception ex)
		{
			metadataPath = text + " (加载失败: " + ex.Message + ")";
			return new Dictionary<string, CardMetadataRow>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private string FindMetadataPath()
	{
		string text = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
		string path2 = ((Directory.GetParent(text) != null) ? Directory.GetParent(text).FullName : text);
		string path3 = Path.Combine(path2, "downloads");
		if (Directory.Exists(path3))
		{
			List<string> list = Directory.GetDirectories(path3, "hearthstonejson-*").OrderByDescending((string path) => path, StringComparer.OrdinalIgnoreCase).ToList();
			foreach (string item in list)
			{
				string text2 = Path.Combine(item, "zhCN", "cards.json");
				if (File.Exists(text2))
				{
					return text2;
				}
			}
			foreach (string item2 in list)
			{
				string text3 = Path.Combine(item2, "all", "cards.collectible.json");
				if (File.Exists(text3))
				{
					return text3;
				}
			}
		}
		string text4 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDT.AchievementWorkbench", "Data", "cards.collectible.json");
		return File.Exists(text4) ? text4 : null;
	}

	private List<AchievementProgressRow> LoadAchievementProgressRows(out int logFileCount)
	{
		Dictionary<string, AchievementProgressRow> dictionary = new Dictionary<string, AchievementProgressRow>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(_hearthstoneLogsRoot))
		{
			logFileCount = 0;
			return new List<AchievementProgressRow>();
		}
		List<string> list = Directory.GetFiles(_hearthstoneLogsRoot, "Achievements.log", SearchOption.AllDirectories).OrderByDescending((string path) => path, StringComparer.OrdinalIgnoreCase).ToList();
		logFileCount = list.Count;
		string text = BuildAchievementProgressCacheSignature(list);
		if (logFileCount == _achievementProgressCacheLogCount && string.Equals(text, _achievementProgressCacheSignature, StringComparison.Ordinal) && _achievementProgressCache != null)
		{
			return _achievementProgressCache;
		}
		foreach (string item in list)
		{
			string input;
			try
			{
				input = File.ReadAllText(item);
			}
			catch
			{
				continue;
			}
			foreach (Match item2 in AchievementNotificationRegex.Matches(input))
			{
				if (!item2.Success)
				{
					continue;
				}
				string value = item2.Groups["id"].Value;
				if (!string.IsNullOrWhiteSpace(value))
				{
					AchievementProgressRow achievementProgressRow = new AchievementProgressRow();
					achievementProgressRow.AchievementId = value;
					achievementProgressRow.Name = CleanMultiline(item2.Groups["name"].Value);
					achievementProgressRow.Type = item2.Groups["type"].Value;
					achievementProgressRow.MaxProgress = ParseInt(item2.Groups["max"].Value);
					achievementProgressRow.Progress = ParseInt(item2.Groups["progress"].Value);
					achievementProgressRow.AckProgress = ParseInt(item2.Groups["ack"].Value);
					achievementProgressRow.Trigger = item2.Groups["trigger"].Value;
					achievementProgressRow.Description = CleanMultiline(item2.Groups["description"].Value);
					achievementProgressRow.Completed = !string.Equals(item2.Groups["completed"].Value, "0", StringComparison.OrdinalIgnoreCase) || ParseInt(item2.Groups["progress"].Value) >= ParseInt(item2.Groups["max"].Value);
					achievementProgressRow.LastSeen = Path.GetFileName(Path.GetDirectoryName(item)) + " " + item2.Groups["time"].Value;
					achievementProgressRow.SourceLog = item;
					AchievementProgressRow achievementProgressRow2 = achievementProgressRow;
					if (!dictionary.TryGetValue(value, out var value2))
					{
						dictionary[value] = achievementProgressRow2;
					}
					else if (achievementProgressRow2.Progress > value2.Progress)
					{
						dictionary[value] = achievementProgressRow2;
					}
					else if (achievementProgressRow2.Progress == value2.Progress && string.CompareOrdinal(achievementProgressRow2.LastSeen, value2.LastSeen) > 0)
					{
						dictionary[value] = achievementProgressRow2;
					}
				}
			}
		}
		List<AchievementProgressRow> list2 = (from row in dictionary.Values
			orderby row.Completed descending, row.ProgressRatio descending, row.AchievementId
			select row).ToList();
		_achievementProgressCacheSignature = text;
		_achievementProgressCacheLogCount = logFileCount;
		_achievementProgressCache = list2;
		return list2;
	}

	private static string BuildAchievementProgressCacheSignature(IEnumerable<string> logPaths)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string logPath in logPaths ?? Enumerable.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(logPath))
			{
				continue;
			}
			stringBuilder.Append(logPath);
			stringBuilder.Append('|');
			try
			{
				FileInfo fileInfo = new FileInfo(logPath);
				stringBuilder.Append(fileInfo.Length.ToString(CultureInfo.InvariantCulture));
				stringBuilder.Append('|');
				stringBuilder.Append(fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
			}
			catch
			{
				stringBuilder.Append("missing");
			}
			stringBuilder.Append(';');
		}
		return stringBuilder.ToString();
	}

	private T LoadJson<T>(string path) where T : class
	{
		if (!File.Exists(path))
		{
			return null;
		}
		string text = File.ReadAllText(path);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return _serializer.Deserialize<T>(text);
	}

	private static Label MakeCaption(string text)
	{
		Label label = new Label();
		label.AutoSize = true;
		label.Text = text;
		label.Padding = new Padding(8, 9, 0, 0);
		label.ForeColor = ThemeTextMuted;
		label.Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold);
		label.BackColor = ThemeSurface;
		return label;
	}

	private static DataTable BuildCollectionTable(IEnumerable<OwnedCollectionRow> rows)
	{
		DataTable dataTable = new DataTable("Collection");
		dataTable.Columns.Add("卡牌ID");
		dataTable.Columns.Add("名称");
		dataTable.Columns.Add("职业");
		dataTable.Columns.Add("费用");
		dataTable.Columns.Add("卡牌类型");
		dataTable.Columns.Add("稀有度");
		dataTable.Columns.Add("种族");
		dataTable.Columns.Add("系列");
		dataTable.Columns.Add("普通");
		dataTable.Columns.Add("金色");
		dataTable.Columns.Add("钻石");
		dataTable.Columns.Add("异画");
		dataTable.Columns.Add("总拥有");
		foreach (OwnedCollectionRow row in rows)
		{
			dataTable.Rows.Add(row.Id, row.Name, row.CardClass, row.CostGroup, row.Type, row.Rarity, row.Race, row.Set, row.Count, row.PremiumCount, row.DiamondCount, row.SignatureCount, row.TotalOwned);
		}
		return dataTable;
	}

	private static DataTable BuildCompletedTable(IEnumerable<CompletedAchievementRow> achievements)
	{
		DataTable dataTable = new DataTable("CompletedAchievements");
		dataTable.Columns.Add("成就ID");
		dataTable.Columns.Add("类别");
		dataTable.Columns.Add("完成次数");
		foreach (CompletedAchievementRow achievement in achievements)
		{
			dataTable.Rows.Add(achievement.Id, achievement.Category, achievement.NumberOfCompletions);
		}
		return dataTable;
	}

	private static void AddGuideMetadataColumns(DataTable dataTable)
	{
		dataTable.Columns.Add("__GuideName");
		dataTable.Columns.Add("__GuideRequirement");
		dataTable.Columns.Add("__GuideClass");
	}

	private static DataTable BuildMultiGuideManagementTable(IEnumerable<MultiGuideManagementRow> rows)
	{
		DataTable dataTable = new DataTable("MultiGuideManagement");
		dataTable.Columns.Add("攻略");
		dataTable.Columns.Add("名称");
		dataTable.Columns.Add("分类");
		dataTable.Columns.Add("版本数");
		dataTable.Columns.Add("版本列表");
		dataTable.Columns.Add("最近更新");
		dataTable.Columns.Add("最近投稿人");
		dataTable.Columns.Add("__GuideGroupKey");
		AddGuideMetadataColumns(dataTable);
		foreach (MultiGuideManagementRow row in rows ?? Enumerable.Empty<MultiGuideManagementRow>())
		{
			dataTable.Rows.Add("管理", row.AchievementName, row.AchievementClass, row.VersionCount, row.VersionList, row.LatestDateText, row.LatestEditor, row.GroupKey, row.AchievementName, string.Empty, row.GuideClassFilter);
		}
		return dataTable;
	}

	private static void AddTrackMetadataColumns(DataTable dataTable)
	{
		dataTable.Columns.Add("__TrackKey");
		dataTable.Columns.Add("__TrackKind");
		dataTable.Columns.Add("__TrackId");
		dataTable.Columns.Add("__TrackName");
		dataTable.Columns.Add("__TrackRequirement");
		dataTable.Columns.Add("__TrackClass");
		dataTable.Columns.Add("__TrackSource");
		dataTable.Columns.Add("__TrackProgressText");
		dataTable.Columns.Add("__TrackStatusText");
		dataTable.Columns.Add("__TrackExtraText");
	}

	private DataTable BuildProgressTable(IEnumerable<AchievementProgressRow> rows)
	{
		DataTable dataTable = new DataTable("AchievementProgress");
		dataTable.Columns.Add("收藏");
		dataTable.Columns.Add("攻略");
		dataTable.Columns.Add("成就ID");
		dataTable.Columns.Add("名称");
		dataTable.Columns.Add("当前进度");
		dataTable.Columns.Add("目标值");
		dataTable.Columns.Add("完成状态");
		dataTable.Columns.Add("具体要求");
		dataTable.Columns.Add("触发类型");
		dataTable.Columns.Add("成就类型");
		dataTable.Columns.Add("最近出现");
		AddGuideMetadataColumns(dataTable);
		AddTrackMetadataColumns(dataTable);
		foreach (AchievementProgressRow row in rows)
		{
			string text = CleanMultiline(row.Description);
			string text2 = GuessAchievementClass(row);
			string text3 = GetProgressAchievementTrackKey(row);
			string text4 = FormatProgress(row.Progress, row.MaxProgress);
			string text5 = row.Completed ? "已完成" : "进行中";
			string text6 = string.Join(" / ", new string[2] { row.Type, row.Trigger }.Where((string part) => !string.IsNullOrWhiteSpace(part)));
			bool flag = HasAchievementGuides(row.Name, text, text2);
			dataTable.Rows.Add(IsAchievementTracked(text3) ? "已收藏" : "收藏", flag ? "攻略" : string.Empty, row.AchievementId, row.Name, row.Progress, row.MaxProgress, text5, text, row.Trigger, row.Type, row.LastSeen, row.Name, text, text2, text3, "progress", row.AchievementId, row.Name, text, text2, "官方进度明细", text4, text5, text6);
		}
		return dataTable;
	}

	private DataTable BuildOfficialAchievementTableV2(IEnumerable<OfficialAchievementExportRow> rows)
	{
		DataTable dataTable = new DataTable("OfficialAchievementsV2");
		dataTable.Columns.Add("收藏");
		dataTable.Columns.Add("攻略");
		dataTable.Columns.Add("名称");
		dataTable.Columns.Add("进度");
		dataTable.Columns.Add("完成状态");
		dataTable.Columns.Add("具体要求");
		dataTable.Columns.Add("关联卡牌收藏");
		dataTable.Columns.Add("分类");
		dataTable.Columns.Add("奖励点数");
		AddGuideMetadataColumns(dataTable);
		AddTrackMetadataColumns(dataTable);
		foreach (OfficialAchievementExportRow row in rows)
		{
			ReferenceAchievementExportRow reference = row.Reference;
			string text = ((reference != null) ? FirstNonEmpty(reference.DisplayName, new string[2] { reference.Name, reference.Text }) : null);
			int num = reference?.Quota ?? 0;
			string text2 = ((row.RootCategory != null) ? row.RootCategory.Name : string.Empty);
			string text3 = ((row.PrimaryCategory != null) ? row.PrimaryCategory.Name : string.Empty);
			string text4 = ((row.LeafCategory != null) ? row.LeafCategory.Name : string.Empty);
			int num2 = reference?.Points ?? 0;
			string text5 = GetOfficialAchievementRequirementText(reference);
			string text6 = BuildOfficialAchievementCollectionHint(row);
			string text7 = FormatProgress(row.Progress, num);
			string text8 = string.Join("/", new string[3] { text2, text3, text4 }.Where((string part) => !string.IsNullOrWhiteSpace(part)));
			string text9 = string.IsNullOrWhiteSpace(text) ? "(未命名成就)" : text;
			string text10 = GuessOfficialAchievementClass(row);
			string text11 = GetOfficialAchievementTrackKey(row);
			bool flag = HasAchievementGuides(text9, text5, text10);
			string text12 = IsOfficialAchievementCompleted(row) ? "已完成" : "进行中";
			dataTable.Rows.Add(IsAchievementTracked(text11) ? "已收藏" : "收藏", flag ? "攻略" : string.Empty, text9, text7, text12, text5, text6, text8, num2, text9, text5, text10, text11, "official", GetOfficialAchievementStableId(row), text9, text5, text10, "官方分类细分", text7, text12, text8);
		}
		return dataTable;
	}

	private DataTable BuildTrackedAchievementTable(IEnumerable<TrackedAchievementDisplayRow> rows)
	{
		DataTable dataTable = new DataTable("TrackedAchievements");
		dataTable.Columns.Add("收藏");
		dataTable.Columns.Add("攻略");
		dataTable.Columns.Add("来源");
		dataTable.Columns.Add("名称");
		dataTable.Columns.Add("进度");
		dataTable.Columns.Add("完成状态");
		dataTable.Columns.Add("具体要求");
		dataTable.Columns.Add("分类/备注");
		dataTable.Columns.Add("收藏时间");
		AddGuideMetadataColumns(dataTable);
		AddTrackMetadataColumns(dataTable);
		foreach (TrackedAchievementDisplayRow row in rows)
		{
			dataTable.Rows.Add("已收藏", row.HasGuide ? "攻略" : string.Empty, row.SourceLabel, row.Name, row.ProgressText, row.StatusText, row.Requirement, row.ExtraText, row.TrackedAtText, row.GuideName, row.GuideRequirement, row.GuideClass, row.TrackKey, row.TrackKind, row.TrackId, row.Name, row.Requirement, row.TrackClass, row.SourceLabel, row.ProgressText, row.StatusText, row.ExtraText);
		}
		return dataTable;
	}

	private static string GuessAchievementCategory(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return "未知";
		}
		string text = id.Split('_').FirstOrDefault() ?? string.Empty;
		return text.ToLowerInvariant() switch
		{
			"amazing" => "精彩操作", 
			"battlegrounds" => "酒馆战棋", 
			"deckbuilding" => "套牌构筑", 
			"global" => "全局成就", 
			"hearthstone" => "炉石通用", 
			"thijs" => "活动成就", 
			_ => text, 
		};
	}

	private static string GetOfficialCategoryDisplayName(int id)
	{
		return id switch
		{
			1 => "生涯", 
			2 => "游戏", 
			3 => "收藏", 
			4 => "冒险", 
			6 => "其他", 
			_ => "官方分类 " + id.ToString(CultureInfo.InvariantCulture), 
		};
	}

	private static string GetOfficialCategoryIcon(int id)
	{
		return id switch
		{
			1 => "progression", 
			2 => "gameplay", 
			3 => "collection", 
			4 => "adventures", 
			6 => "game_modes", 
			_ => "general", 
		};
	}

	private static int GuessOfficialCategory(AchievementProgressRow row)
	{
		string text = string.Join(" ", row?.AchievementId, row?.Name, row?.Description).ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(text))
		{
			return 3;
		}
		if (ContainsAny(text, "adventure", "adventures", "solo adventure", "book of", "chapter", "prologue", "冒险", "剧情", "章节", "英雄之书", "佣兵之书"))
		{
			return 4;
		}
		if (ContainsAny(text, "battleground", "arena", "duels", "mercenaries", "tavern brawl", "brawl", "ranked", "wild", "standard", "酒馆战棋", "竞技场", "对决", "佣兵", "乱斗", "天梯", "标准", "狂野"))
		{
			return 6;
		}
		if (ContainsAny(text, "collection", "collect", "own", "craft", "disenchant", "golden", "diamond", "signature", "收藏", "收集", "拥有", "制作", "分解", "金色", "钻石", "异画"))
		{
			return 2;
		}
		if (ContainsAny(text, "level", "progression", "experience", "xp", "reward track", "hero level", "wins with", "career", "等级", "进度", "经验", "奖励路线", "英雄等级", "生涯"))
		{
			return 1;
		}
		return 3;
	}

	private static string GuessAchievementClass(AchievementProgressRow row)
	{
		string haystack = string.Join(" ", row?.AchievementId, row?.Name, row?.Description).ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(haystack))
		{
			return "中立";
		}
		AchievementClassRule[] achievementClassDetectionRules = AchievementClassDetectionRules;
		foreach (AchievementClassRule achievementClassRule in achievementClassDetectionRules)
		{
			if (achievementClassRule.Keywords.Any((string keyword) => haystack.Contains(keyword)))
			{
				return achievementClassRule.ClassName;
			}
		}
		return "中立";
	}

	private static string GuessOfficialAchievementClass(OfficialAchievementExportRow row)
	{
		string text = NormalizeAchievementClassName((row.LeafCategory != null) ? row.LeafCategory.Name : null);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		text = NormalizeAchievementClassName((row.PrimaryCategory != null) ? row.PrimaryCategory.Name : null);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string haystack = string.Join(" ", GetOfficialAchievementDisplayName(row), GetOfficialAchievementRequirementText(row?.Reference)).ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(haystack))
		{
			return "中立";
		}
		AchievementClassRule[] achievementClassDetectionRules = AchievementClassDetectionRules;
		foreach (AchievementClassRule achievementClassRule in achievementClassDetectionRules)
		{
			if (achievementClassRule.Keywords.Any((string keyword) => haystack.Contains(keyword)))
			{
				return achievementClassRule.ClassName;
			}
		}
		return "中立";
	}

	private List<string> GuessOfficialAchievementClasses(OfficialAchievementExportRow row)
	{
		List<string> list = FindDualClassAchievementClasses(row);
		if (list.Count > 0)
		{
			return list;
		}
		string text = GuessOfficialAchievementClass(row);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "中立";
		}
		return new List<string> { text };
	}

	private List<string> FindDualClassAchievementClasses(OfficialAchievementExportRow row)
	{
		if (row == null || _dualClassAchievementLookup == null || _dualClassAchievementLookup.Count == 0)
		{
			return new List<string>();
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array = new string[3]
		{
			GetOfficialAchievementDisplayName(row),
			row?.Reference?.Name,
			row?.Reference?.DisplayName
		};
		string[] array2 = array;
		foreach (string value in array2)
		{
			string text = NormalizeLookupKey(value);
			if (string.IsNullOrWhiteSpace(text) || !_dualClassAchievementLookup.TryGetValue(text, out List<string> value2) || value2 == null)
			{
				continue;
			}
			foreach (string item in value2.Select(NormalizeAchievementClassName).Where((string className) => !string.IsNullOrWhiteSpace(className)))
			{
				hashSet.Add(item);
			}
		}
		return hashSet.OrderBy(ClassSortValue).ThenBy((string value) => value, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string NormalizeAchievementClassName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim();
		if (text == "中立" || text == "综合" || text == "职业")
		{
			return "中立";
		}
		if (text == "双职业")
		{
			return "双职业";
		}
		AchievementClassRule[] achievementClassDetectionRules = AchievementClassDetectionRules;
		foreach (AchievementClassRule achievementClassRule in achievementClassDetectionRules)
		{
			if (string.Equals(text, achievementClassRule.ClassName, StringComparison.OrdinalIgnoreCase))
			{
				return achievementClassRule.ClassName;
			}
		}
		return null;
	}

	private static bool ContainsAny(string haystack, params string[] keywords)
	{
		if (string.IsNullOrWhiteSpace(haystack) || keywords == null || keywords.Length == 0)
		{
			return false;
		}
		return keywords.Any((string keyword) => !string.IsNullOrWhiteSpace(keyword) && haystack.Contains(keyword));
	}

	private static int ClassSortValue(string className)
	{
		return className switch
		{
			"死亡骑士" => 1, 
			"恶魔猎手" => 2, 
			"德鲁伊" => 3, 
			"猎人" => 4, 
			"法师" => 5, 
			"圣骑士" => 6, 
			"牧师" => 7, 
			"潜行者" => 8, 
			"萨满祭司" => 9, 
			"术士" => 10, 
			"战士" => 11, 
			"双职业" => 98, 
			"中立" => 99, 
			_ => 200, 
		};
	}

	private static string TranslateCardClass(string value)
	{
		return (value ?? string.Empty).ToUpperInvariant() switch
		{
			"DEATHKNIGHT" => "死亡骑士", 
			"DEMONHUNTER" => "恶魔猎手", 
			"DRUID" => "德鲁伊", 
			"HUNTER" => "猎人", 
			"MAGE" => "法师", 
			"PALADIN" => "圣骑士", 
			"PRIEST" => "牧师", 
			"ROGUE" => "潜行者", 
			"SHAMAN" => "萨满祭司",
			"WARLOCK" => "术士", 
			"WARRIOR" => "战士", 
			"NEUTRAL" => "中立", 
			_ => string.IsNullOrWhiteSpace(value) ? "未知" : value, 
		};
	}

	private static string TranslateCardType(string value)
	{
		return (value ?? string.Empty).ToUpperInvariant() switch
		{
			"MINION" => "随从", 
			"SPELL" => "法术", 
			"WEAPON" => "武器", 
			"HERO" => "英雄", 
			"LOCATION" => "地点", 
			"HERO_POWER" => "英雄技能", 
			"ENCHANTMENT" => "附魔", 
			_ => string.IsNullOrWhiteSpace(value) ? "未知" : value, 
		};
	}

	private static string ResolveCollectionCardType(string cardId, string rawType, string rawSet, int? cost)
	{
		string text = (rawType ?? string.Empty).ToUpperInvariant();
		string text2 = (rawSet ?? string.Empty).ToUpperInvariant();
		if (text == "HERO")
		{
			bool flag = !cost.HasValue || string.Equals(text2, "HERO_SKINS", StringComparison.OrdinalIgnoreCase) || string.Equals(text2, "BATTLEGROUNDS_HERO_SKINS", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(cardId) && cardId.StartsWith("HERO_", StringComparison.OrdinalIgnoreCase));
			if (flag)
			{
				return "皮肤";
			}
		}
		return TranslateCardType(rawType);
	}

	private static string TranslateRarity(string value)
	{
		return (value ?? string.Empty).ToUpperInvariant() switch
		{
			"FREE" => "免费", 
			"COMMON" => "普通", 
			"RARE" => "稀有", 
			"EPIC" => "史诗", 
			"LEGENDARY" => "传说", 
			_ => string.IsNullOrWhiteSpace(value) ? string.Empty : value, 
		};
	}

	private static string TranslateRace(string value)
	{
		return (value ?? string.Empty).ToUpperInvariant() switch
		{
			"BEAST" => "野兽", 
			"DEMON" => "恶魔", 
			"DRAGON" => "龙", 
			"ELEMENTAL" => "元素", 
			"MECH" => "机械", 
			"MURLOC" => "鱼人", 
			"NAGA" => "纳迦", 
			"PIRATE" => "海盗", 
			"QUILBOAR" => "野猪人", 
			"TOTEM" => "图腾", 
			"UNDEAD" => "亡灵", 
			_ => string.IsNullOrWhiteSpace(value) ? string.Empty : value, 
		};
	}

	private static string TranslateSet(string value)
	{
		return (value ?? string.Empty).ToUpperInvariant() switch
		{
			"CORE" => "核心",
			"LEGACY" => "传承",
			"EXPERT1" => "经典",
			"HOF" => "名人堂",
			"NAXX" => "纳克萨玛斯",
			"GVG" => "地精大战侏儒",
			"BRM" => "黑石山的火焰",
			"TGT" => "冠军的试炼",
			"LOE" => "探险者协会",
			"OG" => "上古之神的低语",
			"KARA" => "卡拉赞之夜",
			"GANGS" => "龙争虎斗加基森",
			"UNGORO" => "勇闯安戈洛",
			"ICECROWN" => "冰封王座的骑士",
			"LOOTAPALOOZA" => "狗头人与地下世界",
			"GILNEAS" => "女巫森林",
			"BOOMSDAY" => "砰砰计划",
			"TROLL" => "拉斯塔哈的大乱斗",
			"DALARAN" => "暗影崛起",
			"ULDUM" => "奥丹姆奇兵",
			"DRAGONS" => "巨龙降临",
			"DEMONHUNTER_INITIATE" => "恶魔猎手新兵",
			"BLACK_TEMPLE" => "外域的灰烬",
			"SCHOLOMANCE" => "通灵学园",
			"DARKMOON_FAIRE" => "疯狂的暗月马戏团",
			"THE_BARRENS" => "贫瘠之地",
			"STORMWIND" => "暴风城下的集结",
			"ALTERAC_VALLEY" => "奥特兰克的决裂",
			"THE_SUNKEN_CITY" => "探寻沉没之城",
			"REVENDRETH" => "纳斯利亚堡的悬案",
			"RETURN_OF_THE_LICH_KING" => "巫妖王的进军",
			"BATTLE_OF_THE_BANDS" => "传奇音乐节",
			"TITANS" => "泰坦诸神",
			"WONDERS" => "决战荒芜之地",
			"WHIZBANGS_WORKSHOP" => "威兹班的工坊",
			"ISLAND_VACATION" => "胜地历险记",
			"SPACE" => "深暗领域",
			"EMERALD_DREAM" => "翡翠梦境",
			"EVENT" => "活动",
			"REWARD" => "奖励",
			"MISSIONS" => "任务",
			"PROMO" => "推广",
			"TB" => "乱斗模式",
			"HERO_SKINS" => "英雄皮肤",
			"BATTLEGROUNDS" => "酒馆战棋",
			"BATTLEGROUNDS_HERO_SKINS" => "战棋英雄皮肤",
			"BATTLEGROUNDS_TAVERN_TIER" => "战棋酒馆等级",
			"MERCENARIES" => "佣兵战纪",
			"PATH_OF_ARTHAS" => "阿尔萨斯之路",
			"MINI_SET" => "迷你系列",
			"WILD_WEST" => "决战荒芜之地",
			"EVENT_REWARD" => "活动奖励",
			"PLACEHOLDER_202204" => "占位系列",
			"PLACEHOLDER_202204_HERO" => "占位英雄",
			"PLACEHOLDER_202204_SKIN" => "占位皮肤",
			_ => string.IsNullOrWhiteSpace(value) ? string.Empty : value, 
		};
	}

	private static string ToCostGroup(int? cost)
	{
		if (!cost.HasValue)
		{
			return "未知";
		}
		return (cost.Value >= 10) ? "10+" : cost.Value.ToString(CultureInfo.InvariantCulture);
	}

	private static int CostSortValue(string costGroup)
	{
		if (string.Equals(costGroup, "10+", StringComparison.OrdinalIgnoreCase))
		{
			return 10;
		}
		int result;
		return int.TryParse(costGroup, out result) ? result : int.MaxValue;
	}

	private static int ParseInt(string raw)
	{
		int result;
		return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
	}

	private static string CleanMultiline(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		return value.Replace("\r", " ").Replace("\n", " ").Trim();
	}

	private static string GetOfficialAchievementRequirementText(ReferenceAchievementExportRow reference)
	{
		string text = FirstNonEmpty(reference?.Text, new string[2]
		{
			reference?.CompletedText,
			reference?.EmptyText
		});
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		text = Regex.Replace(text, "<.*?>", string.Empty);
		return CleanMultiline(text);
	}

	private static string GetOfficialAchievementDisplayName(OfficialAchievementExportRow row)
	{
		string text = ((row != null && row.Reference != null) ? FirstNonEmpty(row.Reference.DisplayName, new string[2]
		{
			row.Reference.Name,
			row.Reference.Text
		}) : null);
		return string.IsNullOrWhiteSpace(text) ? "(未命名成就)" : text;
	}

	private static string NormalizeLookupKey(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		char[] array = value.Trim().ToLowerInvariant().Where(delegate(char ch)
		{
			if (char.IsWhiteSpace(ch))
			{
				return false;
			}
			UnicodeCategory unicodeCategory = char.GetUnicodeCategory(ch);
			return unicodeCategory != UnicodeCategory.ConnectorPunctuation && unicodeCategory != UnicodeCategory.DashPunctuation && unicodeCategory != UnicodeCategory.OpenPunctuation && unicodeCategory != UnicodeCategory.ClosePunctuation && unicodeCategory != UnicodeCategory.InitialQuotePunctuation && unicodeCategory != UnicodeCategory.FinalQuotePunctuation && unicodeCategory != UnicodeCategory.OtherPunctuation;
		}).ToArray();
		return new string(array);
	}

	private static string NullIfWhiteSpace(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private List<AchievementRelatedCardReference> FindAchievementRelatedCards(OfficialAchievementExportRow row)
	{
		if (row == null || _achievementRelatedCardLookup == null || _achievementRelatedCardLookup.Count == 0)
		{
			return null;
		}
		ReferenceAchievementExportRow reference = row.Reference;
		string[] array = new string[3]
		{
			GetOfficialAchievementDisplayName(row),
			reference?.Name,
			reference?.DisplayName
		};
		string[] array2 = array;
		foreach (string value in array2)
		{
			string text = NormalizeLookupKey(value);
			if (!string.IsNullOrWhiteSpace(text) && _achievementRelatedCardLookup.TryGetValue(text, out var value2) && value2 != null && value2.Count > 0)
			{
				return value2;
			}
		}
		return null;
	}

	private OwnedCollectionRow ResolveRelatedCollectionCard(AchievementRelatedCardReference relatedCard)
	{
		if (relatedCard == null)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(relatedCard.CardId) && _collectionLookupById.TryGetValue(relatedCard.CardId, out var value) && value != null)
		{
			return value;
		}
		string text = NormalizeLookupKey(relatedCard.CardName);
		if (!string.IsNullOrWhiteSpace(text) && _collectionLookupByName.TryGetValue(text, out var value2) && value2 != null)
		{
			return value2.FirstOrDefault();
		}
		return null;
	}

	private static string BuildCardPreview(IList<string> names, int maxItems)
	{
		List<string> list = names?.Where((string item) => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
		if (list.Count == 0)
		{
			return string.Empty;
		}
		if (list.Count <= maxItems)
		{
			return string.Join("、", list);
		}
		return string.Join("、", list.Take(maxItems)) + string.Format(CultureInfo.InvariantCulture, " 等{0}张", list.Count);
	}

	private List<AchievementRelatedCardReference> BuildDerivedCollectionAchievementCards(OfficialAchievementExportRow row)
	{
		if (row == null || !string.Equals((row.RootCategory != null) ? row.RootCategory.Name : null, "收藏", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string text = FirstNonEmpty((row.PrimaryCategory != null) ? row.PrimaryCategory.Name : null, new string[1] { (row.LeafCategory != null) ? row.LeafCategory.Name : null });
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string officialAchievementRequirementText = GetOfficialAchievementRequirementText(row.Reference);
		if (string.IsNullOrWhiteSpace(officialAchievementRequirementText) || !officialAchievementRequirementText.StartsWith("收集", StringComparison.Ordinal))
		{
			return null;
		}
		string text2 = Regex.Replace(officialAchievementRequirementText, "^收集\\d+张(?:不同的)?", string.Empty).Trim();
		text2 = Regex.Replace(text2, "卡牌。?$", string.Empty).Trim();
		if (!text2.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string value = text2.Substring(text.Length).Trim();
		string text3 = NormalizeAchievementClassName(value);
		string text4 = null;
		string text5 = null;
		if (string.IsNullOrWhiteSpace(text3) && !string.IsNullOrWhiteSpace(value))
		{
			switch (value)
			{
			case "法术":
			case "随从":
			case "武器":
			case "英雄":
			case "地点":
				text4 = value;
				break;
			case "传说":
			case "史诗":
			case "稀有":
			case "普通":
			case "免费":
				text5 = value;
				break;
			default:
				return null;
			}
		}
		IEnumerable<OwnedCollectionRow> enumerable = _ownedCollectionRows.Where((OwnedCollectionRow item) => string.Equals(item.Set, text, StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrWhiteSpace(text3))
		{
			enumerable = enumerable.Where((OwnedCollectionRow item) => string.Equals(item.CardClass, text3, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(text4))
		{
			enumerable = enumerable.Where((OwnedCollectionRow item) => string.Equals(item.Type, text4, StringComparison.OrdinalIgnoreCase));
		}
		if (!string.IsNullOrWhiteSpace(text5))
		{
			enumerable = enumerable.Where((OwnedCollectionRow item) => string.Equals(item.Rarity, text5, StringComparison.OrdinalIgnoreCase));
		}
		List<AchievementRelatedCardReference> list = enumerable.OrderBy((OwnedCollectionRow item) => item.CostSort).ThenBy((OwnedCollectionRow item) => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy((OwnedCollectionRow item) => item.Id, StringComparer.OrdinalIgnoreCase).Select((OwnedCollectionRow item) => new AchievementRelatedCardReference
		{
			CardId = item.Id,
			CardName = item.Name
		}).ToList();
		return (list.Count > 0) ? list : null;
	}

	private string BuildOfficialAchievementCollectionHint(OfficialAchievementExportRow row)
	{
		List<AchievementRelatedCardReference> list = FindAchievementRelatedCards(row);
		if (list == null || list.Count == 0)
		{
			list = BuildDerivedCollectionAchievementCards(row);
		}
		if (list != null && list.Count > 0)
		{
			List<string> list2 = new List<string>();
			foreach (AchievementRelatedCardReference item in list)
			{
				OwnedCollectionRow ownedCollectionRow = ResolveRelatedCollectionCard(item);
				string text = FirstNonEmpty((ownedCollectionRow != null) ? ownedCollectionRow.Name : null, new string[2] { item.CardName, item.CardId });
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				string text2 = ((ownedCollectionRow != null && ownedCollectionRow.TotalOwned > 0) ? "有" : "无");
				string item2 = text + "（" + text2 + "）";
				if (!list2.Contains(item2))
				{
					list2.Add(item2);
				}
			}
			return (list2.Count > 0) ? string.Join("；", list2) : "无";
		}
		return "无";
	}

	private static string FormatProgress(int progress, int maxProgress)
	{
		if (maxProgress > 0)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", progress, maxProgress);
		}
		return progress.ToString(CultureInfo.InvariantCulture);
	}

	private static string FormatCompletionRate(int completedCount, int totalCount)
	{
		if (totalCount <= 0)
		{
			return "0%";
		}
		double num = (double)completedCount * 100.0 / (double)totalCount;
		return string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", num);
	}

	private static string FirstNonEmpty(string primary, IEnumerable<string> secondary)
	{
		if (!string.IsNullOrWhiteSpace(primary))
		{
			return primary;
		}
		return secondary?.FirstOrDefault((string item) => !string.IsNullOrWhiteSpace(item));
	}

	private static string GetString(IDictionary<string, object> dictionary, string key)
	{
		if (dictionary == null || !dictionary.ContainsKey(key) || dictionary[key] == null)
		{
			return null;
		}
		return (dictionary[key] as string) ?? Convert.ToString(dictionary[key], CultureInfo.InvariantCulture);
	}

	private static int ParseTrailingInt(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return 0;
		}
		int num = value.LastIndexOf('_');
		if (num < 0 || num >= value.Length - 1)
		{
			return 0;
		}
		int result;
		return int.TryParse(value.Substring(num + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
	}

	private static string GetLocalizedString(IDictionary<string, object> dictionary, string key)
	{
		if (dictionary == null || !dictionary.ContainsKey(key) || dictionary[key] == null)
		{
			return null;
		}
		string text = dictionary[key] as string;
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		if (dictionary[key] is Dictionary<string, object> dictionary2)
		{
			return GetString(dictionary2, "zhCN") ?? GetString(dictionary2, "enUS") ?? dictionary2.Values.Select((object value) => Convert.ToString(value, CultureInfo.InvariantCulture)).FirstOrDefault((string value) => !string.IsNullOrWhiteSpace(value));
		}
		return Convert.ToString(dictionary[key], CultureInfo.InvariantCulture);
	}

	private static string GetFirstArrayValue(IDictionary<string, object> dictionary, string key)
	{
		if (dictionary == null || !dictionary.ContainsKey(key) || dictionary[key] == null)
		{
			return null;
		}
		return (dictionary[key] is object[] source) ? source.Select((object value) => Convert.ToString(value, CultureInfo.InvariantCulture)).FirstOrDefault((string value) => !string.IsNullOrWhiteSpace(value)) : null;
	}

	private static int? GetNullableInt(IDictionary<string, object> dictionary, string key)
	{
		if (dictionary == null || !dictionary.ContainsKey(key) || dictionary[key] == null)
		{
			return null;
		}
		string s = Convert.ToString(dictionary[key], CultureInfo.InvariantCulture);
		int result;
		return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? new int?(result) : null;
	}

	private static bool GetBool(IDictionary<string, object> dictionary, string key)
	{
		if (dictionary == null || !dictionary.ContainsKey(key) || dictionary[key] == null)
		{
			return false;
		}
		string value = Convert.ToString(dictionary[key], CultureInfo.InvariantCulture);
		bool result;
		return bool.TryParse(value, out result) && result;
	}

	private static string ExistsText(string path)
	{
		return File.Exists(path) ? "已找到" : "未找到";
	}

	private static string CompactPathText(string path, int maxLength)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "-";
		}
		string text = path.Trim();
		if (text.Length <= maxLength || maxLength < 12)
		{
			return text;
		}
		int num = (maxLength - 3) / 2;
		int startIndex = text.Length - (maxLength - 3 - num);
		return text.Substring(0, num) + "..." + text.Substring(startIndex);
	}

	private static string CompactDateTimeText(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "-";
		}
		DateTime result;
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out result))
		{
			return result.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		}
		string text = value.Trim().Replace('T', ' ');
		int num = text.IndexOf('.');
		if (num > 0)
		{
			text = text.Substring(0, num);
		}
		text = text.TrimEnd('Z');
		return (text.Length <= 19) ? text : text.Substring(0, 19);
	}

	private static string SafeText(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
	}

	private static string FileTimeText(string path)
	{
		return File.Exists(path) ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss") : "-";
	}

	private void UpdateMindVisionExportRefreshState(MindVisionExportRefreshResult result)
	{
		_mindVisionExportRefreshResult = result ?? MindVisionExportRefreshResult.CreateInitial();
	}

	private void ShowMindVisionExportRefreshWarningIfNeeded()
	{
	}

	private string[] GetMindVisionExportOutputPaths()
	{
		string[] source = new string[6]
		{
			"mindvision-summary.json",
			"mindvision-official-categories.json",
			"mindvision-achievement-category-config.json",
			"mindvision-achievement-reference.json",
			"mindvision-achievements.json",
			"mindvision-achievement-categories.json"
		};
		return (from directory in GetCandidateMindVisionExportDirectories()
			from fileName in source
			select Path.Combine(directory, fileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private IEnumerable<string> GetCandidateMindVisionExportDirectories()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(_mindVisionExportDir))
		{
			hashSet.Add(_mindVisionExportDir);
		}
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mindvision-export");
		if (!string.IsNullOrWhiteSpace(path))
		{
			hashSet.Add(path);
		}
		return hashSet;
	}

	private string FindMindVisionExportFilePath(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}
		return GetCandidateMindVisionExportDirectories().Select((string directory) => Path.Combine(directory, fileName)).Where(File.Exists).OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
	}

	private static string FindLatestExistingPath(IEnumerable<string> paths)
	{
		return (paths ?? Enumerable.Empty<string>()).Where(File.Exists).OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
	}

	private static DateTime? TryGetFileWriteTime(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		try
		{
			return File.GetLastWriteTime(path);
		}
		catch
		{
			return null;
		}
	}

	private static string AppendProcessOutput(string message, string stdout, string stderr)
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(message))
		{
			list.Add(message.Trim());
		}
		string text = NullIfWhiteSpace(CleanMultiline(stdout));
		if (!string.IsNullOrWhiteSpace(text))
		{
			list.Add("stdout: " + text);
		}
		string text2 = NullIfWhiteSpace(CleanMultiline(stderr));
		if (!string.IsNullOrWhiteSpace(text2))
		{
			list.Add("stderr: " + text2);
		}
		return string.Join(" | ", list);
	}
}

internal sealed class FirestoneLoadedData
{
	public string CompletedUpdateDate { get; set; }

	public string MetadataPath { get; set; }

	public int LogFileCount { get; set; }

	public MindVisionExportRefreshResult MindVisionExportRefreshResult { get; set; }

	public List<OwnedCollectionRow> AllCollectionRows { get; set; }

	public List<OwnedCollectionRow> OwnedCollectionRows { get; set; }

	public List<OwnedCollectionRow> SkinCollectionRows { get; set; }

	public List<CompletedAchievementRow> CompletedAchievementRows { get; set; }

	public List<AchievementProgressRow> AchievementProgressRows { get; set; }

	public List<ProfileAchievementSummary> ProfileRows { get; set; }

	public List<OfficialCategoryExportRow> OfficialCategoryExportRows { get; set; }

	public Dictionary<string, OfficialCategoryPathInfo> OfficialTypePathMap { get; set; }

	public Dictionary<string, List<AchievementRelatedCardReference>> AchievementRelatedCardLookup { get; set; }

	public string AchievementRelatedCardMapPath { get; set; }

	public Dictionary<string, List<string>> DualClassAchievementLookup { get; set; }

	public string DualClassAchievementMapPath { get; set; }

	public List<AchievementGuideRow> AchievementGuideRows { get; set; }

	public Dictionary<string, List<AchievementGuideRow>> AchievementGuideLookupByName { get; set; }

	public string AchievementGuideDataPath { get; set; }

	public Dictionary<string, OwnedCollectionRow> CollectionLookupById { get; set; }

	public Dictionary<string, List<OwnedCollectionRow>> CollectionLookupByName { get; set; }
}

internal sealed class DirectJsonBundleSource
{
	public string Key { get; set; }

	public string Label { get; set; }

	public string Path { get; set; }
}

internal sealed class MindVisionExportRefreshResult
{
	public string StatusLabel { get; set; }

	public string Details { get; set; }

	public string ExecutablePath { get; set; }

	public string LatestOutputPath { get; set; }

	public DateTime? LatestOutputTime { get; set; }

	public bool ShouldWarnUser { get; set; }

	public string LatestOutputTimeText
	{
		get
		{
			return LatestOutputTime.HasValue ? LatestOutputTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "-";
		}
	}

	public string Summary
	{
		get
		{
			string text = string.IsNullOrWhiteSpace(Details) ? "-" : Details;
			if (text.Length <= 120)
			{
				return text;
			}
			return text.Substring(0, 117) + "...";
		}
	}

	public static MindVisionExportRefreshResult CreateInitial()
	{
		return Create("未尝试", "当前会话尚未触发运行时导出。", null, null, null, shouldWarnUser: false);
	}

	public static MindVisionExportRefreshResult Create(string statusLabel, string details, string executablePath, string latestOutputPath, DateTime? latestOutputTime, bool shouldWarnUser)
	{
		return new MindVisionExportRefreshResult
		{
			StatusLabel = (string.IsNullOrWhiteSpace(statusLabel) ? "未知" : statusLabel),
			Details = (string.IsNullOrWhiteSpace(details) ? "-" : details),
			ExecutablePath = (string.IsNullOrWhiteSpace(executablePath) ? "-" : executablePath),
			LatestOutputPath = (string.IsNullOrWhiteSpace(latestOutputPath) ? "-" : latestOutputPath),
			LatestOutputTime = latestOutputTime,
			ShouldWarnUser = shouldWarnUser
		};
	}
}

internal sealed class CollectionCard
{
	public string Id { get; set; }

	public int Count { get; set; }

	public int PremiumCount { get; set; }

	public int DiamondCount { get; set; }

	public int SignatureCount { get; set; }

	public int GetOwnedCount()
	{
		return Count + PremiumCount + DiamondCount + SignatureCount;
	}
}

internal sealed class RefreshProgressDialog : Form
{
	private readonly Label _statusLabel;

	public RefreshProgressDialog()
	{
		Text = "炉石成就攻略";
		StartPosition = FormStartPosition.Manual;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ControlBox = false;
		Width = 420;
		Height = 150;
		TopMost = false;
		Padding = new Padding(12);
		BackColor = Color.FromArgb(246, 248, 251);
		Font = new Font("Microsoft YaHei UI", 9f);
		try
		{
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		_statusLabel = new Label
		{
			Dock = DockStyle.Top,
			Height = 46,
			Text = "准备刷新数据...",
			Font = new Font("Microsoft YaHei UI", 10f),
			Padding = new Padding(0, 6, 0, 0),
			ForeColor = Color.FromArgb(30, 41, 59),
			BackColor = Color.FromArgb(246, 248, 251)
		};
		ProgressBar progressBar = new ProgressBar
		{
			Dock = DockStyle.Top,
			Height = 22,
			Style = ProgressBarStyle.Marquee,
			MarqueeAnimationSpeed = 30
		};
		Controls.Add(progressBar);
		Controls.Add(_statusLabel);
		Shown += delegate
		{
			Rectangle workingArea = Screen.FromControl((Owner as Control) ?? this).WorkingArea;
			Location = new Point(workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2), workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
		};
	}

	public void UpdateStatus(string message)
	{
		_statusLabel.Text = string.IsNullOrWhiteSpace(message) ? "正在刷新数据..." : message;
		_statusLabel.Refresh();
		Refresh();
		Application.DoEvents();
	}
}

internal sealed class LocalizedCardRecord
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string CardClass { get; set; }

	public string Type { get; set; }

	public int? Cost { get; set; }

	public bool Collectible { get; set; }

	public string Rarity { get; set; }

	public string Race { get; set; }

	public List<string> Races { get; set; }

	public string Set { get; set; }
}
internal sealed class CardMetadataRow
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string CardClass { get; set; }

	public string Type { get; set; }

	public int? Cost { get; set; }

	public string Rarity { get; set; }

	public string Race { get; set; }

	public string Set { get; set; }
}
internal sealed class OwnedCollectionRow
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string CardClass { get; set; }

	public string Type { get; set; }

	public string CostGroup { get; set; }

	public int? Cost { get; set; }

	public string Rarity { get; set; }

	public string Race { get; set; }

	public string Set { get; set; }

	public int Count { get; set; }

	public int PremiumCount { get; set; }

	public int DiamondCount { get; set; }

	public int SignatureCount { get; set; }

	public int TotalOwned { get; set; }

	public string ClassSort { get; set; }

	public string TypeSort { get; set; }

	public int CostSort { get; set; }
}
internal sealed class CompletedAchievementsFile
{
	public string LastUpdateDate { get; set; }

	public List<CompletedAchievement> Achievements { get; set; }
}
internal sealed class CompletedAchievement
{
	public string Id { get; set; }

	public int NumberOfCompletions { get; set; }
}
internal sealed class CompletedAchievementRow
{
	public string Id { get; set; }

	public string Category { get; set; }

	public int NumberOfCompletions { get; set; }
}
internal sealed class ProfileAchievementSummary
{
	public int Id { get; set; }

	public int AvailablePoints { get; set; }

	public int Points { get; set; }

	public int TotalAchievements { get; set; }

	public int CompletedAchievements { get; set; }
}
internal sealed class AchievementProgressRow
{
	public string AchievementId { get; set; }

	public string Name { get; set; }

	public string Type { get; set; }

	public int MaxProgress { get; set; }

	public int Progress { get; set; }

	public int AckProgress { get; set; }

	public string Trigger { get; set; }

	public string Description { get; set; }

	public bool Completed { get; set; }

	public string LastSeen { get; set; }

	public string SourceLog { get; set; }

	public double ProgressRatio
	{
		get
		{
			if (MaxProgress <= 0)
			{
				return 0.0;
			}
			return (double)Progress / (double)MaxProgress;
		}
	}
}
internal sealed class TrackedAchievementEntry
{
	public string Key { get; set; }

	public string Kind { get; set; }

	public string TrackId { get; set; }

	public string Name { get; set; }

	public string Requirement { get; set; }

	public string AchievementClass { get; set; }

	public string SourceLabel { get; set; }

	public string ProgressText { get; set; }

	public string StatusText { get; set; }

	public string ExtraText { get; set; }

	public string TrackedAt { get; set; }
}
internal sealed class TrackedAchievementDisplayRow
{
	public string TrackKey { get; set; }

	public string TrackKind { get; set; }

	public string TrackId { get; set; }

	public string Name { get; set; }

	public string Requirement { get; set; }

	public string TrackClass { get; set; }

	public string SourceLabel { get; set; }

	public string ProgressText { get; set; }

	public string StatusText { get; set; }

	public string ExtraText { get; set; }

	public string TrackedAtText { get; set; }

	public bool HasGuide { get; set; }

	public string GuideName { get; set; }

	public string GuideRequirement { get; set; }

	public string GuideClass { get; set; }

	public bool IsMissingLiveData { get; set; }
}
internal enum AchievementCategoryMode
{
	Official,
	Class,
	OfficialPrimary
}
internal sealed class ClassAchievementEntry
{
	public string ClassName { get; set; }

	public OfficialAchievementExportRow Achievement { get; set; }
}
internal sealed class AchievementCategoryViewRow
{
	public AchievementCategoryMode Mode { get; set; }

	public string Key { get; set; }

	public string Name { get; set; }

	public int CompletedCount { get; set; }

	public int TotalCount { get; set; }

	public string CompletionRate { get; set; }

	public string CountCompletionRate { get; set; }

	public string PointsProgress { get; set; }

	public string PointCompletionRate { get; set; }

	public string CompletionRateDiff { get; set; }

	public int TotalPoints { get; set; }

	public int Points { get; set; }

	public int AvailablePoints { get; set; }

	public int AttachedCount { get; set; }

	public object AttachedAchievements { get; set; }

	public string DetailText { get; set; }
}
internal sealed class OfficialCalibrationFlatRow
{
	public int categoryId { get; set; }

	public int categorySortOrder { get; set; }

	public string categoryIcon { get; set; }

	public string categoryNameZhCN { get; set; }

	public int subcategoryId { get; set; }

	public int subcategorySortOrder { get; set; }

	public string subcategoryIcon { get; set; }

	public string subcategoryNameZhCN { get; set; }

	public int sectionId { get; set; }

	public int sectionSortOrder { get; set; }

	public string sectionNameZhCN { get; set; }

	public int achievementId { get; set; }

	public int achievementSortOrder { get; set; }

	public string achievementNameZhCN { get; set; }

	public string achievementDescZhCN { get; set; }

	public int quota { get; set; }

	public int points { get; set; }

	public int rewardTrackXp { get; set; }

	public int nextTierId { get; set; }
}
internal sealed class OfficialCategoryExportRow
{
	public int Id { get; set; }

	public string Name { get; set; }

	public string Icon { get; set; }

	public OfficialRuntimeCategoryStats RuntimeStats { get; set; }

	public int AchievementCount { get; set; }

	public List<OfficialAchievementExportRow> Achievements { get; set; }
}
internal sealed class OfficialRuntimeCategoryStats
{
	public int Id { get; set; }

	public string Name { get; set; }

	public string Icon { get; set; }

	public OfficialRuntimeCategoryNumbers Stats { get; set; }
}
internal sealed class OfficialRuntimeCategoryNumbers
{
	public int AvailablePoints { get; set; }

	public int Points { get; set; }

	public int CompletedAchievements { get; set; }

	public int TotalAchievements { get; set; }

	public int Unclaimed { get; set; }
}
internal sealed class OfficialAchievementExportRow
{
	public int AchievementId { get; set; }

	public int Progress { get; set; }

	public int Index { get; set; }

	public int Status { get; set; }

	public ReferenceAchievementExportRow Reference { get; set; }

	public OfficialCategoryReference RootCategory { get; set; }

	public OfficialCategoryReference PrimaryCategory { get; set; }

	public OfficialCategoryReference LeafCategory { get; set; }
}
internal sealed class OfficialCategoryReference
{
	public int Id { get; set; }

	public string Key { get; set; }

	public string Name { get; set; }

	public string Icon { get; set; }
}
internal sealed class OfficialCategoryPathInfo
{
	public OfficialCategoryReference RootCategory { get; set; }

	public OfficialCategoryReference PrimaryCategory { get; set; }

	public OfficialCategoryReference LeafCategory { get; set; }
}
internal sealed class OfficialLeafGroupRow
{
	public string 细分类 { get; set; }

	public int 条目数 { get; set; }

	public int 已完成 { get; set; }
}
internal sealed class OfficialPrimaryGroupDisplayRow
{
	public string Key { get; set; }

	public string RootCategory { get; set; }

	public string PrimaryCategory { get; set; }

	public int LeafCount { get; set; }

	public int ItemCount { get; set; }

	public int CompletedCount { get; set; }

	public string CompletionProgress { get; set; }

	public List<OfficialAchievementExportRow> Achievements { get; set; }
}
internal sealed class ReferenceAchievementExportRow
{
	public string Id { get; set; }

	public int HsAchievementId { get; set; }

	public int HsSectionId { get; set; }

	public int HsRewardTrackXp { get; set; }

	public string Name { get; set; }

	public string DisplayName { get; set; }

	public string Text { get; set; }

	public string CompletedText { get; set; }

	public string EmptyText { get; set; }

	public string Type { get; set; }

	public string Icon { get; set; }

	public string DisplayCardId { get; set; }

	public string DisplayCardType { get; set; }

	public int Points { get; set; }

	public int Priority { get; set; }

	public int Quota { get; set; }

	public bool Root { get; set; }
}
internal sealed class AchievementRelatedCardMapRow
{
	public string sheet { get; set; }

	public string version { get; set; }

	public string profession { get; set; }

	public string achievementName { get; set; }

	public string description { get; set; }

	public string reward { get; set; }

	public List<string> relatedRaw { get; set; }

	public List<AchievementRelatedCardEntry> relatedCards { get; set; }
}

internal sealed class AchievementRelatedCardEntry
{
	public string cardId { get; set; }

	public string cardName { get; set; }
}

internal sealed class AchievementRelatedCardReference
{
	public string CardId { get; set; }

	public string CardName { get; set; }
}
internal sealed class AchievementGuideRow
{
	public string post_id { get; set; }

	public string date { get; set; }

	public string title { get; set; }

	public string series { get; set; }

	public string category { get; set; }

	public string sub_category { get; set; }

	public string achievement_name { get; set; }

	public string requirement { get; set; }

	public int deck_count { get; set; }

	public string recommended_deck_codes { get; set; }

	public string idea { get; set; }

	public string source_url { get; set; }

	public string editor_id { get; set; }

	public int guide_slot { get; set; }

	public string local_text { get; set; }
}

internal sealed class MultiGuideManagementRow
{
	public string GroupKey { get; set; }

	public string AchievementName { get; set; }

	public string AchievementClass { get; set; }

	public string GuideClassFilter { get; set; }

	public int VersionCount { get; set; }

	public string VersionList { get; set; }

	public string LatestDateText { get; set; }

	public string LatestEditor { get; set; }

	public string PreviewText { get; set; }
}

internal sealed class AchievementClassRule
{
	public string ClassName { get; private set; }

	public string[] Keywords { get; private set; }

	public AchievementClassRule(string className, params string[] keywords)
	{
		ClassName = className;
		Keywords = ((keywords != null) ? keywords.Select((string keyword) => (keyword == null) ? string.Empty : keyword.ToLowerInvariant()).ToArray() : new string[0]);
	}
}
