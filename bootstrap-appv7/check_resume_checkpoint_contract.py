from pathlib import Path
import hashlib
import sys

root = Path(sys.argv[1])
path = root / "src/Magic.Capture.App/MainWindow.xaml.cs"
data = path.read_bytes()

expected_pre = "468ed0177505b5763f86dfff7cd0812f1575784be6cd1f75ef25c3501b3a88b8"
old = b"IReadOnlyCollection<string>? resumeCheckpoint = null;"
new = b"IReadOnlySet<string>? resumeCheckpoint = null;"

actual = hashlib.sha256(data).hexdigest()
if actual != expected_pre:
    raise SystemExit(f"unexpected MainWindow preimage: {actual}")

old_count = data.count(old)
new_count = data.count(new)
if old_count != 1 or new_count != 0:
    raise SystemExit(
        f"unexpected resume checkpoint pattern counts: old={old_count} new={new_count}"
    )

candidate = data.replace(old, new, 1)
candidate_post = hashlib.sha256(candidate).hexdigest()
print(f"RED resume checkpoint contract: pre={actual} candidate_post={candidate_post}")
raise SystemExit(
    "resume checkpoint is IReadOnlyCollection<string>; workflow resume contract requires IReadOnlySet<string>"
)
