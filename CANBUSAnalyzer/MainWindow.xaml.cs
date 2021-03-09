using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Wpf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TeslaSCAN;
using AssociatedDBC;

namespace CANBUS
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window, IDisposable
  {
    private string[] allowedLogFormat = { "TXT", "CSV", "ASC" };
    private static class LogFileExtension
    {
        public const string ASC = ".asc";
        public const string CSV = ".csv";
        public const string TXT = ".txt";
    }

    private bool disposed = false;
    bool run = false;
    ObservableCollection<StringWithNotify> runningTasks = new ObservableCollection<StringWithNotify>();
    private Timer timer;
    public Stopwatch stopwatch;
    private const int CANOpenIDLength = 3;
    private const int SAEIDLength = 8;
    private StreamReader inputStream;
    private Parser parser;
    //private uint interpret_source;
    private uint packetId;
    private int busId;
    private long prevUpdate;
    private string currentLogFile;
    private long currentLogSize;
    private string currentTitle;
    private Thread thread;
    private ICANLogParser logParser;
    ObservableCollection<DBCwAssociatedBus>  dbcList = new ObservableCollection<DBCwAssociatedBus>();

    //SortedDictionary<int, char> batterySerial = new SortedDictionary<int, char>();

    private OxyPlot.Wpf.LinearAxis primaryAxis = new OxyPlot.Wpf.LinearAxis();
    private OxyPlot.Wpf.LinearAxis secondaryAxis = new OxyPlot.Wpf.LinearAxis();
    

    public MainWindow()
    {

      InitializeComponent();

      PathList.ItemsSource = runningTasks;       
      PopulateDropdown(PacketMode, PacketDefinitions.GetAll(), "Source", "Name");
      HitsDataGrid.ItemsSource = parser.items;
      DBCFilesGrid.ItemsSource = dbcList;

      stopwatch = new Stopwatch();
      stopwatch.Start();
        
      OxyPlot.Wpf.TimeSpanAxis timeAxis = new OxyPlot.Wpf.TimeSpanAxis();
      timeAxis.Position = AxisPosition.Bottom;
      Graph.Axes.Add(timeAxis);      
        primaryAxis.Key = "Primary";
        primaryAxis.Position = AxisPosition.Left;
        primaryAxis.Title = "●";
        primaryAxis.TitlePosition = 1;
        secondaryAxis.Key = "Secondary";
        secondaryAxis.Position = AxisPosition.Right;
        secondaryAxis.Title = "▶";
        secondaryAxis.TitleFontSize = 8;
        secondaryAxis.TitlePosition = 1;

        //PathList.Columns[0].SortDirection = ListSortDirection.Ascending;

        if (CANBUS.App.StartupLogFilename != null)
        {
            StartParseLog(CANBUS.App.StartupLogFilename);
        }

        dbcList.CollectionChanged += (s, e) =>
        {
            Start_Parsing();
        };

    }

    private void loop()
    {
      while (run)
      {
        timerCallback(null);
      }
    }

        private void addSeriesToGraph(KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>> series, uint yAxis)
    {
            if (series.Key.Item2.Any())
            {
                foreach (var dataPoint in series.Value)
                {
                    dataPoint.Description = series.Key.Item2.Any() ? " (" + series.Key.Item2.First(x => x.Key == dataPoint.Y).Value + ")" : string.Empty;
                }
            }
            
            switch (yAxis)
            {
                case 1:
                    Graph.Series.Add(
                        new LineSeries()
                        {
                            TrackerFormatString = "{0}\n{1}: {2:hh\\:mm\\:ss\\.fff}\nY: {4} {Description}",
                                //+ ((series.Key.Item2.Count > 0)? "\n" + string.Join("\n", series.Key.Item2) : ""),
                                //+ ((series.Key.Item2.Count > 0)? " (" + series.Key.Item2.First(x => x.Key.ToString() == "{4}").Value + ")" : ""),
                            StrokeThickness = 1,
                            LineStyle = LineStyle.Solid,
                            Title = series.Key.Item1,
                            ItemsSource = series.Value,
                            MarkerType = MarkerType.Circle,
                            YAxisKey = "Primary"
                    });
                break;
            case 2:
                Graph.Series.Add(
                    new LineSeries()
                    {
                        TrackerFormatString = "{0}\n{1}: {2:hh\\:mm\\:ss\\.fff}\nY: {4} {Description}",
                            //+ ((series.Key.Item2.Count > 0) ? "\n" + string.Join("\n", series.Key.Item2) : ""),
                        StrokeThickness = 1,
                        LineStyle = LineStyle.Solid,
                        Title = series.Key.Item1,
                        ItemsSource = series.Value,
                        MarkerType = MarkerType.Triangle,
                        YAxisKey = "Secondary"
                    });
                break;
            }
    }

    private void setGraphSeriesList(List<KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>> seriesList)
    {
        Graph.Axes.Remove(primaryAxis);
        Graph.Axes.Remove(secondaryAxis);

        double yAxisOffset1 = 0.04;
        double yAxisOffset2 = 0.02;

        double maxRatioToExistingAxis = 4;

        double? min1 = null;
        double? max1 = null;
        double? min2 = null;
        double? max2 = null;

        KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>> seriesWithHighestValue = seriesList.MaxBy(x => x.Value.MaxBy(z => z.Y).Y);
        KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>> seriesWithLowestValue = seriesList.MinBy(x => x.Value.MinBy(z => z.Y).Y);
        KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>> seriesWithGreatestRange = seriesList.MaxBy(x => x.Value.MaxBy(m => m.Y).Y - x.Value.MinBy(m => m.Y).Y);

        if(seriesWithGreatestRange.Key == seriesWithHighestValue.Key || seriesWithGreatestRange.Key == seriesWithLowestValue.Key)
            {
                min1 = seriesWithGreatestRange.Value.MinBy(m => m.Y).Y;
                max1 = seriesWithGreatestRange.Value.MaxBy(m => m.Y).Y;
                primaryAxis.Minimum = min1.Value - yAxisOffset1 * Math.Abs(max1.Value + min1.Value) / 2;
                primaryAxis.Maximum = max1.Value + yAxisOffset2 * Math.Abs(max1.Value + min1.Value) / 2;
                Graph.Axes.Add(primaryAxis);

                addSeriesToGraph(seriesWithGreatestRange, 1);
                seriesList.Remove(seriesWithGreatestRange);
            }
        else
            {
                double absMax = seriesWithHighestValue.Value.MaxBy(m => m.Y).Y;
                double absMin = seriesWithLowestValue.Value.MinBy(m => m.Y).Y;
                double greatestRangeMax = seriesWithGreatestRange.Value.MaxBy(m => m.Y).Y;
                double greatestRangeMin = seriesWithGreatestRange.Value.MinBy(m => m.Y).Y;

                if(absMax - greatestRangeMax < greatestRangeMin - absMin)
                {
                    min1 = greatestRangeMin;
                    max1 = absMax;
                    primaryAxis.Minimum = min1.Value - yAxisOffset1 * Math.Abs(max1.Value + min1.Value) / 2;
                    primaryAxis.Maximum = max1.Value + yAxisOffset2 * Math.Abs(max1.Value + min1.Value) / 2;
                    Graph.Axes.Add(primaryAxis);

                    addSeriesToGraph(seriesWithGreatestRange, 1);
                    seriesList.Remove(seriesWithGreatestRange);

                    addSeriesToGraph(seriesWithHighestValue, 1);
                    seriesList.Remove(seriesWithHighestValue);
                }
                else
                {
                    min1 = absMin;
                    max1 = greatestRangeMax;
                    primaryAxis.Minimum = min1.Value - yAxisOffset1 * Math.Abs(max1.Value + min1.Value) / 2;
                    primaryAxis.Maximum = max1.Value + yAxisOffset2 * Math.Abs(max1.Value + min1.Value) / 2;
                    Graph.Axes.Add(primaryAxis);

                    addSeriesToGraph(seriesWithGreatestRange, 1);
                    seriesList.Remove(seriesWithGreatestRange);

                    addSeriesToGraph(seriesWithLowestValue, 1);
                    seriesList.Remove(seriesWithLowestValue);
                }

            }

        while(seriesList.Count > 0 && !max2.HasValue) //loop to define secondaryAxis
            {
                bool seriesDeleted = false;
                seriesWithHighestValue = seriesList.MaxBy(x => x.Value.MaxBy(z => z.Y).Y);
                seriesWithLowestValue = seriesList.MinBy(x => x.Value.MinBy(z => z.Y).Y);
                seriesWithGreatestRange = seriesList.MaxBy(x => x.Value.MaxBy(m => m.Y).Y - x.Value.MinBy(m => m.Y).Y);

                List<KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>> limitCaseSeries = new List<KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>>();
                foreach (var item in new KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>[] { seriesWithHighestValue, seriesWithLowestValue, seriesWithGreatestRange } )
                {
                    if (!limitCaseSeries.Contains(item))
                        limitCaseSeries.Add(item);
                }

                foreach (var series in limitCaseSeries)
                {
                    double seriesMin = series.Value.MinBy(m => m.Y).Y;
                    double seriesMax = series.Value.MaxBy(m => m.Y).Y;

                    if (seriesMax <= max1 && seriesMin >= min1 && ((seriesMax - seriesMin) > (max1 - min1) / maxRatioToExistingAxis || seriesMax == seriesMin)) //if range fits in primaryAxis
                    {
                        addSeriesToGraph(series, 1);
                        seriesList.Remove(series);
                        seriesDeleted = true;
                        continue;
                    }

                    if (Math.Max(seriesMax, max1.Value) - Math.Min(seriesMin, min1.Value) > (max1 - min1) * maxRatioToExistingAxis) //if too big/low for primaryAxis
                    {
                        min2 = seriesMin;
                        max2 = seriesMax;
                        secondaryAxis.Minimum = min2.Value - yAxisOffset2 * Math.Abs(max2.Value + min2.Value) / 2;
                        secondaryAxis.Maximum = max2.Value + yAxisOffset1 * Math.Abs(max2.Value + min2.Value) / 2;
                        Graph.Axes.Add(secondaryAxis);

                        addSeriesToGraph(series, 2);
                        seriesList.Remove(series);
                        seriesDeleted = true;
                        break;
                    }
                }

                if (seriesDeleted) continue;

                seriesWithHighestValue = seriesList.MaxBy(x => x.Value.MaxBy(z => z.Y).Y);
                seriesWithLowestValue = seriesList.MinBy(x => x.Value.MinBy(z => z.Y).Y);
                seriesWithGreatestRange = seriesList.MaxBy(x => x.Value.MaxBy(m => m.Y).Y - x.Value.MinBy(m => m.Y).Y);

                if (seriesWithGreatestRange.Key == seriesWithHighestValue.Key || seriesWithGreatestRange.Key == seriesWithLowestValue.Key)
                {
                    min2 = seriesWithGreatestRange.Value.MinBy(m => m.Y).Y;
                    max2 = seriesWithGreatestRange.Value.MaxBy(m => m.Y).Y;
                    secondaryAxis.Minimum = min2.Value - yAxisOffset2 * Math.Abs(max2.Value + min2.Value) / 2;
                    secondaryAxis.Maximum = max2.Value + yAxisOffset1 * Math.Abs(max2.Value + min2.Value) / 2;
                    Graph.Axes.Add(secondaryAxis);

                    addSeriesToGraph(seriesWithGreatestRange, 2);
                    seriesList.Remove(seriesWithGreatestRange);
                }
                else
                {
                    double absMax = seriesWithHighestValue.Value.MaxBy(m => m.Y).Y;
                    double absMin = seriesWithLowestValue.Value.MinBy(m => m.Y).Y;
                    double greatestRangeMax = seriesWithGreatestRange.Value.MaxBy(m => m.Y).Y;
                    double greatestRangeMin = seriesWithGreatestRange.Value.MinBy(m => m.Y).Y;

                    if (absMax - greatestRangeMax < greatestRangeMin - absMin)
                    {
                        min2 = greatestRangeMin;
                        max2 = absMax;
                        secondaryAxis.Minimum = min2.Value - yAxisOffset2 * Math.Abs(max2.Value + min2.Value) / 2;
                        secondaryAxis.Maximum = max2.Value + yAxisOffset1 * Math.Abs(max2.Value + min2.Value) / 2;
                        Graph.Axes.Add(secondaryAxis);

                        addSeriesToGraph(seriesWithGreatestRange, 2);
                        seriesList.Remove(seriesWithGreatestRange);

                        addSeriesToGraph(seriesWithHighestValue, 2);
                        seriesList.Remove(seriesWithHighestValue);
                    }
                    else
                    {
                        min2 = absMin;
                        max2 = greatestRangeMax;
                        secondaryAxis.Minimum = min2.Value - yAxisOffset2 * Math.Abs(max2.Value + min2.Value) / 2;
                        secondaryAxis.Maximum = max2.Value + yAxisOffset1 * Math.Abs(max2.Value + min2.Value) / 2;
                        Graph.Axes.Add(secondaryAxis);

                        addSeriesToGraph(seriesWithGreatestRange, 2);
                        seriesList.Remove(seriesWithGreatestRange);

                        addSeriesToGraph(seriesWithLowestValue, 2);
                        seriesList.Remove(seriesWithLowestValue);
                    }
                }
            }

        foreach (var series in seriesList)
          {
                double seriesMin = series.Value.MinBy(m => m.Y).Y;
                double seriesMax = series.Value.MaxBy(m => m.Y).Y;
                bool seriesFitsAxis1 = seriesMax <= max1 && seriesMin >= min1;
                bool seriesFitsAxis2 = seriesMax <= max2 && seriesMin >= min2;

                if (seriesFitsAxis1 && seriesFitsAxis2)
                {
                    if (max2 - min2 < max1 - min1)
                    {
                        addSeriesToGraph(series, 2);
                    }
                    else
                    {
                        addSeriesToGraph(series, 1);
                    }
                    continue;
                }

                else //if (!seriesFitsAxis1 || !seriesFitsAxis2)
                {
                    double deltaTop1 = Math.Max(seriesMax-max1.Value, 0);
                    double deltaBottom1 = Math.Max(min1.Value-seriesMin, 0);
                    double deltaTop2 = Math.Max(seriesMax - max2.Value, 0);
                    double deltaBottom2 = Math.Max(min2.Value - seriesMin, 0);

                    double relativeStretch1 = (deltaTop1 + deltaBottom1) / (max1.Value - min1.Value);
                    double relativeStretch2 = (deltaTop2 + deltaBottom2) / (max2.Value - min2.Value);

                    if (seriesMax == seriesMin)
                    {
                        if(relativeStretch2 < relativeStretch1)
                        {
                            Graph.Axes.Remove(secondaryAxis);
                            min2 = Math.Min(min2.Value, seriesMin);
                            max2 = Math.Max(max2.Value, seriesMax);
                            secondaryAxis.Minimum = min2.Value - yAxisOffset2 * Math.Abs(max2.Value + min2.Value) / 2;
                            secondaryAxis.Maximum = max2.Value + yAxisOffset1 * Math.Abs(max2.Value + min2.Value) / 2;
                            Graph.Axes.Add(secondaryAxis);

                            addSeriesToGraph(series, 2);
                        }
                        else
                        {
                            Graph.Axes.Remove(primaryAxis);
                            min1 = Math.Min(min1.Value, seriesMin);
                            max1 = Math.Max(max1.Value, seriesMax);
                            primaryAxis.Minimum = min1.Value - yAxisOffset1 * Math.Abs(max1.Value + min1.Value) / 2;
                            primaryAxis.Maximum = max1.Value + yAxisOffset2 * Math.Abs(max1.Value + min1.Value) / 2;
                            Graph.Axes.Add(primaryAxis);

                            addSeriesToGraph(series, 1);
                        }
                    }

                    else //seriesMax != seriesMin
                    {
                        double seriesRatioInvNewPlot1 = (max1.Value - min1.Value + deltaTop1 + deltaBottom1) / (seriesMax - seriesMin);
                        double seriesRatioInvNewPlot2 = (max2.Value - min2.Value + deltaTop2 + deltaBottom2) / (seriesMax - seriesMin);

                        if (Math.Pow(relativeStretch2, 2) + Math.Pow(seriesRatioInvNewPlot2, 2) < Math.Pow(relativeStretch1, 2) + Math.Pow(seriesRatioInvNewPlot1, 2))
                        {

                            Graph.Axes.Remove(secondaryAxis);
                            min2 = Math.Min(min2.Value, seriesMin);
                            max2 = Math.Max(max2.Value, seriesMax);
                            secondaryAxis.Minimum = min2.Value - yAxisOffset2 * Math.Abs(max2.Value + min2.Value) / 2;
                            secondaryAxis.Maximum = max2.Value + yAxisOffset1 * Math.Abs(max2.Value + min2.Value) / 2;
                            Graph.Axes.Add(secondaryAxis);

                            addSeriesToGraph(series, 2);
                        }
                        else
                        {
                            Graph.Axes.Remove(primaryAxis);
                            min1 = Math.Min(min1.Value, seriesMin);
                            max1 = Math.Max(max1.Value, seriesMax);
                            primaryAxis.Minimum = min1.Value - yAxisOffset1 * Math.Abs(max1.Value + min1.Value) / 2;
                            primaryAxis.Maximum = max1.Value + yAxisOffset2 * Math.Abs(max1.Value + min1.Value) / 2;
                            Graph.Axes.Add(primaryAxis);

                            addSeriesToGraph(series, 1);
                        }
                    } 
                }
                
            }
            
      Graph.InvalidatePlot(true);
    }

    private void StartParseLog(string fileName)
    {
      run = false;
      parser.items.Clear();
      HitsDataGrid.Items.Refresh();
      runningTasks.Clear();
      PathList.Items.Refresh();
    
      inputStream = File.OpenText(fileName);

      Title = fileName;
      FileInfo f = new FileInfo(fileName);
      Title += " " + f.Length / 1024 + "k";
      currentLogFile = fileName;
      currentLogSize = f.Length;
      currentTitle = Title;
      string fileExt = Path.GetExtension(currentLogFile).ToLower();
      switch (fileExt)
      {
        case LogFileExtension.ASC:
            logParser = new VectorASCParser();
            break;
        
        case LogFileExtension.CSV:
            logParser = new SavvyCSVParser();
            break;
            
        default:
            logParser = null;
            break;
      }

        //runningTasks.Clear();
        timer?.Dispose();
      timer = null;

      foreach (var v in parser.items.Values)
      {
        if (v.Points == null)
        {
          v.Points = new ConcurrentStack<CustomDataPoint>();
        }
        else
        {
          v.Points.Clear();
        }
      }

      if (thread != null)
      {
        thread.Abort();
      }
      //thread.Abort(); to abort currently loading Trace
      //thread.Join(); to keep currently loading Trace and create new one

      timer = new Timer(updateTitle, null, 1000, 1000);
      run = true;
      thread = new Thread(loop);
      thread.IsBackground = true;
      thread.Start();
    }

    private void timerCallback(object state) {
      try {
        string line;
        string timestamp = null;
        line = inputStream.ReadLine();

        if (inputStream.EndOfStream) {
          run = false;
        }

        if (logParser != null) {
          line = logParser.ParseLine(line, out timestamp);
        }

        if (line == null) {
          return;
        }

        bool knownPacket;
        parser.Parse(line + "\n", 0, timestamp, out knownPacket);
        int idLength = line.IndexOf(" ", 0);

        string s;
        s = line;
        
        int busLength = line.IndexOf(" ", idLength + 1) - idLength -1;
        int bus = int.Parse(line.Substring(idLength + 1, busLength));

        for (int i = idLength + 1 + busLength + 1 + 2; i < s.Length; i += 3)
        {
            s = s.Insert(i, " ");
        }

        string p = "";
        p = line.Substring(0, idLength);
        

        uint pac;

        if (uint.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out pac)) {
          var l = runningTasks.Where(x => x.Str.StartsWith(s.Substring(0,idLength + 1 + busLength + 1))).FirstOrDefault();
          if (l == null) {
            Dispatcher.Invoke(() =>
            runningTasks.Add(new StringWithNotify(pac, s, parser, this)));
          } else {
            l.Str = s;
          }

          if (l != null) {
            l.Used = parser.items.Any(x => x.Value.packetId == pac);
            l.Count++;
            string desc = "";
            int counter = 0;

            if (parser.packetTitles != null)
              if (parser.packetTitles.ContainsKey((int)pac)) {
                if (string.IsNullOrEmpty(l.Verbose))
                  l.Verbose = parser.packetTitles[(int)pac];
              } else {
                foreach (var item in parser.items.Where(x => x.Value.packetId == pac && (x.Value.bus == bus || x.Value.bus == -1))) {
                  counter++;
                  if (counter > 1) {
                    break;
                  }
                  desc += item.Value.messageName;
                }
                l.Verbose = desc;
              }

          }

          if (prevUpdate < stopwatch.ElapsedMilliseconds)
          {
            Dispatcher.BeginInvoke((Action)(() => { Graph.InvalidatePlot(true); }));
            prevUpdate = stopwatch.ElapsedMilliseconds + 1000;
          }

        }
      }
      catch (Exception e) {
        Console.WriteLine(e.Message);
      }
    }

    private void updateTitle(object state)
    {
      //if (currentLogSize > 0)
      Dispatcher.Invoke(() =>
      {
        Title = currentTitle + " - " + parser.numUpdates + " packets per second";
        parser.numUpdates = 0;
      }
      );
    }

    private void Button_Click_Load(object sender, RoutedEventArgs e)
    {
      run = false;
      OpenFileDialog openFileDialog1 = new OpenFileDialog();
      openFileDialog1.Filter = "All log files|*.asc;*csv;*.txt|Vector ASCII|*.asc|SavvyCAN CSV|*.csv";
      if ((bool)openFileDialog1.ShowDialog())
      {
        if (openFileDialog1.FileName != null)
        {
          StartParseLog(openFileDialog1.FileName);
        }
      }
    }

    private void Button_Click_NextLog(object sender, RoutedEventArgs e)
    {
      try
      {
        var path = Path.GetDirectoryName(currentLogFile);
        var fileNames = Directory.GetFiles(path, "*" + Path.GetExtension(currentLogFile));
        for (int i = 0; i < fileNames.Count(); i++)
        {
          if (fileNames[i] == currentLogFile)
          {
            StartParseLog(fileNames[i + 1]);
            break;
          }
        }
      }
      catch (Exception)
      {
      }
    }

    private void Button_Click_PrevtLog(object sender, RoutedEventArgs e)
    {
      try
      {
        var path = Path.GetDirectoryName(currentLogFile);
        var fileNames = Directory.GetFiles(path, "*" + Path.GetExtension(currentLogFile));
        for (int i = 0; i < fileNames.Count(); i++)
        {
          if (fileNames[i] == currentLogFile)
          {
            StartParseLog(fileNames[i - 1]);
            break;
          }
        }
      }
      catch (Exception)
      {
      }
    }

    private void Button_DisplayHelp(object sender, RoutedEventArgs e)
    {
            WindowCollection CBAWindows = Application.Current.Windows;
            foreach (var win in CBAWindows)
            {
                if (win.ToString() == "CANBUS.HelpWindow")
                {
                    return;
                }                    
            }
            new HelpWindow().Show();
    }

    private void PacketMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
            PacketDefinitions.DefinitionSource selectedDefs = (PacketDefinitions.DefinitionSource)PacketMode.SelectedValue;

            if (selectedDefs == PacketDefinitions.DefinitionSource.DBCFile)
            {
                OpenFileDialog openFileDialog1 = new OpenFileDialog();
                openFileDialog1.Filter = "DBC|*.dbc";
                openFileDialog1.Multiselect = true;
                string[] selectedDBCs = null;
                if ((bool)openFileDialog1.ShowDialog())
                {
                    if (thread != null && thread.IsAlive)
                        thread.Abort();
                    selectedDBCs = openFileDialog1.FileNames;
                }

                if (selectedDBCs != null)
                {
                    foreach (var element in selectedDBCs)
                    {
                        var newItem = new DBCwAssociatedBus();
                        if (dbcList.Where(x => x.Path == element).Count() == 0)
                        {
                            newItem.Path = element;
                            newItem.Bus = "-1";
                            //newItem.isJ1939 = false;
                            dbcList.Add(newItem);
                        }

                    }
                    DBCFilesGrid.Items.Refresh();
                    Start_Parsing();
                }
            }
            parser = Parser.FromSource(this, PacketDefinitions.DefinitionSource.DBCFile, dbcList);
        }

    private void Button_Click_DBC(object sender, RoutedEventArgs e)
    {
        PacketMode.SelectedIndex = 0;
        PacketMode.SelectedIndex = 1;
    }

    private void PopulateDropdown(ComboBox cmb, System.Collections.IEnumerable items, string valueMember, string displayMember)
    {
        cmb.SelectedValuePath = valueMember;
        cmb.DisplayMemberPath = displayMember;
        cmb.ItemsSource = items;
        cmb.UpdateLayout();
        if (cmb.HasItems) cmb.SelectedIndex = 0;
    }

    private void HitsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      try
      {
        Graph.Series.Clear();

        List<KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>> seriesList = new List<KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>>();
        foreach (var s in HitsDataGrid.SelectedItems)
        {
          var i = (KeyValuePair<string, ListElement>)s;
          seriesList.Add(new KeyValuePair<Tuple<string, List<KeyValuePair<long, string>>>, ConcurrentStack<CustomDataPoint>>(Tuple.Create(i.Key, i.Value.VT_List), i.Value.Points));
        }
        setGraphSeriesList(seriesList);
      }
      catch (Exception ex)
      {
        if(ex.Message != "no data")
            MessageBox.Show(ex.Message);
      }
      Graph.InvalidatePlot(true);
    }

    private void PathList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      try
      {
        List<Tuple<uint, int>> packetList = new List<Tuple<uint, int>>();
        string packetIdStr = null;
        string busIdStr = null;
        string s = null;

        foreach (var sel in PathList.SelectedItems)
        {
          s = ((StringWithNotify)sel).Str;
          int idLength = s.IndexOf(" ", 0);
          packetIdStr = s.Substring(0, idLength);
          int busLength = s.IndexOf(" ", idLength + 1) - idLength - 1;
          busIdStr = s.Substring(idLength + 1, busLength);
          uint.TryParse(packetIdStr, System.Globalization.NumberStyles.HexNumber, null, out packetId);
          int.TryParse(busIdStr, out busId);
          packetList.Add(Tuple.Create(packetId, busId));
        }

        foreach (var sel in runningTasks)
        {
          uint.TryParse(packetIdStr, System.Globalization.NumberStyles.HexNumber, null, out packetId);
          int.TryParse(busIdStr, out busId);
          packetList.Add(Tuple.Create(packetId, busId));
        }

        var items = parser.items.Where(x => (packetList.Contains(Tuple.Create(x.Value.packetId, x.Value.bus))
                                        || (packetList.Any(m => m.Item1 == x.Value.packetId) && x.Value.bus == -1)));

        HitsDataGrid.ItemsSource = items;
        HitsDataGrid.DataContext = parser.items;

        //List<KeyValuePair<string, ConcurrentStack<DataPoint>>> seriesList = new List<KeyValuePair<string, ConcurrentStack<DataPoint>>>();
        //foreach (var i in items)
        //{
        //  seriesList.Add(new KeyValuePair<string, ConcurrentStack<DataPoint>>(i.Value.name, i.Value.Points));
        //}
        //setGraphSeriesList(seriesList);
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message);
      }
      Graph.InvalidatePlot(true);
    }

    private void Window_Closed(object sender, EventArgs e)
    {
      run = false;
      App.Current.Shutdown();
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!disposed)
      {
        disposed = true;

        if (timer != null)
        {
          timer.Dispose();
          timer = null;
        }

        if (disposing)
        {
          GC.SuppressFinalize(this);
        }
      }
    }

    public void Dispose()
    {
      if (!disposed)
      {
        Dispose(true);
      }
    }

    ~MainWindow()
    {
      Dispose(false);
    }

    private void Start_Parsing()
    {
        parser = Parser.FromSource(this, PacketDefinitions.DefinitionSource.DBCFile, dbcList);
        if(currentLogFile!=null)
            StartParseLog(currentLogFile);
    }

    private void AbortThread(object  sender, EventArgs  e)
    {
        if(thread != null && thread.IsAlive)
            thread.Abort();
    }

    private void DBCFilesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Start_Parsing();
    }

    private void dataGridPathList_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        string[] draggedLogFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

        if (draggedLogFiles.Length == 1 && allowedLogFormat.Contains(draggedLogFiles[0].Split('.').Last().ToUpper()))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void dataGridDBC_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        string[] draggedDBCFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (string file in draggedDBCFiles)
            {
                if (file.Split('.').Last().ToUpper() == "DBC")
                {
                    e.Effects = DragDropEffects.Copy;
                }
            }
    }

    private void dataGridPathList_DragDrop(object sender, DragEventArgs e)
    {
        string[] draggedLogFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

        if (draggedLogFiles.Length == 1 && allowedLogFormat.Contains(draggedLogFiles[0].Split('.').Last().ToUpper()))
        {
            currentLogFile = draggedLogFiles[0];
            StartParseLog(currentLogFile);
        }
    }

    private void dataGridDBC_DragDrop(object sender, DragEventArgs e)
    {
        string[] draggedDBCFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (string file in draggedDBCFiles)
            {
                if (file.Split('.').Last().ToUpper() == "DBC")
                {
                    var newItem = new DBCwAssociatedBus();
                    if (dbcList.Where(x => x.Path == file).Count() == 0)
                    {
                        newItem.Path = file;
                        newItem.Bus = "-1";
                        dbcList.Add(newItem);
                    }
                }
            }
    }

    }

}
