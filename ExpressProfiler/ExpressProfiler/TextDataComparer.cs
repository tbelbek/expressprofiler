#region usings

using System;
using System.Collections.Generic;
using System.Windows.Forms;

#endregion

namespace ExpressProfiler.EventComparers
{
    public class TextDataComparer : IComparer<ListViewItem>
    {
        public TextDataComparer(int checkedColumn, SortOrder sortOrder)
        {
            CheckedColumn = checkedColumn;
            SortOrder = sortOrder;
        }

        public int CheckedColumn { get; set; }
        public SortOrder SortOrder { get; set; }

        public int Compare(ListViewItem x, ListViewItem y)
        {
            return SortOrder == SortOrder.Descending ? CompareDescending(x, y) : CompareAscending(x, y);
        }

        private int CompareAscending(ListViewItem x, ListViewItem y)
        {
            if (x.SubItems[CheckedColumn] == null && y.SubItems[CheckedColumn] == null) return 0;
            if (x.SubItems[CheckedColumn] == null) return -1;
            if (y.SubItems[CheckedColumn] == null) return 1;
            var xIsInt = int.TryParse(x.SubItems[CheckedColumn].Text.Replace(",", ""), out int xAsInt);

            var yIsInt = int.TryParse(y.SubItems[CheckedColumn].Text.Replace(",", ""), out int yAsInt);

            if (!xIsInt || !yIsInt)
                return String.CompareOrdinal(x.SubItems[CheckedColumn].Text, y.SubItems[CheckedColumn].Text);
            if (xAsInt < yAsInt)
                return -1;
            return xAsInt > yAsInt ? 1 : 0;
        }

        private int CompareDescending(ListViewItem x, ListViewItem y)
        {
            if (x.SubItems[CheckedColumn] == null && y.SubItems[CheckedColumn] == null) return 0;
            if (x.SubItems[CheckedColumn] == null) return 1;
            if (y.SubItems[CheckedColumn] == null) return -1;
            var xIsInt = int.TryParse(x.SubItems[CheckedColumn].Text.Replace(",", ""), out var xAsInt);

            var yIsInt = int.TryParse(y.SubItems[CheckedColumn].Text.Replace(",", ""), out int yAsInt);

            if (!xIsInt || !yIsInt)
                return String.CompareOrdinal(y.SubItems[CheckedColumn].Text, x.SubItems[CheckedColumn].Text);
            if (xAsInt > yAsInt)
                return -1;
            return xAsInt < yAsInt ? 1 : 0;
        }
    }
}