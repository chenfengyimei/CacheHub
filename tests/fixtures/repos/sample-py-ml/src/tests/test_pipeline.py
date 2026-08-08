"""Tests for data pipeline."""
import pytest
from src.pipeline.data_processor import DataProcessor
from src.validators.schema_validator import SchemaValidator


class TestDataProcessor:
    def setup_method(self):
        self.validator = SchemaValidator(["id", "timestamp", "data"])
        self.processor = DataProcessor(self.validator)

    def test_process_single(self):
        record = {"id": 1, "timestamp": "2024-01-01", "data": "test"}
        result = self.processor.process_single(record)
        assert result["status"] == "processed"
        assert "processed_at" in result

    def test_process_batch(self):
        records = [
            {"id": 1, "timestamp": "2024-01-01", "data": "a"},
            {"id": 2, "timestamp": "2024-01-02", "data": "b"},
        ]
        results = self.processor.process_batch(records)
        assert len(results) == 2

    def test_get_stats(self):
        records = [{"id": 1, "timestamp": "2024-01-01", "data": "a"}]
        self.processor.process_batch(records)
        stats = self.processor.get_stats()
        assert stats["processed"] == 1
        assert stats["failed"] == 0
