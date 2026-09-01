from pathlib import Path
import hashlib, sys

root = Path(sys.argv[1])

def sha(data): return hashlib.sha256(data).hexdigest()

PATCHES = {
    'src/Magic.Capture.App/Analysis/WindowsOcrService.cs': {
        'pre': '6802b29628c11bb90ce866a77817677e4399d28d7adc0fd05c28df67214c3d51',
        'post': '3a614066eb5711ea85b9c822b1b928bad60b30ecdd7b4487f20d3524b01db06a',
        'ops': [
            (b'using Magic.Capture.Core.Ocr;', b'using Magic.Capture.Core.Ocr;\nusing CoreOcrLine = Magic.Capture.Core.Ocr.OcrLine;\nusing CoreOcrWord = Magic.Capture.Core.Ocr.OcrWord;', 1),
            (b'var lines = new List<OcrLine>();', b'var lines = new List<CoreOcrLine>();', 1),
            (b'return new OcrWord(nativeWord.Text, ScaleToOriginal(rect, resize));', b'return new CoreOcrWord(nativeWord.Text, ScaleToOriginal(rect, resize));', 1),
            (b'lines.Add(new OcrLine(nativeLine.Text, bounds, words));', b'lines.Add(new CoreOcrLine(nativeLine.Text, bounds, words));', 1),
        ],
    },
    'src/Magic.Capture.App/Capture/WindowCaptureService.cs': {
        'pre': '6b526a551adbb22868156557dfb52dd3e25602d0e9148da669cde77a66a6d6fc',
        'post': 'ec884934449ee60728fe9f5f1c205ef58f388b31a08539e739203d73d1ce9218',
        'ops': [(b'NativeMethods.EnumWindows((hwnd, _) =>', b'NativeMethods.EnumWindows((hwnd, ignored) =>', 1)],
    },
    'src/Magic.Capture.App/App.xaml.cs': {
        'pre': 'f491a139a0a4eabc541b24937c36a38354b118af7f7baf72864aa073b75be271',
        'post': 'ce7da4718817aa432d5576289d1e30a294d189299212c279ee3940a2d0b3d9b1',
        'ops': [(b'RecognizeAsync(asset, Services.Settings.PreferredOcrLanguage)', b'RecognizeAsync(asset.PngBytes, Services.Settings.PreferredOcrLanguage)', 1)],
    },
    'src/Magic.Capture.App/Capture/DesktopDuplicationCaptureBackend.cs': {
        'pre': '750de70011c6f600c4e6203c3e45defac2ea3502c41f0fcb532c2e69ae039131',
        'post': 'e0167f0e370675e14ad1f54467dbe2f55f2d6fc15eaf387d53c17ce15a9f7b7f',
        'ops': [(b'acquire == ResultCode.WaitTimeout', b'acquire == Vortice.DXGI.ResultCode.WaitTimeout', 1), (b'MapMode.Read, MapFlags.None', b'MapMode.Read, Vortice.Direct3D11.MapFlags.None', 1)],
    },
    'src/Magic.Capture.App/Documentation/DocumentationExportService.cs': {
        'pre': '6f6a2cb1b5a16aaf97f611720459dcee9c639287384fd184fe38ae683a77dbd9',
        'post': '2b5dc3f9377b9bff3b65de39ecab4902efab9ca61a86bced92af5062a3614c4c',
        'ops': [(b'using Magic.Capture.Core.Documentation;', b'using Magic.Capture.Core.Capture;\nusing Magic.Capture.Core.Documentation;', 1)],
    },
    'src/Magic.Capture.App/Imaging/AnnotationRenderer.cs': {'pre': '9a53d41f046427c576422cb574d98ddc5153dbb149508ef6bef17b0156d79382', 'post': '34afa465bc29e0025062f72c3e436f246554f33898ed170a77b6c1da67e21bb5', 'ops': [(b'Math.Min(90, color.A)', b'Math.Min(90, (int)color.A)', 1)]},
    'src/Magic.Capture.App/Imaging/BitmapContentBounds.cs': {'pre': 'b8b2c70f2bcaa832b4c4a49108e54f1dce334d8297c5b5e93c3239db9f439ebe', 'post': '308dcd3f2c3f11c6dac4eb16ef26d41847fe6092ced63eaa28d41616ce1f79b6', 'ops': [(b'using Magic.Capture.Core.Geometry;', b'using Magic.Capture.Core.Geometry;\nusing Magic.Capture.Core.Imaging;', 1)]},
    'src/Magic.Capture.App/Persistence/EditableProjectService.cs': {'pre': 'f902298f03b7e9951237821c9770197bfea7671fe42de11a446677fa46e5c582', 'post': '65307780ba6f8e0bddc4cfd7ab5903b445ef51c49130cb498a2c184d866c8ffd', 'ops': [(b'using System.Text.Json;', b'using System.Text.Json;\nusing Magic.Capture.App.Imaging;', 1)]},
    'src/Magic.Capture.App/Views/MeasurementOverlayWindow.xaml.cs': {'pre': '281350973d6b886b9a59c0dec2fd8ab7958b160ee61ae56d731644da28ed4cb8', 'post': '3b3ef0d8c54519f3f0997093fe2db22eeec2fbfdf5cbc899ebbf0f12d54de475', 'ops': [(b'using Microsoft.UI.Xaml;', b'using Microsoft.UI.Xaml;\nusing Microsoft.UI.Xaml.Controls;', 1)]},
    'src/Magic.Capture.App/Platform/HotkeyService.cs': {'pre': '056822b931ba9bdc9952abf373dd59ebdbb319fe0f86b46cde6f36ab197e4118', 'post': '019576fabc33bbf096f0da6ece54c8af4bd4aebfd6e3e9c7837e3eba54478666', 'ops': [(b'unchecked((int)message.WParam.ToInt64())', b'unchecked((int)message.WParam)', 1)]},
    'src/Magic.Capture.App/Views/PinWindow.xaml.cs': {'pre': '532d695beaccc64b9cfe5aa8b4d819ee463eef011b21e4ac5ead5b53cc411201', 'post': '85e27d68ec2bbb431a55faaf32bbb26679a485d1a397e66c5fd4ea1860ce4221', 'ops': [(b'Windows.UI.Text.FontWeights.SemiBold', b'Microsoft.UI.Text.FontWeights.SemiBold', 1)]},
    'src/Magic.Capture.App/Views/MonitorTestWindow.xaml.cs': {'pre': '6f7698dfeeb6bc4efdbcc9e6c96d093b1126a6b8b7356dbbd041cf4b97c7e8c4', 'post': '1f2b46ff5b8753939761a0f4d9354176edcff2aa2882c85f2ec75957f9c1e156', 'ops': [(b'Windows.UI.Colors', b'Microsoft.UI.Colors', 17)]},
    'src/Magic.Capture.App/Recording/Mp4RecordingEncoder.cs': {'pre': '052664e92e316b466332e2e2850a839fc2b975a9436dc38a1775533f847b2a35', 'post': 'c35cc61d0a035f45322c157d0cea6ba2a5a86ed29ad23418def745997a8311a5', 'ops': [(b'RecordingAudioPolicy.ChannelCount', b'RecordingAudioPolicy.Channels', 2)]},
    'src/Magic.Capture.App/Recording/M4aAudioRecordingEncoder.cs': {'pre': '4068b7c450c71be1503f91f121754e50bd230763ffe3156027c8d92ae129c11c', 'post': 'cf1fc9d337653628dfebdde1b02af112875aa2f70f336c89f636e5651ef1db59', 'ops': [(b'RecordingAudioPolicy.ChannelCount', b'RecordingAudioPolicy.Channels', 2)]},
    'src/Magic.Capture.App/VideoEditing/VideoEditAdvancedRenderService.cs': {'pre': '64c3f351f23ab9a5754df6b30430109c48363510c588b7fea8370acf1bd44a91', 'post': '07c41b781acbd9fda1e3489c947155d7010d41d8dec58e46ed91b57a45ee69c7', 'ops': [(b'RecordingAudioPolicy.ChannelCount', b'RecordingAudioPolicy.Channels', 3)]},
    'src/Magic.Capture.App/VideoEditing/VideoEditOverlayAssetStore.cs': {'pre': '3c2a3cafc65d0a1f7bc072c32335f70acbce877fd74df12093feb73a2733d814', 'post': '1ee8f90c79ac7c5e0b9ddfad2fd295a0cb134b7e6b264609825e29d63e2a1667', 'ops': [(b'catch (IOException ex) { _log.Warn("VideoEdit.OverlayCache", ex.Message); }', b'catch (IOException ex) { _log.Error("VideoEdit.OverlayCache", ex); }', 1), (b'catch (UnauthorizedAccessException ex) { _log.Warn("VideoEdit.OverlayCache", ex.Message); }', b'catch (UnauthorizedAccessException ex) { _log.Error("VideoEdit.OverlayCache", ex); }', 1)]},
    'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs': {'pre': 'be3d10ddaee6db06326481c91f3b0fabae613acbb9d0f9d5fa581673e1e60ef7', 'post': '47f92295807c8073acc8fd6a424a18d26cb5ba809c3b206bc56a542cec28df86', 'ops': [(b'        var target = preview ? SelectionPathPreview : SelectionPath;\n        target.Points.Clear();\n        foreach (var point in _pathPoints) target.Points.Add(ToDip(point));\n        if (preview && _selectionKind == CaptureSelectionKind.Polygon && _pathHoverPoint is { } hover && _pathPoints.Count > 0)\n            target.Points.Add(ToDip(hover));\n        target.Visibility = target.Points.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;', b'        var targetPoints = preview ? SelectionPathPreview.Points : SelectionPath.Points;\n        UIElement targetVisual = preview ? SelectionPathPreview : SelectionPath;\n        targetPoints.Clear();\n        foreach (var point in _pathPoints) targetPoints.Add(ToDip(point));\n        if (preview && _selectionKind == CaptureSelectionKind.Polygon && _pathHoverPoint is { } hover && _pathPoints.Count > 0)\n            targetPoints.Add(ToDip(hover));\n        targetVisual.Visibility = targetPoints.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;', 1)]},
    'src/Magic.Capture.App/MainWindow.xaml.cs': {
        'pre': 'fac615084e9bcfea03d7e69dfb9466aaa061c3585e85a7fe6a3a678ce6833e19',
        'post': '468ed0177505b5763f86dfff7cd0812f1575784be6cd1f75ef25c3501b3a88b8',
        'ops': [
            (b'using Magic.Capture.Core.Geometry;', b'using Magic.Capture.Core.Geometry;\nusing Magic.Capture.Core.Export;\nusing Magic.Capture.Core.Platform;', 1),
            (b'        var required = FeatureCatalog.RequiredTier(feature);', b'        var selectedFeature = feature.Value;\n        var required = FeatureCatalog.RequiredTier(selectedFeature);', 1),
            (b'FriendlyFeature(feature)', b'FriendlyFeature(selectedFeature)', 2),
            (b'RecognizeAsync(asset, Services.Settings.PreferredOcrLanguage)', b'RecognizeAsync(asset.PngBytes, Services.Settings.PreferredOcrLanguage)', 1),
            (b'        var trace = view.Record;\n        CaptureWorkflow? workflow = null;\n        Guid? assetId = trace.AssetId;\n        var executionStarted = false;\n        try', b'        var trace = view.Record;\n        CaptureWorkflow? workflow = null;\n        Guid? assetId = trace.AssetId;\n        var executionStarted = false;\n        IReadOnlyCollection<string>? resumeCheckpoint = null;\n        try', 1),
            (b'ResumeCompletedSideEffectStepIds = plan.CompletedSafeSideEffectStepIds', b'ResumeCompletedSideEffectStepIds = resumeCheckpoint', 1),
            (b'resumeCompletedSideEffectStepIds: plan.CompletedSafeSideEffectStepIds);', b'resumeCompletedSideEffectStepIds: resumeCheckpoint);', 2),
            (b'            var plan = WorkflowResumePlanner.CreatePlan(workflow, trace);', b'            var plan = WorkflowResumePlanner.CreatePlan(workflow, trace);\n            resumeCheckpoint = plan.CompletedSafeSideEffectStepIds;', 1),
            (b'& FileAttributes.ReparsePoint', b'& System.IO.FileAttributes.ReparsePoint', 1),
            (b'AddHistoryFilterControl(Grid grid, UIElement control', b'AddHistoryFilterControl(Grid grid, FrameworkElement control', 1),
            (b'AiConfigurationPanel.IsEnabled = pro;', b'AiConfigurationPanel.IsHitTestVisible = pro;', 1),
            (b'AiContext: Services.AiContext.Items.Select(i => i.Asset).ToArray(),', b'AiContext: Services.AiContext.Assets,', 1),
        ],
    },
}

for rel, patch in PATCHES.items():
    path = root / rel
    data = path.read_bytes()
    actual = sha(data)
    if actual != patch['pre']:
        raise SystemExit(f'preimage SHA mismatch for {rel}: {actual}')
    for old, new, count in patch['ops']:
        actual_count = data.count(old)
        if actual_count != count:
            raise SystemExit(f'expected {count} patch site(s) in {rel}, got {actual_count}')
        data = data.replace(old, new)
    actual = sha(data)
    if actual != patch['post']:
        raise SystemExit(f'postimage SHA mismatch for {rel}: {actual}')
    path.write_bytes(data)

print(f'OK app compile batch2 fixes: {len(PATCHES)} files patched with verified pre/post SHA-256')
