"""The two HTTPS calls a device makes, on the standard library so the SDK has one dependency."""

import json
import urllib.error
import urllib.request
from typing import Any, Dict, Mapping, Optional, Tuple

Response = Tuple[int, Any, Mapping[str, str]]

TIMEOUT = 30.0


def post_json(url: str, body: Dict[str, Any], timeout: float = TIMEOUT) -> Response:
    """POST ``body`` as JSON. Returns the status, the parsed body (None if it isn't JSON) and the headers.

    An HTTP error status is returned, not raised; a network failure raises ``OSError``.
    """
    request = urllib.request.Request(
        url,
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, _parse(response.read()), dict(response.headers)
    except urllib.error.HTTPError as error:
        with error:
            return error.code, _parse(error.read()), dict(error.headers or {})


def _parse(raw: bytes) -> Optional[Any]:
    try:
        return json.loads(raw) if raw else None
    except ValueError:
        return None
