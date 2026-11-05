"""When to try again after the connection drops (Docs/Client.Protocol.md §5).

Every delay is drawn at random from zero up to a ceiling ("full jitter"), so a fleet that lost its
socket host at the same moment comes back spread out instead of logging in all at once.
"""

import random
from typing import Optional

from . import protocol

SPREAD = 10.0
RESTART_SPREAD = 30.0
# After a close for sending too much: at least this long, plus up to as long again.
THROTTLED_WAIT = 30.0
BASE = 2.0
CAP = 60.0


class ReconnectPolicy:
    def __init__(self, rng: Optional[random.Random] = None) -> None:
        self._rng = rng or random.Random()

    def first_delay(self, close_code: Optional[int]) -> float:
        """The wait after a connection ended, by the code the server closed it with (None if it dropped)."""
        if close_code == protocol.CLOSE_SERVER_RESTARTING:
            return self._up_to(RESTART_SPREAD)
        if close_code == protocol.CLOSE_THROTTLED:
            return THROTTLED_WAIT + self._up_to(THROTTLED_WAIT)
        return self._up_to(SPREAD)

    def retry_delay(self, failures: int) -> float:
        """The wait after ``failures`` failed attempts in a row: up to base × 2^failures, capped."""
        return self._up_to(min(CAP, BASE * (2 ** min(max(failures, 0), 10))))

    def after_throttle(self, retry_after: float) -> float:
        return retry_after + self._up_to(BASE)

    def _up_to(self, ceiling: float) -> float:
        return self._rng.random() * ceiling
