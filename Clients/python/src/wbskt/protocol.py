"""The device protocol's names and limits (Docs/Device.Protocol.md). Mirrors Clients/Wbskt.Protocol."""

SUBPROTOCOL = "wbskt.v1"
MAX_MESSAGE_BYTES = 64 * 1024
MAX_TYPE_LENGTH = 100

SYSTEM_PREFIX = "sys."
PING = "sys.ping"
PONG = "sys.pong"
ACK = "sys.ack"
CAPABILITIES = "capabilities"
STATE_REPORT = "state.report"

REFUSED_EXPIRED = "expired"

REGISTERED = 1
PENDING = 0

API_PREFIX = "/api/v1"
REGISTER_PATH = API_PREFIX + "/client-registrations/initiate"
LOGIN_PATH = API_PREFIX + "/client-auth/login"
SOCKET_PATH = "/ws"

CLOSE_SERVER_RESTARTING = 1001


def is_reserved(message_type: str) -> bool:
    """A type only the platform may use: ``sys.*`` in any case, ``capabilities`` and ``state.report``."""
    lowered = message_type.lower()
    return lowered.startswith(SYSTEM_PREFIX) or lowered in (CAPABILITIES, STATE_REPORT)
