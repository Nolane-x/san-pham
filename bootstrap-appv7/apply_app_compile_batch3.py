from pathlib import Path
import hashlib
import sys

root = Path(sys.argv[1])
path = root / "src/Magic.Capture.App/MainWindow.xaml.cs"
data = path.read_bytes()

expected_pre = "468ed0177505b5763f86dfff7cd0812f1575784be6cd1f75ef25c3501b3a88b8"
expected_post = "79450aa768aebf888a8443354513481e63c460eb30618a1833bde49f478db0ae"
old = b"IReadOnlyCollection<string>? resumeCheckpoint = null;"
new = b"IReadOnlySet<string>? resumeCheckpoint = null;"

actual_pre = hashlib.sha256(data).hexdigest()
if actual_pre != expected_pre:
    raise SystemExit(f"preimage SHA mismatch for MainWindow.xaml.cs: {actual_pre}")

old_count = data.count(old)
new_count = data.count(new)
if old_count != 1 or new_count != 0:
    raise SystemExit(
        f"unexpected resume checkpoint pattern counts: old={old_count} new={new_count}"
    )

data = data.replace(old, new, 1)
actual_post = hashlib.sha256(data).hexdigest()
if actual_post != expected_post:
    raise SystemExit(f"postimage SHA mismatch for MainWindow.xaml.cs: {actual_post}")

path.write_bytes(data)
print(
    "OK app compile batch3: MainWindow resume checkpoint contract "
    f"pre={actual_pre} post={actual_post}"
)
