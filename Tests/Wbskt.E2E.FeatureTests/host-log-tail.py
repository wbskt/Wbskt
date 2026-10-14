#!/usr/bin/env python3
"""Prints, for each failing test in the trx files under RESULTS_DIR, the host log lines written
while that test ran.

    host-log-tail.py TestResults e2e-hosts

xunit v2 gives a test no hook that knows whether it passed, so this runs after the suite instead:
the trx records each test's start and end, and every host log line starts with its timestamp
(Config/serilog.json: [yyyy-MM-dd HH:mm:ss.fff]). The hosts log in the runner's local time, which
on GitHub's runners is UTC; set E2E_HOST_LOG_UTC_OFFSET_MINUTES when it is not. Lines without a
timestamp (stack traces, multi-line messages) belong to the stamped line above them.

Standard library only. Exits 0 even when there is nothing to print: it is a report, not a gate.
"""

import glob
import os
import re
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timedelta, timezone

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
STAMP = re.compile(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]")
# Slack around the trx window: the host may log a request a moment after the client gave up on it.
SLACK = timedelta(seconds=2)
MAX_LINES = 400


def parse_trx_time(value):
    # e.g. 2026-10-14T03:12:01.1234567+00:00; Python's fromisoformat takes at most 6 fraction digits.
    m = re.match(r"^(.*T\d{2}:\d{2}:\d{2})(?:\.(\d+))?(.*)$", value)
    if not m:
        return None
    base, frac, tz = m.group(1), (m.group(2) or "0")[:6].ljust(6, "0"), m.group(3) or "+00:00"
    if tz == "Z":
        tz = "+00:00"
    try:
        return datetime.fromisoformat(f"{base}.{frac}{tz}").astimezone(timezone.utc)
    except ValueError:
        return None


def failing_tests(results_dir):
    for path in sorted(glob.glob(os.path.join(results_dir, "**", "*.trx"), recursive=True)):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as e:
            print(f"::warning::cannot parse {path}: {e}")
            continue
        for r in root.iterfind(".//t:Results/t:UnitTestResult", NS):
            if r.get("outcome") != "Failed":
                continue
            start, end = parse_trx_time(r.get("startTime", "")), parse_trx_time(r.get("endTime", ""))
            if start and end:
                yield r.get("testName", "?"), start, end


def log_lines_between(path, start, end, offset):
    out, keep = [], False
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = STAMP.match(line)
            if m:
                ts = (datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S.%f") - offset).replace(tzinfo=timezone.utc)
                if ts > end + SLACK:
                    break
                keep = ts >= start - SLACK
            if keep:
                out.append(line.rstrip("\n"))
    return out


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    results_dir, logs_dir = sys.argv[1], sys.argv[2]
    offset = timedelta(minutes=int(os.environ.get("E2E_HOST_LOG_UTC_OFFSET_MINUTES", "0")))
    logs = sorted(glob.glob(os.path.join(logs_dir, "*.log")))
    found = False
    for name, start, end in failing_tests(results_dir):
        found = True
        print(f"::group::{name} ({start:%H:%M:%S.%f}Z .. {end:%H:%M:%S.%f}Z)")
        for log in logs:
            lines = log_lines_between(log, start, end, offset)
            if not lines:
                continue
            print(f"----- {os.path.basename(log)}: {len(lines)} line(s)")
            if len(lines) > MAX_LINES:
                print(f"(first {MAX_LINES // 2} and last {MAX_LINES // 2})")
                lines = lines[: MAX_LINES // 2] + ["..."] + lines[-MAX_LINES // 2:]
            print("\n".join(lines))
        print("::endgroup::")
    if not found:
        print("no failing tests with a start and end time in the trx files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
