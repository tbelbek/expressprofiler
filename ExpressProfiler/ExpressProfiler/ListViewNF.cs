#region usings

using System.Windows.Forms;

#endregion

namespace ExpressProfiler
{
    internal class ListViewNF : ListView
    {
        public ListViewNF()
        {
            //Activate double buffering
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            //Enable the OnNotifyMessage event so we get a chance to filter out 
            // Windows messages before they get to the form's WndProc
            SetStyle(ControlStyles.EnableNotifyMessage, true);

            SortOrder = SortOrder.Ascending;
        }

        public SortOrder SortOrder { get; set; }

        public void ToggleSortOrder()
        {
            if (SortOrder == SortOrder.Ascending)
            {
                SortOrder = SortOrder.Descending;
                return;
            }

            SortOrder = SortOrder.Ascending;
        }

        protected override void OnNotifyMessage(Message m)
        {
            //Filter out the WM_ERASEBKGND message
            if (m.Msg != 0x14) base.OnNotifyMessage(m);
        }
    }
}