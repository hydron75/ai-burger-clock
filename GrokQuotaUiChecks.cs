using System.Drawing.Imaging;

namespace AiBurgerClock;

// Synthetic swatches use the production bar painter, not account data or native notifications.
internal static class GrokQuotaUiChecks
{
    internal static void Run(Action<bool, string> check, string? reportDirectory)
    {
        foreach (bool dark in new[] { false, true })
        foreach (int dpi in new[] { 96, 192 })
        {
            int Scale(int value) => (int)Math.Round(value * dpi / 96d);
            Color background = dark ? Color.FromArgb(30, 30, 30) : Color.White;
            using var current = new QuotaBalanceBar { BackColor = background, Size = new(Scale(520), Scale(8)) };
            using var previous = new QuotaBalanceBar { BackColor = background, Size = current.Size };
            current.UpdateBalance(QuotaProvider.Xai, 37.5, PanelTone.Good, "합성 현재 조회", "", 60);
            previous.UpdateBalance(QuotaProvider.Xai, 37.5, PanelTone.Muted, "합성 이전 값", "", 60);
            check(QuotaBarPalette.Contrast(current.FillColor, previous.FillColor) >= 3,
                $"xAI current neutral differs from previous gray by at least 3:1 ({(dark ? "dark" : "light")}, {dpi} DPI)");
            check(current.ElapsedPercent == 60 && previous.ElapsedPercent is null && previous.FillColor == Color.Gray,
                "Current xAI has time/marker; previous keeps the original gray and no time comparison");
            var missing = QuotaPanelModel.Section(new(QuotaProvider.Xai), DateTimeOffset.Parse("2032-03-04T00:00:00Z"));
            check(missing.Rows is [{ UsedPercent: null }], "No reading has no percentage or usage bar");
            if (reportDirectory is null) continue;
            using var bitmap = new Bitmap(Scale(580), Scale(270));
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(background);
            using var font = new Font("Malgun Gothic", 11);
            using var bold = new Font("Malgun Gothic", 12, FontStyle.Bold);
            using var text = new SolidBrush(dark ? Color.White : Color.Black);
            using var muted = new SolidBrush(dark ? Color.FromArgb(180, 180, 180) : Color.DimGray);
            graphics.DrawString($"합성 데이터 · xAI · {(dark ? "다크" : "라이트")} · {dpi * 100 / 96}%", bold, text, Scale(20), Scale(14));
            graphics.DrawString("현재 조회 · 주간 크레딧 37.5% 사용 · 시간 60%", font, text, Scale(20), Scale(60));
            graphics.TranslateTransform(Scale(20), Scale(96)); current.DrawBar(graphics, dpi); graphics.ResetTransform();
            graphics.DrawString("이전 조회값 · 주간 크레딧 37.5% 사용 (이전)", font, muted, Scale(20), Scale(128));
            graphics.TranslateTransform(Scale(20), Scale(164)); previous.DrawBar(graphics, dpi); graphics.ResetTransform();
            graphics.DrawString("조회 없음 · 한도 조회 대기 (사용량 막대 없음)", font, muted, Scale(20), Scale(204));
            Directory.CreateDirectory(reportDirectory);
            bitmap.Save(Path.Combine(reportDirectory, $"xai-neutral-{(dark ? "dark" : "light")}-{dpi * 100 / 96}.png"), ImageFormat.Png);
        }
    }
}
