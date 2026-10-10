using System.Diagnostics;
using System.Drawing.Imaging;

namespace AiBurgerClock;

// Windows-only tooltip layout and native Popup/Draw checks, using synthetic quota data.
internal static class QuotaToolTipUiChecks
{
    internal static async Task RunAsync(AccountQuotaView view, Label[] labels, string? reportDirectory,
        Action<bool, string> check)
    {
        using var graphics = view.CreateGraphics();
        string longText = labels.Single(label => label.Text == "ChatGPT").AccessibleDescription!;
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using var dpiBitmap = new Bitmap(1, 1);
            dpiBitmap.SetResolution(dpi, dpi);
            using var dpiGraphics = Graphics.FromImage(dpiBitmap);
            var shortSize = AccountQuotaView.MeasureDetail(dpiGraphics, "리셋: 2026-10-10 22:00 KST", dpi, 1920);
            var longSize = AccountQuotaView.MeasureDetail(dpiGraphics, longText, dpi, 1920);
            check(shortSize.Width == (int)Math.Round(AccountQuotaView.DetailWidth * dpi / 96.0) &&
                longSize.Width == shortSize.Width, $"Quota tooltips keep a fixed 320-DIP width at {dpi} DPI");
            check(longSize.Height > shortSize.Height, $"Long quota details expand downwards without widening at {dpi} DPI");
        }
        var narrowSize = AccountQuotaView.MeasureDetail(graphics, longText, 96, 280);
        check(narrowSize.Width < 280 && narrowSize.Height > AccountQuotaView.MeasureDetail(graphics, longText, 96, 1920).Height,
            "Quota tooltip width also respects a narrow monitor's working area");
        string token = "C:\\" + new string('x', 200) + "\\agy.exe";
        var tokenSize = AccountQuotaView.MeasureDetail(graphics, token, 96, 1920);
        using var format = AccountQuotaView.DetailFormat();
        var tokenContent = graphics.MeasureString(token, AccountQuotaView.DetailFont, new SizeF(304, tokenSize.Height - 16), format,
            out int charactersFitted, out int linesFilled);
        check(tokenContent.Width <= 304 && charactersFitted == token.Length && linesFilled > 1 &&
            tokenSize.Height > AccountQuotaView.MeasureDetail(graphics, "agy.exe", 96, 1920).Height,
            "An unbroken long CLI path wraps without horizontal clipping");

        // Show the real native tooltip on visible quota labels; no mouse input or account calls.
        foreach (var (label, file) in new[]
        {
            (labels.Single(label => label.Text == "ChatGPT"), "quota-tooltip-provider.png"),
            (labels.Single(label => label.Text.StartsWith("5시간  100%", StringComparison.Ordinal)), "quota-tooltip-session.png"),
            (labels.Single(label => label.Text.StartsWith("주간  55%", StringComparison.Ordinal)), "quota-tooltip-weekly.png")
        })
        {
            string detail = label.AccessibleDescription!;
            Size popup = Size.Empty;
            Size painted = Size.Empty;
            PopupEventHandler onPopup = (_, e) => { if (e.AssociatedControl == label) popup = e.ToolTipSize; };
            DrawToolTipEventHandler onDraw = (_, e) =>
            {
                if (e.AssociatedControl != label) return;
                painted = e.Bounds.Size;
                if (reportDirectory is null) return;
                Directory.CreateDirectory(reportDirectory);
                using var bitmap = new Bitmap(e.Bounds.Width, e.Bounds.Height);
                bitmap.SetResolution(e.Graphics.DpiX, e.Graphics.DpiY);
                using var imageGraphics = Graphics.FromImage(bitmap);
                var render = new DrawToolTipEventArgs(imageGraphics, e.AssociatedWindow, e.AssociatedControl,
                    new Rectangle(Point.Empty, bitmap.Size), e.ToolTipText, view.Details.BackColor, view.Details.ForeColor, e.Font);
                AccountQuotaView.DrawDetail(render, label.DeviceDpi, view.Details.ForeColor);
                bitmap.Save(Path.Combine(reportDirectory, file), ImageFormat.Png);
            };
            view.Details.Popup += onPopup;
            view.Details.Draw += onDraw;
            try
            {
                view.Details.Show(detail, label, new Point(8, label.Height), 5000);
                var elapsed = Stopwatch.StartNew();
                while ((popup.IsEmpty || painted.IsEmpty) && elapsed.Elapsed < TimeSpan.FromSeconds(2))
                    await Task.Delay(25);
                check(!popup.IsEmpty && !painted.IsEmpty, "Native quota Tooltip Popup and Draw occur: " + file);
                var expected = AccountQuotaView.MeasureDetail(graphics, detail, label.DeviceDpi, Screen.FromControl(label).WorkingArea.Width);
                check(popup == expected && painted == expected, "Native quota tooltip paints the measured fixed-width box: " + file);
                int padding = (int)Math.Round(8 * label.DeviceDpi / 96.0);
                graphics.MeasureString(detail, AccountQuotaView.DetailFont,
                    new SizeF(painted.Width - 2 * padding, painted.Height - 2 * padding), format, out int fitted, out _);
                check(fitted == detail.Length, "Every quota detail character fits inside the wrapped tooltip: " + file);
                check(view.Details.GetToolTip(label) == detail && label.AccessibleDescription == detail,
                    "Wrapped tooltip preserves complete shared and accessible detail: " + file);
                Console.WriteLine($"INFO: {file} native tooltip {painted.Width}x{painted.Height} pixels, DPI {label.DeviceDpi}.");
            }
            finally
            {
                view.Details.Hide(label);
                view.Details.Popup -= onPopup;
                view.Details.Draw -= onDraw;
            }
        }
    }
}
