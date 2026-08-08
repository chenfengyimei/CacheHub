"""Schema validator for data pipeline."""
from typing import Dict, Any, List


class SchemaValidator:
    def __init__(self, required_fields: List[str]):
        self.required_fields = required_fields

    def validate(self, record: Dict[str, Any]) -> bool:
        """Returns True if record has all required fields."""
        for field in self.required_fields:
            if field not in record:
                return False
            if record[field] is None:
                return False
        return True

    def validate_batch(self, records: List[Dict[str, Any]]) -> List[bool]:
        return [self.validate(r) for r in records]
