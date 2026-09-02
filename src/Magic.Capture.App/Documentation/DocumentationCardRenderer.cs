using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Documentation;

namespace Magic.Capture.App.Documentation;

internal sealed class DocumentationCardRenderer
{
    public const long MaximumLongImagePixels = 150_000_000;
    private const int MaximumLongImageDimension = 65_000;

    public byte[] RenderOverviewCard(DocumentationProject project, byte[]? logoPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        project = DocumentationPolicy.Normalize(project);
        var profile = DocumentationTemplateCatalog.Get(project.Template);
        var contents = DocumentationTextExport.BuildContents(project);
        var sectionCount = contents.Count(item => !string.IsNullOrWhiteSpace(item.Section));
        var headerBand = string.IsNullOrWhiteSpace(project.Header) ? 0 : 34;
        var logoBand = logoPng is { Length: > 0 } ? 110 : 0;
        var subtitleBand = string.IsNullOrWhiteSpace(project.Subtitle) ? 0 : 48;
        var tocHeight = contents.Count == 0 ? 0 : 70 + contents.Count * 30 + sectionCount * 24;
        var footerBand = string.IsNullOrWhiteSpace(project.Footer) ? 0 : 48;
        var cardWidth = profile.CardWidth;
        var cardHeight = checked(profile.OuterPadding * 2 + headerBand + logoBand + 96 + subtitleBand + tocHeight + footerBand);
        ValidateBitmapSize(cardWidth, cardHeight, "documentation overview");

        using var output = new Bitmap(cardWidth, cardHeight, PixelFormat.Format32bppArgb);
        output.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(output);
        ConfigureGraphics(graphics);
        graphics.Clear(Color.White);
        using var border = new Pen(Color.FromArgb(214, 220, 226), 2);
        using var titleBrush = new SolidBrush(Color.FromArgb(31, 35, 40));
        using var mutedBrush = new SolidBrush(Color.FromArgb(101, 109, 118));
        using var accentBrush = new SolidBrush(Color.FromArgb(37, 99, 235));
        using var titleFont = new Font("Segoe UI", Math.Max(28, profile.TitleFontPixels + 12), FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Segoe UI", Math.Max(16, profile.BodyFontPixels + 1), FontStyle.Regular, GraphicsUnit.Pixel);
        using var headingFont = new Font("Segoe UI", Math.Max(19, profile.TitleFontPixels + 2), FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", profile.BodyFontPixels, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);

        graphics.DrawRectangle(border, 1, 1, cardWidth - 3, cardHeight - 3);
        var x = profile.OuterPadding;
        var y = profile.OuterPadding;
        var contentWidth = cardWidth - profile.OuterPadding * 2;

        if (!string.IsNullOrWhiteSpace(project.Header))
        {
            graphics.DrawString(project.Header, smallFont, mutedBrush, new RectangleF(x, y, contentWidth, 24));
            y += headerBand;
        }

        if (logoPng is { Length: > 0 })
        {
            DocumentationArchivePolicy.ValidateImageLength(logoPng.LongLength);
            using var logo = BitmapCodec.DecodeForPixelProcessing(logoPng);
            DrawContained(graphics, logo, new RectangleF(x, y, Math.Min(260, contentWidth), 86));
            y += logoBand;
        }

        graphics.DrawString(project.Title, titleFont, titleBrush, new RectangleF(x, y, contentWidth, 82));
        y += 88;
        if (!string.IsNullOrWhiteSpace(project.Subtitle))
        {
            graphics.DrawString(project.Subtitle, subtitleFont, mutedBrush, new RectangleF(x, y, contentWidth, 42));
            y += subtitleBand;
        }

        if (contents.Count > 0)
        {
            graphics.DrawString("Contents", headingFont, titleBrush, new RectangleF(x, y, contentWidth, 38));
            y += 46;
            foreach (var item in contents)
            {
                if (!string.IsNullOrWhiteSpace(item.Section))
                {
                    graphics.DrawString(item.Section, bodyFont, accentBrush, new RectangleF(x, y, contentWidth, 24));
                    y += 24;
                }
                graphics.DrawString($"{item.StepNumber}. {item.Title}", bodyFont, titleBrush,
                    new RectangleF(x + 18, y, contentWidth - 18, 28));
                y += 30;
            }
        }

        if (!string.IsNullOrWhiteSpace(project.Footer))
            graphics.DrawString(project.Footer, smallFont, mutedBrush,
                new RectangleF(x, cardHeight - profile.OuterPadding - 28, contentWidth, 24));
        return BitmapCodec.EncodePng(output);
    }

    public byte[] RenderStepCard(DocumentationProject project, DocumentationStep step, byte[] sourcePng, int stepNumber, byte[]? logoPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(sourcePng);
        if (stepNumber <= 0) throw new ArgumentOutOfRangeException(nameof(stepNumber));
        project = DocumentationPolicy.Normalize(project);
        var profile = DocumentationTemplateCatalog.Get(project.Template);
        DocumentationArchivePolicy.ValidateImageLength(sourcePng.LongLength);

        using var source = BitmapCodec.DecodeForPixelProcessing(sourcePng);
        var innerMax = profile.CardWidth - profile.OuterPadding * 2;
        var scale = Math.Min(1d, innerMax / (double)source.Width);
        var imageWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var imageHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var cardWidth = Math.Clamp(imageWidth + profile.OuterPadding * 2, profile.MinimumCardWidth, profile.CardWidth);
        var cardHeight = checked(profile.HeaderHeight + profile.ImagePadding + imageHeight + profile.DescriptionHeight + profile.FooterHeight);
        ValidateBitmapSize(cardWidth, cardHeight, "documentation card");

        using var output = new Bitmap(cardWidth, cardHeight, PixelFormat.Format32bppArgb);
        output.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(output);
        ConfigureGraphics(graphics);
        graphics.Clear(Color.White);

        using var border = new Pen(Color.FromArgb(214, 220, 226), 2);
        using var titleBrush = new SolidBrush(Color.FromArgb(31, 35, 40));
        using var mutedBrush = new SolidBrush(Color.FromArgb(101, 109, 118));
        using var accentBrush = new SolidBrush(Color.FromArgb(37, 99, 235));
        using var markerFill = new SolidBrush(Color.FromArgb(215, 239, 68, 68));
        using var markerBorder = new Pen(Color.White, 4);
        using var titleFont = new Font("Segoe UI", profile.TitleFontPixels, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", profile.BodyFontPixels, FontStyle.Regular, GraphicsUnit.Pixel);
        using var numberFont = new Font("Segoe UI", Math.Max(15, profile.TitleFontPixels - 1), FontStyle.Bold, GraphicsUnit.Pixel);
        using var sourceFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var headerFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);

        graphics.DrawRectangle(border, 1, 1, cardWidth - 3, cardHeight - 3);
        var titleTop = 18f;
        if (!string.IsNullOrWhiteSpace(project.Header))
        {
            graphics.DrawString(project.Header, headerFont, mutedBrush,
                new RectangleF(profile.OuterPadding, 6, cardWidth - profile.OuterPadding * 2, 18));
            titleTop = 26f;
        }
        var badge = new RectangleF(profile.OuterPadding, titleTop, 36, 36);
        graphics.FillEllipse(accentBrush, badge);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(stepNumber.ToString(), numberFont, Brushes.White, badge, center);

        var title = string.IsNullOrWhiteSpace(step.Title) ? $"Step {stepNumber}" : step.Title!;
        var logoReserve = logoPng is { Length: > 0 } ? 150 : 0;
        graphics.DrawString(title, titleFont, titleBrush,
            new RectangleF(profile.OuterPadding + 50, titleTop - 2, cardWidth - profile.OuterPadding * 2 - 50 - logoReserve, 48));
        if (logoPng is { Length: > 0 })
        {
            DocumentationArchivePolicy.ValidateImageLength(logoPng.LongLength);
            using var logo = BitmapCodec.DecodeForPixelProcessing(logoPng);
            DrawContained(graphics, logo, new RectangleF(cardWidth - profile.OuterPadding - 132, 16, 132, 48));
        }

        var imageX = (cardWidth - imageWidth) / 2;
        var imageY = profile.HeaderHeight + profile.ImagePadding;
        graphics.DrawImage(source, new Rectangle(imageX, imageY, imageWidth, imageHeight), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        graphics.DrawRectangle(border, imageX, imageY, imageWidth, imageHeight);

        if (step.ClickPoint is { } click)
        {
            var markerX = imageX + (float)(click.X * scale);
            var markerY = imageY + (float)(click.Y * scale);
            const float radius = 15;
            var marker = new RectangleF(markerX - radius, markerY - radius, radius * 2, radius * 2);
            graphics.FillEllipse(markerFill, marker);
            graphics.DrawEllipse(markerBorder, marker);
        }

        var textTop = imageY + imageHeight + 18;
        if (!string.IsNullOrWhiteSpace(step.Description))
            graphics.DrawString(step.Description, bodyFont, titleBrush,
                new RectangleF(profile.OuterPadding, textTop, cardWidth - profile.OuterPadding * 2, Math.Max(48, profile.DescriptionHeight - 32)));
        var sourceLabel = BuildSourceLabel(step);
        if (sourceLabel is not null)
            graphics.DrawString(sourceLabel, sourceFont, mutedBrush,
                new RectangleF(profile.OuterPadding, cardHeight - profile.FooterHeight - 22, cardWidth - profile.OuterPadding * 2, 18));
        if (!string.IsNullOrWhiteSpace(project.Footer))
            graphics.DrawString(project.Footer, headerFont, mutedBrush,
                new RectangleF(profile.OuterPadding, cardHeight - 22, cardWidth - profile.OuterPadding * 2, 18));
        return BitmapCodec.EncodePng(output);
    }

    public byte[] RenderLongImage(DocumentationProject project, IReadOnlyDictionary<string, byte[]> images, byte[]? logoPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(images);
        project = DocumentationPolicy.Normalize(project);
        if (project.Steps.Count == 0) throw new InvalidDataException("Long-image export requires at least one documentation step.");
        var profile = DocumentationTemplateCatalog.Get(project.Template);

        var cards = new List<Bitmap>(project.Steps.Count + 1);
        try
        {
            cards.Add(BitmapCodec.DecodeForPixelProcessing(RenderOverviewCard(project, logoPng)));
            var width = cards[0].Width;
            long height = cards[0].Height;
            for (var i = 0; i < project.Steps.Count; i++)
            {
                var step = project.Steps[i];
                if (!images.TryGetValue(step.ImageKey, out var source))
                    throw new InvalidDataException($"Missing documentation image: {step.ImageKey}");
                var cardBytes = RenderStepCard(project, step, source, i + 1, logoPng);
                var card = BitmapCodec.DecodeForPixelProcessing(cardBytes);
                cards.Add(card);
                width = Math.Max(width, card.Width);
                height = checked(height + card.Height + 20);
                if (height > MaximumLongImageDimension || (long)width * height > MaximumLongImagePixels)
                    throw new InvalidDataException("Documentation long image exceeds the safe 150,000,000-pixel export limit.");
            }

            ValidateBitmapSize(width, checked((int)height), "documentation long image");
            using var output = new Bitmap(width, (int)height, PixelFormat.Format32bppArgb);
            output.SetResolution(96, 96);
            using var graphics = Graphics.FromImage(output);
            ConfigureGraphics(graphics);
            graphics.Clear(profile.Id == "print" ? Color.White : Color.FromArgb(245, 247, 250));
            var y = 0;
            foreach (var card in cards)
            {
                var x = (width - card.Width) / 2;
                graphics.DrawImageUnscaled(card, x, y);
                y += card.Height + 20;
            }
            return BitmapCodec.EncodePng(output);
        }
        finally
        {
            foreach (var card in cards) card.Dispose();
        }
    }

    public IReadOnlyList<byte[]> RenderCards(DocumentationProject project, IReadOnlyDictionary<string, byte[]> images, byte[]? logoPng = null, bool includeOverview = true)
    {
        project = DocumentationPolicy.Normalize(project);
        var result = new List<byte[]>(project.Steps.Count + (includeOverview ? 1 : 0));
        if (includeOverview) result.Add(RenderOverviewCard(project, logoPng));
        for (var i = 0; i < project.Steps.Count; i++)
        {
            var step = project.Steps[i];
            if (!images.TryGetValue(step.ImageKey, out var source))
                throw new InvalidDataException($"Missing documentation image: {step.ImageKey}");
            result.Add(RenderStepCard(project, step, source, i + 1, logoPng));
        }
        return result;
    }

    private static void DrawContained(Graphics graphics, Image image, RectangleF bounds)
    {
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var width = Math.Max(1f, image.Width * scale);
        var height = Math.Max(1f, image.Height * scale);
        var x = bounds.X;
        var y = bounds.Y + (bounds.Height - height) / 2f;
        graphics.DrawImage(image, x, y, width, height);
    }

    private static string? BuildSourceLabel(DocumentationStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.SafeKeyGesture)) return "Keyboard: " + step.SafeKeyGesture;
        var target = step.Target;
        if (target is null) return null;
        if (!string.IsNullOrWhiteSpace(target.WindowTitle)) return target.WindowTitle;
        return string.IsNullOrWhiteSpace(target.ProcessName) ? null : target.ProcessName;
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    private static void ValidateBitmapSize(int width, int height, string label)
    {
        if (width <= 0 || height <= 0 || width > MaximumLongImageDimension || height > MaximumLongImageDimension ||
            (long)width * height > MaximumLongImagePixels)
            throw new InvalidDataException($"The {label} dimensions exceed the safe rendering limit.");
    }
}
