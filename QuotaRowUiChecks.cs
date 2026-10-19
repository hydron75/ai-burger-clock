using System.Globalization;

namespace AiBurgerClock;

// Native GDI measurements and the real row painter, using synthetic text only.
internal static class QuotaRowUiChecks
{
    internal static void Run(StatusWindow window, IReadOnlyList<QuotaState> states, DateTimeOffset now,
        string? reportDirectory, Action<bool, string> check)
    {
        string longName = "주간 Synthetic Model With A Long Shared Prefix Research Alpha";
        string unicodeName = "주간 👩🏽‍💻 Synthetic e\u0301 Model 🇰🇷 With A Long Name 👨‍👩‍👧‍👦";
        string[] values = ["0% 사용 · 약 4시간 59분 후 리셋", "9.9% 사용 · 약 6일 23시간 후 리셋",
            "99.9% 사용 · 약 6일 23시간 후 리셋", "100% 사용 · 약 5시간 후 리셋",
            "99.9% 사용 (이전) · 약 6일 23시간 후 리셋", "97% 사용 (이전) · 갱신 대기",
            "12.3% 사용 · 리셋 미제공"];
        foreach (int dpi in new[] { 96, 120, 144, 192 })
        foreach (int width in new[] { 326, 309 }) // Same viewport, with and without its scrollbar.
        {
            int Scale(int value) => (int)Math.Round(value * dpi / 96.0);
            using var font = new Font("Segoe UI", (float)(8.5 * dpi / 72.0), GraphicsUnit.Pixel);
            using var row = new AccountQuotaView.RowLabel
            {
                Size = new Size(Scale(width), Scale(19)), Font = font, ForeColor = Color.Black, BackColor = Color.White
            };
            using var bitmap = new Bitmap(row.Width, row.Height);
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            foreach (string value in values)
            {
                QuotaLine Line(string name) => new(name + "  " + value, PanelTone.Good, "합성 Provider 상세",
                    "synthetic-window", 12.3, NameText: name, ValueText: value, ReadingText: name + "  " + value);
                void Paint(string name)
                {
                    var line = Line(name);
                    row.Text = line.Text;
                    row.UpdateRow(line);
                    graphics.Clear(Color.White);
                    row.DrawRow(graphics, dpi);
                }
                Paint("주간");
                check(row.ValueFits && row.VisibleName == "주간" && row.Height == Scale(19),
                    $"Short name and full value fit the unchanged row height at {dpi} DPI / {width}: {value}");
                using var right = bitmap.Clone(row.ValueBounds, bitmap.PixelFormat);
                Paint(longName);
                check(row.ValueFits && row.ValueText == value && row.VisibleName.Contains('…') &&
                    row.VisibleName.StartsWith("주", StringComparison.Ordinal) && row.VisibleName.EndsWith("a", StringComparison.Ordinal) &&
                    row.NameBounds.Right + Scale(6) <= row.ValueBounds.Left && row.ValueBounds.Right == row.ClientSize.Width,
                    $"Only the name is middle-ellipsized after right-side width reservation at {dpi} DPI / {width}: {value}");
                bool sameValue = Enumerable.Range(0, right.Width).All(x => Enumerable.Range(0, right.Height).All(y =>
                    right.GetPixel(x, y) == bitmap.GetPixel(row.ValueBounds.X + x, y)));
                check(sameValue && row.Text == Line(longName).Text && row.AccessibleName == Line(longName).ReadingText &&
                    row.AccessibleDescription == "",
                    $"Value pixels and unsqueezed accessible reading survive a long name at {dpi} DPI / {width}: {value}");
            }
            var unicodeLine = new QuotaLine(unicodeName + "  " + values[0], PanelTone.Good, "합성 상세",
                "synthetic-unicode", 0, NameText: unicodeName, ValueText: values[0], ReadingText: unicodeName + "  " + values[0]);
            row.Text = unicodeLine.Text;
            row.UpdateRow(unicodeLine);
            row.DrawRow(graphics, dpi);
            var originalElements = StringInfo.GetTextElementEnumerator(unicodeName);
            var elements = new HashSet<string> { "…" };
            while (originalElements.MoveNext()) elements.Add(originalElements.GetTextElement());
            var visibleElements = StringInfo.GetTextElementEnumerator(row.VisibleName);
            bool intact = true;
            while (visibleElements.MoveNext()) intact &= elements.Contains(visibleElements.GetTextElement());
            check(row.ValueFits && intact && row.VisibleName.Contains('…') && row.AccessibleName == unicodeLine.ReadingText,
                $"Real-font fitting preserves Unicode clusters and the full reading at {dpi} DPI / {width}");
        }

        var size = window.ClientSize;
        var view = window.QuotaView;
        window.UpdateQuotas(states, now);
        int[] heights = view.Controls.OfType<Panel>().Select(box => box.Height).ToArray();
        int[] counts = view.Controls.OfType<Panel>().Select(box => box.Controls.OfType<QuotaBalanceBar>().Count()).ToArray();
        var synthetic = states.Select(state => state with
        {
            Reading = state.Reading is not { } reading ? null : reading with
            {
                Windows = reading.Windows.Select((item, index) => item with
                {
                    Label = index < 2 ? item.Label : state.Provider == QuotaProvider.Gemini ? unicodeName : longName
                }).ToArray()
            }
        }).ToArray();
        window.UpdateQuotas(synthetic, now);
        view.AutoScrollPosition = Point.Empty;
        SmokeTest.RenderAndCheckLayout(window, "account-quotas-long-names.png", reportDirectory);
        view.ScrollControlIntoView(view.Controls.OfType<Panel>().Single(box => box.Name == "GeminiQuotaBox"));
        SmokeTest.RenderAndCheckLayout(window, "account-quotas-long-names-gemini.png", reportDirectory);
        check(window.ClientSize == size && view.Controls.OfType<Panel>().Select(box => box.Height).SequenceEqual(heights) &&
            view.Controls.OfType<Panel>().Select(box => box.Controls.OfType<QuotaBalanceBar>().Count()).SequenceEqual(counts) &&
            !view.HorizontalScroll.Visible, "Long names do not add rows, change card/window height or introduce horizontal scrolling");
        foreach (var section in QuotaPanelModel.Sections(synthetic, now))
        {
            var rows = view.Controls.OfType<Panel>().Single(box => box.Name == section.Provider + "QuotaBox")
                .Controls.OfType<AccountQuotaView.RowLabel>().ToArray();
            check(rows.Zip(section.Rows).All(pair => pair.First.NameText == pair.Second.NameText &&
                    pair.First.ValueText == pair.Second.ValueText && pair.First.Text == pair.Second.Text &&
                    pair.First.AccessibleName == pair.Second.ReadingText && view.DetailFor(pair.First) == section.Detail),
                "Split Windows rows retain full names, readings and unchanged Provider detail: " + section.Provider);
        }
        window.UpdateQuotas(states, now);
        view.AutoScrollPosition = Point.Empty;
    }
}
