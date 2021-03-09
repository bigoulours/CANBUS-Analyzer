using System;
using System.Collections.Generic;
using AssociatedDBC;

namespace TeslaSCAN
{
  public class Packet
  {
    public uint id;
    public int bus;
    Parser parser;
    public List<Value> values;
    public double currentMultiplexer;

    public Packet(uint id, DBCwAssociatedBus dbcFile, Parser parser)
    {
      this.id = id;
      this.bus = int.Parse(dbcFile.Bus);
      this.parser = parser;
      values = new List<Value>();
    }

    public void AddValue(string name, string messageName, string busStr, string unit, string tag, Func<byte[], object> formula, List<KeyValuePair<long, string>> VT_List, int[] additionalPackets = null)
    {
      List<uint> list = new List<uint>();
      list.Add(id);
      if (additionalPackets != null)
      {
        foreach (uint i in additionalPackets)
        {
          list.Add(i);
        }
      }

      values.Add(new Value(name, messageName, busStr, unit, tag, formula, list, VT_List));
    }

    public void Update(byte[] bytes, string timestamp)
    {
      foreach (var val in values)
      {
        //if (val.formula != null)
        {
          try
          {
            {
              // sorts by packet ID
              if (val.formula!=null)
                parser.UpdateItem(val.name, val.messageName, val.unit, val.tag, val.index, val.formula(bytes), id, int.Parse(val.bus), val.VT_List, timestamp);
                //object Debugobj = val.formula(bytes);
                //int Debugint = val.index;
            }
          }
          catch (Exception e)
          {
            Console.WriteLine(e.ToString());
          }
        }
      }
    }
  }
}
