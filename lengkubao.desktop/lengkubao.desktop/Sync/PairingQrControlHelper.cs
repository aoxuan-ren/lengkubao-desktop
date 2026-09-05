using System;
using System.Drawing;
using System.Windows.Forms;

namespace lengkubao.desktop.Sync
{
    /// <summary>WinForms 配对二维码展示控件。</summary>
    public static class PairingQrControlHelper
    {
        public static PictureBox CreateQrPictureBox(int size = 120)
        {
            return new PictureBox
            {
                Name = "picPairingQr",
                Size = new Size(size, size),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
        }

        public static void RefreshQr(PictureBox pictureBox, string pairingCode, int tcpPort)
        {
            if (pictureBox == null || pictureBox.IsDisposed) return;

            Image old = pictureBox.Image;
            try
            {
                pictureBox.Image = PairingQrService.CreateQrBitmap(pairingCode, tcpPort, 4);
            }
            catch
            {
                pictureBox.Image = null;
            }
            old?.Dispose();
        }
    }
}
