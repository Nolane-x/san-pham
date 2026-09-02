using Magic.Capture.Core.Signals;

namespace Magic.Capture.Core.ScreenGraph;

public static class ScreenGraphBuilder
{
    public const int CurrentSchemaVersion = 1;

    public static ScreenGraphDocument Build(ScreenGraphBuildInput input)
    {
        var nodes = new List<ScreenGraphNode>
        {
            new("doc", ScreenNodeKind.Document, null, input.CaptureBounds, 1, null,
                new Dictionary<string, string>
                {
                    ["sourceKind"] = input.SourceKind,
                    ["width"] = input.Width.ToString(),
                    ["height"] = input.Height.ToString()
                })
        };

        var lineIndex = 0;
        var wordIndex = 0;
        foreach (var line in input.Ocr.Lines)
        {
            lineIndex++;
            var lineId = $"l{lineIndex}";
            nodes.Add(new ScreenGraphNode(lineId, ScreenNodeKind.TextLine, line.Text, line.Bounds, .98, "doc", null));
            foreach (var word in line.Words)
            {
                wordIndex++;
                nodes.Add(new ScreenGraphNode($"w{wordIndex}", ScreenNodeKind.Word, word.Text, word.Bounds, .98, lineId, null));
            }
        }

        if (input.Table is { } table)
        {
            nodes.Add(new ScreenGraphNode("t1", ScreenNodeKind.Table, null, table.Bounds, table.Confidence, "doc",
                new Dictionary<string, string>
                {
                    ["rows"] = table.RowCount.ToString(),
                    ["columns"] = table.ColumnCount.ToString(),
                    ["preview"] = string.Join(" | ", table.Rows.Take(4).Select(r => string.Join("\t", r)))
                }));
        }

        var barcodeIndex = 0;
        foreach (var barcode in input.Barcodes)
        {
            barcodeIndex++;
            nodes.Add(new ScreenGraphNode($"b{barcodeIndex}", ScreenNodeKind.Barcode, barcode.Value, barcode.Bounds, .99, "doc",
                new Dictionary<string, string> { ["format"] = barcode.Format }));
        }

        var signalIndex = 0;
        foreach (var signal in TextSignalExtractor.Extract(input.Ocr))
        {
            signalIndex++;
            nodes.Add(new ScreenGraphNode($"s{signalIndex}", Map(signal.Kind), signal.Value, signal.Bounds, signal.Confidence, "doc", null));
        }

        AddUiAutomationNodes(input.UiAutomationNodes, input.Ocr, nodes);

        return new ScreenGraphDocument(CurrentSchemaVersion, input.CaptureId, input.CreatedUtc, input.Width, input.Height,
            input.SourceKind, input.SourceDisplayName, nodes);
    }

    private static void AddUiAutomationNodes(IReadOnlyList<ScreenUiAutomationNode>? source, Magic.Capture.Core.Ocr.OcrDocument ocr, List<ScreenGraphNode> nodes)
    {
        if (source is null || source.Count == 0) return;
        var ocrEvidence = UiAutomationOcrCorrelation.Correlate(source, ocr);

        var accepted = new List<(ScreenUiAutomationNode Item, string Key)>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < source.Count; i++)
        {
            var key = string.IsNullOrWhiteSpace(source[i].StableKey) ? $"index-{i}" : source[i].StableKey;
            if (!seenKeys.Add(key)) continue;
            accepted.Add((source[i], key));
        }

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < accepted.Count; i++)
            ids[accepted[i].Key] = $"u{i + 1}";

        foreach (var (item, key) in accepted)
        {
            var parentId = !string.IsNullOrWhiteSpace(item.ParentStableKey) && ids.TryGetValue(item.ParentStableKey, out var mappedParent)
                ? mappedParent
                : "doc";
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["controlType"] = item.ControlType
            };
            AddAttribute(attributes, "automationId", item.AutomationId);
            AddAttribute(attributes, "value", item.Value);
            AddAttribute(attributes, "accessKey", item.AccessKey);
            AddAttribute(attributes, "acceleratorKey", item.AcceleratorKey);
            AddAttribute(attributes, "processId", item.ProcessId);
            AddAttribute(attributes, "processName", item.ProcessName);
            AddAttribute(attributes, "windowTitle", item.WindowTitle);
            AddAttribute(attributes, "enabled", item.IsEnabled);
            AddAttribute(attributes, "checked", item.IsChecked);
            AddAttribute(attributes, "selected", item.IsSelected);
            AddAttribute(attributes, "focused", item.HasKeyboardFocus);
            AddAttribute(attributes, "password", item.IsPassword);
            if (ocrEvidence.TryGetValue(key, out var correlation))
            {
                AddAttribute(attributes, "ocrText", correlation.Text);
                AddAttribute(attributes, "ocrWordIds", string.Join(",", correlation.WordIds));
                AddAttribute(attributes, "ocrWordCount", correlation.WordIds.Count);
            }
            nodes.Add(new ScreenGraphNode(ids[key], ScreenNodeKind.UiAutomation, item.Name ?? item.Value, item.Bounds, .99, parentId, attributes));
        }
    }

    private static void AddAttribute(Dictionary<string, string> attributes, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) attributes[key] = value;
    }

    private static void AddAttribute(Dictionary<string, string> attributes, string key, bool? value)
    {
        if (value.HasValue) attributes[key] = value.Value ? "true" : "false";
    }

    private static void AddAttribute(Dictionary<string, string> attributes, string key, int? value)
    {
        if (value.HasValue) attributes[key] = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ScreenNodeKind Map(TextSignalKind kind) => kind switch
    {
        TextSignalKind.Url => ScreenNodeKind.Url,
        TextSignalKind.Email => ScreenNodeKind.Email,
        TextSignalKind.Phone => ScreenNodeKind.Phone,
        TextSignalKind.IpAddress => ScreenNodeKind.IpAddress,
        TextSignalKind.FilePath => ScreenNodeKind.FilePath,
        TextSignalKind.StackFrame => ScreenNodeKind.StackFrame,
        TextSignalKind.ErrorHeadline => ScreenNodeKind.Error,
        TextSignalKind.ErrorCode => ScreenNodeKind.ErrorCode,
        TextSignalKind.Money => ScreenNodeKind.Money,
        TextSignalKind.Percentage => ScreenNodeKind.Percentage,
        TextSignalKind.LineReference => ScreenNodeKind.LineReference,
        _ => ScreenNodeKind.CodeLike
    };
}
