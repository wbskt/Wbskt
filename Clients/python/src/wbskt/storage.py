"""Where a device keeps the credentials it got when it registered."""

import json
import os
import re
import sys
from pathlib import Path
from typing import Optional, Protocol, Tuple

Credentials = Tuple[Optional[str], Optional[str]]


class Storage(Protocol):
    def load(self) -> Credentials: ...

    def save(self, client_ref_id: str, secret: str) -> None: ...

    def clear(self) -> None: ...


class FileStorage:
    """Credentials in a JSON file, so the device registers once and signs in with them afterwards."""

    def __init__(self, path) -> None:
        self.path = Path(path)

    @classmethod
    def for_device(cls, device_name: str) -> "FileStorage":
        """``wbskt/<name>.json`` in the user's data folder (``~/.local/share`` on Linux)."""
        safe = re.sub(r'[\\/:*?"<>|\x00-\x1f]', "_", device_name)
        return cls(_data_dir() / "wbskt" / f"{safe}.json")

    def load(self) -> Credentials:
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
            return str(data["clientRefId"]), str(data["secret"])
        except (OSError, ValueError, KeyError, TypeError):
            return None, None

    def save(self, client_ref_id: str, secret: str) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps({"clientRefId": client_ref_id, "secret": secret}), encoding="utf-8")
        try:
            os.chmod(tmp, 0o600)
        except OSError:
            pass
        os.replace(tmp, self.path)

    def clear(self) -> None:
        try:
            self.path.unlink()
        except FileNotFoundError:
            pass


class MemoryStorage:
    """Credentials held in memory only: for tests, or a device that is given them some other way."""

    def __init__(self, client_ref_id: Optional[str] = None, secret: Optional[str] = None) -> None:
        self._creds: Credentials = (client_ref_id, secret)

    def load(self) -> Credentials:
        return self._creds

    def save(self, client_ref_id: str, secret: str) -> None:
        self._creds = (client_ref_id, secret)

    def clear(self) -> None:
        self._creds = (None, None)


def _data_dir() -> Path:
    if sys.platform == "win32":
        return Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local")
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support"
    return Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local" / "share")
