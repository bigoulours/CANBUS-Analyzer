using OxyPlot;
using OxyPlot.Wpf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CANBUS;

namespace TeslaSCAN
{
  public class ListElement : INotifyPropertyChanged
  {
    public uint packetId { get; set; }
    public int bus { get; set; }
    public string idHex
    {
      get
      {
        return System.Convert.ToString(packetId, 16).ToUpper().PadLeft(3, '0');
      }
    }
    public string name { get; set; }
    public string messageName { get; set; }
    private object value;
    public object Current
    {
      get
      {
        return value;
      }
    }

    public ConcurrentStack<CustomDataPoint> Points { get; set; }
    public LineSeries Line { get; private set; }

    public string unit { get; set; }
    public int index;
    public double max { get; set; }
    public double min { get; set; }
    public List<KeyValuePair<long, string>> VT_List;
    public bool changed;
    public bool selected;
    public string tag;
    public int viewType;
    public long timeStamp;
    public List<int> bits = new List<int>();
    public int numBits
    {
      get
      {
        return bits.Any() ? bits.Last() - bits.First() + 1 : 0;
      }
    }
    public double scaling { get; set; }
    public object previous;

    public override string ToString()
    {
      return value.ToString();
    }

    public void SetValue(object val, string timestamp)
    {
      previous = value;
      changed = value != val;
      value = val;
      TimeSpan formatedTimestamp = TimeSpan.FromMilliseconds(System.Convert.ToDouble(timestamp)/1000);

      if (changed) {
        NotifyPropertyChanged("Current");
      }

      if (!(val is string)) {

        double valueD = System.Convert.ToDouble(val);

        if (valueD > max) {
          max = valueD;
        }

        if (valueD < min) {
          min = valueD;
        }


#if VERBOSE
      Console.WriteLine(this.name + " " + val);
#endif
        Points.Push(new CustomDataPoint(OxyPlot.Axes.DateTimeAxis.ToDouble(formatedTimestamp), valueD));
        NotifyPropertyChanged("Points");
      }
      /*if (Points.Count > 1)
      {
        double dt = Points[Points.Count - 1].X - Points[Points.Count - 2].X;
        double a = dt / (0.99 + dt);
        Points[Points.Count - 1] = new DataPoint(Points[Points.Count - 1].X, Points[Points.Count - 2].Y * (1-a) + Points[Points.Count - 1].Y * a);
      }*/
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void NotifyPropertyChanged(String propertyName = "")
    {
     if (PropertyChanged != null)
     {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
     }
    }

    public ListElement(string name, string messageName, string unit, string tag, int index, object value, uint packetId, int bus, List<KeyValuePair<long, string>> VT_List)
    {
      this.name = name;
      this.messageName = messageName;
      this.bus = bus;
      this.unit = unit;
      this.tag = tag;
      this.index = index;
      this.value = value;
      this.VT_List = VT_List;
      if (value is double)
        this.max = this.min = (double)value;
      this.packetId = packetId;
      changed = true;

      this.Points = new ConcurrentStack<CustomDataPoint>();
    }
  }
}
