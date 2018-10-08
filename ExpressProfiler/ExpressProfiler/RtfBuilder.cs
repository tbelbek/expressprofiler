//Traceutils assembly
//writen by Locky, 2009. 

#region usings

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Text;

#endregion

namespace ExpressProfiler
{
    internal class RTFBuilder
    {
        private static readonly char[] Slashable = {'{', '}', '\\'};
        private readonly List<Color> m_Colortable = new List<Color>();
        private readonly float m_DefaultFontSize;
        private readonly StringCollection m_Fonttable = new StringCollection();

        private readonly StringBuilder m_Sb = new StringBuilder();


        private Color m_Backcolor;
        private Color m_Forecolor;


        public RTFBuilder()
        {
            ForeColor = Color.FromKnownColor(KnownColor.WindowText);
            BackColor = Color.FromKnownColor(KnownColor.Window);
            m_DefaultFontSize = 20F;
        }

        public Color ForeColor
        {
            set
            {
                if (!m_Colortable.Contains(value)) m_Colortable.Add(value);
                if (value != m_Forecolor) m_Sb.Append($"\\cf{m_Colortable.IndexOf(value) + 1} ");
                m_Forecolor = value;
            }
        }

        public Color BackColor
        {
            set
            {
                if (!m_Colortable.Contains(value)) m_Colortable.Add(value);
                if (value != m_Backcolor) m_Sb.Append($"\\highlight{m_Colortable.IndexOf(value) + 1} ");
                m_Backcolor = value;
            }
        }

        public void AppendLine()
        {
            m_Sb.AppendLine("\\line");
        }

        public void Append(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            value = CheckChar(value);
            if (value.IndexOf(Environment.NewLine) >= 0)
            {
                var lines = value.Split(new[] {Environment.NewLine}, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    m_Sb.Append(line);
                    m_Sb.Append("\\line ");
                }
            }
            else
            {
                m_Sb.Append(value);
            }
        }

        private static string CheckChar(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOfAny(Slashable) >= 0)
                value = value.Replace("{", "\\{").Replace("}", "\\}").Replace("\\", "\\\\");
            var replaceuni = false;
            foreach (var t in value)
                if (t > 255)
                {
                    replaceuni = true;
                    break;
                }

            if (!replaceuni) return value;
            var sb = new StringBuilder();
            foreach (var t in value)
                if (t <= 255)
                {
                    sb.Append(t);
                }
                else
                {
                    sb.Append("\\u");
                    sb.Append((int) t);
                    sb.Append("?");
                }

            value = sb.ToString();


            return value;
        }

        public new string ToString()
        {
            var result = new StringBuilder();
            result.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang3081");
            result.Append("{\\fonttbl");
            for (var i = 0; i < m_Fonttable.Count; i++)
                try
                {
                    result.Append(string.Format(m_Fonttable[i], i));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

            result.AppendLine("}");
            result.Append("{\\colortbl ;");
            foreach (var item in m_Colortable)
                result.AppendFormat("\\red{0}\\green{1}\\blue{2};", item.R, item.G, item.B);
            result.AppendLine("}");
            result.Append("\\viewkind4\\uc1\\pard\\plain\\f0");
            result.AppendFormat("\\fs{0} ", m_DefaultFontSize);
            result.AppendLine();
            result.Append(m_Sb);
            result.Append("}");
            return result.ToString();
        }
    }
}