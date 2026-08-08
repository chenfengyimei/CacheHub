"""Django serializer for User model."""
from rest_framework import serializers
from .models import User


class UserSerializer(serializers.ModelSerializer):
    # BUG: nullable fields are dropped — fields with null=True are not serialized
    # when the value is None, instead of being included as null in the response.
    class Meta:
        model = User
        fields = ['id', 'email', 'name', 'avatar', 'bio', 'phone', 'is_active']

    def to_representation(self, instance):
        data = super().to_representation(instance)
        # BUG: This removes nullable fields instead of preserving them as null
        return {k: v for k, v in data.items() if v is not None}
