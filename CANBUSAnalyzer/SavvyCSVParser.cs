using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CANBUS
{
    class SavvyCSVParser : ICANLogParser
    {
        private const int MinColumns = 14;
        private const int ByteLength = 2;

        private static class ColumnIndex
        {
            public const int Timestamp = 0;
            public const int ID = 1;
            public const int Bus = 4;
            public const int DataLength = 5;
            public const int FirstByte = 6;
        }

        public string ParseLine(string rawLine, out string timestamp)
        {
            string formattedLine = null;
            timestamp = null;
            if (!string.IsNullOrEmpty(rawLine))
            {
                string[] split = rawLine.Split(',');

                // Ensure we have the expected number of columns
                if (split.Length >= MinColumns && int.TryParse(split[ColumnIndex.DataLength], out int dataLength))
                {
                    timestamp = split[ColumnIndex.Timestamp];

                    // Raw data is assumed to be in the final array element
                    formattedLine = split[ColumnIndex.ID].TrimStart('0') + " " + split[ColumnIndex.Bus] + " ";

                    for (int i = ColumnIndex.FirstByte; i < ColumnIndex.FirstByte + dataLength; i++)
                    {
                        // Sanity check
                        Debug.Assert(split[i].Length == ByteLength);

                        // Add to formatted data
                        formattedLine += split[i];

                    }

                }
            }

            return formattedLine;
        }
    }
}
