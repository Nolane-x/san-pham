from pathlib import Path
import hashlib
import sys

root = Path(sys.argv[1])
path = root / "src/Magic.Capture.App/MainWindow.xaml.cs"
data = path.read_bytes()

expected_post = "79450aa768aebf888a8443354513481e63c460eb30618a1833bde49f478db0ae"
old = b"IReadOnlyCollection<string>? resumeCheckpoint = null;"
new = b"IReadOnlySet<string>? resumeCheckpoint = null;"

actual = hashlib.sha256(data).hexdigest()
if actual != expected_post:
    raise SystemExit(f"unexpected MainWindow postimage: {actual}")

old_count = data.count(old)
new_count = data.count(new)
if old_count != 0 or new_count != 1:
    raise SystemExit(
        f"resume checkpoint regression: old={old_count} new={new_count}"
    )

print(f"GREEN resume checkpoint contract: sha256={actual}")
