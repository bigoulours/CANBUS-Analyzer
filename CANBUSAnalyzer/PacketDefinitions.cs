using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CANBUS
{
    public class PacketDefinitions
    {
        public enum DefinitionSource
        {
            Default = 0,
            DBCFile = 1,
            SMTModelS = 2
        }

        public DefinitionSource Source { get; private set; }
        public string Name { get; private set; }

        public PacketDefinitions(DefinitionSource source, string name)
        {
            Source = source;
            Name = name;
        }

        public static IEnumerable<PacketDefinitions> GetAll()
        {
            return new PacketDefinitions[] { GetSMTModelS(), GetDBCFile() };
        }

        public static PacketDefinitions GetSMTModelS()
        {
            return new PacketDefinitions(DefinitionSource.SMTModelS, "Model S");
        }

        public static PacketDefinitions GetDBCFile()
        {
              return new PacketDefinitions(DefinitionSource.DBCFile, "DBC file...");
        }

  }
}
