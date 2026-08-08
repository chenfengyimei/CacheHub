"""User repository with fetch functions."""
import functools
from typing import Optional, List
from dataclasses import dataclass


@dataclass
class User:
    id: int
    email: str
    name: str
    active: bool = True


class UserRepository:
    def __init__(self, db_session):
        self._session = db_session

    def get_by_id(self, user_id: int) -> Optional[User]:
        row = self._session.query("SELECT * FROM users WHERE id = ?", (user_id,))
        if not row:
            return None
        return User(id=row[0], email=row[1], name=row[2], active=row[3])

    def get_all(self) -> List[User]:
        rows = self._session.query_all("SELECT * FROM users")
        return [User(id=r[0], email=r[1], name=r[2], active=r[3]) for r in rows]

    def create(self, email: str, name: str) -> User:
        # BUG: No retry decorator — transient DB failures cause unhandled exceptions
        cursor = self._session.execute(
            "INSERT INTO users (email, name, active) VALUES (?, ?, 1)",
            (email, name)
        )
        return User(id=cursor.lastrowid, email=email, name=name)

    def update(self, user_id: int, **fields) -> bool:
        if not fields:
            return False
        sets = ", ".join(f"{k} = ?" for k in fields)
        values = list(fields.values()) + [user_id]
        cursor = self._session.execute(
            f"UPDATE users SET {sets} WHERE id = ?", values
        )
        return cursor.rowcount > 0

    def delete(self, user_id: int) -> bool:
        cursor = self._session.execute("DELETE FROM users WHERE id = ?", (user_id,))
        return cursor.rowcount > 0
