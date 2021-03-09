using CANBUS;
using DBCLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssociatedDBC;

namespace TeslaSCAN
{
  [Serializable]
  public class Parser
  {   
    private PacketDefinitions _definitions;
    protected internal PacketDefinitions Definitions
    {
        get
        {
            if (_definitions == null)
                _definitions = GetPacketDefinitions();

            return _definitions;
        }
    }

    protected virtual PacketDefinitions GetPacketDefinitions() {
      return PacketDefinitions.GetDBCFile();
    }

    public Dictionary<string, ListElement> items;
    public SortedList<Tuple<uint, int>, Packet> packets;
    public List<List<ListElement>> ignoreList;
    public int numUpdates;
    public char[] tagFilter;
    public string packetTitlesRaw;
    public string[] packetTitlesLines;
    public Dictionary<int, string> packetTitles = new Dictionary<int, string>();
    private MainWindow mainWindow;

    static UInt64 ByteSwap64(UInt64 n)
    {
      UInt64 n_swapped = 0;
      for (int byte_index = 7; byte_index >= 0; --byte_index)
      {
        n_swapped <<= 8;
        n_swapped |= n % (1 << 8);
        n >>= 8;
      }
      return n_swapped;
    }

    public static double? ExtractSignalFromBytes(byte[] bytes, int StartBit, int BitSize, bool signed, double ScaleFactor, double Offset, bool bigEndian = false) {
      UInt64 signalMask = 0;

      if (StartBit + BitSize > bytes.Length * 8) // check data length
        return null;

      for (int bit_index = (int)(StartBit + BitSize - 1); bit_index >= 0; --bit_index) {
        signalMask <<= 1;
        if (bit_index >= StartBit) {
          signalMask |= 1;
        }
      }

      UInt64 signalValueRaw = 0;
      for (int byte_index = bytes.Length - 1; byte_index >= 0; --byte_index) {
        signalValueRaw <<= 8;
        signalValueRaw += bytes[byte_index];
      }

      signalValueRaw &= signalMask;

      if (bigEndian) {
        signalMask = ByteSwap64(signalMask);
        signalValueRaw = ByteSwap64(signalValueRaw);
      }

      while ((signalMask & 0x1) == 0) {
        signalValueRaw >>= 1;
        signalMask >>= 1;
      }

      double signalValue = signalValueRaw;

      if (signed) {
        UInt64 signalMaskHighBit = (signalMask + 1) >> 1;
        if ((signalValueRaw & signalMaskHighBit) != 0) {
          signalValue = -(Int64)((signalValueRaw ^ signalMask) + 1);
        }
      }

      signalValue *= ScaleFactor;
      signalValue += Offset;

      return signalValue;
    }

    static double ExtractSignalFromBytes(byte[] bytes, Message.Signal signal) {
      UInt64 signalMask = 0;
      for (int bit_index = (int)(signal.StartBit + signal.BitSize - 1); bit_index >= 0; --bit_index) {
        signalMask <<= 1;
        if (bit_index >= signal.StartBit) {
          signalMask |= 1;
        }
      }

      UInt64 signalValueRaw = 0;
      for (int byte_index = bytes.Length - 1; byte_index >= 0; --byte_index) {
        signalValueRaw <<= 8;
        signalValueRaw += bytes[byte_index];
      }

      signalValueRaw &= signalMask;

      if (signal.ByteOrder == Message.Signal.ByteOrderEnum.BigEndian) {
        signalMask = ByteSwap64(signalMask);
        signalValueRaw = ByteSwap64(signalValueRaw);
      }

      while ((signalMask & 0x1) == 0) {
        signalValueRaw >>= 1;
        signalMask >>= 1;
      }

      double signalValue = signalValueRaw;

      if (signal.ValueType == Message.Signal.ValueTypeEnum.Signed) {
        UInt64 signalMaskHighBit = (signalMask + 1) >> 1;
        if ((signalValueRaw & signalMaskHighBit) != 0) {
          signalValue = -(Int64)((signalValueRaw ^ signalMask) + 1);
        }
      }

      signalValue *= signal.ScaleFactor;
      signalValue += signal.Offset;

      return signalValue;
    }

