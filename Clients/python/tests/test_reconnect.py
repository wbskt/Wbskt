import random

from wbskt import ReconnectPolicy
from wbskt.reconnect import CAP, RESTART_SPREAD, SPREAD


def test_delays_stay_within_their_ceilings():
    policy = ReconnectPolicy(random.Random(1))

    for _ in range(1000):
        assert 0 <= policy.first_delay(False) <= SPREAD
        assert 0 <= policy.first_delay(True) <= RESTART_SPREAD
        assert 0 <= policy.retry_delay(0) <= 2
        assert 0 <= policy.retry_delay(3) <= 16
        assert 0 <= policy.retry_delay(50) <= CAP
        assert 5 <= policy.after_throttle(5) <= 7


def test_delays_are_spread_rather_than_fixed():
    policy = ReconnectPolicy(random.Random(2))
    delays = [policy.first_delay(False) for _ in range(1000)]

    assert min(delays) < 1
    assert max(delays) > 9
