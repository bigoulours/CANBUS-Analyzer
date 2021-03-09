using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CANBUS
{
    class VectorASCParser : ICANLogParser
    {
        private const int MinColumns = 7;
        private const int CANOpenIDLength = 3;
        private const int SAEIDLength = 8;
        private const int ByteLength = 2;

        private static class ColumnIndex
        {
            public const int Timestamp = 0;
            public const int Bus = 1;
            public const int ID = 2;
            public const int DataLength = 5;
            public const int FirstByte = 6;
        }

        private Regex regexLine;
        private bool isHeaderDone;

        public VectorASCParser()
        {
            regexLine = new Regex(@"^\s*((?<Data>[^ ]+)\s*)+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
        }

        public string ParseLine(string rawLine, out string timestamp)
        {
            string formattedLine = null;
            timestamp = null;
            if (rawLine != null)
            {
                // Look for "// version" line or an empty line to mark the end of the file header
                if ((isHeaderDone || (isHeaderDone = IsEndOfHeader(rawLine))) && rawLine.Length > 0)
                {
                    Match m = regexLine.Match(rawLine);

                    // Ensure we have the expected number of columns
                    if (m.Success && m.Groups["Data"].Captures.Count >= MinColumns)
                    {
                        CaptureCollection capData = m.Groups["Data"].Captures;

                        timestamp = capData[ColumnIndex.Timestamp].Value.Replace(".", string.Empty);

                        string id = capData[ColumnIndex.ID].Value;
                        // Ensure that this is a valid (non-error) frame
                        if (id.Length < CANOpenIDLength)
                         id = id.PadLeft(CANOpenIDLength, '0');
                        else
                            if ((id.Length > CANOpenIDLength) && (id.Length < SAEIDLength))
                                id = id.PadLeft(SAEIDLength, '0');
                        if (id.Length > SAEIDLength)
                            id = id.Substring(0,SAEIDLength);

                        int dataLength;
                        if ((id.Length == CANOpenIDLength || id.Length == SAEIDLength) && int.TryParse(capData[ColumnIndex.DataLength].Value, out dataLength))
                        {
                            // Starting with message ID
                            formattedLine = id + " ";

                            // Append Bus#
                            string bus = capData[ColumnIndex.Bus].Value;
                            formattedLine += bus + " ";

                            // Append message data
                            for (int i = ColumnIndex.FirstByte; i < ColumnIndex.FirstByte + dataLength && i < capData.Count; i++)
                            {
                                // Sanity check
                                Debug.Assert(capData[i].Value.Length == ByteLength);

                                // Add to formatted data
                                formattedLine += capData[i].Value;
                                 
                            }
                        }
                        else
                            Debug.WriteLine("Unpadded value: " + capData[2].Value);

                    }
                    else
                        Debug.WriteLine("Skipping Line: " + rawLine);
                }
            }

            return formattedLine;
        }

        private bool IsEndOfHeader(string rawLine)
        {
            return rawLine != null && (rawLine.Length == 0 || rawLine.StartsWith("// version"));
        }
    }
}
