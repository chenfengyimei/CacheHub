"""Tests for user_repository."""
import pytest
from unittest.mock import MagicMock
from src.repositories.user_repository import UserRepository, User


class TestUserRepository:
    def setup_method(self):
        self.session = MagicMock()
        self.repo = UserRepository(self.session)

    def test_get_by_id_found(self):
        self.session.query.return_value = (1, "test@test.com", "Test", 1)
        user = self.repo.get_by_id(1)
        assert user is not None
        assert user.id == 1
        assert user.email == "test@test.com"

    def test_get_by_id_not_found(self):
        self.session.query.return_value = None
        user = self.repo.get_by_id(999)
        assert user is None

    def test_get_all(self):
        self.session.query_all.return_value = [
            (1, "a@test.com", "A", 1),
            (2, "b@test.com", "B", 0),
        ]
        users = self.repo.get_all()
        assert len(users) == 2
        assert users[0].email == "a@test.com"

    def test_create(self):
        cursor = MagicMock()
        cursor.lastrowid = 42
        self.session.execute.return_value = cursor
        user = self.repo.create("new@test.com", "New User")
        assert user.id == 42
        assert user.email == "new@test.com"

    def test_update(self):
        cursor = MagicMock()
        cursor.rowcount = 1
        self.session.execute.return_value = cursor
        assert self.repo.update(1, name="Updated") is True

    def test_update_no_fields(self):
        assert self.repo.update(1) is False

    def test_delete(self):
        cursor = MagicMock()
        cursor.rowcount = 1
        self.session.execute.return_value = cursor
        assert self.repo.delete(1) is True
