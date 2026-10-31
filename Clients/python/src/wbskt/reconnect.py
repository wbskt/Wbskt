"""When to try again after the connection drops (Docs/Device.Protocol.md §5).

Every delay is drawn at random from zero up to a ceiling ("full jitter"), so a fleet that lost its
socket host at the same moment comes back spread out instead of logging in all at once.
"""

import random
from typing import Optional

SPREAD = 10.0
RESTART_SPREAD = 30.0
BASE = 2.0
CAP = 60.0


class ReconnectPolicy:
    def __init__(self, rng: Optional[random.Random] = None) -> None:
        self._rng = rng or random.Random()

    def first_delay(self, server_restarting: bool) -> float:
        return self._up_to(RESTART_SPREAD if server_restarting else SPREAD)

    def retry_delay(self, failures: int) -> float:
        """The wait after ``failures`` failed attempts in a row: up to base × 2^failures, capped."""
        return self._up_to(min(CAP, BASE * (2 ** min(max(failures, 0), 10))))

    def after_throttle(self, retry_after: float) -> float:
        return retry_after + self._up_to(BASE)

    def _up_to(self, ceiling: float) -> float:
        return self._rng.random() * ceiling
