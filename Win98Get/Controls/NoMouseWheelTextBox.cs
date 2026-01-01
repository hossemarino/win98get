namespace Win98Get.Controls;

public sealed class NoMouseWheelTextBox : TextBox
{
    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_MOUSEHWHEEL = 0x020E;

        if (false && (m.Msg is WM_MOUSEWHEEL or WM_MOUSEHWHEEL))
        {
            return;
        }

        base.WndProc(ref m);
    }
}
