"""Service layer — exercises ABC / Protocol / free function / abstract method."""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Protocol

from .models import Order, User


class UserRepo(Protocol):
    """A typing.Protocol — should report kind=protocol, isAbstract=True."""

    def get(self, user_id: int) -> User: ...

    def save(self, user: User) -> None: ...


class OrderRepo(ABC):
    """An abc.ABC base — should report kind=class, isAbstract=True, isAbc=True."""

    @abstractmethod
    def find(self, order_id: int) -> Order: ...

    @abstractmethod
    def persist(self, order: Order) -> None: ...


class OrderService:
    """Concrete service class with a multi-parameter constructor."""

    def __init__(self, users: UserRepo, orders: OrderRepo, default_currency: str = "USD") -> None:
        self.users = users
        self.orders = orders
        self.default_currency = default_currency

    def place(self, user_id: int) -> Order:
        user = self.users.get(user_id)
        order = Order(order_id=0, user=user)
        self.orders.persist(order)
        return order


def calculate_discount(amount: int, percent: float = 10.0) -> int:
    """Top-level function — should appear as kind=function."""
    return int(amount * (1 - percent / 100.0))
