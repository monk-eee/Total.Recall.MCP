"""Domain models — exercises class / dataclass / frozen dataclass / enum."""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class OrderStatus(Enum):
    PENDING = "pending"
    APPROVED = "approved"
    SHIPPED = "shipped"
    CANCELLED = "cancelled"


@dataclass
class User:
    """A regular dataclass — mutable, no frozen."""

    id: int
    email: str
    display_name: str = ""


@dataclass(frozen=True)
class Money:
    """A frozen dataclass — properties should report hasInit=True."""

    amount: int
    currency: str = "USD"


class Order:
    """A plain class with an explicit __init__."""

    def __init__(self, order_id: int, user: User, status: OrderStatus = OrderStatus.PENDING) -> None:
        self.order_id = order_id
        self.user = user
        self.status = status
        self._cached_total: int | None = None

    def total(self) -> int:
        return self._cached_total or 0


class _InternalCache:
    """Leading-underscore class — should report isInternal=True."""

    def __init__(self) -> None:
        self.entries: dict[str, int] = {}
