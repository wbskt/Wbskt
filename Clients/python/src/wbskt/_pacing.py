"""A token bucket that paces the client's sends below the gateway's message budget."""

import asyncio
import time


class Pacer:
    """``burst`` sends back to back, then ``per_second``. Single event loop only."""

    def __init__(self, per_second: float, burst: int) -> None:
        self._per_second = max(0.1, per_second)
        self._burst = max(1, burst)
        self._tokens = float(self._burst)
        self._last = time.monotonic()

    async def take(self) -> None:
        while True:
            now = time.monotonic()
            self._tokens = min(self._burst, self._tokens + (now - self._last) * self._per_second)
            self._last = now
            if self._tokens >= 1:
                self._tokens -= 1
                return
            await asyncio.sleep((1 - self._tokens) / self._per_second)
