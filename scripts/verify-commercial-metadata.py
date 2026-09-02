#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
from decimal import Decimal, InvalidOperation
from pathlib import Path

PRICE = re.compile(r"^\d+\.\d{2}$")


def _capture(pattern: str, text: str) -> str | None:
    match = re.search(pattern, text, re.MULTILINE)
    return match.group(1).strip() if match else None


def verify(root: Path) -> list[str]:
    errors: list[str] = []
    commercial_path = root / "release" / "commercial.json"
    readme_path = root / "README.md"
    store_guide_path = root / "packaging" / "STORE_SUBMISSION.md"

    for path in (commercial_path, readme_path, store_guide_path):
        if not path.is_file():
            errors.append(f"Missing commercial metadata file: {path.relative_to(root)}")
    if errors:
        return errors

    data = json.loads(commercial_path.read_text(encoding="utf-8"))
    market = str(data.get("market", "")).strip()
    currency = str(data.get("currency", "")).strip()
    app_price = str(data.get("appPrice", "")).strip()
    pro_msrp = str(data.get("proMsrp", "")).strip()
    launch_price = str(data.get("proLaunchPrice", "")).strip()
    sku_type = str(data.get("proSkuType", "")).strip()

    if market != "US":
        errors.append(f"release/commercial.json market must be 'US', got {market!r}.")
    if currency != "USD":
        errors.append(f"release/commercial.json currency must be 'USD', got {currency!r}.")
    if app_price != "0.00":
        errors.append(f"release/commercial.json appPrice must be '0.00', got {app_price!r}.")
    if int(data.get("plusTrialHours", 0) or 0) != 168:
        errors.append("release/commercial.json plusTrialHours must be 168.")
    if data.get("plusSold") is not False:
        errors.append("release/commercial.json plusSold must be false.")
    if sku_type != "Durable":
        errors.append(f"release/commercial.json proSkuType must be 'Durable', got {sku_type!r}.")
    if data.get("proLifetime") is not True:
        errors.append("release/commercial.json proLifetime must be true.")
    if data.get("subscription") is not False:
        errors.append("release/commercial.json subscription must be false.")
    if data.get("developerAiCreditsIncluded") is not False:
        errors.append("release/commercial.json developerAiCreditsIncluded must be false.")

    for field_name, value in (("proMsrp", pro_msrp), ("proLaunchPrice", launch_price)):
        if not PRICE.fullmatch(value):
            errors.append(f"release/commercial.json {field_name} must be an exact decimal string with two places, got {value!r}.")
    try:
        if PRICE.fullmatch(pro_msrp) and PRICE.fullmatch(launch_price):
            msrp_decimal = Decimal(pro_msrp)
            launch_decimal = Decimal(launch_price)
            if msrp_decimal <= 0:
                errors.append("release/commercial.json proMsrp must be greater than zero.")
            if launch_decimal <= 0 or launch_decimal >= msrp_decimal:
                errors.append("release/commercial.json proLaunchPrice must be greater than zero and lower than proMsrp.")
    except InvalidOperation:
        errors.append("release/commercial.json contains an invalid decimal price.")

    launch_days = data.get("launchDurationDays")
    if not isinstance(launch_days, int) or isinstance(launch_days, bool) or launch_days <= 0:
        errors.append("release/commercial.json launchDurationDays must be a positive integer.")
        launch_days = None

    expected_msrp = f"${pro_msrp}"
    expected_launch = f"${launch_price}"
    expected_duration = f"{launch_days} consecutive days" if launch_days is not None else None

    readme = readme_path.read_text(encoding="utf-8")
    readme_msrp = _capture(r"^Pro Lifetime MSRP \(US\)\s+(\$\d+\.\d{2})\s*$", readme)
    readme_launch = _capture(r"^Launch price \(US\)\s+(\$\d+\.\d{2})\s*$", readme)
    readme_duration = _capture(r"^Launch-price duration\s+(.+?)\s*$", readme)
    if readme_msrp != expected_msrp:
        errors.append(f"README.md Pro Lifetime MSRP {readme_msrp!r} does not match commercial metadata {expected_msrp!r}.")
    if readme_launch != expected_launch:
        errors.append(f"README.md launch price {readme_launch!r} does not match commercial metadata {expected_launch!r}.")
    if expected_duration and readme_duration != expected_duration:
        errors.append(f"README.md launch duration {readme_duration!r} does not match commercial metadata {expected_duration!r}.")

    store_guide = store_guide_path.read_text(encoding="utf-8")
    guide_msrp = _capture(r"^Pro Lifetime regular US MSRP\s+(\$\d+\.\d{2})\s*$", store_guide)
    guide_launch = _capture(r"^Pro Lifetime US launch\s+(\$\d+\.\d{2})\s*$", store_guide)
    guide_duration = _capture(r"^Launch duration\s+(.+?)\s*$", store_guide)
    expected_guide_duration = f"{expected_duration} from public Pro availability" if expected_duration else None
    if guide_msrp != expected_msrp:
        errors.append(f"STORE_SUBMISSION.md Pro Lifetime MSRP {guide_msrp!r} does not match commercial metadata {expected_msrp!r}.")
    if guide_launch != expected_launch:
        errors.append(f"STORE_SUBMISSION.md launch price {guide_launch!r} does not match commercial metadata {expected_launch!r}.")
    if expected_guide_duration and guide_duration != expected_guide_duration:
        errors.append(f"STORE_SUBMISSION.md launch duration {guide_duration!r} does not match commercial metadata {expected_guide_duration!r}.")

    return errors


def main(argv: list[str]) -> int:
    root = Path(argv[1]).resolve() if len(argv) > 1 else Path(__file__).resolve().parents[1]
    try:
        errors = verify(root)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"Commercial metadata verifier failed to read metadata: {exc}")
        return 2

    print("Magic Capture Desktop commercial metadata verifier")
    if errors:
        for error in errors:
            print(f"  ERROR: {error}")
        print(f"  Errors: {len(errors)}")
        return 1

    data = json.loads((root / "release" / "commercial.json").read_text(encoding="utf-8"))
    print(f"  US Pro MSRP       : ${data['proMsrp']}")
    print(f"  US launch price   : ${data['proLaunchPrice']}")
    print(f"  Launch duration   : {data['launchDurationDays']} days")
    print("  README / Store doc: consistent")
    print("  Errors             : 0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
