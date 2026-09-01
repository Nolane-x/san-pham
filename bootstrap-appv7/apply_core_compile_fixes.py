from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')

FILES = {
    'src/Magic.Capture.Core/Documentation/DocumentationArchivePolicy.cs': (
        'e1413015cd232c83df14d827dfe2a08d78c231b9993f73947869288454bf1f4a',
        'e43b7de63e8350476f5d8dabc8fb4409604e6e6e14f02936dfd0446a5e226cad'),
    'src/Magic.Capture.Core/Portability/PortableArchivePolicy.cs': (
        '2e95b1f5c234573dcb1fbc7d5f4aacb0899bb4c628b2b03dbf47ad4e349479bf',
        '2183c88629c8fb1e15da15b479f030c451bdb2a843ab7413e85dd3b19a3d0ae7'),
    'src/Magic.Capture.Core/Imaging/BgraContentBounds.cs': (
        '9a41134aac57ef5acbc534c8335bd298175890aa64bb5badff9dcbb75fffc344',
        '6fb93fa76010d492fbb5643f34c108d08ceb5cad0639a5a5a635d3833b1b31db'),
    'src/Magic.Capture.Core/Imaging/TranslationAlignment.cs': (
        '486ab55252a10e834623d2f3d10ce64f5c7ef19ee728c1bb7f065188678409f2',
        'ad65535d94eb164edc95d46fbc7b0b663285895b7f7e266ae4c21bbac0895875'),
    'src/Magic.Capture.Core/Settings/AppSettingsRules.cs': (
        'cf8247f01b9e899b6bab55ffa5030a7197a9228dc9168f63de32dcd72e563138',
        '5d68b8d93067576af3f198b105e4893c4be092e82764e92906e5ad81bfed8bc6'),
    'src/Magic.Capture.Core/Ocr/OcrLayoutDiff.cs': (
        '83d8bcd6e5957e3253da06e24dc470cc4371415cd20cbf3bf7a99fdfa97e20f1',
        '593d3555506a86f156f6cd9f01cc4a0c8477bbfd8d10ff21da3e6b9c94826c5c'),
}

def digest(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()

def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected one replacement target, found {count}')
    return text.replace(old, new, 1)

texts: dict[str, str] = {}
for rel, (before, _) in FILES.items():
    p = ROOT / rel
    text = p.read_text(encoding='utf-8')
    actual = digest(text)
    if actual != before:
        raise SystemExit(f'{rel}: preimage sha256 {actual} != {before}')
    texts[rel] = text

for rel in [
    'src/Magic.Capture.Core/Documentation/DocumentationArchivePolicy.cs',
    'src/Magic.Capture.Core/Portability/PortableArchivePolicy.cs',
]:
    texts[rel] = replace_once(
        texts[rel],
        "name.StartsWith('/', StringComparison.Ordinal) || name.EndsWith('/', StringComparison.Ordinal)",
        'name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal)',
        rel)

rel = 'src/Magic.Capture.Core/Imaging/BgraContentBounds.cs'
texts[rel] = replace_once(texts[rel],
'''        var bb = Median(corners.Select(i => bgra[i]).ToArray());
        var bg = Median(corners.Select(i => bgra[i + 1]).ToArray());
        var br = Median(corners.Select(i => bgra[i + 2]).ToArray());''',
'''        var bb = Median(new byte[] { bgra[corners[0]], bgra[corners[1]], bgra[corners[2]], bgra[corners[3]] });
        var bg = Median(new byte[] { bgra[corners[0] + 1], bgra[corners[1] + 1], bgra[corners[2] + 1], bgra[corners[3] + 1] });
        var br = Median(new byte[] { bgra[corners[0] + 2], bgra[corners[1] + 2], bgra[corners[2] + 2], bgra[corners[3] + 2] });''', rel)

rel = 'src/Magic.Capture.Core/Imaging/TranslationAlignment.cs'
texts[rel] = replace_once(texts[rel],
'        void Consider(int offsetX, int offsetY, int evaluationSampleStep)',
'''        static void Consider(
            ReadOnlySpan<byte> first,
            ReadOnlySpan<byte> second,
            int width,
            int height,
            int maxOffset,
            long pixelCount,
            CancellationToken cancellationToken,
            int offsetX,
            int offsetY,
            int evaluationSampleStep,
            ref TranslationAlignmentResult best,
            ref int bestDistance,
            ref int evaluated)''', rel)
texts[rel] = replace_once(texts[rel], '            Consider(0, 0, fineSampleStep);',
'''            Consider(first, second, width, height, maxOffset, pixelCount, cancellationToken,
                0, 0, fineSampleStep, ref best, ref bestDistance, ref evaluated);''', rel)
texts[rel] = replace_once(texts[rel], '                Consider(offsetX, offsetY, stageSampleStep);',
'''                Consider(first, second, width, height, maxOffset, pixelCount, cancellationToken,
                    offsetX, offsetY, stageSampleStep, ref best, ref bestDistance, ref evaluated);''', rel)

rel = 'src/Magic.Capture.Core/Settings/AppSettingsRules.cs'
texts[rel] = texts[rel].replace(
    '            var action = source.PostCaptureAction is { } candidate && Enum.IsDefined(typeof(PostCaptureAction), candidate) ? candidate : null;',
    '            PostCaptureAction? action = source.PostCaptureAction is { } candidate && Enum.IsDefined(typeof(PostCaptureAction), candidate) ? candidate : null;')
if texts[rel].count('PostCaptureAction? action = source.PostCaptureAction') != 2:
    raise SystemExit(f'{rel}: expected two nullable action declarations')
texts[rel] = replace_once(texts[rel],
    '            var region = profile.Region is { } candidate && !candidate.IsEmpty ? candidate : null;',
    '            PixelRect? region = profile.Region is { } candidate && !candidate.IsEmpty ? candidate : null;', rel)

rel = 'src/Magic.Capture.Core/Ocr/OcrLayoutDiff.cs'
for old, new in [
    ('changes.Add(new(a.Text, a.Bounds, PixelRect.Empty, true, true));', 'changes.Add(new(a.Text ?? string.Empty, a.Bounds, PixelRect.Empty, true, true));'),
    ('if (textChanged || moved) changes.Add(new(b.Text, a.Bounds, b.Bounds, textChanged, moved));', 'if (textChanged || moved) changes.Add(new(b.Text ?? string.Empty, a.Bounds, b.Bounds, textChanged, moved));'),
    ('if (!used[j]) changes.Add(new(rightLines[j].Text, PixelRect.Empty, rightLines[j].Bounds, true, true));', 'if (!used[j]) changes.Add(new(rightLines[j].Text ?? string.Empty, PixelRect.Empty, rightLines[j].Bounds, true, true));'),
]:
    texts[rel] = replace_once(texts[rel], old, new, rel)

for rel, (_, after) in FILES.items():
    text = texts[rel]
    actual = digest(text)
    if actual != after:
        raise SystemExit(f'{rel}: postimage sha256 {actual} != {after}')
    (ROOT / rel).write_text(text, encoding='utf-8', newline='')

print('OK core compile fixes: 6 files patched with verified pre/post SHA-256')
