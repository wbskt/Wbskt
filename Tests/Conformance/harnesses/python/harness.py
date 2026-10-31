"""Conformance harness for the Python SDK: drives AsyncWbsktClient from JSON lines on stdin."""

import asyncio
import json
import os
import sys
from typing import Any, Dict

from wbskt import AsyncWbsktClient, Command, DataType, FileStorage, Parameter


def emit(event: str, **fields: Any) -> None:
    sys.stdout.write(json.dumps({"event": event, **fields}) + "\n")
    sys.stdout.flush()


def command_from_json(data: Dict[str, Any]) -> Command:
    parameters = [
        Parameter(
            name=p["name"],
            data_type=DataType(p.get("dataType", "string")),
            label=p.get("label", ""),
            description=p.get("description", ""),
            is_required=p.get("isRequired", False),
        )
        for p in data.get("parameters", [])
    ]
    return Command(data["command"], data.get("description", ""), parameters)


async def read_lines(queue: "asyncio.Queue[str]") -> None:
    loop = asyncio.get_running_loop()
    while True:
        line = await loop.run_in_executor(None, sys.stdin.readline)
        await queue.put(line)
        if not line:
            return


async def main() -> None:
    client = AsyncWbsktClient(
        os.environ["WBSKT_DEVICE_NAME"],
        os.environ.get("WBSKT_POLICY_PIN") or None,
        api_url=os.environ["WBSKT_API_URL"],
        socket_url=os.environ["WBSKT_SOCKET_URL"],
        storage=FileStorage(os.environ["WBSKT_STORAGE"]),
        capabilities=[command_from_json(c) for c in json.loads(os.environ.get("WBSKT_COMMANDS", "[]"))],
    )
    client.on_connected = lambda: emit("connected")
    client.on_disconnected = lambda: emit("disconnected")
    client.on_message = lambda m: emit("message", type=m.type, payload=m.payload, commandId=m.command_id)

    await client.start()
    emit("started")

    lines: "asyncio.Queue[str]" = asyncio.Queue()
    reader = asyncio.ensure_future(read_lines(lines))
    try:
        while True:
            line = await lines.get()
            if not line:
                break
            op = json.loads(line)
            name = op.get("op")
            if name == "stop":
                break
            try:
                if name == "send":
                    await client.send(op["type"], op.get("payload"))
                    emit("sent", type=op["type"])
                elif name == "state":
                    await client.report_state(op["values"])
                    emit("sent", type="state.report")
            except ValueError as error:
                emit("rejected", type=op.get("type"), reason=str(error))
    finally:
        await client.stop()
        reader.cancel()


if __name__ == "__main__":
    asyncio.run(main())
    # The stdin reader thread may still be blocked on readline.
    os._exit(0)
