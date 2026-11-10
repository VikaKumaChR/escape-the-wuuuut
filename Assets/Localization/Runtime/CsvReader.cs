using System.Collections.Generic;
using System.Text;

namespace DistortedSpace.Localization
{
    /// <summary>Minimal RFC 4180 CSV reader: quoted fields, doubled quotes, LF or CRLF line endings.</summary>
    internal static class CsvReader
    {
        public static List<string[]> Read(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text))
                return rows;

            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            int start = text[0] == '﻿' ? 1 : 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c != '"')
                        field.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                        quoted = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Length = 0;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        fields.Add(field.ToString());
                        field.Length = 0;
                        rows.Add(fields.ToArray());
                        fields.Clear();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }
    }
}
