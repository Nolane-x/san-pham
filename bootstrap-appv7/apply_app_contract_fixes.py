from pathlib import Path
import hashlib, sys

root = Path(sys.argv[1])
PATCHES = {
    'src/Magic.Capture.App/Ai/Provider/AiProviderClientBase.cs': {
        'pre': 'ba1fc024821647c322b1358fbfc7d1de9df2a535859f7c9de615f9c624cf19da',
        'post': '6ccd49ecaa3cb5b06a77cd8a888096d5b0a5f9239cf6df72419b031d2da1bdd9',
        'old': b'    public abstract Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default);\n    public abstract Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default);',
        'new': b'    public abstract Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default);\n    public abstract Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);\n    public abstract Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default);',
    },
    'src/Magic.Capture.App/Platform/NativeMessageRouter.cs': {
        'pre': 'dcbdf9ca0daf874918dde1fe5b58521f40fc1f98a58b4cd84cab8adae9ee3824',
        'post': 'aec65dcde0e5be5dd4cf0614abfea9dc818ac83d34357f5cbc3e27858d68d7f9',
        'old': b'internal sealed record NativeWindowMessage(uint Message, UIntPtr WParam, IntPtr LParam) : EventArgs;',
        'new': b'internal sealed class NativeWindowMessage(uint message, UIntPtr wParam, IntPtr lParam) : EventArgs\n{\n    public uint Message { get; } = message;\n    public UIntPtr WParam { get; } = wParam;\n    public IntPtr LParam { get; } = lParam;\n}',
    },
    'src/Magic.Capture.App/App.xaml.cs': {
        'pre': 'aa3805a9d970920f790ededf7848e397499922fad07a2f10e46f445c665c0d50',
        'post': 'f491a139a0a4eabc541b24937c36a38354b118af7f7baf72864aa073b75be271',
        'old': b'    protected override void OnLaunched(LaunchActivatedEventArgs args)',
        'new': b'    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)',
    },
    'src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs': {
        'pre': '4a6ab372fd04f4ed70907d6ec7c8bd0d0c289e66377ede700bc0263e08b9c4ad',
        'post': 'cc61b555dfb335f5a156e1c652f9cd1685fee7ec93dd7c73ccc2eaf202b544b8',
        'old': b'using Magic.Capture.Core.ScreenGraph;\nusing Magic.Capture.Core.Privacy;',
        'new': b'using Magic.Capture.Core.ScreenGraph;\nusing Magic.Capture.Core.Settings;\nusing Magic.Capture.Core.Privacy;',
    },
}

def sha(data): return hashlib.sha256(data).hexdigest()
for rel, p in PATCHES.items():
    path = root / rel
    data = path.read_bytes()
    actual = sha(data)
    if actual != p['pre']:
        raise SystemExit(f'preimage SHA mismatch for {rel}: {actual}')
    if data.count(p['old']) != 1:
        raise SystemExit(f'expected exactly one patch site in {rel}')
    data = data.replace(p['old'], p['new'], 1)
    if sha(data) != p['post']:
        raise SystemExit(f'postimage SHA mismatch for {rel}: {sha(data)}')
    path.write_bytes(data)
print('OK app contract fixes: 4 files patched with verified pre/post SHA-256')
