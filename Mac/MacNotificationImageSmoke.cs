using AppKit;
using CoreGraphics;
using Foundation;
using UserNotifications;

namespace AiBurgerClock;

// Native check of the notification pictures (kind -> level badge / thread / picture, the picture really is the
// menu-bar drawing, temporary files, and the no-picture fallbacks). Synthetic data only; no request ever reaches the
// real notification center: the sender gets a fake "add" and the main thread is the calling thread.
internal static class MacNotificationImageSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Notification image: " + message);
    }

    private static readonly MacNotificationKind[] StatusKinds =
    [
        MacNotificationKind.ScheduleFull, MacNotificationKind.ScheduleBurger, MacNotificationKind.ProviderRecovered,
        MacNotificationKind.ProviderIssue, MacNotificationKind.Holiday
    ];

    internal static string Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aibc-notify-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteReviewImages();
            VerifyStyles();
            int pictures = VerifyPictures();
            int sent = VerifySender(directory);
            VerifyStore(directory);
            return $"notification pictures (kind -> Info/Warning badge + status/quota thread, {pictures} PNGs: status pictures = menu-bar drawing for every schedule/attention light+dark, " +
                   $"blue quota picture distinct from the status colors, {sent} fake requests: attachment + thread, plain fallback when the picture cannot be made/attached/accepted, " +
                   "temporary files removed, folder limit and start sweep)";
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    // Opt-in review pictures (AIBURGERCLOCK_SMOKE_IMAGES): one picture per kind, light and dark, exactly the PNG a
    // notification would carry. Synthetic status values only.
    private static void WriteReviewImages()
    {
        string? folder = Environment.GetEnvironmentVariable(MacQuotaRowImages.EnvironmentVariable);
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        (MacNotificationKind Kind, TrayAppearance? Appearance)[] samples =
        [
            (MacNotificationKind.ScheduleFull, new(AgentState.FullThrottle, TrayAttention.Green)),
            (MacNotificationKind.ScheduleBurger, new(AgentState.BurgerTime, TrayAttention.Orange)),
            (MacNotificationKind.ProviderRecovered, new(AgentState.FullThrottle, TrayAttention.Green)),
            (MacNotificationKind.ProviderIssue, new(AgentState.FullThrottle, TrayAttention.Orange)),
            (MacNotificationKind.ProviderIssue, new(AgentState.FullThrottle, TrayAttention.Red)),
            (MacNotificationKind.Holiday, new(AgentState.FullThrottle, TrayAttention.Green)),
            (MacNotificationKind.Quota, null)
        ];
        foreach (var (kind, appearance) in samples)
        foreach (bool dark in new[] { false, true })
        {
            using NSData? png = MacNotificationImage.Png(kind, appearance, dark);
            string name = $"notification-{kind}{(appearance is { } value ? "-" + value.Schedule + "-" + value.Attention : "")}-{(dark ? "dark" : "light")}.png";
            if (png is not null) File.WriteAllBytes(Path.Combine(folder, name.ToLowerInvariant()), png.ToArray());
        }
    }

    // The Windows balloon levels, written out so a change of either side shows up here.
    private static void VerifyStyles()
    {
        (MacNotificationKind Kind, MacNotificationLevel Level, string Thread, bool StatusIcon)[] expected =
        [
            (MacNotificationKind.ScheduleFull, MacNotificationLevel.Info, "aiburgerclock.status", true),
            (MacNotificationKind.ScheduleBurger, MacNotificationLevel.Warning, "aiburgerclock.status", true),
            (MacNotificationKind.ProviderRecovered, MacNotificationLevel.Info, "aiburgerclock.status", true),
            (MacNotificationKind.ProviderIssue, MacNotificationLevel.Warning, "aiburgerclock.status", true),
            (MacNotificationKind.Holiday, MacNotificationLevel.Info, "aiburgerclock.status", true),
            (MacNotificationKind.Quota, MacNotificationLevel.Info, "aiburgerclock.quota", false)
        ];
        Check(expected.Length == Enum.GetValues<MacNotificationKind>().Length, "a notification kind has no expected style.");
        foreach (var (kind, level, thread, statusIcon) in expected)
            Check(MacNotificationLook.Style(kind) == new MacNotificationStyle(level, thread, statusIcon), $"{kind} has the wrong style.");
        Check(MacNotificationLook.ForSchedule(AgentState.FullThrottle) == MacNotificationKind.ScheduleFull &&
              MacNotificationLook.ForSchedule(AgentState.BurgerTime) == MacNotificationKind.ScheduleBurger,
            "schedule kinds are wrong (Info only for FULL THROTTLE).");
        var recovered = new ProviderAlert(ProviderKind.OpenAI, Recommendation.Go, true, "t", "b");
        var hold = new ProviderAlert(ProviderKind.OpenAI, Recommendation.Hold, false, "t", "b");
        var stop = new ProviderAlert(ProviderKind.Claude, Recommendation.Stop, false, "t", "b");
        Check(MacNotificationLook.ForProviderAlert(recovered) == MacNotificationKind.ProviderRecovered &&
              MacNotificationLook.ForProviderAlert(hold) == MacNotificationKind.ProviderIssue &&
              MacNotificationLook.ForProviderAlert(stop) == MacNotificationKind.ProviderIssue,
            "provider kinds are wrong (Info only for recovery).");
        string caches = MacNotificationImageStore.Default().Directory;
        Check(caches.EndsWith(Path.Combine("com.hydron75.aiburgerclock", "notification-images"), StringComparison.Ordinal),
            "the default picture folder is not the app's cache folder.");
    }

    private static int VerifyPictures()
    {
        int count = 0;
        foreach (bool dark in new[] { false, true })
        {
            foreach (AgentState schedule in Enum.GetValues<AgentState>())
            foreach (TrayAttention attention in Enum.GetValues<TrayAttention>())
            {
                var appearance = new TrayAppearance(schedule, attention);
                foreach (MacNotificationKind kind in StatusKinds)
                {
                    using NSBitmapImageRep picture = Decode(kind, appearance, dark);
                    count++;
                    string context = $"{kind}/{schedule}/{attention}/{(dark ? "dark" : "light")}";
                    var (disc, accent) = MacStatusIcon.Colors(attention, dark);
                    CheckFrame(picture, context);
                    Check(MacStatusIcon.Near(At(picture, 4, 10), disc, 0.03), "disc color is wrong: " + context);
                    Check(HasColor(picture, 6, 5, 14, 15, accent, 0.05), $"letter is not drawn in the menu-bar accent color: {context} (expected {Describe(accent)}, strongest letter-area pixel {Darkest(picture)})");
                    CheckBadge(picture, MacNotificationLook.Style(kind).Level, context);
                }
                // The attached picture is the menu-bar drawing, not a look-alike: compare it with the real icon
                // image rendered by the menu bar's own code, everywhere except the badge corner.
                using NSImage icon = MacStatusIcon.Create(appearance);
                using NSBitmapImageRep reference = MacStatusIcon.Render(icon, dark, 6);
                using NSBitmapImageRep attached = Decode(MacNotificationKind.ScheduleFull, appearance, dark);
                // Both are 6 pixels per point, so they must agree: compare the average color of every 1x1 grid cell
                // (premultiplied, 8x8 samples per cell), everywhere except the badge corner.
                int mismatched = 0, total = 0;
                string detail = "";
                for (int cellX = 0; cellX < MacStatusIcon.Size; cellX++)
                for (int cellY = 0; cellY < MacStatusIcon.Size; cellY++)
                {
                    if (cellX >= 11 && cellY <= 8) continue;
                    total++;
                    var expected = CellAverage(reference, cellX, cellY, 6.0, MacStatusIcon.Size * 6, convert: true);
                    var actual = CellAverage(attached, cellX, cellY, (double)attached.PixelsWide / MacStatusIcon.Size, attached.PixelsHigh, convert: false);
                    if (Math.Abs(expected.R - actual.R) > 0.03 || Math.Abs(expected.G - actual.G) > 0.03 ||
                        Math.Abs(expected.B - actual.B) > 0.03 || Math.Abs(expected.A - actual.A) > 0.03)
                    {
                        mismatched++;
                        if (detail.Length < 300) detail += $" ({cellX},{cellY}) menu bar {Format(expected)} picture {Format(actual)};";
                    }
                }
                Check(mismatched == 0,
                    $"the picture differs from the menu-bar icon ({mismatched}/{total} points): {schedule}/{attention}/{(dark ? "dark" : "light")}{detail}");
                count++;
            }

            // Quota: its own blue disc, readable, and not one of the status colors.
            using NSBitmapImageRep quota = Decode(MacNotificationKind.Quota, null, dark);
            count++;
            var (quotaDisc, _) = MacStatusIcon.Colors(TrayAttention.Green, dark);
            NSColor quotaAccent = MacNotificationImage.QuotaAccent(dark);
            CheckFrame(quota, "Quota/" + (dark ? "dark" : "light"));
            Check(MacStatusIcon.Near(At(quota, 4, 10), quotaDisc, 0.03), "quota disc color is wrong.");
            Check(HasColor(quota, 6, 5, 14, 15, quotaAccent, 0.05), "quota arrow is not drawn in the quota blue.");
            CheckBadge(quota, MacNotificationLevel.Info, "Quota");
            double contrast = MacControls.Contrast(quotaAccent, () => quotaDisc, NSAppearance.GetAppearance(NSAppearance.NameAqua)!);
            Check(contrast >= 4.5, $"quota blue contrast {contrast:0.00}:1 on the {(dark ? "dark" : "light")} disc is below 4.5:1.");
            foreach (TrayAttention attention in Enum.GetValues<TrayAttention>())
            {
                var status = MacStatusIcon.Colors(attention, dark).Glyph.UsingColorSpace(NSColorSpace.SRGBColorSpace)!;
                var blue = quotaAccent.UsingColorSpace(NSColorSpace.SRGBColorSpace)!;
                double distance = Math.Abs(status.RedComponent - blue.RedComponent) + Math.Abs(status.GreenComponent - blue.GreenComponent) +
                    Math.Abs(status.BlueComponent - blue.BlueComponent);
                Check(distance > 0.5, $"the quota blue is too close to the {attention} status color.");
            }
        }
        // Before the first tick a status notice has no appearance and so no picture (and never a guessed one).
        Check(MacNotificationImage.Png(MacNotificationKind.ScheduleFull, null, false) is null, "a status picture was drawn without an appearance.");
        return count;
    }

    private static int VerifySender(string directory)
    {
        var store = new MacNotificationImageStore(directory);
        var requests = new List<(string Id, string Title, string Body, string Thread, int Attachments, string? AttachmentId)>();
        int failures = 0, rejectWithAttachment = 0, rejectAlways = 0;
        bool dark = false;
        NSError? Error() => new NSError(new NSString("MacNotificationImageSmoke"), 1);
        var sender = new MacNotificationSender(store,
            (request, done) =>
            {
                UNNotificationContent content = request.Content;
                requests.Add((request.Identifier, content.Title, content.Body, content.ThreadIdentifier, content.Attachments.Length,
                    content.Attachments.Length > 0 ? content.Attachments[0].Identifier : null));
                if (content.Attachments.Length > 0)
                {
                    // The system accepts a picture only if it is a readable PNG of the expected size.
                    string path = content.Attachments[0].Url?.Path ?? "";
                    using var bitmap = new NSBitmapImageRep(NSData.FromFile(path) ?? throw new InvalidOperationException("attachment file is unreadable"));
                    Check(bitmap.PixelsWide == MacNotificationImage.Pixels && bitmap.PixelsHigh == MacNotificationImage.Pixels,
                        "the attached picture is not 120x120.");
                }
                bool reject = rejectAlways > 0 || (rejectWithAttachment > 0 && content.Attachments.Length > 0);
                done(reject ? Error() : null);
            },
            action => action(), () => dark, () => failures++);
        var orange = new TrayAppearance(AgentState.FullThrottle, TrayAttention.Orange);

        sender.Send("t1", "b1", MacNotificationKind.ProviderIssue, orange);
        Check(requests is [{ Id: "aiburgerclock-1", Title: "t1", Body: "b1", Thread: "aiburgerclock.status", Attachments: 1, AttachmentId: "status-1" }],
            "a status notice must carry its picture, text and status thread.");
        Check(store.Files().Length == 0, "the temporary picture was not removed after the request was accepted.");

        dark = true;
        sender.Send("t2", "b2", MacNotificationKind.Quota, null);
        Check(requests.Count == 2 && requests[1] is { Thread: "aiburgerclock.quota", Attachments: 1 } &&
              requests[1].Title == "t2" && requests[1].Body == "b2", "a quota notice must carry a picture and the quota thread.");

        // Before the first tick: no appearance, so the notice goes out as before (text only, status thread).
        sender.Send("t3", "b3", MacNotificationKind.Holiday, null);
        Check(requests.Count == 3 && requests[2] is { Attachments: 0, Thread: "aiburgerclock.status", Title: "t3" },
            "a status notice without an appearance must be sent without a picture.");

        // The picture cannot be written (the folder path is a file): still sent, text only, nothing reported.
        string blocked = Path.Combine(Path.GetTempPath(), "aibc-notify-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocked, "not a folder");
        try
        {
            var blockedSender = new MacNotificationSender(new MacNotificationImageStore(Path.Combine(blocked, "images")),
                (request, done) =>
                {
                    requests.Add((request.Identifier, request.Content.Title, request.Content.Body, request.Content.ThreadIdentifier,
                        request.Content.Attachments.Length, null));
                    done(null);
                }, action => action(), () => false, () => failures++);
            blockedSender.Send("t4", "b4", MacNotificationKind.ScheduleFull, orange);
            Check(requests.Count == 4 && requests[3] is { Attachments: 0, Title: "t4", Id: "aiburgerclock-1" } && failures == 0,
                "an unwritable picture folder must not stop the notification.");
        }
        finally { File.Delete(blocked); }

        // The system refuses a request that carries a picture: the same notification goes out once more without it.
        rejectWithAttachment = 1;
        sender.Send("t5", "b5", MacNotificationKind.ScheduleBurger, new TrayAppearance(AgentState.BurgerTime, TrayAttention.Orange));
        Check(requests.Count == 6 && requests[4] is { Attachments: 1, Id: "aiburgerclock-4" } &&
              requests[5] is { Attachments: 0, Id: "aiburgerclock-4", Title: "t5", Body: "b5", Thread: "aiburgerclock.status" } && failures == 0,
            "a refused picture must be retried once as a plain notification, without reporting a failure.");
        Check(store.Files().Length == 0, "temporary pictures were left behind after the retry.");

        // The plain notification fails too: reported once, no further retry.
        rejectAlways = 1;
        sender.Send("t6", "b6", MacNotificationKind.Holiday, null);
        Check(requests.Count == 7 && failures == 1, "a refused plain notification must be reported once and not retried.");
        sender.Send("t7", "b7", MacNotificationKind.Holiday, orange);
        Check(requests.Count == 9 && failures == 2 && requests[8].Attachments == 0,
            "a request refused with and without a picture must be reported once, after one retry.");
        return requests.Count;
    }

    private static void VerifyStore(string directory)
    {
        string folder = Path.Combine(directory, "limit");
        var store = new MacNotificationImageStore(folder);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < 25; i++)
        {
            string path = Path.Combine(folder, $"old-{i}.png");
            File.WriteAllBytes(path, [1, 2, 3]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-60 + i));
        }
        using NSData png = MacNotificationImage.Png(MacNotificationKind.Quota, null, false)!;
        string? written = store.Write(png, 99, MacNotificationKind.Quota);
        Check(written is not null && File.Exists(written) && store.Files().Length <= MacNotificationImageStore.MaximumFiles,
            $"the picture folder kept {store.Files().Length} files (limit {MacNotificationImageStore.MaximumFiles}).");
        Check(File.Exists(written!) && !File.Exists(Path.Combine(folder, "old-0.png")) && File.Exists(Path.Combine(folder, "old-24.png")),
            "the folder limit must remove the oldest files first.");
        store.Sweep();
        Check(store.Files().Length == 0, "the start sweep did not remove the leftovers.");
    }

    // Writes the picture through the same path the app uses and reads it back as a PNG.
    private static NSBitmapImageRep Decode(MacNotificationKind kind, TrayAppearance? appearance, bool dark)
    {
        using NSData png = MacNotificationImage.Png(kind, appearance, dark) ?? throw new InvalidOperationException("Notification image: no picture was drawn for " + kind);
        return new NSBitmapImageRep(png);
    }

    private static void CheckFrame(NSBitmapImageRep picture, string context)
    {
        Check(picture.PixelsWide == MacNotificationImage.Pixels && picture.PixelsHigh == MacNotificationImage.Pixels, "the picture is not 120x120: " + context);
        Check(At(picture, 0, 20).A < 0.05 && At(picture, 20, 20).A < 0.05, "the picture's top corners must be transparent: " + context);
    }

    // Badge on the lower-right corner of the 20-point grid: blue for Info, yellow for Warning (sampled off the letter).
    private static void CheckBadge(NSBitmapImageRep picture, MacNotificationLevel level, string context)
    {
        (double x, double y) = level == MacNotificationLevel.Info ? (14.0, 3.0) : (14.0, 1.6);
        Check(MacStatusIcon.Near(At(picture, x, y), MacNotificationImage.LevelColor(level), 0.03), $"{level} badge color is wrong: {context}");
    }

    // True if a pixel inside the 20-point box [x0,x1] x [y0,y1] has the color (the letter or arrow).
    private static bool HasColor(NSBitmapImageRep picture, double x0, double y0, double x1, double y1, NSColor color, double tolerance)
    {
        for (double y = y0; y < y1; y += 0.25)
        for (double x = x0; x < x1; x += 0.25)
            if (MacStatusIcon.Near(At(picture, x, y), color, tolerance)) return true;
        return false;
    }

    private static (double R, double G, double B, double A) CellAverage(NSBitmapImageRep bitmap, int cellX, int cellY, double scale,
        double height, bool convert)
    {
        double red = 0, green = 0, blue = 0, alpha = 0;
        for (int a = 0; a < 8; a++)
        for (int b = 0; b < 8; b++)
        {
            var pixel = At(bitmap, cellX + (a + 0.5) / 8, cellY + (b + 0.5) / 8, scale, height, convert);
            red += pixel.R * pixel.A;
            green += pixel.G * pixel.A;
            blue += pixel.B * pixel.A;
            alpha += pixel.A;
        }
        return (red / 64, green / 64, blue / 64, alpha / 64);
    }

    private static string Format((double R, double G, double B, double A) pixel) => $"{pixel.R:0.00}/{pixel.G:0.00}/{pixel.B:0.00}/{pixel.A:0.00}";

    private static string Describe(NSColor color)
    {
        NSColor srgb = color.UsingColorSpace(NSColorSpace.SRGBColorSpace)!;
        return $"{srgb.RedComponent:0.00}/{srgb.GreenComponent:0.00}/{srgb.BlueComponent:0.00}";
    }

    // Failure detail only: the most saturated opaque pixel in the letter area.
    private static string Darkest(NSBitmapImageRep picture)
    {
        (double R, double G, double B, double A) best = default;
        double bestSpread = -1;
        for (double y = 5; y < 15; y += 0.25)
        for (double x = 6; x < 14; x += 0.25)
        {
            var pixel = At(picture, x, y);
            double spread = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            if (pixel.A > 0.98 && spread > bestSpread) { best = pixel; bestSpread = spread; }
        }
        return $"{best.R:0.00}/{best.G:0.00}/{best.B:0.00}";
    }

    // A point on the 20-point grid (origin bottom-left, as drawn) read from the bitmap (row 0 is the top).
    private static (double R, double G, double B, double A) At(NSBitmapImageRep bitmap, double x, double y) =>
        At(bitmap, x, y, (double)bitmap.PixelsWide / MacStatusIcon.Size, bitmap.PixelsHigh);

    // convert: a bitmap drawn in device RGB (the menu-bar smoke's) is read back through sRGB; our own PNG is tagged sRGB,
    // so its components are the drawn sRGB values as they are (converting them again would shift the colors).
    private static (double R, double G, double B, double A) At(NSBitmapImageRep bitmap, double x, double y, double scale, double height,
        bool convert = false)
    {
        int column = Math.Clamp((int)Math.Floor(x * scale), 0, (int)bitmap.PixelsWide - 1);
        int row = Math.Clamp((int)(height - 1 - Math.Floor(y * scale)), 0, (int)bitmap.PixelsHigh - 1);
        if (convert) return MacStatusIcon.Pixel(bitmap, column, row);
        using var raw = bitmap.ColorAt(column, row) ?? throw new InvalidOperationException("Notification image: pixel unavailable.");
        raw.GetRgba(out var red, out var green, out var blue, out var alpha);
        return (red, green, blue, alpha);
    }
}
