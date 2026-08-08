"""Tests for UserSerializer — verify nullable fields are preserved."""
import pytest
from unittest.mock import MagicMock
from src.serializers.user_serializer import UserSerializer


class TestUserSerializer:
    def test_nullable_fields_preserved(self):
        """Nullable fields should be in output as null, not dropped."""
        user = MagicMock()
        user.id = 1
        user.email = "test@test.com"
        user.name = "Test User"
        user.avatar = None
        user.bio = None
        user.phone = None
        user.is_active = True

        serializer = UserSerializer(user)
        data = serializer.data

        # These should be present as null, not absent
        assert 'avatar' in data, "avatar field should be present (as null)"
        assert 'bio' in data, "bio field should be present (as null)"
        assert 'phone' in data, "phone field should be present (as null)"

    def test_non_null_fields_preserved(self):
        user = MagicMock()
        user.id = 1
        user.email = "test@test.com"
        user.name = "Test"
        user.avatar = "https://example.com/avatar.png"
        user.bio = "Hello"
        user.phone = "123-456-7890"
        user.is_active = True

        serializer = UserSerializer(user)
        data = serializer.data
        assert data['avatar'] == "https://example.com/avatar.png"
        assert data['bio'] == "Hello"
