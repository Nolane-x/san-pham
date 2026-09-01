from __future__ import annotations
import hashlib
import runpy
import sys
from pathlib import Path

runpy.run_path(str(Path(__file__).with_name('apply_core_compile_fixes_base.py')), run_name='__main__')

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
FILES = {
    'src/Magic.Capture.Core/Projects/EditableProjectRecoveryPolicy.cs': (
        '0d557ecce39d182ed42450963ed7ad44c4a21a13226c7dc55559e1f945e5e879',
        '7dab069728eb352359091e4c0ef13f98b620328a297cc8cb62e1b11669784cdd'),
    'src/Magic.Capture.Core/History/HistoryRetentionPlanner.cs': (
        '8be10228b4e2a52582ac884a3d7d40b1ea446c5dc24392bcfb42d66a6c825ca7',
        'fd757eb9015588d52233ed7a4201d8f34fcadbc7f1c8bbfd9b92b8fc6a6cd09d'),
    'src/Magic.Capture.Core/Tables/TableCellInference.cs': (
        'e6b0808db3f6437e8a0e29ca0d5cc7af0412104287614f08c73909ab757eaa8d',
        '398810af58a7e64eb552c39082b427556f86df6928c099252d74edf0c66b6818'),
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
    text = (ROOT / rel).read_text(encoding='utf-8')
    actual = digest(text)
    if actual != before:
        raise SystemExit(f'{rel}: behavior preimage sha256 {actual} != {before}')
    texts[rel] = text

recovery = 'src/Magic.Capture.Core/Projects/EditableProjectRecoveryPolicy.cs'
texts[recovery] = replace_once(
    texts[recovery],
    '''            .Select(group => new EditableProjectRecoveryCandidate(group
                .OrderByDescending(item => item.Journal!.UpdatedUtc)
                .First().Journal!))''',
    '''            .Select(group => new EditableProjectRecoveryCandidate(group
                .OrderByDescending(item => item.Journal!.DirtyRevision)
                .ThenByDescending(item => item.Journal!.UpdatedUtc)
                .First().Journal!))''',
    recovery)

history = 'src/Magic.Capture.Core/History/HistoryRetentionPlanner.cs'
texts[history] = replace_once(
    texts[history],
    '''        if (policy.MaximumBytes is >= 0)
        {
            long keptBytes = 0;
            foreach (var item in remaining)
            {
                if (keptBytes + item.FileBytes <= policy.MaximumBytes.Value)
                    keptBytes += item.FileBytes;
                else
                    deleted.Add(item.Id);
            }
        }''',
    '''        if (policy.MaximumBytes is >= 0)
        {
            long keptBytes = 0;
            var budgetExhausted = false;
            foreach (var item in remaining)
            {
                var fileBytes = Math.Max(0, item.FileBytes);
                if (!budgetExhausted && fileBytes <= policy.MaximumBytes.Value - keptBytes)
                {
                    keptBytes += fileBytes;
                    continue;
                }

                budgetExhausted = true;
                deleted.Add(item.Id);
            }
        }''',
    history)

table = 'src/Magic.Capture.Core/Tables/TableCellInference.cs'
texts[table] = replace_once(
    texts[table],
    '''    private static bool TryParseInteger(string value, CultureInfo culture, out long number)
    {
        var styles = NumberStyles.Integer | NumberStyles.AllowThousands;
        if (long.TryParse(value, styles, culture, out number)) return true;
        return long.TryParse(value, styles, CultureInfo.InvariantCulture, out number);
    }''',
    '''    private static bool TryParseInteger(string value, CultureInfo culture, out long number)
    {
        if (LooksLikeFractionalNumber(value, culture))
        {
            number = default;
            return false;
        }

        var styles = NumberStyles.Integer | NumberStyles.AllowThousands;
        if (long.TryParse(value, styles, culture, out number)) return true;
        return long.TryParse(value, styles, CultureInfo.InvariantCulture, out number);
    }

    private static bool LooksLikeFractionalNumber(string value, CultureInfo culture)
    {
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        if (!string.IsNullOrEmpty(decimalSeparator) && value.Contains(decimalSeparator, StringComparison.Ordinal))
            return true;

        return !string.Equals(decimalSeparator, ".", StringComparison.Ordinal)
            && value.Contains('.', StringComparison.Ordinal)
            && !value.Contains(decimalSeparator, StringComparison.Ordinal);
    }''',
    table)

for rel, (_, after) in FILES.items():
    text = texts[rel]
    actual = digest(text)
    if actual != after:
        raise SystemExit(f'{rel}: behavior postimage sha256 {actual} != {after}')
    (ROOT / rel).write_text(text, encoding='utf-8', newline='')

print('OK Core behavior fixes: recovery revision authority + monotonic history byte budget + locale decimal inference, verified pre/post SHA-256')
