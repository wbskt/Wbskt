"""What the SDK hands you and what you hand it."""

from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Optional


@dataclass(frozen=True)
class Message:
    """A message from the server: a command when ``command_id`` is set."""

    type: str
    payload: Any = None
    command_id: Optional[str] = None
    expires_at: Optional[datetime] = None


class DataType(str, Enum):
    STRING = "string"
    NUMBER = "number"
    BOOLEAN = "boolean"
    OBJECT = "object"


@dataclass
class Parameter:
    name: str
    data_type: DataType = DataType.STRING
    label: str = ""
    description: str = ""
    is_required: bool = False

    def to_json(self) -> Dict[str, Any]:
        return {
            "name": self.name,
            "label": self.label or self.name,
            "dataType": DataType(self.data_type).value,
            "description": self.description,
            "isRequired": self.is_required,
        }


@dataclass
class Command:
    """A command the device accepts, so the console can offer it with a form."""

    command: str
    description: str = ""
    parameters: List[Parameter] = field(default_factory=list)

    def to_json(self) -> Dict[str, Any]:
        return {
            "command": self.command,
            "description": self.description,
            "parameters": [p.to_json() for p in self.parameters],
        }


class WbsktError(Exception):
    """The client cannot go on: the reason is in the message."""


class RegistrationRequired(WbsktError):
    """There are no stored credentials and no policy PIN to register with."""
