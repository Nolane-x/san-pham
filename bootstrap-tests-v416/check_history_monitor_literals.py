from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
HISTORY = 'tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs'
HISTORY_SHA = 'b3a65dfc8c8332db22d48bb1757fa4ddc1c77116cdddc8ef06b63d7337a05fac'
PROJECT = 'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj'
PROJECT_SHA = '71c50c7e3d7581a5933dffe6ca62b2dbff1b17f31176004dc1a8e3254d15ee71'

history = (ROOT / HISTORY).read_text(encoding='utf-8')
history_digest = hashlib.sha256(history.encode('utf-8')).hexdigest()
if history_digest != HISTORY_SHA:
    raise SystemExit(f'{HISTORY}: sha256 {history_digest} != {HISTORY_SHA}')
for display in ('DISPLAY1', 'DISPLAY2'):
    expected = f'monitor: @"\\\\.\\{display}"'
    if history.count(expected) != 1:
        raise SystemExit(f'{HISTORY}: expected one verbatim monitor literal for {display}')
    invalid = f'monitor: "\\\\.\\{display}"'
    if invalid in history:
        raise SystemExit(f'{HISTORY}: invalid escaped monitor literal still present for {display}')

project = (ROOT / PROJECT).read_text(encoding='utf-8')
project_digest = hashlib.sha256(project.encode('utf-8')).hexdigest()
if project_digest != PROJECT_SHA:
    raise SystemExit(f'{PROJECT}: sha256 {project_digest} != {PROJECT_SHA}')
if project.count('<Using Include="Xunit" />') != 1:
    raise SystemExit(f'{PROJECT}: expected exactly one xUnit global using')

print(f'GREEN test compile contracts: history={history_digest} project={project_digest}')
