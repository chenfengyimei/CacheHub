"""Data processing pipeline."""
import logging
from typing import List, Dict, Any
from .schema_validator import SchemaValidator

logger = logging.getLogger(__name__)


class DataProcessor:
    def __init__(self, validator: SchemaValidator):
        self.validator = validator
        self.processed_count = 0
        self.failed_count = 0

    def process_batch(self, records: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Process a batch of records. Returns successfully processed records."""
        results = []
        for record in records:
            try:
                processed = self.process_single(record)
                results.append(processed)
                self.processed_count += 1
            except Exception as e:
                # BUG: No data validation step — invalid data passes through unchecked
                logger.error("Failed to process record: %s", e)
                self.failed_count += 1
        return results

    def process_single(self, record: Dict[str, Any]) -> Dict[str, Any]:
        """Process a single record."""
        # BUG: Should validate input data before processing
        result = {
            **record,
            'processed_at': __import__('datetime').datetime.utcnow().isoformat(),
            'status': 'processed',
        }
        return result

    def get_stats(self) -> Dict[str, int]:
        return {
            'processed': self.processed_count,
            'failed': self.failed_count,
        }