    public Parser(MainWindow mainWindow, ObservableCollection<DBCwAssociatedBus> dbcList = null) {
      this.mainWindow = mainWindow;
      items = new Dictionary<string, ListElement>();
      packets = new SortedList<Tuple<uint, int>, Packet>();

      Packet p;

      if (dbcList != null) {

        foreach (DBCwAssociatedBus dbcFile in dbcList) //parse each file
        {
            //if (dbcFile != null)
            //{
                
                Reader reader = new DBCLib.Reader();

                reader.AllowErrors = true;

                List<object> entries = reader.Read(dbcFile.Path);
                foreach (object entry in entries)
                {
                    if (entry is Message)
                    {
                        Message message = (Message)entry;

                        try
                            {
                                packets.Add(Tuple.Create(message.Id, int.Parse(dbcFile.Bus)), p = new Packet(message.Id, dbcFile, this));

                                foreach (Message.Signal signal in message.Signals)
                                {
                                    string nameWoPrefix = Regex.Replace(signal.Name, @"^_(_|\d)*_", "");
                                    var valueLookup = (DBCLib.Value)
                                        entries.Where(x => x is DBCLib.Value && ((DBCLib.Value)x).ContextSignalName == signal.Name).FirstOrDefault();

                                    List<KeyValuePair<long, string>> VT_List = new List<KeyValuePair<long, string>>();
                                    if (valueLookup != null)
                                    {
                                        VT_List = valueLookup.Mapping.ToList();
                                    }
                                    

                                    p.AddValue(
                                        dbcFile.Bus + "_" + (message.Id).ToString("X") + "_" + nameWoPrefix,
                                        message.Name,
                                        dbcFile.Bus,
                                        signal.Unit,
                                        nameWoPrefix, 
                                        (bytes) =>
                                        {
                                            double result;
                                            if (signal.StartBit + signal.BitSize > bytes.Length * 8) // check data length
                                                return null;
                                            if (signal.Multiplexer) // if this is our multiplex / page selector
                                                return
                                                    p.currentMultiplexer = // store it
                                                    ExtractSignalFromBytes(bytes, signal); // and return it
                                            else if (signal.MultiplexerIdentifier != null)
                                            { // else if this is a sub-item
                                                if (signal.MultiplexerIdentifier == p.currentMultiplexer) // check if we're on the same page
                                                    result = ExtractSignalFromBytes(bytes, signal); // then return it
                                                else return null;
                                            }
                                            else result = ExtractSignalFromBytes(bytes, signal);
                                            //if (valueLookup != null)
                                            //{
                                            //    string s =
                                            //    valueLookup.Mapping.Where(x => x.Key == result).FirstOrDefault().Value; //TryGetValue((long)result, out s);
                                            //    if (s != null)
                                            //        return s;
                                            //}
                                            return result;
                                        },
                                       VT_List
                                       ,
                                        null
                                        );
                                }
                            }
                        catch { }
                    }
                }
                             
        //}
       }
     }
    }

    internal static Parser FromSource(MainWindow mainWindow,PacketDefinitions.DefinitionSource source, ObservableCollection<DBCwAssociatedBus> dbcList = null)
    {      
        return new Parser(mainWindow, dbcList);
    }


    private bool ParsePacket(string raw, uint id, int bus, byte[] bytes, string timestamp)
    {
      bool knownPacket = false;
      Tuple<uint, int> packetKey = Tuple.Create(id, bus);
      if (!packets.ContainsKey(packetKey))
        {
        packetKey = Tuple.Create(id, -1);
        
        }

        if (packets.ContainsKey(packetKey))
        {
            knownPacket = true;
            //first point of series is missing when updating only once:
            packets[packetKey].Update(bytes, timestamp);
            packets[packetKey].Update(bytes, timestamp);
        }

        numUpdates++;

      return knownPacket;
    }

    public void UpdateItem(string name, string messageName, string unit, string tag, int index, object value, uint id, int bus, List<KeyValuePair<long, string>> VT_List, string timestamp)
    {
      if (value == null)
        return;
      ListElement l;
      items.TryGetValue(name, out l);
      if (l == null)
      {
        items.Add(name, l = new ListElement(name, messageName, unit, tag, index, value, id, bus, VT_List));
      }
      else l.SetValue(value, timestamp);
    }


    public bool Parse(string input, int idToFind, string timestamp, out bool knownPacket)
    {
      knownPacket = false;

      if (!input.Contains('\n'))
      {
        return false;
      }
      if (input.StartsWith(">"))
      {
        input = input.Substring(1);
      }
      List<string> lines = input?.Split('\n').ToList();
      lines.Remove(lines.Last());

      bool found = false;

      foreach (var line in lines)
      {
        try
        {
          uint id = 0;
          int idLength = line.IndexOf(" ", 0);

            if (!uint.TryParse(line.Substring(0, idLength), System.Globalization.NumberStyles.HexNumber, null, out id))
            {
                continue;
            }
          
          int busLength = line.IndexOf(" ", idLength + 1) - idLength - 1;
          int bus = int.Parse(line.Substring(idLength + 1, busLength));

          string[] raw = new string[(line.Length - idLength - busLength - 2) / 2];
          int r = 0;
          int i;
          for (i = idLength + 1 + busLength +1; i < line.Length - 1; i += 2)
          {
            raw[r++] = line.Substring(i, 2);
          }
          List<byte> bytes = new List<byte>();
          i = 0;
          byte b = 0;
          for (i = 0; i < raw.Length; i++)
          {
            if ((raw[i].Length != 2) || !byte.TryParse(raw[i], System.Globalization.NumberStyles.HexNumber, null, out b))
            {
              break;
            }
            else
            {
              bytes.Add(b);
            }
          }

          if (bytes.Count == raw.Length)
          { // try to validate the parsing 
            knownPacket = ParsePacket(line, id, bus, bytes.ToArray(), timestamp);
            if (idToFind > 0)
            {
              if (idToFind == id)
              {
                found = true;
              }
            }
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e.ToString());
        }
      }
      if (found)
      {
        return true;
      }
      return false;
    }
  }
}
